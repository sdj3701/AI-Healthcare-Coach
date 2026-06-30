using System;
using System.IO;
using UnityEngine;

namespace AIHealthcareCoach.MediaPipe
{
    public sealed class PoseDebugLandmarkLogger : IDisposable
    {
        private readonly StreamWriter writer;

        public PoseDebugLandmarkLogger(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            writer = new StreamWriter(path, false);
            FilePath = path;
        }

        public string FilePath { get; }

        public void Write(LandmarkFrame frame)
        {
            if (writer == null || frame == null)
            {
                return;
            }

            writer.WriteLine(JsonUtility.ToJson(frame));
            writer.Flush();
        }

        public void Dispose()
        {
            writer?.Dispose();
        }
    }
}
