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
        private const string MenuPath = "AI Healthcare Coach/Build/iOS Development Build";
        private const string DeveloperTeamId = "VBT88ZWM6D";

        [MenuItem(MenuPath)]
        public static void BuildFromMenu()
        {
            Build();
        }

        public static void Build()
        {
            EnsureIOSBuildTarget();
            ConfigureSigning();

            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes are configured in Build Profiles.");
            }

            var outputPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Build", "iOS"));

            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options =
                    BuildOptions.Development |
                    BuildOptions.AllowDebugging
            };

            Debug.Log(
                "Starting iOS development build with Profiler discovery and script debugging. " +
                "Profiler autoconnect is disabled because it crashes Unity 6000.3.18f1 " +
                "on the iPhone XS Max running iOS 18.7.9. Deep Profiling is intentionally disabled.");

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"iOS development build failed: {report.summary.result} " +
                    $"({report.summary.totalErrors} errors)");
            }

            Debug.Log($"iOS development build completed: {outputPath}");
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
    }
}
#endif
