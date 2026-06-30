using System;
using System.IO;
using UnityEngine;

namespace AIHealthcareCoach.MediaPipe
{
    public sealed class PoseStorageRetentionPolicy
    {
        public int DeleteExpiredDebugLogs(string debugDirectory, float retentionHours)
        {
            if (string.IsNullOrWhiteSpace(debugDirectory) || !Directory.Exists(debugDirectory))
            {
                return 0;
            }

            var deletedCount = 0;
            var cutoffUtc = DateTime.UtcNow.AddHours(-Mathf.Max(1f, retentionHours));
            var files = Directory.GetFiles(debugDirectory, "*", SearchOption.TopDirectoryOnly);
            for (var i = 0; i < files.Length; i++)
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(files[i]) > cutoffUtc)
                    {
                        continue;
                    }

                    File.Delete(files[i]);
                    deletedCount++;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[PoseStorageRetentionPolicy] Failed to delete expired debug log: " + exception.Message);
                }
            }

            return deletedCount;
        }
    }
}
