using System;
using Rag.Healthcare.Pose;
using UnityEngine;

namespace Rag.Healthcare.Tts
{
    public sealed class CoachTtsController : MonoBehaviour
    {
        [SerializeField] private TtsBackend backend = TtsBackend.Auto;
        [SerializeField] private bool speakOnStart = true;
        [SerializeField] private string startupMessage = "코치 시스템이 준비되었습니다.";
        [SerializeField, Range(-10, 10)] private int windowsVoiceRate;
        [SerializeField, Range(0, 100)] private int windowsVoiceVolume = 100;
        [SerializeField] private string macOsVoice = string.Empty;
        [SerializeField, Range(80, 320)] private int macOsWordsPerMinute = 185;

        [Header("Request Scheduling")]
        [SerializeField] private bool requestSchedulingEnabled = true;
        [SerializeField, Min(0f)] private float duplicateCooldownSeconds = 2f;
        [SerializeField, Min(0.1f)] private float infoTtlSeconds = 1.5f;
        [SerializeField, Min(0.1f)] private float warningTtlSeconds = 3f;
        [SerializeField, Min(0.1f)] private float criticalTtlSeconds = 6f;
        [SerializeField, Min(0.01f)] private float backendPollIntervalSeconds = 0.05f;
        [SerializeField, Min(0f)] private float startObservationGraceSeconds = 0.5f;
        [SerializeField, Min(0.1f)] private float stopTimeoutSeconds = 0.75f;
        [SerializeField, Min(0.5f)] private float nativeStartTimeoutSeconds = 2f;

        private ITtsService ttsService;
        private TtsRequestScheduler scheduler;
        private int generation;
        private bool acceptingRequests;
        private bool backendHealthy = true;
        private bool sessionOpen;
        private bool resumeWhenBackendIdle;
        private bool resumeSessionAfterPause;
        private bool destroyed;
        private double nextBackendPollAt;
        private long activeNativeRequestId;
        private long activeNativeGeneration;
        private bool activeNativeRequestTerminal;
        private bool activeNativeRequestStarted;
        private double activeNativeRequestAcceptedAt;
        private long lastDroppedEventCount;

        public event Action<string> PlaybackFailed;

        public bool IsSpeaking => SafeIsSpeaking();
        public bool HasPendingRequest => scheduler != null && scheduler.HasPending;
        public bool IsAdmissionOpen => sessionOpen && acceptingRequests && backendHealthy && !destroyed;
        public bool IsBackendHealthy => backendHealthy;
        public int SchedulingGeneration => generation;
        public TtsBackend ActiveBackend { get; private set; } = TtsBackend.LogOnly;
        public string LastStatusMessage { get; private set; } = string.Empty;

        private void Awake()
        {
            EnsureScheduler();
            ttsService = CreateTtsService();
            Debug.Log($"[TTS] Active backend: {ActiveBackend}");
        }

        private void OnEnable()
        {
            BeginSession();
        }

        private void Start()
        {
            if (speakOnStart)
            {
                if (!TrySchedule(
                        startupMessage,
                        "coach_startup",
                        TtsRequestPriority.Info,
                        infoTtlSeconds,
                        out var statusMessage) &&
                    !string.IsNullOrWhiteSpace(statusMessage))
                {
                    Debug.LogWarning(statusMessage);
                }
            }
        }

        private void Update()
        {
            if (resumeWhenBackendIdle)
            {
                var resumePollAt = (double)Time.unscaledTime;
                if (resumePollAt < nextBackendPollAt)
                {
                    return;
                }

                nextBackendPollAt = resumePollAt + Math.Max(0.01d, backendPollIntervalSeconds);
                DrainBackendEvents(false);
                if (!backendHealthy || !resumeWhenBackendIdle)
                {
                    return;
                }

                if (!SafeIsSpeaking())
                {
                    resumeWhenBackendIdle = false;
                    BeginSession();
                }
            }

            if (!requestSchedulingEnabled || !IsAdmissionOpen)
            {
                return;
            }

            if (scheduler != null && scheduler.IsBusy)
            {
                PumpScheduler(false, out _, out _);
                return;
            }

            var now = (double)Time.unscaledTime;
            if (now < nextBackendPollAt)
            {
                return;
            }

            nextBackendPollAt = now + Math.Max(0.01d, backendPollIntervalSeconds);
            DrainBackendEvents(true);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                if (IsAdmissionOpen)
                {
                    resumeSessionAfterPause = true;
                    Suspend();
                }
            }
            else
            {
                var shouldResume = resumeSessionAfterPause;
                resumeSessionAfterPause = false;
                if (shouldResume &&
                    isActiveAndEnabled &&
                    backendHealthy &&
                    !IsAdmissionOpen)
                {
                    Resume();
                }
            }
        }

        private void OnDisable()
        {
            resumeSessionAfterPause = false;
            EndSessionInternal(false);
        }

        private void OnDestroy()
        {
            if (destroyed)
            {
                return;
            }

            destroyed = true;
            EndSessionInternal(false);
            if (ttsService is IDisposable disposable)
            {
                disposable.Dispose();
            }

            ttsService = null;
        }

        public void Speak(string message)
        {
            if (!TrySpeak(message, out var statusMessage) && !string.IsNullOrWhiteSpace(statusMessage))
            {
                Debug.LogWarning(statusMessage);
            }
        }

        public bool TrySpeak(string message, out string statusMessage)
        {
            return TrySchedule(
                message,
                message,
                TtsRequestPriority.Info,
                infoTtlSeconds,
                out statusMessage);
        }

        public bool TrySchedule(
            string message,
            string semanticId,
            TtsRequestPriority priority,
            float ttlSeconds,
            out string statusMessage)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                statusMessage = "TTS로 읽을 문장이 비어 있습니다.";
                LastStatusMessage = statusMessage;
                return false;
            }

            if (!IsAdmissionOpen)
            {
                statusMessage = backendHealthy
                    ? "TTS 컨트롤러가 비활성 상태여서 요청을 받을 수 없습니다."
                    : "TTS backend가 안전하지 않은 상태여서 이번 세션의 음성 안내를 비활성화했습니다.";
                LastStatusMessage = statusMessage;
                return false;
            }

            var trimmedMessage = message.Trim();
            if (!requestSchedulingEnabled)
            {
                return TrySpeakDirect(trimmedMessage, out statusMessage);
            }

            EnsureScheduler();
            var now = (double)Time.unscaledTime;
            var result = scheduler.Enqueue(
                trimmedMessage,
                semanticId,
                priority,
                now,
                Mathf.Max(0f, ttlSeconds),
                generation);

            if (!result.IsScheduled)
            {
                statusMessage = BuildScheduleStatus(result);
                LastStatusMessage = statusMessage;
                return result.IsBenignSuppression;
            }

            // Pose feedback can arrive inside the tracking result callback. Starting the
            // native synthesizer here would extend that same Unity frame, so admission is
            // intentionally cheap and Update pumps the accepted request on a later tick.
            statusMessage = BuildScheduleStatus(result);
            LastStatusMessage = statusMessage;
            return true;
        }

        private bool TrySpeakDirect(string trimmedMessage, out string statusMessage)
        {
            ttsService ??= CreateTtsService();
            try
            {
                if (!ttsService.TrySpeak(trimmedMessage, out var errorMessage))
                {
                    statusMessage = BuildPlaybackFailure(errorMessage);
                    ReportPlaybackFailure(statusMessage);
                    return false;
                }
            }
            catch (Exception exception)
            {
                statusMessage = BuildPlaybackFailure(exception.Message);
                ReportPlaybackFailure(statusMessage);
                return false;
            }

            statusMessage = $"{ActiveBackend} TTS 재생 중";
            LastStatusMessage = statusMessage;
            return true;
        }

        public void SpeakPoseFeedback(PoseFeedbackMessage feedback)
        {
            if (!TrySpeakPoseFeedback(feedback, out var statusMessage) &&
                !string.IsNullOrWhiteSpace(statusMessage))
            {
                Debug.LogWarning($"[TTS] Pose feedback request rejected: {statusMessage}");
            }
        }

        public bool TrySpeakPoseFeedback(
            PoseFeedbackMessage feedback,
            out string statusMessage)
        {
            if (feedback == null)
            {
                statusMessage = "TTS로 읽을 자세 피드백이 없습니다.";
                LastStatusMessage = statusMessage;
                return false;
            }

            var priority = ToPriority(feedback.severity);
            var ttlSeconds = GetTtlSeconds(priority);
            var semanticId = string.IsNullOrWhiteSpace(feedback.id)
                ? feedback.text
                : feedback.id;
            return TrySchedule(
                feedback.text,
                semanticId,
                priority,
                ttlSeconds,
                out statusMessage);
        }

        public void Stop()
        {
            resumeSessionAfterPause = false;
            EndSessionInternal(true);
        }

        public bool BeginSession()
        {
            if (destroyed)
            {
                LastStatusMessage = "TTS 컨트롤러가 종료되어 새 세션을 시작할 수 없습니다.";
                LogSessionAdmissionFailure();
                return false;
            }

            if (IsAdmissionOpen)
            {
                // OnEnable prepares the backend before the workout screen appears.
                // Treat a second BeginSession call as idempotent so an asynchronous
                // native Stop cannot make the workout-start announcement disappear.
                LastStatusMessage = $"{ActiveBackend} TTS 세션 준비 완료";
                return true;
            }

            EnsureScheduler();
            if (SafeIsSpeaking())
            {
                sessionOpen = false;
                acceptingRequests = false;
                resumeWhenBackendIdle = true;
                LastStatusMessage = "TTS backend 종료를 기다린 뒤 새 세션의 음성 안내를 시작합니다.";
                LogSessionAdmissionFailure();
                return false;
            }

            resumeWhenBackendIdle = false;
            backendHealthy = true;
            sessionOpen = true;
            acceptingRequests = true;
            AdvanceGeneration();
            ResetNativeRequestTracking();
            DrainBackendEvents(false);
            if (!backendHealthy)
            {
                if (string.IsNullOrWhiteSpace(LastStatusMessage))
                {
                    LastStatusMessage = "TTS backend가 안전하지 않아 새 세션을 시작할 수 없습니다.";
                }

                LogSessionAdmissionFailure();
                return false;
            }

            if (ttsService is IQueuedTtsService eventSource)
            {
                lastDroppedEventCount = eventSource.DroppedEventCount;
            }

            LastStatusMessage = $"{ActiveBackend} TTS 세션 준비 완료";
            return true;
        }

        public void EndSession()
        {
            resumeSessionAfterPause = false;
            EndSessionInternal(true);
        }

        public void Suspend()
        {
            EndSessionInternal(false);
        }

        public bool Resume()
        {
            return BeginSession();
        }

        [ContextMenu("Test Korean Coaching")]
        private void TestKoreanCoaching()
        {
            Speak("무릎을 발끝 방향으로 맞춰 주세요.");
        }

        private ITtsService CreateTtsService()
        {
            ActiveBackend = ResolveBackend();
            return ActiveBackend switch
            {
                TtsBackend.WindowsPowerShell => new WindowsPowerShellTtsService(windowsVoiceRate, windowsVoiceVolume),
                TtsBackend.MacOsSay => new MacOsSayTtsService(macOsVoice, macOsWordsPerMinute),
                TtsBackend.AndroidNative => new AndroidNativeTtsService(),
                TtsBackend.IosNative => new IosNativeTtsService(),
                _ => new LogTtsService()
            };
        }

        private TtsBackend ResolveBackend()
        {
            if (backend != TtsBackend.Auto)
            {
                return backend;
            }

            return TtsBackendResolver.ResolveAuto(Application.platform);
        }

        private void EnsureScheduler()
        {
            if (scheduler != null)
            {
                return;
            }

            scheduler = new TtsRequestScheduler(new TtsRequestSchedulerOptions
            {
                DuplicateCooldownSeconds = Mathf.Max(0f, duplicateCooldownSeconds),
                StartObservationGraceSeconds = Mathf.Max(0f, startObservationGraceSeconds),
                StopTimeoutSeconds = Mathf.Max(0.1f, stopTimeoutSeconds),
                MinimumPreemptPriority = TtsRequestPriority.Critical,
                MaximumRecentSemanticIds = 128
            });
        }

        private void AdvanceGeneration()
        {
            generation = generation == int.MaxValue ? 1 : generation + 1;
            scheduler?.BeginGeneration(generation);
            nextBackendPollAt = 0d;
        }

        private void EndSessionInternal(bool updateStatus)
        {
            resumeWhenBackendIdle = false;
            if (!sessionOpen)
            {
                acceptingRequests = false;
                if (updateStatus)
                {
                    LastStatusMessage = "TTS 재생을 중지했습니다.";
                }

                return;
            }

            sessionOpen = false;
            acceptingRequests = false;
            CancelScheduledPlayback(updateStatus);
        }

        private void CancelScheduledPlayback(bool updateStatus)
        {
            EnsureScheduler();
            var hadScheduledWork = scheduler.IsBusy;
            var backendWasSpeaking = SafeIsSpeaking();
            AdvanceGeneration();

            if (hadScheduledWork || backendWasSpeaking)
            {
                TryStopBackend();
            }

            ResetNativeRequestTracking();

            if (updateStatus)
            {
                LastStatusMessage = "TTS 재생을 중지했습니다.";
            }
        }

        private bool PumpScheduler(
            bool force,
            out long failedRequestId,
            out string failureMessage)
        {
            failedRequestId = 0;
            failureMessage = string.Empty;
            if (scheduler == null || !scheduler.IsBusy)
            {
                return true;
            }

            var now = (double)Time.unscaledTime;
            if (!force && now < nextBackendPollAt)
            {
                return true;
            }

            nextBackendPollAt = now + Math.Max(0.01d, backendPollIntervalSeconds);
            DrainBackendEvents(true);
            if (!backendHealthy || scheduler == null || !scheduler.IsBusy)
            {
                failedRequestId = scheduler?.Active?.RequestId ?? 0;
                failureMessage = "TTS backend event stream is unhealthy.";
                return false;
            }

            if (activeNativeRequestId > 0 &&
                !activeNativeRequestStarted &&
                !activeNativeRequestTerminal &&
                now - activeNativeRequestAcceptedAt >= Math.Max(0.5d, nativeStartTimeoutSeconds))
            {
                failedRequestId = scheduler.Active?.RequestId ?? 0;
                failureMessage = "Native TTS did not emit a started or terminal event before timeout.";
                QuarantineBackend("iOS TTS가 제한 시간 안에 재생을 시작하지 않아 이번 세션의 음성 안내를 비활성화했습니다.");
                return false;
            }

            var backendIsSpeaking = activeNativeRequestId > 0
                ? !activeNativeRequestTerminal
                : SafeIsSpeaking();
            var action = scheduler.Poll(now, backendIsSpeaking);
            if (action.Type == TtsSchedulerActionType.None || action.Request == null)
            {
                if (!scheduler.IsBusy)
                {
                    ResetNativeRequestTracking();
                }

                return true;
            }

            if (action.Type == TtsSchedulerActionType.QuarantineBackend)
            {
                failedRequestId = action.Request.RequestId;
                failureMessage = "TTS backend did not stop before the safety timeout.";
                QuarantineBackend("TTS backend가 제한 시간 안에 중지되지 않아 이번 세션의 음성 안내를 비활성화했습니다.");
                return false;
            }

            if (action.Type == TtsSchedulerActionType.StopForPreemption)
            {
                TryStopBackend();
                scheduler.AcknowledgeStopIssued(action.Request.RequestId, now);
                LastStatusMessage = "중요한 안내를 재생하기 위해 현재 TTS를 중지하는 중입니다.";
                return true;
            }

            ttsService ??= CreateTtsService();
            try
            {
                if (!TryStartBackendRequest(action.Request, out var nativeRequestId, out var errorMessage))
                {
                    failedRequestId = action.Request.RequestId;
                    failureMessage = errorMessage;
                    scheduler.AcknowledgeStartFailed(action.Request.RequestId, now);
                    ResetNativeRequestTracking();
                    ReportPlaybackFailure(BuildPlaybackFailure(errorMessage));
                    return false;
                }

                if (nativeRequestId > 0)
                {
                    activeNativeRequestId = nativeRequestId;
                    activeNativeRequestTerminal = false;
                    activeNativeRequestStarted = false;
                    activeNativeRequestAcceptedAt = now;
                    activeNativeGeneration = (ttsService as IQueuedTtsService)?.NativeGeneration ?? 0;
                }
                else
                {
                    ResetNativeRequestTracking();
                }
            }
            catch (Exception exception)
            {
                failedRequestId = action.Request.RequestId;
                failureMessage = exception.Message;
                scheduler.AcknowledgeStartFailed(action.Request.RequestId, now);
                ResetNativeRequestTracking();
                ReportPlaybackFailure(BuildPlaybackFailure(exception.Message));
                return false;
            }

            scheduler.AcknowledgeStarted(
                action.Request.RequestId,
                now,
                activeNativeRequestId > 0 || SafeIsSpeaking());
            LastStatusMessage = $"{ActiveBackend} TTS 재생 중";
            return true;
        }

        private bool TryStartBackendRequest(
            TtsScheduledRequest request,
            out long nativeRequestId,
            out string errorMessage)
        {
            nativeRequestId = 0;
            if (ttsService is IQueuedTtsService queuedService)
            {
                // Only the C# active slot is submitted. The C# pending slot never enters
                // the native queue, so both layers cannot build independent backlogs.
                return queuedService.TryEnqueue(
                    request.Text,
                    request.Priority,
                    0,
                    out nativeRequestId,
                    out errorMessage);
            }

            return ttsService.TrySpeak(request.Text, out errorMessage);
        }

        private void DrainBackendEvents(bool enforceHealth)
        {
            if (!(ttsService is IQueuedTtsService eventSource))
            {
                return;
            }

            var droppedEventCount = eventSource.DroppedEventCount;
            if (enforceHealth && droppedEventCount > lastDroppedEventCount)
            {
                lastDroppedEventCount = droppedEventCount;
                QuarantineBackend("iOS TTS 이벤트가 유실되어 이번 세션의 음성 안내를 비활성화했습니다.");
                return;
            }

            if (droppedEventCount < lastDroppedEventCount)
            {
                // Native generation reset can reset its diagnostic counter.
                lastDroppedEventCount = droppedEventCount;
            }

            const int maximumEventsPerDrain = 64;
            for (var i = 0; i < maximumEventsPerDrain; i++)
            {
                if (!eventSource.TryPollEvent(out var backendEvent))
                {
                    break;
                }

                if (backendEvent.Version != 1)
                {
                    eventSource.AcknowledgeEvent(backendEvent.Sequence);
                    QuarantineBackend(
                        $"지원하지 않는 iOS TTS 이벤트 ABI 버전({backendEvent.Version})이 감지되어 " +
                        "이번 세션의 음성 안내를 비활성화했습니다.");
                    return;
                }

                var message = string.Empty;
                if (backendEvent.Type == TtsBackendEventType.Failed)
                {
                    eventSource.TryGetEventMessage(backendEvent.Sequence, out message);
                }

                var acknowledged = eventSource.AcknowledgeEvent(backendEvent.Sequence);
                if (enforceHealth && (!acknowledged || backendEvent.DroppedBefore > 0))
                {
                    QuarantineBackend(!acknowledged
                        ? "iOS TTS 이벤트 확인에 실패하여 이번 세션의 음성 안내를 비활성화했습니다."
                        : "iOS TTS 이벤트 queue overflow가 감지되어 이번 세션의 음성 안내를 비활성화했습니다.");
                    return;
                }

                HandleBackendEvent(backendEvent, message, enforceHealth);
                if (!backendHealthy && enforceHealth)
                {
                    return;
                }
            }

            lastDroppedEventCount = eventSource.DroppedEventCount;
        }

        private void HandleBackendEvent(
            TtsBackendEvent backendEvent,
            string message,
            bool enforceHealth)
        {
            if (enforceHealth && backendEvent.Type == TtsBackendEventType.MediaServicesLost)
            {
                QuarantineBackend("iOS 오디오 서비스가 중단되어 이번 세션의 음성 안내를 비활성화했습니다.");
                return;
            }

            if (backendEvent.RequestId <= 0 ||
                backendEvent.RequestId != activeNativeRequestId ||
                (activeNativeGeneration > 0 && backendEvent.Generation != activeNativeGeneration))
            {
                return;
            }

            if (backendEvent.Type == TtsBackendEventType.Started)
            {
                activeNativeRequestStarted = true;
            }

            if (backendEvent.IsTerminal)
            {
                activeNativeRequestTerminal = true;
            }

            if (backendEvent.Type == TtsBackendEventType.Failed)
            {
                var details = string.IsNullOrWhiteSpace(message)
                    ? $"Native event code {backendEvent.Code}"
                    : message;
                // Publish callbacks only after the terminal state is visible. A listener
                // may synchronously stop or reopen a session from PlaybackFailed.
                ReportPlaybackFailure(BuildPlaybackFailure(details));
            }
        }

        private void QuarantineBackend(string statusMessage)
        {
            if (!backendHealthy)
            {
                return;
            }

            backendHealthy = false;
            sessionOpen = false;
            acceptingRequests = false;
            resumeWhenBackendIdle = false;
            resumeSessionAfterPause = false;
            scheduler?.QuarantineBackend();
            TryStopBackend();
            ResetNativeRequestTracking();
            ReportPlaybackFailure(statusMessage);
        }

        private void ResetNativeRequestTracking()
        {
            activeNativeRequestId = 0;
            activeNativeGeneration = 0;
            activeNativeRequestTerminal = false;
            activeNativeRequestStarted = false;
            activeNativeRequestAcceptedAt = 0d;
        }

        private bool SafeIsSpeaking()
        {
            if (ttsService == null)
            {
                return false;
            }

            try
            {
                return ttsService.IsSpeaking;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[TTS] Could not read backend state: {exception.Message}");
                return false;
            }
        }

        private void TryStopBackend()
        {
            if (ttsService == null)
            {
                return;
            }

            try
            {
                ttsService.Stop();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[TTS] Could not stop backend: {exception.Message}");
            }
        }

        private string BuildPlaybackFailure(string errorMessage)
        {
            return string.IsNullOrWhiteSpace(errorMessage)
                ? $"{ActiveBackend} TTS 재생을 시작하지 못했습니다."
                : $"{ActiveBackend} TTS 재생을 시작하지 못했습니다: {errorMessage}";
        }

        private void ReportPlaybackFailure(string statusMessage)
        {
            LastStatusMessage = statusMessage;
            Debug.LogWarning($"[TTS] Playback failed ({ActiveBackend}): {statusMessage}");
            PlaybackFailed?.Invoke(statusMessage);
        }

        private void LogSessionAdmissionFailure()
        {
            Debug.LogWarning(
                $"[TTS] Could not begin session ({ActiveBackend}): {LastStatusMessage}");
        }

        private string BuildScheduleStatus(TtsEnqueueResult result)
        {
            return result.Disposition switch
            {
                TtsEnqueueDisposition.AcceptedAsActive => $"{ActiveBackend} TTS 재생 중",
                TtsEnqueueDisposition.AcceptedAsPending => "TTS 재생 대기 중",
                TtsEnqueueDisposition.CoalescedPending => "대기 중인 TTS를 최신 안내로 교체했습니다.",
                TtsEnqueueDisposition.AcceptedWithPreemption => "중요한 TTS 안내를 우선 재생하도록 예약했습니다.",
                TtsEnqueueDisposition.SuppressedActiveDuplicate => "같은 TTS 안내가 재생 중이어서 중복 요청을 생략했습니다.",
                TtsEnqueueDisposition.SuppressedCooldownDuplicate => "같은 TTS 안내의 반복 제한 시간 안이어서 요청을 생략했습니다.",
                TtsEnqueueDisposition.DroppedLowerPriority => "더 중요한 TTS 안내가 대기 중이어서 요청을 생략했습니다.",
                TtsEnqueueDisposition.RejectedGeneration => "이전 세션의 TTS 요청을 폐기했습니다.",
                TtsEnqueueDisposition.RejectedBackendUnhealthy => "TTS backend가 격리되어 요청을 폐기했습니다.",
                _ => string.IsNullOrWhiteSpace(result.Reason)
                    ? "TTS 요청을 처리하지 못했습니다."
                    : result.Reason
            };
        }

        private float GetTtlSeconds(TtsRequestPriority priority)
        {
            return priority switch
            {
                TtsRequestPriority.Critical => Mathf.Max(0.1f, criticalTtlSeconds),
                TtsRequestPriority.Warning => Mathf.Max(0.1f, warningTtlSeconds),
                _ => Mathf.Max(0.1f, infoTtlSeconds)
            };
        }

        private static TtsRequestPriority ToPriority(FeedbackSeverity severity)
        {
            return severity switch
            {
                FeedbackSeverity.Critical => TtsRequestPriority.Critical,
                FeedbackSeverity.Warning => TtsRequestPriority.Warning,
                _ => TtsRequestPriority.Info
            };
        }
    }
}
