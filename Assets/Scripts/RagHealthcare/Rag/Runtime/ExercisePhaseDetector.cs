using UnityEngine;

namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class ExercisePhaseDetector
    {
        private const float SlowDirectionDeadZoneRatio = 0.15f;
        private const float MinimumKneeDirectionVelocity = 1f;
        private const float MinimumHipDirectionVelocity = 0.01f;

        private readonly ExercisePhaseState state = new ExercisePhaseState();
        private readonly float[] personalDepthKneeAngleSamples =
            new float[6];
        private readonly float[] personalDepthHipDropSamples =
            new float[6];
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
            state.HasCompletedSquatAttemptThisFrame = false;
            if (feature == null || !feature.HasLeftKneeAngle && !feature.HasRightKneeAngle)
            {
                SetPhase(ExercisePhase.Unknown, feature == null ? 0L : feature.TimestampUnixMilliseconds);
                state.HasReachedBottomInCurrentRep = false;
                ResetRepMotion();
                return state;
            }

            UpdateCurrentDepthState(feature, settings);
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
                LearnAcceptedBottom(settings);
                state.HasReachedBottomInCurrentRep = false;
            }

            if (state.CurrentPhase != ExercisePhase.Standing &&
                state.CurrentPhase != ExercisePhase.Unknown &&
                nextPhase == ExercisePhase.Standing &&
                repMotionStarted)
            {
                state.HasCompletedSquatAttemptThisFrame = true;
                state.LastCompletedBottomDecision =
                    state.CurrentBottomDecision;
                state.LastCompletedAttemptMinimumKneeAngle =
                    minimumKneeAngleInCurrentRep;
                state.LastCompletedAttemptMaximumHipDrop =
                    maximumHipDropInCurrentRep;
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

        public void Reset(bool preserveAdaptiveDepthProfile = false)
        {
            state.CurrentPhase = ExercisePhase.Unknown;
            state.PreviousPhase = ExercisePhase.Unknown;
            state.PhaseStartedAtUnixMilliseconds = 0L;
            state.HasReachedBottomInCurrentRep = false;
            if (!preserveAdaptiveDepthProfile)
            {
                state.RepCount = 0;
                state.AdaptiveBottomKneeAngle = 0f;
                state.AdaptiveBottomSampleCount = 0;
                state.HasPersonalizedDepthProfile = false;
                state.MaximumCountableBottomKneeAngle = 0f;
                state.MinimumBottomHipDrop = 0f;
                state.HasPendingPersonalizedDepthAnnouncement = false;
            }
            ResetPersonalDepthFailureCandidates();
            ClearStandingReference();
            ResetRepMotion();
        }

        public void Suspend(long timestampUnixMilliseconds)
        {
            SetPhase(ExercisePhase.Unknown, timestampUnixMilliseconds);
            state.HasReachedBottomInCurrentRep = false;
            ResetPersonalDepthFailureCandidates();
            ClearStandingReference();
            ResetRepMotion();
        }

        public bool RegisterPersonalDepthFailureCandidate(
            float minimumKneeAngle,
            float maximumHipDrop,
            RealtimePoseRuleSettings settings)
        {
            if (settings == null ||
                minimumKneeAngle <= 0f ||
                minimumKneeAngle >= 180f ||
                maximumHipDrop < 0f)
            {
                ResetPersonalDepthFailureCandidates();
                return false;
            }

            var target = settings.PersonalDepthFailureSampleCount;
            state.PersonalDepthFailureSampleTarget = target;
            var index = state.PersonalDepthFailureSampleCount;
            if (index < 0 || index >= personalDepthKneeAngleSamples.Length)
            {
                ResetPersonalDepthFailureCandidates();
                index = 0;
            }

            personalDepthKneeAngleSamples[index] = minimumKneeAngle;
            personalDepthHipDropSamples[index] = maximumHipDrop;
            state.PersonalDepthFailureSampleCount = index + 1;
            if (state.PersonalDepthFailureSampleCount < target)
            {
                return false;
            }

            var minimumAngle = float.PositiveInfinity;
            var maximumAngle = float.NegativeInfinity;
            var minimumDrop = float.PositiveInfinity;
            var maximumDrop = float.NegativeInfinity;
            for (var i = 0; i < target; i++)
            {
                minimumAngle = Mathf.Min(
                    minimumAngle,
                    personalDepthKneeAngleSamples[i]);
                maximumAngle = Mathf.Max(
                    maximumAngle,
                    personalDepthKneeAngleSamples[i]);
                minimumDrop = Mathf.Min(
                    minimumDrop,
                    personalDepthHipDropSamples[i]);
                maximumDrop = Mathf.Max(
                    maximumDrop,
                    personalDepthHipDropSamples[i]);
            }

            var isConsistent =
                maximumAngle - minimumAngle <=
                    settings.MaximumPersonalDepthKneeAngleSpread &&
                maximumDrop - minimumDrop <=
                    settings.MaximumPersonalDepthHipDropSpread;
            if (!isConsistent)
            {
                ResetPersonalDepthFailureCandidates();
                return false;
            }

            var medianAngle = Median(
                personalDepthKneeAngleSamples,
                target);
            var medianDrop = Median(
                personalDepthHipDropSamples,
                target);
            state.MaximumCountableBottomKneeAngle = Mathf.Clamp(
                medianAngle + settings.PersonalizedKneeAngleMargin,
                settings.MaximumCountableBottomKneeAngle,
                settings.MaximumPersonalizedBottomKneeAngle);
            state.MinimumBottomHipDrop = Mathf.Clamp(
                medianDrop - settings.PersonalizedHipDropMargin,
                settings.MinimumPersonalizedBottomHipDrop,
                settings.MinimumBottomHipDrop);
            state.HasPersonalizedDepthProfile = true;
            state.HasPendingPersonalizedDepthAnnouncement = true;
            ResetPersonalDepthFailureCandidates();
            return true;
        }

        public void RejectPersonalDepthFailureCandidate()
        {
            ResetPersonalDepthFailureCandidates();
        }

        public bool ConsumePersonalizedDepthAnnouncement()
        {
            if (!state.HasPendingPersonalizedDepthAnnouncement)
            {
                return false;
            }

            state.HasPendingPersonalizedDepthAnnouncement = false;
            return true;
        }

        private ExercisePhase ResolvePhase(PoseFeatureFrame feature, RealtimePoseRuleSettings settings)
        {
            var hasHipEvidence = TryGetHipDrop(feature, out var hipDrop);
            var standingHipTolerance = settings.StandingHipDropTolerance;
            var isStandingByHip = !hasHipEvidence || hipDrop <= standingHipTolerance;
            if (feature.AverageKneeAngle >= settings.StandingKneeAngle &&
                isStandingByHip)
            {
                var isReturningUp =
                    feature.KneeAngleVelocityDegreesPerSecond >
                    settings.PhaseVelocityDeadZoneDegreesPerSecond ||
                    hasHipEvidence &&
                    feature.HipCenterYVelocityPerSecond <
                    -settings.PhaseHipVelocityDeadZonePerSecond;
                if (state.CurrentPhase == ExercisePhase.Descent &&
                    state.HasReachedBottomInCurrentRep &&
                    isReturningUp)
                {
                    // A fast ascent can move from a qualifying deep frame straight
                    // into the standing band between camera samples. Preserve one
                    // Ascent frame so the accepted rep is not discarded.
                    return ExercisePhase.Ascent;
                }

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
            state.MaximumHipDropInCurrentRep =
                maximumHipDropInCurrentRep;
            ObserveHipToKneeDepth(feature, settings);

            var velocity = feature.KneeAngleVelocityDegreesPerSecond;
            var deadZone = settings.PhaseVelocityDeadZoneDegreesPerSecond;
            var directionDeadZone = Mathf.Max(
                MinimumKneeDirectionVelocity,
                deadZone * SlowDirectionDeadZoneRatio);
            var hipVelocity = hasHipEvidence
                ? feature.HipCenterYVelocityPerSecond
                : 0f;
            var hipDeadZone = settings.PhaseHipVelocityDeadZonePerSecond;
            var hipDirectionDeadZone = Mathf.Max(
                MinimumHipDirectionVelocity,
                hipDeadZone * SlowDirectionDeadZoneRatio);
            var wasDescending =
                hasObservedDescentInCurrentRep ||
                previousKneeVelocity < -directionDeadZone ||
                previousHipVelocity > hipDirectionDeadZone;
            var isDescending =
                velocity < -directionDeadZone ||
                hipVelocity > hipDirectionDeadZone;
            var isAscending =
                velocity > directionDeadZone ||
                hipVelocity < -hipDirectionDeadZone;
            var stoppedDescending =
                velocity >= -directionDeadZone &&
                (!hasHipEvidence || hipVelocity <= hipDirectionDeadZone);
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
                feature.AverageKneeAngle <= state.EffectiveBottomKneeAngle ||
                hasHipEvidence &&
                hipDrop >= state.MinimumBottomHipDrop;

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
                // A user can pause at a shallow point, hear the cue, and continue
                // descending. Return to Descent so the stale bottom decision cannot
                // keep producing guidance while the correction is in progress.
                if (isDescending)
                {
                    return ExercisePhase.Descent;
                }

                return isAscending ||
                       feature.AverageKneeAngle >= settings.bottomExitKneeAngle ||
                       hasHipEvidence && hipDrop < settings.MinimumRecognizableHipDrop
                    ? ExercisePhase.Ascent
                    : ExercisePhase.Bottom;
            }

            if (state.CurrentPhase != ExercisePhase.Ascent &&
                ((reversedUpward && reachedRecognizableDepth) ||
                 heldAtBottom && hasRecognizableMotion && stoppedDescending))
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
            var hasUsefulKneeMotion =
                kneeAngleAtRepStart - minimumKneeAngleInCurrentRep >=
                settings.MinimumPhaseKneeAngleExcursion;
            var hasRecognizableMotion =
                hasUsefulKneeMotion ||
                maximumHipDropInCurrentRep >=
                settings.MinimumRecognizableHipDrop;
            return state.HasReachedHipToKneeDepthInCurrentRep &&
                   state.HasReachedSecondaryDepthInCurrentRep &&
                   hasRecognizableMotion;
        }

        private void UpdateCurrentDepthState(
            PoseFeatureFrame feature,
            RealtimePoseRuleSettings settings)
        {
            state.RequiredHipToKneeDepth =
                settings.MinimumAcceptedHipToKneeDepth;
            if (!state.HasPersonalizedDepthProfile)
            {
                state.MaximumCountableBottomKneeAngle =
                    settings.MaximumCountableBottomKneeAngle;
                state.MinimumBottomHipDrop =
                    settings.MinimumBottomHipDrop;
            }
            state.AdaptiveBottomSampleTarget = settings.AdaptiveBottomSampleCount;
            state.PersonalDepthFailureSampleTarget =
                settings.PersonalDepthFailureSampleCount;
            state.HasHipToKneeDepth = feature != null && feature.HasHipToKneeDepth;
            state.CurrentHipToKneeDepth = state.HasHipToKneeDepth
                ? feature.HipToKneeDepth
                : 0f;
            state.EffectiveBottomKneeAngle = ResolveEffectiveBottomKneeAngle(settings);
            state.MaximumHipDropInCurrentRep =
                maximumHipDropInCurrentRep;
        }

        private void ObserveHipToKneeDepth(
            PoseFeatureFrame feature,
            RealtimePoseRuleSettings settings)
        {
            if (!repMotionStarted || feature == null || !feature.HasHipToKneeDepth)
            {
                state.ConsecutiveHipToKneeDepthFrames = 0;
                return;
            }

            state.MaximumHipToKneeDepthInCurrentRep = Mathf.Max(
                state.MaximumHipToKneeDepthInCurrentRep,
                feature.HipToKneeDepth);
            if (feature.HipToKneeDepth >=
                settings.MinimumAcceptedHipToKneeDepth)
            {
                state.ConsecutiveHipToKneeDepthFrames++;
                if (state.ConsecutiveHipToKneeDepthFrames >=
                    settings.MinimumHipToKneeDepthFrames)
                {
                    state.HasReachedHipToKneeDepthInCurrentRep = true;
                }
            }
            else
            {
                state.ConsecutiveHipToKneeDepthFrames = 0;
            }

            if (minimumKneeAngleInCurrentRep <=
                    state.MaximumCountableBottomKneeAngle ||
                maximumHipDropInCurrentRep >=
                    state.MinimumBottomHipDrop)
            {
                state.HasReachedSecondaryDepthInCurrentRep = true;
            }
        }

        private void LearnAcceptedBottom(RealtimePoseRuleSettings settings)
        {
            var targetSamples = settings.AdaptiveBottomSampleCount;
            if (!state.HasReachedHipToKneeDepthInCurrentRep ||
                !state.HasReachedSecondaryDepthInCurrentRep ||
                minimumKneeAngleInCurrentRep <= 0f ||
                minimumKneeAngleInCurrentRep >= 180f ||
                state.AdaptiveBottomSampleCount >= targetSamples)
            {
                return;
            }

            var previousSampleCount = state.AdaptiveBottomSampleCount;
            state.AdaptiveBottomKneeAngle =
                previousSampleCount <= 0
                    ? minimumKneeAngleInCurrentRep
                    : (state.AdaptiveBottomKneeAngle * previousSampleCount +
                       minimumKneeAngleInCurrentRep) /
                      (previousSampleCount + 1);
            state.AdaptiveBottomSampleCount = previousSampleCount + 1;
            state.EffectiveBottomKneeAngle =
                ResolveEffectiveBottomKneeAngle(settings);
        }

        private float ResolveEffectiveBottomKneeAngle(
            RealtimePoseRuleSettings settings)
        {
            if (state.AdaptiveBottomSampleCount <= 0 ||
                state.AdaptiveBottomKneeAngle <= 0f)
            {
                return settings.BottomKneeAngle;
            }

            return Mathf.Clamp(
                state.AdaptiveBottomKneeAngle +
                settings.AdaptiveBottomKneeAngleMargin,
                settings.BottomKneeAngle,
                settings.MaximumRecognizableBottomKneeAngle);
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
            state.MaximumHipToKneeDepthInCurrentRep = float.NegativeInfinity;
            state.ConsecutiveHipToKneeDepthFrames = 0;
            state.HasReachedHipToKneeDepthInCurrentRep = false;
            state.HasReachedSecondaryDepthInCurrentRep = false;
            state.HasIssuedShallowDepthFeedbackInCurrentRep = false;
            state.HasIssuedBottomDecisionFeedbackInCurrentRep = false;
            state.HasPassedBottomDecisionInCurrentRep = false;
            state.HasKneeCollapseInCurrentRep = false;
            state.CurrentBottomDecision =
                SquatBottomDecision.NotAtBottom;
            state.CurrentKneeWidthRatio = 0f;
            previousKneeVelocity = 0f;
            previousHipVelocity = 0f;
            maximumHipDropInCurrentRep = 0f;
            state.MaximumHipDropInCurrentRep = 0f;
            bottomCandidateStartedAt = 0L;
            repMotionStarted = false;
            hasObservedDescentInCurrentRep = false;
        }

        private void ResetPersonalDepthFailureCandidates()
        {
            state.PersonalDepthFailureSampleCount = 0;
            for (var i = 0; i < personalDepthKneeAngleSamples.Length; i++)
            {
                personalDepthKneeAngleSamples[i] = 0f;
                personalDepthHipDropSamples[i] = 0f;
            }
        }

        private static float Median(float[] values, int count)
        {
            var sorted = new float[Mathf.Clamp(count, 0, values.Length)];
            for (var i = 0; i < sorted.Length; i++)
            {
                sorted[i] = values[i];
            }

            System.Array.Sort(sorted);
            if (sorted.Length == 0)
            {
                return 0f;
            }

            var middle = sorted.Length / 2;
            return sorted.Length % 2 == 0
                ? (sorted[middle - 1] + sorted[middle]) * 0.5f
                : sorted[middle];
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
