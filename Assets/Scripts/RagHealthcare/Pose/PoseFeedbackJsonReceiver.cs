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

        public bool ReceiveFeedback(PoseFeedbackMessage feedback)
        {
            if (feedback == null || string.IsNullOrWhiteSpace(feedback.text))
            {
                return false;
            }

            if (IsRetiredExcessiveDepthFeedback(feedback))
            {
                LastVoiceStatusMessage =
                    "깊은 스쿼트는 올바른 자세로 처리되어 이전 깊이 경고를 읽지 않았습니다.";
                return false;
            }

            if (feedback.confidence < minimumConfidence)
            {
                return false;
            }

            if (IsDuplicateCoolingDown(feedback))
            {
                return false;
            }

            LatestFeedback = feedback;
            LatestFeedbackText = feedback.text;
            LatestFeedbackTime = Time.unscaledTime;

            FeedbackAccepted?.Invoke(feedback);

            if (!VoiceEnabled)
            {
                LastVoiceStatusMessage =
                    "음성 코칭이 꺼져 있어 자세 피드백을 읽지 않았습니다.";
                return false;
            }

            var resolvedCoachTts = ResolveCoachTts();
            if (resolvedCoachTts == null)
            {
                LastVoiceStatusMessage = "음성 코칭 컨트롤러가 연결되지 않아 자세 피드백을 읽을 수 없습니다.";
                if (!missingCoachTtsWarningLogged)
                {
                    missingCoachTtsWarningLogged = true;
                    Debug.LogWarning($"[PoseFeedbackJsonReceiver] {LastVoiceStatusMessage}");
                }

                return false;
            }

            if (!resolvedCoachTts.TrySpeakPoseFeedback(
                    feedback,
                    out var statusMessage,
                    out var requestScheduled))
            {
                LastVoiceStatusMessage = statusMessage;
                if (!string.IsNullOrWhiteSpace(statusMessage))
                {
                    Debug.LogWarning(
                        $"[PoseFeedbackJsonReceiver] TTS request was rejected " +
                        $"({resolvedCoachTts.ActiveBackend}): {statusMessage}");
                }

                return false;
            }

            LastVoiceStatusMessage = statusMessage;
            if (!requestScheduled)
            {
                return false;
            }

            MarkSpoken(feedback);
            return true;
        }

        public bool CancelPendingFeedback(string semanticId)
        {
            var resolvedCoachTts = ResolveCoachTts();
            if (resolvedCoachTts == null)
            {
                return false;
            }

            var cancelled =
                resolvedCoachTts.CancelPendingPoseFeedback(semanticId);
            if (cancelled)
            {
                LastVoiceStatusMessage =
                    resolvedCoachTts.LastStatusMessage;
            }

            return cancelled;
        }

        public bool CancelPendingFeedbackPrefix(
            string semanticIdPrefix)
        {
            var resolvedCoachTts = ResolveCoachTts();
            if (resolvedCoachTts == null)
            {
                return false;
            }

            var cancelled =
                resolvedCoachTts.CancelPendingPoseFeedbackPrefix(
                    semanticIdPrefix);
            if (cancelled)
            {
                LastVoiceStatusMessage =
                    resolvedCoachTts.LastStatusMessage;
            }

            return cancelled;
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

            return false;
        }

        private void MarkSpoken(PoseFeedbackMessage feedback)
        {
            var key = string.IsNullOrWhiteSpace(feedback.id)
                ? feedback.text
                : feedback.id;
            lastSpokenTimes[key] = Time.unscaledTime;
        }

        private static bool IsRetiredExcessiveDepthFeedback(
            PoseFeedbackMessage feedback)
        {
            var id = feedback.id ?? string.Empty;
            var text = feedback.text ?? string.Empty;
            return id == "squat_depth_excessive" ||
                   id == "squat_depth_deep" ||
                   id.EndsWith(
                       "_knee_bend_deep",
                       StringComparison.Ordinal) ||
                   text.IndexOf(
                       "너무 깊게",
                       StringComparison.Ordinal) >= 0 ||
                   text.IndexOf(
                       "깊이를 조금 줄",
                       StringComparison.Ordinal) >= 0 ||
                   text.IndexOf(
                       "knee bend is too deep",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
