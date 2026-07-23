using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;

namespace Rag.Healthcare.Diagnostics
{
    /// <summary>
    /// Emits early startup breadcrumbs through IOSDeviceConsoleLog so physical
    /// devices always print boot progress in Xcode Console (Release included).
    /// </summary>
    [Preserve]
    internal static class IOSBootDiagnostics
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void OnAfterAssembliesLoaded()
        {
            IOSDeviceConsoleLog.Write("[IOSBoot] AfterAssembliesLoaded");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            IOSDeviceConsoleLog.Write("[IOSBoot] SubsystemRegistration");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void OnBeforeSplashScreen()
        {
            IOSDeviceConsoleLog.Write("[IOSBoot] BeforeSplashScreen");
            TryDismissUnitySplash();
        }

        private static void TryDismissUnitySplash()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (SplashScreen.isFinished)
            {
                IOSDeviceConsoleLog.Write("[IOSBoot] Splash already finished");
                return;
            }

            SplashScreen.Stop(SplashScreen.StopBehavior.FadeOut);
            IOSDeviceConsoleLog.Write("[IOSBoot] SplashScreen.FadeOut");
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnBeforeSceneLoad()
        {
            IOSDeviceConsoleLog.Write("[IOSBoot] BeforeSceneLoad");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnAfterSceneLoad()
        {
            var scene = SceneManager.GetActiveScene();
            IOSDeviceConsoleLog.Write(
                "[IOSBoot] AfterSceneLoad scene='" + scene.name + "' " +
                "path='" + scene.path + "' buildIndex=" + scene.buildIndex +
                " rootCount=" + scene.rootCount);
        }
    }
}
