using System;
using Rag.Healthcare.Rag.Runtime;
using UnityEngine;

namespace Rag.Healthcare.Pose.Session
{
    /// <summary>
    /// Pure lifecycle for either initial calibration or an already calibrated workout.
    /// Calibration uses Ready → Countdown → InWorkout, while an actual workout is
    /// restricted to InWorkout ↔ PausedOutOfFrame until the session ends.
    /// Consumes <see cref="PoseTrackingQualityReport"/> and full-body calibration; does not mutate them.
    /// </summary>
    public sealed class WorkoutSessionStateMachine
    {
        private readonly FullBodyCalibrationEvaluator calibrationEvaluator = new FullBodyCalibrationEvaluator();
        private CalibrationSettings settings;
        private float outOfFrameElapsed;
        private float pausedElapsed;
        private bool sessionActive;
        private bool calibrationConfirmedFired;

        public WorkoutSessionStateMachine()
            : this(null)
        {
        }

        public WorkoutSessionStateMachine(CalibrationSettings settings)
        {
            this.settings = settings ?? new CalibrationSettings();
            LatestCalibration = calibrationEvaluator.Latest;
            State = WorkoutTrackingState.ReadyForCalibration;
            SessionMode = WorkoutSessionMode.None;
        }

        public WorkoutTrackingState State { get; private set; }

        public float CountdownRemainingSeconds { get; private set; }

        public FullBodyCalibrationReport LatestCalibration { get; private set; }

        public CalibrationSettings Settings => settings;

        public bool AllowsPoseAnalysis =>
            sessionActive &&
            SessionMode == WorkoutSessionMode.Workout &&
            State == WorkoutTrackingState.InWorkout;

        public bool IsSessionActive => sessionActive;

        public WorkoutSessionMode SessionMode { get; private set; }

        public bool IsCalibrationSession =>
            sessionActive && SessionMode == WorkoutSessionMode.Calibration;

        public bool IsWorkoutSession =>
            sessionActive && SessionMode == WorkoutSessionMode.Workout;

        public event Action<WorkoutTrackingState> StateChanged;

        public event Action<float> CountdownTicked;

        public event Action CalibrationConfirmed;

        public void Configure(CalibrationSettings nextSettings)
        {
            settings = nextSettings ?? new CalibrationSettings();
        }

        /// <summary>
        /// Starts the one-time full-body calibration flow. Stable visibility hold,
        /// countdown, and countdown rollback all remain enabled in this mode.
        /// </summary>
        public void BeginCalibrationSession()
        {
            sessionActive = true;
            SessionMode = WorkoutSessionMode.Calibration;
            calibrationConfirmedFired = false;
            ResetSessionTracking();
            SetState(WorkoutTrackingState.ReadyForCalibration);
        }

        /// <summary>
        /// Starts an actual workout after dedicated calibration has completed.
        /// Analysis begins immediately and tracking loss can only pause the workout;
        /// it never re-enters calibration or starts another countdown.
        /// </summary>
        public void BeginWorkoutSession()
        {
            sessionActive = true;
            SessionMode = WorkoutSessionMode.Workout;
            calibrationConfirmedFired = true;
            ResetSessionTracking();
            SetState(WorkoutTrackingState.InWorkout);
        }

        /// <summary>
        /// Compatibility wrapper for callers that start the initial calibration flow.
        /// </summary>
        [Obsolete("Use BeginCalibrationSession() to make the session purpose explicit.")]
        public void BeginSession()
        {
            BeginCalibrationSession();
        }

        /// <summary>
        /// Compatibility wrapper for callers that start an already calibrated workout.
        /// </summary>
        [Obsolete("Use BeginWorkoutSession() to make the session purpose explicit.")]
        public void BeginCalibratedSession()
        {
            BeginWorkoutSession();
        }

        public void EndSession()
        {
            sessionActive = false;
            SessionMode = WorkoutSessionMode.None;
            calibrationConfirmedFired = false;
            ResetSessionTracking();
            SetState(WorkoutTrackingState.ReadyForCalibration);
        }

        public void Tick(
            JointTrackingFrame frame,
            PoseTrackingQualityReport quality,
            float deltaSeconds)
        {
            if (!sessionActive)
            {
                return;
            }

            var dt = Mathf.Max(0f, deltaSeconds);
            FullBodyCalibrationReport calibration = null;
            if (SessionMode == WorkoutSessionMode.Calibration)
            {
                calibration = calibrationEvaluator.Evaluate(frame, settings, quality, dt);
                LatestCalibration = calibration;
            }

            switch (State)
            {
                case WorkoutTrackingState.ReadyForCalibration:
                    TickReady(calibration);
                    break;
                case WorkoutTrackingState.CountingDown:
                    TickCountdown(calibration, dt);
                    break;
                case WorkoutTrackingState.InWorkout:
                    TickInWorkout(calibration, quality, dt);
                    break;
                case WorkoutTrackingState.PausedOutOfFrame:
                    TickPaused(calibration, quality, dt);
                    break;
            }
        }

        private void TickReady(FullBodyCalibrationReport calibration)
        {
            if (!calibration.IsCalibrated)
            {
                return;
            }

            CountdownRemainingSeconds = Mathf.Max(0f, settings.countdownSeconds);
            calibrationConfirmedFired = false;
            SetState(WorkoutTrackingState.CountingDown);
            CountdownTicked?.Invoke(CountdownRemainingSeconds);
        }

        private void TickCountdown(FullBodyCalibrationReport calibration, float dt)
        {
            if (!calibration.AllFullBodyVisible)
            {
                CountdownRemainingSeconds = 0f;
                calibrationEvaluator.ResetHold();
                SetState(WorkoutTrackingState.ReadyForCalibration);
                return;
            }

            CountdownRemainingSeconds = Mathf.Max(0f, CountdownRemainingSeconds - dt);
            CountdownTicked?.Invoke(CountdownRemainingSeconds);

            if (CountdownRemainingSeconds > 0f)
            {
                return;
            }

            if (!calibrationConfirmedFired)
            {
                calibrationConfirmedFired = true;
                CalibrationConfirmed?.Invoke();
            }

            outOfFrameElapsed = 0f;
            SetState(WorkoutTrackingState.InWorkout);
        }

        private void TickInWorkout(
            FullBodyCalibrationReport calibration,
            PoseTrackingQualityReport quality,
            float dt)
        {
            if (IsTrackingDegraded(calibration, quality))
            {
                outOfFrameElapsed += dt;
                if (outOfFrameElapsed >= Mathf.Max(0f, settings.outOfFrameGraceSeconds))
                {
                    outOfFrameElapsed = 0f;
                    pausedElapsed = 0f;
                    SetState(WorkoutTrackingState.PausedOutOfFrame);
                }

                return;
            }

            outOfFrameElapsed = 0f;
        }

        private void TickPaused(
            FullBodyCalibrationReport calibration,
            PoseTrackingQualityReport quality,
            float dt)
        {
            if (!IsTrackingDegraded(calibration, quality))
            {
                outOfFrameElapsed = 0f;
                pausedElapsed = 0f;
                SetState(WorkoutTrackingState.InWorkout);
                return;
            }

            // A calibrated workout must never fall back into the one-time
            // calibration pipeline. Keep it paused for any duration and resume on
            // the first recovered frame.
            if (SessionMode == WorkoutSessionMode.Workout)
            {
                return;
            }

            pausedElapsed += dt;
            if (pausedElapsed >= Mathf.Max(0f, settings.reReadyDebounceSeconds))
            {
                pausedElapsed = 0f;
                outOfFrameElapsed = 0f;
                CountdownRemainingSeconds = 0f;
                calibrationEvaluator.ResetHold();
                SetState(WorkoutTrackingState.ReadyForCalibration);
            }
        }

        private bool IsTrackingDegraded(
            FullBodyCalibrationReport calibration,
            PoseTrackingQualityReport quality)
        {
            if (SessionMode == WorkoutSessionMode.Workout)
            {
                // The one-time full-body calibration includes strict head/ankle
                // visibility requirements that are not valid workout pause gates.
                // During exercise, consume only the workout-specific quality signal.
                return quality == null || quality.State != PoseTrackingQualityState.Good;
            }

            if (quality != null && quality.State != PoseTrackingQualityState.Good)
            {
                return true;
            }

            return calibration != null &&
                   calibration.MinimumGroupScore < settings.pauseVisibilityThreshold;
        }

        private void ResetSessionTracking()
        {
            outOfFrameElapsed = 0f;
            pausedElapsed = 0f;
            CountdownRemainingSeconds = 0f;
            calibrationEvaluator.Reset();
            LatestCalibration = calibrationEvaluator.Latest;
        }

        private void SetState(WorkoutTrackingState next)
        {
            if (State == next)
            {
                return;
            }

            State = next;
            StateChanged?.Invoke(State);
        }
    }
}
