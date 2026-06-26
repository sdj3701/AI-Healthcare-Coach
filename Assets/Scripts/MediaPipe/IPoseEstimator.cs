using System;
using UnityEngine;

namespace AIHealthcareCoach.MediaPipe
{
    public interface IPoseEstimator : IDisposable
    {
        string BackendName { get; }
        bool IsReady { get; }
        string LastError { get; }

        bool Initialize(PoseEstimatorSettings settings);

        bool TryProcessFrame(
            Color32[] rgbaPixels,
            int width,
            int height,
            long timestampMs,
            bool mirrored,
            int rotationAngle,
            out LandmarkFrame frame);
    }
}
