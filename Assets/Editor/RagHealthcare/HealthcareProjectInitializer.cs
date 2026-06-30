#if UNITY_EDITOR
using System.Linq;
using Rag.Healthcare.Pose;
using Rag.Healthcare.Tts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Rag.Healthcare.Editor
{
    [InitializeOnLoad]
    public static class HealthcareProjectInitializer
    {
        private const string SessionKey = "Rag.Healthcare.ProjectInitializer.Ran";
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const string UrpAssetPath = "Assets/Settings/Rendering/HealthcareURPAsset.asset";

        static HealthcareProjectInitializer()
        {
            EditorApplication.delayCall += RunOnce;
        }

        private static void RunOnce()
        {
            if (SessionState.GetBool(SessionKey, false))
            {
                return;
            }

            SessionState.SetBool(SessionKey, true);

            EnsureFolder("Assets", "Scenes");
            EnsureFolder("Assets", "Settings");
            EnsureFolder("Assets/Settings", "Rendering");

            EnsureUrpPipelineAsset();
            EnsureMainScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void EnsureFolder(string parent, string child)
        {
            var path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static void EnsureUrpPipelineAsset()
        {
            var urpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (urpAsset == null)
            {
                urpAsset = UniversalRenderPipelineAsset.Create();
                urpAsset.name = "HealthcareURPAsset";
                urpAsset.supportsCameraOpaqueTexture = true;
                urpAsset.useSRPBatcher = true;
                AssetDatabase.CreateAsset(urpAsset, UrpAssetPath);
            }

#if UNITY_6000_0_OR_NEWER
            if (GraphicsSettings.defaultRenderPipeline == null)
            {
                GraphicsSettings.defaultRenderPipeline = urpAsset;
            }
#else
            if (GraphicsSettings.renderPipelineAsset == null)
            {
                GraphicsSettings.renderPipelineAsset = urpAsset;
            }
#endif

            if (QualitySettings.renderPipeline == null)
            {
                QualitySettings.renderPipeline = urpAsset;
            }
        }

        private static void EnsureMainScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainScenePath) != null)
            {
                EnsureSceneInBuildSettings();
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 1.4f, -6f);
            cameraObject.transform.rotation = Quaternion.Euler(8f, 0f, 0f);
            var camera = cameraObject.AddComponent<UnityEngine.Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            cameraObject.AddComponent<AudioListener>();

            var lightObject = new GameObject("Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;

            var runtimeObject = new GameObject("Coach Runtime");
            runtimeObject.AddComponent<CoachTtsController>();
            runtimeObject.AddComponent<PoseFeedbackJsonReceiver>();

            EditorSceneManager.SaveScene(scene, MainScenePath);
            EnsureSceneInBuildSettings();
        }

        private static void EnsureSceneInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(scene => scene.path == MainScenePath))
            {
                return;
            }

            scenes.Insert(0, new EditorBuildSettingsScene(MainScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
#endif
