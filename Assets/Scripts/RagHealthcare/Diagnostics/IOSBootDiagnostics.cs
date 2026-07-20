using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rag.Healthcare.Diagnostics
{
    /// <summary>
    /// Emits early startup breadcrumbs so device consoles show whether managed
    /// code and the first scene actually begin after the native engine init.
    /// </summary>
    internal static class IOSBootDiagnostics
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            Debug.Log("[IOSBoot] SubsystemRegistration");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void OnBeforeSplashScreen()
        {
            Debug.Log("[IOSBoot] BeforeSplashScreen");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnBeforeSceneLoad()
        {
            Debug.Log("[IOSBoot] BeforeSceneLoad");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnAfterSceneLoad()
        {
            var scene = SceneManager.GetActiveScene();
            Debug.Log(
                $"[IOSBoot] AfterSceneLoad scene='{scene.name}' " +
                $"path='{scene.path}' buildIndex={scene.buildIndex} " +
                $"rootCount={scene.rootCount}");
        }
    }
}
