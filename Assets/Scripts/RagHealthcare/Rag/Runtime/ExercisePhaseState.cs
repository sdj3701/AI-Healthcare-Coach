namespace Rag.Healthcare.Rag.Runtime
{
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
        public float RequiredHipToKneeDepth;
        public float AdaptiveBottomKneeAngle;
        public float EffectiveBottomKneeAngle;
        public int AdaptiveBottomSampleCount;
        public int AdaptiveBottomSampleTarget = 3;
    }
}
