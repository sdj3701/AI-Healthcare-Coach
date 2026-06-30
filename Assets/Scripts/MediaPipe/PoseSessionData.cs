using System;

namespace AIHealthcareCoach.MediaPipe
{
    [Serializable]
    public sealed class PoseSessionData
    {
        public string sessionId;
        public string exercise;
        public string startedAtUtc;
        public string platform;
        public string backend;
        public string cameraDevice;
        public string storagePolicy;

        [NonSerialized] public float startedAtRealtimeSeconds;
    }

    [Serializable]
    public sealed class PoseFeedbackEvent
    {
        public string sessionId;
        public string timestampUtc;
        public long tMs;
        public long frameTimestampMs;
        public string ruleId;
        public string severity;
        public string message;
        public int jointId;
        public string jointName;
        public float confidence;
        public string qualityState;
        public float cameraFps;
        public float poseFps;
        public int rep;
    }

    [Serializable]
    public sealed class PoseSessionSummary
    {
        public string sessionId;
        public string exercise;
        public string startedAtUtc;
        public string endedAtUtc;
        public string platform;
        public string backend;
        public string cameraDevice;
        public string storagePolicy;
        public float durationSeconds;
        public int successfulFrames;
        public int failedFrames;
        public int droppedFrames;
        public int sampledFrames;
        public int feedbackCount;
        public int infoFeedbackCount;
        public int warningFeedbackCount;
        public int criticalFeedbackCount;
        public float averageCameraFps;
        public float averagePoseFps;
        public float averageInferenceMs;
        public float averageVisibility;
        public float averagePresence;
        public int ringBufferFrameCount;
        public int ringBufferCapacity;
        public string[] topFeedbackIds;
        public string summaryPath;
        public string eventsPath;
    }
}
