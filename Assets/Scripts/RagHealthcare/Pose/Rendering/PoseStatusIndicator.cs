using System;
using Rag.Healthcare.Pose.Providers;
using UnityEngine;

namespace Rag.Healthcare.Pose.Rendering
{
    public enum PoseStatusIcon { Searching, Ready, AdjustCamera, Warning, Pause }

    [Serializable]
    public sealed class PoseStatusPresentation
    {
        public PoseStatusIcon icon;
        public string label;
        public Color color;
        public bool emphasizeScreenBorder;
    }

    public sealed class PoseStatusIndicator : MonoBehaviour
    {
        [SerializeField] private PoseProviderHealthMonitor healthMonitor;
        [SerializeField] private PoseFeedbackJsonReceiver feedbackReceiver;

        public event Action<PoseStatusPresentation> Changed;
        public PoseStatusPresentation Current { get; private set; }

        private void Awake()
        {
            healthMonitor ??= FindFirstObjectByType<PoseProviderHealthMonitor>();
            feedbackReceiver ??= FindFirstObjectByType<PoseFeedbackJsonReceiver>();
        }

        private void OnEnable()
        {
            if (healthMonitor != null) healthMonitor.HealthChanged += HandleHealth;
            if (feedbackReceiver != null) feedbackReceiver.FeedbackAccepted += HandleFeedback;
        }

        private void OnDisable()
        {
            if (healthMonitor != null) healthMonitor.HealthChanged -= HandleHealth;
            if (feedbackReceiver != null) feedbackReceiver.FeedbackAccepted -= HandleFeedback;
        }

        private void HandleHealth(PoseProviderTelemetry telemetry)
        {
            switch (telemetry.health)
            {
                case PoseProviderHealth.Healthy:
                    Publish(PoseStatusIcon.Ready, "자세 인식 준비", new Color(0.15f, 0.8f, 0.45f), false);
                    break;
                case PoseProviderHealth.Degraded:
                    Publish(PoseStatusIcon.AdjustCamera, "카메라 위치를 조정하세요", new Color(1f, 0.7f, 0.1f), true);
                    break;
                case PoseProviderHealth.Unavailable:
                    Publish(PoseStatusIcon.Warning, "자세를 인식할 수 없습니다", new Color(0.95f, 0.25f, 0.2f), true);
                    break;
                default:
                    Publish(PoseStatusIcon.Searching, "자세 찾는 중", new Color(0.35f, 0.65f, 1f), false);
                    break;
            }
        }

        private void HandleFeedback(PoseFeedbackMessage feedback)
        {
            if (feedback.severity == FeedbackSeverity.Critical)
                Publish(PoseStatusIcon.Pause, feedback.text, new Color(0.95f, 0.15f, 0.15f), true);
            else if (feedback.severity == FeedbackSeverity.Warning)
                Publish(PoseStatusIcon.Warning, feedback.text, new Color(1f, 0.55f, 0.1f), true);
        }

        private void Publish(PoseStatusIcon icon, string label, Color color, bool emphasize)
        {
            Current = new PoseStatusPresentation { icon = icon, label = label, color = color, emphasizeScreenBorder = emphasize };
            Changed?.Invoke(Current);
        }
    }
}
