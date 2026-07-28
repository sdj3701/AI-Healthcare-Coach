namespace Rag.Healthcare.Rag.Runtime
{
    public enum SquatBottomDecision
    {
        TrackingUnavailable,
        NotAtBottom,
        HipHeightFailed,
        KneeCollapseFailed,
        PersonalDepthFailed,
        Passed
    }

    public sealed class ExercisePhaseState
    {
        public string Exercise = "squat";
        public ExercisePhase CurrentPhase = ExercisePhase.Unknown;
        public ExercisePhase PreviousPhase = ExercisePhase.Unknown;
        public int RepCount;
        public long PhaseStartedAtUnixMilliseconds;
        public bool HasReachedBottomInCurrentRep;
        public float MinimumKneeAngleInCurrentRep = 180f;
        public bool HasHipToKneeDepth;
        public float CurrentHipToKneeDepth;
        public float MaximumHipToKneeDepthInCurrentRep = float.NegativeInfinity;
        public int ConsecutiveHipToKneeDepthFrames;
        public bool HasReachedHipToKneeDepthInCurrentRep;
        public bool HasReachedSecondaryDepthInCurrentRep;
        public bool HasIssuedShallowDepthFeedbackInCurrentRep;
        public bool HasIssuedBottomDecisionFeedbackInCurrentRep;
        public bool HasPassedBottomDecisionInCurrentRep;
        public bool HasKneeCollapseInCurrentRep;
        public float RequiredHipToKneeDepth;
        public float MaximumCountableBottomKneeAngle;
        public float MinimumBottomHipDrop;
        public float MaximumHipDropInCurrentRep;
        public float CurrentKneeWidthRatio;
        public SquatBottomDecision CurrentBottomDecision =
            SquatBottomDecision.NotAtBottom;
        public SquatBottomDecision LastCompletedBottomDecision =
            SquatBottomDecision.NotAtBottom;
        public bool HasCompletedSquatAttemptThisFrame;
        public float LastCompletedAttemptMinimumKneeAngle = 180f;
        public float LastCompletedAttemptMaximumHipDrop;
        public int PersonalDepthFailureSampleCount;
        public int PersonalDepthFailureSampleTarget = 3;
        public bool HasPersonalizedDepthProfile;
        public bool HasPendingPersonalizedDepthAnnouncement;
        public float AdaptiveBottomKneeAngle;
        public float EffectiveBottomKneeAngle;
        public int AdaptiveBottomSampleCount;
        public int AdaptiveBottomSampleTarget = 3;
    }
}
