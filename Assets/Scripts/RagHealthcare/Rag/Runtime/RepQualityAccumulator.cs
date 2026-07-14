using System.Collections.Generic;
using Rag.Healthcare.Pose;

namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class RepQualityAccumulator
    {
        public int ValidFrameCount { get; private set; }
        public int WarningFrameCount { get; private set; }
        public int CriticalFrameCount { get; private set; }
        public bool HasPersistentWarning { get; private set; }

        public float WarningRatio => ValidFrameCount <= 0
            ? 0f
            : WarningFrameCount / (float)ValidFrameCount;

        public void Observe(
            bool hasReliableCore,
            IReadOnlyList<FeedbackEvent> candidates,
            RealtimePoseRuleSettings settings)
        {
            if (!hasReliableCore || settings == null)
            {
                return;
            }

            ValidFrameCount++;
            var hasWarning = false;
            var hasCritical = false;

            if (candidates != null)
            {
                foreach (var candidate in candidates)
                {
                    if (candidate == null || candidate.Severity < FeedbackSeverity.Warning)
                    {
                        continue;
                    }

                    hasWarning = true;
                    if (candidate.Severity == FeedbackSeverity.Critical)
                    {
                        hasCritical = true;
                    }

                    if (candidate.PersistenceRatio >= settings.immediateViolationPersistenceRatio)
                    {
                        HasPersistentWarning = true;
                    }
                }
            }

            if (hasWarning)
            {
                WarningFrameCount++;
            }

            if (hasCritical)
            {
                CriticalFrameCount++;
            }
        }

        public bool HasEnoughEvidence(RealtimePoseRuleSettings settings)
        {
            return settings != null && ValidFrameCount >= settings.minimumValidRepFrames;
        }

        public bool HasConfirmedViolation(RealtimePoseRuleSettings settings)
        {
            if (settings == null)
            {
                return false;
            }

            if (CriticalFrameCount >= settings.minimumCriticalFrames || HasPersistentWarning)
            {
                return true;
            }

            return HasEnoughEvidence(settings) && WarningRatio >= settings.minimumRepViolationRatio;
        }

        public bool IsCorrect(RealtimePoseRuleSettings settings)
        {
            return HasEnoughEvidence(settings) && !HasConfirmedViolation(settings);
        }

        public void Reset()
        {
            ValidFrameCount = 0;
            WarningFrameCount = 0;
            CriticalFrameCount = 0;
            HasPersistentWarning = false;
        }
    }
}
