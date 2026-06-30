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

        private readonly List<PoseFeedbackMessage> generatedFeedback = new List<PoseFeedbackMessage>();
        private Coroutine startCoroutine;
        private Coroutine trackingCoroutine;
        private bool isTracking;
        private bool isRequestInFlight;
        private float nextSampleAt;
        private float poseFpsWindowStartedAt;
        private int poseFramesInWindow;
        private float lastFailureLogAt = -999f;
        private string lastLoggedFailure = string.Empty;

        public event Action<JointTrackingFrame> TrackingFrameReceived;
        public event Action<string> TrackingFailed;

        public JointTrackingFrame LatestFrame { get; private set; }
        public bool IsTracking => isTracking;
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

        private void OnDestroy()
        {
            trackingProvider?.Dispose();
        }

        public void StartTracking()
        {
            if (isTracking || startCoroutine != null)
            {
                return;
            }

            startCoroutine = StartCoroutine(StartTrackingRoutine());
        }

        public void StopTracking()
        {
            isTracking = false;
            isRequestInFlight = false;

            if (startCoroutine != null)
            {
                StopCoroutine(startCoroutine);
                startCoroutine = null;
            }

            if (trackingCoroutine != null)
            {
                StopCoroutine(trackingCoroutine);
                trackingCoroutine = null;
            }

            trackingProvider?.Dispose();
            LastInferenceMilliseconds = 0f;
        }

        public void RequestSingleTrackingFrame()
        {
            if (!isActiveAndEnabled || isRequestInFlight)
            {
                return;
            }

            StartCoroutine(RequestSingleTrackingFrameRoutine());
        }

        private IEnumerator StartTrackingRoutine()
        {
            if (!PrepareCameraAndProvider())
            {
                startCoroutine = null;
                yield break;
            }

            yield return trackingProvider.Initialize();

            if (!trackingProvider.IsReady)
            {
                NotifyFailure(BuildProviderFailureMessage());
                startCoroutine = null;
                yield break;
            }

            isTracking = true;
            ResetRuntimeMetrics();
            trackingCoroutine = StartCoroutine(TrackingLoop());
            startCoroutine = null;
        }

        private IEnumerator RequestSingleTrackingFrameRoutine()
        {
            if (!PrepareCameraAndProvider())
            {
                yield break;
            }

            if (!trackingProvider.IsReady)
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

        private IEnumerator TrackingLoop()
        {
            nextSampleAt = Time.unscaledTime;

            while (isTracking)
            {
                var interval = Mathf.Max(0.01f, requestIntervalSeconds);
                var now = Time.unscaledTime;
                if (now < nextSampleAt)
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

            yield return trackingProvider.EstimatePose(
                cameraSource.PreviewTexture,
                timestamp,
                value => frame = value,
                message => error = message);

            isRequestInFlight = false;
            LastInferenceMilliseconds = (Time.realtimeSinceStartup - startedAt) * 1000f;

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
    }
}
