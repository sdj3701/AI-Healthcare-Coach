#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
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
        private const string AffectedUnityVersion = "6000.3.18f1";
        private const string SharedBeeCachePath =
            "$HOME/Library/Unity/cache/bee";
        private const string ProjectLocalBeeCachePath =
            "$PROJECT_DIR/Il2CppBuildCache/$CONFIGURATION";

        [MenuItem(DevelopmentMenuPath)]
        public static void BuildFromMenu()
        {
            Build();
        }

        public static void Build()
        {
            BuildInternal(
                BuildOptions.None,
                "iOS",
                "device",
                useDebugLaunchConfiguration: true);
        }

        [MenuItem(ReleaseMenuPath)]
        public static void BuildReleaseFromMenu()
        {
            BuildRelease();
        }

        public static void BuildRelease()
        {
            BuildInternal(
                BuildOptions.None,
                "iOS-Release",
                "release",
                useDebugLaunchConfiguration: false);
        }

        private static void BuildInternal(
            BuildOptions extraOptions,
            string outputSubdir,
            string label,
            bool useDebugLaunchConfiguration)
        {
            EnsureIOSBuildTarget();
            ConfigureSigning();
            var stableOptions = UseStableIOSBuildOptions(extraOptions);
            ForceStableProfilerSettings();

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

            // Unity Development Player is intentionally disabled on iOS because
            // 6000.3.18f1 can stall before the first scene on physical devices.
            // The Xcode Debug launch configuration still provides native logs.
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = stableOptions
            };

            Debug.Log(
                $"Starting iOS {label} build. Unity Development Player, profiler " +
                "autoconnect, Deep Profiling, Script Debugging, and debugger waits " +
                "are OFF. Clean Build Cache is ON and IL2CPP code generation uses " +
                "OptimizeSize. Metal Validation is OFF in ProjectSettings.asset.");

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"iOS {label} build failed: {report.summary.result} " +
                    $"({report.summary.totalErrors} errors)");
            }

            if (useDebugLaunchConfiguration)
            {
                ConfigureStableDeviceXcodeProject(outputPath);
            }

            Debug.Log($"iOS {label} build completed: {outputPath}");
        }

        private static void ConfigureStableDeviceXcodeProject(
            string outputPath)
        {
            var schemePath = Path.Combine(
                outputPath,
                "Unity-iPhone.xcodeproj",
                "xcshareddata",
                "xcschemes",
                "Unity-iPhone.xcscheme");
            if (!File.Exists(schemePath))
            {
                throw new FileNotFoundException(
                    "The generated Unity-iPhone shared scheme was not found.",
                    schemePath);
            }

            var original = File.ReadAllText(schemePath);
            var updated = UseDebugLaunchConfiguration(original);
            if (!string.Equals(original, updated, StringComparison.Ordinal))
            {
                File.WriteAllText(schemePath, updated);
            }

            Debug.Log(
                "Configured the stable iOS device Xcode LaunchAction to use Debug.");

            var projectPath = Path.Combine(
                outputPath,
                "Unity-iPhone.xcodeproj",
                "project.pbxproj");
            SanitizeGeneratedXcodeProject(projectPath);

            Debug.Log(
                "Sanitized duplicate iOS build phases and configured the " +
                "IL2CPP build to use an export-local Bee cache.");
        }

        internal static string UseDebugLaunchConfiguration(string schemeXml)
        {
            if (schemeXml == null)
            {
                throw new ArgumentNullException(nameof(schemeXml));
            }

            var launchStart = schemeXml.IndexOf(
                "<LaunchAction",
                StringComparison.Ordinal);
            if (launchStart < 0)
            {
                throw new InvalidDataException(
                    "The Xcode scheme does not contain a LaunchAction.");
            }

            var launchTagEnd = schemeXml.IndexOf('>', launchStart);
            if (launchTagEnd < 0)
            {
                throw new InvalidDataException(
                    "The Xcode scheme LaunchAction tag is incomplete.");
            }

            const string attributeName = "buildConfiguration";
            var attributeStart = schemeXml.IndexOf(
                attributeName,
                launchStart,
                launchTagEnd - launchStart,
                StringComparison.Ordinal);
            if (attributeStart < 0)
            {
                throw new InvalidDataException(
                    "The Xcode scheme LaunchAction has no buildConfiguration.");
            }

            var valueStart = schemeXml.IndexOf('"', attributeStart);
            var valueEnd = valueStart < 0
                ? -1
                : schemeXml.IndexOf('"', valueStart + 1);
            if (valueStart < 0 ||
                valueEnd < 0 ||
                valueEnd > launchTagEnd)
            {
                throw new InvalidDataException(
                    "The Xcode scheme LaunchAction buildConfiguration is invalid.");
            }

            return schemeXml.Substring(0, valueStart + 1) +
                   "Debug" +
                   schemeXml.Substring(valueEnd);
        }

        internal static string UseProjectLocalBeeCache(string projectText)
        {
            if (projectText == null)
            {
                throw new ArgumentNullException(nameof(projectText));
            }

            if (projectText.Contains(ProjectLocalBeeCachePath))
            {
                return projectText;
            }

            if (!projectText.Contains(SharedBeeCachePath))
            {
                throw new InvalidDataException(
                    "The Xcode IL2CPP build phase does not contain the " +
                    "expected shared Bee cache path.");
            }

            return projectText.Replace(
                SharedBeeCachePath,
                ProjectLocalBeeCachePath);
        }

        internal static BuildOptions UseStableIOSBuildOptions(
            BuildOptions requestedOptions)
        {
            if (!string.Equals(
                    Application.unityVersion,
                    AffectedUnityVersion,
                    StringComparison.Ordinal))
            {
                return requestedOptions;
            }

            // Unity 6000.3.18f1 Development Player can stall in
            // PlayerLoadFirstScene on a physical iPhone. Keep the clean IL2CPP
            // conversion used by the linker workaround, but never re-enable the
            // Development Player or its debugger/profiler connection paths.
            var safeOptions =
                requestedOptions &
                ~BuildOptions.Development &
                ~BuildOptions.ConnectWithProfiler &
                ~BuildOptions.AllowDebugging &
                ~BuildOptions.EnableDeepProfilingSupport &
                ~BuildOptions.WaitForPlayerConnection;
            return safeOptions | BuildOptions.CleanBuildCache;
        }

        internal static void SanitizeGeneratedXcodeProject(
            string projectPath)
        {
            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException(
                    "The generated Unity-iPhone Xcode project was not found.",
                    projectPath);
            }

            var original = File.ReadAllText(projectPath);
            var updated = RemoveDuplicateBuildPhaseReferences(original);
            updated = UseProjectLocalBeeCache(updated);
            if (!string.Equals(original, updated, StringComparison.Ordinal))
            {
                File.WriteAllText(projectPath, updated);
            }
        }

        internal static string RemoveDuplicateBuildPhaseReferences(
            string projectText)
        {
            if (projectText == null)
            {
                throw new ArgumentNullException(nameof(projectText));
            }

            var newline = projectText.Contains("\r\n") ? "\r\n" : "\n";
            var lines = projectText.Split(
                new[] { newline },
                StringSplitOptions.None);
            var output = new List<string>(lines.Length);
            var phaseIds = new HashSet<string>(StringComparer.Ordinal);
            var inBuildPhases = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!inBuildPhases &&
                    string.Equals(
                        trimmed,
                        "buildPhases = (",
                        StringComparison.Ordinal))
                {
                    inBuildPhases = true;
                    phaseIds.Clear();
                    output.Add(line);
                    continue;
                }

                if (inBuildPhases &&
                    string.Equals(trimmed, ");", StringComparison.Ordinal))
                {
                    inBuildPhases = false;
                    output.Add(line);
                    continue;
                }

                if (inBuildPhases &&
                    TryGetBuildPhaseId(trimmed, out var phaseId) &&
                    !phaseIds.Add(phaseId))
                {
                    continue;
                }

                output.Add(line);
            }

            return string.Join(newline, output);
        }

        private static bool TryGetBuildPhaseId(
            string line,
            out string phaseId)
        {
            phaseId = null;
            var commentStart = line.IndexOf(
                " /*",
                StringComparison.Ordinal);
            if (commentStart <= 0 || !line.EndsWith(",", StringComparison.Ordinal))
            {
                return false;
            }

            var candidate = line.Substring(0, commentStart).Trim();
            if (candidate.Length != 24)
            {
                return false;
            }

            foreach (var character in candidate)
            {
                var isDigit = character >= '0' && character <= '9';
                var isUpperHex = character >= 'A' && character <= 'F';
                if (!isDigit && !isUpperHex)
                {
                    return false;
                }
            }

            phaseId = candidate;
            return true;
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

        private static void ForceStableProfilerSettings()
        {
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.buildWithDeepProfilingSupport = false;
            EditorUserBuildSettings.waitForManagedDebugger = false;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.explicitNullChecks = true;
            EditorUserBuildSettings.development = false;
            ConfigureStableIOSIl2CppCodeGeneration();
        }

        internal static void ConfigureStableIOSIl2CppCodeGeneration()
        {
            // OptimizeSize avoids the split generic path that caused the first
            // URP RenderGraph linker failure. The build handler also forces a
            // clean conversion without enabling Unity Development Player.
            PlayerSettings.SetIl2CppCodeGeneration(
                NamedBuildTarget.iOS,
                Il2CppCodeGeneration.OptimizeSize);
        }
    }

    public sealed class IOSIl2CppBuildPreprocessor :
        IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
            {
                return;
            }

            IOSDevelopmentBuild.ConfigureStableIOSIl2CppCodeGeneration();
            Debug.Log(
                "Configured iOS IL2CPP code generation to OptimizeSize before export.");
        }
    }
}
#endif
