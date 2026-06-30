using System;
using System.Collections.Generic;
using UnityEngine;

namespace AIHealthcareCoach.MediaPipe
{
    public sealed class PoseSessionSummaryBuilder
    {
        private PoseSessionData session;
        private int sampledFrames;
        private int metricFrameCount;
        private int qualityFrameCount;
        private float cameraFpsTotal;
        private float poseFpsTotal;
        private float inferenceMsTotal;
        private float visibilityTotal;
        private float presenceTotal;

        public void Begin(PoseSessionData sessionData)
        {
            session = sessionData;
            sampledFrames = 0;
            metricFrameCount = 0;
            qualityFrameCount = 0;
            cameraFpsTotal = 0f;
            poseFpsTotal = 0f;
            inferenceMsTotal = 0f;
            visibilityTotal = 0f;
            presenceTotal = 0f;
        }

        public void RecordFrame(LandmarkFrame frame, PoseQualityReport qualityReport, float inferenceMs)
        {
            if (session == null)
            {
                return;
            }

            sampledFrames++;
            metricFrameCount++;
            cameraFpsTotal += frame == null ? 0f : frame.cameraFps;
            poseFpsTotal += frame == null ? 0f : frame.poseFps;
            inferenceMsTotal += Mathf.Max(0f, inferenceMs);

            if (qualityReport == null)
            {
                return;
            }

            qualityFrameCount++;
            visibilityTotal += qualityReport.averageVisibility;
            presenceTotal += qualityReport.averagePresence;
        }

        public PoseSessionSummary Build(
            int successfulFrames,
            int failedFrames,
            int droppedFrames,
            IReadOnlyList<PoseFeedbackEvent> feedbackEvents,
            PoseFrameRingBuffer ringBuffer)
        {
            if (session == null)
            {
                return null;
            }

            var endedAtUtc = DateTime.UtcNow;
            var durationSeconds = Mathf.Max(0f, Time.realtimeSinceStartup - session.startedAtRealtimeSeconds);
            CountFeedbackSeverity(feedbackEvents, out var infoCount, out var warningCount, out var criticalCount);

            return new PoseSessionSummary
            {
                sessionId = session.sessionId,
                exercise = session.exercise,
                startedAtUtc = session.startedAtUtc,
                endedAtUtc = endedAtUtc.ToString("o"),
                platform = session.platform,
                backend = session.backend,
                cameraDevice = session.cameraDevice,
                storagePolicy = session.storagePolicy,
                durationSeconds = durationSeconds,
                successfulFrames = successfulFrames,
                failedFrames = failedFrames,
                droppedFrames = droppedFrames,
                sampledFrames = sampledFrames,
                feedbackCount = feedbackEvents == null ? 0 : feedbackEvents.Count,
                infoFeedbackCount = infoCount,
                warningFeedbackCount = warningCount,
                criticalFeedbackCount = criticalCount,
                averageCameraFps = metricFrameCount == 0 ? 0f : cameraFpsTotal / metricFrameCount,
                averagePoseFps = metricFrameCount == 0 ? 0f : poseFpsTotal / metricFrameCount,
                averageInferenceMs = metricFrameCount == 0 ? 0f : inferenceMsTotal / metricFrameCount,
                averageVisibility = qualityFrameCount == 0 ? 0f : visibilityTotal / qualityFrameCount,
                averagePresence = qualityFrameCount == 0 ? 0f : presenceTotal / qualityFrameCount,
                ringBufferFrameCount = ringBuffer == null ? 0 : ringBuffer.Count,
                ringBufferCapacity = ringBuffer == null ? 0 : ringBuffer.Capacity,
                topFeedbackIds = BuildTopFeedbackIds(feedbackEvents)
            };
        }

        public void Clear()
        {
            session = null;
            sampledFrames = 0;
            metricFrameCount = 0;
            qualityFrameCount = 0;
            cameraFpsTotal = 0f;
            poseFpsTotal = 0f;
            inferenceMsTotal = 0f;
            visibilityTotal = 0f;
            presenceTotal = 0f;
        }

        private static void CountFeedbackSeverity(
            IReadOnlyList<PoseFeedbackEvent> feedbackEvents,
            out int infoCount,
            out int warningCount,
            out int criticalCount)
        {
            infoCount = 0;
            warningCount = 0;
            criticalCount = 0;

            if (feedbackEvents == null)
            {
                return;
            }

            for (var i = 0; i < feedbackEvents.Count; i++)
            {
                var severity = feedbackEvents[i] == null ? string.Empty : feedbackEvents[i].severity;
                if (string.Equals(severity, PoseExerciseFeedbackSeverity.Critical.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    criticalCount++;
                }
                else if (string.Equals(severity, PoseExerciseFeedbackSeverity.Warning.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    warningCount++;
                }
                else
                {
                    infoCount++;
                }
            }
        }

        private static string[] BuildTopFeedbackIds(IReadOnlyList<PoseFeedbackEvent> feedbackEvents)
        {
            if (feedbackEvents == null || feedbackEvents.Count == 0)
            {
                return Array.Empty<string>();
            }

            var counts = new Dictionary<string, int>();
            for (var i = 0; i < feedbackEvents.Count; i++)
            {
                var key = feedbackEvents[i] == null ? string.Empty : feedbackEvents[i].ruleId;
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                counts.TryGetValue(key, out var count);
                counts[key] = count + 1;
            }

            var ranked = new List<KeyValuePair<string, int>>(counts);
            ranked.Sort((left, right) => right.Value.CompareTo(left.Value));

            var resultCount = Mathf.Min(3, ranked.Count);
            var result = new string[resultCount];
            for (var i = 0; i < resultCount; i++)
            {
                result[i] = ranked[i].Key;
            }

            return result;
        }
    }
}
