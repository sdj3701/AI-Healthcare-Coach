using System.Collections;
using System.IO;
using UnityEngine;

namespace AIHealthcareCoach.MediaPipe
{
    public sealed class MediaPipeTestRunner : MonoBehaviour
    {
        [Header("Pose Sampling")]
        [SerializeField] private int targetPoseFps = 15;
        [SerializeField] private bool simulatePoseWhenNativeUnavailable = true;

        [Header("MediaPipe")]
        [SerializeField] private string modelRelativePath = "MediaPipe/pose_landmarker_lite.task";
        [SerializeField] private float minPoseDetectionConfidence = 0.5f;
        [SerializeField] private float minPosePresenceConfidence = 0.5f;
        [SerializeField] private float minTrackingConfidence = 0.5f;
        [SerializeField] private float requiredVisibility = 0.45f;
        [SerializeField] private float averageVisibilityThreshold = 0.55f;

        private CameraPreviewController cameraPreview;
        private PoseOverlayRenderer overlayRenderer;
        private PoseDebugHud debugHud;
        private IPoseEstimator poseEstimator;
        private PoseQualityGate qualityGate;
        private PoseQualityReport qualityReport;
        private LandmarkFrame latestFrame;
        private Color32[] pixelBuffer;
        private string status = "Stopped";
        private bool isStartingCamera;
        private float nextPoseSampleAt;
        private float poseFpsWindowStartedAt;
        private int poseFrameCount;
        private float poseFps;
        private string lastError;

        private void Awake()
        {
            cameraPreview = GetComponent<CameraPreviewController>();
            overlayRenderer = GetComponent<PoseOverlayRenderer>();
            debugHud = GetComponent<PoseDebugHud>();
            qualityGate = new PoseQualityGate(requiredVisibility, averageVisibilityThreshold);
            qualityReport = qualityGate.Evaluate(false, null);

            if (cameraPreview == null)
            {
                cameraPreview = gameObject.AddComponent<CameraPreviewController>();
            }

            if (overlayRenderer == null)
            {
                overlayRenderer = gameObject.AddComponent<PoseOverlayRenderer>();
            }

            if (debugHud == null)
            {
                debugHud = gameObject.AddComponent<PoseDebugHud>();
            }
        }

        private void Update()
        {
            cameraPreview.TickFps();

            if (!cameraPreview.IsRunning || !cameraPreview.DidUpdateThisFrame)
            {
                qualityReport = qualityGate.Evaluate(cameraPreview.IsRunning, latestFrame);
                return;
            }

            if (Time.unscaledTime < nextPoseSampleAt)
            {
                return;
            }

            nextPoseSampleAt = Time.unscaledTime + 1f / Mathf.Max(1f, targetPoseFps);
            ProcessPoseSample();
        }

        private void ProcessPoseSample()
        {
            if (poseEstimator == null || !poseEstimator.IsReady)
            {
                lastError = poseEstimator == null ? "Pose estimator has not been created." : poseEstimator.LastError;
                latestFrame = LandmarkFrame.Empty(CurrentTimestampMs(), "POSE_BACKEND_NOT_READY", lastError);
                qualityReport = qualityGate.Evaluate(cameraPreview.IsRunning, latestFrame);
                return;
            }

            pixelBuffer = cameraPreview.GetPixels(pixelBuffer);
            if (pixelBuffer == null || pixelBuffer.Length == 0)
            {
                latestFrame = LandmarkFrame.Empty(CurrentTimestampMs(), "CAMERA_FRAME_EMPTY", "Camera frame is not ready yet.");
                qualityReport = qualityGate.Evaluate(cameraPreview.IsRunning, latestFrame);
                return;
            }

            var timestampMs = CurrentTimestampMs();
            LandmarkFrame frame;
            var success = poseEstimator.TryProcessFrame(
                pixelBuffer,
                cameraPreview.Width,
                cameraPreview.Height,
                timestampMs,
                cameraPreview.IsDisplayMirrored,
                cameraPreview.RotationAngle,
                out frame);

            latestFrame = frame;
            if (latestFrame != null)
            {
                latestFrame.cameraFps = cameraPreview.CameraFps;
                latestFrame.poseFps = poseFps;
            }

            if (success)
            {
                CountPoseFrame();
                status = "Running";
                lastError = string.Empty;
            }
            else
            {
                lastError = poseEstimator.LastError;
                status = string.IsNullOrEmpty(lastError) ? "Running without pose" : "Pose error";
            }

            qualityReport = qualityGate.Evaluate(cameraPreview.IsRunning, latestFrame);
        }

        private void OnGUI()
        {
            DrawToolbar();

            var previewRect = CalculatePreviewRect();
            cameraPreview.DrawPreview(previewRect);

            if (latestFrame != null)
            {
                overlayRenderer.DrawOverlay(previewRect, latestFrame, cameraPreview.IsDisplayMirrored);
            }

            var hudRect = new Rect(12f, Screen.height - 212f, Mathf.Min(520f, Screen.width - 24f), 200f);
            debugHud.DrawHud(
                hudRect,
                status,
                poseEstimator == null ? string.Empty : poseEstimator.BackendName,
                cameraPreview.ActiveDeviceName,
                latestFrame,
                qualityReport,
                cameraPreview.CameraFps,
                poseFps,
                BuildVisibleError());
        }

        private void DrawToolbar()
        {
            GUI.Box(new Rect(8f, 8f, Screen.width - 16f, 54f), string.Empty);

            GUI.enabled = !cameraPreview.IsRunning && !isStartingCamera;
            if (GUI.Button(new Rect(18f, 20f, 120f, 30f), "Start Camera"))
            {
                StartCoroutine(StartCameraAndPose());
            }

            GUI.enabled = cameraPreview.IsRunning || isStartingCamera;
            if (GUI.Button(new Rect(148f, 20f, 120f, 30f), "Stop Camera"))
            {
                StopCameraAndPose();
            }

            GUI.enabled = true;
            GUI.Label(new Rect(284f, 24f, Screen.width - 300f, 24f), status);
        }

        private IEnumerator StartCameraAndPose()
        {
            isStartingCamera = true;
            status = "Starting camera";
            lastError = string.Empty;

            yield return StartCoroutine(cameraPreview.StartCamera());

            isStartingCamera = false;

            if (!cameraPreview.IsRunning)
            {
                status = "Camera failed";
                lastError = cameraPreview.LastError;
                qualityReport = qualityGate.Evaluate(false, null);
                yield break;
            }

            CreatePoseEstimator();
            nextPoseSampleAt = Time.unscaledTime;
            poseFpsWindowStartedAt = Time.unscaledTime;
            poseFrameCount = 0;
            poseFps = 0f;
            status = "Running";
        }

        private void StopCameraAndPose()
        {
            StopAllCoroutines();
            isStartingCamera = false;
            cameraPreview.StopCamera();

            if (poseEstimator != null)
            {
                poseEstimator.Dispose();
                poseEstimator = null;
            }

            pixelBuffer = null;
            latestFrame = null;
            poseFps = 0f;
            lastError = string.Empty;
            status = "Stopped";
            qualityReport = qualityGate.Evaluate(false, null);
        }

        private void CreatePoseEstimator()
        {
            if (poseEstimator != null)
            {
                poseEstimator.Dispose();
            }

            poseEstimator = PoseEstimatorFactory.Create(simulatePoseWhenNativeUnavailable);
            var settings = new PoseEstimatorSettings
            {
                modelPath = Path.Combine(Application.streamingAssetsPath, modelRelativePath),
                numPoses = 1,
                minPoseDetectionConfidence = minPoseDetectionConfidence,
                minPosePresenceConfidence = minPosePresenceConfidence,
                minTrackingConfidence = minTrackingConfidence,
                targetPoseFps = targetPoseFps,
                simulatePoseWhenNativeUnavailable = simulatePoseWhenNativeUnavailable
            };

            if (!poseEstimator.Initialize(settings))
            {
                lastError = poseEstimator.LastError;
                status = "Pose backend failed";
            }
        }

        private Rect CalculatePreviewRect()
        {
            var top = 74f;
            var bottom = 228f;
            var availableWidth = Mathf.Max(240f, Screen.width - 24f);
            var availableHeight = Mathf.Max(160f, Screen.height - top - bottom);
            var aspect = 4f / 3f;

            if (cameraPreview.Width > 16 && cameraPreview.Height > 16)
            {
                var width = cameraPreview.Width;
                var height = cameraPreview.Height;
                if (cameraPreview.RotationAngle == 90 || cameraPreview.RotationAngle == 270)
                {
                    var temp = width;
                    width = height;
                    height = temp;
                }

                aspect = width / (float)height;
            }

            var rectWidth = availableWidth;
            var rectHeight = rectWidth / aspect;
            if (rectHeight > availableHeight)
            {
                rectHeight = availableHeight;
                rectWidth = rectHeight * aspect;
            }

            return new Rect(
                (Screen.width - rectWidth) * 0.5f,
                top + (availableHeight - rectHeight) * 0.5f,
                rectWidth,
                rectHeight);
        }

        private string BuildVisibleError()
        {
            if (!string.IsNullOrEmpty(lastError))
            {
                return lastError;
            }

            if (!string.IsNullOrEmpty(cameraPreview.LastError))
            {
                return cameraPreview.LastError;
            }

            if (latestFrame != null && !string.IsNullOrEmpty(latestFrame.errorMessage))
            {
                return latestFrame.errorMessage;
            }

            return string.Empty;
        }

        private void CountPoseFrame()
        {
            poseFrameCount++;
            var elapsed = Time.unscaledTime - poseFpsWindowStartedAt;
            if (elapsed < 1f)
            {
                return;
            }

            poseFps = poseFrameCount / elapsed;
            poseFrameCount = 0;
            poseFpsWindowStartedAt = Time.unscaledTime;
        }

        private static long CurrentTimestampMs()
        {
            return (long)(Time.realtimeSinceStartup * 1000f);
        }

        private void OnDestroy()
        {
            StopCameraAndPose();
        }
    }
}
