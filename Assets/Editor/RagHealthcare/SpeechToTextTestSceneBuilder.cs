#if UNITY_EDITOR
using System.Linq;
using Rag.Healthcare.Speech;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rag.Healthcare.Editor
{
    [InitializeOnLoad]
    public static class SpeechToTextTestSceneBuilder
    {
        private const string SessionKey = "Rag.Healthcare.SpeechToTextTestSceneBuilder.Ran";
        private const string ScenePath = "Assets/Scenes/SpeechToTextTest.unity";

        static SpeechToTextTestSceneBuilder()
        {
            EditorApplication.delayCall += EnsureOncePerEditorSession;
        }

        [MenuItem("Rag/Speech/Create Speech-to-Text Test Scene")]
        public static void EnsureSpeechToTextTestScene()
        {
            EnsureFolder("Assets", "Scenes");

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                CreateScene();
            }

            EnsureSceneInBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void EnsureOncePerEditorSession()
        {
            if (SessionState.GetBool(SessionKey, false))
            {
                return;
            }

            SessionState.SetBool(SessionKey, true);
            EnsureSpeechToTextTestScene();
        }

        private static void CreateScene()
        {
            var previousScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            var camera = cameraObject.AddComponent<UnityEngine.Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.1f, 0.12f);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            cameraObject.AddComponent<AudioListener>();

            var lightObject = new GameObject("Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;

            var runtimeObject = new GameObject("Speech To Text Test Runtime");
            runtimeObject.AddComponent<SpeechToTextTestController>();

            EditorSceneManager.SaveScene(scene, ScenePath);

            if (previousScene.IsValid() && !string.IsNullOrEmpty(previousScene.path))
            {
                EditorSceneManager.OpenScene(previousScene.path, OpenSceneMode.Single);
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
