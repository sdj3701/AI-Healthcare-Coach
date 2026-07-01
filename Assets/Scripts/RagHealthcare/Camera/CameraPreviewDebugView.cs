using Rag.Healthcare.Pose;
using UnityEngine;

namespace Rag.Healthcare.Camera
{
    public sealed class CameraPreviewDebugView : MonoBehaviour
    {
        [SerializeField] private CameraCaptureSource cameraSource;
        [SerializeField] private JointTrackingController trackingController;
        [SerializeField] private bool showPreview = true;
        [SerializeField] private bool showHud = true;

        private void Awake()
        {
            cameraSource ??= FindFirstObjectByType<CameraCaptureSource>();
            trackingController ??= FindFirstObjectByType<JointTrackingController>();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (showPreview)
            {
                DrawPreview();
            }

            if (showHud)
            {
                DrawHud();
            }
        }

        private void DrawToolbar()
        {
            GUI.Box(new Rect(8f, 8f, Screen.width - 16f, 54f), string.Empty);

            GUI.enabled = cameraSource != null && !cameraSource.IsRunning && !cameraSource.IsStarting;
            if (GUI.Button(new Rect(18f, 20f, 120f, 30f), "Start Camera"))
            {
                cameraSource.StartCamera();
                trackingController?.StartTracking();
            }

            GUI.enabled = cameraSource != null && (cameraSource.IsRunning || cameraSource.IsStarting);
            if (GUI.Button(new Rect(148f, 20f, 120f, 30f), "Stop Camera"))
            {
                trackingController?.StopTracking();
                cameraSource.StopCamera();
            }

            GUI.enabled = cameraSource != null && !cameraSource.IsStarting;
            if (GUI.Button(new Rect(278f, 20f, 130f, 30f), "Switch Camera"))
            {
                var wasTracking = trackingController != null && trackingController.IsTracking;
                trackingController?.StopTracking();
                cameraSource.StopCamera();
                cameraSource.TogglePreferredCameraFacing();
                cameraSource.StartCamera();
                if (wasTracking)
                {
                    trackingController?.StartTracking();
                }
            }

            GUI.enabled = true;
            GUI.Label(new Rect(424f, 24f, Screen.width - 440f, 24f), BuildToolbarStatus());
        }

        private void DrawPreview()
        {
            var rect = CalculatePreviewRect();
            var texture = cameraSource == null ? null : cameraSource.PreviewTexture;
            if (texture == null)
            {
                GUI.Box(rect, "Camera preview");
                return;
            }

            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, false);
        }

        private void DrawHud()
        {
            var rect = new Rect(12f, Screen.height - 220f, Mathf.Min(760f, Screen.width - 24f), 208f);
            GUI.Box(rect, BuildHudText());
        }

        private Rect CalculatePreviewRect()
        {
            const float top = 74f;
            const float bottom = 248f;
            var availableWidth = Mathf.Max(240f, Screen.width - 24f);
            var availableHeight = Mathf.Max(160f, Screen.height - top - bottom);
            var aspect = 16f / 9f;

            if (cameraSource != null && cameraSource.FrameWidth > 16 && cameraSource.FrameHeight > 16)
            {
                aspect = cameraSource.FrameWidth / (float)cameraSource.FrameHeight;
            }

            var width = availableWidth;
            var height = width / aspect;
            if (height > availableHeight)
            {
                height = availableHeight;
                width = height * aspect;
            }

            return new Rect((Screen.width - width) * 0.5f, top + (availableHeight - height) * 0.5f, width, height);
        }

        private string BuildToolbarStatus()
        {
            if (cameraSource == null)
            {
                return "CameraCaptureSource missing";
            }

            var state = cameraSource.IsRunning ? "Running" : cameraSource.IsStarting ? "Starting" : "Stopped";
            var facing = cameraSource.ActiveCameraIsFrontFacing ? "front" : "rear/unknown";
            var preferred = cameraSource.PreferFrontCamera ? "front" : "rear";
            return $"{state} / active: {facing} / preferred: {preferred}";
        }

        private string BuildHudText()
        {
            if (cameraSource == null)
            {
                return "CameraCaptureSource missing";
            }

            var latestFrame = trackingController == null ? null : trackingController.LatestFrame;
            var jointCount = latestFrame == null || latestFrame.joints == null ? 0 : latestFrame.joints.Length;

            return
                "TestRagSysten Camera / RAG Status\n" +
                $"Camera: {(cameraSource.IsRunning ? "Running" : cameraSource.IsStarting ? "Starting" : "Stopped")} {cameraSource.ActiveDeviceName} {cameraSource.FrameWidth}x{cameraSource.FrameHeight}\n" +
                $"Camera Error: {cameraSource.LastError}\n" +
                $"Tracking: {(trackingController != null && trackingController.IsTracking ? "Running" : "Stopped")}\n" +
                $"Pose FPS: {(trackingController == null ? 0f : trackingController.PoseFps):0.0}\n" +
                $"Inference ms: {(trackingController == null ? 0f : trackingController.LastInferenceMilliseconds):0.0}\n" +
                $"Frames ok/fail/drop: {(trackingController == null ? 0 : trackingController.SuccessfulFrameCount)}/{(trackingController == null ? 0 : trackingController.FailedFrameCount)}/{(trackingController == null ? 0 : trackingController.DroppedFrameCount)}\n" +
                $"Landmarks: {jointCount}\n" +
                $"Tracking Error: {(trackingController == null ? string.Empty : trackingController.LastTrackingError)}";
        }
    }
}
