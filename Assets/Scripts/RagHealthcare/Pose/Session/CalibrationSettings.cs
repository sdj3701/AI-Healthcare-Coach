using System;
using UnityEngine;

namespace Rag.Healthcare.Pose.Session
{
    /// <summary>
    /// Tunable thresholds and timings for full-body session calibration (PBI-109).
    /// Separate from analysis-gate thresholds in <c>RealtimePoseRuleSettings</c>.
    /// </summary>
    [Serializable]
    public sealed class CalibrationSettings
    {
        [Range(0f, 1f)]
        public float calibrationVisibilityThreshold = 0.85f;

        [Range(0.5f, 5f)]
        public float calibrationHoldSeconds = 1.5f;

        [Range(1f, 5f)]
        public float countdownSeconds = 3f;

        [Range(0f, 1f)]
        public float pauseVisibilityThreshold = 0.60f;

        [Range(0.1f, 3f)]
        public float outOfFrameGraceSeconds = 0.5f;

        // Used only by the dedicated calibration flow after it has reached
        // InWorkout. Real workout sessions remain paused until tracking recovers.
        [Range(0.1f, 3f)]
        public float reReadyDebounceSeconds = 0.5f;

        public bool requireHeadLandmark = true;

        public bool runCalibrationProfileSampling = true;
    }
}
