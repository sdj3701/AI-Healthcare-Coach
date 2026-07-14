using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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

        public PoseCoordinateSaveResult SaveCompressedCoordinates(
            string sessionId,
            IReadOnlyList<LandmarkFrame> frames)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return PoseCoordinateSaveResult.Fail("Session ID is empty.");
            }

            try
            {
                Directory.CreateDirectory(eventsDirectory);
                var path = Path.Combine(eventsDirectory, SanitizeFileName(sessionId) + "_coordinates.jsonl.gz");
                using (var file = File.Create(path))
                using (var gzip = new GZipStream(file, System.IO.Compression.CompressionLevel.Optimal))
                using (var writer = new StreamWriter(gzip))
                {
                    if (frames != null)
                    {
                        for (var i = 0; i < frames.Count; i++)
                        {
                            if (frames[i] != null)
                            {
                                writer.WriteLine(JsonUtility.ToJson(frames[i]));
                            }
                        }
                    }
                }

                return PoseCoordinateSaveResult.Ok(path, frames == null ? 0 : frames.Count);
            }
            catch (Exception exception)
            {
                return PoseCoordinateSaveResult.Fail(exception.Message);
            }
        }

        public string[] ListSessionIds()
        {
            if (!Directory.Exists(summariesDirectory)) return Array.Empty<string>();
            var files = Directory.GetFiles(summariesDirectory, "*_summary.json", SearchOption.TopDirectoryOnly);
            var result = new string[files.Length];
            for (var i = 0; i < files.Length; i++)
            {
                var name = Path.GetFileNameWithoutExtension(files[i]);
                result[i] = name.EndsWith("_summary", StringComparison.Ordinal)
                    ? name.Substring(0, name.Length - "_summary".Length)
                    : name;
            }
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        public bool DeleteSession(string sessionId, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                error = "Session ID is empty.";
                return false;
            }

            try
            {
                var safe = SanitizeFileName(sessionId);
                DeleteIfExists(Path.Combine(summariesDirectory, safe + "_summary.json"));
                DeleteIfExists(Path.Combine(eventsDirectory, safe + "_events.jsonl"));
                DeleteIfExists(Path.Combine(eventsDirectory, safe + "_coordinates.jsonl.gz"));
                foreach (var path in Directory.Exists(debugDirectory)
                             ? Directory.GetFiles(debugDirectory, safe + "_*", SearchOption.TopDirectoryOnly)
                             : Array.Empty<string>())
                {
                    DeleteIfExists(path);
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public bool DeleteAll(out string error)
        {
            error = string.Empty;
            try
            {
                if (Directory.Exists(rootDirectory)) Directory.Delete(rootDirectory, true);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
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

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
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

    public readonly struct PoseCoordinateSaveResult
    {
        public readonly bool success;
        public readonly string path;
        public readonly int frameCount;
        public readonly string error;

        private PoseCoordinateSaveResult(bool success, string path, int frameCount, string error)
        {
            this.success = success;
            this.path = path ?? string.Empty;
            this.frameCount = frameCount;
            this.error = error ?? string.Empty;
        }

        public static PoseCoordinateSaveResult Ok(string path, int frameCount) => new PoseCoordinateSaveResult(true, path, frameCount, string.Empty);
        public static PoseCoordinateSaveResult Fail(string error) => new PoseCoordinateSaveResult(false, string.Empty, 0, error);
    }
}
