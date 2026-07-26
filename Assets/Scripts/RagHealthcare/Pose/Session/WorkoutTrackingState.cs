namespace Rag.Healthcare.Pose.Session
{
    /// <summary>
    /// Identifies whether the active state-machine run is collecting the initial
    /// full-body calibration or tracking an already calibrated workout.
    /// </summary>
    public enum WorkoutSessionMode
    {
        None = 0,
        Calibration = 1,
        Workout = 2
    }

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

        /// <summary>
        /// Body left the frame during workout; analysis is paused until tracking recovers.
        /// Dedicated calibration flows may still return to calibration after their legacy timeout.
        /// </summary>
        PausedOutOfFrame = 3
    }
}
