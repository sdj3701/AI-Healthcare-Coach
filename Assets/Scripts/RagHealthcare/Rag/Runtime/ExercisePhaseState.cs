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
    }
}
