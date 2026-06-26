using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace AIHealthcareCoach.MediaPipe
{
    public sealed class IOSMediaPipePoseEstimator : IPoseEstimator
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int AHC_PoseInitialize(
            string modelPath,
            int numPoses,
            float minPoseDetectionConfidence,
            float minPosePresenceConfidence,
            float minTrackingConfidence);

        [DllImport("__Internal")]
        private static extern int AHC_PoseProcessRgba(
            IntPtr rgbaPixels,
            int width,
            int height,
            long timestampMs,
            int rotationAngle,
            int mirrored);

        [DllImport("__Internal")]
        private static extern int AHC_PoseGetLatestJson(StringBuilder buffer, int capacity);

        [DllImport("__Internal")]
        private static extern int AHC_PoseGetLastError(StringBuilder buffer, int capacity);

        [DllImport("__Internal")]
        private static extern void AHC_PoseDispose();
#endif

        private const int JsonBufferCapacity = 65536;
        private readonly StringBuilder jsonBuffer = new StringBuilder(JsonBufferCapacity);
        private bool isReady;

        public string BackendName
        {
            get { return "iOS MediaPipeTasksVision"; }
        }

        public bool IsReady
        {
            get { return isReady; }
        }

        public string LastError { get; private set; }

        public bool Initialize(PoseEstimatorSettings settings)
        {
#if UNITY_IOS && !UNITY_EDITOR
            var code = AHC_PoseInitialize(
                settings.modelPath,
                settings.numPoses,
                settings.minPoseDetectionConfidence,
                settings.minPosePresenceConfidence,
                settings.minTrackingConfidence);

            isReady = code == 0;
            LastError = isReady ? string.Empty : ReadLastError();
            return isReady;
#else
            isReady = false;
            LastError = "iOS MediaPipe bridge is only available in an iOS device build.";
            return false;
#endif
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
#if UNITY_IOS && !UNITY_EDITOR
            if (!isReady)
            {
                frame = LandmarkFrame.Empty(timestampMs, "NOT_INITIALIZED", LastError);
                return false;
            }

            if (rgbaPixels == null || rgbaPixels.Length == 0 || width <= 0 || height <= 0)
            {
                frame = LandmarkFrame.Empty(timestampMs, "INVALID_FRAME", "Frame pixels are empty.");
                return false;
            }

            var handle = GCHandle.Alloc(rgbaPixels, GCHandleType.Pinned);
            try
            {
                var code = AHC_PoseProcessRgba(
                    handle.AddrOfPinnedObject(),
                    width,
                    height,
                    timestampMs,
                    rotationAngle,
                    mirrored ? 1 : 0);

                if (code != 0)
                {
                    LastError = ReadLastError();
                    frame = LandmarkFrame.Empty(timestampMs, "NATIVE_PROCESS_FAILED", LastError);
                    return false;
                }
            }
            finally
            {
                handle.Free();
            }

            jsonBuffer.Length = 0;
            var required = AHC_PoseGetLatestJson(jsonBuffer, JsonBufferCapacity);
            if (required <= 0 || jsonBuffer.Length == 0)
            {
                LastError = ReadLastError();
                frame = LandmarkFrame.Empty(timestampMs, "EMPTY_NATIVE_RESULT", LastError);
                return false;
            }

            frame = JsonUtility.FromJson<LandmarkFrame>(jsonBuffer.ToString());
            return frame != null && string.IsNullOrEmpty(frame.errorCode);
#else
            frame = LandmarkFrame.Empty(timestampMs, "IOS_BACKEND_UNAVAILABLE", LastError);
            return false;
#endif
        }

        public void Dispose()
        {
#if UNITY_IOS && !UNITY_EDITOR
            AHC_PoseDispose();
#endif
            isReady = false;
        }

        private static string ReadLastError()
        {
#if UNITY_IOS && !UNITY_EDITOR
            var buffer = new StringBuilder(2048);
            AHC_PoseGetLastError(buffer, buffer.Capacity);
            return buffer.ToString();
#else
            return "iOS MediaPipe bridge is not available in this runtime.";
#endif
        }
    }
}
