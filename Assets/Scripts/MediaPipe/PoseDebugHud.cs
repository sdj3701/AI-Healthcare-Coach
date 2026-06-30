using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AIHealthcareCoach.MediaPipe
{
    public sealed class PoseDebugHud : MonoBehaviour
    {
        private readonly StringBuilder builder = new StringBuilder(512);

        public void DrawHud(
            Rect rect,
            string status,
            string backend,
            string deviceName,
            LandmarkFrame frame,
            PoseQualityReport report,
            float cameraFps,
            float poseFps,
            float inferenceMs,
            int successfulFrames,
            int failedFrames,
            int droppedFrames,
            IReadOnlyList<PoseExerciseFeedbackMessage> feedbackMessages,
            string lastError)
        {
            builder.Length = 0;
            builder.AppendLine("MediaPipe Pose Test");
            builder.Append("Status: ").AppendLine(status);
            builder.Append("Backend: ").AppendLine(string.IsNullOrEmpty(backend) ? "-" : backend);
            builder.Append("Camera: ").AppendLine(string.IsNullOrEmpty(deviceName) ? "-" : deviceName);
            builder.Append("Camera FPS: ").Append(cameraFps.ToString("0.0")).AppendLine();
            builder.Append("Pose FPS: ").Append(poseFps.ToString("0.0")).AppendLine();
            builder.Append("Inference ms: ").Append(inferenceMs.ToString("0.0")).AppendLine();
            builder.Append("Frames ok/fail/drop: ")
                .Append(successfulFrames)
                .Append("/")
                .Append(failedFrames)
                .Append("/")
                .Append(droppedFrames)
                .AppendLine();
            builder.Append("Landmarks: ").Append(frame == null ? 0 : frame.LandmarkCount).AppendLine();

            if (report != null)
            {
                builder.Append("Avg Visibility: ").Append(report.averageVisibility.ToString("0.00")).AppendLine();
                builder.Append("Avg Presence: ").Append(report.averagePresence.ToString("0.00")).AppendLine();
                builder.Append("Quality: ").Append(report.state).Append(" (")
                    .Append(report.trackableRequiredLandmarks).Append("/")
                    .Append(report.requiredLandmarkCount).AppendLine(")");
                builder.Append("Visible Required: ").Append(report.visibleRequiredLandmarks).Append("/")
                    .Append(report.requiredLandmarkCount).AppendLine();
                builder.Append("Message: ").AppendLine(report.message);
            }

            if (frame != null)
            {
                builder.Append("Timestamp ms: ").Append(frame.timestampMs).AppendLine();
                builder.Append("Source: ").Append(frame.sourceWidth).Append("x").Append(frame.sourceHeight).AppendLine();
                builder.Append("Camera Mode: ").AppendLine(string.IsNullOrEmpty(frame.cameraMode) ? "-" : frame.cameraMode);
                builder.Append("Rotation: ").Append(frame.rotationAngle).AppendLine();
                builder.Append("Mirrored: ").Append(frame.mirrored).AppendLine();
            }

            if (!string.IsNullOrEmpty(lastError))
            {
                builder.Append("Error: ").AppendLine(lastError);
            }

            if (feedbackMessages != null && feedbackMessages.Count > 0)
            {
                builder.AppendLine("Feedback:");
                var count = Mathf.Min(3, feedbackMessages.Count);
                for (var i = 0; i < count; i++)
                {
                    var feedback = feedbackMessages[i];
                    builder.Append("- ")
                        .Append(feedback.severity)
                        .Append(": ")
                        .Append(feedback.text)
                        .Append(" (")
                        .Append(feedback.confidence.ToString("0.00"))
                        .AppendLine(")");
                }
            }

            GUI.Box(rect, builder.ToString());
        }
    }
}
