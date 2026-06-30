using System;

namespace AIHealthcareCoach.MediaPipe
{
    public enum PoseExerciseFeedbackSeverity
    {
        Info,
        Warning,
        Critical
    }

    [Serializable]
    public sealed class PoseExerciseFeedbackMessage
    {
        public string id;
        public string text;
        public string jointName;
        public int jointId;
        public float confidence;
        public PoseExerciseFeedbackSeverity severity;
    }
}
