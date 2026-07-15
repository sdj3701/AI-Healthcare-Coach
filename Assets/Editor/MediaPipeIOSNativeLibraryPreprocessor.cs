#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AIHealthcareCoach.Editor
{
    [InitializeOnLoad]
    public sealed class MediaPipeIOSNativeLibraryPreprocessor : IPreprocessBuildWithReport, IActiveBuildTargetChanged
    {
        private const string PackageName = "com.github.homuler.mediapipe";
        private const string ExpectedPackageVersion = "0.16.3";
        private const string PackageAssetRoot = "Packages/com.github.homuler.mediapipe";
        private const string PackageFrameworkAssetPath =
            PackageAssetRoot + "/Runtime/Plugins/iOS/MediaPipeUnity.framework";
        private const string PayloadRelativePath =
            "tools/MediaPipeNative/0.16.3/iOS/MediaPipeUnity.framework";
        private const string FrameworkBinaryName = "MediaPipeUnity";
        private const string FrameworkPlistName = "Info.plist";
        private const string ExpectedBinarySha256 =
            "F8A69E9067A052A1728E59407383F61B7D1744DAECF6338E6BE1E6A0C7D5D481";
        private const string ExpectedPlistSha256 =
            "6E04BB43C200AE4282566F3B377D0267C09616831954491AE43D24290DC91220";

        static MediaPipeIOSNativeLibraryPreprocessor()
        {
            EditorApplication.delayCall += RepairForActiveIosTarget;
        }

        public int callbackOrder => -10000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
            {
                return;
            }

            EnsureFrameworkAvailable(true);
        }

        public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget)
        {
            if (newTarget == BuildTarget.iOS)
            {
                EditorApplication.delayCall += RepairForActiveIosTarget;
            }
        }

        [MenuItem("AI Healthcare/iOS/Repair MediaPipe Native Framework")]
        public static void RepairFromMenu()
        {
            try
            {
                RepairForBatch();
                EditorUtility.DisplayDialog(
                    "MediaPipe iOS",
                    "MediaPipeUnity.framework is ready for the iOS build.",
                    "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("MediaPipe iOS repair failed", exception.Message, "OK");
            }
        }

        public static void RepairForBatch()
        {
            EnsureFrameworkAvailable(true);
            Debug.Log("MEDIAPIPE_IOS_FRAMEWORK_READY");
        }

        public static bool ValidateBundledPayload(out string error)
        {
            var payloadRoot = GetProjectPath(PayloadRelativePath);
            var binaryPath = Path.Combine(payloadRoot, FrameworkBinaryName);
            var plistPath = Path.Combine(payloadRoot, FrameworkPlistName);

            if (!File.Exists(binaryPath))
            {
                error = "Bundled MediaPipe iOS binary is missing: " + binaryPath;
                return false;
            }

            if (!File.Exists(plistPath))
            {
                error = "Bundled MediaPipe iOS Info.plist is missing: " + plistPath;
                return false;
            }

            if (!HasExpectedHash(binaryPath, ExpectedBinarySha256))
            {
                error = "Bundled MediaPipe iOS binary checksum does not match v" + ExpectedPackageVersion + ".";
                return false;
            }

            if (!HasExpectedHash(plistPath, ExpectedPlistSha256))
            {
                error = "Bundled MediaPipe iOS Info.plist checksum does not match v" + ExpectedPackageVersion + ".";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static void RepairForActiveIosTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
            {
                return;
            }

            try
            {
                EnsureFrameworkAvailable(true);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[MediaPipe iOS] Native framework repair failed. " +
                    "Use AI Healthcare > iOS > Repair MediaPipe Native Framework before building.\n" +
                    exception);
            }
        }

        private static void EnsureFrameworkAvailable(bool importAfterCopy)
        {
            if (!ValidateBundledPayload(out var payloadError))
            {
                throw new BuildFailedException(payloadError);
            }

            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(
                PackageAssetRoot + "/package.json");
            if (packageInfo == null || !string.Equals(packageInfo.name, PackageName, StringComparison.Ordinal))
            {
                throw new BuildFailedException("MediaPipe Unity package could not be resolved from " + PackageAssetRoot + ".");
            }

            if (!string.Equals(packageInfo.version, ExpectedPackageVersion, StringComparison.Ordinal))
            {
                throw new BuildFailedException(
                    "MediaPipe Unity package version is " + packageInfo.version +
                    ", but the bundled iOS framework is v" + ExpectedPackageVersion + ". Update both together.");
            }

            var targetFrameworkPath = Path.Combine(
                packageInfo.resolvedPath,
                "Runtime",
                "Plugins",
                "iOS",
                "MediaPipeUnity.framework");
            var targetBinaryPath = Path.Combine(targetFrameworkPath, FrameworkBinaryName);
            var targetPlistPath = Path.Combine(targetFrameworkPath, FrameworkPlistName);

            if (HasExpectedHash(targetBinaryPath, ExpectedBinarySha256) &&
                HasExpectedHash(targetPlistPath, ExpectedPlistSha256))
            {
                EnsureExecutableOnMac(targetBinaryPath);
                return;
            }

            var payloadRoot = GetProjectPath(PayloadRelativePath);
            Directory.CreateDirectory(targetFrameworkPath);
            CopyReplacing(Path.Combine(payloadRoot, FrameworkBinaryName), targetBinaryPath);
            CopyReplacing(Path.Combine(payloadRoot, FrameworkPlistName), targetPlistPath);
            EnsureExecutableOnMac(targetBinaryPath);

            if (!HasExpectedHash(targetBinaryPath, ExpectedBinarySha256) ||
                !HasExpectedHash(targetPlistPath, ExpectedPlistSha256))
            {
                throw new BuildFailedException("MediaPipe iOS framework repair completed, but checksum verification failed.");
            }

            if (importAfterCopy)
            {
                AssetDatabase.ImportAsset(
                    PackageFrameworkAssetPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }

            Debug.Log("[MediaPipe iOS] Restored native framework for package v" + ExpectedPackageVersion + ".");
        }

        private static string GetProjectPath(string relativePath)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        }

        private static bool HasExpectedHash(string path, string expectedHash)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            using var stream = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            var actualHash = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
            return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
        }

        private static void CopyReplacing(string sourcePath, string destinationPath)
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("MediaPipe iOS payload file is missing.", sourcePath);
            }

            if (File.Exists(destinationPath))
            {
                File.SetAttributes(destinationPath, FileAttributes.Normal);
            }

            File.Copy(sourcePath, destinationPath, true);
        }

        private static void EnsureExecutableOnMac(string binaryPath)
        {
            if (Application.platform != RuntimePlatform.OSXEditor)
            {
                return;
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/chmod",
                Arguments = "755 " + QuoteProcessArgument(binaryPath),
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit();
            if (process == null || process.ExitCode != 0)
            {
                throw new BuildFailedException("Could not set executable permission on " + binaryPath + ".");
            }
        }

        private static string QuoteProcessArgument(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
#endif
