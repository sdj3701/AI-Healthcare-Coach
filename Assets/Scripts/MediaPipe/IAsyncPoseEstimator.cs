using System;
using UnityEngine;

namespace AIHealthcareCoach.MediaPipe
{
    public enum AsyncPoseResultStatus
    {
        Waiting = 0,
        Ready = 1,
        Failed = 2
    }

    /// <summary>
    /// Optional latest-only pose contract for native backends that finish inference
    /// outside Unity's player loop. The submitted pixel buffer only has to remain
    /// valid for the duration of <see cref="TrySubmitFrame"/>.
    /// </summary>
    public interface IAsyncPoseEstimator
    {
        bool SupportsAsyncProcessing { get; }

        bool TrySubmitFrame(
            Color32[] rgbaPixels,
            int width,
            int height,
            long timestampMs,
            bool mirrored,
            int rotationAngle,
            out string errorMessage);

        AsyncPoseResultStatus TryGetLatestResult(
            out LandmarkFrame frame,
            out string errorMessage);

        void CancelPendingFrame();

        /// <summary>
        /// Rebuilds an asynchronous backend after its completion callback did not
        /// arrive before the caller's timeout. Ordinary lifecycle cancellation
        /// should continue to use <see cref="CancelPendingFrame"/>.
        /// </summary>
        bool TryRecoverFromTimeout(out string errorMessage);
    }
}
