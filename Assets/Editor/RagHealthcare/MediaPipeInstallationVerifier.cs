using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Rag.Healthcare.Editor
{
    public static class MediaPipeInstallationVerifier
    {
        private const string ModelPath = "Assets/StreamingAssets/MediaPipe/pose_landmarker_lite.task";
        private const string SwiftBridgePath = "Assets/Plugins/iOS/AHCMediaPipePoseBridge.swift";
        private const string BuildPostprocessorPath = "Assets/Editor/MediaPipeIOSBuildPostprocessor.cs";

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
            var absoluteBridgePath = Path.Combine(Directory.GetCurrentDirectory(), SwiftBridgePath);
            var bridgeSource = File.Exists(absoluteBridgePath)
                ? File.ReadAllText(absoluteBridgePath)
                : string.Empty;
            var asyncBridgeConfigured = bridgeSource.Contains("AHC_PoseGetBridgeVersion") &&
                                        bridgeSource.Contains("AHC_PoseSubmitRgba") &&
                                        bridgeSource.Contains("AHC_PoseTryConsumeLatest") &&
                                        bridgeSource.Contains("AHC_PoseCancelPending");
            var bridgeConfigured = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(SwiftBridgePath) != null &&
                                   AssetDatabase.LoadAssetAtPath<MonoScript>(BuildPostprocessorPath) != null &&
                                   asyncBridgeConfigured;
            var model = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ModelPath);
            var modelInfo = new FileInfo(Path.Combine(Directory.GetCurrentDirectory(), ModelPath));
            var modelValid = model != null && modelInfo.Exists && modelInfo.Length > 1024;

            var success = bridgeConfigured && modelValid;
            return new MediaPipeVerificationReport
            {
                success = success,
                packageConfigured = bridgeConfigured,
                asyncBridgeConfigured = asyncBridgeConfigured,
                modelValid = modelValid,
                modelBytes = modelInfo.Exists ? modelInfo.Length : 0,
                message = success
                    ? $"MediaPipe iOS Swift bridge and pose model are configured ({modelInfo.Length:N0} bytes)."
                    : $"iOS Swift bridge configured: {bridgeConfigured}; async ABI configured: {asyncBridgeConfigured}; model valid: {modelValid}. Verify {SwiftBridgePath}, {BuildPostprocessorPath}, and {ModelPath}."
            };
        }
    }

    [Serializable]
    public sealed class MediaPipeVerificationReport
    {
        public bool success;
        public bool packageConfigured;
        public bool asyncBridgeConfigured;
        public bool modelValid;
        public long modelBytes;
        public string message;
    }
}
