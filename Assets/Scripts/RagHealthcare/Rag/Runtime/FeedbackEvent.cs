using System.Collections.Generic;
using Rag.Healthcare.Pose;

namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class FeedbackEvent
    {
        public string Id;
        public string RuleId;
        public string Exercise;
        public string Joint;
        public string Side;
        public FeedbackSeverity Severity;
        public float Confidence;
        public float PersistenceRatio;
        public long TimestampUnixMilliseconds;
        public string TemplateText;
        public ExercisePhase Phase;
        public Dictionary<string, float> Evidence;
    }
}
