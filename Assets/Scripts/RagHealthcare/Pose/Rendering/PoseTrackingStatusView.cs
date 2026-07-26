using System.Text;
using Rag.Healthcare.Camera;
using Rag.Healthcare.Pose.Providers;
using Rag.Healthcare.Rag.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using UnityEngine.UI;

namespace Rag.Healthcare.Pose.Rendering
{
    public sealed class PoseTrackingStatusView : MonoBehaviour
    {
        [SerializeField] private JointTrackingController trackingController;
        [SerializeField] private CameraCaptureSource cameraSource;
        [SerializeField] private PoseFeedbackJsonReceiver feedbackReceiver;
        [SerializeField] private RealtimeFeedbackOrchestrator feedbackOrchestrator;
        [SerializeField] private Text statusText;
        [SerializeField, Min(0.05f)] private float updateIntervalSeconds = 0.25f;
        [SerializeField] private bool createTargetInput = true;
        [SerializeField] private InputField targetCountInput;
        [SerializeField] private Button targetCountConfirmButton;
        [SerializeField] private bool createAvatar3DPreview = true;
        [SerializeField] private PoseAvatar3DPreview avatar3DPreview;

        private readonly StringBuilder builder = new StringBuilder(512);
        private float nextUpdateAt;

        private void Awake()
        {
            trackingController ??= FindFirstObjectByType<JointTrackingController>();
            cameraSource ??= FindFirstObjectByType<CameraCaptureSource>();
            feedbackReceiver ??= FindFirstObjectByType<PoseFeedbackJsonReceiver>();
            feedbackOrchestrator ??= FindFirstObjectByType<RealtimeFeedbackOrchestrator>();
            statusText ??= GetComponent<Text>();
            EnsureTargetInput();
            EnsureAvatar3DPreview();
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

            if (feedbackOrchestrator != null)
            {
                var phaseState = feedbackOrchestrator.PhaseState;
                var trackingQuality = feedbackOrchestrator.LatestTrackingQuality;
                builder.Append("Tracking quality: ")
                    .Append(trackingQuality == null ? "-" : trackingQuality.State.ToString());
                if (trackingQuality != null && !string.IsNullOrWhiteSpace(trackingQuality.Reason))
                {
                    builder.Append(" (").Append(trackingQuality.Reason).Append(")");
                }
                builder.AppendLine();
                if (feedbackOrchestrator.IsWaitingForStandingRearm)
                {
                    builder.AppendLine("Rep ready: stand upright to start");
                }
                builder.Append("Correct reps: ")
                    .Append(feedbackOrchestrator.CorrectRepCount)
                    .Append("/")
                    .Append(feedbackOrchestrator.HasCorrectRepTarget
                        ? feedbackOrchestrator.TargetCorrectRepCount
                        : phaseState == null ? 0 : phaseState.RepCount)
                    .Append(feedbackOrchestrator.IsCorrectRepTargetComplete ? " (complete)" : string.Empty)
                    .AppendLine();
                builder.Append("Total reps: ")
                    .Append(phaseState == null ? 0 : phaseState.RepCount)
                    .AppendLine();
                builder.Append("Phase: ")
                    .Append(phaseState == null ? "Unknown" : phaseState.CurrentPhase.ToString())
                    .Append(feedbackOrchestrator.CurrentRepHasViolation ? " (needs correction)" : " (clean)")
                    .AppendLine();
                if (phaseState != null)
                {
                    builder.Append("Hip/knee depth: ")
                        .Append(phaseState.HasHipToKneeDepth
                            ? phaseState.CurrentHipToKneeDepth.ToString("+0.000;-0.000;0.000")
                            : "-")
                        .Append(" / ")
                        .Append(phaseState.RequiredHipToKneeDepth.ToString("0.000"))
                        .Append(phaseState.HasReachedHipToKneeDepthInCurrentRep
                            ? " (passed)"
                            : " (not passed)")
                        .AppendLine();
                    builder.Append("Adaptive bottom angle: ")
                        .Append(phaseState.AdaptiveBottomSampleCount > 0
                            ? phaseState.AdaptiveBottomKneeAngle.ToString("0.0") + " deg"
                            : "-")
                        .Append(" (")
                        .Append(phaseState.AdaptiveBottomSampleCount)
                        .Append("/")
                        .Append(phaseState.AdaptiveBottomSampleTarget)
                        .AppendLine(")");
                }
            }

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

        private void EnsureTargetInput()
        {
            if (!createTargetInput)
            {
                return;
            }

            EnsureEventSystem();

            if (targetCountInput != null && targetCountConfirmButton != null)
            {
                WireTargetInput();
                return;
            }

            var root = ResolveTargetInputRoot();
            if (root == null)
            {
                return;
            }

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var rowObject = new GameObject("Correct Rep Target Input", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rowObject.transform.SetParent(root, false);

            var rowRect = rowObject.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, 0f);
            rowRect.anchorMax = new Vector2(1f, 0f);
            rowRect.pivot = new Vector2(0.5f, 0f);
            rowRect.offsetMin = new Vector2(16f, 16f);
            rowRect.offsetMax = new Vector2(-16f, 54f);

            var layout = rowObject.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            targetCountInput = CreateTargetInput(rowObject.transform, font);
            targetCountConfirmButton = CreateConfirmButton(rowObject.transform, font);

            AdjustStatusTextForInput();
            WireTargetInput();
        }

        private RectTransform ResolveTargetInputRoot()
        {
            if (statusText != null && statusText.transform.parent is RectTransform statusParent)
            {
                return statusParent;
            }

            return transform as RectTransform;
        }

        private InputField CreateTargetInput(Transform parent, Font font)
        {
            var inputObject = new GameObject("Target Count Field", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            inputObject.transform.SetParent(parent, false);

            var image = inputObject.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.92f);

            var layout = inputObject.GetComponent<LayoutElement>();
            layout.flexibleWidth = 1f;
            layout.minHeight = 36f;

            var input = inputObject.GetComponent<InputField>();
            input.contentType = InputField.ContentType.IntegerNumber;
            input.characterLimit = 3;

            var placeholder = CreateInputText("Placeholder", inputObject.transform, font, "목표 개수", new Color(0.35f, 0.35f, 0.35f, 0.75f));
            var text = CreateInputText("Text", inputObject.transform, font, string.Empty, Color.black);
            input.placeholder = placeholder;
            input.textComponent = text;

            if (feedbackOrchestrator != null && feedbackOrchestrator.TargetCorrectRepCount > 0)
            {
                input.text = feedbackOrchestrator.TargetCorrectRepCount.ToString();
            }

            return input;
        }

        private Button CreateConfirmButton(Transform parent, Font font)
        {
            var buttonObject = new GameObject("Confirm Target Count", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.15f, 0.55f, 0.95f, 0.95f);

            var layout = buttonObject.GetComponent<LayoutElement>();
            layout.preferredWidth = 72f;
            layout.minHeight = 36f;

            var label = CreateInputText("Text", buttonObject.transform, font, "확인", Color.white);
            label.alignment = TextAnchor.MiddleCenter;

            return buttonObject.GetComponent<Button>();
        }

        private static Text CreateInputText(string name, Transform parent, Font font, string value, Color color)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);

            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(10f, 4f);
            rect.offsetMax = new Vector2(-10f, -4f);

            var text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = 16;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.text = value;
            return text;
        }

        private void AdjustStatusTextForInput()
        {
            if (statusText == null || !(statusText.transform is RectTransform rect))
            {
                return;
            }

            rect.offsetMin = new Vector2(rect.offsetMin.x, Mathf.Max(rect.offsetMin.y, 68f));
        }

        private void WireTargetInput()
        {
            if (targetCountConfirmButton == null)
            {
                return;
            }

            targetCountConfirmButton.onClick.RemoveListener(ApplyTargetCountFromInput);
            targetCountConfirmButton.onClick.AddListener(ApplyTargetCountFromInput);
        }

        private void ApplyTargetCountFromInput()
        {
            feedbackOrchestrator ??= FindFirstObjectByType<RealtimeFeedbackOrchestrator>();
            if (feedbackOrchestrator == null)
            {
                return;
            }

            var text = targetCountInput == null ? string.Empty : targetCountInput.text;
            var targetCount = 0;
            if (!string.IsNullOrWhiteSpace(text))
            {
                int.TryParse(text.Trim(), out targetCount);
            }

            feedbackOrchestrator.SetCorrectRepTarget(Mathf.Max(0, targetCount));
        }

        private void EnsureAvatar3DPreview()
        {
            if (!createAvatar3DPreview)
            {
                return;
            }

            avatar3DPreview ??= FindFirstObjectByType<PoseAvatar3DPreview>();
            if (avatar3DPreview == null)
            {
                avatar3DPreview = gameObject.AddComponent<PoseAvatar3DPreview>();
            }

            avatar3DPreview.Initialize(trackingController);
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            eventSystem.AddComponent<InputSystemUIInputModule>();
#else
            eventSystem.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}
