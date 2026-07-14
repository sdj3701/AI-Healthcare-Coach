using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Rag.Healthcare.Editor
{
    public static class MediaPipeInstallationVerifier
    {
        private const string ModelPath = "Assets/StreamingAssets/MediaPipe/pose_landmarker_lite.task";
        private const string PackageName = "com.github.homuler.mediapipe";

        [MenuItem("AI Healthcare/Verify MediaPipe Installation")]
        public static void VerifyMenu()
        {
            var report = Verify();
            if (report.success)
            {
                Debug.Log(report.message);
                EditorUtility.DisplayDialog("MediaPipe verification", report.message, "OK");
            }
            else
            {
                Debug.LogError(report.message);
                EditorUtility.DisplayDialog("MediaPipe verification failed", report.message, "OK");
            }
        }

        public static MediaPipeVerificationReport Verify()
        {
            var manifestPath = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "manifest.json");
            var manifest = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : string.Empty;
            var packageConfigured = manifest.IndexOf(PackageName, StringComparison.OrdinalIgnoreCase) >= 0;
            var model = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ModelPath);
            var modelInfo = new FileInfo(Path.Combine(Directory.GetCurrentDirectory(), ModelPath));
            var modelValid = model != null && modelInfo.Exists && modelInfo.Length > 1024;

            var success = packageConfigured && modelValid;
            return new MediaPipeVerificationReport
            {
                success = success,
                packageConfigured = packageConfigured,
                modelValid = modelValid,
                modelBytes = modelInfo.Exists ? modelInfo.Length : 0,
                message = success
                    ? $"MediaPipe package and pose model are configured ({modelInfo.Length:N0} bytes)."
                    : $"Package configured: {packageConfigured}; model valid: {modelValid}. Resolve Packages/manifest.json and verify {ModelPath}."
            };
        }
    }

    [Serializable]
    public sealed class MediaPipeVerificationReport
    {
        public bool success;
        public bool packageConfigured;
        public bool modelValid;
        public long modelBytes;
        public string message;
    }
}
