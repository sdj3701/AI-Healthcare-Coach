using System;
using System.Collections;
using System.Collections.Generic;
using Rag.Healthcare.Camera;
using Rag.Healthcare.Pose.Analysis;
using Rag.Healthcare.Pose.Providers;
using UnityEngine;

namespace Rag.Healthcare.Pose
{
    public sealed class JointTrackingController : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private CameraCaptureSource cameraSource;
        [SerializeField] private PoseFeedbackJsonReceiver feedbackReceiver;
        [SerializeField] private PoseFeedbackAnalyzer feedbackAnalyzer;

        [Header("Tracking Provider")]
        [SerializeField] private PoseTrackingBackend backend = PoseTrackingBackend.LocalMediaPipe;
        [SerializeField] private PoseTrackingProvider trackingProvider;

        [Header("Tracking")]
        [SerializeField] private bool autoStartTracking;
        [SerializeField, Min(0.01f)] private float requestIntervalSeconds = 1f / 15f;
        [SerializeField, Min(0f)] private float failureLogCooldownSeconds = 1f;
        [SerializeField, Min(1f)] private float cameraStartupTimeoutSeconds = 10f;

        private readonly List<PoseFeedbackMessage> generatedFeedback = new List<PoseFeedbackMessage>();
        private Coroutine startCoroutine;
        private Coroutine trackingCoroutine;
        private Coroutine singleFrameCoroutine;
        private bool isTracking;
        private bool trackingRequested;
        private bool isRequestInFlight;
        private bool resumeTrackingAfterPause;
        private float nextSampleAt;
        private float poseFpsWindowStartedAt;
        private int poseFramesInWindow;
        private float lastFailureLogAt = -999f;
        private string lastLoggedFailure = string.Empty;
        private int lastSampledCameraTextureId;
        private uint lastSampledCameraUpdateCount;
        private int trackingEpoch;

        public event Action<JointTrackingFrame> TrackingFrameReceived;
        public event Action<string> TrackingFailed;

        public JointTrackingFrame LatestFrame { get; private set; }
        public bool IsTracking => isTracking;
        public bool IsStartRequested => trackingRequested;
        public bool IsRequestInFlight => isRequestInFlight;
        public bool IsStopping { get; private set; }
        public bool IsIdle => !isTracking &&
                              !isRequestInFlight &&
                              startCoroutine == null &&
                              trackingCoroutine == null &&
                              singleFrameCoroutine == null;
        public PoseTrackingBackend Backend => backend;
        public PoseTrackingProvider TrackingProvider => trackingProvider;
        public float PoseFps { get; private set; }
        public float LastInferenceMilliseconds { get; private set; }
        public int DroppedFrameCount { get; private set; }
        public int SuccessfulFrameCount { get; private set; }
        public int FailedFrameCount { get; private set; }
        public string LastTrackingError { get; private set; } = string.Empty;

        private void Awake()
        {
            cameraSource ??= FindFirstObjectByType<CameraCaptureSource>();
            feedbackReceiver ??= FindFirstObjectByType<PoseFeedbackJsonReceiver>();
            feedbackAnalyzer ??= FindFirstObjectByType<PoseFeedbackAnalyzer>();
        }

        private void OnEnable()
        {
            if (autoStartTracking)
            {
                StartTracking();
            }
        }

        private void OnDisable()
        {
            StopTracking();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                var shouldResume = trackingRequested || isTracking;
                StopTracking();
                resumeTrackingAfterPause = shouldResume;
                return;
            }

            if (!resumeTrackingAfterPause || !isActiveAndEnabled)
            {
                return;
            }

            resumeTrackingAfterPause = false;
            StartTracking();
        }

        private void OnDestroy()
        {
            trackingRequested = false;
            trackingProvider?.Dispose();
        }

        public void StartTracking()
        {
            var wasRequested = trackingRequested;
            trackingRequested = true;

            if (isTracking || startCoroutine != null || trackingCoroutine != null)
            {
                // A Start received while the previous tracking loop is draining is
                // intentionally retained. TrackingLoop starts the new epoch after its
                // physical native request has finished.
                if (!wasRequested)
                {
                    AdvanceTrackingEpoch();
                }

                return;
            }

            if (!wasRequested)
            {
                AdvanceTrackingEpoch();
            }

            IsStopping = false;
            startCoroutine = StartCoroutine(StartTrackingRoutine());
        }

        public void StopTracking()
        {
            resumeTrackingAfterPause = false;
            trackingRequested = false;
            isTracking = false;
            AdvanceTrackingEpoch();
            IsStopping = startCoroutine != null ||
                         trackingCoroutine != null ||
                         singleFrameCoroutine != null ||
                         isRequestInFlight;
            trackingProvider?.CancelPendingEstimate();

            if (startCoroutine != null)
            {
                StopCoroutine(startCoroutine);
                startCoroutine = null;
            }

            if (singleFrameCoroutine != null && !isRequestInFlight)
            {
                StopCoroutine(singleFrameCoroutine);
                singleFrameCoroutine = null;
            }

            // Keep the provider warm between ordinary Stop/Start operations. Repeatedly
            // destroying the native PoseLandmarker while the camera session is settling
            // is both expensive and unsafe on iOS. The provider is disposed in OnDestroy.
            LatestFrame = null;
            LastInferenceMilliseconds = 0f;
            RefreshStoppingState();
        }

        public void RequestSingleTrackingFrame()
        {
            if (!isActiveAndEnabled || isRequestInFlight || singleFrameCoroutine != null)
            {
                return;
            }

            singleFrameCoroutine = StartCoroutine(RequestSingleTrackingFrameRoutine());
        }

        public void ConfigureSamplingRate(float targetFps)
        {
            requestIntervalSeconds = 1f / Mathf.Clamp(targetFps, 1f, 60f);
        }

        private IEnumerator StartTrackingRoutine()
        {
            // StartCoroutine advances an iterator immediately. Yield once so the
            // returned handle is assigned before any startup path can fail or exit.
            yield return null;

            var trackingLoopStarted = false;
            try
            {
                if (!trackingRequested)
                {
                    yield break;
                }

                if (!PrepareCameraAndProvider())
                {
                    trackingRequested = false;
                    yield break;
                }

                yield return WaitForCameraReady();
                if (!IsCameraReady())
                {
                    NotifyFailure(BuildCameraFailureMessage());
                    trackingRequested = false;
                    yield break;
                }

                if (trackingProvider.NeedsReinitialize)
                {
                    yield return trackingProvider.Initialize();
                }

                if (!trackingProvider.IsReady)
                {
                    NotifyFailure(BuildProviderFailureMessage());
                    trackingRequested = false;
                    yield break;
                }

                if (!trackingRequested)
                {
                    yield break;
                }

                isTracking = true;
                ResetRuntimeMetrics();
                trackingCoroutine = StartCoroutine(TrackingLoop());
                trackingLoopStarted = trackingCoroutine != null;
            }
            finally
            {
                startCoroutine = null;
                if (!trackingLoopStarted)
                {
                    isTracking = false;
                }

                RefreshStoppingState();
            }
        }

        private IEnumerator RequestSingleTrackingFrameRoutine()
        {
            // Match the Start routine's handle-assignment guard. A synchronous
            // camera/provider failure must not leave a completed Coroutine handle
            // stored in singleFrameCoroutine forever.
            yield return null;

            try
            {
                if (!PrepareCameraAndProvider())
                {
                    yield break;
                }

                yield return WaitForCameraReady();
                if (!IsCameraReady())
                {
                    NotifyFailure(BuildCameraFailureMessage());
                    yield break;
                }

                if (trackingProvider.NeedsReinitialize)
                {
                    yield return trackingProvider.Initialize();
                }

                if (!trackingProvider.IsReady)
                {
                    NotifyFailure(BuildProviderFailureMessage());
                    yield break;
                }

                yield return EstimateCurrentFrame();
            }
            finally
            {
                singleFrameCoroutine = null;
                RefreshStoppingState();
            }
        }

        private IEnumerator TrackingLoop()
        {
            // Make sure trackingCoroutine receives its handle before frame access or
            // provider code can throw during the first pass through the loop.
            yield return null;
            nextSampleAt = Time.unscaledTime;

            try
            {
                while (isTracking)
                {
                    var interval = Mathf.Max(0.01f, requestIntervalSeconds);
                    var now = Time.unscaledTime;
                    if (now < nextSampleAt)
                    {
                        yield return null;
                        continue;
                    }

                    // Texture.updateCount identifies whether the camera changed since the
                    // previous sample, even when it did not change in this exact Unity frame.
                    if (!TryReserveFreshCameraFrame())
                    {
                        yield return null;
                        continue;
                    }

                    var skippedSamples = Mathf.FloorToInt(Mathf.Max(0f, now - nextSampleAt) / interval);
                    if (skippedSamples > 0)
                    {
                        DroppedFrameCount += skippedSamples;
                    }

                    nextSampleAt = now + interval;
                    yield return EstimateCurrentFrame();
                }
            }
            finally
            {
                trackingCoroutine = null;
                isTracking = false;
                isRequestInFlight = false;
                RefreshStoppingState();
            }

            if (trackingRequested && isActiveAndEnabled)
            {
                StartTracking();
            }
        }

        private bool TryReserveFreshCameraFrame()
        {
            var webCamTexture = cameraSource == null ? null : cameraSource.WebCamTexture;
            if (webCamTexture == null)
            {
                return true;
            }

            var textureId = webCamTexture.GetInstanceID();
            var textureChanged = textureId != lastSampledCameraTextureId;
            var didUpdateThisFrame = webCamTexture.didUpdateThisFrame;
            var updateCount = webCamTexture.updateCount;
            var updateCountChanged = updateCount != lastSampledCameraUpdateCount;

            // didUpdateThisFrame is the authoritative signal on iOS, where
            // updateCount can remain unchanged even while AVFoundation delivers
            // frames. updateCount remains a fallback so a frame that arrived between
            // pose sampling ticks can still be consumed on other platforms.
            if (!textureChanged && !didUpdateThisFrame && !updateCountChanged)
            {
                return false;
            }

            lastSampledCameraTextureId = textureId;
            lastSampledCameraUpdateCount = updateCount;
            return true;
        }

        private IEnumerator EstimateCurrentFrame()
        {
            if (isRequestInFlight || trackingProvider == null)
            {
                DroppedFrameCount++;
                yield break;
            }

            if (cameraSource == null || !cameraSource.HasValidFrame || cameraSource.PreviewTexture == null)
            {
                yield break;
            }

            isRequestInFlight = true;

            JointTrackingFrame frame = null;
            string error = null;
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var startedAt = Time.realtimeSinceStartup;
            var estimateEpoch = trackingEpoch;

            try
            {
                yield return trackingProvider.EstimatePose(
                    cameraSource.PreviewTexture,
                    timestamp,
                    value => frame = value,
                    message => error = message);
            }
            finally
            {
                isRequestInFlight = false;
                LastInferenceMilliseconds = (Time.realtimeSinceStartup - startedAt) * 1000f;
            }

            // Stop and a queued Start may both happen while the native request is
            // draining. Never let that previous session's frame or cancellation
            // error leak into the newly requested session.
            if (estimateEpoch != trackingEpoch)
            {
                yield break;
            }

            // StopTracking cancels an in-flight native request with PROCESS_CANCELLED.
            // That is an expected lifecycle result, not a failed tracking frame. A
            // caller-requested single frame remains reportable because its coroutine
            // stays assigned until this method returns.
            if (!trackingRequested && !isTracking && singleFrameCoroutine == null)
            {
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                FailedFrameCount++;
                LastTrackingError = error;
                NotifyFailure(error);
                yield break;
            }

            if (frame == null)
            {
                FailedFrameCount++;
                LastTrackingError = "Pose landmarks were not detected.";
                NotifyFailure("Pose landmarks were not detected.");
                yield break;
            }

            ReceiveTrackingFrame(frame, timestamp);
        }

        private bool PrepareCameraAndProvider()
        {
            if (cameraSource == null)
            {
                NotifyFailure("Camera source is missing.");
                return false;
            }

            if (!cameraSource.IsRunning && !cameraSource.StartCamera())
            {
                NotifyFailure("Camera could not be started.");
                return false;
            }

            trackingProvider = ResolveProvider();
            if (trackingProvider == null)
            {
                NotifyFailure("Pose tracking provider is missing.");
                return false;
            }

            return true;
        }

        private IEnumerator WaitForCameraReady()
        {
            var deadline = Time.realtimeSinceStartup + Mathf.Max(1f, cameraStartupTimeoutSeconds);
            while (cameraSource != null && Time.realtimeSinceStartup < deadline)
            {
                if (cameraSource.HasValidFrame)
                {
                    yield break;
                }

                if (!cameraSource.IsRunning &&
                    !cameraSource.IsStarting &&
                    !string.IsNullOrWhiteSpace(cameraSource.LastError))
                {
                    yield break;
                }

                yield return null;
            }
        }

        private bool IsCameraReady()
        {
            return cameraSource != null && cameraSource.HasValidFrame;
        }

        private string BuildCameraFailureMessage()
        {
            if (cameraSource != null && !string.IsNullOrWhiteSpace(cameraSource.LastError))
            {
                return cameraSource.LastError;
            }

            return "Camera did not provide a valid frame within " +
                   Mathf.Max(1f, cameraStartupTimeoutSeconds).ToString("0.#") + " seconds.";
        }

        private PoseTrackingProvider ResolveProvider()
        {
            if (trackingProvider != null)
            {
                return trackingProvider;
            }

            var providers = GetComponents<PoseTrackingProvider>();
            foreach (var provider in providers)
            {
                if (provider.Backend == backend)
                {
                    return provider;
                }
            }

            return backend switch
            {
                PoseTrackingBackend.LocalMediaPipe => gameObject.AddComponent<MediaPipePoseTrackingProvider>(),
                PoseTrackingBackend.LocalSentisMoveNet => gameObject.AddComponent<SentisMoveNetPoseTrackingProvider>(),
                PoseTrackingBackend.RemoteApi => gameObject.AddComponent<RemoteApiPoseTrackingProvider>(),
                PoseTrackingBackend.Disabled => gameObject.AddComponent<NullPoseTrackingProvider>(),
                _ => null
            };
        }

        private void ReceiveTrackingFrame(JointTrackingFrame frame, long fallbackTimestamp)
        {
            if (frame.timestampUnixMilliseconds <= 0)
            {
                frame.timestampUnixMilliseconds = fallbackTimestamp;
            }

            LatestFrame = frame;
            SuccessfulFrameCount++;
            LastTrackingError = string.Empty;
            CountPoseFrame();
            TrackingFrameReceived?.Invoke(frame);
            ForwardFeedback(frame.feedback);
            AnalyzeAndForwardFeedback(frame);
        }

        private void AnalyzeAndForwardFeedback(JointTrackingFrame frame)
        {
            if (feedbackAnalyzer == null)
            {
                return;
            }

            generatedFeedback.Clear();
            feedbackAnalyzer.Analyze(frame, generatedFeedback);
            ForwardFeedback(generatedFeedback);
        }

        private void ForwardFeedback(IReadOnlyList<PoseFeedbackMessage> feedbackMessages)
        {
            if (feedbackReceiver == null || feedbackMessages == null)
            {
                return;
            }

            foreach (var feedback in feedbackMessages)
            {
                feedbackReceiver.ReceiveFeedback(feedback);
            }
        }

        private string BuildProviderFailureMessage()
        {
            if (trackingProvider == null)
            {
                return "Pose tracking provider is missing.";
            }

            if (!string.IsNullOrWhiteSpace(trackingProvider.LastError))
            {
                return trackingProvider.LastError;
            }

            return $"Pose tracking provider '{trackingProvider.GetType().Name}' is not ready.";
        }

        private void NotifyFailure(string message)
        {
            LastTrackingError = message ?? string.Empty;
            if (ShouldLogFailure(message))
            {
                Debug.LogWarning("[JointTrackingController] " + message);
            }

            TrackingFailed?.Invoke(message);
        }

        private void ResetRuntimeMetrics()
        {
            PoseFps = 0f;
            LastInferenceMilliseconds = 0f;
            DroppedFrameCount = 0;
            SuccessfulFrameCount = 0;
            FailedFrameCount = 0;
            LastTrackingError = string.Empty;
            poseFramesInWindow = 0;
            poseFpsWindowStartedAt = Time.unscaledTime;
        }

        private void CountPoseFrame()
        {
            poseFramesInWindow++;
            var elapsed = Time.unscaledTime - poseFpsWindowStartedAt;
            if (elapsed < 1f)
            {
                return;
            }

            PoseFps = poseFramesInWindow / elapsed;
            poseFramesInWindow = 0;
            poseFpsWindowStartedAt = Time.unscaledTime;
        }

        private bool ShouldLogFailure(string message)
        {
            var now = Time.unscaledTime;
            if (!string.Equals(lastLoggedFailure, message, StringComparison.Ordinal) ||
                now - lastFailureLogAt >= failureLogCooldownSeconds)
            {
                lastLoggedFailure = message;
                lastFailureLogAt = now;
                return true;
            }

            return false;
        }

        private void AdvanceTrackingEpoch()
        {
            unchecked
            {
                trackingEpoch++;
                if (trackingEpoch == 0)
                {
                    trackingEpoch = 1;
                }
            }
        }

        private void RefreshStoppingState()
        {
            if (startCoroutine == null &&
                trackingCoroutine == null &&
                singleFrameCoroutine == null &&
                !isRequestInFlight)
            {
                IsStopping = false;
            }
        }
    }
}
