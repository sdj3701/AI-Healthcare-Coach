using UnityEngine;

namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class ExercisePhaseDetector
    {
        private readonly ExercisePhaseState state = new ExercisePhaseState();
        private float minimumKneeAngleInCurrentRep = 180f;
        private float kneeAngleAtRepStart = 180f;
        private float previousKneeVelocity;
        private float previousHipVelocity;
        private float standingHipCoordinate;
        private float standingKneeAngle;
        private float maximumHipDropInCurrentRep;
        private long bottomCandidateStartedAt;
        private bool repMotionStarted;
        private bool hasStandingHipReference;
        private bool hasStandingKneeReference;
        private bool hasObservedDescentInCurrentRep;

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
            // A direction reversal recognizes the phase bottom, but it does not by
            // itself prove useful squat depth. Keep these two facts separate so a
            // small near-standing wobble cannot complete a rep and a shallow full
            // movement can still enter Bottom for depth guidance.
            if (repMotionStarted && HasSufficientDepth(settings))
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
                previousHipVelocity = feature.HipCenterYVelocityPerSecond;
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
            ClearStandingReference();
            ResetRepMotion();
        }

        public void Suspend(long timestampUnixMilliseconds)
        {
            SetPhase(ExercisePhase.Unknown, timestampUnixMilliseconds);
            state.HasReachedBottomInCurrentRep = false;
            ClearStandingReference();
            ResetRepMotion();
        }

        private ExercisePhase ResolvePhase(PoseFeatureFrame feature, RealtimePoseRuleSettings settings)
        {
            var hasHipEvidence = TryGetHipDrop(feature, out var hipDrop);
            var standingHipTolerance = settings.StandingHipDropTolerance;
            var isStandingByHip = !hasHipEvidence || hipDrop <= standingHipTolerance;
            if (feature.AverageKneeAngle >= settings.StandingKneeAngle &&
                isStandingByHip)
            {
                UpdateStandingReference(feature);
                return ExercisePhase.Standing;
            }

            if (state.CurrentPhase == ExercisePhase.Standing &&
                feature.AverageKneeAngle >= settings.standingExitKneeAngle &&
                (!hasHipEvidence || hipDrop <= settings.MinimumRecognizableHipDrop))
            {
                UpdateStandingReference(feature);
                return ExercisePhase.Standing;
            }

            if (!repMotionStarted)
            {
                repMotionStarted = true;
                var kneeReference = hasStandingKneeReference
                    ? standingKneeAngle
                    : Mathf.Max(
                        settings.StandingKneeAngle,
                        feature.AverageKneeAngle);
                kneeAngleAtRepStart = Mathf.Max(
                    kneeReference,
                    feature.AverageKneeAngle);
                minimumKneeAngleInCurrentRep = feature.AverageKneeAngle;
                maximumHipDropInCurrentRep = hasHipEvidence
                    ? Mathf.Max(0f, hipDrop)
                    : 0f;
            }
            else
            {
                minimumKneeAngleInCurrentRep = Mathf.Min(minimumKneeAngleInCurrentRep, feature.AverageKneeAngle);
                if (hasHipEvidence)
                {
                    maximumHipDropInCurrentRep = Mathf.Max(
                        maximumHipDropInCurrentRep,
                        hipDrop);
                }
            }

            state.MinimumKneeAngleInCurrentRep = minimumKneeAngleInCurrentRep;

            var velocity = feature.KneeAngleVelocityDegreesPerSecond;
            var deadZone = settings.PhaseVelocityDeadZoneDegreesPerSecond;
            var hipVelocity = hasHipEvidence
                ? feature.HipCenterYVelocityPerSecond
                : 0f;
            var hipDeadZone = settings.PhaseHipVelocityDeadZonePerSecond;
            var wasDescending =
                hasObservedDescentInCurrentRep ||
                previousKneeVelocity < -deadZone ||
                previousHipVelocity > hipDeadZone;
            var isDescending =
                velocity < -deadZone ||
                hipVelocity > hipDeadZone;
            var isAscending =
                velocity > deadZone ||
                hipVelocity < -hipDeadZone;
            var stoppedDescending =
                velocity >= 0f &&
                (!hasHipEvidence || hipVelocity <= hipDeadZone);
            if (isDescending)
            {
                hasObservedDescentInCurrentRep = true;
            }

            var reversedUpward =
                wasDescending &&
                (isAscending || stoppedDescending);
            var kneeExcursion =
                Mathf.Max(0f, kneeAngleAtRepStart - minimumKneeAngleInCurrentRep);
            var hasRecognizableMotion =
                kneeExcursion >= settings.MinimumPhaseKneeAngleExcursion ||
                maximumHipDropInCurrentRep >= settings.MinimumRecognizableHipDrop;
            var reachedRecognizableDepth =
                minimumKneeAngleInCurrentRep <= settings.MaximumRecognizableBottomKneeAngle &&
                hasRecognizableMotion;
            var isWithinBottomZone =
                feature.AverageKneeAngle <= settings.BottomKneeAngle ||
                hasHipEvidence && hipDrop >= settings.MinimumBottomHipDrop;

            if (isWithinBottomZone && hasRecognizableMotion)
            {
                if (bottomCandidateStartedAt <= 0L)
                {
                    bottomCandidateStartedAt = feature.TimestampUnixMilliseconds;
                }
            }
            else
            {
                bottomCandidateStartedAt = 0L;
            }

            var bottomDwellMilliseconds = Mathf.RoundToInt(settings.minimumBottomDwellSeconds * 1000f);
            var heldAtBottom = bottomCandidateStartedAt > 0L &&
                               feature.TimestampUnixMilliseconds - bottomCandidateStartedAt >= bottomDwellMilliseconds;

            if (state.CurrentPhase == ExercisePhase.Bottom)
            {
                return isAscending ||
                       feature.AverageKneeAngle >= settings.bottomExitKneeAngle ||
                       hasHipEvidence && hipDrop < settings.MinimumRecognizableHipDrop
                    ? ExercisePhase.Ascent
                    : ExercisePhase.Bottom;
            }

            if (state.CurrentPhase != ExercisePhase.Ascent &&
                ((reversedUpward && reachedRecognizableDepth) ||
                 heldAtBottom && hasRecognizableMotion))
            {
                // Consume the descent/reversal latch on the first Bottom entry.
                // Otherwise every positive-velocity ascent frame could be mistaken
                // for another reversal and oscillate Ascent → Bottom.
                hasObservedDescentInCurrentRep = false;
                return ExercisePhase.Bottom;
            }

            if (isDescending)
            {
                return ExercisePhase.Descent;
            }

            if (isAscending)
            {
                return ExercisePhase.Ascent;
            }

            if (state.CurrentPhase == ExercisePhase.Descent || state.CurrentPhase == ExercisePhase.Ascent)
            {
                return state.CurrentPhase;
            }

            return ExercisePhase.Unknown;
        }

        private bool HasSufficientDepth(RealtimePoseRuleSettings settings)
        {
            var kneeDepthReached =
                minimumKneeAngleInCurrentRep <= settings.MaximumBottomKneeAngle;
            if (!hasStandingHipReference)
            {
                return kneeDepthReached;
            }

            var hasUsefulKneeMotion =
                kneeAngleAtRepStart - minimumKneeAngleInCurrentRep >=
                settings.MinimumPhaseKneeAngleExcursion;
            return maximumHipDropInCurrentRep >= settings.MinimumBottomHipDrop &&
                   (kneeDepthReached || hasUsefulKneeMotion);
        }

        private bool TryGetHipDrop(PoseFeatureFrame feature, out float hipDrop)
        {
            hipDrop = 0f;
            if (feature == null ||
                !feature.HasCenterBalance ||
                !feature.HasTorsoTilt ||
                feature.HipCenterY >= -Mathf.Epsilon)
            {
                return false;
            }

            if (!hasStandingHipReference)
            {
                standingHipCoordinate = feature.HipCenterY;
                hasStandingHipReference = true;
            }

            hipDrop = feature.HipCenterY - standingHipCoordinate;
            return true;
        }

        private void UpdateStandingReference(PoseFeatureFrame feature)
        {
            UpdateStandingKneeReference(feature);
            if (feature == null ||
                !feature.HasCenterBalance ||
                !feature.HasTorsoTilt ||
                feature.HipCenterY >= -Mathf.Epsilon)
            {
                return;
            }

            if (!hasStandingHipReference)
            {
                standingHipCoordinate = feature.HipCenterY;
                hasStandingHipReference = true;
                return;
            }

            // Slow baseline adaptation absorbs small camera/user placement drift
            // without chasing the hip during a squat.
            standingHipCoordinate =
                Mathf.Lerp(standingHipCoordinate, feature.HipCenterY, 0.1f);
        }

        private void UpdateStandingKneeReference(PoseFeatureFrame feature)
        {
            if (feature == null)
            {
                return;
            }

            if (!hasStandingKneeReference)
            {
                standingKneeAngle = feature.AverageKneeAngle;
                hasStandingKneeReference = true;
                return;
            }

            // Adapt only toward a slightly straighter observed standing pose. The
            // bounded EMA avoids turning one noisy 180° frame into an exaggerated
            // excursion baseline for users whose natural standing angle is 165–170°.
            var boundedMaximum = Mathf.Min(
                Mathf.Max(standingKneeAngle, feature.AverageKneeAngle),
                standingKneeAngle + 3f);
            standingKneeAngle =
                Mathf.Lerp(standingKneeAngle, boundedMaximum, 0.2f);
        }

        private void ClearStandingReference()
        {
            standingHipCoordinate = 0f;
            standingKneeAngle = 0f;
            hasStandingHipReference = false;
            hasStandingKneeReference = false;
        }

        private void ResetRepMotion()
        {
            minimumKneeAngleInCurrentRep = 180f;
            kneeAngleAtRepStart = hasStandingKneeReference
                ? standingKneeAngle
                : 0f;
            state.MinimumKneeAngleInCurrentRep = 180f;
            previousKneeVelocity = 0f;
            previousHipVelocity = 0f;
            maximumHipDropInCurrentRep = 0f;
            bottomCandidateStartedAt = 0L;
            repMotionStarted = false;
            hasObservedDescentInCurrentRep = false;
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
