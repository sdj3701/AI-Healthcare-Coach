using UnityEngine;

namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class PoseWindowStats
    {
        /// <summary>
        /// When the time filter would leave too few frames (e.g. cold start / very low FPS),
        /// keep at least this many newest samples so rule ratios stay defined.
        /// </summary>
        public const int MinimumSampleCount = 3;

        public int FrameCount;
        public int ValidCoreFrameCount;
        public float ValidCoreFrameRatio;
        public float AverageKneeAngle;
        public float MinimumKneeAngle = 180f;
        public float AverageTorsoTiltDegrees;
        public float AveragePelvicTiltRatio;
        public float AverageCenterBalanceOffset;
        public float AverageLeftKneeValgusOffset;
        public float AverageRightKneeValgusOffset;
        public float AverageKneeSymmetryDelta;
        public float KneeSymmetryViolationRatio;
        public float LeftKneeObservationRatio;
        public float RightKneeObservationRatio;
        public float LeftKneeAlignmentViolationRatio;
        public float RightKneeAlignmentViolationRatio;
        public float KneeAlignmentViolationRatio;
        public float TorsoTiltViolationRatio;
        public float PelvicTiltViolationRatio;
        public float CenterBalanceViolationRatio;
        public float ShallowDepthViolationRatio;
        public float DeepDepthViolationRatio;
        public float KneeWidthObservationRatio;
        public float AverageKneeWidthRatio;
        public float MinimumKneeWidthRatio = float.PositiveInfinity;
        public float KneeCollapseViolationRatio;
        public int MaximumConsecutiveKneeCollapseFrames;
        public int MaximumConsecutiveExcessiveDepthFrames;
        public float AverageValidityScore;
        public PoseFeatureFrame LatestFrame;

        public static PoseWindowStats Calculate(PoseWindowBuffer buffer, RealtimePoseRuleSettings settings)
        {
            return Calculate(buffer, settings, new PoseWindowStats(), float.PositiveInfinity);
        }

        public static PoseWindowStats Calculate(
            PoseWindowBuffer buffer,
            RealtimePoseRuleSettings settings,
            PoseWindowStats stats)
        {
            return Calculate(buffer, settings, stats, float.PositiveInfinity);
        }

        public static PoseWindowStats Calculate(
            PoseWindowBuffer buffer,
            RealtimePoseRuleSettings settings,
            PoseWindowStats stats,
            float analysisWindowSeconds)
        {
            stats ??= new PoseWindowStats();
            stats.Reset();
            if (buffer == null)
            {
                return stats;
            }

            var kneeAngleCount = 0;
            var torsoCount = 0;
            var pelvicTiltCount = 0;
            var balanceCount = 0;
            var leftValgusCount = 0;
            var rightValgusCount = 0;
            var symmetryCount = 0;
            var symmetryViolations = 0;
            var leftKneeAlignmentViolations = 0;
            var rightKneeAlignmentViolations = 0;
            var torsoViolations = 0;
            var pelvicTiltViolations = 0;
            var balanceViolations = 0;
            var shallowDepthViolations = 0;
            var deepDepthViolations = 0;
            var kneeWidthCount = 0;
            var kneeCollapseViolations = 0;
            var consecutiveKneeCollapseFrames = 0;
            var consecutiveExcessiveDepthFrames = 0;

            var startIndex = ResolveAnalysisStartIndex(buffer, analysisWindowSeconds);

            for (var i = startIndex; i < buffer.Count; i++)
            {
                var frame = buffer.GetChronological(i);
                if (frame == null)
                {
                    continue;
                }

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
                    stats.MinimumKneeAngle = Mathf.Min(stats.MinimumKneeAngle, frame.AverageKneeAngle);
                    kneeAngleCount++;
                    if (frame.AverageKneeAngle > settings.MaximumBottomKneeAngle)
                    {
                        shallowDepthViolations++;
                    }
                    else if (frame.HasLeftKneeAngle &&
                             frame.HasRightKneeAngle &&
                             frame.AverageKneeAngle <
                             settings.MinimumBottomKneeAngle)
                    {
                        deepDepthViolations++;
                    }
                }

                if (frame.HasLeftKneeAngle &&
                    frame.HasRightKneeAngle &&
                    frame.AverageKneeAngle <
                    settings.MinimumBottomKneeAngle)
                {
                    consecutiveExcessiveDepthFrames++;
                    stats.MaximumConsecutiveExcessiveDepthFrames =
                        Mathf.Max(
                            stats.MaximumConsecutiveExcessiveDepthFrames,
                            consecutiveExcessiveDepthFrames);
                }
                else
                {
                    consecutiveExcessiveDepthFrames = 0;
                }

                if (frame.HasKneeWidthRatio)
                {
                    stats.AverageKneeWidthRatio += frame.KneeWidthRatio;
                    stats.MinimumKneeWidthRatio = Mathf.Min(
                        stats.MinimumKneeWidthRatio,
                        frame.KneeWidthRatio);
                    kneeWidthCount++;
                    if (frame.KneeWidthRatio <
                        settings.MinimumKneeWidthRatio)
                    {
                        kneeCollapseViolations++;
                        consecutiveKneeCollapseFrames++;
                        stats.MaximumConsecutiveKneeCollapseFrames =
                            Mathf.Max(
                                stats.MaximumConsecutiveKneeCollapseFrames,
                                consecutiveKneeCollapseFrames);
                    }
                    else
                    {
                        consecutiveKneeCollapseFrames = 0;
                    }
                }
                else
                {
                    consecutiveKneeCollapseFrames = 0;
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

                if (frame.HasPelvicTilt)
                {
                    stats.AveragePelvicTiltRatio += frame.PelvicTiltRatio;
                    pelvicTiltCount++;
                    if (frame.PelvicTiltRatio > settings.MaximumPelvicTiltRatio)
                    {
                        pelvicTiltViolations++;
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
                        leftKneeAlignmentViolations++;
                    }
                }

                if (frame.HasRightKneeValgus)
                {
                    stats.AverageRightKneeValgusOffset += frame.RightKneeValgusOffset;
                    rightValgusCount++;
                    if (frame.RightKneeValgusOffset > settings.MaximumKneeValgusOffset)
                    {
                        rightKneeAlignmentViolations++;
                    }
                }

                if (frame.HasLeftKneeAngle && frame.HasRightKneeAngle)
                {
                    var symmetryDelta = Mathf.Abs(frame.LeftKneeAngle - frame.RightKneeAngle);
                    stats.AverageKneeSymmetryDelta += symmetryDelta;
                    symmetryCount++;
                    if (symmetryDelta > settings.MaximumLeftRightKneeAngleDelta)
                    {
                        symmetryViolations++;
                    }
                }
            }

            if (stats.FrameCount > 0)
            {
                stats.ValidCoreFrameRatio = stats.ValidCoreFrameCount / (float)stats.FrameCount;
                stats.AverageValidityScore /= stats.FrameCount;
                stats.LeftKneeObservationRatio = leftValgusCount / (float)stats.FrameCount;
                stats.RightKneeObservationRatio = rightValgusCount / (float)stats.FrameCount;
            }

            if (kneeAngleCount > 0)
            {
                stats.AverageKneeAngle /= kneeAngleCount;
                stats.ShallowDepthViolationRatio = shallowDepthViolations / (float)kneeAngleCount;
                stats.DeepDepthViolationRatio = deepDepthViolations / (float)kneeAngleCount;
            }
            else
            {
                stats.MinimumKneeAngle = 0f;
            }

            if (stats.FrameCount > 0)
            {
                stats.KneeWidthObservationRatio =
                    kneeWidthCount / (float)stats.FrameCount;
            }

            if (kneeWidthCount > 0)
            {
                stats.AverageKneeWidthRatio /=
                    kneeWidthCount;
                stats.KneeCollapseViolationRatio =
                    kneeCollapseViolations / (float)kneeWidthCount;
            }
            else
            {
                stats.MinimumKneeWidthRatio = 0f;
            }

            if (torsoCount > 0)
            {
                stats.AverageTorsoTiltDegrees /= torsoCount;
                stats.TorsoTiltViolationRatio = torsoViolations / (float)torsoCount;
            }

            if (pelvicTiltCount > 0)
            {
                stats.AveragePelvicTiltRatio /= pelvicTiltCount;
                stats.PelvicTiltViolationRatio = pelvicTiltViolations / (float)pelvicTiltCount;
            }

            if (balanceCount > 0)
            {
                stats.AverageCenterBalanceOffset /= balanceCount;
                stats.CenterBalanceViolationRatio = balanceViolations / (float)balanceCount;
            }

            var valgusObservationCount = leftValgusCount + rightValgusCount;
            var kneeAlignmentViolations = leftKneeAlignmentViolations + rightKneeAlignmentViolations;
            if (leftValgusCount > 0)
            {
                stats.AverageLeftKneeValgusOffset /= leftValgusCount;
                stats.LeftKneeAlignmentViolationRatio = leftKneeAlignmentViolations / (float)leftValgusCount;
            }

            if (rightValgusCount > 0)
            {
                stats.AverageRightKneeValgusOffset /= rightValgusCount;
                stats.RightKneeAlignmentViolationRatio = rightKneeAlignmentViolations / (float)rightValgusCount;
            }

            if (valgusObservationCount > 0)
            {
                stats.KneeAlignmentViolationRatio = kneeAlignmentViolations / (float)valgusObservationCount;
            }

            if (symmetryCount > 0)
            {
                stats.AverageKneeSymmetryDelta /= symmetryCount;
                stats.KneeSymmetryViolationRatio = symmetryViolations / (float)symmetryCount;
            }

            return stats;
        }

        /// <summary>
        /// Time filter is authoritative: only frames within
        /// [latestTimestamp - analysisWindowSeconds, latestTimestamp] are kept.
        /// Buffer capacity / expectedPoseFps only size the ring; they do not define the window.
        /// If the time window would leave fewer than <see cref="MinimumSampleCount"/> frames,
        /// the newest MinimumSampleCount samples are retained.
        /// </summary>
        private static int ResolveAnalysisStartIndex(PoseWindowBuffer buffer, float analysisWindowSeconds)
        {
            if (buffer.Count <= 0)
            {
                return 0;
            }

            if (float.IsInfinity(analysisWindowSeconds) || analysisWindowSeconds >= float.MaxValue / 4f)
            {
                return 0;
            }

            long latestTimestamp = 0L;
            for (var i = buffer.Count - 1; i >= 0; i--)
            {
                var frame = buffer.GetChronological(i);
                if (frame == null)
                {
                    continue;
                }

                latestTimestamp = frame.TimestampUnixMilliseconds;
                break;
            }

            var windowMs = (long)(Mathf.Max(0f, analysisWindowSeconds) * 1000f);
            var cutoff = latestTimestamp - windowMs;

            var inWindowStart = buffer.Count;
            for (var i = 0; i < buffer.Count; i++)
            {
                var frame = buffer.GetChronological(i);
                if (frame != null && frame.TimestampUnixMilliseconds >= cutoff)
                {
                    inWindowStart = i;
                    break;
                }
            }

            var inWindowCount = buffer.Count - inWindowStart;
            if (inWindowCount >= MinimumSampleCount)
            {
                return inWindowStart;
            }

            return Mathf.Max(0, buffer.Count - MinimumSampleCount);
        }

        public void Reset()
        {
            FrameCount = 0;
            ValidCoreFrameCount = 0;
            ValidCoreFrameRatio = 0f;
            AverageKneeAngle = 0f;
            MinimumKneeAngle = 180f;
            AverageTorsoTiltDegrees = 0f;
            AveragePelvicTiltRatio = 0f;
            AverageCenterBalanceOffset = 0f;
            AverageLeftKneeValgusOffset = 0f;
            AverageRightKneeValgusOffset = 0f;
            AverageKneeSymmetryDelta = 0f;
            KneeSymmetryViolationRatio = 0f;
            LeftKneeObservationRatio = 0f;
            RightKneeObservationRatio = 0f;
            LeftKneeAlignmentViolationRatio = 0f;
            RightKneeAlignmentViolationRatio = 0f;
            KneeAlignmentViolationRatio = 0f;
            TorsoTiltViolationRatio = 0f;
            PelvicTiltViolationRatio = 0f;
            CenterBalanceViolationRatio = 0f;
            ShallowDepthViolationRatio = 0f;
            DeepDepthViolationRatio = 0f;
            KneeWidthObservationRatio = 0f;
            AverageKneeWidthRatio = 0f;
            MinimumKneeWidthRatio = float.PositiveInfinity;
            KneeCollapseViolationRatio = 0f;
            MaximumConsecutiveKneeCollapseFrames = 0;
            MaximumConsecutiveExcessiveDepthFrames = 0;
            AverageValidityScore = 0f;
            LatestFrame = null;
        }
    }
}
