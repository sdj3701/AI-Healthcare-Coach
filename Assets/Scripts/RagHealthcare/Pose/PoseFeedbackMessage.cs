using System;

namespace Rag.Healthcare.Pose
{
    [Serializable]
    public sealed class PoseFeedbackMessage
    {
        public string id;
        public string text;
        public string joint;
        public float confidence;
        public FeedbackSeverity severity;
    }
}
