using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AIHealthcareCoach.MediaPipe
{
    public sealed class PoseSessionStorage
    {
        private readonly string rootDirectory;
        private readonly string summariesDirectory;
        private readonly string eventsDirectory;
        private readonly string debugDirectory;

        public PoseSessionStorage(string rootFolderName)
        {
            var safeRoot = string.IsNullOrWhiteSpace(rootFolderName) ? "pose_sessions" : rootFolderName.Trim();
            rootDirectory = Path.Combine(Application.persistentDataPath, safeRoot);
            summariesDirectory = Path.Combine(rootDirectory, "summaries");
            eventsDirectory = Path.Combine(rootDirectory, "events");
            debugDirectory = Path.Combine(rootDirectory, "debug");
        }

        public string RootDirectory
        {
            get { return rootDirectory; }
        }

        public string DebugDirectory
        {
            get { return debugDirectory; }
        }

        public PoseSessionSaveResult SaveSession(
            PoseSessionSummary summary,
            IReadOnlyList<PoseFeedbackEvent> feedbackEvents)
        {
            if (summary == null || string.IsNullOrWhiteSpace(summary.sessionId))
            {
                return PoseSessionSaveResult.Fail("Session summary is empty.");
            }

            try
            {
                Directory.CreateDirectory(summariesDirectory);
                Directory.CreateDirectory(eventsDirectory);

                var safeSessionId = SanitizeFileName(summary.sessionId);
                var summaryPath = Path.Combine(summariesDirectory, safeSessionId + "_summary.json");
                var eventsPath = Path.Combine(eventsDirectory, safeSessionId + "_events.jsonl");

                summary.summaryPath = summaryPath;
                summary.eventsPath = eventsPath;
                File.WriteAllText(summaryPath, JsonUtility.ToJson(summary, true));

                using (var writer = new StreamWriter(eventsPath, false))
                {
                    if (feedbackEvents != null)
                    {
                        for (var i = 0; i < feedbackEvents.Count; i++)
                        {
                            if (feedbackEvents[i] != null)
                            {
                                writer.WriteLine(JsonUtility.ToJson(feedbackEvents[i]));
                            }
                        }
                    }
                }

                return PoseSessionSaveResult.Ok(summaryPath, eventsPath);
            }
            catch (Exception exception)
            {
                return PoseSessionSaveResult.Fail(exception.Message);
            }
        }

        public string CreateDebugLandmarkLogPath(string sessionId)
        {
            return CreateDebugLogPath(sessionId, "landmarks_debug.jsonl");
        }

        public string CreateDebugQaLogPath(string sessionId)
        {
            return CreateDebugLogPath(sessionId, "qa.jsonl");
        }

        private string CreateDebugLogPath(string sessionId, string suffix)
        {
            Directory.CreateDirectory(debugDirectory);
            return Path.Combine(debugDirectory, SanitizeFileName(sessionId) + "_" + SanitizeFileName(suffix));
        }

        private static string SanitizeFileName(string value)
        {
            var safe = string.IsNullOrWhiteSpace(value) ? "session" : value.Trim();
            var invalidChars = Path.GetInvalidFileNameChars();
            for (var i = 0; i < invalidChars.Length; i++)
            {
                safe = safe.Replace(invalidChars[i], '_');
            }

            return safe;
        }
    }

    public readonly struct PoseSessionSaveResult
    {
        public readonly bool success;
        public readonly string summaryPath;
        public readonly string eventsPath;
        public readonly string error;

        private PoseSessionSaveResult(bool success, string summaryPath, string eventsPath, string error)
        {
            this.success = success;
            this.summaryPath = summaryPath ?? string.Empty;
            this.eventsPath = eventsPath ?? string.Empty;
            this.error = error ?? string.Empty;
        }

        public static PoseSessionSaveResult Ok(string summaryPath, string eventsPath)
        {
            return new PoseSessionSaveResult(true, summaryPath, eventsPath, string.Empty);
        }

        public static PoseSessionSaveResult Fail(string error)
        {
            return new PoseSessionSaveResult(false, string.Empty, string.Empty, error);
        }
    }
}
