using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AIHealthcareCoach.MediaPipe
{
    public sealed class MediaPipeQaLogger : IDisposable
    {
        private readonly StreamWriter writer;

        public MediaPipeQaLogger(string fileName)
        {
            var safeFileName = string.IsNullOrWhiteSpace(fileName)
                ? "mediapipe_pose_qa.jsonl"
                : fileName.Trim();
            var path = Path.Combine(Application.persistentDataPath, safeFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            writer = new StreamWriter(path, true);
            FilePath = path;
        }

        public string FilePath { get; }

        public void Write(
            string status,
            string backend,
            string cameraDevice,
            LandmarkFrame frame,
            PoseQualityReport report,
            float inferenceMs,
            int successfulFrames,
            int failedFrames,
            int droppedFrames,
            IReadOnlyList<PoseExerciseFeedbackMessage> feedbackMessages,
            string errorMessage)
        {
            if (writer == null)
            {
                return;
            }

            var record = new QaRecord
            {
                timestampUtc = DateTime.UtcNow.ToString("o"),
                platform = Application.platform.ToString(),
                status = status ?? string.Empty,
                backend = backend ?? string.Empty,
                cameraDevice = cameraDevice ?? string.Empty,
                cameraFps = frame == null ? 0f : frame.cameraFps,
                poseFps = frame == null ? 0f : frame.poseFps,
                inferenceMs = inferenceMs,
                successfulFrames = successfulFrames,
                failedFrames = failedFrames,
                droppedFrames = droppedFrames,
                landmarkCount = frame == null ? 0 : frame.LandmarkCount,
                avgVisibility = report == null ? 0f : report.averageVisibility,
                avgPresence = report == null ? 0f : report.averagePresence,
                visibleRequired = report == null ? 0 : report.visibleRequiredLandmarks,
                trackableRequired = report == null ? 0 : report.trackableRequiredLandmarks,
                requiredLandmarkCount = report == null ? 0 : report.requiredLandmarkCount,
                qualityState = report == null ? string.Empty : report.state.ToString(),
                feedbackCount = feedbackMessages == null ? 0 : feedbackMessages.Count,
                firstFeedbackId = feedbackMessages == null || feedbackMessages.Count == 0 ? string.Empty : feedbackMessages[0].id,
                firstFeedbackSeverity = feedbackMessages == null || feedbackMessages.Count == 0 ? string.Empty : feedbackMessages[0].severity.ToString(),
                errorCode = frame == null ? string.Empty : frame.errorCode,
                errorMessage = errorMessage ?? string.Empty
            };

            writer.WriteLine(JsonUtility.ToJson(record));
            writer.Flush();
        }

        public void Dispose()
        {
            writer?.Dispose();
        }

        [Serializable]
        private sealed class QaRecord
        {
            public string timestampUtc;
            public string platform;
            public string status;
            public string backend;
            public string cameraDevice;
            public float cameraFps;
            public float poseFps;
            public float inferenceMs;
            public int successfulFrames;
            public int failedFrames;
            public int droppedFrames;
            public int landmarkCount;
            public float avgVisibility;
            public float avgPresence;
            public int visibleRequired;
            public int trackableRequired;
            public int requiredLandmarkCount;
            public string qualityState;
            public int feedbackCount;
            public string firstFeedbackId;
            public string firstFeedbackSeverity;
            public string errorCode;
            public string errorMessage;
        }
    }
}
