using System;
using System.IO;
using UnityEngine;

namespace Rag.Healthcare.Pose.Providers
{
    public enum PoseProviderHealth { Initializing, Healthy, Degraded, Unavailable }

    [Serializable]
    public sealed class PoseProviderTelemetry
    {
        public string timestampUtc;
        public string backend;
        public PoseProviderHealth health;
        public float poseFps;
        public float inferenceMilliseconds;
        public int successfulFrames;
        public int failedFrames;
        public int droppedFrames;
        public float failureRatio;
        public string lastError;
    }

    public sealed class PoseProviderHealthMonitor : MonoBehaviour
    {
        private const float TelemetryIntervalSeconds = 5f;

        [SerializeField] private JointTrackingController controller;
        [SerializeField, Min(1f)] private float unavailableAfterSeconds = 3f;
        [SerializeField, Range(0f, 1f)] private float degradedFailureRatio = 0.2f;
        [SerializeField, Min(1f)] private float minimumHealthyFps = 8f;
        [SerializeField] private bool writeLocalTelemetry = true;

        private float lastFrameAt;
        private float nextTelemetryAt;

        public event Action<PoseProviderTelemetry> HealthChanged;
        public PoseProviderTelemetry Latest { get; private set; }
        public bool AllowsRuleEvaluation => Latest != null && Latest.health == PoseProviderHealth.Healthy;

        private void Awake()
        {
            controller ??= GetComponent<JointTrackingController>();
        }

        private void OnEnable()
        {
            if (controller == null) return;
            controller.TrackingFrameReceived += HandleFrame;
            lastFrameAt = Time.unscaledTime;
        }

        private void OnDisable()
        {
            if (controller == null) return;
            controller.TrackingFrameReceived -= HandleFrame;
        }

        private void Update()
        {
            TryPublishHealth();
        }

        private void HandleFrame(JointTrackingFrame _)
        {
            lastFrameAt = Time.unscaledTime;
        }

        private void TryPublishHealth()
        {
            if (controller == null)
            {
                return;
            }

            var now = Time.unscaledTime;
            var health = ResolveHealth();
            if (Latest != null && Latest.health == health && now < nextTelemetryAt)
            {
                return;
            }

            Publish(health);
            nextTelemetryAt = now + TelemetryIntervalSeconds;
        }

        private PoseProviderHealth ResolveHealth()
        {
            if (!controller.IsTracking) return PoseProviderHealth.Initializing;
            if (Time.unscaledTime - lastFrameAt >= unavailableAfterSeconds) return PoseProviderHealth.Unavailable;
            var attempts = controller.SuccessfulFrameCount + controller.FailedFrameCount;
            var failureRatio = attempts == 0 ? 0f : (float)controller.FailedFrameCount / attempts;
            if (failureRatio > degradedFailureRatio || (controller.PoseFps > 0f && controller.PoseFps < minimumHealthyFps))
                return PoseProviderHealth.Degraded;
            return PoseProviderHealth.Healthy;
        }

        private void Publish(PoseProviderHealth health)
        {
            var attempts = controller.SuccessfulFrameCount + controller.FailedFrameCount;
            Latest = new PoseProviderTelemetry
            {
                timestampUtc = DateTime.UtcNow.ToString("o"),
                backend = controller.Backend.ToString(),
                health = health,
                poseFps = controller.PoseFps,
                inferenceMilliseconds = controller.LastInferenceMilliseconds,
                successfulFrames = controller.SuccessfulFrameCount,
                failedFrames = controller.FailedFrameCount,
                droppedFrames = controller.DroppedFrameCount,
                failureRatio = attempts == 0 ? 0f : (float)controller.FailedFrameCount / attempts,
                lastError = controller.LastTrackingError
            };
            HealthChanged?.Invoke(Latest);
            if (writeLocalTelemetry) AppendTelemetry(Latest);
        }

        private static void AppendTelemetry(PoseProviderTelemetry telemetry)
        {
            try
            {
                var directory = Path.Combine(Application.persistentDataPath, "pose_sessions", "telemetry");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "provider_health.jsonl"), JsonUtility.ToJson(telemetry) + Environment.NewLine);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[PoseProviderHealthMonitor] Telemetry write failed: " + exception.Message);
            }
        }
    }
}
