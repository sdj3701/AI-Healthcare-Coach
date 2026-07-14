using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace Rag.Healthcare.Content
{
    [Serializable]
    public sealed class ContentArtifact
    {
        public string relativePath;
        public string sha256;
        public long sizeBytes;
    }

    [Serializable]
    public sealed class ContentUpdatePackage
    {
        public string schemaVersion;
        public string packageVersion;
        public string minimumAppVersion;
        public ContentArtifact[] artifacts;
    }

    public sealed class ContentUpdateInstaller
    {
        private readonly string root = Path.Combine(Application.persistentDataPath, "content_updates");

        public bool InstallFromStaging(string manifestPath, string stagingDirectory, out string error)
        {
            error = string.Empty;
            try
            {
                var package = JsonUtility.FromJson<ContentUpdatePackage>(File.ReadAllText(manifestPath));
                if (package?.artifacts == null || string.IsNullOrWhiteSpace(package.packageVersion))
                {
                    error = "Update manifest is invalid.";
                    return false;
                }

                foreach (var artifact in package.artifacts)
                {
                    var source = SafeCombine(stagingDirectory, artifact.relativePath);
                    if (!File.Exists(source) || new FileInfo(source).Length != artifact.sizeBytes ||
                        !string.Equals(Hash(source), artifact.sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "Artifact verification failed: " + artifact.relativePath;
                        return false;
                    }
                }

                Directory.CreateDirectory(root);
                var target = SafeCombine(Path.Combine(root, "versions"), package.packageVersion);
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.CreateDirectory(target);
                foreach (var artifact in package.artifacts)
                {
                    var source = SafeCombine(stagingDirectory, artifact.relativePath);
                    var destination = SafeCombine(target, artifact.relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? target);
                    File.Copy(source, destination, true);
                }

                var currentPath = Path.Combine(root, "current.txt");
                var previousPath = Path.Combine(root, "previous.txt");
                if (File.Exists(currentPath)) File.WriteAllText(previousPath, File.ReadAllText(currentPath));
                File.WriteAllText(currentPath, package.packageVersion);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public bool Rollback(out string error)
        {
            error = string.Empty;
            try
            {
                var current = Path.Combine(root, "current.txt");
                var previous = Path.Combine(root, "previous.txt");
                if (!File.Exists(previous))
                {
                    error = "No previous content version is available.";
                    return false;
                }
                var previousVersion = File.ReadAllText(previous).Trim();
                var previousDirectory = SafeCombine(Path.Combine(root, "versions"), previousVersion);
                if (!Directory.Exists(previousDirectory))
                {
                    error = "Previous content directory is missing.";
                    return false;
                }
                File.WriteAllText(current, previousVersion);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static string SafeCombine(string rootPath, string relative)
        {
            var fullRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative ?? string.Empty));
            var prefix = fullRoot + Path.DirectorySeparatorChar;
            if (!string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase) &&
                !fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Path escapes the content update root.");
            return fullPath;
        }

        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
