using UnityEngine;

namespace AIHealthcareCoach.MediaPipe
{
    public sealed class EditorStubPoseEstimator : IPoseEstimator
    {
        private readonly bool simulatePose;
        private bool isReady;
        private PoseEstimatorSettings settings;

        public EditorStubPoseEstimator(bool simulatePose)
        {
            this.simulatePose = simulatePose;
        }

        public string BackendName
        {
            get { return simulatePose ? "Editor Stub Pose" : "No Native Pose Backend"; }
        }

        public bool IsReady
        {
            get { return isReady; }
        }

        public string LastError { get; private set; }

        public bool Initialize(PoseEstimatorSettings settings)
        {
            this.settings = settings;
            isReady = true;
            LastError = string.Empty;
            return true;
        }

        public bool TryProcessFrame(
            Color32[] rgbaPixels,
            int width,
            int height,
            long timestampMs,
            bool mirrored,
            int rotationAngle,
            out LandmarkFrame frame)
        {
            if (!isReady)
            {
                frame = LandmarkFrame.Empty(timestampMs, "NOT_INITIALIZED", "Pose estimator is not initialized.");
                return false;
            }

            if (!simulatePose)
            {
                frame = LandmarkFrame.Empty(timestampMs, "NATIVE_BACKEND_UNAVAILABLE", "Real MediaPipe inference runs on iOS in this build.");
                return false;
            }

            frame = BuildSyntheticPose(timestampMs, width, height, mirrored, rotationAngle);
            return true;
        }

        public void Dispose()
        {
            isReady = false;
        }

        private LandmarkFrame BuildSyntheticPose(long timestampMs, int width, int height, bool mirrored, int rotationAngle)
        {
            var landmarks = new PoseLandmark[PoseLandmarkNames.Count];
            var seconds = timestampMs * 0.001f;
            var sway = Mathf.Sin(seconds * 1.5f) * 0.025f;
            var armLift = Mathf.Sin(seconds * 2.2f) * 0.05f;

            Set(landmarks, 0, 0.50f + sway, 0.16f, -0.06f, 0.98f);
            Set(landmarks, 1, 0.47f + sway, 0.14f, -0.05f, 0.96f);
            Set(landmarks, 2, 0.46f + sway, 0.14f, -0.05f, 0.96f);
            Set(landmarks, 3, 0.45f + sway, 0.14f, -0.05f, 0.96f);
            Set(landmarks, 4, 0.53f + sway, 0.14f, -0.05f, 0.96f);
            Set(landmarks, 5, 0.54f + sway, 0.14f, -0.05f, 0.96f);
            Set(landmarks, 6, 0.55f + sway, 0.14f, -0.05f, 0.96f);
            Set(landmarks, 7, 0.42f + sway, 0.17f, -0.04f, 0.94f);
            Set(landmarks, 8, 0.58f + sway, 0.17f, -0.04f, 0.94f);
            Set(landmarks, 9, 0.47f + sway, 0.20f, -0.04f, 0.95f);
            Set(landmarks, 10, 0.53f + sway, 0.20f, -0.04f, 0.95f);

            Set(landmarks, 11, 0.39f + sway, 0.31f, 0.0f, 0.98f);
            Set(landmarks, 12, 0.61f + sway, 0.31f, 0.0f, 0.98f);
            Set(landmarks, 13, 0.33f + sway, 0.45f + armLift, 0.02f, 0.96f);
            Set(landmarks, 14, 0.67f + sway, 0.45f - armLift, 0.02f, 0.96f);
            Set(landmarks, 15, 0.30f + sway, 0.59f + armLift, 0.02f, 0.95f);
            Set(landmarks, 16, 0.70f + sway, 0.59f - armLift, 0.02f, 0.95f);
            Set(landmarks, 17, 0.29f + sway, 0.62f + armLift, 0.01f, 0.93f);
            Set(landmarks, 18, 0.71f + sway, 0.62f - armLift, 0.01f, 0.93f);
            Set(landmarks, 19, 0.31f + sway, 0.63f + armLift, 0.01f, 0.93f);
            Set(landmarks, 20, 0.69f + sway, 0.63f - armLift, 0.01f, 0.93f);
            Set(landmarks, 21, 0.33f + sway, 0.61f + armLift, 0.01f, 0.93f);
            Set(landmarks, 22, 0.67f + sway, 0.61f - armLift, 0.01f, 0.93f);

            Set(landmarks, 23, 0.43f + sway, 0.55f, 0.0f, 0.98f);
            Set(landmarks, 24, 0.57f + sway, 0.55f, 0.0f, 0.98f);
            Set(landmarks, 25, 0.41f + sway, 0.75f, 0.03f, 0.97f);
            Set(landmarks, 26, 0.59f + sway, 0.75f, 0.03f, 0.97f);
            Set(landmarks, 27, 0.40f + sway, 0.93f, 0.02f, 0.96f);
            Set(landmarks, 28, 0.60f + sway, 0.93f, 0.02f, 0.96f);
            Set(landmarks, 29, 0.38f + sway, 0.96f, 0.02f, 0.94f);
            Set(landmarks, 30, 0.62f + sway, 0.96f, 0.02f, 0.94f);
            Set(landmarks, 31, 0.43f + sway, 0.98f, -0.02f, 0.94f);
            Set(landmarks, 32, 0.57f + sway, 0.98f, -0.02f, 0.94f);

            return new LandmarkFrame
            {
                timestampMs = timestampMs,
                cameraMode = "editor_stub",
                sourceWidth = width,
                sourceHeight = height,
                mirrored = mirrored,
                rotationAngle = rotationAngle,
                landmarks = landmarks,
                worldLandmarks = BuildWorldLandmarks(landmarks),
                errorCode = string.Empty,
                errorMessage = string.Empty
            };
        }

        private static PoseLandmark[] BuildWorldLandmarks(PoseLandmark[] source)
        {
            var world = new PoseLandmark[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                var landmark = source[i];
                world[i] = new PoseLandmark(
                    landmark.id,
                    (landmark.x - 0.5f) * 1.2f,
                    (0.55f - landmark.y) * 1.8f,
                    landmark.z,
                    landmark.visibility,
                    landmark.presence);
            }

            return world;
        }

        private static void Set(PoseLandmark[] landmarks, int id, float x, float y, float z, float visibility)
        {
            landmarks[id] = new PoseLandmark(id, Mathf.Clamp01(x), Mathf.Clamp01(y), z, visibility, visibility);
        }
    }
}
