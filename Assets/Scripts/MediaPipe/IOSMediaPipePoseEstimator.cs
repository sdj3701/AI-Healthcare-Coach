using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AIHealthcareCoach.MediaPipe
{
    public sealed class IOSMediaPipePoseEstimator : IPoseEstimator, IAsyncPoseEstimator
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
        private static extern int AHC_PoseGetBridgeVersion();

        [DllImport("__Internal")]
        private static extern int AHC_PoseProcessRgba(
            IntPtr rgbaPixels,
            int width,
            int height,
            long timestampMs,
            int rotationAngle,
            int mirrored);

        [DllImport("__Internal")]
        private static extern int AHC_PoseSubmitRgba(
            IntPtr rgbaPixels,
            int width,
            int height,
            long timestampMs,
            int rotationAngle,
            int mirrored);

        [DllImport("__Internal")]
        private static extern int AHC_PoseTryConsumeLatest();

        [DllImport("__Internal")]
        private static extern void AHC_PoseCancelPending();

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
#if UNITY_IOS && !UNITY_EDITOR
        private const int RequiredAsyncBridgeVersion = 2;
#endif

        private StringBuilder jsonBuffer = new StringBuilder(InitialJsonBufferCapacity);
        private GCHandle pinnedPixelsHandle;
        private Color32[] pinnedPixels;
        private bool isReady;
#if UNITY_IOS && !UNITY_EDITOR
        private static readonly object NativeLifecycleGate = new object();
        private bool asyncApiAvailable = true;
        private long pendingTimestampMs;
        private InitializationSettings lastInitializationSettings;
        private bool hasInitializationSettings;

        private readonly struct InitializationSettings
        {
            public InitializationSettings(PoseEstimatorSettings settings)
            {
                ModelPath = settings.modelPath;
                NumPoses = settings.numPoses;
                MinPoseDetectionConfidence = settings.minPoseDetectionConfidence;
                MinPosePresenceConfidence = settings.minPosePresenceConfidence;
                MinTrackingConfidence = settings.minTrackingConfidence;
            }

            public string ModelPath { get; }
            public int NumPoses { get; }
            public float MinPoseDetectionConfidence { get; }
            public float MinPosePresenceConfidence { get; }
            public float MinTrackingConfidence { get; }
        }
#endif

        public string BackendName
        {
            get { return "iOS MediaPipeTasksVision"; }
        }

        public bool IsReady
        {
            get { return isReady; }
        }

        public bool SupportsAsyncProcessing
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                return isReady && asyncApiAvailable;
#else
                return false;
#endif
            }
        }

        public string LastError { get; private set; }

        public bool Initialize(PoseEstimatorSettings settings)
        {
            if (settings == null)
            {
                isReady = false;
                LastError = "Pose estimator settings are missing.";
                return false;
            }

#if UNITY_IOS && !UNITY_EDITOR
            Monitor.Enter(NativeLifecycleGate);
            try
            {
                var initializationSettings = new InitializationSettings(settings);
                int bridgeVersion;
                try
                {
                    bridgeVersion = AHC_PoseGetBridgeVersion();
                }
                catch (EntryPointNotFoundException)
                {
                    isReady = false;
                    asyncApiAvailable = false;
                    LastError = BuildOutdatedBridgeMessage();
                    return false;
                }

                if (bridgeVersion < RequiredAsyncBridgeVersion)
                {
                    isReady = false;
                    asyncApiAvailable = false;
                    LastError = BuildOutdatedBridgeMessage();
                    return false;
                }

                int code;
                try
                {
                    code = InitializeNative(initializationSettings);
                }
                catch (EntryPointNotFoundException)
                {
                    isReady = false;
                    asyncApiAvailable = false;
                    LastError = BuildOutdatedBridgeMessage();
                    return false;
                }

                isReady = code == 0;
                asyncApiAvailable = true;
                pendingTimestampMs = 0;
                if (isReady)
                {
                    lastInitializationSettings = initializationSettings;
                    hasInitializationSettings = true;
                }
                LastError = isReady ? string.Empty : ReadLastError();
                return isReady;
            }
            finally
            {
                Monitor.Exit(NativeLifecycleGate);
            }
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

            return TryParseLatestFrame(timestampMs, out frame);
#else
            frame = LandmarkFrame.Empty(timestampMs, "IOS_BACKEND_UNAVAILABLE", LastError);
            return false;
#endif
        }

        public bool TrySubmitFrame(
            Color32[] rgbaPixels,
            int width,
            int height,
            long timestampMs,
            bool mirrored,
            int rotationAngle,
            out string errorMessage)
        {
            errorMessage = string.Empty;
#if UNITY_IOS && !UNITY_EDITOR
            if (!isReady)
            {
                errorMessage = string.IsNullOrWhiteSpace(LastError)
                    ? "Pose estimator is not initialized."
                    : LastError;
                return false;
            }

            if (!TryValidateFrame(rgbaPixels, width, height, out errorMessage))
            {
                LastError = errorMessage;
                return false;
            }

            try
            {
                EnsurePixelsPinned(rgbaPixels);
                var code = AHC_PoseSubmitRgba(
                    pinnedPixelsHandle.AddrOfPinnedObject(),
                    width,
                    height,
                    timestampMs,
                    rotationAngle,
                    mirrored ? 1 : 0);
                if (code == 0)
                {
                    pendingTimestampMs = timestampMs;
                    LastError = string.Empty;
                    return true;
                }

                errorMessage = code == -14
                    ? "MediaPipe is still processing the previous frame."
                    : ReadLastError();
                LastError = errorMessage;
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                asyncApiAvailable = false;
                errorMessage = BuildOutdatedBridgeMessage();
                LastError = errorMessage;
                return false;
            }
            catch (Exception exception)
            {
                errorMessage = "Failed to submit the camera frame: " + exception.Message;
                LastError = errorMessage;
                return false;
            }
#else
            errorMessage = "Asynchronous iOS MediaPipe is only available in an iOS device build.";
            return false;
#endif
        }

        public AsyncPoseResultStatus TryGetLatestResult(
            out LandmarkFrame frame,
            out string errorMessage)
        {
            frame = null;
            errorMessage = string.Empty;
#if UNITY_IOS && !UNITY_EDITOR
            if (!isReady || !asyncApiAvailable)
            {
                errorMessage = string.IsNullOrWhiteSpace(LastError)
                    ? "Pose estimator is not initialized."
                    : LastError;
                return AsyncPoseResultStatus.Failed;
            }

            try
            {
                var status = AHC_PoseTryConsumeLatest();
                if (status == 0)
                {
                    return AsyncPoseResultStatus.Waiting;
                }

                if (status < 0)
                {
                    errorMessage = ReadLastError();
                    LastError = errorMessage;
                    pendingTimestampMs = 0;
                    return AsyncPoseResultStatus.Failed;
                }

                var timestamp = pendingTimestampMs;
                pendingTimestampMs = 0;
                if (!TryParseLatestFrame(timestamp, out frame))
                {
                    errorMessage = LastError;
                    return AsyncPoseResultStatus.Failed;
                }

                return AsyncPoseResultStatus.Ready;
            }
            catch (EntryPointNotFoundException)
            {
                asyncApiAvailable = false;
                errorMessage = BuildOutdatedBridgeMessage();
                LastError = errorMessage;
                return AsyncPoseResultStatus.Failed;
            }
            catch (Exception exception)
            {
                errorMessage = "Failed to read the asynchronous pose result: " + exception.Message;
                LastError = errorMessage;
                pendingTimestampMs = 0;
                return AsyncPoseResultStatus.Failed;
            }
#else
            errorMessage = "Asynchronous iOS MediaPipe is only available in an iOS device build.";
            return AsyncPoseResultStatus.Failed;
#endif
        }

        public void CancelPendingFrame()
        {
#if UNITY_IOS && !UNITY_EDITOR
            pendingTimestampMs = 0;
            if (!asyncApiAvailable || !Monitor.TryEnter(NativeLifecycleGate))
            {
                return;
            }

            try
            {
                try
                {
                    AHC_PoseCancelPending();
                }
                catch (EntryPointNotFoundException)
                {
                    asyncApiAvailable = false;
                }
            }
            finally
            {
                Monitor.Exit(NativeLifecycleGate);
            }
#endif
        }

        public bool TryRecoverFromTimeout(out string errorMessage)
        {
            errorMessage = string.Empty;
#if UNITY_IOS && !UNITY_EDITOR
            Monitor.Enter(NativeLifecycleGate);
            try
            {
                pendingTimestampMs = 0;
                if (!isReady || !asyncApiAvailable || !hasInitializationSettings)
                {
                    errorMessage = "MediaPipe timeout recovery cannot run because the last successful initialization settings are unavailable.";
                    LastError = errorMessage;
                    isReady = false;
                    return false;
                }

                try
                {
                    // A callback that never arrives leaves the live-stream graph physically
                    // busy. Cancellation only invalidates its generation, so timeout recovery
                    // must replace the graph before another frame can be accepted.
                    AHC_PoseDispose();
                    var code = InitializeNative(lastInitializationSettings);
                    isReady = code == 0;
                    asyncApiAvailable = true;
                    if (isReady)
                    {
                        LastError = string.Empty;
                        return true;
                    }

                    var nativeError = ReadLastError();
                    errorMessage = string.IsNullOrWhiteSpace(nativeError)
                        ? $"MediaPipe timeout recovery failed with native error {code}."
                        : "MediaPipe timeout recovery failed: " + nativeError;
                    LastError = errorMessage;
                    return false;
                }
                catch (EntryPointNotFoundException)
                {
                    asyncApiAvailable = false;
                    isReady = false;
                    errorMessage = BuildOutdatedBridgeMessage();
                    LastError = errorMessage;
                    return false;
                }
                catch (Exception exception)
                {
                    isReady = false;
                    errorMessage = "MediaPipe timeout recovery failed: " + exception.Message;
                    LastError = errorMessage;
                    return false;
                }
            }
            finally
            {
                Monitor.Exit(NativeLifecycleGate);
            }
#else
            errorMessage = "MediaPipe timeout recovery is only available in an iOS device build.";
            LastError = errorMessage;
            isReady = false;
            return false;
#endif
        }

        public void Dispose()
        {
            CancelPendingFrame();
#if UNITY_IOS && !UNITY_EDITOR
            if (Monitor.TryEnter(NativeLifecycleGate))
            {
                try
                {
                    try
                    {
                        AHC_PoseDispose();
                    }
                    catch (EntryPointNotFoundException)
                    {
                        // An incremental Xcode export can contain an older bridge.
                        asyncApiAvailable = false;
                    }
                }
                finally
                {
                    Monitor.Exit(NativeLifecycleGate);
                }
            }
#endif
            ReleasePinnedPixels();
            isReady = false;
        }

        /// <summary>
        /// Releases only managed resources after a background lifecycle operation.
        /// A superseding provider may already own the process-wide native graph, so
        /// a delayed cleanup must never call the global AHC_PoseDispose entry point.
        /// </summary>
        public void AbandonManagedResources()
        {
            ReleasePinnedPixels();
            isReady = false;
#if UNITY_IOS && !UNITY_EDITOR
            pendingTimestampMs = 0;
#endif
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

#if UNITY_IOS && !UNITY_EDITOR
        private static int InitializeNative(InitializationSettings settings)
        {
            return AHC_PoseInitialize(
                settings.ModelPath,
                settings.NumPoses,
                settings.MinPoseDetectionConfidence,
                settings.MinPosePresenceConfidence,
                settings.MinTrackingConfidence);
        }

        private static string BuildOutdatedBridgeMessage()
        {
            return "The Xcode project contains an outdated MediaPipe pose bridge. " +
                   "Delete the previous iOS export and build a fresh Xcode project from Unity; " +
                   "synchronous pose fallback is disabled because it can freeze the UI.";
        }
#endif

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

        private bool TryParseLatestFrame(long timestampMs, out LandmarkFrame frame)
        {
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
                LastError = string.IsNullOrWhiteSpace(frame.errorMessage)
                    ? frame.errorCode
                    : frame.errorMessage;
                return false;
            }

            LastError = string.Empty;
            return true;
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
