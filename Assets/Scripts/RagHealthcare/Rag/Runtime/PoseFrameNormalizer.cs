using Rag.Healthcare.Pose;

namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class PoseFrameNormalizer
    {
        private readonly PoseFrameView frameView = new PoseFrameView();

        public PoseFrameView Normalize(JointTrackingFrame frame, float minimumVisibility)
        {
            frameView.Reset(frame, minimumVisibility);
            return frameView;
        }
    }
}
