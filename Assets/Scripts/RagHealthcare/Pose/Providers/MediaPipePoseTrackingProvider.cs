using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;

#pragma warning disable 0414

#if AHC_USE_HOMULER_MEDIAPIPE
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using Mediapipe.Unity.Experimental;
#else
using AIHealthcareCoach.MediaPipe;
#endif

namespace Rag.Healthcare.Pose.Providers
{
    public sealed class MediaPipePoseTrackingProvider : PoseTrackingProvider
    {
        private const int ExpectedLandmarkCount = 33;

        [Header("Model")]
        [SerializeField] private string modelRelativePath = "MediaPipe/pose_landmarker_lite.task";
        [SerializeField] private TextAsset modelBytesAsset;

        [Header("MediaPipe")]
        [SerializeField] private bool useGpuDelegate = true;
        [SerializeField, Min(1)] private int numPoses = 1;
        [SerializeField, Range(0f, 1f)] private float minPoseDetectionConfidence = 0.5f;
        [SerializeField, Range(0f, 1f)] private float minPosePresenceConfidence = 0.5f;
        [SerializeField, Range(0f, 1f)] private float minTrackingConfidence = 0.5f;
        [SerializeField, Tooltip("Camera metadata is used when this value is negative.")]
        private int imageRotationDegrees = -1;
        [SerializeField] private bool mirrorXOutput;
        [SerializeField] private bool invertYOutput;

        [Header("Runtime")]
        [SerializeField, Min(1)] private int framePoolSize = 2;
        [SerializeField, Min(0.25f)] private float asyncInferenceTimeoutSeconds = 3f;

#if !AHC_USE_HOMULER_MEDIAPIPE
        [Header("Inference Input")]
        [SerializeField, Tooltip("Downscale only the pose inference input; the camera preview keeps its original resolution.")]
        private bool enableInferenceDownscale = true;
        [SerializeField, Min(16)] private int inferenceWidth = 480;
        [SerializeField, Min(16)] private int inferenceHeight = 360;
#endif

        private static readonly ProfilerMarker ReadbackMarker =
            new ProfilerMarker("AHC.Pose.GetPixels32OrFallbackReadback");
        private static readonly ProfilerMarker MetadataMarker =
            new ProfilerMarker("AHC.Pose.RotationMirrorMetadata");
        private static readonly ProfilerMarker NativeSubmitMarker =
            new ProfilerMarker("AHC.Pose.NativeSubmit");
        private static readonly ProfilerMarker ResultPollMarker =
            new ProfilerMarker("AHC.Pose.ResultPoll");
        private static readonly ProfilerMarker BuildFrameMarker =
            new ProfilerMarker("AHC.Pose.BuildFrame");

#if !AHC_USE_HOMULER_MEDIAPIPE
        [Header("Editor Python Fallback")]
        [SerializeField] private bool useEditorPythonMediaPipeFallback = true;
        [SerializeField, Min(1)] private int targetPoseFps = 8;
        [SerializeField] private string editorPythonExecutablePath = string.Empty;
        [SerializeField] private string editorPythonWorkerRelativePath = "MediaPipe/editor_pose_worker.py";
#endif

        private bool isReady;
        private bool isProcessingFrame;
        private bool requireNativeSessionReset;
        private string resolvedModelPath;
        private long lastTimestampUnixMilliseconds;

#if AHC_USE_HOMULER_MEDIAPIPE
        private PoseLandmarker poseLandmarker;
        private PoseLandmarkerResult resultBuffer;
        private TextureFramePool textureFramePool;
        private ImageProcessingOptions imageProcessingOptions;
        private int appliedImageRotationDegrees = int.MinValue;
#else
        private IPoseEstimator fallbackPoseEstimator;
        private Color32[] fallbackPixels;
        private RenderTexture inferenceRenderTexture;
        private Texture2D inferenceReadbackTexture;
        private Task<bool> fallbackInitializationTask;
        private Task<AsyncRecoveryOutcome> fallbackRecoveryTask;
        private float asyncBusyStartedAt = -1f;
        private int consecutiveAsyncRecoveryCount;

        private sealed class AsyncRecoveryOutcome
        {
            public bool Recovered;
            public string Error = string.Empty;
        }
#endif

        public override PoseTrackingBackend Backend => PoseTrackingBackend.LocalMediaPipe;
        public override bool IsReady => isReady;
        public override bool NeedsReinitialize => !IsReady || requireNativeSessionReset;
        public int DroppedFrameCount { get; private set; }
        public float LastInferenceMilliseconds { get; private set; }
        public float LastReadbackMilliseconds { get; private set; }
        public float LastMetadataMilliseconds { get; private set; }
        public float LastSubmitMilliseconds { get; private set; }
        public float LastPollMilliseconds { get; private set; }
        public float LastBuildFrameMilliseconds { get; private set; }

        private void OnDestroy()
        {
#if !AHC_USE_HOMULER_MEDIAPIPE
            ReleaseInferenceResources();
#endif
        }

        public override IEnumerator Initialize()
        {
#if !AHC_USE_HOMULER_MEDIAPIPE
            // A stopped Start coroutine does not cancel native graph creation. Reattach
            // to that task on the next Start instead of disposing an estimator that is
            // still inside MediaPipe initialization.
            if (fallbackInitializationTask != null)
            {
                yield return CompleteFallbackInitialization();
                yield break;
            }
#endif

            Dispose();
            LastError = string.Empty;
            DroppedFrameCount = 0;
            LastInferenceMilliseconds = 0f;
            LastReadbackMilliseconds = 0f;
            LastMetadataMilliseconds = 0f;
            LastSubmitMilliseconds = 0f;
            LastPollMilliseconds = 0f;
            LastBuildFrameMilliseconds = 0f;

            if (!TryResolveModel(out resolvedModelPath, out var modelBytes))
            {
                SetFailure("MediaPipe model asset is missing. Put pose_landmarker_lite.task under Assets/StreamingAssets/MediaPipe or assign a .bytes TextAsset.");
                yield break;
            }

#if AHC_USE_HOMULER_MEDIAPIPE
            try
            {
                var baseOptions = string.IsNullOrWhiteSpace(resolvedModelPath)
                    ? new BaseOptions(ResolveDelegate(), modelAssetBuffer: modelBytes)
                    : new BaseOptions(ResolveDelegate(), modelAssetPath: resolvedModelPath);

                var options = new PoseLandmarkerOptions(
                    baseOptions,
                    runningMode: RunningMode.VIDEO,
                    numPoses: Mathf.Max(1, numPoses),
                    minPoseDetectionConfidence: minPoseDetectionConfidence,
                    minPosePresenceConfidence: minPosePresenceConfidence,
                    minTrackingConfidence: minTrackingConfidence,
                    outputSegmentationMasks: false);

                poseLandmarker = PoseLandmarker.CreateFromOptions(options);
                resultBuffer = PoseLandmarkerResult.Alloc(Mathf.Max(1, numPoses), false);
                UpdateImageProcessingOptions(null);
                isReady = true;
            }
            catch (Exception exception)
            {
                SetFailure("MediaPipe provider failed to initialize: " + exception.Message);
                Dispose();
            }
#else
            if (!useEditorPythonMediaPipeFallback)
            {
                SetFailure(
                    "The platform MediaPipe provider is disabled. Enable the local MediaPipe fallback to use " +
                    "the iOS Swift bridge or Editor Python worker.");
                yield break;
            }

            var settings = new PoseEstimatorSettings
            {
                modelPath = resolvedModelPath,
                numPoses = Mathf.Max(1, numPoses),
                minPoseDetectionConfidence = minPoseDetectionConfidence,
                minPosePresenceConfidence = minPosePresenceConfidence,
                minTrackingConfidence = minTrackingConfidence,
                targetPoseFps = Mathf.Max(1, targetPoseFps),
                simulatePoseWhenNativeUnavailable = false,
                usePythonMediaPipeInEditor = true,
                editorPythonExecutablePath = editorPythonExecutablePath,
                editorPythonWorkerRelativePath = editorPythonWorkerRelativePath
            };

            fallbackPoseEstimator = PoseEstimatorFactory.Create(settings);
            if (fallbackPoseEstimator == null)
            {
                SetFailure("Pose estimator factory returned no fallback provider.");
                Dispose();
                yield break;
            }

#if UNITY_IOS && !UNITY_EDITOR
            var estimatorToInitialize = fallbackPoseEstimator;
            fallbackInitializationTask = Task.Run(() => estimatorToInitialize.Initialize(settings));
            yield return CompleteFallbackInitialization();
#else
            if (!fallbackPoseEstimator.Initialize(settings))
            {
                SetFailure("MediaPipe fallback failed to initialize: " + fallbackPoseEstimator.LastError);
                Dispose();
                yield break;
            }

            MarkFallbackInitialized();
#endif
#endif

            yield break;
        }

        public override IEnumerator EstimatePose(
            Texture source,
            long timestampUnixMilliseconds,
            Action<JointTrackingFrame> onFrame,
            Action<string> onError)
        {
            if (!isReady)
            {
                onError?.Invoke(BuildNotReadyMessage());
                yield break;
            }

            if (source == null || source.width <= 16 || source.height <= 16)
            {
                onError?.Invoke("No camera frame was provided.");
                yield break;
            }

            if (isProcessingFrame)
            {
                DroppedFrameCount++;
                onError?.Invoke("MediaPipe provider is still processing the previous frame; dropping the new frame.");
                yield break;
            }

#if AHC_USE_HOMULER_MEDIAPIPE
            isProcessingFrame = true;

            TextureFrame textureFrame = null;
            var startedAt = Time.realtimeSinceStartup;
            try
            {
                EnsureTextureFramePool(source.width, source.height);
                if (!textureFramePool.TryGetTextureFrame(out textureFrame))
                {
                    DroppedFrameCount++;
                    onError?.Invoke("MediaPipe texture frame pool is full; dropping the new frame.");
                    yield break;
                }

                yield return new WaitForEndOfFrame();

                var readbackStartedAt = Time.realtimeSinceStartupAsDouble;
                using (ReadbackMarker.Auto())
                {
                    textureFrame.ReadTextureOnCPU(source);
                }
                LastReadbackMilliseconds = ElapsedMilliseconds(readbackStartedAt);
                using var image = textureFrame.BuildCPUImage();
                textureFrame.Release();
                textureFrame = null;

                var mediaPipeTimestamp = NormalizeTimestamp(timestampUnixMilliseconds);
                var metadataStartedAt = Time.realtimeSinceStartupAsDouble;
                using (MetadataMarker.Auto())
                {
                    UpdateImageProcessingOptions(source);
                }
                LastMetadataMilliseconds = ElapsedMilliseconds(metadataStartedAt);

                bool success;
                var submitStartedAt = Time.realtimeSinceStartupAsDouble;
                using (NativeSubmitMarker.Auto())
                {
                    success = poseLandmarker.TryDetectForVideo(
                        image,
                        mediaPipeTimestamp,
                        imageProcessingOptions,
                        ref resultBuffer);
                }
                LastSubmitMilliseconds = ElapsedMilliseconds(submitStartedAt);

                if (!success)
                {
                    onError?.Invoke("Pose landmarks were not detected.");
                    yield break;
                }

                var frame = BuildFrame(resultBuffer, mediaPipeTimestamp);
                if (frame == null)
                {
                    onError?.Invoke("Pose result has an unexpected landmark count.");
                    yield break;
                }

                onFrame?.Invoke(frame);
            }
            catch (Exception exception)
            {
                SetFailure("MediaPipe frame processing failed: " + exception.Message);
                onError?.Invoke(LastError);
            }
            finally
            {
                LastInferenceMilliseconds = (Time.realtimeSinceStartup - startedAt) * 1000f;
                textureFrame?.Release();
                isProcessingFrame = false;
            }
#else
            isProcessingFrame = true;
            var startedAt = Time.realtimeSinceStartup;
            try
            {
                if (fallbackPoseEstimator is IAsyncPoseEstimator asyncEstimator &&
                    asyncEstimator.SupportsAsyncProcessing)
                {
                    yield return ProcessAsyncFallbackFrame(
                        asyncEstimator,
                        source,
                        timestampUnixMilliseconds,
                        onFrame,
                        onError);
                }
                else if (fallbackPoseEstimator is IAsyncPoseEstimator)
                {
                    isReady = false;
                    LastError = string.IsNullOrWhiteSpace(fallbackPoseEstimator.LastError)
                        ? "The asynchronous MediaPipe pose backend is unavailable. Build a fresh iOS Xcode export from Unity."
                        : fallbackPoseEstimator.LastError;
                    onError?.Invoke(LastError);
                }
                else
                {
                    ProcessAndPublishFallbackFrame(
                        source,
                        timestampUnixMilliseconds,
                        onFrame,
                        onError);
                }
            }
            finally
            {
                LastInferenceMilliseconds = (Time.realtimeSinceStartup - startedAt) * 1000f;
                isProcessingFrame = false;
            }

            yield break;
#endif
        }

        public override void Dispose()
        {
            // Capture before CancelPendingEstimate(): Stop already sets this flag so the
            // next Initialize hard-resets the native graph. Dispose-only scene handoff
            // must keep AbandonManagedResources and must not treat cancel-from-dispose
            // as a session boundary.
            var hardResetNativeSession = requireNativeSessionReset;
            CancelPendingEstimate();
#if AHC_USE_HOMULER_MEDIAPIPE
            poseLandmarker?.Close();
            poseLandmarker = null;
            textureFramePool?.Dispose();
            textureFramePool = null;
            resultBuffer = default;
            appliedImageRotationDegrees = int.MinValue;
#else
            var estimatorToDispose = fallbackPoseEstimator;
            Task activeNativeTask = fallbackInitializationTask;
            if (activeNativeTask == null)
            {
                activeNativeTask = fallbackRecoveryTask;
            }

            fallbackPoseEstimator = null;
            fallbackInitializationTask = null;
            fallbackRecoveryTask = null;
            fallbackPixels = null;
            ReleaseInferenceResources();
            asyncBusyStartedAt = -1f;
            consecutiveAsyncRecoveryCount = 0;

            if (estimatorToDispose != null)
            {
                if (activeNativeTask != null && !activeNativeTask.IsCompleted)
                {
                    activeNativeTask.ContinueWith(
                        completedTask =>
                        {
                            // Observe any background failure before releasing the
                            // estimator so it cannot surface later as an unobserved
                            // task exception during app shutdown.
                            _ = completedTask.Exception;
                            try
                            {
                                if (estimatorToDispose is IOSMediaPipePoseEstimator iosEstimator)
                                {
                                    if (hardResetNativeSession)
                                    {
                                        iosEstimator.Dispose();
                                    }
                                    else
                                    {
                                        iosEstimator.AbandonManagedResources();
                                    }
                                }
                                else
                                {
                                    estimatorToDispose.Dispose();
                                }
                            }
                            catch
                            {
                                // The Unity object is already disposing; no managed
                                // callback target remains for a teardown error.
                            }
                        },
                        TaskScheduler.Default);
                }
                else
                {
                    if (estimatorToDispose is IOSMediaPipePoseEstimator iosEstimator)
                    {
                        // Stop→Start needs AHC_PoseDispose so the next Initialize does
                        // not reuse a stuck warm graph. Scene handoff keeps Abandon.
                        if (hardResetNativeSession)
                        {
                            iosEstimator.Dispose();
                        }
                        else
                        {
                            iosEstimator.AbandonManagedResources();
                        }
                    }
                    else
                    {
                        estimatorToDispose.Dispose();
                    }
                }
            }
#endif
            isReady = false;
            isProcessingFrame = false;
            lastTimestampUnixMilliseconds = 0;
            LastInferenceMilliseconds = 0f;
        }

        public override void CancelPendingEstimate()
        {
#if !AHC_USE_HOMULER_MEDIAPIPE
            // A Stop/Start pair starts a new lifecycle budget. A transient busy
            // period from the request being drained must not inherit the previous
            // session's timeout window or consume its only recovery attempt.
            asyncBusyStartedAt = -1f;
            consecutiveAsyncRecoveryCount = 0;

            // Initialization and timeout recovery may be creating a GPU graph on a
            // worker thread. Calling into the bridge here would wait on its state lock
            // and freeze Unity, so let that operation finish naturally.
            if ((fallbackInitializationTask != null && !fallbackInitializationTask.IsCompleted) ||
                (fallbackRecoveryTask != null && !fallbackRecoveryTask.IsCompleted))
            {
                requireNativeSessionReset = true;
#if UNITY_IOS && !UNITY_EDITOR
                isReady = false;
#endif
                return;
            }

            if (fallbackPoseEstimator is IAsyncPoseEstimator asyncEstimator)
            {
                asyncEstimator.CancelPendingFrame();
            }

#if UNITY_IOS && !UNITY_EDITOR
            // Keep the managed provider warm, but force the next Start through
            // Initialize→AHC_PoseDispose so a cancelled detectAsync cannot stick
            // to the shared PoseLandmarker across sessions.
            requireNativeSessionReset = true;
            isReady = false;
#endif
#endif
        }

        private bool TryResolveModel(out string modelPath, out byte[] modelBytes)
        {
            modelPath = string.Empty;
            modelBytes = null;

            if (modelBytesAsset != null && modelBytesAsset.bytes != null && modelBytesAsset.bytes.Length > 0)
            {
                modelBytes = modelBytesAsset.bytes;
                return true;
            }

            var relativePath = string.IsNullOrWhiteSpace(modelRelativePath)
                ? "MediaPipe/pose_landmarker_lite.task"
                : modelRelativePath.Trim();

            var streamingAssetsPath = Application.streamingAssetsPath;
            if (string.IsNullOrWhiteSpace(streamingAssetsPath))
            {
                return false;
            }

            modelPath = Path.Combine(streamingAssetsPath, relativePath);
            return File.Exists(modelPath);
        }

        private string BuildNotReadyMessage()
        {
            if (!string.IsNullOrWhiteSpace(LastError))
            {
                return LastError;
            }

            return "MediaPipe provider failed to initialize.";
        }

        private long NormalizeTimestamp(long timestampUnixMilliseconds)
        {
            if (timestampUnixMilliseconds <= lastTimestampUnixMilliseconds)
            {
                timestampUnixMilliseconds = lastTimestampUnixMilliseconds + 1;
            }

            lastTimestampUnixMilliseconds = timestampUnixMilliseconds;
            return timestampUnixMilliseconds;
        }

        private static int NormalizeRotation(int rotationDegrees)
        {
            var normalized = rotationDegrees % 360;
            if (normalized < 0)
            {
                normalized += 360;
            }

            return normalized;
        }

#if !AHC_USE_HOMULER_MEDIAPIPE
        private IEnumerator CompleteFallbackInitialization()
        {
            var initializationTask = fallbackInitializationTask;
            if (initializationTask == null)
            {
                yield break;
            }

            while (!initializationTask.IsCompleted)
            {
                yield return null;
            }

            fallbackInitializationTask = null;

            bool initialized;
            try
            {
                initialized = initializationTask.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                SetFailure("MediaPipe fallback initialization failed: " + exception.GetBaseException().Message);
                Dispose();
                yield break;
            }

            if (!initialized || fallbackPoseEstimator == null || !fallbackPoseEstimator.IsReady)
            {
                var error = fallbackPoseEstimator == null
                    ? "The pose estimator was disposed while it was initializing."
                    : fallbackPoseEstimator.LastError;
                SetFailure("MediaPipe fallback failed to initialize: " + error);
                Dispose();
                yield break;
            }

            MarkFallbackInitialized();
        }

        private void MarkFallbackInitialized()
        {
            isReady = true;
            requireNativeSessionReset = false;
            LastError = string.Empty;
            asyncBusyStartedAt = -1f;
            consecutiveAsyncRecoveryCount = 0;
            DrainStaleNativeResults();
            Debug.Log("[MediaPipePoseTrackingProvider] Using " + fallbackPoseEstimator.BackendName + " fallback.");
        }

        private void DrainStaleNativeResults()
        {
            if (fallbackPoseEstimator is IOSMediaPipePoseEstimator iosEstimator)
            {
                iosEstimator.DiscardPendingResults();
            }
        }

        private IEnumerator ProcessAsyncFallbackFrame(
            IAsyncPoseEstimator asyncEstimator,
            Texture source,
            long timestampUnixMilliseconds,
            Action<JointTrackingFrame> onFrame,
            Action<string> onError)
        {
            if (fallbackPoseEstimator == null || !fallbackPoseEstimator.IsReady)
            {
                onError?.Invoke(BuildNotReadyMessage());
                yield break;
            }

            if (!TryReadFallbackPixels(
                    source,
                    out var pixels,
                    out var width,
                    out var height,
                    out var rotationAngle,
                    out var verticallyMirrored))
            {
                onError?.Invoke("MediaPipe requires a readable WebCamTexture or Texture2D frame.");
                yield break;
            }

            var mediaPipeTimestamp = NormalizeTimestamp(timestampUnixMilliseconds);
            bool submitted;
            string submitError;
            var submitStartedAt = Time.realtimeSinceStartupAsDouble;
            using (NativeSubmitMarker.Auto())
            {
                submitted = asyncEstimator.TrySubmitFrame(
                    pixels,
                    width,
                    height,
                    mediaPipeTimestamp,
                    verticallyMirrored,
                    rotationAngle,
                    out submitError);
            }
            LastSubmitMilliseconds = ElapsedMilliseconds(submitStartedAt);
            if (!submitted)
            {
                // Never call the legacy blocking iOS API here. It waits on a native
                // semaphore for up to one second per frame and starves every UI event.
                if (!asyncEstimator.SupportsAsyncProcessing)
                {
                    isReady = false;
                    LastError = string.IsNullOrWhiteSpace(submitError)
                        ? "The asynchronous MediaPipe pose bridge is unavailable. Build a fresh iOS Xcode export from Unity."
                        : submitError;
                    onError?.Invoke(LastError);
                    yield break;
                }

                if (IsNativeBusyError(submitError))
                {
                    if (asyncBusyStartedAt < 0f)
                    {
                        asyncBusyStartedAt = Time.realtimeSinceStartup;
                    }
                    else if (Time.realtimeSinceStartup - asyncBusyStartedAt >=
                             Mathf.Max(0.25f, asyncInferenceTimeoutSeconds))
                    {
                        yield return RecoverAsyncFallback(
                            asyncEstimator,
                            "MediaPipe remained busy after cancellation",
                            onError);
                        yield break;
                    }
                }
                else
                {
                    asyncBusyStartedAt = -1f;
                }

                LastError = string.IsNullOrWhiteSpace(submitError)
                    ? "MediaPipe did not accept the latest camera frame."
                    : submitError;
                onError?.Invoke(LastError);
                yield break;
            }

            asyncBusyStartedAt = -1f;

            var deadline = Time.realtimeSinceStartup + Mathf.Max(0.25f, asyncInferenceTimeoutSeconds);
            while (true)
            {
                AsyncPoseResultStatus status;
                LandmarkFrame landmarkFrame;
                string resultError;
                var pollStartedAt = Time.realtimeSinceStartupAsDouble;
                using (ResultPollMarker.Auto())
                {
                    status = asyncEstimator.TryGetLatestResult(
                        out landmarkFrame,
                        out resultError);
                }
                LastPollMilliseconds = ElapsedMilliseconds(pollStartedAt);
                if (status == AsyncPoseResultStatus.Waiting)
                {
                    if (Time.realtimeSinceStartup < deadline)
                    {
                        yield return null;
                        continue;
                    }

                    yield return RecoverAsyncFallback(
                        asyncEstimator,
                        "MediaPipe asynchronous inference timed out",
                        onError);
                    yield break;
                }

                if (status == AsyncPoseResultStatus.Failed)
                {
                    LastError = string.IsNullOrWhiteSpace(resultError)
                        ? "Pose landmarks were not detected."
                        : resultError;
                    onError?.Invoke(LastError);
                    yield break;
                }

                var frame = BuildFrame(landmarkFrame, mediaPipeTimestamp);
                if (frame == null)
                {
                    LastError = "Pose result has an unexpected landmark count.";
                    onError?.Invoke(LastError);
                    yield break;
                }

                LastError = string.Empty;
                consecutiveAsyncRecoveryCount = 0;
                onFrame?.Invoke(frame);
                yield break;
            }
        }

        private IEnumerator RecoverAsyncFallback(
            IAsyncPoseEstimator asyncEstimator,
            string reason,
            Action<string> onError)
        {
            const int maximumConsecutiveRecoveries = 2;
            if (consecutiveAsyncRecoveryCount >= maximumConsecutiveRecoveries)
            {
                requireNativeSessionReset = true;
                isReady = false;
                LastError = reason +
                            "; automatic recovery was already attempted. Stop and Start tracking to retry safely.";
                onError?.Invoke(LastError);
                yield break;
            }

            consecutiveAsyncRecoveryCount++;
            var estimator = fallbackPoseEstimator;
            fallbackRecoveryTask = Task.Run(() =>
            {
                var recovered = asyncEstimator.TryRecoverFromTimeout(out var recoveryError);
                return new AsyncRecoveryOutcome
                {
                    Recovered = recovered,
                    Error = recoveryError ?? string.Empty
                };
            });

            var recoveryTask = fallbackRecoveryTask;
            while (!recoveryTask.IsCompleted)
            {
                yield return null;
            }

            fallbackRecoveryTask = null;

            AsyncRecoveryOutcome outcome;
            try
            {
                outcome = recoveryTask.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                outcome = new AsyncRecoveryOutcome
                {
                    Recovered = false,
                    Error = exception.GetBaseException().Message
                };
            }

            isReady = outcome.Recovered && ReferenceEquals(estimator, fallbackPoseEstimator) && estimator.IsReady;
            asyncBusyStartedAt = -1f;
            if (isReady)
            {
                LastError = reason + "; the native pose graph was restarted in the background.";
            }
            else
            {
                LastError = string.IsNullOrWhiteSpace(outcome.Error)
                    ? reason + " and the native pose graph could not be restarted."
                    : reason + "; recovery failed: " + outcome.Error;
            }

            onError?.Invoke(LastError);
        }

        private static bool IsNativeBusyError(string error)
        {
            return !string.IsNullOrWhiteSpace(error) &&
                   error.IndexOf("still processing", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ProcessAndPublishFallbackFrame(
            Texture source,
            long timestampUnixMilliseconds,
            Action<JointTrackingFrame> onFrame,
            Action<string> onError)
        {
            var error = ProcessFallbackFrame(source, timestampUnixMilliseconds, out var frame);
            if (!string.IsNullOrWhiteSpace(error))
            {
                onError?.Invoke(error);
                return;
            }

            onFrame?.Invoke(frame);
        }

        private string ProcessFallbackFrame(Texture source, long timestampUnixMilliseconds, out JointTrackingFrame frame)
        {
            frame = null;

            if (fallbackPoseEstimator == null || !fallbackPoseEstimator.IsReady)
            {
                return BuildNotReadyMessage();
            }

            if (!TryReadFallbackPixels(
                    source,
                    out var pixels,
                    out var width,
                    out var height,
                    out var rotationAngle,
                    out var verticallyMirrored))
            {
                return "Editor Python MediaPipe fallback requires a readable WebCamTexture or Texture2D frame.";
            }

            try
            {
                var mediaPipeTimestamp = NormalizeTimestamp(timestampUnixMilliseconds);
                var success = fallbackPoseEstimator.TryProcessFrame(
                    pixels,
                    width,
                    height,
                    mediaPipeTimestamp,
                    verticallyMirrored,
                    rotationAngle,
                    out var landmarkFrame);

                if (!success)
                {
                    LastError = string.IsNullOrWhiteSpace(fallbackPoseEstimator.LastError)
                        ? "Pose landmarks were not detected."
                        : fallbackPoseEstimator.LastError;
                    return LastError;
                }

                frame = BuildFrame(landmarkFrame, mediaPipeTimestamp);
                if (frame == null)
                {
                    LastError = "Pose result has an unexpected landmark count.";
                    return LastError;
                }

                LastError = string.Empty;
                return string.Empty;
            }
            catch (Exception exception)
            {
                SetFailure("Editor Python MediaPipe frame processing failed: " + exception.Message);
                return LastError;
            }
        }

        private bool TryReadFallbackPixels(
            Texture source,
            out Color32[] pixels,
            out int width,
            out int height,
            out int rotationAngle,
            out bool verticallyMirrored)
        {
            pixels = null;
            width = 0;
            height = 0;
            rotationAngle = 0;
            verticallyMirrored = false;

            if (source is WebCamTexture webCamTexture)
            {
                var sourceWidth = webCamTexture.width;
                var sourceHeight = webCamTexture.height;
                if (!webCamTexture.isPlaying || sourceWidth <= 16 || sourceHeight <= 16)
                {
                    return false;
                }

                var metadataStartedAt = Time.realtimeSinceStartupAsDouble;
                using (MetadataMarker.Auto())
                {
                    rotationAngle = ResolveImageRotation(webCamTexture);
                    verticallyMirrored = webCamTexture.videoVerticallyMirrored;
                }
                LastMetadataMilliseconds = ElapsedMilliseconds(metadataStartedAt);

                if (enableInferenceDownscale)
                {
                    width = Mathf.Min(sourceWidth, Mathf.Max(16, inferenceWidth));
                    height = Mathf.Min(sourceHeight, Mathf.Max(16, inferenceHeight));
                    EnsureInferenceResources(width, height);
                }
                else
                {
                    width = sourceWidth;
                    height = sourceHeight;
                }

                var requiredLength = width * height;
                if (fallbackPixels == null || fallbackPixels.Length != requiredLength)
                {
                    fallbackPixels = new Color32[requiredLength];
                }

                var readbackStartedAt = Time.realtimeSinceStartupAsDouble;
                using (ReadbackMarker.Auto())
                {
                    if (enableInferenceDownscale)
                    {
                        var previousRenderTexture = RenderTexture.active;
                        try
                        {
                            Graphics.Blit(webCamTexture, inferenceRenderTexture);
                            RenderTexture.active = inferenceRenderTexture;
                            inferenceReadbackTexture.ReadPixels(
                                new Rect(0f, 0f, width, height),
                                0,
                                0,
                                false);
                            inferenceReadbackTexture.GetRawTextureData<Color32>().CopyTo(fallbackPixels);
                            pixels = fallbackPixels;
                        }
                        finally
                        {
                            RenderTexture.active = previousRenderTexture;
                        }
                    }
                    else
                    {
                        pixels = webCamTexture.GetPixels32(fallbackPixels);
                    }
                }
                LastReadbackMilliseconds = ElapsedMilliseconds(readbackStartedAt);
                return pixels != null && pixels.Length >= requiredLength;
            }

            if (source is Texture2D texture2D)
            {
                width = texture2D.width;
                height = texture2D.height;
                if (width <= 16 || height <= 16)
                {
                    return false;
                }

                var metadataStartedAt = Time.realtimeSinceStartupAsDouble;
                using (MetadataMarker.Auto())
                {
                    rotationAngle = ResolveImageRotation(texture2D);
                }
                LastMetadataMilliseconds = ElapsedMilliseconds(metadataStartedAt);

                var readbackStartedAt = Time.realtimeSinceStartupAsDouble;
                using (ReadbackMarker.Auto())
                {
                    pixels = texture2D.GetPixels32();
                }
                LastReadbackMilliseconds = ElapsedMilliseconds(readbackStartedAt);
                return pixels != null && pixels.Length >= width * height;
            }

            return false;
        }

        private JointTrackingFrame BuildFrame(LandmarkFrame result, long timestampUnixMilliseconds)
        {
            var startedAt = Time.realtimeSinceStartupAsDouble;
            using (BuildFrameMarker.Auto())
            {
                if (result == null || result.landmarks == null || result.landmarks.Length < ExpectedLandmarkCount)
                {
                    LastBuildFrameMilliseconds = ElapsedMilliseconds(startedAt);
                    return null;
                }

                var joints = new TrackedJoint[ExpectedLandmarkCount];
                var names = PoseJointNames.MediaPipe33;
                for (var i = 0; i < ExpectedLandmarkCount; i++)
                {
                    var landmark = result.landmarks[i];
                    var visibility = landmark.visibility > 0f ? landmark.visibility : landmark.presence;
                    if (visibility <= 0f)
                    {
                        visibility = 1f;
                    }

                    var confidence = landmark.presence > 0f ? landmark.presence : visibility;
                    var x = mirrorXOutput ? 1f - landmark.x : landmark.x;
                    var y = invertYOutput ? 1f - landmark.y : landmark.y;

                    joints[i] = new TrackedJoint
                    {
                        name = names[i],
                        x = Mathf.Clamp01(x),
                        y = Mathf.Clamp01(y),
                        z = landmark.z,
                        visibility = Mathf.Clamp01(visibility),
                        confidence = Mathf.Clamp01(confidence)
                    };
                }

                var frame = new JointTrackingFrame
                {
                    id = Guid.NewGuid().ToString("N"),
                    timestampUnixMilliseconds = timestampUnixMilliseconds,
                    joints = joints,
                    feedback = Array.Empty<PoseFeedbackMessage>()
                };
                LastBuildFrameMilliseconds = ElapsedMilliseconds(startedAt);
                return frame;
            }
        }

        private void EnsureInferenceResources(int width, int height)
        {
            if (inferenceRenderTexture != null &&
                inferenceReadbackTexture != null &&
                inferenceRenderTexture.width == width &&
                inferenceRenderTexture.height == height &&
                inferenceReadbackTexture.width == width &&
                inferenceReadbackTexture.height == height)
            {
                return;
            }

            ReleaseInferenceResources();
            inferenceRenderTexture = new RenderTexture(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default)
            {
                name = "AHC Pose Inference Downscale",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            inferenceRenderTexture.Create();
            inferenceReadbackTexture = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = "AHC Pose Inference Readback"
            };
        }

        private void ReleaseInferenceResources()
        {
            if (inferenceRenderTexture != null)
            {
                inferenceRenderTexture.Release();
                Destroy(inferenceRenderTexture);
                inferenceRenderTexture = null;
            }

            if (inferenceReadbackTexture != null)
            {
                Destroy(inferenceReadbackTexture);
                inferenceReadbackTexture = null;
            }
        }
#endif

#if AHC_USE_HOMULER_MEDIAPIPE
        private void UpdateImageProcessingOptions(Texture source)
        {
            var rotationDegrees = ResolveImageRotation(source);
            if (rotationDegrees == appliedImageRotationDegrees)
            {
                return;
            }

            imageProcessingOptions = new ImageProcessingOptions(rotationDegrees: rotationDegrees);
            appliedImageRotationDegrees = rotationDegrees;
        }

        private BaseOptions.Delegate ResolveDelegate()
        {
#if UNITY_IOS && !UNITY_EDITOR
            // Force GPU (Metal) delegate on iOS to prevent XNNPACK thread pool crashes and improve performance
            return BaseOptions.Delegate.GPU;
#else
            return useGpuDelegate ? BaseOptions.Delegate.GPU : BaseOptions.Delegate.CPU;
#endif
        }

        private void EnsureTextureFramePool(int width, int height)
        {
            if (textureFramePool != null
                && textureFramePool.textureWidth == width
                && textureFramePool.textureHeight == height)
            {
                return;
            }

            textureFramePool?.Dispose();
            textureFramePool = new TextureFramePool(
                width,
                height,
                TextureFormat.RGBA32,
                Mathf.Max(1, framePoolSize));
        }

        private JointTrackingFrame BuildFrame(PoseLandmarkerResult result, long timestampUnixMilliseconds)
        {
            var startedAt = Time.realtimeSinceStartupAsDouble;
            using (BuildFrameMarker.Auto())
            {
                if (result.poseLandmarks == null || result.poseLandmarks.Count == 0)
                {
                    LastBuildFrameMilliseconds = ElapsedMilliseconds(startedAt);
                    return null;
                }

                var landmarks = result.poseLandmarks[0].landmarks;
                if (landmarks == null || landmarks.Count != ExpectedLandmarkCount)
                {
                    LastBuildFrameMilliseconds = ElapsedMilliseconds(startedAt);
                    return null;
                }

                var joints = new TrackedJoint[ExpectedLandmarkCount];
                var names = PoseJointNames.MediaPipe33;
                for (var i = 0; i < ExpectedLandmarkCount; i++)
                {
                    var landmark = landmarks[i];
                    var visibility = landmark.visibility ?? landmark.presence ?? 1f;
                    var confidence = landmark.presence ?? landmark.visibility ?? visibility;
                    var x = mirrorXOutput ? 1f - landmark.x : landmark.x;
                    var y = invertYOutput ? 1f - landmark.y : landmark.y;

                    joints[i] = new TrackedJoint
                    {
                        name = names[i],
                        x = Mathf.Clamp01(x),
                        y = Mathf.Clamp01(y),
                        z = landmark.z,
                        visibility = Mathf.Clamp01(visibility),
                        confidence = Mathf.Clamp01(confidence)
                    };
                }

                var frame = new JointTrackingFrame
                {
                    id = Guid.NewGuid().ToString("N"),
                    timestampUnixMilliseconds = timestampUnixMilliseconds,
                    joints = joints,
                    feedback = Array.Empty<PoseFeedbackMessage>()
                };
                LastBuildFrameMilliseconds = ElapsedMilliseconds(startedAt);
                return frame;
            }
        }
#endif

        private int ResolveImageRotation(Texture source)
        {
            if (imageRotationDegrees >= 0)
            {
                return NormalizeRotation(imageRotationDegrees);
            }

            return source is WebCamTexture webCamTexture
                ? NormalizeRotation(webCamTexture.videoRotationAngle)
                : 0;
        }

        private static float ElapsedMilliseconds(double startedAt)
        {
            return (float)((Time.realtimeSinceStartupAsDouble - startedAt) * 1000d);
        }
    }
}

#pragma warning restore 0414
