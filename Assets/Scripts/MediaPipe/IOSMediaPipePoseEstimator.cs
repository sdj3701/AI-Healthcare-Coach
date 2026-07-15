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

        private const int InitialJsonBufferCapacity = 65536;
        private const int MaximumJsonBufferCapacity = 1024 * 1024;
        private const int MaximumImageDimension = 8192;

        private StringBuilder jsonBuffer = new StringBuilder(InitialJsonBufferCapacity);
        private GCHandle pinnedPixelsHandle;
        private Color32[] pinnedPixels;
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

            if (!TryValidateFrame(rgbaPixels, width, height, out var validationError))
            {
                LastError = validationError;
                frame = LandmarkFrame.Empty(timestampMs, "INVALID_FRAME", LastError);
                return false;
            }

            try
            {
                EnsurePixelsPinned(rgbaPixels);
                var code = AHC_PoseProcessRgba(
                    pinnedPixelsHandle.AddrOfPinnedObject(),
                    width,
                    height,
                    timestampMs,
                    rotationAngle,
                    mirrored ? 1 : 0);

                if (code != 0)
                {
                    LastError = code == -14
                        ? "MediaPipe is still processing the previous frame."
                        : ReadLastError();
                    frame = LandmarkFrame.Empty(timestampMs, "NATIVE_PROCESS_FAILED", LastError);
                    return false;
                }
            }
            catch (Exception exception)
            {
                LastError = "Failed to pin or process the camera frame: " + exception.Message;
                frame = LandmarkFrame.Empty(timestampMs, "NATIVE_PROCESS_EXCEPTION", LastError);
                return false;
            }

            if (!TryReadLatestJson(out var json, out var jsonError))
            {
                LastError = jsonError;
                frame = LandmarkFrame.Empty(timestampMs, "EMPTY_NATIVE_RESULT", LastError);
                return false;
            }

            try
            {
                frame = JsonUtility.FromJson<LandmarkFrame>(json);
            }
            catch (Exception exception)
            {
                LastError = "MediaPipe returned invalid JSON: " + exception.Message;
                frame = LandmarkFrame.Empty(timestampMs, "INVALID_NATIVE_RESULT", LastError);
                return false;
            }

            if (frame == null)
            {
                LastError = "MediaPipe returned an empty pose result.";
                frame = LandmarkFrame.Empty(timestampMs, "INVALID_NATIVE_RESULT", LastError);
                return false;
            }

            if (!string.IsNullOrEmpty(frame.errorCode))
            {
                LastError = frame.errorMessage;
                return false;
            }

            LastError = string.Empty;
            return true;
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
            ReleasePinnedPixels();
            isReady = false;
        }

        private static bool TryValidateFrame(
            Color32[] rgbaPixels,
            int width,
            int height,
            out string error)
        {
            if (rgbaPixels == null || rgbaPixels.Length == 0)
            {
                error = "Frame pixels are empty.";
                return false;
            }

            if (width <= 0 || height <= 0 || width > MaximumImageDimension || height > MaximumImageDimension)
            {
                error = $"Frame dimensions are invalid: {width}x{height}.";
                return false;
            }

            var requiredPixelCount = (long)width * height;
            if (requiredPixelCount > rgbaPixels.LongLength)
            {
                error = $"Frame buffer is too small. Expected {requiredPixelCount} pixels but received {rgbaPixels.LongLength}.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void EnsurePixelsPinned(Color32[] pixels)
        {
            if (ReferenceEquals(pinnedPixels, pixels) && pinnedPixelsHandle.IsAllocated)
            {
                return;
            }

            ReleasePinnedPixels();
            pinnedPixelsHandle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            pinnedPixels = pixels;
        }

        private void ReleasePinnedPixels()
        {
            if (pinnedPixelsHandle.IsAllocated)
            {
                pinnedPixelsHandle.Free();
            }

            pinnedPixels = null;
        }

        private bool TryReadLatestJson(out string json, out string error)
        {
#if UNITY_IOS && !UNITY_EDITOR
            jsonBuffer.Length = 0;
            var required = AHC_PoseGetLatestJson(jsonBuffer, jsonBuffer.Capacity);
            if (required <= 0)
            {
                json = string.Empty;
                error = ReadLastError();
                return false;
            }

            if (required > jsonBuffer.Capacity)
            {
                if (required > MaximumJsonBufferCapacity)
                {
                    json = string.Empty;
                    error = $"MediaPipe JSON result is too large ({required} bytes).";
                    return false;
                }

                jsonBuffer = new StringBuilder(required);
                required = AHC_PoseGetLatestJson(jsonBuffer, jsonBuffer.Capacity);
                if (required <= 0 || required > jsonBuffer.Capacity)
                {
                    json = string.Empty;
                    error = "MediaPipe JSON result changed size while it was being copied.";
                    return false;
                }
            }

            if (jsonBuffer.Length == 0)
            {
                json = string.Empty;
                error = ReadLastError();
                return false;
            }

            json = jsonBuffer.ToString();
            error = string.Empty;
            return true;
#else
            json = string.Empty;
            error = "iOS MediaPipe bridge is not available in this runtime.";
            return false;
#endif
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
