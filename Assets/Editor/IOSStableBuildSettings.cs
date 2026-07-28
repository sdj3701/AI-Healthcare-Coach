#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AIHealthcareCoach.Editor
{
    /// <summary>
    /// Keeps iOS-related EditorUserBuildSettings aligned with
    /// docs/troubleshooting/ios-black-screen-editor-vs-device.md so Build Settings UI
    /// cannot silently re-enable Autoconnect / script debugging.
    /// ProjectSettings.metalAPIValidation stays OFF in ProjectSettings.asset.
    /// </summary>
    [InitializeOnLoad]
    public static class IOSStableBuildSettings
    {
        static IOSStableBuildSettings()
        {
            EditorApplication.delayCall += ApplySafeDefaults;
            BuildPlayerWindow.RegisterBuildPlayerHandler(
                BuildPlayerWithSafeIOSDefaults);
        }

        [MenuItem("AI Healthcare Coach/Build/Apply Safe iOS Build Settings")]
        public static void ApplySafeDefaultsFromMenu()
        {
            ApplySafeDefaults();
            Debug.Log(
                "Applied safe iOS build settings: Autoconnect Profiler OFF, " +
                "Unity Development Player OFF, Script Debugging OFF, " +
                "wait-for-debugger OFF, Deep Profiling OFF. " +
                "Metal API Validation must stay OFF in Player Settings.");
        }

        private static void ApplySafeDefaults()
        {
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.waitForManagedDebugger = false;
            EditorUserBuildSettings.buildWithDeepProfilingSupport = false;

            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS)
            {
                EditorUserBuildSettings.development = false;
            }
        }

        private static void BuildPlayerWithSafeIOSDefaults(
            BuildPlayerOptions options)
        {
            if (options.target == BuildTarget.iOS)
            {
                ApplySafeDefaults();
                IOSDevelopmentBuild
                    .ConfigureStableIOSIl2CppCodeGeneration();
                options.options =
                    IOSDevelopmentBuild.UseStableIOSBuildOptions(
                        options.options);
                Debug.Log(
                    "Building iOS with the Unity 6000.3.18f1 IL2CPP " +
                    "compatibility mode: Unity Development Player is OFF and " +
                    "Clean Build Cache is ON; Script Debugging and profiler " +
                    "autoconnect are OFF.");
            }

            BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
        }
    }
}
#endif
