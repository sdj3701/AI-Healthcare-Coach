using System;
using Rag.Healthcare.Pose;
using UnityEngine;

namespace Rag.Healthcare.Feedback
{
    [Serializable]
    public sealed class FeedbackChannelSettings
    {
        public bool voiceEnabled = true;
        public bool textEnabled = true;
        public bool soundEnabled;
        public bool vibrationEnabled;
    }

    public sealed class FeedbackAccessibilityController : MonoBehaviour
    {
        [SerializeField] private PoseFeedbackJsonReceiver receiver;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip warningClip;
        [SerializeField] private FeedbackChannelSettings settings = new FeedbackChannelSettings();

        public event Action<string> TextFeedback;
        public FeedbackChannelSettings Settings => settings;

        private void Awake()
        {
            receiver ??= FindFirstObjectByType<PoseFeedbackJsonReceiver>();
            audioSource ??= GetComponent<AudioSource>();
            var saved = PlayerPrefs.GetString("ahc.feedback.channels.v1", string.Empty);
            if (!string.IsNullOrWhiteSpace(saved)) JsonUtility.FromJsonOverwrite(saved, settings);
            if (receiver != null) receiver.VoiceEnabled = settings.voiceEnabled;
        }

        private void OnEnable()
        {
            if (receiver != null) receiver.FeedbackAccepted += HandleFeedback;
        }

        private void OnDisable()
        {
            if (receiver != null) receiver.FeedbackAccepted -= HandleFeedback;
        }

        public void Configure(bool voice, bool text, bool sound, bool vibration)
        {
            settings.voiceEnabled = voice;
            settings.textEnabled = text;
            settings.soundEnabled = sound;
            settings.vibrationEnabled = vibration;
            if (receiver != null) receiver.VoiceEnabled = voice;
            PlayerPrefs.SetString("ahc.feedback.channels.v1", JsonUtility.ToJson(settings));
        }

        private void HandleFeedback(PoseFeedbackMessage feedback)
        {
            if (settings.textEnabled) TextFeedback?.Invoke(feedback.text);
            if (settings.soundEnabled && audioSource != null && warningClip != null) audioSource.PlayOneShot(warningClip);
            if (settings.vibrationEnabled && feedback.severity != FeedbackSeverity.Info) Handheld.Vibrate();
        }
    }

    public sealed class SafetyPauseMonitor : MonoBehaviour
    {
        [SerializeField] private PoseFeedbackJsonReceiver receiver;
        [SerializeField, Min(1f)] private float persistenceSeconds = 4f;
        [SerializeField, Min(1)] private int warningCountThreshold = 3;

        private string activeRule;
        private float firstSeenAt;
        private int count;

        public event Action<PoseFeedbackMessage> PauseRecommended;

        private void Awake() => receiver ??= FindFirstObjectByType<PoseFeedbackJsonReceiver>();
        private void OnEnable() { if (receiver != null) receiver.FeedbackAccepted += Observe; }
        private void OnDisable() { if (receiver != null) receiver.FeedbackAccepted -= Observe; }

        private void Observe(PoseFeedbackMessage feedback)
        {
            if (feedback == null || feedback.severity == FeedbackSeverity.Info) return;
            var now = Time.unscaledTime;
            if (!string.Equals(activeRule, feedback.id, StringComparison.Ordinal) || now - firstSeenAt > persistenceSeconds * 2f)
            {
                activeRule = feedback.id;
                firstSeenAt = now;
                count = 0;
            }
            count++;
            if (feedback.severity == FeedbackSeverity.Critical ||
                (count >= warningCountThreshold && now - firstSeenAt >= persistenceSeconds))
            {
                PauseRecommended?.Invoke(new PoseFeedbackMessage
                {
                    id = "safety_pause_recommended",
                    text = "같은 위험 자세가 계속 감지됩니다. 잠시 멈추고 자세와 컨디션을 확인하세요.",
                    joint = feedback.joint,
                    confidence = feedback.confidence,
                    severity = FeedbackSeverity.Critical
                });
                count = 0;
                firstSeenAt = now;
            }
        }
    }
}
