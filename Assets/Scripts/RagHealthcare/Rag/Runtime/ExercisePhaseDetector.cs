using UnityEngine;

namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class ExercisePhaseDetector
    {
        private readonly ExercisePhaseState state = new ExercisePhaseState();

        public ExercisePhaseState State => state;

        public ExercisePhaseState Update(PoseFeatureFrame feature, RealtimePoseRuleSettings settings)
        {
            if (feature == null || !feature.HasLeftKneeAngle && !feature.HasRightKneeAngle)
            {
                SetPhase(ExercisePhase.Unknown, feature == null ? 0L : feature.TimestampUnixMilliseconds);
                return state;
            }

            var nextPhase = ResolvePhase(feature, settings);
            if (state.CurrentPhase == ExercisePhase.Bottom)
            {
                state.HasReachedBottomInCurrentRep = true;
            }

            if (state.CurrentPhase == ExercisePhase.Ascent &&
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
            return state;
        }

        public void Reset()
        {
            state.CurrentPhase = ExercisePhase.Unknown;
            state.PreviousPhase = ExercisePhase.Unknown;
            state.RepCount = 0;
            state.PhaseStartedAtUnixMilliseconds = 0L;
            state.HasReachedBottomInCurrentRep = false;
        }

        private static ExercisePhase ResolvePhase(PoseFeatureFrame feature, RealtimePoseRuleSettings settings)
        {
            if (feature.AverageKneeAngle >= settings.StandingKneeAngle)
            {
                return ExercisePhase.Standing;
            }

            if (feature.AverageKneeAngle <= settings.BottomKneeAngle)
            {
                return Mathf.Abs(feature.KneeAngleVelocityDegreesPerSecond) <= settings.PhaseVelocityDeadZoneDegreesPerSecond
                    ? ExercisePhase.Bottom
                    : feature.KneeAngleVelocityDegreesPerSecond < 0f
                        ? ExercisePhase.Descent
                        : ExercisePhase.Ascent;
            }

            if (feature.KneeAngleVelocityDegreesPerSecond < -settings.PhaseVelocityDeadZoneDegreesPerSecond)
            {
                return ExercisePhase.Descent;
            }

            if (feature.KneeAngleVelocityDegreesPerSecond > settings.PhaseVelocityDeadZoneDegreesPerSecond)
            {
                return ExercisePhase.Ascent;
            }

            return ExercisePhase.Unknown;
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
