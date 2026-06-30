using System;
using System.Collections;
using System.IO;
using UnityEngine;

#pragma warning disable 0414

#if AHC_USE_HOMULER_MEDIAPIPE
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using Mediapipe.Unity.Experimental;
#endif

namespace Rag.Healthcare.Pose.Providers
{
    public sealed class MediaPipePoseTrackingProvider : PoseTrackingProvider
    {
        private const string PluginDefine = "AHC_USE_HOMULER_MEDIAPIPE";
        private const int ExpectedLandmarkCount = 33;

        [Header("Model")]
        [SerializeField] private string modelRelativePath = "MediaPipe/pose_landmarker_lite.task";
        [SerializeField] private TextAsset modelBytesAsset;

        [Header("MediaPipe")]
        [SerializeField] private bool useGpuDelegate;
        [SerializeField, Min(1)] private int numPoses = 1;
        [SerializeField, Range(0f, 1f)] private float minPoseDetectionConfidence = 0.5f;
        [SerializeField, Range(0f, 1f)] private float minPosePresenceConfidence = 0.5f;
        [SerializeField, Range(0f, 1f)] private float minTrackingConfidence = 0.5f;
        [SerializeField] private int imageRotationDegrees;
        [SerializeField] private bool mirrorXOutput;
        [SerializeField] private bool invertYOutput;

        [Header("Runtime")]
        [SerializeField, Min(1)] private int framePoolSize = 2;

        private bool isReady;
        private bool isProcessingFrame;
        private string resolvedModelPath;
        private long lastTimestampUnixMilliseconds;

#if AHC_USE_HOMULER_MEDIAPIPE
        private PoseLandmarker poseLandmarker;
        private PoseLandmarkerResult resultBuffer;
        private TextureFramePool textureFramePool;
        private ImageProcessingOptions imageProcessingOptions;
#endif

        public override PoseTrackingBackend Backend => PoseTrackingBackend.LocalMediaPipe;
        public override bool IsReady => isReady;
        public int DroppedFrameCount { get; private set; }
        public float LastInferenceMilliseconds { get; private set; }

        public override IEnumerator Initialize()
        {
            Dispose();
            LastError = string.Empty;
            DroppedFrameCount = 0;
            LastInferenceMilliseconds = 0f;

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
                imageProcessingOptions = new ImageProcessingOptions(rotationDegrees: NormalizeRotation(imageRotationDegrees));
                isReady = true;
            }
            catch (Exception exception)
            {
                SetFailure("MediaPipe provider failed to initialize: " + exception.Message);
                Dispose();
            }
#else
            SetFailure(
                "MediaPipe Unity Plugin package is configured, but the provider is compiled in fallback mode. " +
                $"Install/resolve com.github.homuler.mediapipe and add scripting define '{PluginDefine}'.");
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

                textureFrame.ReadTextureOnCPU(source);
                using var image = textureFrame.BuildCPUImage();
                textureFrame.Release();
                textureFrame = null;

                var mediaPipeTimestamp = NormalizeTimestamp(timestampUnixMilliseconds);
                var success = poseLandmarker.TryDetectForVideo(
                    image,
                    mediaPipeTimestamp,
                    imageProcessingOptions,
                    ref resultBuffer);

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
            onError?.Invoke(BuildNotReadyMessage());
            yield break;
#endif
        }

        public override void Dispose()
        {
#if AHC_USE_HOMULER_MEDIAPIPE
            poseLandmarker?.Close();
            poseLandmarker = null;
            textureFramePool?.Dispose();
            textureFramePool = null;
            resultBuffer = default;
#endif
            isReady = false;
            isProcessingFrame = false;
            lastTimestampUnixMilliseconds = 0;
            LastInferenceMilliseconds = 0f;
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

#if AHC_USE_HOMULER_MEDIAPIPE
        private BaseOptions.Delegate ResolveDelegate()
        {
            return useGpuDelegate ? BaseOptions.Delegate.GPU : BaseOptions.Delegate.CPU;
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
            if (result.poseLandmarks == null || result.poseLandmarks.Count == 0)
            {
                return null;
            }

            var landmarks = result.poseLandmarks[0].landmarks;
            if (landmarks == null || landmarks.Count != ExpectedLandmarkCount)
            {
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

            return new JointTrackingFrame
            {
                id = Guid.NewGuid().ToString("N"),
                timestampUnixMilliseconds = timestampUnixMilliseconds,
                joints = joints,
                feedback = Array.Empty<PoseFeedbackMessage>()
            };
        }
#endif
    }
}

#pragma warning restore 0414
