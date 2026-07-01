using UnityEngine;

namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class PoseWindowStats
    {
        public int FrameCount;
        public int ValidCoreFrameCount;
        public float ValidCoreFrameRatio;
        public float AverageKneeAngle;
        public float AverageTorsoTiltDegrees;
        public float AverageCenterBalanceOffset;
        public float AverageLeftKneeValgusOffset;
        public float AverageRightKneeValgusOffset;
        public float AverageKneeSymmetryDelta;
        public float KneeAlignmentViolationRatio;
        public float TorsoTiltViolationRatio;
        public float CenterBalanceViolationRatio;
        public float AverageValidityScore;
        public PoseFeatureFrame LatestFrame;

        public static PoseWindowStats Calculate(PoseWindowBuffer buffer, RealtimePoseRuleSettings settings)
        {
            var stats = new PoseWindowStats();
            if (buffer == null)
            {
                return stats;
            }

            var kneeAngleCount = 0;
            var torsoCount = 0;
            var balanceCount = 0;
            var leftValgusCount = 0;
            var rightValgusCount = 0;
            var symmetryCount = 0;
            var kneeAlignmentViolations = 0;
            var torsoViolations = 0;
            var balanceViolations = 0;

            foreach (var frame in buffer.RecentFrames())
            {
                stats.FrameCount++;
                stats.LatestFrame = frame;
                stats.AverageValidityScore += frame.ValidityScore;

                if (frame.HasReliableSquatCore)
                {
                    stats.ValidCoreFrameCount++;
                }

                if (frame.HasLeftKneeAngle || frame.HasRightKneeAngle)
                {
                    stats.AverageKneeAngle += frame.AverageKneeAngle;
                    kneeAngleCount++;
                }

                if (frame.HasTorsoTilt)
                {
                    stats.AverageTorsoTiltDegrees += frame.TorsoTiltDegrees;
                    torsoCount++;
                    if (frame.TorsoTiltDegrees > settings.MaximumTorsoTiltDegrees)
                    {
                        torsoViolations++;
                    }
                }

                if (frame.HasCenterBalance)
                {
                    stats.AverageCenterBalanceOffset += frame.CenterBalanceOffset;
                    balanceCount++;
                    if (frame.CenterBalanceOffset > settings.MaximumCenterBalanceOffset)
                    {
                        balanceViolations++;
                    }
                }

                if (frame.HasLeftKneeValgus)
                {
                    stats.AverageLeftKneeValgusOffset += frame.LeftKneeValgusOffset;
                    leftValgusCount++;
                    if (frame.LeftKneeValgusOffset > settings.MaximumKneeValgusOffset)
                    {
                        kneeAlignmentViolations++;
                    }
                }

                if (frame.HasRightKneeValgus)
                {
                    stats.AverageRightKneeValgusOffset += frame.RightKneeValgusOffset;
                    rightValgusCount++;
                    if (frame.RightKneeValgusOffset > settings.MaximumKneeValgusOffset)
                    {
                        kneeAlignmentViolations++;
                    }
                }

                if (frame.HasLeftKneeAngle && frame.HasRightKneeAngle)
                {
                    stats.AverageKneeSymmetryDelta += Mathf.Abs(frame.LeftKneeAngle - frame.RightKneeAngle);
                    symmetryCount++;
                }
            }

            if (stats.FrameCount > 0)
            {
                stats.ValidCoreFrameRatio = stats.ValidCoreFrameCount / (float)stats.FrameCount;
                stats.AverageValidityScore /= stats.FrameCount;
            }

            if (kneeAngleCount > 0)
            {
                stats.AverageKneeAngle /= kneeAngleCount;
            }

            if (torsoCount > 0)
            {
                stats.AverageTorsoTiltDegrees /= torsoCount;
                stats.TorsoTiltViolationRatio = torsoViolations / (float)torsoCount;
            }

            if (balanceCount > 0)
            {
                stats.AverageCenterBalanceOffset /= balanceCount;
                stats.CenterBalanceViolationRatio = balanceViolations / (float)balanceCount;
            }

            var valgusObservationCount = leftValgusCount + rightValgusCount;
            if (leftValgusCount > 0)
            {
                stats.AverageLeftKneeValgusOffset /= leftValgusCount;
            }

            if (rightValgusCount > 0)
            {
                stats.AverageRightKneeValgusOffset /= rightValgusCount;
            }

            if (valgusObservationCount > 0)
            {
                stats.KneeAlignmentViolationRatio = kneeAlignmentViolations / (float)valgusObservationCount;
            }

            if (symmetryCount > 0)
            {
                stats.AverageKneeSymmetryDelta /= symmetryCount;
            }

            return stats;
        }
    }
}
