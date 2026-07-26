using System;
using System.Collections.Generic;
using Rag.Healthcare.Tts;
using UnityEngine;

namespace Rag.Healthcare.Pose
{
    public sealed class PoseFeedbackJsonReceiver : MonoBehaviour
    {
        [SerializeField] private CoachTtsController coachTts;
        [SerializeField, Range(0f, 1f)] private float minimumConfidence = 0.5f;
        [SerializeField, Min(0f)] private float duplicateCooldownSeconds = 2f;

        private readonly Dictionary<string, float> lastSpokenTimes = new Dictionary<string, float>();
        private bool missingCoachTtsWarningLogged;

        public event Action<PoseFeedbackMessage> FeedbackAccepted;

        public bool VoiceEnabled { get; set; } = true;
        public CoachTtsController CoachTts => coachTts;

        public PoseFeedbackMessage LatestFeedback { get; private set; }
        public string LatestFeedbackText { get; private set; } = string.Empty;
        public float LatestFeedbackTime { get; private set; } = -1f;
        public string LastVoiceStatusMessage { get; private set; } = string.Empty;

        private void Awake()
        {
            ResolveCoachTts();
        }

        public void ReceiveFeedbackJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var feedback = JsonUtility.FromJson<PoseFeedbackMessage>(json);
            ReceiveFeedback(feedback);
        }

        public void ReceiveFeedback(PoseFeedbackMessage feedback)
        {
            if (feedback == null || string.IsNullOrWhiteSpace(feedback.text))
            {
                return;
            }

            if (feedback.confidence < minimumConfidence)
            {
                return;
            }

            if (IsDuplicateCoolingDown(feedback))
            {
                return;
            }

            LatestFeedback = feedback;
            LatestFeedbackText = feedback.text;
            LatestFeedbackTime = Time.unscaledTime;

            FeedbackAccepted?.Invoke(feedback);

            if (VoiceEnabled)
            {
                var resolvedCoachTts = ResolveCoachTts();
                if (resolvedCoachTts == null)
                {
                    LastVoiceStatusMessage = "음성 코칭 컨트롤러가 연결되지 않아 자세 피드백을 읽을 수 없습니다.";
                    if (!missingCoachTtsWarningLogged)
                    {
                        missingCoachTtsWarningLogged = true;
                        Debug.LogWarning($"[PoseFeedbackJsonReceiver] {LastVoiceStatusMessage}");
                    }

                    return;
                }

                if (!resolvedCoachTts.TrySpeakPoseFeedback(feedback, out var statusMessage))
                {
                    LastVoiceStatusMessage = statusMessage;
                    if (!string.IsNullOrWhiteSpace(statusMessage))
                    {
                        Debug.LogWarning(
                            $"[PoseFeedbackJsonReceiver] TTS request was rejected " +
                            $"({resolvedCoachTts.ActiveBackend}): {statusMessage}");
                    }

                    return;
                }

                LastVoiceStatusMessage = statusMessage;
            }
        }

        private CoachTtsController ResolveCoachTts()
        {
            if (coachTts == null)
            {
                coachTts = FindFirstObjectByType<CoachTtsController>();
            }

            if (coachTts != null)
            {
                missingCoachTtsWarningLogged = false;
            }

            return coachTts;
        }

        private bool IsDuplicateCoolingDown(PoseFeedbackMessage feedback)
        {
            if (duplicateCooldownSeconds <= 0f)
            {
                return false;
            }

            var key = string.IsNullOrWhiteSpace(feedback.id) ? feedback.text : feedback.id;
            var now = Time.unscaledTime;
            if (lastSpokenTimes.TryGetValue(key, out var lastTime) &&
                now - lastTime < duplicateCooldownSeconds)
            {
                return true;
            }

            lastSpokenTimes[key] = now;
            return false;
        }
    }
}
