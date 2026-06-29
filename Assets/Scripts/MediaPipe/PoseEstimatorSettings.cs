using System;

namespace AIHealthcareCoach.MediaPipe
{
    [Serializable]
    public sealed class PoseEstimatorSettings
    {
        public string modelPath;
        public int numPoses = 1;
        public float minPoseDetectionConfidence = 0.5f;
        public float minPosePresenceConfidence = 0.5f;
        public float minTrackingConfidence = 0.5f;
        public int targetPoseFps = 15;
        public bool simulatePoseWhenNativeUnavailable;
        public bool usePythonMediaPipeInEditor = true;
        public string editorPythonExecutablePath;
        public string editorPythonWorkerRelativePath = "MediaPipe/editor_pose_worker.py";
    }
}
