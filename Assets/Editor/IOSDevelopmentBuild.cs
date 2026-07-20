#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AIHealthcareCoach.Editor
{
    public static class IOSDevelopmentBuild
    {
        private const string DevelopmentMenuPath =
            "AI Healthcare Coach/Build/iOS Development Build";
        private const string ReleaseMenuPath =
            "AI Healthcare Coach/Build/iOS Release Build";
        private const string DeveloperTeamId = "VBT88ZWM6D";

        [MenuItem(DevelopmentMenuPath)]
        public static void BuildFromMenu()
        {
            Build();
        }

        public static void Build()
        {
            BuildInternal(BuildOptions.Development, "iOS", "development");
        }

        [MenuItem(ReleaseMenuPath)]
        public static void BuildReleaseFromMenu()
        {
            BuildRelease();
        }

        public static void BuildRelease()
        {
            BuildInternal(BuildOptions.None, "iOS-Release", "release");
        }

        private static void BuildInternal(
            BuildOptions extraOptions,
            string outputSubdir,
            string label)
        {
            EnsureIOSBuildTarget();
            ConfigureSigning();
            ForceStableProfilerSettings(
                (extraOptions & BuildOptions.Development) != 0);

            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes are configured in Build Profiles.");
            }

            var outputPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Build", outputSubdir));

            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }

            // Development keeps profiler support without Script Debugging. Profiler
            // autoconnect and debugger waits stay off; Metal Validation is disabled
            // separately in ProjectSettings.asset for stable physical-device startup.
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = extraOptions
            };

            Debug.Log(
                $"Starting iOS {label} build. Profiler autoconnect, Deep Profiling, " +
                "Script Debugging, and debugger waits are OFF. Metal Validation is OFF " +
                "in ProjectSettings.asset.");

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"iOS {label} build failed: {report.summary.result} " +
                    $"({report.summary.totalErrors} errors)");
            }

            Debug.Log($"iOS {label} build completed: {outputPath}");
        }

        private static void EnsureIOSBuildTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS)
            {
                return;
            }

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.iOS,
                    BuildTarget.iOS))
            {
                throw new InvalidOperationException(
                    "Could not switch to the iOS build target. Verify iOS Build Support is installed.");
            }
        }

        private static void ConfigureSigning()
        {
            PlayerSettings.iOS.appleDeveloperTeamID = DeveloperTeamId;
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
        }

        private static void ForceStableProfilerSettings(bool development)
        {
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.buildWithDeepProfilingSupport = false;
            EditorUserBuildSettings.waitForManagedDebugger = false;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.explicitNullChecks = true;
            EditorUserBuildSettings.development = development;
        }
    }
}
#endif
