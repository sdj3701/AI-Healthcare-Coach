#if UNITY_EDITOR
using System.Linq;
using Rag.Healthcare.Camera;
using Rag.Healthcare.Pose;
using Rag.Healthcare.Pose.Providers;
using Rag.Healthcare.Pose.Rendering;
using Rag.Healthcare.Rag.Knowledge;
using Rag.Healthcare.Rag.Logging;
using Rag.Healthcare.Rag.Runtime;
using Rag.Healthcare.Tts;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Rag.Healthcare.Editor
{
    public static class RagSquatCoachSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/TestRagSysten.unity";
        private const string MediaPipeDefine = "AHC_USE_HOMULER_MEDIAPIPE";

        [MenuItem("Rag/RAG/Create TestRagSysten Scene")]
        public static void EnsureSquatCoachScene()
        {
            EnsureFolder("Assets", "Scenes");

            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (sceneAsset == null)
            {
                CreateScene();
            }
            else
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            EnsureSceneInBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("Rag/RAG/Enable Homuler MediaPipe Define")]
        public static void EnableHomulerMediaPipeDefine()
        {
            var target = NamedBuildTarget.Standalone;
            var defines = PlayerSettings.GetScriptingDefineSymbols(target)
                .Split(';')
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            if (!defines.Contains(MediaPipeDefine))
            {
                defines.Add(MediaPipeDefine);
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
                Debug.Log($"Added scripting define '{MediaPipeDefine}' for {target.TargetName}.");
            }
            else
            {
                Debug.Log($"Scripting define '{MediaPipeDefine}' is already enabled for {target.TargetName}.");
            }
        }

        private static void CreateScene()
        {
            var previousScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera();
            CreateLight();

            var runtime = new GameObject("RAG Squat Coach Runtime");
            var cameraSource = runtime.AddComponent<CameraCaptureSource>();
            var trackingController = runtime.AddComponent<JointTrackingController>();
            var mediaPipeProvider = runtime.AddComponent<MediaPipePoseTrackingProvider>();
            var feedbackReceiver = runtime.AddComponent<PoseFeedbackJsonReceiver>();
            var coachTts = runtime.AddComponent<CoachTtsController>();
            var ragRetriever = runtime.AddComponent<RagRetriever>();
            var sessionLogger = runtime.AddComponent<SessionJsonlLogger>();
            var orchestrator = runtime.AddComponent<RealtimeFeedbackOrchestrator>();
            var debugView = runtime.AddComponent<CameraPreviewDebugView>();

            SetObject(trackingController, "cameraSource", cameraSource);
            SetObject(trackingController, "feedbackReceiver", feedbackReceiver);
            SetObject(trackingController, "trackingProvider", mediaPipeProvider);
            SetEnum(trackingController, "backend", (int)PoseTrackingBackend.LocalMediaPipe);
            SetBool(trackingController, "autoStartTracking", false);
            SetFloat(trackingController, "requestIntervalSeconds", 1f / 15f);

            SetObject(feedbackReceiver, "coachTts", coachTts);
            SetEnum(coachTts, "backend", (int)TtsBackend.Auto);

            SetObject(orchestrator, "trackingController", trackingController);
            SetObject(orchestrator, "feedbackReceiver", feedbackReceiver);
            SetObject(orchestrator, "ragRetriever", ragRetriever);
            SetObject(orchestrator, "sessionLogger", sessionLogger);
            SetBool(orchestrator, "startTrackingOnStart", true);

            SetObject(debugView, "cameraSource", cameraSource);
            SetObject(debugView, "trackingController", trackingController);
            SetObject(debugView, "feedbackReceiver", feedbackReceiver);

            CreateUi(cameraSource, trackingController, feedbackReceiver);

            EditorSceneManager.SaveScene(scene, ScenePath);

            if (previousScene.IsValid() && !string.IsNullOrEmpty(previousScene.path))
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            var camera = cameraObject.AddComponent<UnityEngine.Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.04f, 0.05f, 0.06f);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            cameraObject.AddComponent<AudioListener>();
        }

        private static void CreateLight()
        {
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
        }

        private static void CreateUi(
            CameraCaptureSource cameraSource,
            JointTrackingController trackingController,
            PoseFeedbackJsonReceiver feedbackReceiver)
        {
            var canvasObject = new GameObject("Coach Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            var previewRoot = new GameObject("Camera Preview", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            previewRoot.transform.SetParent(canvasObject.transform, false);
            var previewRect = previewRoot.GetComponent<RectTransform>();
            Stretch(previewRect);
            previewRect.offsetMin = new Vector2(24f, 24f);
            previewRect.offsetMax = new Vector2(-360f, -24f);

            var previewImage = previewRoot.GetComponent<RawImage>();
            previewImage.color = Color.white;

            var fitter = previewRoot.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

            var overlayObject = new GameObject("Pose Overlay", typeof(RectTransform), typeof(PoseSkeletonRenderer));
            overlayObject.transform.SetParent(previewRoot.transform, false);
            var overlayRect = overlayObject.GetComponent<RectTransform>();
            Stretch(overlayRect);

            var skeletonRenderer = overlayObject.GetComponent<PoseSkeletonRenderer>();
            SetObject(skeletonRenderer, "trackingController", trackingController);
            SetObject(skeletonRenderer, "overlayRoot", overlayRect);

            var binder = canvasObject.AddComponent<PosePreviewOverlayBinder>();
            SetObject(binder, "cameraSource", cameraSource);
            SetObject(binder, "previewImage", previewImage);
            SetObject(binder, "overlayRoot", overlayRect);
            SetObject(binder, "aspectRatioFitter", fitter);

            var panelObject = new GameObject("Status Panel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(canvasObject.transform, false);
            var panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1f, 0f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 0.5f);
            panelRect.sizeDelta = new Vector2(320f, 0f);
            panelRect.anchoredPosition = new Vector2(-24f, 0f);

            var panelImage = panelObject.GetComponent<Image>();
            panelImage.color = new Color(0.06f, 0.07f, 0.08f, 0.88f);

            var statusObject = new GameObject("Tracking Status", typeof(RectTransform), typeof(Text), typeof(PoseTrackingStatusView));
            statusObject.transform.SetParent(panelObject.transform, false);
            var statusRect = statusObject.GetComponent<RectTransform>();
            Stretch(statusRect);
            statusRect.offsetMin = new Vector2(16f, 16f);
            statusRect.offsetMax = new Vector2(-16f, -16f);

            var statusText = statusObject.GetComponent<Text>();
            statusText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            statusText.fontSize = 18;
            statusText.color = Color.white;
            statusText.alignment = TextAnchor.UpperLeft;
            statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            statusText.verticalOverflow = VerticalWrapMode.Truncate;
            statusText.text = "Pose tracking status";

            var statusView = statusObject.GetComponent<PoseTrackingStatusView>();
            SetObject(statusView, "trackingController", trackingController);
            SetObject(statusView, "cameraSource", cameraSource);
            SetObject(statusView, "feedbackReceiver", feedbackReceiver);
            SetObject(statusView, "statusText", statusText);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        private static void SetObject(Object target, string propertyName, Object value)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.objectReferenceValue = value;
                serializedObject.ApplyModifiedProperties();
            }
        }

        private static void SetBool(Object target, string propertyName, bool value)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.boolValue = value;
                serializedObject.ApplyModifiedProperties();
            }
        }

        private static void SetFloat(Object target, string propertyName, float value)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.floatValue = value;
                serializedObject.ApplyModifiedProperties();
            }
        }

        private static void SetEnum(Object target, string propertyName, int value)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.enumValueIndex = value;
                serializedObject.ApplyModifiedProperties();
            }
        }

        private static void EnsureFolder(string parent, string child)
        {
            var path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static void EnsureSceneInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(scene => scene.path == ScenePath))
            {
                return;
            }

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
#endif
