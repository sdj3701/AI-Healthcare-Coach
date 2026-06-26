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
            string lastError)
        {
            builder.Length = 0;
            builder.AppendLine("MediaPipe Pose Test");
            builder.Append("Status: ").AppendLine(status);
            builder.Append("Backend: ").AppendLine(string.IsNullOrEmpty(backend) ? "-" : backend);
            builder.Append("Camera: ").AppendLine(string.IsNullOrEmpty(deviceName) ? "-" : deviceName);
            builder.Append("Camera FPS: ").Append(cameraFps.ToString("0.0")).AppendLine();
            builder.Append("Pose FPS: ").Append(poseFps.ToString("0.0")).AppendLine();
            builder.Append("Landmarks: ").Append(frame == null ? 0 : frame.LandmarkCount).AppendLine();

            if (report != null)
            {
                builder.Append("Avg Visibility: ").Append(report.averageVisibility.ToString("0.00")).AppendLine();
                builder.Append("Quality: ").Append(report.state).Append(" (")
                    .Append(report.visibleRequiredLandmarks).Append("/")
                    .Append(report.requiredLandmarkCount).AppendLine(")");
                builder.Append("Message: ").AppendLine(report.message);
            }

            if (frame != null)
            {
                builder.Append("Rotation: ").Append(frame.rotationAngle).AppendLine();
                builder.Append("Mirrored: ").Append(frame.mirrored).AppendLine();
            }

            if (!string.IsNullOrEmpty(lastError))
            {
                builder.Append("Error: ").AppendLine(lastError);
            }

            GUI.Box(rect, builder.ToString());
        }
    }
}
