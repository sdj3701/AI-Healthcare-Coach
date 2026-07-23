using UnityEngine;

namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class ExercisePhaseDetector
    {
        private readonly ExercisePhaseState state = new ExercisePhaseState();
        private float minimumKneeAngleInCurrentRep = 180f;
        private float previousKneeVelocity;
        private long bottomCandidateStartedAt;
        private bool repMotionStarted;

        public ExercisePhaseState State => state;

        public ExercisePhaseState Update(PoseFeatureFrame feature, RealtimePoseRuleSettings settings)
        {
            if (feature == null || !feature.HasLeftKneeAngle && !feature.HasRightKneeAngle)
            {
                SetPhase(ExercisePhase.Unknown, feature == null ? 0L : feature.TimestampUnixMilliseconds);
                state.HasReachedBottomInCurrentRep = false;
                ResetRepMotion();
                return state;
            }

            var nextPhase = ResolvePhase(feature, settings);
            // Mark bottom as soon as this frame enters or stays in Bottom so same-frame
            // depth evaluation does not emit a CorrectRep-blocking shallow Warning.
            if (state.CurrentPhase == ExercisePhase.Bottom || nextPhase == ExercisePhase.Bottom)
            {
                state.HasReachedBottomInCurrentRep = true;
            }

            if ((state.CurrentPhase == ExercisePhase.Ascent || state.CurrentPhase == ExercisePhase.Bottom) &&
                nextPhase == ExercisePhase.Standing &&
                state.HasReachedBottomInCurrentRep)
            {
                state.RepCount++;
                state.HasReachedBottomInCurrentRep = false;
            }

            if (nextPhase == ExercisePhase.Standing && state.CurrentPhase == ExercisePhase.Standing)
            {
                state.HasReachedBottomInCurrentRep = false;
            }

            SetPhase(nextPhase, feature.TimestampUnixMilliseconds);
            state.Exercise = string.IsNullOrWhiteSpace(feature.Exercise) ? "squat" : feature.Exercise;

            if (nextPhase == ExercisePhase.Standing)
            {
                ResetRepMotion();
            }
            else
            {
                previousKneeVelocity = feature.KneeAngleVelocityDegreesPerSecond;
            }

            return state;
        }

        public void Reset()
        {
            state.CurrentPhase = ExercisePhase.Unknown;
            state.PreviousPhase = ExercisePhase.Unknown;
            state.RepCount = 0;
            state.PhaseStartedAtUnixMilliseconds = 0L;
            state.HasReachedBottomInCurrentRep = false;
            ResetRepMotion();
        }

        public void Suspend(long timestampUnixMilliseconds)
        {
            SetPhase(ExercisePhase.Unknown, timestampUnixMilliseconds);
            state.HasReachedBottomInCurrentRep = false;
            ResetRepMotion();
        }

        private ExercisePhase ResolvePhase(PoseFeatureFrame feature, RealtimePoseRuleSettings settings)
        {
            if (feature.AverageKneeAngle >= settings.StandingKneeAngle)
            {
                return ExercisePhase.Standing;
            }

            if (state.CurrentPhase == ExercisePhase.Standing &&
                feature.AverageKneeAngle >= settings.standingExitKneeAngle)
            {
                return ExercisePhase.Standing;
            }

            if (!repMotionStarted)
            {
                repMotionStarted = true;
                minimumKneeAngleInCurrentRep = feature.AverageKneeAngle;
            }
            else
            {
                minimumKneeAngleInCurrentRep = Mathf.Min(minimumKneeAngleInCurrentRep, feature.AverageKneeAngle);
            }

            state.MinimumKneeAngleInCurrentRep = minimumKneeAngleInCurrentRep;

            var velocity = feature.KneeAngleVelocityDegreesPerSecond;
            var deadZone = settings.PhaseVelocityDeadZoneDegreesPerSecond;
            var reversedUpward = previousKneeVelocity < -deadZone && velocity >= 0f;
            var reachedRecognizableDepth =
                minimumKneeAngleInCurrentRep <= settings.maximumRecognizableBottomKneeAngle;
            var isWithinBottomZone = feature.AverageKneeAngle <= settings.BottomKneeAngle;

            if (isWithinBottomZone)
            {
                if (bottomCandidateStartedAt <= 0L)
                {
                    bottomCandidateStartedAt = feature.TimestampUnixMilliseconds;
                }
            }
            else if (state.CurrentPhase != ExercisePhase.Bottom)
            {
                bottomCandidateStartedAt = 0L;
            }

            var bottomDwellMilliseconds = Mathf.RoundToInt(settings.minimumBottomDwellSeconds * 1000f);
            var heldAtBottom = bottomCandidateStartedAt > 0L &&
                               feature.TimestampUnixMilliseconds - bottomCandidateStartedAt >= bottomDwellMilliseconds;

            if ((reversedUpward && reachedRecognizableDepth) || heldAtBottom)
            {
                return ExercisePhase.Bottom;
            }

            if (state.CurrentPhase == ExercisePhase.Bottom)
            {
                return velocity > deadZone || feature.AverageKneeAngle >= settings.bottomExitKneeAngle
                    ? ExercisePhase.Ascent
                    : ExercisePhase.Bottom;
            }

            if (velocity < -deadZone)
            {
                return ExercisePhase.Descent;
            }

            if (velocity > deadZone)
            {
                return ExercisePhase.Ascent;
            }

            if (state.CurrentPhase == ExercisePhase.Descent || state.CurrentPhase == ExercisePhase.Ascent)
            {
                return state.CurrentPhase;
            }

            return ExercisePhase.Unknown;
        }

        private void ResetRepMotion()
        {
            minimumKneeAngleInCurrentRep = 180f;
            state.MinimumKneeAngleInCurrentRep = 180f;
            previousKneeVelocity = 0f;
            bottomCandidateStartedAt = 0L;
            repMotionStarted = false;
        }

        private void SetPhase(ExercisePhase nextPhase, long timestampUnixMilliseconds)
        {
            if (state.CurrentPhase == nextPhase)
            {
                return;
            }

            state.PreviousPhase = state.CurrentPhase;
            state.CurrentPhase = nextPhase;
            state.PhaseStartedAtUnixMilliseconds = timestampUnixMilliseconds;
        }
    }
}
