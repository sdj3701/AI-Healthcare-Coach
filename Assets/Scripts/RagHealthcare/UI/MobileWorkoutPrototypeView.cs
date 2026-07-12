using System;
using Rag.Healthcare.Camera;
using Rag.Healthcare.Pose;
using Rag.Healthcare.Pose.Rendering;
using Rag.Healthcare.Rag.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using UnityEngine.UI;

namespace Rag.Healthcare.UI
{
    public sealed class MobileWorkoutPrototypeView : MonoBehaviour
    {
        private enum ScreenStep
        {
            Exercise = 1,
            Target = 2,
            Session = 3
        }

        private sealed class ExerciseOption
        {
            public ExerciseOption(string id, string name, string category, float caloriesPerRep, bool supported)
            {
                Id = id;
                Name = name;
                Category = category;
                CaloriesPerRep = caloriesPerRep;
                Supported = supported;
            }

            public string Id { get; }
            public string Name { get; }
            public string Category { get; }
            public float CaloriesPerRep { get; }
            public bool Supported { get; }
        }

        private static readonly ExerciseOption[] Exercises =
        {
            new ExerciseOption("squat", "스쿼트", "하체", 0.5f, true),
            new ExerciseOption("lunge", "런지", "하체", 0.45f, false),
            new ExerciseOption("pushup", "푸시업", "상체", 0.4f, false),
            new ExerciseOption("plank", "플랭크", "맨몸", 0.3f, false)
        };

        [SerializeField] private CameraCaptureSource cameraSource;
        [SerializeField] private JointTrackingController trackingController;
        [SerializeField] private RealtimeFeedbackOrchestrator feedbackOrchestrator;
        [SerializeField] private PoseFeedbackJsonReceiver feedbackReceiver;
        [SerializeField] private PoseJsonReplayPlayer replayPlayer;
        [SerializeField] private bool hideLegacyDebugView = true;
        [SerializeField] private bool hideGeneratedDesktopCanvas = true;
        [SerializeField, Min(0.05f)] private float refreshIntervalSeconds = 0.2f;

        private Canvas canvas;
        private RectTransform contentRoot;
        private Text stepLabel;
        private Text titleLabel;
        private Text statusLabel;
        private Text timerLabel;
        private Text counterLabel;
        private Text targetLabel;
        private Text phaseLabel;
        private Text feedbackLabel;
        private Text cameraStateLabel;
        private Text replayStateLabel;
        private Text poseFpsLabel;
        private RawImage previewImage;
        private GameObject previewPlaceholder;
        private RectTransform progressRoot;
        private InputField repsInput;
        private InputField setsInput;

        private ScreenStep currentStep = ScreenStep.Exercise;
        private string selectedExerciseId = "squat";
        private string selectedCategory = "하체";
        private int repsPerSet = 15;
        private int sets = 3;
        private bool workoutRunning;
        private bool replayMode;
        private float sessionStartedAt;
        private float elapsedBeforePause;
        private float nextRefreshAt;
        private Font uiFont;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<MobileWorkoutPrototypeView>() != null)
            {
                return;
            }

            if (FindFirstObjectByType<CameraCaptureSource>() == null ||
                FindFirstObjectByType<JointTrackingController>() == null)
            {
                return;
            }

            var viewObject = new GameObject("Mobile Workout Prototype View");
            viewObject.AddComponent<MobileWorkoutPrototypeView>();
        }

        private void Awake()
        {
            ResolveReferences();
            EnsureEventSystem();
            uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            BuildUi();
            ApplyCompatibilityVisibility();
            ApplyTargetCount();
            RenderCurrentStep();
        }

        private void OnEnable()
        {
            if (cameraSource != null)
            {
                cameraSource.PreviewTextureChanged += HandlePreviewTextureChanged;
            }
        }

        private void OnDisable()
        {
            if (cameraSource != null)
            {
                cameraSource.PreviewTextureChanged -= HandlePreviewTextureChanged;
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefreshAt)
            {
                return;
            }

            nextRefreshAt = Time.unscaledTime + refreshIntervalSeconds;
            RefreshDynamicText();
            RefreshPreviewTexture();
        }

        private void ResolveReferences()
        {
            cameraSource ??= FindFirstObjectByType<CameraCaptureSource>();
            trackingController ??= FindFirstObjectByType<JointTrackingController>();
            feedbackOrchestrator ??= FindFirstObjectByType<RealtimeFeedbackOrchestrator>();
            feedbackReceiver ??= FindFirstObjectByType<PoseFeedbackJsonReceiver>();
            replayPlayer ??= FindFirstObjectByType<PoseJsonReplayPlayer>();

            if (replayPlayer == null)
            {
                replayPlayer = gameObject.AddComponent<PoseJsonReplayPlayer>();
            }
        }

        private void BuildUi()
        {
            var canvasObject = new GameObject("Mobile Coach Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(390f, 844f);
            scaler.matchWidthOrHeight = 0.5f;

            var background = CreateImage(canvasObject.transform, "Background", new Color(0.035f, 0.045f, 0.06f, 1f));
            Stretch(background.rectTransform);

            var phone = CreateImage(canvasObject.transform, "Phone Frame", new Color(0.075f, 0.09f, 0.115f, 1f));
            var phoneRect = phone.rectTransform;
            phoneRect.anchorMin = new Vector2(0.5f, 0.5f);
            phoneRect.anchorMax = new Vector2(0.5f, 0.5f);
            phoneRect.pivot = new Vector2(0.5f, 0.5f);
            phoneRect.sizeDelta = new Vector2(374f, 812f);
            phoneRect.anchoredPosition = Vector2.zero;

            var screen = CreateImage(phone.transform, "Phone Screen", new Color(0.025f, 0.035f, 0.05f, 1f));
            var screenRect = screen.rectTransform;
            Stretch(screenRect);
            screenRect.offsetMin = new Vector2(12f, 12f);
            screenRect.offsetMax = new Vector2(-12f, -12f);

            var layout = screen.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 18, 12);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            BuildStatusBar(screen.transform);
            BuildTitle(screen.transform);
            BuildProgress(screen.transform);

            var contentObject = new GameObject("Step Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            contentObject.transform.SetParent(screen.transform, false);
            contentRoot = contentObject.GetComponent<RectTransform>();

            var contentLayout = contentObject.GetComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 8f;
            contentLayout.padding = new RectOffset(0, 0, 0, 0);
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = false;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            var contentElement = contentObject.GetComponent<LayoutElement>();
            contentElement.flexibleHeight = 1f;
        }

        private void BuildStatusBar(Transform parent)
        {
            var row = CreateRow(parent, "Status Bar", 24f, 0f);
            row.padding = new RectOffset(8, 8, 0, 0);
            row.childAlignment = TextAnchor.MiddleCenter;

            statusLabel = CreateText(row.transform, "Status", "19:44  5G", 11, new Color(0.68f, 0.75f, 0.84f, 1f), TextAnchor.MiddleLeft);
            statusLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var battery = CreateText(row.transform, "Battery", "배터리 82%", 10, new Color(0.25f, 0.9f, 0.62f, 1f), TextAnchor.MiddleRight);
            battery.gameObject.AddComponent<LayoutElement>().preferredWidth = 84f;
        }

        private void BuildTitle(Transform parent)
        {
            var panel = CreateImage(parent, "Title Panel", new Color(0.045f, 0.06f, 0.08f, 0.95f));
            panel.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 3, 3);
            layout.spacing = 0f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            titleLabel = CreateText(panel.transform, "Title", "AI 헬스케어 코치", 16, Color.white, TextAnchor.MiddleLeft);
            titleLabel.fontStyle = FontStyle.Bold;
            titleLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

            var subtitle = CreateText(panel.transform, "Subtitle", "운동 선택부터 자세 추적, 리플레이까지 한 화면 흐름으로 확인합니다.", 10, new Color(0.56f, 0.63f, 0.72f, 1f), TextAnchor.MiddleLeft);
            subtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
        }

        private void BuildProgress(Transform parent)
        {
            var rowObject = new GameObject("Step Progress", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowObject.transform.SetParent(parent, false);
            progressRoot = rowObject.GetComponent<RectTransform>();

            var row = rowObject.GetComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(8, 8, 0, 0);
            row.spacing = 5f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = false;
            row.childControlHeight = false;

            rowObject.GetComponent<LayoutElement>().preferredHeight = 26f;

            for (var i = 1; i <= 3; i++)
            {
                var dot = CreateImage(progressRoot, "Step " + i, new Color(0.18f, 0.22f, 0.3f, 1f));
                var element = dot.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = i == 1 ? 44f : 18f;
                element.preferredHeight = 8f;
            }

            stepLabel = CreateText(progressRoot, "Step Label", "STEP 1 / 3", 11, new Color(0.25f, 0.9f, 0.62f, 1f), TextAnchor.MiddleRight);
            stepLabel.fontStyle = FontStyle.Bold;
            var labelElement = stepLabel.gameObject.AddComponent<LayoutElement>();
            labelElement.flexibleWidth = 1f;
            labelElement.preferredHeight = 18f;
        }

        private void RenderCurrentStep()
        {
            ClearContent();
            UpdateProgress();

            switch (currentStep)
            {
                case ScreenStep.Exercise:
                    RenderExerciseStep();
                    break;
                case ScreenStep.Target:
                    RenderTargetStep();
                    break;
                case ScreenStep.Session:
                    RenderSessionStep();
                    break;
            }

            RefreshDynamicText();
            RefreshPreviewTexture();
        }

        private void RenderExerciseStep()
        {
            AddHeader("운동 선택", "현재 자세 판별은 스쿼트를 기준으로 동작합니다.");

            var selected = GetSelectedExercise();
            var selectedPanel = CreateCard(contentRoot, "Selected Exercise", 74f);
            var selectedLayout = selectedPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            selectedLayout.padding = new RectOffset(12, 12, 8, 8);
            selectedLayout.spacing = 3f;

            var selectedTitle = CreateText(selectedPanel.transform, "Selected Title", "선택한 운동", 10, new Color(0.55f, 0.63f, 0.73f, 1f), TextAnchor.MiddleLeft);
            selectedTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

            var selectedName = CreateText(selectedPanel.transform, "Selected Name", selected.Name + "  " + (selected.Supported ? "자세 추적 지원" : "준비 중"), 17, Color.white, TextAnchor.MiddleLeft);
            selectedName.fontStyle = FontStyle.Bold;
            selectedName.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            var categoryRow = CreateRow(contentRoot, "Category Row", 48f, 6f);
            CreateCategoryButton(categoryRow.transform, "하체");
            CreateCategoryButton(categoryRow.transform, "상체");
            CreateCategoryButton(categoryRow.transform, "맨몸");

            var list = CreateCard(contentRoot, "Exercise List", 238f);
            var listLayout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            listLayout.padding = new RectOffset(10, 10, 8, 8);
            listLayout.spacing = 7f;

            foreach (var exercise in Exercises)
            {
                if (exercise.Category != selectedCategory)
                {
                    continue;
                }

                CreateExerciseButton(list.transform, exercise);
            }

            AddSpacer(8f);
            CreateButton(contentRoot, "다음: 목표 횟수 설정", new Color(0.12f, 0.82f, 0.5f, 1f), new Color(0.02f, 0.04f, 0.05f, 1f), 48f, () =>
            {
                currentStep = ScreenStep.Target;
                RenderCurrentStep();
            });
        }

        private void RenderTargetStep()
        {
            AddHeader("목표 설정", "반복 횟수와 세트를 정하면 정확한 자세 카운트 목표가 적용됩니다.");

            var selected = GetSelectedExercise();
            var card = CreateCard(contentRoot, "Target Card", 280f);
            var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.spacing = 9f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;

            var name = CreateText(card.transform, "Exercise Name", selected.Name, 20, Color.white, TextAnchor.MiddleLeft);
            name.fontStyle = FontStyle.Bold;
            name.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

            repsInput = CreateNumberInput(card.transform, "반복 횟수", repsPerSet);
            setsInput = CreateNumberInput(card.transform, "세트 수", sets);

            targetLabel = CreateText(card.transform, "Target Summary", string.Empty, 13, new Color(0.25f, 0.9f, 0.62f, 1f), TextAnchor.MiddleLeft);
            targetLabel.fontStyle = FontStyle.Bold;
            targetLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;

            var totalCalorie = CreateText(card.transform, "Calorie Summary", string.Empty, 11, new Color(0.86f, 0.52f, 0.52f, 1f), TextAnchor.MiddleLeft);
            totalCalorie.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
            totalCalorie.text = "예상 소모: " + (repsPerSet * sets * selected.CaloriesPerRep).ToString("0.0") + " kcal";

            var buttonRow = CreateRow(contentRoot, "Target Buttons", 50f, 8f);
            CreateButton(buttonRow.transform, "이전", new Color(0.1f, 0.13f, 0.18f, 1f), Color.white, 50f, () =>
            {
                currentStep = ScreenStep.Exercise;
                RenderCurrentStep();
            });
            CreateButton(buttonRow.transform, "운동 화면으로", new Color(0.12f, 0.82f, 0.5f, 1f), new Color(0.02f, 0.04f, 0.05f, 1f), 50f, () =>
            {
                ApplyTargetCountFromInputs();
                currentStep = ScreenStep.Session;
                RenderCurrentStep();
            });

            RefreshTargetSummary();
        }

        private void RenderSessionStep()
        {
            AddHeader("운동 세션", "Start로 카메라 추적을 시작하고 Stop으로 JSON 기반 3D 리플레이를 확인합니다.");

            var hud = CreateCard(contentRoot, "Session HUD", 64f);
            var hudRow = hud.gameObject.AddComponent<HorizontalLayoutGroup>();
            hudRow.padding = new RectOffset(10, 10, 6, 6);
            hudRow.spacing = 8f;
            hudRow.childControlWidth = true;
            hudRow.childControlHeight = true;
            hudRow.childForceExpandWidth = true;

            phaseLabel = CreateText(hud.transform, "Phase", "Phase: -", 11, Color.white, TextAnchor.MiddleLeft);
            phaseLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            timerLabel = CreateText(hud.transform, "Timer", "00:00", 16, new Color(0.25f, 0.9f, 0.62f, 1f), TextAnchor.MiddleRight);
            timerLabel.fontStyle = FontStyle.Bold;
            timerLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 80f;

            BuildPreviewPanel();
            BuildMetricRow();

            feedbackLabel = CreateText(contentRoot, "Latest Feedback", "최근 피드백: -", 10, new Color(0.62f, 0.69f, 0.78f, 1f), TextAnchor.MiddleLeft);
            feedbackLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

            var utilityRow = CreateRow(contentRoot, "Camera Utilities", 38f, 6f);
            CreateButton(utilityRow.transform, "카메라 전환", new Color(0.1f, 0.13f, 0.18f, 1f), Color.white, 38f, SwitchCamera);
            CreateButton(utilityRow.transform, "목표 수정", new Color(0.1f, 0.13f, 0.18f, 1f), Color.white, 38f, () =>
            {
                StopWorkoutOnly();
                currentStep = ScreenStep.Target;
                RenderCurrentStep();
            });

            var controlRow = CreateRow(contentRoot, "Session Controls", 54f, 8f);
            CreateButton(controlRow.transform, "START", new Color(0.12f, 0.82f, 0.5f, 1f), new Color(0.02f, 0.04f, 0.05f, 1f), 54f, StartWorkout);
            CreateButton(controlRow.transform, "STOP", new Color(0.82f, 0.18f, 0.25f, 1f), Color.white, 54f, StopWorkoutAndReplay);

            var resetRow = CreateRow(contentRoot, "Reset Row", 30f, 6f);
            CreateButton(resetRow.transform, "리셋", new Color(0.07f, 0.09f, 0.13f, 1f), new Color(0.62f, 0.69f, 0.78f, 1f), 30f, ResetSession);
            CreateButton(resetRow.transform, "운동 선택", new Color(0.07f, 0.09f, 0.13f, 1f), new Color(0.62f, 0.69f, 0.78f, 1f), 30f, () =>
            {
                StopWorkoutOnly();
                currentStep = ScreenStep.Exercise;
                RenderCurrentStep();
            });
        }

        private void BuildPreviewPanel()
        {
            var panel = CreateCard(contentRoot, "Camera Preview Panel", 270f);
            panel.color = new Color(0.005f, 0.007f, 0.012f, 1f);

            previewImage = panel.gameObject.AddComponent<RawImage>();
            previewImage.color = Color.white;

            var overlay = new GameObject("Pose Overlay", typeof(RectTransform), typeof(PoseSkeletonRenderer));
            overlay.transform.SetParent(panel.transform, false);
            Stretch(overlay.GetComponent<RectTransform>());

            var label = CreateText(panel.transform, "Preview Label", "AI SKELETON DETECTING", 10, new Color(0.25f, 0.9f, 0.62f, 1f), TextAnchor.UpperLeft);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.offsetMin = new Vector2(12f, -34f);
            labelRect.offsetMax = new Vector2(-12f, -8f);

            previewPlaceholder = new GameObject("Preview Placeholder", typeof(RectTransform), typeof(Image));
            previewPlaceholder.transform.SetParent(panel.transform, false);
            Stretch(previewPlaceholder.GetComponent<RectTransform>());
            previewPlaceholder.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.045f, 0.92f);

            var placeholderLayout = previewPlaceholder.AddComponent<VerticalLayoutGroup>();
            placeholderLayout.padding = new RectOffset(18, 18, 82, 26);
            placeholderLayout.spacing = 8f;
            placeholderLayout.childAlignment = TextAnchor.MiddleCenter;
            placeholderLayout.childControlWidth = true;
            placeholderLayout.childControlHeight = false;

            cameraStateLabel = CreateText(previewPlaceholder.transform, "Camera State", "Start를 누르면 카메라 추적이 시작됩니다.", 13, Color.white, TextAnchor.MiddleCenter);
            cameraStateLabel.fontStyle = FontStyle.Bold;
            cameraStateLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;

            replayStateLabel = CreateText(previewPlaceholder.transform, "Replay State", "Stop을 누르면 저장 JSON 기반 3D 리플레이가 표시됩니다.", 10, new Color(0.62f, 0.69f, 0.78f, 1f), TextAnchor.MiddleCenter);
            replayStateLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
        }

        private void BuildMetricRow()
        {
            var row = CreateRow(contentRoot, "Metric Row", 62f, 6f);
            counterLabel = CreateMetric(row.transform, "정확한 자세", "0");
            targetLabel = CreateMetric(row.transform, "목표", GetTargetCount().ToString());
            poseFpsLabel = CreateMetric(row.transform, "Pose FPS", "0.0");
        }

        private Text CreateMetric(Transform parent, string caption, string value)
        {
            var card = CreateImage(parent, caption, new Color(0.055f, 0.075f, 0.105f, 1f));
            var element = card.gameObject.AddComponent<LayoutElement>();
            element.flexibleWidth = 1f;
            element.preferredHeight = 62f;

            var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 6, 5);
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;

            var top = CreateText(card.transform, caption + " Caption", caption, 9, new Color(0.48f, 0.55f, 0.65f, 1f), TextAnchor.MiddleCenter);
            top.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

            var bottom = CreateText(card.transform, caption + " Value", value, 17, new Color(0.25f, 0.9f, 0.62f, 1f), TextAnchor.MiddleCenter);
            bottom.fontStyle = FontStyle.Bold;
            bottom.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
            return bottom;
        }

        private void AddHeader(string title, string body)
        {
            var header = CreateImage(contentRoot, "Step Header", new Color(0f, 0f, 0f, 0f));
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 54f;
            var layout = header.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 2f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;

            var heading = CreateText(header.transform, "Heading", title, 18, Color.white, TextAnchor.MiddleLeft);
            heading.fontStyle = FontStyle.Bold;
            heading.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

            var description = CreateText(header.transform, "Description", body, 10, new Color(0.56f, 0.63f, 0.72f, 1f), TextAnchor.UpperLeft);
            description.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
        }

        private void CreateCategoryButton(Transform parent, string category)
        {
            var selected = selectedCategory == category;
            CreateButton(
                parent,
                category,
                selected ? new Color(0.12f, 0.82f, 0.5f, 1f) : new Color(0.08f, 0.105f, 0.145f, 1f),
                selected ? new Color(0.02f, 0.04f, 0.05f, 1f) : Color.white,
                48f,
                () =>
                {
                    selectedCategory = category;
                    RenderCurrentStep();
                });
        }

        private void CreateExerciseButton(Transform parent, ExerciseOption exercise)
        {
            var selected = selectedExerciseId == exercise.Id;
            var label = exercise.Name + "   " + exercise.Category + "   " + (exercise.Supported ? "지원" : "준비 중");
            var button = CreateButton(
                parent,
                label,
                selected ? new Color(0.07f, 0.35f, 0.24f, 1f) : new Color(0.08f, 0.105f, 0.145f, 1f),
                selected ? new Color(0.55f, 1f, 0.78f, 1f) : new Color(0.72f, 0.78f, 0.86f, 1f),
                44f,
                () =>
                {
                    if (!exercise.Supported)
                    {
                        return;
                    }

                    selectedExerciseId = exercise.Id;
                    RenderCurrentStep();
                });

            button.interactable = exercise.Supported;
        }

        private InputField CreateNumberInput(Transform parent, string label, int value)
        {
            var row = CreateRow(parent, label + " Row", 56f, 8f);
            var labelText = CreateText(row.transform, label, label, 12, new Color(0.72f, 0.78f, 0.86f, 1f), TextAnchor.MiddleLeft);
            labelText.fontStyle = FontStyle.Bold;
            labelText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var minus = CreateButton(row.transform, "-", new Color(0.1f, 0.13f, 0.18f, 1f), Color.white, 38f, () => AdjustInput(label, -1));
            minus.gameObject.GetComponent<LayoutElement>().preferredWidth = 42f;

            var inputObject = new GameObject(label + " Input", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            inputObject.transform.SetParent(row.transform, false);
            inputObject.GetComponent<Image>().color = new Color(0.92f, 0.96f, 1f, 1f);

            var element = inputObject.GetComponent<LayoutElement>();
            element.preferredWidth = 64f;
            element.preferredHeight = 38f;

            var input = inputObject.GetComponent<InputField>();
            input.contentType = InputField.ContentType.IntegerNumber;
            input.characterLimit = 3;

            var text = CreateText(inputObject.transform, "Text", value.ToString(), 17, new Color(0.02f, 0.04f, 0.05f, 1f), TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
            text.fontStyle = FontStyle.Bold;

            var placeholder = CreateText(inputObject.transform, "Placeholder", "0", 17, new Color(0.3f, 0.35f, 0.42f, 0.6f), TextAnchor.MiddleCenter);
            Stretch(placeholder.rectTransform);

            input.textComponent = text;
            input.placeholder = placeholder;
            input.text = value.ToString();
            input.onEndEdit.AddListener(_ => ApplyTargetCountFromInputs());

            var plus = CreateButton(row.transform, "+", new Color(0.1f, 0.13f, 0.18f, 1f), Color.white, 38f, () => AdjustInput(label, 1));
            plus.gameObject.GetComponent<LayoutElement>().preferredWidth = 42f;
            return input;
        }

        private Button CreateButton(Transform parent, string label, Color background, Color textColor, float height, Action onClick)
        {
            var buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);

            var image = buttonObject.GetComponent<Image>();
            image.color = background;

            var element = buttonObject.GetComponent<LayoutElement>();
            element.preferredHeight = height;
            element.flexibleWidth = 1f;

            var button = buttonObject.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = background;
            colors.highlightedColor = Color.Lerp(background, Color.white, 0.08f);
            colors.pressedColor = Color.Lerp(background, Color.black, 0.15f);
            colors.disabledColor = new Color(background.r, background.g, background.b, 0.45f);
            button.colors = colors;

            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }

            var text = CreateText(buttonObject.transform, "Text", label, 12, textColor, TextAnchor.MiddleCenter);
            text.fontStyle = FontStyle.Bold;
            Stretch(text.rectTransform);
            return button;
        }

        private HorizontalLayoutGroup CreateRow(Transform parent, string name, float height, float spacing)
        {
            var rowObject = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowObject.transform.SetParent(parent, false);
            rowObject.GetComponent<LayoutElement>().preferredHeight = height;

            var row = rowObject.GetComponent<HorizontalLayoutGroup>();
            row.spacing = spacing;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;
            return row;
        }

        private Image CreateCard(Transform parent, string name, float height)
        {
            var image = CreateImage(parent, name, new Color(0.055f, 0.075f, 0.105f, 1f));
            image.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return image;
        }

        private Image CreateImage(Transform parent, string name, Color color)
        {
            var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            var image = imageObject.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private Text CreateText(Transform parent, string name, string value, int fontSize, Color color, TextAnchor alignment)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);

            var text = textObject.GetComponent<Text>();
            text.font = uiFont;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.text = value;
            return text;
        }

        private void AddSpacer(float height)
        {
            var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(contentRoot, false);
            spacer.GetComponent<LayoutElement>().preferredHeight = height;
        }

        private void ClearContent()
        {
            previewImage = null;
            previewPlaceholder = null;
            timerLabel = null;
            counterLabel = null;
            targetLabel = null;
            phaseLabel = null;
            feedbackLabel = null;
            cameraStateLabel = null;
            replayStateLabel = null;
            poseFpsLabel = null;
            repsInput = null;
            setsInput = null;

            for (var i = contentRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(contentRoot.GetChild(i).gameObject);
            }
        }

        private void UpdateProgress()
        {
            var step = (int)currentStep;
            stepLabel.text = "STEP " + step + " / 3";

            for (var i = 0; i < 3; i++)
            {
                var image = progressRoot.GetChild(i).GetComponent<Image>();
                var element = progressRoot.GetChild(i).GetComponent<LayoutElement>();
                var active = i + 1 == step;
                image.color = active ? new Color(0.25f, 0.9f, 0.62f, 1f) : new Color(0.18f, 0.22f, 0.3f, 1f);
                element.preferredWidth = active ? 44f : 18f;
            }
        }

        private void RefreshDynamicText()
        {
            var now = workoutRunning ? Time.unscaledTime - sessionStartedAt + elapsedBeforePause : elapsedBeforePause;
            if (timerLabel != null)
            {
                var seconds = Mathf.FloorToInt(Mathf.Max(0f, now));
                timerLabel.text = (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
            }

            if (counterLabel != null && feedbackOrchestrator != null)
            {
                counterLabel.text = feedbackOrchestrator.CorrectRepCount.ToString();
            }

            if (targetLabel != null)
            {
                targetLabel.text = GetTargetCount().ToString();
            }

            if (phaseLabel != null)
            {
                var phase = feedbackOrchestrator == null || feedbackOrchestrator.PhaseState == null
                    ? "Unknown"
                    : feedbackOrchestrator.PhaseState.CurrentPhase.ToString();
                var status = feedbackOrchestrator != null && feedbackOrchestrator.CurrentRepHasViolation ? "교정 필요" : "정상";
                phaseLabel.text = GetSelectedExercise().Name + "\nPhase: " + phase + " / " + status;
            }

            if (feedbackLabel != null)
            {
                var feedback = feedbackReceiver == null || string.IsNullOrWhiteSpace(feedbackReceiver.LatestFeedbackText)
                    ? "-"
                    : feedbackReceiver.LatestFeedbackText;
                feedbackLabel.text = "최근 피드백: " + Trim(feedback, 62);
            }

            if (poseFpsLabel != null)
            {
                poseFpsLabel.text = trackingController == null ? "0.0" : trackingController.PoseFps.ToString("0.0");
            }

            RefreshTargetSummary();
        }

        private void RefreshTargetSummary()
        {
            if (targetLabel == null || currentStep != ScreenStep.Target)
            {
                return;
            }

            var targetCount = Mathf.Max(1, repsPerSet) * Mathf.Max(1, sets);
            targetLabel.text = "목표 정확 자세 카운트: " + targetCount + "개";
        }

        private void RefreshPreviewTexture()
        {
            if (previewImage == null)
            {
                return;
            }

            Texture texture = null;
            if (cameraSource != null && (cameraSource.IsRunning || cameraSource.IsStarting))
            {
                texture = cameraSource.PreviewTexture;
                replayMode = false;
            }
            else if (replayPlayer != null && replayPlayer.LoadedFrameCount > 0)
            {
                texture = replayPlayer.PreviewTexture;
                replayMode = true;
            }

            previewImage.texture = texture;
            previewImage.enabled = texture != null;

            if (previewPlaceholder != null)
            {
                previewPlaceholder.SetActive(texture == null);
            }

            if (cameraStateLabel != null)
            {
                cameraStateLabel.text = BuildCameraStateText();
            }

            if (replayStateLabel != null)
            {
                replayStateLabel.text = BuildReplayStateText();
            }
        }

        private void HandlePreviewTextureChanged(Texture texture)
        {
            RefreshPreviewTexture();
        }

        private void StartWorkout()
        {
            ApplyTargetCount();
            replayMode = false;
            replayPlayer?.StopReplay();
            cameraSource?.StartCamera();
            trackingController?.StartTracking();

            if (!workoutRunning)
            {
                sessionStartedAt = Time.unscaledTime;
            }

            workoutRunning = true;
            RefreshPreviewTexture();
        }

        private void StopWorkoutAndReplay()
        {
            StopWorkoutOnly();
            cameraSource?.StopCamera();
            replayPlayer?.PlayLatestSession();
            replayMode = true;
            RefreshPreviewTexture();
        }

        private void StopWorkoutOnly()
        {
            if (workoutRunning)
            {
                elapsedBeforePause += Time.unscaledTime - sessionStartedAt;
            }

            workoutRunning = false;
            trackingController?.StopTracking();
        }

        private void SwitchCamera()
        {
            replayMode = false;
            replayPlayer?.StopReplay();

            var shouldResumeTracking = workoutRunning || (trackingController != null && trackingController.IsTracking);
            trackingController?.StopTracking();
            cameraSource?.StopCamera();
            cameraSource?.TogglePreferredCameraFacing();
            cameraSource?.StartCamera();

            if (shouldResumeTracking)
            {
                trackingController?.StartTracking();
                workoutRunning = true;
                sessionStartedAt = Time.unscaledTime;
            }
        }

        private void ResetSession()
        {
            StopWorkoutOnly();
            cameraSource?.StopCamera();
            replayPlayer?.StopReplay();
            elapsedBeforePause = 0f;
            replayMode = false;
            ApplyTargetCount();
            RenderCurrentStep();
        }

        private void ApplyTargetCountFromInputs()
        {
            repsPerSet = ReadInput(repsInput, repsPerSet);
            sets = ReadInput(setsInput, sets);
            ApplyTargetCount();
            RefreshDynamicText();
        }

        private void AdjustInput(string label, int delta)
        {
            var input = label.Contains("세트") ? setsInput : repsInput;
            if (input == null)
            {
                return;
            }

            var value = ReadInput(input, label.Contains("세트") ? sets : repsPerSet);
            value = Mathf.Clamp(value + delta, 1, 999);
            input.text = value.ToString();
            ApplyTargetCountFromInputs();
        }

        private void ApplyTargetCount()
        {
            feedbackOrchestrator ??= FindFirstObjectByType<RealtimeFeedbackOrchestrator>();
            feedbackOrchestrator?.SetCorrectRepTarget(GetTargetCount());
        }

        private int GetTargetCount()
        {
            return Mathf.Max(1, repsPerSet) * Mathf.Max(1, sets);
        }

        private ExerciseOption GetSelectedExercise()
        {
            foreach (var exercise in Exercises)
            {
                if (exercise.Id == selectedExerciseId)
                {
                    return exercise;
                }
            }

            return Exercises[0];
        }

        private string BuildCameraStateText()
        {
            if (cameraSource == null)
            {
                return "카메라 소스가 없습니다.";
            }

            if (cameraSource.IsStarting)
            {
                return "카메라 시작 중입니다.";
            }

            if (cameraSource.IsRunning)
            {
                return "카메라 추적 중입니다.";
            }

            if (replayMode)
            {
                return "3D 리플레이 준비 중입니다.";
            }

            return "Start를 누르면 카메라 추적이 시작됩니다.";
        }

        private string BuildReplayStateText()
        {
            if (replayPlayer == null)
            {
                return "리플레이 플레이어가 없습니다.";
            }

            if (replayPlayer.LoadedFrameCount > 0)
            {
                return "Replay: " + (replayPlayer.IsPlaying ? "Playing" : replayPlayer.LastReplayStatus) +
                       " / frames: " + replayPlayer.LoadedFrameCount;
            }

            return "Stop을 누르면 저장 JSON 기반 3D 리플레이가 표시됩니다.";
        }

        private void ApplyCompatibilityVisibility()
        {
            if (hideLegacyDebugView)
            {
                var debugView = FindFirstObjectByType<CameraPreviewDebugView>();
                if (debugView != null)
                {
                    debugView.enabled = false;
                }
            }

            if (!hideGeneratedDesktopCanvas)
            {
                return;
            }

            var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (var candidate in canvases)
            {
                if (candidate == canvas || candidate.gameObject.name != "Coach Canvas")
                {
                    continue;
                }

                candidate.enabled = false;
                var raycaster = candidate.GetComponent<GraphicRaycaster>();
                if (raycaster != null)
                {
                    raycaster.enabled = false;
                }
            }
        }

        private static int ReadInput(InputField input, int fallback)
        {
            if (input == null || string.IsNullOrWhiteSpace(input.text))
            {
                return Mathf.Max(1, fallback);
            }

            return int.TryParse(input.text.Trim(), out var value) ? Mathf.Clamp(value, 1, 999) : Mathf.Max(1, fallback);
        }

        private static string Trim(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            return value.Length <= maxLength ? value : value.Substring(0, maxLength - 3) + "...";
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
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
