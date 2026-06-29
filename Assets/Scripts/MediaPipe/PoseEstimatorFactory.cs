namespace AIHealthcareCoach.MediaPipe
{
    public static class PoseEstimatorFactory
    {
        public static IPoseEstimator Create(bool simulatePoseWhenNativeUnavailable)
        {
            return Create(new PoseEstimatorSettings
            {
                simulatePoseWhenNativeUnavailable = simulatePoseWhenNativeUnavailable
            });
        }

        public static IPoseEstimator Create(PoseEstimatorSettings settings)
        {
#if UNITY_IOS && !UNITY_EDITOR
            return new IOSMediaPipePoseEstimator();
#elif UNITY_EDITOR
            if (settings != null && settings.usePythonMediaPipeInEditor)
            {
                return new EditorPythonMediaPipePoseEstimator();
            }

            return new EditorStubPoseEstimator(settings != null && settings.simulatePoseWhenNativeUnavailable);
#else
            return new EditorStubPoseEstimator(settings != null && settings.simulatePoseWhenNativeUnavailable);
#endif
        }
    }
}
