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

        private void Awake()
        {
            if (coachTts == null)
            {
                coachTts = FindFirstObjectByType<CoachTtsController>();
            }
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

            coachTts ??= FindFirstObjectByType<CoachTtsController>();
            coachTts?.SpeakPoseFeedback(feedback);
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
