#if UNITY_EDITOR
using System.Collections.Generic;
using AIHealthcareCoach.Tts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AIHealthcareCoach.Editor
{
    [InitializeOnLoad]
    public static class TtsDemoSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/TtsDemo.unity";

        static TtsDemoSceneBuilder()
        {
            EditorApplication.delayCall += CreateSceneIfMissing;
        }

        [MenuItem("AI Healthcare Coach/Create TTS Demo Scene")]
        public static void CreateSceneFromMenu()
        {
            CreateScene();
        }

        public static void CreateSceneFromCommandLine()
        {
            CreateScene();
        }

        private static void CreateSceneIfMissing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                AddSceneToBuildSettings(ScenePath);
                return;
            }

            CreateScene();
        }

        private static void CreateScene()
        {
            EnsureFolder("Assets", "Scenes");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "TtsDemo";

            CreateCamera();
            CreateLight();
            CreateRuntime();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Created TTS demo scene at {ScenePath}");
        }

        private static void EnsureFolder(string parent, string child)
        {
            var path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 1.4f, -6f);
            cameraObject.transform.rotation = Quaternion.Euler(8f, 0f, 0f);

            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
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
            light.intensity = 1.1f;
        }

        private static void CreateRuntime()
        {
            var runtimeObject = new GameObject("TTS Runtime");
            runtimeObject.AddComponent<TtsController>();
            runtimeObject.AddComponent<TtsDemoView>();
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(scene => scene.path == scenePath))
            {
                return;
            }

            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
#endif
