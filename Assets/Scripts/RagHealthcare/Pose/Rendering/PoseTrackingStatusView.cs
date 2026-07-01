using System.Text;
using Rag.Healthcare.Camera;
using Rag.Healthcare.Pose.Providers;
using UnityEngine;
using UnityEngine.UI;

namespace Rag.Healthcare.Pose.Rendering
{
    public sealed class PoseTrackingStatusView : MonoBehaviour
    {
        [SerializeField] private JointTrackingController trackingController;
        [SerializeField] private CameraCaptureSource cameraSource;
        [SerializeField] private PoseFeedbackJsonReceiver feedbackReceiver;
        [SerializeField] private Text statusText;
        [SerializeField, Min(0.05f)] private float updateIntervalSeconds = 0.25f;

        private readonly StringBuilder builder = new StringBuilder(512);
        private float nextUpdateAt;

        private void Awake()
        {
            trackingController ??= FindFirstObjectByType<JointTrackingController>();
            cameraSource ??= FindFirstObjectByType<CameraCaptureSource>();
            feedbackReceiver ??= FindFirstObjectByType<PoseFeedbackJsonReceiver>();
            statusText ??= GetComponent<Text>();
        }

        private void Update()
        {
            if (statusText == null || Time.unscaledTime < nextUpdateAt)
            {
                return;
            }

            nextUpdateAt = Time.unscaledTime + updateIntervalSeconds;
            statusText.text = BuildStatusText();
        }

        private string BuildStatusText()
        {
            builder.Length = 0;

            if (trackingController == null)
            {
                return "Pose tracking controller: missing";
            }

            builder.Append("Backend: ").Append(trackingController.Backend).AppendLine();
            builder.Append("Tracking: ").Append(trackingController.IsTracking ? "Running" : "Stopped").AppendLine();
            builder.Append("Camera: ");
            if (cameraSource == null)
            {
                builder.AppendLine("-");
            }
            else
            {
                builder.Append(cameraSource.ActiveDeviceName)
                    .Append(" ")
                    .Append(cameraSource.FrameWidth)
                    .Append("x")
                    .Append(cameraSource.FrameHeight)
                    .AppendLine();
            }

            builder.Append("Pose FPS: ").Append(trackingController.PoseFps.ToString("0.0")).AppendLine();
            builder.Append("Inference ms: ").Append(trackingController.LastInferenceMilliseconds.ToString("0.0")).AppendLine();
            builder.Append("Frames ok/fail/drop: ")
                .Append(trackingController.SuccessfulFrameCount)
                .Append("/")
                .Append(trackingController.FailedFrameCount)
                .Append("/")
                .Append(trackingController.DroppedFrameCount)
                .AppendLine();

            var latestFrame = trackingController.LatestFrame;
            builder.Append("Landmarks: ")
                .Append(latestFrame == null || latestFrame.joints == null ? 0 : latestFrame.joints.Length)
                .AppendLine();

            if (trackingController.TrackingProvider is MediaPipePoseTrackingProvider mediaPipeProvider)
            {
                builder.Append("Provider ms/drop: ")
                    .Append(mediaPipeProvider.LastInferenceMilliseconds.ToString("0.0"))
                    .Append("/")
                    .Append(mediaPipeProvider.DroppedFrameCount)
                    .AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(trackingController.LastTrackingError))
            {
                builder.Append("Error: ").AppendLine(trackingController.LastTrackingError);
            }

            if (feedbackReceiver != null)
            {
                builder.Append("Latest Feedback: ");
                builder.AppendLine(string.IsNullOrWhiteSpace(feedbackReceiver.LatestFeedbackText)
                    ? "-"
                    : feedbackReceiver.LatestFeedbackText);
            }

            return builder.ToString();
        }
    }
}
