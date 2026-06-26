#if UNITY_EDITOR && UNITY_IOS
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AIHealthcareCoach.Editor
{
    public static class MediaPipeIOSBuildPostprocessor
    {
        [PostProcessBuild(999)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
            {
                return;
            }

            WritePodfile(pathToBuiltProject);
            UpdateXcodeProject(pathToBuiltProject);
            UpdatePlist(pathToBuiltProject);
            TryRunPodInstall(pathToBuiltProject);
        }

        private static void WritePodfile(string pathToBuiltProject)
        {
            var podfilePath = Path.Combine(pathToBuiltProject, "Podfile");
            var content =
                "platform :ios, '13.0'\n" +
                "use_frameworks!\n\n" +
                "target 'UnityFramework' do\n" +
                "  pod 'MediaPipeTasksVision'\n" +
                "end\n\n" +
                "target 'Unity-iPhone' do\n" +
                "end\n";

            File.WriteAllText(podfilePath, content);
            Debug.Log("Wrote MediaPipe Podfile: " + podfilePath);
        }

        private static void UpdateXcodeProject(string pathToBuiltProject)
        {
            var projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            var mainTarget = project.GetUnityMainTargetGuid();
            var frameworkTarget = project.GetUnityFrameworkTargetGuid();

            project.SetBuildProperty(mainTarget, "IPHONEOS_DEPLOYMENT_TARGET", "13.0");
            project.SetBuildProperty(frameworkTarget, "IPHONEOS_DEPLOYMENT_TARGET", "13.0");
            project.SetBuildProperty(frameworkTarget, "SWIFT_VERSION", "5.0");
            project.SetBuildProperty(frameworkTarget, "CLANG_ENABLE_MODULES", "YES");
            project.SetBuildProperty(frameworkTarget, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "NO");
            project.SetBuildProperty(mainTarget, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "YES");
            project.AddBuildProperty(frameworkTarget, "OTHER_LDFLAGS", "$(inherited)");

            project.WriteToFile(projectPath);
            Debug.Log("Updated iOS Xcode project for MediaPipe Swift bridge.");
        }

        private static void UpdatePlist(string pathToBuiltProject)
        {
            var plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString(
                "NSCameraUsageDescription",
                "Camera access is used to estimate body pose landmarks for exercise feedback.");
            plist.WriteToFile(plistPath);
        }

        private static void TryRunPodInstall(string pathToBuiltProject)
        {
            if (Application.platform != RuntimePlatform.OSXEditor)
            {
                Debug.Log("Skipping pod install because this editor is not running on macOS.");
                return;
            }

            try
            {
                var command = "cd " + ShellQuote(pathToBuiltProject) + " && pod install";
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = "-lc " + ShellQuote(command),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    Debug.Log("pod install completed for MediaPipeTasksVision.\n" + output);
                }
                else
                {
                    Debug.LogWarning("pod install failed. Open Terminal in the Xcode export folder and run 'pod install'.\n" + error);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not run pod install automatically: " + exception.Message);
            }
        }

        private static string ShellQuote(string value)
        {
            return "'" + value.Replace("'", "'\\''") + "'";
        }
    }
}
#endif
