#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AIHealthcareCoach.Editor
{
    /// <summary>
    /// Normalizes Data/boot.config after each iOS export so Development builds
    /// cannot keep Connect/autoconnect or script-debugger waits that stall
    /// startup on physical devices (see docs/ios-black-screen-editor-vs-device.md).
    /// </summary>
    public sealed class IOSBootConfigPostprocessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => 1000;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
            {
                return;
            }

            var bootConfigPath = Path.Combine(report.summary.outputPath, "Data", "boot.config");
            if (!File.Exists(bootConfigPath))
            {
                Debug.LogWarning("[IOSBootConfig] boot.config not found: " + bootConfigPath);
                return;
            }

            var sanitized = SanitizeBootConfig(File.ReadAllLines(bootConfigPath));
            File.WriteAllLines(bootConfigPath, sanitized);
            Debug.Log("[IOSBootConfig] Sanitized boot.config at " + bootConfigPath);
        }

        internal static List<string> SanitizeBootConfig(IEnumerable<string> lines)
        {
            var output = new List<string>();

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();
                if (line.Length == 0)
                {
                    continue;
                }

                // PlayerConnection boot entries stall some iOS Development exports
                // before the first scene load. Release exports omit them entirely.
                if (line.StartsWith("player-connection-"))
                {
                    continue;
                }

                if (line.StartsWith("wait-for-native-debugger=") ||
                    line.StartsWith("wait-for-managed-debugger="))
                {
                    output.Add(line.Split('=')[0] + "=0");
                    continue;
                }

                output.Add(line);
            }

            return output;
        }
    }
}
#endif
