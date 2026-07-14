using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIHealthcareCoach.MediaPipe;
using UnityEngine;

namespace Rag.Healthcare.Analytics
{
    [Serializable]
    public sealed class SessionListItem
    {
        public string sessionId;
        public string exercise;
        public string startedAtUtc;
        public float durationSeconds;
        public int feedbackCount;
        public float stabilityScore;
    }

    public sealed class SessionHistoryRepository
    {
        private readonly string summariesDirectory;

        public SessionHistoryRepository(string rootFolderName = "pose_sessions")
        {
            summariesDirectory = Path.Combine(Application.persistentDataPath, rootFolderName, "summaries");
        }

        public SessionListItem[] Load()
        {
            if (!Directory.Exists(summariesDirectory)) return Array.Empty<SessionListItem>();
            var result = new List<SessionListItem>();
            foreach (var path in Directory.GetFiles(summariesDirectory, "*_summary.json"))
            {
                try
                {
                    var summary = JsonUtility.FromJson<PoseSessionSummary>(File.ReadAllText(path));
                    if (summary == null) continue;
                    result.Add(new SessionListItem
                    {
                        sessionId = summary.sessionId,
                        exercise = summary.exercise,
                        startedAtUtc = summary.startedAtUtc,
                        durationSeconds = summary.durationSeconds,
                        feedbackCount = summary.feedbackCount,
                        stabilityScore = StabilityScore.Calculate(summary)
                    });
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[SessionHistory] Skipped invalid summary: " + exception.Message);
                }
            }
            return result.OrderByDescending(item => item.startedAtUtc).ToArray();
        }
    }

    public static class StabilityScore
    {
        public static float Calculate(PoseSessionSummary summary)
        {
            if (summary == null) return 0f;
            var attempted = Mathf.Max(1, summary.successfulFrames + summary.failedFrames + summary.droppedFrames);
            var framePenalty = 35f * (summary.failedFrames + summary.droppedFrames) / attempted;
            var feedbackPenalty = Mathf.Min(35f, summary.warningFeedbackCount * 1.5f + summary.criticalFeedbackCount * 4f);
            var confidencePenalty = 30f * (1f - Mathf.Clamp01(summary.averageVisibility));
            return Mathf.Clamp(100f - framePenalty - feedbackPenalty - confidencePenalty, 0f, 100f);
        }
    }

    [Serializable]
    public sealed class ErrorTrend
    {
        public string ruleId;
        public int occurrences;
        public bool improving;
    }

    public static class SessionTrendAnalyzer
    {
        public static ErrorTrend[] Analyze(IReadOnlyList<PoseSessionSummary> chronologicalSessions)
        {
            if (chronologicalSessions == null) return Array.Empty<ErrorTrend>();
            var firstHalf = new Dictionary<string, int>();
            var secondHalf = new Dictionary<string, int>();
            var total = new Dictionary<string, int>();
            for (var index = 0; index < chronologicalSessions.Count; index++)
            {
                var ids = chronologicalSessions[index]?.topFeedbackIds;
                if (ids == null) continue;
                foreach (var id in ids)
                {
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    Increment(total, id);
                    Increment(index < chronologicalSessions.Count / 2 ? firstHalf : secondHalf, id);
                }
            }
            return total.Select(item => new ErrorTrend
            {
                ruleId = item.Key,
                occurrences = item.Value,
                improving = Get(secondHalf, item.Key) < Get(firstHalf, item.Key)
            }).OrderByDescending(item => item.occurrences).ToArray();
        }

        private static void Increment(Dictionary<string, int> values, string key) => values[key] = Get(values, key) + 1;
        private static int Get(Dictionary<string, int> values, string key) => values.TryGetValue(key, out var value) ? value : 0;
    }

    [Serializable]
    public sealed class PrivacyTrustSurveyResponse
    {
        public string submittedAtUtc;
        [Range(1, 5)] public int understoodNoVideoStorage;
        [Range(1, 5)] public int trustOnDeviceProcessing;
        [Range(1, 5)] public int deletionConfidence;
        public string optionalComment;
    }

    public static class PrivacyTrustSurveyStore
    {
        public static bool Save(PrivacyTrustSurveyResponse response, out string error)
        {
            error = string.Empty;
            try
            {
                response.submittedAtUtc = DateTime.UtcNow.ToString("o");
                var directory = Path.Combine(Application.persistentDataPath, "analytics");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "privacy_trust_survey.jsonl"), JsonUtility.ToJson(response) + Environment.NewLine);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }
    }

    public enum CoreEventName
    {
        OnboardingCompleted,
        WorkoutStarted,
        CalibrationCompleted,
        FeedbackShown,
        WorkoutCompleted,
        ReportViewed,
        ReplayViewed,
        LocalDataDeleted
    }

    [Serializable]
    public sealed class CoreEventRecord
    {
        public string eventName;
        public string timestampUtc;
        public string sessionId;
        public string exercise;
        public string value;
    }

    public static class CoreEventLogger
    {
        public static void Log(CoreEventName eventName, string sessionId = "", string exercise = "", string value = "")
        {
            var record = new CoreEventRecord
            {
                eventName = eventName.ToString(),
                timestampUtc = DateTime.UtcNow.ToString("o"),
                sessionId = sessionId ?? string.Empty,
                exercise = exercise ?? string.Empty,
                value = value ?? string.Empty
            };
            var directory = Path.Combine(Application.persistentDataPath, "analytics");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "core_events.jsonl"), JsonUtility.ToJson(record) + Environment.NewLine);
        }
    }
}
