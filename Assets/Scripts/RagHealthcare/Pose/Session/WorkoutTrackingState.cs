namespace Rag.Healthcare.Pose.Session
{
    /// <summary>
    /// Session-level pose tracking lifecycle for full-body calibration and workout gating.
    /// </summary>
    public enum WorkoutTrackingState
    {
        /// <summary>Waiting for stable full-body visibility (guide silhouette shown).</summary>
        ReadyForCalibration = 0,

        /// <summary>Full-body hold satisfied; countdown before analysis starts.</summary>
        CountingDown = 1,

        /// <summary>Pose analysis and feedback are active.</summary>
        InWorkout = 2,

        /// <summary>Body left the frame during workout; analysis paused pending recovery or re-calibration.</summary>
        PausedOutOfFrame = 3
    }
}
