namespace AIHealthcareCoach.MediaPipe
{
    public static class PoseEstimatorFactory
    {
        public static IPoseEstimator Create(bool simulatePoseWhenNativeUnavailable)
        {
#if UNITY_IOS && !UNITY_EDITOR
            return new IOSMediaPipePoseEstimator();
#else
            return new EditorStubPoseEstimator(simulatePoseWhenNativeUnavailable);
#endif
        }
    }
}
