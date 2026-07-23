using System;
using Rag.Healthcare.Rag.Runtime;
using UnityEngine;

namespace Rag.Healthcare.Pose.Session
{
    /// <summary>
    /// Pure session lifecycle: Ready → Countdown → InWorkout ↔ PausedOutOfFrame.
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
        }

        public WorkoutTrackingState State { get; private set; }

        public float CountdownRemainingSeconds { get; private set; }

        public FullBodyCalibrationReport LatestCalibration { get; private set; }

        public CalibrationSettings Settings => settings;

        public bool AllowsPoseAnalysis => State == WorkoutTrackingState.InWorkout;

        public bool IsSessionActive => sessionActive;

        public event Action<WorkoutTrackingState> StateChanged;

        public event Action<float> CountdownTicked;

        public event Action CalibrationConfirmed;

        public void Configure(CalibrationSettings nextSettings)
        {
            settings = nextSettings ?? new CalibrationSettings();
        }

        public void BeginSession()
        {
            sessionActive = true;
            calibrationConfirmedFired = false;
            outOfFrameElapsed = 0f;
            pausedElapsed = 0f;
            CountdownRemainingSeconds = 0f;
            calibrationEvaluator.Reset();
            LatestCalibration = calibrationEvaluator.Latest;
            SetState(WorkoutTrackingState.ReadyForCalibration);
        }

        public void EndSession()
        {
            sessionActive = false;
            calibrationConfirmedFired = false;
            outOfFrameElapsed = 0f;
            pausedElapsed = 0f;
            CountdownRemainingSeconds = 0f;
            calibrationEvaluator.Reset();
            LatestCalibration = calibrationEvaluator.Latest;
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
            var calibration = calibrationEvaluator.Evaluate(frame, settings, quality, dt);
            LatestCalibration = calibration;

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
            if (quality != null && quality.State != PoseTrackingQualityState.Good)
            {
                return true;
            }

            return calibration != null &&
                   calibration.MinimumGroupScore < settings.pauseVisibilityThreshold;
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
