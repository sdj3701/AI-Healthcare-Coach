#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AIHealthcareCoach.Editor
{
    /// <summary>
    /// Keeps iOS-related EditorUserBuildSettings aligned with
    /// docs/ios-black-screen-editor-vs-device.md so Build Settings UI
    /// cannot silently re-enable Autoconnect / script debugging.
    /// ProjectSettings.metalAPIValidation stays OFF in ProjectSettings.asset.
    /// </summary>
    [InitializeOnLoad]
    public static class IOSStableBuildSettings
    {
        static IOSStableBuildSettings()
        {
            EditorApplication.delayCall += ApplySafeDefaults;
        }

        [MenuItem("AI Healthcare Coach/Build/Apply Safe iOS Build Settings")]
        public static void ApplySafeDefaultsFromMenu()
        {
            ApplySafeDefaults();
            Debug.Log(
                "Applied safe iOS build settings: Autoconnect Profiler OFF, " +
                "Script Debugging OFF, wait-for-debugger OFF, Deep Profiling OFF. " +
                "Metal API Validation must stay OFF in Player Settings.");
        }

        private static void ApplySafeDefaults()
        {
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.waitForManagedDebugger = false;
            EditorUserBuildSettings.buildWithDeepProfilingSupport = false;
        }
    }
}
#endif
