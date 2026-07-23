using System;
using System.Collections;
using Rag.Healthcare.Diagnostics;
using Rag.Healthcare.Camera;
using Rag.Healthcare.Performance;
using Rag.Healthcare.Pose;
using Rag.Healthcare.Pose.Rendering;
using Rag.Healthcare.Pose.Session;
using Rag.Healthcare.Product;
using Rag.Healthcare.Rag.Runtime;
using Rag.Healthcare.Tts;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Rag.Healthcare.UI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class MobileWorkoutPrototypeView : MonoBehaviour
    {
        private const string PanelSettingsResourcePath = "UI/MobileWorkoutPanelSettings";
        private const string RuntimeThemeResourcePath = "UI/UnityDefaultRuntimeTheme";
        private const string BundledKoreanFontResourcePath = "Fonts/NotoSansKR-Regular";
        private const float BaseHorizontalPadding = 16f;
        private const float BaseTopPadding = 12f;
        private const float BaseBottomPadding = 14f;

        private static readonly string[] KoreanFontFallbacks =
        {
#if UNITY_ANDROID
            "Noto Sans CJK KR", "Noto Sans KR", "Droid Sans Fallback", "sans-serif"
#elif UNITY_IOS
            "Apple SD Gothic Neo", "Noto Sans KR", "Arial Unicode MS"
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            "Apple SD Gothic Neo", "Noto Sans KR", "Arial Unicode MS"
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            "Malgun Gothic", "맑은 고딕", "Noto Sans KR", "Arial Unicode MS"
#else
            "Noto Sans KR", "Noto Sans CJK KR", "Arial Unicode MS"
#endif
        };

        private enum ScreenStep
        {
            Profile = 1,
            Calibration = 2,
            Exercise = 3,
            Target = 4,
            Session = 5
        }

        private enum PreviewMode
        {
            None,
            Camera,
            Replay
        }

        private enum SessionTransitionKind
        {
            None,
            Starting,
            Stopping,
            SwitchingCamera,
            Resetting,
            Leaving
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
            new ExerciseOption("legpress", "레그 프레스", "하체", 0.4f, false),
            new ExerciseOption("deadlift", "데드리프트", "하체", 0.7f, false),
            new ExerciseOption("pushup", "푸시업", "상체", 0.4f, false),
            new ExerciseOption("pullup", "풀업", "상체", 0.6f, false),
            new ExerciseOption("plank", "플랭크", "맨몸", 0.3f, false),
            new ExerciseOption("burpee", "버피 테스트", "맨몸", 0.8f, false)
        };

        [SerializeField] private CameraCaptureSource cameraSource;
        [SerializeField] private JointTrackingController trackingController;
        [SerializeField] private RealtimeFeedbackOrchestrator feedbackOrchestrator;
        [SerializeField] private PoseFeedbackJsonReceiver feedbackReceiver;
        [SerializeField] private CoachTtsController coachTts;
        [SerializeField] private PoseJsonReplayPlayer replayPlayer;
        [SerializeField] private bool hideLegacyDebugView = true;
        [SerializeField] private bool hideGeneratedDesktopCanvas = true;
        [SerializeField] private bool showUiInEditMode = true;
        [SerializeField, Min(0.05f)] private float refreshIntervalSeconds = 0.2f;

        [Header("Mobile Performance")]
        [SerializeField] private bool applyMobilePerformanceDefaults = true;
        [SerializeField, Min(16)] private int mobileCameraWidth = 640;
        [SerializeField, Min(16)] private int mobileCameraHeight = 480;
        [SerializeField, Range(1, 60)] private int mobileCameraFps = 20;
        [SerializeField, Range(1, 30)] private int mobilePoseFps = 12;
        [SerializeField, Range(15, 120)] private int mobileTargetFrameRate = 30;
        [SerializeField, Min(1f)] private float trackingStartupTimeoutSeconds = 15f;

        [Header("Performance Bench")]
        [SerializeField] private DevicePerformanceProfiler performanceProfiler;
        [SerializeField] private bool showPerformanceBenchControls = true;

        private UIDocument document;
        private PanelSettings runtimePanelSettings;
        private Font runtimeFont;
        private bool ownsRuntimeFont;
        private bool mobilePerformanceConfigured;
        private VisualElement root;
        private VisualElement screenRoot;
        private ScrollView contentScroll;
        private VisualElement contentRoot;
        private VisualElement[] progressPips;
        private VisualElement previewFrame;
        private Image previewImage;
        private VisualElement poseOverlay;
        private VisualElement previewPlaceholder;
        private Label stepLabel;
        private Label timerLabel;
        private Label correctCountLabel;
        private Label targetCountLabel;
        private Label poseFpsLabel;
        private Label phaseLabel;
        private Label feedbackLabel;
        private Label cameraStateLabel;
        private Label replayStateLabel;
        private Label performanceBenchStatusLabel;
        private TextField repsField;
        private TextField setsField;
        private CalibrationOverlayView calibrationOverlay;
        private OnboardingStatusManager profileStatus;
        private bool showingProfileOnboarding;
        private bool hasCompletedCalibrationThisLaunch;
        private bool calibrationFlowRunning;
        private bool calibrationSucceededThisFlow;
        private Label calibrationStatusLabel;
        private Button calibrationContinueButton;
        private Coroutine calibrationFlowCoroutine;
        private string performanceBenchStatusText = "Perf bench: idle";
        private bool performanceBenchSubscribed;

        private ScreenStep currentStep = ScreenStep.Profile;
        private string selectedExerciseId = "squat";
        private string selectedCategory = "하체";
        private int repsPerSet = 15;
        private int sets = 3;
        private bool workoutRunning;
        private PreviewMode previewMode = PreviewMode.None;
        private float sessionStartedAt;
        private float elapsedBeforePause;
        private float nextRefreshAt;
        private Rect lastSafeArea = new Rect(-1f, -1f, -1f, -1f);
        private int lastScreenWidth = -1;
        private int lastScreenHeight = -1;
        private Coroutine sessionTransitionCoroutine;
        private SessionTransitionKind sessionTransitionKind;
        private bool pendingWorkoutStart;
        private bool pendingWorkoutStop;
        private bool currentSessionReceivedPoseFrame;
        private string cameraOperationError = string.Empty;
        private Texture lastPreviewLayoutTexture;
        private int lastPreviewLayoutWidth = -1;
        private int lastPreviewLayoutHeight = -1;
        private int lastPreviewRotation = int.MinValue;
        private bool lastPreviewVerticallyMirrored;
        private bool lastPreviewSelfieMirrored;
        private bool lastPreviewUsesCameraMetadata;
        private float lastPreviewFrameWidth = -1f;
        private float lastPreviewFrameHeight = -1f;
        private bool rootVisualElementWarningLogged;
        private bool buildUiSuccessLogged;
        private bool missingThemeWarningLogged;
#if UNITY_EDITOR
        private bool editorRebuildQueued;
#endif

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
            if (Application.isPlaying && !buildUiSuccessLogged)
            {
                IOSDeviceConsoleLog.Write("[MobileWorkoutPrototypeView] Awake on " + gameObject.name);
            }

            EnsureDocumentAndUi();
#if UNITY_EDITOR
            QueueEditorRebuild();
#endif
        }

        private void OnEnable()
        {
            EnsureDocumentAndUi();
#if UNITY_EDITOR
            QueueEditorRebuild();
#endif

            if (Application.isPlaying)
            {
                if (cameraSource != null)
                {
                    cameraSource.PreviewTextureChanged += HandlePreviewTextureChanged;
                }
                if (trackingController != null)
                {
                    trackingController.TrackingFrameReceived += HandleTrackingFrameReceived;
                }

                SubscribePerformanceProfiler();
            }
        }

        private void OnDisable()
        {
            if (calibrationFlowCoroutine != null)
            {
                StopCoroutine(calibrationFlowCoroutine);
                calibrationFlowCoroutine = null;
            }

            calibrationFlowRunning = false;

            if (sessionTransitionCoroutine != null)
            {
                StopCoroutine(sessionTransitionCoroutine);
                sessionTransitionCoroutine = null;
            }

            pendingWorkoutStart = false;
            pendingWorkoutStop = false;
            sessionTransitionKind = SessionTransitionKind.None;

            if (cameraSource != null)
            {
                cameraSource.PreviewTextureChanged -= HandlePreviewTextureChanged;
            }
            if (trackingController != null)
            {
                trackingController.TrackingFrameReceived -= HandleTrackingFrameReceived;
            }

            UnsubscribePerformanceProfiler();

            if (!Application.isPlaying && document != null)
            {
                document.rootVisualElement?.Clear();
            }
        }

        private void OnDestroy()
        {
            UnsubscribePerformanceProfiler();

            if (runtimePanelSettings != null)
            {
                DestroyPanelSettings();
            }

            if (ownsRuntimeFont && runtimeFont != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(runtimeFont);
                }
                else
                {
                    DestroyImmediate(runtimeFont);
                }
            }

            runtimeFont = null;
            ownsRuntimeFont = false;
        }

        private void Start()
        {
            EnsureDocumentAndUi();
#if UNITY_EDITOR
            QueueEditorRebuild();
#endif
        }

        private void Update()
        {
            if (!Application.isPlaying && !showUiInEditMode)
            {
                return;
            }

            if (root == null)
            {
                EnsureDocumentAndUi();
            }

            ApplySafeAreaInsetsIfNeeded();

            if (Time.unscaledTime < nextRefreshAt)
            {
                return;
            }

            nextRefreshAt = Time.unscaledTime + refreshIntervalSeconds;
            RefreshDynamicText();
            RefreshPreviewTexture();
        }

        private void EnsureDocumentAndUi(bool forceRebuild = false)
        {
            if (!Application.isPlaying && !showUiInEditMode)
            {
                return;
            }

            ResolveReferences();
            ApplyMobilePerformanceDefaults();
            EnsureDocument();

            if (document == null || document.rootVisualElement == null)
            {
                if (!rootVisualElementWarningLogged)
                {
                    rootVisualElementWarningLogged = true;
                    Debug.LogWarning("[MobileWorkoutPrototypeView] UIDocument rootVisualElement is unavailable; UI build was skipped.");
                }

                return;
            }

            if (!forceRebuild && root != null && root.panel != null)
            {
                if (Application.isPlaying)
                {
                    ApplyCompatibilityVisibility();
                    ApplyTargetCount();
                }

                document.rootVisualElement.MarkDirtyRepaint();
                return;
            }

            BuildUi();
            if (!buildUiSuccessLogged)
            {
                buildUiSuccessLogged = true;
                IOSDeviceConsoleLog.Write("[MobileWorkoutPrototypeView] UI Toolkit workout interface built successfully.");
            }

            if (Application.isPlaying)
            {
                ApplyCompatibilityVisibility();
                ApplyTargetCount();
            }

            RenderCurrentStep();
            document.rootVisualElement.MarkDirtyRepaint();
        }

        private void ResolveReferences()
        {
            cameraSource ??= FindFirstObjectByType<CameraCaptureSource>();
            trackingController ??= FindFirstObjectByType<JointTrackingController>();
            feedbackOrchestrator ??= FindFirstObjectByType<RealtimeFeedbackOrchestrator>();
            feedbackReceiver ??= FindFirstObjectByType<PoseFeedbackJsonReceiver>();
            coachTts ??= FindFirstObjectByType<CoachTtsController>();
            replayPlayer ??= FindFirstObjectByType<PoseJsonReplayPlayer>();
            performanceProfiler ??= FindFirstObjectByType<DevicePerformanceProfiler>();
            profileStatus ??= FindFirstObjectByType<OnboardingStatusManager>();

            if (Application.isPlaying && profileStatus == null)
            {
                profileStatus = gameObject.AddComponent<OnboardingStatusManager>();
            }

            if (Application.isPlaying && replayPlayer == null)
            {
                replayPlayer = gameObject.AddComponent<PoseJsonReplayPlayer>();
            }

            if (Application.isPlaying && performanceProfiler == null)
            {
                performanceProfiler = gameObject.AddComponent<DevicePerformanceProfiler>();
            }

            if (Application.isPlaying)
            {
                SubscribePerformanceProfiler();
            }
        }

        private void EnsureDocument()
        {
            document ??= GetComponent<UIDocument>();
            if (document == null)
            {
                document = gameObject.AddComponent<UIDocument>();
            }

            if (document.panelSettings == null)
            {
                document.panelSettings = Resources.Load<PanelSettings>(PanelSettingsResourcePath);
                if (document.panelSettings == null)
                {
                    runtimePanelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                    runtimePanelSettings.name = "Mobile Workout Runtime Panel Settings";
                    runtimePanelSettings.hideFlags = HideFlags.DontSave;
                    document.panelSettings = runtimePanelSettings;
                }
            }

            document.sortingOrder = 100;
            ConfigurePanelSettings(document.panelSettings);
        }

        private void ConfigurePanelSettings(PanelSettings panelSettings)
        {
            if (panelSettings == null)
            {
                return;
            }

            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(390, 844);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0f;
            panelSettings.sortingOrder = 100;
            panelSettings.clearColor = true;
            panelSettings.colorClearValue = new Color(0.06f, 0.09f, 0.14f, 1f);

            var theme = Resources.Load<ThemeStyleSheet>(RuntimeThemeResourcePath);
#if UNITY_EDITOR
            theme ??= AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");
#endif
            if (theme != null)
            {
                panelSettings.themeStyleSheet = theme;
            }
            else if (!missingThemeWarningLogged)
            {
                missingThemeWarningLogged = true;
                Debug.LogWarning("[MobileWorkoutPrototypeView] Runtime UI Toolkit theme could not be loaded.");
            }
        }

#if UNITY_EDITOR
        private void QueueEditorRebuild()
        {
            if (Application.isPlaying || !showUiInEditMode || editorRebuildQueued)
            {
                return;
            }

            editorRebuildQueued = true;
            EditorApplication.delayCall += RebuildEditorPreview;
        }

        private void RebuildEditorPreview()
        {
            editorRebuildQueued = false;
            if (this == null || Application.isPlaying || !isActiveAndEnabled || !showUiInEditMode)
            {
                return;
            }

            root = null;
            EnsureDocumentAndUi(true);
            document?.rootVisualElement?.MarkDirtyRepaint();
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }
#endif

        private void BuildUi()
        {
            var documentRoot = document.rootVisualElement;
            documentRoot.Clear();
            documentRoot.style.width = Length.Percent(100f);
            documentRoot.style.height = Length.Percent(100f);
            documentRoot.style.flexGrow = 1f;
            documentRoot.style.backgroundColor = ColorFromHex(0x0B1119);

            root = new VisualElement { name = "mobile-workout-root" };
            root.StretchToParentSize();
            root.style.backgroundColor = ColorFromHex(0x090D12);
            root.style.alignItems = Align.Stretch;
            root.style.justifyContent = Justify.FlexStart;
            var font = ResolveRuntimeFont();
            if (font != null)
            {
                root.style.unityFont = font;
            }
            documentRoot.Add(root);

            screenRoot = new VisualElement { name = "full-screen-content" };
            screenRoot.StretchToParentSize();
            screenRoot.style.backgroundColor = ColorFromHex(0x0B1119);
            screenRoot.style.paddingTop = BaseTopPadding;
            screenRoot.style.paddingRight = BaseHorizontalPadding;
            screenRoot.style.paddingBottom = BaseBottomPadding;
            screenRoot.style.paddingLeft = BaseHorizontalPadding;
            root.Add(screenRoot);

            screenRoot.Add(BuildAppHeader());
            screenRoot.Add(BuildProgressBlock());

            contentScroll = new ScrollView(ScrollViewMode.Vertical) { name = "step-scroll" };
            contentScroll.style.flexGrow = 1f;
            contentScroll.style.width = Length.Percent(100f);
            contentScroll.style.marginTop = 8f;
            contentScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            contentScroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            contentRoot = contentScroll.contentContainer;
            contentRoot.style.flexGrow = 1f;
            contentRoot.style.flexDirection = FlexDirection.Column;
            contentRoot.style.width = Length.Percent(100f);
            screenRoot.Add(contentScroll);

            screenRoot.RegisterCallback<GeometryChangedEvent>(_ => ApplySafeAreaInsetsIfNeeded());
            screenRoot.schedule.Execute(() => ApplySafeAreaInsetsIfNeeded(true));
        }

        private VisualElement BuildAppHeader()
        {
            var header = new VisualElement { name = "app-header" };
            header.style.height = 58f;
            header.style.flexShrink = 0f;
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingLeft = 4f;
            header.style.paddingRight = 4f;

            var brand = new VisualElement { name = "app-brand" };
            brand.style.flexGrow = 1f;
            brand.style.justifyContent = Justify.Center;

            var title = Label("AI 헬스케어 코치", 19, Color.white, FontStyle.Bold);
            title.style.height = 27f;
            brand.Add(title);

            var subtitle = Label("실시간 자세 코칭", 10, ColorFromHex(0x8F9AAF), FontStyle.Bold);
            subtitle.style.height = 18f;
            brand.Add(subtitle);
            header.Add(brand);

            var online = Label("ONLINE", 9, ColorFromHex(0x34D399), FontStyle.Bold);
            online.style.width = 64f;
            online.style.height = 28f;
            online.style.unityTextAlign = TextAnchor.MiddleCenter;
            online.style.backgroundColor = new Color(0.02f, 0.23f, 0.17f, 0.9f);
            online.style.borderTopLeftRadius = 14f;
            online.style.borderTopRightRadius = 14f;
            online.style.borderBottomLeftRadius = 14f;
            online.style.borderBottomRightRadius = 14f;
            header.Add(online);
            return header;
        }

        private void ApplySafeAreaInsetsIfNeeded(bool force = false)
        {
            if (screenRoot == null || document == null || Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            var safeArea = Screen.safeArea;
            if (!force &&
                lastScreenWidth == Screen.width &&
                lastScreenHeight == Screen.height &&
                lastSafeArea == safeArea)
            {
                return;
            }

            var documentRoot = document.rootVisualElement;
            var panelWidth = documentRoot.resolvedStyle.width;
            var panelHeight = documentRoot.resolvedStyle.height;
            if (float.IsNaN(panelWidth) || float.IsNaN(panelHeight) || panelWidth <= 0f || panelHeight <= 0f)
            {
                return;
            }

            var scaleX = panelWidth / Screen.width;
            var scaleY = panelHeight / Screen.height;
            var leftInset = Mathf.Max(0f, safeArea.xMin) * scaleX;
            var rightInset = Mathf.Max(0f, Screen.width - safeArea.xMax) * scaleX;
            var topInset = Mathf.Max(0f, Screen.height - safeArea.yMax) * scaleY;
            var bottomInset = Mathf.Max(0f, safeArea.yMin) * scaleY;

            screenRoot.style.paddingLeft = BaseHorizontalPadding + leftInset;
            screenRoot.style.paddingRight = BaseHorizontalPadding + rightInset;
            screenRoot.style.paddingTop = BaseTopPadding + topInset;
            screenRoot.style.paddingBottom = BaseBottomPadding + bottomInset;

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            lastSafeArea = safeArea;
        }

        private VisualElement BuildHeroHeader()
        {
            var header = new VisualElement { name = "wireframe-header" };
            header.style.width = 420f;
            header.style.maxWidth = Length.Percent(100f);
            header.style.alignItems = Align.Center;
            header.style.height = 86f;

            var title = Label("🏋 Smart Fitness Wireframe", 23, ColorFromHex(0x34D399), FontStyle.Bold);
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.height = 34f;
            header.Add(title);

            var subtitle = Label("실제 스마트폰처럼 화면들을 터치하며 작동하는 초고화질 인터랙티브 와이어프레임 데모입니다.", 12, ColorFromHex(0xB6C2D5));
            subtitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            subtitle.style.width = 410f;
            subtitle.style.height = 40f;
            header.Add(subtitle);
            return header;
        }

        private VisualElement BuildPhoneNotch()
        {
            var row = new VisualElement { name = "phone-notch-row" };
            row.style.height = 26f;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.Center;

            var notch = new VisualElement { name = "phone-notch" };
            notch.style.width = 128f;
            notch.style.height = 24f;
            notch.style.backgroundColor = Color.black;
            notch.style.borderTopLeftRadius = 12f;
            notch.style.borderTopRightRadius = 12f;
            notch.style.borderBottomLeftRadius = 12f;
            notch.style.borderBottomRightRadius = 12f;
            notch.style.flexDirection = FlexDirection.Row;
            notch.style.alignItems = Align.Center;
            notch.style.justifyContent = Justify.Center;

            var leftDot = new VisualElement();
            leftDot.style.width = 10f;
            leftDot.style.height = 10f;
            leftDot.style.borderTopLeftRadius = 5f;
            leftDot.style.borderTopRightRadius = 5f;
            leftDot.style.borderBottomLeftRadius = 5f;
            leftDot.style.borderBottomRightRadius = 5f;
            leftDot.style.backgroundColor = ColorFromHex(0x111827);
            leftDot.style.marginRight = 14f;
            notch.Add(leftDot);

            var speaker = new VisualElement();
            speaker.style.width = 48f;
            speaker.style.height = 4f;
            speaker.style.borderTopLeftRadius = 2f;
            speaker.style.borderTopRightRadius = 2f;
            speaker.style.borderBottomLeftRadius = 2f;
            speaker.style.borderBottomRightRadius = 2f;
            speaker.style.backgroundColor = ColorFromHex(0x25324A);
            speaker.style.marginRight = 14f;
            notch.Add(speaker);

            var rightDot = new VisualElement();
            rightDot.style.width = 10f;
            rightDot.style.height = 10f;
            rightDot.style.borderTopLeftRadius = 5f;
            rightDot.style.borderTopRightRadius = 5f;
            rightDot.style.borderBottomLeftRadius = 5f;
            rightDot.style.borderBottomRightRadius = 5f;
            rightDot.style.backgroundColor = ColorFromHex(0x1D4ED8);
            notch.Add(rightDot);

            row.Add(notch);
            return row;
        }

        private VisualElement BuildHomeIndicator()
        {
            var row = new VisualElement { name = "phone-home-indicator-row" };
            row.style.height = 12f;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.Center;

            var bar = new VisualElement { name = "phone-home-indicator" };
            bar.style.width = 128f;
            bar.style.height = 6f;
            bar.style.backgroundColor = ColorFromHex(0x334155);
            bar.style.borderTopLeftRadius = 3f;
            bar.style.borderTopRightRadius = 3f;
            bar.style.borderBottomLeftRadius = 3f;
            bar.style.borderBottomRightRadius = 3f;
            row.Add(bar);
            return row;
        }

        private VisualElement BuildStatusBar()
        {
            var row = Row("status-bar", 24f, 0f);
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.paddingLeft = 8f;
            row.style.paddingRight = 8f;

            row.Add(Label("AI HEALTH", 10, ColorFromHex(0xB6C2D5), FontStyle.Bold));
            row.Add(Label("ONLINE", 9, ColorFromHex(0x34D399), FontStyle.Bold));
            return row;
        }

        private VisualElement BuildTitleBlock()
        {
            var block = Card("title-block", 54f);
            block.style.paddingTop = 6f;
            block.style.paddingRight = 10f;
            block.style.paddingBottom = 6f;
            block.style.paddingLeft = 10f;

            var title = Label("AI 헬스케어 코치", 17, Color.white, FontStyle.Bold);
            title.style.height = 24f;
            block.Add(title);

            var subtitle = Label("운동 선택, 목표 설정, 자세 추적, 리플레이를 한 흐름으로 확인합니다.", 10, ColorFromHex(0x8F9AAF));
            subtitle.style.height = 18f;
            block.Add(subtitle);
            return block;
        }

        private VisualElement BuildProgressBlock()
        {
            var row = Row("step-progress", 28f, 6f);
            row.style.paddingLeft = 8f;
            row.style.paddingRight = 8f;
            row.style.alignItems = Align.Center;

            progressPips = new VisualElement[4];
            for (var i = 0; i < progressPips.Length; i++)
            {
                var pip = new VisualElement { name = "step-pip-" + (i + 1) };
                pip.style.height = 8f;
                pip.style.width = i == 0 ? 42f : 18f;
                pip.style.borderTopLeftRadius = 4f;
                pip.style.borderTopRightRadius = 4f;
                pip.style.borderBottomLeftRadius = 4f;
                pip.style.borderBottomRightRadius = 4f;
                row.Add(pip);
                progressPips[i] = pip;
            }

            stepLabel = Label("1 / 4 정보수집", 11, ColorFromHex(0x34D399), FontStyle.Bold);
            stepLabel.style.flexGrow = 1f;
            stepLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(stepLabel);
            return row;
        }

        private void RenderCurrentStep()
        {
            if (contentRoot == null)
            {
                return;
            }

            contentRoot.Clear();
            ResetStepReferences();

            // Hard order: 1) profile → 2) calibration → 3) exercise → 4) session
            profileStatus ??= FindFirstObjectByType<OnboardingStatusManager>();
            if (Application.isPlaying && profileStatus == null)
            {
                profileStatus = gameObject.AddComponent<OnboardingStatusManager>();
            }

            var profileReady = profileStatus != null && profileStatus.HasCompletedProfile;
            if (!profileReady)
            {
                currentStep = ScreenStep.Profile;
            }
            else if (currentStep == ScreenStep.Profile)
            {
                currentStep = ScreenStep.Calibration;
            }
            else if (!hasCompletedCalibrationThisLaunch &&
                     currentStep > ScreenStep.Calibration)
            {
                currentStep = ScreenStep.Calibration;
            }

            UpdateProgress();

            switch (currentStep)
            {
                case ScreenStep.Profile:
                    showingProfileOnboarding = true;
                    RenderProfileOnboardingStep();
                    break;
                case ScreenStep.Calibration:
                    showingProfileOnboarding = false;
                    RenderCalibrationStep();
                    break;
                case ScreenStep.Exercise:
                    showingProfileOnboarding = false;
                    RenderExerciseStep();
                    break;
                case ScreenStep.Target:
                    showingProfileOnboarding = false;
                    RenderTargetStep();
                    break;
                case ScreenStep.Session:
                    showingProfileOnboarding = false;
                    RenderSessionStep();
                    if (Application.isPlaying && previewMode == PreviewMode.Camera)
                    {
                        cameraSource?.StartCamera();
                    }
                    break;
            }

            RefreshDynamicText();
            RefreshPreviewTexture();
            if (contentScroll != null)
            {
                contentScroll.scrollOffset = Vector2.zero;
            }
        }

        private void RenderProfileOnboardingStep()
        {
            if (stepLabel != null)
            {
                stepLabel.text = "1 / 4 정보수집";
            }

            if (profileStatus == null)
            {
                AddHeader("건강 프로필", "Play 모드에서 기본 정보를 입력할 수 있어요.");
                return;
            }

            var onboarding = new HealthProfileOnboardingView(
                profileStatus,
                () =>
                {
                    showingProfileOnboarding = false;
                    currentStep = ScreenStep.Calibration;
                    hasCompletedCalibrationThisLaunch = false;
                    calibrationSucceededThisFlow = false;
                    RenderCurrentStep();
                },
                ResolveRuntimeFont());
            onboarding.Bind(contentRoot);
        }

        private void RenderCalibrationStep()
        {
            AddHeader(
                "전신 캘리브레이션",
                "카메라에 전신이 들어오도록 맞춘 뒤 안정화되면 운동을 시작할 수 있어요.");

            contentRoot.Add(BuildPreviewPanel());

            calibrationStatusLabel = Label(
                "측정 시작을 누르면 전신 인식이 시작됩니다.",
                12,
                ColorFromHex(0xCBD5E1),
                FontStyle.Normal);
            calibrationStatusLabel.style.marginTop = 10f;
            calibrationStatusLabel.style.whiteSpace = WhiteSpace.Normal;
            contentRoot.Add(calibrationStatusLabel);

            contentRoot.Add(Label(
                "전신이 보이도록 서서 잠시 기다려 주세요. 의료적 진단이 아닌 운동 준비용 측정입니다.",
                10,
                ColorFromHex(0x8F9AAF),
                FontStyle.Normal));

            contentRoot.Add(Spacer(12f));
            var row = Row("calibration-buttons", 52f, 8f);
            row.Add(ActionButton(
                "측정 시작",
                ColorFromHex(0x14B8A6),
                Color.black,
                52f,
                StartCalibrationFlow));
            row.Add(ActionButton(
                "다시 측정",
                ColorFromHex(0x161B22),
                Color.white,
                52f,
                RestartCalibrationFlow));
            contentRoot.Add(row);

            contentRoot.Add(Spacer(8f));
            calibrationContinueButton = ActionButton(
                "운동 선택으로",
                ColorFromHex(0x22C55E),
                ColorFromHex(0x07110C),
                52f,
                CompleteCalibrationAndGoExercise);
            calibrationContinueButton.SetEnabled(false);
            contentRoot.Add(calibrationContinueButton);

            if (Application.isPlaying && previewMode == PreviewMode.Camera)
            {
                cameraSource?.StartCamera();
            }

            RefreshCalibrationUiState();
        }

        private void RenderExerciseStep()
        {
            var selected = GetSelectedExercise();
            var pickedHeader = Row("picked-header", 22f, 0f);
            pickedHeader.style.justifyContent = Justify.SpaceBetween;
            var pickedTitle = Label("선택한 운동 목록 (1)", 11, ColorFromHex(0xB6C2D5), FontStyle.Normal);
            pickedTitle.style.flexGrow = 1f;
            pickedHeader.Add(pickedTitle);
            pickedHeader.Add(Label("Real-time Save", 9, ColorFromHex(0x34D399), FontStyle.Bold));
            contentRoot.Add(pickedHeader);

            var chipRow = Row("picked-chip-row", 38f, 0f);
            var chip = new Button(() => { }) { text = GetExerciseIcon(selected.Id) + "  " + selected.Name + "  ×" };
            chip.style.width = 104f;
            chip.style.height = 30f;
            chip.style.backgroundColor = new Color(0.015f, 0.18f, 0.14f, 0.92f);
            chip.style.color = ColorFromHex(0x5EEAD4);
            chip.style.unityFont = ResolveRuntimeFont();
            chip.style.unityFontStyleAndWeight = FontStyle.Bold;
            chip.style.fontSize = 12f;
            chip.style.borderTopLeftRadius = 16f;
            chip.style.borderTopRightRadius = 16f;
            chip.style.borderBottomLeftRadius = 16f;
            chip.style.borderBottomRightRadius = 16f;
            chip.style.borderTopColor = ColorFromHex(0x10B981);
            chip.style.borderRightColor = ColorFromHex(0x10B981);
            chip.style.borderBottomColor = ColorFromHex(0x10B981);
            chip.style.borderLeftColor = ColorFromHex(0x10B981);
            chip.style.borderTopWidth = 1f;
            chip.style.borderRightWidth = 1f;
            chip.style.borderBottomWidth = 1f;
            chip.style.borderLeftWidth = 1f;
            chipRow.Add(chip);
            contentRoot.Add(chipRow);

            var divider = new VisualElement { name = "section-divider" };
            divider.style.height = 1f;
            divider.style.backgroundColor = ColorFromHex(0x1F2937);
            divider.style.marginTop = 2f;
            divider.style.marginBottom = 14f;
            contentRoot.Add(divider);

            var categoryRow = Row("category-row", 48f, 7f);
            contentRoot.Add(Label("운동 카테고리 선택", 10, ColorFromHex(0x9AA7BB), FontStyle.Bold));
            categoryRow.style.marginTop = 8f;
            AddCategoryButton(categoryRow, "상체");
            AddCategoryButton(categoryRow, "하체");
            AddCategoryButton(categoryRow, "맨몸");
            contentRoot.Add(categoryRow);

            var list = Card("exercise-list", 354f);
            list.style.marginTop = 16f;
            list.style.paddingTop = 12f;
            list.style.paddingRight = 12f;
            list.style.paddingBottom = 12f;
            list.style.paddingLeft = 12f;

            var visibleExercises = GetVisibleExercises();
            var listHeader = Row("exercise-list-header", 26f, 0f);
            var listTitle = Label(selectedCategory + " 운동 리스트 (터치하여 선택)", 12, ColorFromHex(0xD6E0EF), FontStyle.Bold);
            listTitle.style.flexGrow = 1f;
            listHeader.Add(listTitle);

            var countPill = Label(visibleExercises.Length + "개 항목", 10, ColorFromHex(0xCBD5E1), FontStyle.Bold);
            countPill.style.unityTextAlign = TextAnchor.MiddleCenter;
            countPill.style.width = 58f;
            countPill.style.height = 22f;
            countPill.style.backgroundColor = ColorFromHex(0x263347);
            countPill.style.borderTopLeftRadius = 10f;
            countPill.style.borderTopRightRadius = 10f;
            countPill.style.borderBottomLeftRadius = 10f;
            countPill.style.borderBottomRightRadius = 10f;
            listHeader.Add(countPill);
            list.Add(listHeader);

            for (var i = 0; i < visibleExercises.Length; i += 2)
            {
                var exerciseRow = Row("exercise-row-" + i, 58f, 8f);
                exerciseRow.style.marginTop = i == 0 ? 8f : 8f;
                AddExerciseButton(exerciseRow, visibleExercises[i]);
                if (i + 1 < visibleExercises.Length)
                {
                    AddExerciseButton(exerciseRow, visibleExercises[i + 1]);
                }
                list.Add(exerciseRow);
            }

            contentRoot.Add(list);
            contentRoot.Add(Spacer(24f));
            contentRoot.Add(ActionButton("개수 설정하러 가기 (다음)  ›", ColorFromHex(0x14B8A6), Color.black, 52f, () =>
            {
                previewMode = PreviewMode.None;
                replayPlayer?.StopReplay();
                currentStep = ScreenStep.Target;
                RenderCurrentStep();
            }));
        }

        private void RenderTargetStep()
        {
            AddHeader("목표 설정", "반복 횟수와 세트를 정하면 정확한 자세 카운트 목표가 적용됩니다.");

            var selected = GetSelectedExercise();
            var card = Card("target-card", 292f);
            card.style.paddingTop = 12f;
            card.style.paddingRight = 12f;
            card.style.paddingBottom = 12f;
            card.style.paddingLeft = 12f;
            card.Add(Label(selected.Name, 22, Color.white, FontStyle.Bold));
            repsField = AddNumberField(card, "반복 횟수", repsPerSet, value =>
            {
                repsPerSet = Mathf.Clamp(value, 1, 999);
                RefreshDynamicText();
            });
            setsField = AddNumberField(card, "세트 수", sets, value =>
            {
                sets = Mathf.Clamp(value, 1, 999);
                RefreshDynamicText();
            });
            targetCountLabel = Label(string.Empty, 14, ColorFromHex(0x34D399), FontStyle.Bold);
            targetCountLabel.style.marginTop = 8f;
            targetCountLabel.style.height = 28f;
            card.Add(targetCountLabel);

            var calorieLabel = Label("예상 소모: " + (repsPerSet * sets * selected.CaloriesPerRep).ToString("0.0") + " kcal", 12, ColorFromHex(0xFB7185), FontStyle.Bold);
            calorieLabel.style.height = 24f;
            card.Add(calorieLabel);
            contentRoot.Add(card);

            contentRoot.Add(Spacer(10f));
            var row = Row("target-buttons", 52f, 8f);
            row.Add(ActionButton("이전", ColorFromHex(0x161B22), Color.white, 52f, () =>
            {
                previewMode = PreviewMode.None;
                replayPlayer?.StopReplay();
                currentStep = ScreenStep.Exercise;
                RenderCurrentStep();
            }));
            row.Add(ActionButton("운동 화면으로", ColorFromHex(0x22C55E), ColorFromHex(0x07110C), 52f, () =>
            {
                if (!hasCompletedCalibrationThisLaunch)
                {
                    previewMode = PreviewMode.None;
                    replayPlayer?.StopReplay();
                    currentStep = ScreenStep.Calibration;
                    RenderCurrentStep();
                    return;
                }

                ApplyTargetCountFromFields();
                previewMode = PreviewMode.Camera;
                currentStep = ScreenStep.Session;
                RenderCurrentStep();
            }));
            contentRoot.Add(row);
        }

        private void RenderSessionStep()
        {
            AddHeader("운동 세션", "Start로 추적을 시작하고 Stop으로 저장 JSON 기반 3D 리플레이를 확인합니다.");

            var hud = Card("session-hud", 64f);
            hud.style.flexDirection = FlexDirection.Row;
            hud.style.alignItems = Align.Center;
            hud.style.paddingLeft = 10f;
            hud.style.paddingRight = 10f;
            phaseLabel = Label("Phase: -", 11, Color.white, FontStyle.Bold);
            phaseLabel.style.flexGrow = 1f;
            timerLabel = Label("00:00", 17, ColorFromHex(0x34D399), FontStyle.Bold);
            timerLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            timerLabel.style.width = 84f;
            hud.Add(phaseLabel);
            hud.Add(timerLabel);
            contentRoot.Add(hud);

            contentRoot.Add(BuildPreviewPanel());
            contentRoot.Add(BuildMetricRow());

            feedbackLabel = Label("최근 피드백: -", 10, ColorFromHex(0x9AA7BB));
            feedbackLabel.style.height = 34f;
            feedbackLabel.style.marginTop = 6f;
            contentRoot.Add(feedbackLabel);

            var utilityRow = Row("utility-row", 38f, 7f);
            utilityRow.Add(ActionButton("카메라 전환", ColorFromHex(0x161B22), Color.white, 38f, SwitchCamera));
            utilityRow.Add(ActionButton(
                "목표 수정",
                ColorFromHex(0x161B22),
                Color.white,
                38f,
                () => LeaveSession(ScreenStep.Target)));
            contentRoot.Add(utilityRow);

            var controlRow = Row("control-row", 54f, 8f);
            controlRow.style.marginTop = 6f;
            controlRow.Add(ActionButton("START", ColorFromHex(0x22C55E), ColorFromHex(0x07110C), 54f, StartWorkout));
            controlRow.Add(ActionButton("STOP", ColorFromHex(0xE11D48), Color.white, 54f, StopWorkoutAndReplay));
            contentRoot.Add(controlRow);

            var resetRow = Row("reset-row", 30f, 7f);
            resetRow.style.marginTop = 6f;
            resetRow.Add(ActionButton("리셋", ColorFromHex(0x101621), ColorFromHex(0x94A3B8), 30f, ResetSession));
            resetRow.Add(ActionButton(
                "운동 선택",
                ColorFromHex(0x101621),
                ColorFromHex(0x94A3B8),
                30f,
                () => LeaveSession(ScreenStep.Exercise)));
            contentRoot.Add(resetRow);

            if (showPerformanceBenchControls)
            {
                contentRoot.Add(BuildPerformanceBenchRow());
            }
        }

        private VisualElement BuildPerformanceBenchRow()
        {
            var block = new VisualElement { name = "perf-bench-block" };
            block.style.marginTop = 8f;
            block.style.flexShrink = 0f;

            var row = Row("perf-bench-row", 28f, 6f);
            row.Add(ActionButton("60초", ColorFromHex(0x1E293B), ColorFromHex(0x94A3B8), 28f, () => StartPerformanceBench(DevicePerformanceProfiler.BenchKind60s), 56f));
            row.Add(ActionButton("10분", ColorFromHex(0x1E293B), ColorFromHex(0x94A3B8), 28f, () => StartPerformanceBench(DevicePerformanceProfiler.BenchKind10m), 56f));
            row.Add(ActionButton("중지", ColorFromHex(0x1E293B), ColorFromHex(0xF87171), 28f, StopPerformanceBench, 48f));
            block.Add(row);

            performanceBenchStatusLabel = Label(performanceBenchStatusText, 9, ColorFromHex(0x64748B));
            performanceBenchStatusLabel.style.marginTop = 4f;
            performanceBenchStatusLabel.style.height = 28f;
            performanceBenchStatusLabel.style.whiteSpace = WhiteSpace.Normal;
            block.Add(performanceBenchStatusLabel);
            return block;
        }

        private void StartPerformanceBench(string benchKind)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ResolveReferences();
            if (performanceProfiler == null)
            {
                performanceBenchStatusText = "Perf bench: profiler missing";
                RefreshPerformanceBenchStatus();
                return;
            }

            performanceProfiler.BeginBench(benchKind);
            performanceBenchStatusText = "Perf bench: " + benchKind + " running";
            RefreshPerformanceBenchStatus();
        }

        private void StopPerformanceBench()
        {
            if (!Application.isPlaying || performanceProfiler == null || !performanceProfiler.IsRunning)
            {
                return;
            }

            performanceProfiler.Finish();
        }

        private void SubscribePerformanceProfiler()
        {
            if (performanceProfiler == null || performanceBenchSubscribed)
            {
                return;
            }

            performanceProfiler.Completed += HandlePerformanceBenchCompleted;
            performanceBenchSubscribed = true;
        }

        private void UnsubscribePerformanceProfiler()
        {
            if (performanceProfiler == null || !performanceBenchSubscribed)
            {
                return;
            }

            performanceProfiler.Completed -= HandlePerformanceBenchCompleted;
            performanceBenchSubscribed = false;
        }

        private void HandlePerformanceBenchCompleted(PerformanceBenchmarkResult _)
        {
            var report = performanceProfiler == null ? null : performanceProfiler.LastReport;
            if (report == null)
            {
                performanceBenchStatusText = "Perf bench: finished (no report)";
                RefreshPerformanceBenchStatus();
                return;
            }

            var fileName = string.IsNullOrWhiteSpace(report.savedPath)
                ? "(unsaved)"
                : System.IO.Path.GetFileName(report.savedPath);
            var acceptance = report.acceptance;
            var verdict = acceptance == null || !acceptance.applicable
                ? "SMOKE"
                : acceptance.passed
                    ? "PASS"
                    : "FAIL";
            performanceBenchStatusText = "Saved " + fileName + " · " + verdict;
            RefreshPerformanceBenchStatus();
        }

        private void RefreshPerformanceBenchStatus()
        {
            if (performanceBenchStatusLabel == null)
            {
                return;
            }

            if (performanceProfiler != null && performanceProfiler.IsRunning)
            {
                var elapsed = Mathf.FloorToInt(performanceProfiler.ElapsedSeconds);
                var memoryMb = performanceProfiler.LiveMemoryPeakBytes / (1024f * 1024f);
                performanceBenchStatusText =
                    performanceProfiler.BenchKind + " " +
                    (elapsed / 60).ToString("00") + ":" + (elapsed % 60).ToString("00") +
                    " · pose " + performanceProfiler.LivePoseFps.ToString("0.0") +
                    " · inf " + performanceProfiler.LiveInferenceMs.ToString("0.0") + "ms" +
                    " · mem " + memoryMb.ToString("0.0") + "MB";
            }

            performanceBenchStatusLabel.text = performanceBenchStatusText;
        }

        private VisualElement BuildPreviewPanel()
        {
            var frame = Card("preview-frame", 270f);
            previewFrame = frame;
            InvalidatePreviewLayout();
            frame.style.marginTop = 8f;
            frame.style.backgroundColor = Color.black;
            frame.style.position = Position.Relative;
            frame.style.overflow = Overflow.Hidden;

            previewImage = new Image { name = "camera-or-replay-preview", scaleMode = ScaleMode.ScaleToFit };
            previewImage.style.position = Position.Absolute;
            previewImage.style.left = 0f;
            previewImage.style.top = 0f;
            previewImage.style.width = Length.Percent(100f);
            previewImage.style.height = Length.Percent(100f);
            frame.Add(previewImage);

            // Keep pose landmarks in the final upright display coordinate space.
            // Camera memory orientation belongs only to the raw preview image and
            // must not transform already-normalized MediaPipe output a second time.
            poseOverlay = new VisualElement
            {
                name = "pose-overlay",
                pickingMode = PickingMode.Ignore
            };
            poseOverlay.style.position = Position.Absolute;
            poseOverlay.style.left = 0f;
            poseOverlay.style.top = 0f;
            poseOverlay.style.width = Length.Percent(100f);
            poseOverlay.style.height = Length.Percent(100f);
            poseOverlay.style.visibility = Visibility.Hidden;
            poseOverlay.generateVisualContent += OnGeneratePoseOverlayContent;
            frame.Add(poseOverlay);

            frame.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (previewFrame == frame)
                {
                    ApplyPreviewLayout(previewImage == null ? null : previewImage.image);
                }
            });

            previewPlaceholder = new VisualElement { name = "preview-placeholder" };
            previewPlaceholder.style.position = Position.Absolute;
            previewPlaceholder.style.left = 0f;
            previewPlaceholder.style.right = 0f;
            previewPlaceholder.style.top = 0f;
            previewPlaceholder.style.bottom = 0f;
            previewPlaceholder.style.backgroundColor = new Color(0.02f, 0.03f, 0.045f, 0.94f);
            previewPlaceholder.style.alignItems = Align.Center;
            previewPlaceholder.style.justifyContent = Justify.Center;
            previewPlaceholder.style.paddingLeft = 18f;
            previewPlaceholder.style.paddingRight = 18f;

            cameraStateLabel = Label("Start를 누르면 카메라 추적이 시작됩니다.", 13, Color.white, FontStyle.Bold);
            cameraStateLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            replayStateLabel = Label("Stop을 누르면 저장 JSON 기반 3D 리플레이가 표시됩니다.", 10, ColorFromHex(0x9AA7BB));
            replayStateLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            replayStateLabel.style.marginTop = 8f;
            previewPlaceholder.Add(cameraStateLabel);
            previewPlaceholder.Add(replayStateLabel);
            frame.Add(previewPlaceholder);

            calibrationOverlay = new CalibrationOverlayView();
            calibrationOverlay.Bind(frame);
            calibrationOverlay.SetVisible(false);

            var tag = Label("AI SKELETON DETECTING", 10, ColorFromHex(0x34D399), FontStyle.Bold);
            tag.style.position = Position.Absolute;
            tag.style.left = 12f;
            tag.style.top = 10f;
            tag.style.backgroundColor = new Color(0.02f, 0.03f, 0.045f, 0.7f);
            tag.style.paddingTop = 4f;
            tag.style.paddingRight = 8f;
            tag.style.paddingBottom = 4f;
            tag.style.paddingLeft = 8f;
            tag.style.borderTopLeftRadius = 10f;
            tag.style.borderTopRightRadius = 10f;
            tag.style.borderBottomLeftRadius = 10f;
            tag.style.borderBottomRightRadius = 10f;
            frame.Add(tag);
            return frame;
        }

        private VisualElement BuildMetricRow()
        {
            var row = Row("metric-row", 64f, 7f);
            row.style.marginTop = 8f;
            correctCountLabel = AddMetric(row, "정확한 자세", "0");
            targetCountLabel = AddMetric(row, "목표", GetTargetCount().ToString());
            poseFpsLabel = AddMetric(row, "Pose FPS", "0.0");
            return row;
        }

        private void AddHeader(string title, string body)
        {
            var header = new VisualElement { name = "step-header" };
            header.style.height = 56f;
            header.Add(Label(title, 18, Color.white, FontStyle.Bold));
            var description = Label(body, 10, ColorFromHex(0x8F9AAF));
            description.style.marginTop = 2f;
            header.Add(description);
            contentRoot.Add(header);
        }

        private void AddCategoryButton(VisualElement parent, string category)
        {
            var selected = selectedCategory == category;
            var button = ActionButton(
                BuildCategoryLabel(category),
                selected ? ColorFromHex(0x22C55E) : ColorFromHex(0x161B22),
                selected ? ColorFromHex(0x07110C) : Color.white,
                48f,
                () =>
                {
                    selectedCategory = category;
                    RenderCurrentStep();
                });
            button.style.marginRight = 8f;
            parent.Add(button);
        }

        private void AddExerciseButton(VisualElement parent, ExerciseOption exercise)
        {
            var selected = selectedExerciseId == exercise.Id;
            var label = GetExerciseIcon(exercise.Id) + "  " + exercise.Name + "\n" + exercise.CaloriesPerRep.ToString("0.0") + " kcal/회";
            var button = ActionButton(
                label,
                selected ? ColorFromHex(0x064E3B) : ColorFromHex(0x161B22),
                selected ? ColorFromHex(0xBBF7D0) : exercise.Supported ? ColorFromHex(0xCBD5E1) : ColorFromHex(0x7B879A),
                56f,
                () =>
                {
                    if (!exercise.Supported)
                    {
                        return;
                    }

                    selectedExerciseId = exercise.Id;
                    RenderCurrentStep();
                });
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.style.fontSize = 11f;
            button.style.paddingLeft = 10f;
            button.style.paddingRight = 8f;
            button.style.marginRight = 8f;
            button.style.borderTopWidth = selected ? 1f : 0f;
            button.style.borderRightWidth = selected ? 1f : 0f;
            button.style.borderBottomWidth = selected ? 1f : 0f;
            button.style.borderLeftWidth = selected ? 1f : 0f;
            button.style.borderTopColor = ColorFromHex(0x10B981);
            button.style.borderRightColor = ColorFromHex(0x10B981);
            button.style.borderBottomColor = ColorFromHex(0x10B981);
            button.style.borderLeftColor = ColorFromHex(0x10B981);
            parent.Add(button);
        }

        private TextField AddNumberField(VisualElement parent, string label, int value, Action<int> onChanged)
        {
            var row = Row(label + "-row", 54f, 8f);
            row.style.marginTop = 10f;
            var title = Label(label, 12, ColorFromHex(0xCBD5E1), FontStyle.Bold);
            title.style.flexGrow = 1f;
            title.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(title);

            row.Add(ActionButton("-", ColorFromHex(0x111827), Color.white, 38f, () =>
            {
                value = Mathf.Clamp(ReadIntField(label == "세트 수" ? setsField : repsField, value) - 1, 1, 999);
                var target = label == "세트 수" ? setsField : repsField;
                if (target != null)
                {
                    target.value = value.ToString();
                }
                onChanged(value);
            }, 42f));

            var field = new TextField { value = value.ToString(), maxLength = 3 };
            field.style.width = 64f;
            field.style.height = 38f;
            field.style.unityFont = ResolveRuntimeFont();
            field.style.unityTextAlign = TextAnchor.MiddleCenter;
            field.style.backgroundColor = ColorFromHex(0xF1F5F9);
            field.style.color = ColorFromHex(0x020617);
            field.style.borderTopLeftRadius = 8f;
            field.style.borderTopRightRadius = 8f;
            field.style.borderBottomLeftRadius = 8f;
            field.style.borderBottomRightRadius = 8f;
            field.RegisterValueChangedCallback(evt =>
            {
                var parsed = ParsePositiveInt(evt.newValue, value);
                onChanged(parsed);
            });
            row.Add(field);

            row.Add(ActionButton("+", ColorFromHex(0x111827), Color.white, 38f, () =>
            {
                value = Mathf.Clamp(ReadIntField(label == "세트 수" ? setsField : repsField, value) + 1, 1, 999);
                var target = label == "세트 수" ? setsField : repsField;
                if (target != null)
                {
                    target.value = value.ToString();
                }
                onChanged(value);
            }, 42f));

            parent.Add(row);
            return field;
        }

        private Label AddMetric(VisualElement parent, string title, string value)
        {
            var card = Card(title + "-metric", 64f);
            card.style.flexGrow = 1f;
            card.style.paddingTop = 6f;
            card.style.paddingRight = 4f;
            card.style.paddingBottom = 5f;
            card.style.paddingLeft = 4f;
            card.style.alignItems = Align.Center;
            card.style.justifyContent = Justify.Center;
            card.Add(Label(title, 9, ColorFromHex(0x7B879A), FontStyle.Bold));
            var metric = Label(value, 17, ColorFromHex(0x34D399), FontStyle.Bold);
            metric.style.marginTop = 3f;
            card.Add(metric);
            parent.Add(card);
            return metric;
        }

        private void RefreshDynamicText()
        {
            var elapsed = workoutRunning ? Time.unscaledTime - sessionStartedAt + elapsedBeforePause : elapsedBeforePause;
            if (timerLabel != null)
            {
                var seconds = Mathf.FloorToInt(Mathf.Max(0f, elapsed));
                timerLabel.text = (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
            }

            if (correctCountLabel != null)
            {
                correctCountLabel.text = feedbackOrchestrator == null ? "0" : feedbackOrchestrator.CorrectRepCount.ToString();
            }

            if (targetCountLabel != null)
            {
                targetCountLabel.text = currentStep == ScreenStep.Target
                    ? "목표 정확 자세 카운트: " + GetTargetCount() + "개"
                    : GetTargetCount().ToString();
            }

            if (poseFpsLabel != null)
            {
                poseFpsLabel.text = trackingController == null ? "0.0" : trackingController.PoseFps.ToString("0.0");
            }

            if (phaseLabel != null)
            {
                var currentPhase = feedbackOrchestrator == null || feedbackOrchestrator.PhaseState == null
                    ? ExercisePhase.Unknown
                    : feedbackOrchestrator.PhaseState.CurrentPhase;
                var phase = currentPhase.ToString();
                var status = BuildPoseDecisionStatus(currentPhase);
                phaseLabel.text = GetSelectedExercise().Name + "\nPhase: " + phase + " / " + status;
            }

            if (feedbackLabel != null)
            {
                var feedback = feedbackReceiver == null || string.IsNullOrWhiteSpace(feedbackReceiver.LatestFeedbackText)
                    ? "-"
                    : feedbackReceiver.LatestFeedbackText;
                feedbackLabel.text = "최근 피드백: " + Trim(feedback, 62);
            }

            if (cameraStateLabel != null)
            {
                cameraStateLabel.text = BuildCameraStateText();
            }

            if (replayStateLabel != null)
            {
                replayStateLabel.text = BuildReplayStateText();
            }

            var showCalibUi = feedbackOrchestrator != null &&
                              feedbackOrchestrator.SessionStateMachine != null &&
                              feedbackOrchestrator.SessionStateMachine.IsSessionActive &&
                              (workoutRunning ||
                               calibrationFlowRunning ||
                               currentStep == ScreenStep.Calibration);
            if (showCalibUi && calibrationOverlay != null)
            {
                calibrationOverlay.Update(
                    feedbackOrchestrator.SessionState,
                    feedbackOrchestrator.LatestCalibration,
                    feedbackOrchestrator.CountdownRemaining);
            }
            else if (calibrationOverlay != null)
            {
                calibrationOverlay.SetVisible(false);
            }

            RefreshCalibrationUiState();

            if (showPerformanceBenchControls && performanceBenchStatusLabel != null)
            {
                RefreshPerformanceBenchStatus();
            }
        }

        private void RefreshCalibrationUiState()
        {
            if (currentStep != ScreenStep.Calibration)
            {
                return;
            }

            var sessionMachine = feedbackOrchestrator?.SessionStateMachine;
            var sessionState = feedbackOrchestrator?.SessionState ?? WorkoutTrackingState.ReadyForCalibration;

            // Freeze success and stop analysis so coaching does not start here.
            if (calibrationFlowRunning &&
                !calibrationSucceededThisFlow &&
                sessionMachine != null &&
                sessionMachine.IsSessionActive &&
                sessionState == WorkoutTrackingState.InWorkout)
            {
                calibrationSucceededThisFlow = true;
                feedbackOrchestrator?.EndWorkoutSession();
                calibrationFlowRunning = false;
            }

            if (calibrationContinueButton != null)
            {
                calibrationContinueButton.SetEnabled(calibrationSucceededThisFlow);
            }

            if (calibrationStatusLabel == null)
            {
                return;
            }

            if (calibrationSucceededThisFlow)
            {
                calibrationStatusLabel.text = "전신 감지 완료! 운동 선택으로 이동할 수 있어요.";
                return;
            }

            if (!calibrationFlowRunning || sessionMachine == null || !sessionMachine.IsSessionActive)
            {
                calibrationStatusLabel.text = "측정 시작을 누르면 전신 인식이 시작됩니다.";
                return;
            }

            switch (sessionState)
            {
                case WorkoutTrackingState.ReadyForCalibration:
                    var guidance = feedbackOrchestrator.LatestCalibration?.GuidanceReason;
                    calibrationStatusLabel.text = string.IsNullOrWhiteSpace(guidance)
                        ? "전신이 보이도록 맞추고 잠시 유지해 주세요."
                        : guidance;
                    break;
                case WorkoutTrackingState.CountingDown:
                    var seconds = Mathf.Max(1, Mathf.CeilToInt(feedbackOrchestrator.CountdownRemaining));
                    calibrationStatusLabel.text = "전신 감지 완료! " + seconds + "초 후 측정이 확정됩니다.";
                    break;
                case WorkoutTrackingState.InWorkout:
                    calibrationStatusLabel.text = "전신 측정이 완료되었어요. 운동 선택으로 이동할 수 있습니다.";
                    break;
                case WorkoutTrackingState.PausedOutOfFrame:
                    calibrationStatusLabel.text = "전신이 화면을 벗어났어요. 다시 프레임 안으로 들어와 주세요.";
                    break;
                default:
                    calibrationStatusLabel.text = "전신 측정을 진행 중입니다.";
                    break;
            }
        }

        private void RefreshPreviewTexture()
        {
            if (previewImage == null)
            {
                return;
            }

            Texture texture = previewMode switch
            {
                PreviewMode.Camera when cameraSource != null &&
                                            (cameraSource.IsRunning || cameraSource.IsStarting) =>
                    cameraSource.PreviewTexture,
                PreviewMode.Replay when replayPlayer != null && replayPlayer.LoadedFrameCount > 0 =>
                    replayPlayer.PreviewTexture,
                _ => null
            };

            previewImage.image = texture;
            ApplyPreviewLayout(texture);
            previewImage.style.display = texture == null ? DisplayStyle.None : DisplayStyle.Flex;

            if (previewPlaceholder != null)
            {
                previewPlaceholder.style.display = texture == null ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void ApplyPreviewLayout(Texture texture)
        {
            if (previewImage == null || previewFrame == null)
            {
                return;
            }

            if (texture == null)
            {
                previewImage.style.left = 0f;
                previewImage.style.top = 0f;
                previewImage.style.width = Length.Percent(100f);
                previewImage.style.height = Length.Percent(100f);
                previewImage.style.rotate = new Rotate(new Angle(0f, AngleUnit.Degree));
                previewImage.style.scale = new Scale(Vector3.one);
                ResetPoseOverlayLayout();
                InvalidatePreviewLayout();
                return;
            }

            var frameWidth = previewFrame.resolvedStyle.width;
            var frameHeight = previewFrame.resolvedStyle.height;
            var textureWidth = texture.width;
            var textureHeight = texture.height;
            if (float.IsNaN(frameWidth) || float.IsNaN(frameHeight) ||
                frameWidth <= 0f || frameHeight <= 0f ||
                textureWidth <= 0 || textureHeight <= 0)
            {
                return;
            }

            // WebCamTexture metadata becomes reliable a few frames after Play().
            // This method is called by the periodic refresh, frame callbacks, and
            // geometry changes so the correction updates without restarting UI.
            var usesCameraMetadata = previewMode == PreviewMode.Camera &&
                                     cameraSource != null &&
                                     texture == cameraSource.PreviewTexture;
            var rotation = usesCameraMetadata
                ? NormalizeRotation(cameraSource.VideoRotationAngle)
                : 0;
            var verticallyMirrored = usesCameraMetadata && cameraSource.VideoVerticallyMirrored;
            var selfieMirrored = usesCameraMetadata && cameraSource.ActiveCameraIsFrontFacing;

            if (lastPreviewLayoutTexture == texture &&
                lastPreviewLayoutWidth == textureWidth &&
                lastPreviewLayoutHeight == textureHeight &&
                lastPreviewRotation == rotation &&
                lastPreviewVerticallyMirrored == verticallyMirrored &&
                lastPreviewSelfieMirrored == selfieMirrored &&
                lastPreviewUsesCameraMetadata == usesCameraMetadata &&
                Mathf.Abs(lastPreviewFrameWidth - frameWidth) < 0.1f &&
                Mathf.Abs(lastPreviewFrameHeight - frameHeight) < 0.1f)
            {
                return;
            }

            var quarterTurn = rotation == 90 || rotation == 270;
            var displayAspect = quarterTurn
                ? (float)textureHeight / textureWidth
                : (float)textureWidth / textureHeight;
            var frameAspect = frameWidth / frameHeight;

            float fittedWidth;
            float fittedHeight;
            if (displayAspect > frameAspect)
            {
                fittedWidth = frameWidth;
                fittedHeight = frameWidth / displayAspect;
            }
            else
            {
                fittedHeight = frameHeight;
                fittedWidth = frameHeight * displayAspect;
            }

            // Size the raw camera element in its unrotated coordinate system. The
            // separate pose overlay stays in the final upright fitted rectangle.
            var elementWidth = quarterTurn ? fittedHeight : fittedWidth;
            var elementHeight = quarterTurn ? fittedWidth : fittedHeight;
            previewImage.style.left = (frameWidth - elementWidth) * 0.5f;
            previewImage.style.top = (frameHeight - elementHeight) * 0.5f;
            previewImage.style.width = elementWidth;
            previewImage.style.height = elementHeight;
            previewImage.style.rotate = new Rotate(new Angle(rotation, AngleUnit.Degree));
            previewImage.style.scale = new Scale(PoseDisplayCoordinateMapper.ResolvePreviewScale(
                rotation,
                verticallyMirrored,
                selfieMirrored));

            if (poseOverlay != null)
            {
                poseOverlay.style.left = (frameWidth - fittedWidth) * 0.5f;
                poseOverlay.style.top = (frameHeight - fittedHeight) * 0.5f;
                poseOverlay.style.width = fittedWidth;
                poseOverlay.style.height = fittedHeight;
                poseOverlay.style.rotate = new Rotate(new Angle(0f, AngleUnit.Degree));
                poseOverlay.style.scale = new Scale(Vector3.one);
                poseOverlay.style.display = usesCameraMetadata ? DisplayStyle.Flex : DisplayStyle.None;
                poseOverlay.MarkDirtyRepaint();
            }

            lastPreviewLayoutTexture = texture;
            lastPreviewLayoutWidth = textureWidth;
            lastPreviewLayoutHeight = textureHeight;
            lastPreviewRotation = rotation;
            lastPreviewVerticallyMirrored = verticallyMirrored;
            lastPreviewSelfieMirrored = selfieMirrored;
            lastPreviewUsesCameraMetadata = usesCameraMetadata;
            lastPreviewFrameWidth = frameWidth;
            lastPreviewFrameHeight = frameHeight;
        }

        private void ResetPoseOverlayLayout()
        {
            if (poseOverlay == null)
            {
                return;
            }

            poseOverlay.style.left = 0f;
            poseOverlay.style.top = 0f;
            poseOverlay.style.width = Length.Percent(100f);
            poseOverlay.style.height = Length.Percent(100f);
            poseOverlay.style.rotate = new Rotate(new Angle(0f, AngleUnit.Degree));
            poseOverlay.style.scale = new Scale(Vector3.one);
            poseOverlay.style.display = DisplayStyle.None;
        }

        private void InvalidatePreviewLayout()
        {
            lastPreviewLayoutTexture = null;
            lastPreviewLayoutWidth = -1;
            lastPreviewLayoutHeight = -1;
            lastPreviewRotation = int.MinValue;
            lastPreviewVerticallyMirrored = false;
            lastPreviewSelfieMirrored = false;
            lastPreviewUsesCameraMetadata = false;
            lastPreviewFrameWidth = -1f;
            lastPreviewFrameHeight = -1f;
        }

        private static int NormalizeRotation(int rotation)
        {
            rotation %= 360;
            return rotation < 0 ? rotation + 360 : rotation;
        }

        private void HandlePreviewTextureChanged(Texture texture)
        {
            RefreshPreviewTexture();
        }

        private void StartWorkout()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (calibrationFlowCoroutine != null || calibrationFlowRunning)
            {
                return;
            }

            if (sessionTransitionCoroutine != null)
            {
                // STOP, reset, and camera switching are asynchronous on iOS. Keep
                // the user's START intent and run it once that transition releases.
                pendingWorkoutStart = true;
                pendingWorkoutStop = false;
                return;
            }

            pendingWorkoutStart = false;
            if (workoutRunning)
            {
                previewMode = PreviewMode.Camera;
                replayPlayer?.StopReplay();
                trackingController?.StartTracking();
                RefreshPreviewTexture();
                return;
            }

            sessionTransitionKind = SessionTransitionKind.Starting;
            sessionTransitionCoroutine = StartCoroutine(StartWorkoutRoutine());
        }

        private IEnumerator StartWorkoutRoutine()
        {
            // Keep the handle valid until camera readiness has completed.
            yield return null;
            try
            {
                ApplyTargetCount();
                previewMode = PreviewMode.Camera;
                replayPlayer?.StopReplay();
                // Stop→Start must not keep the previous session's pose mesh. UI Toolkit
                // generateVisualContent early-returns leave the last drawn content, so hide
                // until the first live frame of this session arrives.
                HidePoseOverlay();
                cameraOperationError = string.Empty;
                RefreshPreviewTexture();

                var cameraReady = false;
                var cameraError = string.Empty;
                if (cameraSource == null)
                {
                    cameraError = "카메라 소스가 없습니다.";
                }
                else
                {
                    yield return cameraSource.EnsureCameraReady((success, error) =>
                    {
                        cameraReady = success;
                        cameraError = error;
                    });
                }

                if (!cameraReady)
                {
                    cameraOperationError = string.IsNullOrWhiteSpace(cameraError)
                        ? "카메라를 준비하지 못했습니다."
                        : cameraError;
                    workoutRunning = false;
                    RefreshPreviewTexture();
                    yield break;
                }

                if (trackingController == null)
                {
                    cameraOperationError = "관절 추적 컨트롤러가 없습니다.";
                    workoutRunning = false;
                    yield break;
                }

                currentSessionReceivedPoseFrame = false;
                trackingController.StartTracking();
                var trackingDeadline = Time.realtimeSinceStartup + Mathf.Max(1f, trackingStartupTimeoutSeconds);
                while (trackingController.IsStartRequested &&
                       !trackingController.IsTracking &&
                       Time.realtimeSinceStartup < trackingDeadline)
                {
                    if (pendingWorkoutStop)
                    {
                        trackingController.StopTracking();
                        yield break;
                    }

                    yield return null;
                }

                if (!trackingController.IsTracking)
                {
                    var trackingError = trackingController.LastTrackingError;
                    trackingController.StopTracking();
                    cameraOperationError = string.IsNullOrWhiteSpace(trackingError)
                        ? "관절 추적을 제한 시간 안에 시작하지 못했습니다."
                        : trackingError;
                    workoutRunning = false;
                    RefreshPreviewTexture();
                    yield break;
                }

                if (pendingWorkoutStop)
                {
                    trackingController.StopTracking();
                    yield break;
                }

                coachTts ??= FindFirstObjectByType<CoachTtsController>();
                coachTts?.BeginSession();
                feedbackOrchestrator ??= FindFirstObjectByType<RealtimeFeedbackOrchestrator>();
                feedbackOrchestrator?.BeginWorkoutSession(skipCalibration: hasCompletedCalibrationThisLaunch);
                sessionStartedAt = Time.unscaledTime;
                workoutRunning = true;
                RefreshPreviewTexture();
            }
            finally
            {
                CompleteSessionTransition();
            }
        }

        private void StartCalibrationFlow()
        {
            if (!Application.isPlaying || currentStep != ScreenStep.Calibration)
            {
                return;
            }

            if (sessionTransitionCoroutine != null)
            {
                return;
            }

            if (calibrationFlowCoroutine != null)
            {
                return;
            }

            calibrationSucceededThisFlow = false;
            calibrationFlowCoroutine = StartCoroutine(StartCalibrationRoutine());
        }

        private void RestartCalibrationFlow()
        {
            if (!Application.isPlaying || currentStep != ScreenStep.Calibration)
            {
                return;
            }

            if (sessionTransitionCoroutine != null)
            {
                return;
            }

            if (calibrationFlowCoroutine != null)
            {
                StopCoroutine(calibrationFlowCoroutine);
                calibrationFlowCoroutine = null;
            }

            StopCalibrationTrackingOnly();
            calibrationSucceededThisFlow = false;
            calibrationFlowCoroutine = StartCoroutine(StartCalibrationRoutine());
        }

        private IEnumerator StartCalibrationRoutine()
        {
            yield return null;
            try
            {
                previewMode = PreviewMode.Camera;
                replayPlayer?.StopReplay();
                HidePoseOverlay();
                cameraOperationError = string.Empty;
                workoutRunning = false;
                calibrationSucceededThisFlow = false;
                RefreshPreviewTexture();

                var cameraReady = false;
                var cameraError = string.Empty;
                if (cameraSource == null)
                {
                    cameraError = "카메라 소스가 없습니다.";
                }
                else
                {
                    yield return cameraSource.EnsureCameraReady((success, error) =>
                    {
                        cameraReady = success;
                        cameraError = error;
                    });
                }

                if (!cameraReady)
                {
                    cameraOperationError = string.IsNullOrWhiteSpace(cameraError)
                        ? "카메라를 준비하지 못했습니다."
                        : cameraError;
                    calibrationFlowRunning = false;
                    RefreshPreviewTexture();
                    RefreshCalibrationUiState();
                    yield break;
                }

                if (trackingController == null)
                {
                    cameraOperationError = "관절 추적 컨트롤러가 없습니다.";
                    calibrationFlowRunning = false;
                    RefreshCalibrationUiState();
                    yield break;
                }

                trackingController.StartTracking();
                var trackingDeadline = Time.realtimeSinceStartup + Mathf.Max(1f, trackingStartupTimeoutSeconds);
                while (trackingController.IsStartRequested &&
                       !trackingController.IsTracking &&
                       Time.realtimeSinceStartup < trackingDeadline)
                {
                    yield return null;
                }

                if (!trackingController.IsTracking)
                {
                    var trackingError = trackingController.LastTrackingError;
                    trackingController.StopTracking();
                    cameraOperationError = string.IsNullOrWhiteSpace(trackingError)
                        ? "관절 추적을 제한 시간 안에 시작하지 못했습니다."
                        : trackingError;
                    calibrationFlowRunning = false;
                    RefreshPreviewTexture();
                    RefreshCalibrationUiState();
                    yield break;
                }

                feedbackOrchestrator ??= FindFirstObjectByType<RealtimeFeedbackOrchestrator>();
                feedbackOrchestrator?.BeginWorkoutSession(skipCalibration: false);
                calibrationFlowRunning = true;
                workoutRunning = false;
                RefreshPreviewTexture();
                RefreshCalibrationUiState();
            }
            finally
            {
                calibrationFlowCoroutine = null;
            }
        }

        private void StopCalibrationTrackingOnly()
        {
            calibrationFlowRunning = false;
            feedbackOrchestrator ??= FindFirstObjectByType<RealtimeFeedbackOrchestrator>();
            feedbackOrchestrator?.EndWorkoutSession();
            trackingController?.StopTracking();
            HidePoseOverlay();
            calibrationOverlay?.SetVisible(false);
        }

        private void CompleteCalibrationAndGoExercise()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (!calibrationSucceededThisFlow && !hasCompletedCalibrationThisLaunch)
            {
                return;
            }

            if (calibrationFlowCoroutine != null)
            {
                StopCoroutine(calibrationFlowCoroutine);
                calibrationFlowCoroutine = null;
            }

            hasCompletedCalibrationThisLaunch = true;
            calibrationFlowRunning = false;
            feedbackOrchestrator ??= FindFirstObjectByType<RealtimeFeedbackOrchestrator>();
            feedbackOrchestrator?.EndWorkoutSession();
            trackingController?.StopTracking();
            HidePoseOverlay();
            calibrationOverlay?.SetVisible(false);
            previewMode = PreviewMode.None;
            replayPlayer?.StopReplay();
            currentStep = ScreenStep.Exercise;
            RenderCurrentStep();
        }

        private void StopWorkoutAndReplay()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (sessionTransitionCoroutine != null)
            {
                if (sessionTransitionKind != SessionTransitionKind.Stopping &&
                    sessionTransitionKind != SessionTransitionKind.Resetting &&
                    sessionTransitionKind != SessionTransitionKind.Leaving)
                {
                    pendingWorkoutStop = true;
                    pendingWorkoutStart = false;
                }

                return;
            }

            pendingWorkoutStart = false;
            pendingWorkoutStop = false;
            sessionTransitionKind = SessionTransitionKind.Stopping;
            sessionTransitionCoroutine = StartCoroutine(StopWorkoutAndReplayRoutine());
        }

        private IEnumerator StopWorkoutAndReplayRoutine()
        {
            // Ensure the coroutine handle is assigned before transition work runs.
            yield return null;
            try
            {
                var sessionHasPoseFrames = currentSessionReceivedPoseFrame;
                currentSessionReceivedPoseFrame = false;
                StopWorkoutOnly();
                yield return WaitForTrackingRequestToFinish();

                // Keep the capture session warm while replay is visible. START can then
                // resume without repeatedly tearing down AVFoundation and MediaPipe.
                yield return null;

                // Clear the previous load result first so a failed attempt cannot
                // make stale replay frames look like a newly prepared replay.
                replayPlayer?.ClearReplay();
                if (!sessionHasPoseFrames)
                {
                    previewMode = PreviewMode.Camera;
                    RefreshPreviewTexture();
                    yield break;
                }

                replayPlayer?.PlayLatestSession();
                var replayReady = replayPlayer != null &&
                                  replayPlayer.LoadedFrameCount > 0 &&
                                  replayPlayer.PreviewTexture != null;
                previewMode = replayReady ? PreviewMode.Replay : PreviewMode.Camera;
                RefreshPreviewTexture();
            }
            finally
            {
                CompleteSessionTransition();
            }
        }

        private void StopWorkoutOnly()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (workoutRunning)
            {
                elapsedBeforePause += Time.unscaledTime - sessionStartedAt;
            }

            workoutRunning = false;
            feedbackOrchestrator ??= FindFirstObjectByType<RealtimeFeedbackOrchestrator>();
            feedbackOrchestrator?.EndWorkoutSession();
            coachTts ??= FindFirstObjectByType<CoachTtsController>();
            coachTts?.EndSession();
            trackingController?.StopTracking();
            HidePoseOverlay();
            calibrationOverlay?.SetVisible(false);
        }

        private void SwitchCamera()
        {
            if (!Application.isPlaying || sessionTransitionCoroutine != null)
            {
                return;
            }

            pendingWorkoutStop = false;
            sessionTransitionKind = SessionTransitionKind.SwitchingCamera;
            sessionTransitionCoroutine = StartCoroutine(SwitchCameraRoutine());
        }

        private IEnumerator SwitchCameraRoutine()
        {
            // Ensure the coroutine handle is assigned before transition work runs.
            yield return null;
            try
            {
                previewMode = PreviewMode.Camera;
                replayPlayer?.StopReplay();
                var shouldResumeTracking = workoutRunning || (trackingController != null && trackingController.IsTracking);
                if (cameraSource == null)
                {
                    cameraOperationError = "카메라 소스가 없습니다.";
                    RefreshPreviewTexture();
                    yield break;
                }

                var currentFacing = cameraSource.HasValidFrame
                    ? cameraSource.ActiveCameraIsFrontFacing
                    : cameraSource.PreferFrontCamera;
                var targetFacing = !currentFacing;
                if (!cameraSource.IsCameraFacingAvailable(targetFacing, out var availabilityError))
                {
                    cameraOperationError = availabilityError;
                    RefreshPreviewTexture();
                    yield break;
                }

                if (workoutRunning)
                {
                    elapsedBeforePause += Time.unscaledTime - sessionStartedAt;
                }

                workoutRunning = false;
                coachTts ??= FindFirstObjectByType<CoachTtsController>();
                coachTts?.Suspend();
                trackingController?.StopTracking();
                HidePoseOverlay();
                var trackingBecameIdle = false;
                yield return WaitForTrackingRequestToFinish(value => trackingBecameIdle = value);

                if (!trackingBecameIdle)
                {
                    cameraOperationError = "관절 추적 정리가 끝나지 않아 카메라 전환을 취소했습니다.";
                    coachTts?.EndSession();

                    RefreshPreviewTexture();
                    yield break;
                }

                var switchSucceeded = false;
                var switchError = string.Empty;
                cameraSource.SwitchCameraFacing(
                    targetFacing,
                    (success, error) =>
                    {
                        switchSucceeded = success;
                        switchError = error;
                    });
                while (cameraSource.IsSwitchingCamera)
                {
                    yield return null;
                }

                cameraOperationError = switchSucceeded ? string.Empty : switchError;
                var cameraRecovered = cameraSource != null && cameraSource.HasValidFrame;

                if (shouldResumeTracking && cameraRecovered)
                {
                    trackingController?.StartTracking();
                    var trackingDeadline = Time.realtimeSinceStartup + Mathf.Max(1f, trackingStartupTimeoutSeconds);
                    while (trackingController != null &&
                           trackingController.IsStartRequested &&
                           !trackingController.IsTracking &&
                           Time.realtimeSinceStartup < trackingDeadline)
                    {
                        if (pendingWorkoutStop)
                        {
                            trackingController.StopTracking();
                            break;
                        }

                        yield return null;
                    }

                    if (trackingController != null && trackingController.IsTracking && !pendingWorkoutStop)
                    {
                        coachTts?.Resume();
                        workoutRunning = true;
                        sessionStartedAt = Time.unscaledTime;
                    }
                    else
                    {
                        trackingController?.StopTracking();
                        coachTts?.EndSession();
                        if (!pendingWorkoutStop)
                        {
                            cameraOperationError = string.IsNullOrWhiteSpace(trackingController?.LastTrackingError)
                                ? "카메라 전환 후 관절 추적을 다시 시작하지 못했습니다."
                                : trackingController.LastTrackingError;
                        }
                    }
                }
                else if (shouldResumeTracking)
                {
                    coachTts?.EndSession();
                }

                RefreshPreviewTexture();
            }
            finally
            {
                CompleteSessionTransition();
            }
        }

        private IEnumerator WaitForTrackingRequestToFinish(Action<bool> onCompleted = null)
        {
            // Native live-stream cancellation must drain its physical callback. If
            // that callback never arrives, the provider performs timeout recovery at
            // three seconds, so keep this non-blocking UI wait slightly longer.
            const float timeoutSeconds = 4.5f;
            var timeoutAt = Time.realtimeSinceStartup + timeoutSeconds;
            while (trackingController != null &&
                   !trackingController.IsIdle &&
                   Time.realtimeSinceStartup < timeoutAt)
            {
                yield return null;
            }

            if (trackingController != null && !trackingController.IsIdle)
            {
                Debug.LogWarning("[MobileWorkoutPrototypeView] Pose tracking did not become idle before the UI transition timeout.");
            }

            onCompleted?.Invoke(trackingController == null || trackingController.IsIdle);
        }

        private void LeaveSession(ScreenStep targetStep)
        {
            if (!Application.isPlaying || sessionTransitionCoroutine != null)
            {
                return;
            }

            pendingWorkoutStart = false;
            pendingWorkoutStop = false;
            sessionTransitionKind = SessionTransitionKind.Leaving;
            sessionTransitionCoroutine = StartCoroutine(LeaveSessionRoutine(targetStep));
        }

        private IEnumerator LeaveSessionRoutine(ScreenStep targetStep)
        {
            yield return null;
            try
            {
                StopWorkoutOnly();
                yield return WaitForTrackingRequestToFinish();
                previewMode = PreviewMode.None;
                replayPlayer?.StopReplay();
                currentStep = targetStep;
                RenderCurrentStep();
            }
            finally
            {
                CompleteSessionTransition();
            }
        }

        private void ResetSession()
        {
            if (!Application.isPlaying || sessionTransitionCoroutine != null)
            {
                return;
            }

            pendingWorkoutStart = false;
            pendingWorkoutStop = false;
            sessionTransitionKind = SessionTransitionKind.Resetting;
            sessionTransitionCoroutine = StartCoroutine(ResetSessionRoutine());
        }

        private IEnumerator ResetSessionRoutine()
        {
            // Ensure the coroutine handle is assigned before transition work runs.
            yield return null;
            try
            {
                StopWorkoutOnly();
                yield return WaitForTrackingRequestToFinish();
                replayPlayer?.ClearReplay();
                elapsedBeforePause = 0f;
                currentSessionReceivedPoseFrame = false;
                previewMode = PreviewMode.Camera;
                cameraOperationError = string.Empty;
                ApplyTargetCount();
                RenderCurrentStep();
            }
            finally
            {
                CompleteSessionTransition();
            }
        }

        private void CompleteSessionTransition()
        {
            sessionTransitionCoroutine = null;
            sessionTransitionKind = SessionTransitionKind.None;
            if (currentStep != ScreenStep.Session || !isActiveAndEnabled)
            {
                pendingWorkoutStart = false;
                pendingWorkoutStop = false;
                return;
            }

            if (pendingWorkoutStop)
            {
                pendingWorkoutStop = false;
                pendingWorkoutStart = false;
                StopWorkoutAndReplay();
                return;
            }

            if (!pendingWorkoutStart)
            {
                return;
            }

            pendingWorkoutStart = false;
            StartWorkout();
        }

        private void ApplyTargetCountFromFields()
        {
            repsPerSet = ReadIntField(repsField, repsPerSet);
            sets = ReadIntField(setsField, sets);
            ApplyTargetCount();
            RefreshDynamicText();
        }

        private void ApplyTargetCount()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            feedbackOrchestrator ??= FindFirstObjectByType<RealtimeFeedbackOrchestrator>();
            feedbackOrchestrator?.SetCorrectRepTarget(GetTargetCount());
        }

        private void ApplyMobilePerformanceDefaults()
        {
            if (mobilePerformanceConfigured ||
                !applyMobilePerformanceDefaults ||
                !Application.isPlaying ||
                !Application.isMobilePlatform)
            {
                return;
            }

            mobilePerformanceConfigured = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Mathf.Clamp(mobileTargetFrameRate, 15, 120);
            cameraSource?.ConfigureCapture(mobileCameraWidth, mobileCameraHeight, mobileCameraFps);
            trackingController?.ConfigureSamplingRate(mobilePoseFps);
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
                if (candidate.gameObject.name == "Coach Canvas")
                {
                    candidate.enabled = false;
                }
            }
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

        private ExerciseOption[] GetVisibleExercises()
        {
            return Array.FindAll(Exercises, exercise => exercise.Category == selectedCategory);
        }

        private static string BuildCategoryLabel(string category)
        {
            return category switch
            {
                "상체" => "상체",
                "하체" => "하체",
                "맨몸" => "맨몸",
                _ => category
            };
        }

        private static string GetExerciseIcon(string id)
        {
            return id switch
            {
                "squat" => "SQ",
                "lunge" => "LU",
                "legpress" => "LP",
                "deadlift" => "DL",
                "pushup" => "PU",
                "pullup" => "PL",
                "plank" => "PK",
                "burpee" => "BP",
                _ => "EX"
            };
        }

        private Font ResolveRuntimeFont()
        {
            if (runtimeFont != null)
            {
                return runtimeFont;
            }

            runtimeFont = Resources.Load<Font>(BundledKoreanFontResourcePath);
            if (runtimeFont != null)
            {
                ownsRuntimeFont = false;
                return runtimeFont;
            }

            try
            {
                runtimeFont = Font.CreateDynamicFontFromOSFont(KoreanFontFallbacks, 16);
                ownsRuntimeFont = runtimeFont != null;
                if (ownsRuntimeFont)
                {
                    runtimeFont.name = "AI Healthcare Korean Runtime Font";
                    runtimeFont.hideFlags = HideFlags.DontSave;
                    return runtimeFont;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[MobileWorkoutPrototypeView] Korean system font could not be loaded: " + exception.Message);
            }

            runtimeFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            ownsRuntimeFont = false;
            return runtimeFont;
        }

        private void UpdateProgress()
        {
            string label;
            int activePip;
            switch (currentStep)
            {
                case ScreenStep.Profile:
                    label = "1 / 4 정보수집";
                    activePip = 0;
                    break;
                case ScreenStep.Calibration:
                    label = "2 / 4 전신측정";
                    activePip = 1;
                    break;
                case ScreenStep.Exercise:
                case ScreenStep.Target:
                    label = currentStep == ScreenStep.Target ? "3 / 4 목표설정" : "3 / 4 운동선택";
                    activePip = 2;
                    break;
                default:
                    label = "4 / 4 운동피드백";
                    activePip = 3;
                    break;
            }

            if (stepLabel != null)
            {
                stepLabel.text = label;
            }

            if (progressPips == null)
            {
                return;
            }

            for (var i = 0; i < progressPips.Length; i++)
            {
                var active = i == activePip;
                progressPips[i].style.width = active ? 42f : 18f;
                progressPips[i].style.backgroundColor = active ? ColorFromHex(0x34D399) : ColorFromHex(0x334155);
            }
        }

        private void ResetStepReferences()
        {
            previewFrame = null;
            previewImage = null;
            poseOverlay = null;
            calibrationOverlay = null;
            calibrationStatusLabel = null;
            calibrationContinueButton = null;
            InvalidatePreviewLayout();
            previewPlaceholder = null;
            timerLabel = null;
            correctCountLabel = null;
            targetCountLabel = null;
            poseFpsLabel = null;
            phaseLabel = null;
            feedbackLabel = null;
            cameraStateLabel = null;
            replayStateLabel = null;
            performanceBenchStatusLabel = null;
            repsField = null;
            setsField = null;
        }

        private string BuildCameraStateText()
        {
            if (previewMode == PreviewMode.Replay)
            {
                return "3D 리플레이를 표시하고 있습니다.";
            }

            if (previewMode == PreviewMode.None)
            {
                return "미리보기가 숨겨져 있습니다.";
            }

            if (cameraSource == null)
            {
                return "카메라 소스가 없습니다.";
            }

            if (cameraSource.IsStarting)
            {
                return "카메라 시작 중입니다.";
            }

            if (cameraSource.IsSwitchingCamera)
            {
                return "카메라 전환 중입니다.";
            }

            if (!string.IsNullOrWhiteSpace(cameraOperationError))
            {
                return "세션 오류: " + Trim(cameraOperationError, 58);
            }

            if (!string.IsNullOrWhiteSpace(trackingController?.LastTrackingError))
            {
                return "인식 오류: " + Trim(trackingController.LastTrackingError, 58);
            }

            if ((workoutRunning || calibrationFlowRunning) &&
                feedbackOrchestrator != null &&
                feedbackOrchestrator.SessionStateMachine != null &&
                feedbackOrchestrator.SessionStateMachine.IsSessionActive)
            {
                var sessionState = feedbackOrchestrator.SessionState;
                if (sessionState == WorkoutTrackingState.CountingDown)
                {
                    var seconds = Mathf.Max(1, Mathf.CeilToInt(feedbackOrchestrator.CountdownRemaining));
                    return "전신 감지 완료! " + seconds + "초 후 시작합니다";
                }

                if (sessionState == WorkoutTrackingState.PausedOutOfFrame)
                {
                    return "전신이 화면을 벗어났어요. 다시 프레임 안으로 들어와 주세요";
                }

                if (sessionState == WorkoutTrackingState.ReadyForCalibration)
                {
                    var calibration = feedbackOrchestrator.LatestCalibration;
                    if (calibration != null && !string.IsNullOrWhiteSpace(calibration.GuidanceReason))
                    {
                        return calibration.GuidanceReason;
                    }

                    return "카메라 뒤로 물러서주세요. 전신이 보이도록 서 주세요.";
                }
            }

            if (cameraSource.IsRunning && trackingController != null && trackingController.IsTracking)
            {
                var quality = feedbackOrchestrator == null ? null : feedbackOrchestrator.LatestTrackingQuality;
                if (quality != null && quality.State != PoseTrackingQualityState.Good)
                {
                    return string.IsNullOrWhiteSpace(quality.Reason)
                        ? "관절 위치를 안정화하는 중입니다."
                        : quality.Reason;
                }

                if (feedbackOrchestrator != null && feedbackOrchestrator.IsWaitingForStandingRearm)
                {
                    return "스쿼트 판정을 시작하려면 바르게 서 주세요.";
                }

                return "관절 인식 중 · " + trackingController.PoseFps.ToString("0.0") + " FPS";
            }

            if (cameraSource.IsRunning)
            {
                return "카메라 준비 완료 · 관절 추적 대기 중입니다.";
            }

            return "Start를 누르면 카메라 추적이 시작됩니다.";
        }

        private string BuildPoseDecisionStatus(ExercisePhase phase)
        {
            if (trackingController == null || !trackingController.IsTracking || trackingController.PoseFps < 1f)
            {
                return "추적 대기";
            }

            var quality = feedbackOrchestrator == null ? null : feedbackOrchestrator.LatestTrackingQuality;
            if (quality != null && quality.State != PoseTrackingQualityState.Good)
            {
                return quality.State == PoseTrackingQualityState.Unavailable
                    ? "전신 확인 필요"
                    : "추적 보정 중";
            }

            if (feedbackOrchestrator != null && feedbackOrchestrator.IsWaitingForStandingRearm)
            {
                return "준비 자세로 서기";
            }

            if (phase == ExercisePhase.Unknown)
            {
                return "인식 불안정";
            }

            if (feedbackOrchestrator != null && feedbackOrchestrator.CurrentRepHasViolation)
            {
                return "교정 필요";
            }

            return phase switch
            {
                ExercisePhase.Standing => "준비",
                ExercisePhase.Bottom => "깊이 확인",
                ExercisePhase.Descent => "동작 중",
                ExercisePhase.Ascent => "동작 중",
                _ => "인식 불안정"
            };
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

        private Button ActionButton(string text, Color background, Color textColor, float height, Action action, float width = -1f)
        {
            var button = new Button(action) { text = text };
            button.style.height = height;
            button.style.flexGrow = width < 0f ? 1f : 0f;
            if (width > 0f)
            {
                button.style.width = width;
            }

            button.style.backgroundColor = background;
            button.style.color = textColor;
            button.style.unityFont = ResolveRuntimeFont();
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.fontSize = 12f;
            button.style.borderTopLeftRadius = 12f;
            button.style.borderTopRightRadius = 12f;
            button.style.borderBottomLeftRadius = 12f;
            button.style.borderBottomRightRadius = 12f;
            button.style.borderTopWidth = 0f;
            button.style.borderRightWidth = 0f;
            button.style.borderBottomWidth = 0f;
            button.style.borderLeftWidth = 0f;
            return button;
        }

        private VisualElement Card(string name, float height)
        {
            var card = new VisualElement { name = name };
            card.style.height = height;
            card.style.backgroundColor = ColorFromHex(0x111827);
            card.style.borderTopLeftRadius = 12f;
            card.style.borderTopRightRadius = 12f;
            card.style.borderBottomLeftRadius = 12f;
            card.style.borderBottomRightRadius = 12f;
            return card;
        }

        private VisualElement Row(string name, float height, float gap)
        {
            var row = new VisualElement { name = name };
            row.style.height = height;
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        private Label Label(string text, int size, Color color, FontStyle style = FontStyle.Normal)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color;
            label.style.unityFont = ResolveRuntimeFont();
            label.style.unityFontStyleAndWeight = style;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static VisualElement Spacer(float height)
        {
            var spacer = new VisualElement();
            spacer.style.height = height;
            return spacer;
        }

        private static int ReadIntField(TextField field, int fallback)
        {
            if (field == null)
            {
                return Mathf.Max(1, fallback);
            }

            return ParsePositiveInt(field.value, fallback);
        }

        private static int ParsePositiveInt(string value, int fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Mathf.Max(1, fallback);
            }

            return int.TryParse(value.Trim(), out var parsed) ? Mathf.Clamp(parsed, 1, 999) : Mathf.Max(1, fallback);
        }

        private static string Trim(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            return value.Length <= maxLength ? value : value.Substring(0, maxLength - 3) + "...";
        }

        private static Color ColorFromHex(int rgb)
        {
            return new Color(
                ((rgb >> 16) & 0xFF) / 255f,
                ((rgb >> 8) & 0xFF) / 255f,
                (rgb & 0xFF) / 255f,
                1f);
        }

        private readonly struct BoneSegment
        {
            public BoneSegment(string from, string to)
            {
                From = from;
                To = to;
            }
            public string From { get; }
            public string To { get; }
        }

        private static readonly BoneSegment[] BoneSegments =
        {
            new BoneSegment(PoseJointNames.LeftShoulder, PoseJointNames.RightShoulder),
            new BoneSegment(PoseJointNames.LeftHip, PoseJointNames.RightHip),
            new BoneSegment(PoseJointNames.LeftShoulder, PoseJointNames.LeftElbow),
            new BoneSegment(PoseJointNames.LeftElbow, PoseJointNames.LeftWrist),
            new BoneSegment(PoseJointNames.RightShoulder, PoseJointNames.RightElbow),
            new BoneSegment(PoseJointNames.RightElbow, PoseJointNames.RightWrist),
            new BoneSegment(PoseJointNames.LeftHip, PoseJointNames.LeftKnee),
            new BoneSegment(PoseJointNames.LeftKnee, PoseJointNames.LeftAnkle),
            new BoneSegment(PoseJointNames.LeftAnkle, PoseJointNames.LeftHeel),
            new BoneSegment(PoseJointNames.LeftHeel, PoseJointNames.LeftFootIndex),
            new BoneSegment(PoseJointNames.RightHip, PoseJointNames.RightKnee),
            new BoneSegment(PoseJointNames.RightKnee, PoseJointNames.RightAnkle),
            new BoneSegment(PoseJointNames.RightAnkle, PoseJointNames.RightHeel),
            new BoneSegment(PoseJointNames.RightHeel, PoseJointNames.RightFootIndex),
            new BoneSegment(PoseJointNames.LeftShoulder, PoseJointNames.LeftHip),
            new BoneSegment(PoseJointNames.RightShoulder, PoseJointNames.RightHip)
        };

        private void HandleTrackingFrameReceived(JointTrackingFrame frame)
        {
            if (workoutRunning && frame != null)
            {
                currentSessionReceivedPoseFrame = true;
                // Reveal only after a live frame for the current workout session so a
                // Stop→Start transition cannot flash the previous overlay mesh.
                if (poseOverlay != null)
                {
                    poseOverlay.style.visibility = Visibility.Visible;
                }
            }

            if (previewImage != null && poseOverlay != null)
            {
                ApplyPreviewLayout(previewImage.image);
                poseOverlay.MarkDirtyRepaint();
            }
        }

        private void HidePoseOverlay()
        {
            if (poseOverlay == null)
            {
                return;
            }

            poseOverlay.style.visibility = Visibility.Hidden;
            poseOverlay.MarkDirtyRepaint();
        }

        private void OnGeneratePoseOverlayContent(MeshGenerationContext mgc)
        {
            if (previewMode != PreviewMode.Camera ||
                previewImage == null ||
                poseOverlay == null ||
                trackingController == null ||
                !trackingController.IsTracking ||
                poseOverlay.resolvedStyle.visibility == Visibility.Hidden ||
                (cameraSource != null && cameraSource.IsSwitchingCamera))
            {
                return;
            }

            var width = poseOverlay.layout.width;
            var height = poseOverlay.layout.height;

            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0f || height <= 0f)
            {
                return;
            }

            var frame = trackingController == null ? null : trackingController.LatestFrame;
            if (frame == null || frame.joints == null || frame.joints.Length == 0)
            {
                return;
            }

            var rect = new Rect(0f, 0f, width, height);
            // Lifecycle contract:
            // - Preview: rotation + verticalMirror + front selfie scale (ResolvePreviewScale)
            // - Overlay: upright fitted rect, no extra front X mirror (avoids double/opposite mirror)
            // Front-camera selfie mirroring is applied on the preview Image scale only.
            // Overlay landmarks stay in upright provider space so they move with the mirrored preview.
            var mirrorX = false;
            var painter = mgc.painter2D;

            // Draw bones
            painter.strokeColor = new Color(0.1f, 0.9f, 0.6f, 0.85f);
            painter.lineWidth = 4f;
            foreach (var segment in BoneSegments)
            {
                if (frame.TryGetJoint(segment.From, out var fromJoint) &&
                    frame.TryGetJoint(segment.To, out var toJoint) &&
                    PoseDisplayCoordinateMapper.CanRender(fromJoint) &&
                    PoseDisplayCoordinateMapper.CanRender(toJoint))
                {
                    var p1 = PoseDisplayCoordinateMapper.ToDisplayPoint(fromJoint, rect, mirrorX);
                    var p2 = PoseDisplayCoordinateMapper.ToDisplayPoint(toJoint, rect, mirrorX);
                    painter.BeginPath();
                    painter.MoveTo(p1);
                    painter.LineTo(p2);
                    painter.Stroke();
                }
            }

            // Draw joints
            float jointSize = 8f;
            float half = jointSize * 0.5f;
            foreach (var joint in frame.joints)
            {
                if (PoseDisplayCoordinateMapper.CanRender(joint))
                {
                    var p = PoseDisplayCoordinateMapper.ToDisplayPoint(joint, rect, mirrorX);
                    painter.fillColor = GetJointColor(joint.name);
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(p.x - half, p.y - half));
                    painter.LineTo(new Vector2(p.x + half, p.y - half));
                    painter.LineTo(new Vector2(p.x + half, p.y + half));
                    painter.LineTo(new Vector2(p.x - half, p.y + half));
                    painter.ClosePath();
                    painter.Fill();
                }
            }
        }

        private Color GetJointColor(string jointName)
        {
            if (jointName.StartsWith("left_"))
            {
                return new Color(0.14f, 0.58f, 0.95f, 0.95f);
            }
            if (jointName.StartsWith("right_"))
            {
                return new Color(0.95f, 0.42f, 0.2f, 0.95f);
            }
            return new Color(0.95f, 0.95f, 0.95f, 0.9f);
        }

        private void DestroyPanelSettings()
        {
            if (Application.isPlaying)
            {
                Destroy(runtimePanelSettings);
            }
            else
            {
                DestroyImmediate(runtimePanelSettings);
            }

            runtimePanelSettings = null;
        }
    }
}
