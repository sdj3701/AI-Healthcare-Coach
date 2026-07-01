using Rag.Healthcare.Pose;
using UnityEngine;

namespace Rag.Healthcare.Camera
{
    public sealed class CameraPreviewDebugView : MonoBehaviour
    {
        private readonly struct OverlaySegment
        {
            public OverlaySegment(string from, string to)
            {
                From = from;
                To = to;
            }

            public string From { get; }
            public string To { get; }
        }

        private static readonly OverlaySegment[] OverlaySegments =
        {
            new OverlaySegment(PoseJointNames.LeftShoulder, PoseJointNames.RightShoulder),
            new OverlaySegment(PoseJointNames.LeftHip, PoseJointNames.RightHip),
            new OverlaySegment(PoseJointNames.LeftShoulder, PoseJointNames.LeftElbow),
            new OverlaySegment(PoseJointNames.LeftElbow, PoseJointNames.LeftWrist),
            new OverlaySegment(PoseJointNames.RightShoulder, PoseJointNames.RightElbow),
            new OverlaySegment(PoseJointNames.RightElbow, PoseJointNames.RightWrist),
            new OverlaySegment(PoseJointNames.LeftHip, PoseJointNames.LeftKnee),
            new OverlaySegment(PoseJointNames.LeftKnee, PoseJointNames.LeftAnkle),
            new OverlaySegment(PoseJointNames.RightHip, PoseJointNames.RightKnee),
            new OverlaySegment(PoseJointNames.RightKnee, PoseJointNames.RightAnkle),
            new OverlaySegment(PoseJointNames.LeftShoulder, PoseJointNames.LeftHip),
            new OverlaySegment(PoseJointNames.RightShoulder, PoseJointNames.RightHip)
        };

        [SerializeField] private CameraCaptureSource cameraSource;
        [SerializeField] private JointTrackingController trackingController;
        [SerializeField] private PoseFeedbackJsonReceiver feedbackReceiver;
        [SerializeField] private bool showPreview = true;
        [SerializeField] private bool showHud = true;
        [SerializeField, Range(0f, 1f)] private float minimumOverlayConfidence = 0.35f;
        [SerializeField] private float jointSize = 8f;
        [SerializeField] private float boneThickness = 4f;

        private readonly Color leftJointColor = new Color(0.18f, 0.62f, 1f, 0.95f);
        private readonly Color rightJointColor = new Color(1f, 0.45f, 0.18f, 0.95f);
        private readonly Color centerJointColor = new Color(0.95f, 0.95f, 0.95f, 0.95f);
        private readonly Color boneColor = new Color(0.15f, 0.95f, 0.62f, 0.85f);

        private void Awake()
        {
            cameraSource ??= FindFirstObjectByType<CameraCaptureSource>();
            trackingController ??= FindFirstObjectByType<JointTrackingController>();
            feedbackReceiver ??= FindFirstObjectByType<PoseFeedbackJsonReceiver>();
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
            DrawPoseOverlay(rect);
        }

        private void DrawHud()
        {
            var rect = new Rect(12f, Screen.height - 256f, Mathf.Min(820f, Screen.width - 24f), 244f);
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
            var latestFeedback = feedbackReceiver == null ? string.Empty : feedbackReceiver.LatestFeedbackText;
            var feedbackAge = feedbackReceiver == null || feedbackReceiver.LatestFeedbackTime < 0f
                ? string.Empty
                : $" ({Time.unscaledTime - feedbackReceiver.LatestFeedbackTime:0.0}s ago)";

            return
                "TestRagSysten Camera / RAG Status\n" +
                $"Camera: {(cameraSource.IsRunning ? "Running" : cameraSource.IsStarting ? "Starting" : "Stopped")} {cameraSource.ActiveDeviceName} {cameraSource.FrameWidth}x{cameraSource.FrameHeight}\n" +
                $"Camera Error: {cameraSource.LastError}\n" +
                $"Tracking: {(trackingController != null && trackingController.IsTracking ? "Running" : "Stopped")}\n" +
                $"Pose FPS: {(trackingController == null ? 0f : trackingController.PoseFps):0.0}\n" +
                $"Inference ms: {(trackingController == null ? 0f : trackingController.LastInferenceMilliseconds):0.0}\n" +
                $"Frames ok/fail/drop: {(trackingController == null ? 0 : trackingController.SuccessfulFrameCount)}/{(trackingController == null ? 0 : trackingController.FailedFrameCount)}/{(trackingController == null ? 0 : trackingController.DroppedFrameCount)}\n" +
                $"Landmarks: {jointCount}\n" +
                $"Latest Feedback: {TrimForHud(latestFeedback, 92)}{feedbackAge}\n" +
                $"Tracking Error: {(trackingController == null ? string.Empty : trackingController.LastTrackingError)}";
        }

        private void DrawPoseOverlay(Rect previewRect)
        {
            var frame = trackingController == null ? null : trackingController.LatestFrame;
            if (frame == null || frame.joints == null || frame.joints.Length == 0)
            {
                return;
            }

            foreach (var segment in OverlaySegments)
            {
                if (!TryGetRenderableJoint(frame, segment.From, out var from) ||
                    !TryGetRenderableJoint(frame, segment.To, out var to))
                {
                    continue;
                }

                DrawLine(ToPreviewPoint(previewRect, from), ToPreviewPoint(previewRect, to), boneColor, boneThickness);
            }

            foreach (var joint in frame.joints)
            {
                if (!CanRender(joint))
                {
                    continue;
                }

                DrawJoint(ToPreviewPoint(previewRect, joint), GetJointColor(joint.name), jointSize);
            }
        }

        private bool TryGetRenderableJoint(JointTrackingFrame frame, string jointName, out TrackedJoint joint)
        {
            return frame.TryGetJoint(jointName, out joint) && CanRender(joint);
        }

        private bool CanRender(TrackedJoint joint)
        {
            if (joint == null)
            {
                return false;
            }

            var score = Mathf.Max(joint.confidence, joint.visibility);
            return score >= minimumOverlayConfidence &&
                   joint.x >= -0.1f && joint.x <= 1.1f &&
                   joint.y >= -0.1f && joint.y <= 1.1f;
        }

        private static Vector2 ToPreviewPoint(Rect previewRect, TrackedJoint joint)
        {
            return new Vector2(
                previewRect.xMin + Mathf.Clamp01(joint.x) * previewRect.width,
                previewRect.yMin + Mathf.Clamp01(joint.y) * previewRect.height);
        }

        private void DrawJoint(Vector2 center, Color color, float size)
        {
            var previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private static void DrawLine(Vector2 start, Vector2 end, Color color, float thickness)
        {
            var delta = end - start;
            var length = delta.magnitude;
            if (length <= Mathf.Epsilon)
            {
                return;
            }

            var previousMatrix = GUI.matrix;
            var previousColor = GUI.color;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
            GUI.DrawTexture(new Rect(start.x, start.y - thickness * 0.5f, length, thickness), Texture2D.whiteTexture);
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        private Color GetJointColor(string jointName)
        {
            if (jointName.StartsWith("left_"))
            {
                return leftJointColor;
            }

            if (jointName.StartsWith("right_"))
            {
                return rightJointColor;
            }

            return centerJointColor;
        }

        private static string TrimForHud(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            return value.Length <= maxLength ? value : value.Substring(0, maxLength - 3) + "...";
        }
    }
}
