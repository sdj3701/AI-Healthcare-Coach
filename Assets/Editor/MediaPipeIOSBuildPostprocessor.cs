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
                "platform :ios, '15.0'\n" +
                "use_frameworks!\n\n" +
                "target 'UnityFramework' do\n" +
                "  pod 'MediaPipeTasksVision'\n" +
                "end\n\n" +
                "target 'Unity-iPhone' do\n" +
                "end\n\n" +
                // Unity's libiPhone-lib.a and MediaPipe's static graph library both bundle
                // their own copies of minizip/zlib (unzOpen/unzClose ...), ICU (ucasemap/UCaseMap ...)
                // and internal threading (Thread::Thread) symbols. CocoaPods force-loads the
                // MediaPipe graph library into the UnityFramework target where Unity is also linked,
                // which produces "duplicate symbol" link errors. Rewriting -force_load to -load_hidden
                // demotes the graph library's symbols to hidden visibility so they no longer clash
                // with Unity's globals, while MediaPipe still resolves them internally.
                "post_install do |installer|\n" +
                "  support_files = File.join(installer.sandbox.root, 'Target Support Files', 'Pods-UnityFramework')\n" +
                "  Dir.glob(File.join(support_files, '*.xcconfig')).each do |xcconfig|\n" +
                "    contents = File.read(xcconfig)\n" +
                "    updated = contents.gsub('-force_load', '-load_hidden')\n" +
                "    File.write(xcconfig, updated) if updated != contents\n" +
                "  end\n" +
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

            project.SetBuildProperty(mainTarget, "IPHONEOS_DEPLOYMENT_TARGET", "15.0");
            project.SetBuildProperty(frameworkTarget, "IPHONEOS_DEPLOYMENT_TARGET", "15.0");
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
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-lc " + ShellQuote(command),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.EnvironmentVariables["LANG"] = "en_US.UTF-8";
                startInfo.EnvironmentVariables["LC_ALL"] = "en_US.UTF-8";

                var process = new Process
                {
                    StartInfo = startInfo
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
