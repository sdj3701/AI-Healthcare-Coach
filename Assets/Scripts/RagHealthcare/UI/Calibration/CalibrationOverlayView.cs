using Rag.Healthcare.Pose.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rag.Healthcare.UI
{
    /// <summary>
    /// Full-screen-ish calibration guidance overlay for the workout preview (PBI-109).
    /// </summary>
    public sealed class CalibrationOverlayView
    {
        private const string DefaultReadyGuidance = "카메라 뒤로 물러서주세요. 전신이 보이도록 서 주세요.";
        private const string CountdownGuidanceFormat = "전신 감지 완료! {0}초 후 시작합니다";
        private const string PausedOutOfFrameGuidance = "전신이 화면을 벗어났어요. 다시 프레임 안으로 들어와 주세요";

        private const int ColorOverlayBg = 0x020307;
        private const int ColorGuidance = 0xE2E8F0;
        private const int ColorCountdown = 0x34D399;
        private const int ColorPaused = 0xFB923C;
        private const int ColorBarTrack = 0x334155;
        private const int ColorBarFill = 0x34D399;

        private readonly VisualElement root;
        private readonly Label guidanceLabel;
        private readonly Label countdownLabel;
        private readonly VisualElement holdTrack;
        private readonly VisualElement holdFill;

        public CalibrationOverlayView()
        {
            root = new VisualElement { name = "calibration-overlay", pickingMode = PickingMode.Ignore };
            root.style.position = Position.Absolute;
            root.style.left = 0f;
            root.style.right = 0f;
            root.style.top = 0f;
            root.style.bottom = 0f;
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.paddingLeft = 18f;
            root.style.paddingRight = 18f;
            root.style.backgroundColor = new Color(
                ((ColorOverlayBg >> 16) & 0xFF) / 255f,
                ((ColorOverlayBg >> 8) & 0xFF) / 255f,
                (ColorOverlayBg & 0xFF) / 255f,
                0.72f);
            root.style.display = DisplayStyle.None;

            guidanceLabel = CreateLabel(DefaultReadyGuidance, 13, ColorFromHex(ColorGuidance), FontStyle.Bold);
            guidanceLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            guidanceLabel.style.whiteSpace = WhiteSpace.Normal;
            guidanceLabel.style.marginBottom = 10f;
            root.Add(guidanceLabel);

            countdownLabel = CreateLabel(string.Empty, 42, ColorFromHex(ColorCountdown), FontStyle.Bold);
            countdownLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            countdownLabel.style.marginBottom = 12f;
            countdownLabel.style.display = DisplayStyle.None;
            root.Add(countdownLabel);

            holdTrack = new VisualElement { name = "calibration-hold-track", pickingMode = PickingMode.Ignore };
            holdTrack.style.width = Length.Percent(70f);
            holdTrack.style.height = 8f;
            holdTrack.style.backgroundColor = ColorFromHex(ColorBarTrack);
            holdTrack.style.borderTopLeftRadius = 8f;
            holdTrack.style.borderTopRightRadius = 8f;
            holdTrack.style.borderBottomLeftRadius = 8f;
            holdTrack.style.borderBottomRightRadius = 8f;
            holdTrack.style.overflow = Overflow.Hidden;
            holdTrack.style.display = DisplayStyle.None;

            holdFill = new VisualElement { name = "calibration-hold-fill", pickingMode = PickingMode.Ignore };
            holdFill.style.height = Length.Percent(100f);
            holdFill.style.width = Length.Percent(0f);
            holdFill.style.backgroundColor = ColorFromHex(ColorBarFill);
            holdTrack.Add(holdFill);
            root.Add(holdTrack);
        }

        public VisualElement Root => root;

        public void Bind(VisualElement parent)
        {
            if (parent == null)
            {
                return;
            }

            if (root.parent != null)
            {
                root.RemoveFromHierarchy();
            }

            parent.Add(root);
        }

        public void SetVisible(bool visible)
        {
            root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Update(WorkoutTrackingState state, FullBodyCalibrationReport calibration, float countdownRemaining)
        {
            switch (state)
            {
                case WorkoutTrackingState.InWorkout:
                    SetVisible(false);
                    return;

                case WorkoutTrackingState.CountingDown:
                    SetVisible(true);
                    var seconds = Mathf.Max(1, Mathf.CeilToInt(countdownRemaining));
                    guidanceLabel.text = string.Format(CountdownGuidanceFormat, seconds);
                    guidanceLabel.style.color = ColorFromHex(ColorCountdown);
                    countdownLabel.text = seconds.ToString();
                    countdownLabel.style.display = DisplayStyle.Flex;
                    holdTrack.style.display = DisplayStyle.None;
                    break;

                case WorkoutTrackingState.PausedOutOfFrame:
                    SetVisible(true);
                    guidanceLabel.text = PausedOutOfFrameGuidance;
                    guidanceLabel.style.color = ColorFromHex(ColorPaused);
                    countdownLabel.style.display = DisplayStyle.None;
                    holdTrack.style.display = DisplayStyle.None;
                    break;

                default:
                    SetVisible(true);
                    var reason = calibration != null && !string.IsNullOrWhiteSpace(calibration.GuidanceReason)
                        ? calibration.GuidanceReason
                        : DefaultReadyGuidance;
                    guidanceLabel.text = reason;
                    guidanceLabel.style.color = ColorFromHex(ColorGuidance);
                    countdownLabel.style.display = DisplayStyle.None;

                    var held = calibration == null ? 0f : Mathf.Max(0f, calibration.HeldSeconds);
                    var holdTarget = 1.5f;
                    var progress = Mathf.Clamp01(held / holdTarget);
                    holdFill.style.width = Length.Percent(progress * 100f);
                    holdTrack.style.display = DisplayStyle.Flex;
                    break;
            }
        }

        private static Label CreateLabel(string text, int size, Color color, FontStyle style)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = style;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static Color ColorFromHex(int rgb)
        {
            return new Color(
                ((rgb >> 16) & 0xFF) / 255f,
                ((rgb >> 8) & 0xFF) / 255f,
                (rgb & 0xFF) / 255f,
                1f);
        }
    }
}
