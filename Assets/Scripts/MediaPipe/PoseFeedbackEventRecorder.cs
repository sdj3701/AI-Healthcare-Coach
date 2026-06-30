using System;
using System.Collections.Generic;
using UnityEngine;

namespace AIHealthcareCoach.MediaPipe
{
    public sealed class PoseFeedbackEventRecorder
    {
        private readonly List<PoseFeedbackEvent> events = new List<PoseFeedbackEvent>();
        private PoseSessionData session;

        public IReadOnlyList<PoseFeedbackEvent> Events
        {
            get { return events; }
        }

        public int Count
        {
            get { return events.Count; }
        }

        public void Begin(PoseSessionData sessionData)
        {
            session = sessionData;
            events.Clear();
        }

        public void Record(
            LandmarkFrame frame,
            PoseQualityReport qualityReport,
            IReadOnlyList<PoseExerciseFeedbackMessage> feedbackMessages)
        {
            if (session == null || feedbackMessages == null || feedbackMessages.Count == 0)
            {
                return;
            }

            var elapsedMs = (long)((Time.realtimeSinceStartup - session.startedAtRealtimeSeconds) * 1000f);
            if (elapsedMs < 0)
            {
                elapsedMs = 0;
            }
            for (var i = 0; i < feedbackMessages.Count; i++)
            {
                var feedback = feedbackMessages[i];
                if (feedback == null)
                {
                    continue;
                }

                events.Add(new PoseFeedbackEvent
                {
                    sessionId = session.sessionId,
                    timestampUtc = DateTime.UtcNow.ToString("o"),
                    tMs = elapsedMs,
                    frameTimestampMs = frame == null ? 0 : frame.timestampMs,
                    ruleId = feedback.id ?? string.Empty,
                    severity = feedback.severity.ToString(),
                    message = feedback.text ?? string.Empty,
                    jointId = feedback.jointId,
                    jointName = feedback.jointName ?? string.Empty,
                    confidence = Mathf.Clamp01(feedback.confidence),
                    qualityState = qualityReport == null ? string.Empty : qualityReport.state.ToString(),
                    cameraFps = frame == null ? 0f : frame.cameraFps,
                    poseFps = frame == null ? 0f : frame.poseFps,
                    rep = 0
                });
            }
        }

        public void Clear()
        {
            events.Clear();
            session = null;
        }
    }
}
