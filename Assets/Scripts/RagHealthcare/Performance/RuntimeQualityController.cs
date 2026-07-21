using Rag.Healthcare.Camera;
using Rag.Healthcare.Pose;
using UnityEngine;

namespace Rag.Healthcare.Performance
{
    public enum DevicePerformanceMode { Standard, LowSpec }

    [System.Serializable]
    public sealed class PerformanceProfile
    {
        public DevicePerformanceMode mode;
        public int cameraWidth;
        public int cameraHeight;
        public int cameraFps;
        public int poseFps;
        public bool onDeviceLlmEnabled;
        public int maximumSessionMinutes;
    }

    public sealed class RuntimeQualityController : MonoBehaviour
    {
        [SerializeField] private CameraCaptureSource cameraSource;
        [SerializeField] private JointTrackingController trackingController;

        public PerformanceProfile Current { get; private set; }

        private void Awake()
        {
            cameraSource ??= FindFirstObjectByType<CameraCaptureSource>();
            trackingController ??= FindFirstObjectByType<JointTrackingController>();
        }

        public PerformanceProfile Apply(DevicePerformanceMode mode)
        {
            Current = mode == DevicePerformanceMode.LowSpec
                ? new PerformanceProfile { mode = mode, cameraWidth = 640, cameraHeight = 480, cameraFps = 20, poseFps = 10, onDeviceLlmEnabled = false, maximumSessionMinutes = 8 }
                : CreateStandardProfile(mode);
            cameraSource?.ConfigureCapture(Current.cameraWidth, Current.cameraHeight, Current.cameraFps);
            trackingController?.ConfigureSamplingRate(Current.poseFps);
            return Current;
        }

        private static PerformanceProfile CreateStandardProfile(DevicePerformanceMode mode)
        {
#if UNITY_IOS && !UNITY_EDITOR
            // Keep the profile request aligned with CameraCaptureSource's iOS clamp.
            // 640x480 avoids known WebCamTexture rotation metadata issues.
            const int cameraWidth = 640;
            const int cameraHeight = 480;
#else
            const int cameraWidth = 1280;
            const int cameraHeight = 720;
#endif
            return new PerformanceProfile
            {
                mode = mode,
                cameraWidth = cameraWidth,
                cameraHeight = cameraHeight,
                cameraFps = 30,
                poseFps = 15,
                onDeviceLlmEnabled = true,
                maximumSessionMinutes = 15
            };
        }
    }
}
