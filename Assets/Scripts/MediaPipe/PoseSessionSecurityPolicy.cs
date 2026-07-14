using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace AIHealthcareCoach.MediaPipe
{
    public sealed class PoseSessionSecurityPolicy
    {
        private static readonly HashSet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".json", ".jsonl", ".gz"
        };

        public bool ValidateNoRawMedia(string rootDirectory, out string[] violations)
        {
            var found = new List<string>();
            if (Directory.Exists(rootDirectory))
            {
                foreach (var file in Directory.GetFiles(rootDirectory, "*", SearchOption.AllDirectories))
                {
                    if (!AllowedExtensions.Contains(Path.GetExtension(file))) found.Add(file);
                }
            }
            violations = found.ToArray();
            return violations.Length == 0;
        }

        public string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
            {
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
