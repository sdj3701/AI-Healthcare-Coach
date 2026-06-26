using System;

namespace AIHealthcareCoach.MediaPipe
{
    [Serializable]
    public struct PoseLandmark
    {
        public int id;
        public string name;
        public float x;
        public float y;
        public float z;
        public float visibility;
        public float presence;

        public PoseLandmark(int id, float x, float y, float z, float visibility, float presence)
        {
            this.id = id;
            name = PoseLandmarkNames.GetName(id);
            this.x = x;
            this.y = y;
            this.z = z;
            this.visibility = visibility;
            this.presence = presence;
        }
    }

    [Serializable]
    public sealed class LandmarkFrame
    {
        public long timestampMs;
        public string cameraMode;
        public float cameraFps;
        public float poseFps;
        public int sourceWidth;
        public int sourceHeight;
        public bool mirrored;
        public int rotationAngle;
        public PoseLandmark[] landmarks;
        public PoseLandmark[] worldLandmarks;
        public string errorCode;
        public string errorMessage;

        public int LandmarkCount
        {
            get { return landmarks == null ? 0 : landmarks.Length; }
        }

        public bool HasPose
        {
            get { return LandmarkCount >= PoseLandmarkNames.Count; }
        }

        public static LandmarkFrame Empty(long timestampMs, string errorCode, string errorMessage)
        {
            return new LandmarkFrame
            {
                timestampMs = timestampMs,
                cameraMode = string.Empty,
                landmarks = Array.Empty<PoseLandmark>(),
                worldLandmarks = Array.Empty<PoseLandmark>(),
                errorCode = errorCode,
                errorMessage = errorMessage
            };
        }
    }
}
