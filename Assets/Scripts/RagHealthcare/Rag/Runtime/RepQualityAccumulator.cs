using System.Collections.Generic;
using Rag.Healthcare.Pose;
using UnityEngine;

namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class RepQualityAccumulator
    {
        private readonly Dictionary<string, int> warningFramesByRule =
            new Dictionary<string, int>();
        private readonly Dictionary<string, int> criticalFramesByRule =
            new Dictionary<string, int>();
        private readonly HashSet<string> rulesObservedThisFrame =
            new HashSet<string>();

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
            rulesObservedThisFrame.Clear();

            if (candidates != null)
            {
                foreach (var candidate in candidates)
                {
                    if (candidate == null || candidate.Severity < FeedbackSeverity.Warning)
                    {
                        continue;
                    }

                    // Sequential bottom cues corrected before completion do not
                    // poison the final score. The retired excessive-depth rule is
                    // also ignored so feedback from an older provider cannot turn
                    // an accepted deep squat into a failed rep.
                    if (candidate.RuleId == "squat_depth_shallow" ||
                        candidate.RuleId == "squat_depth_hip_height" ||
                        candidate.RuleId == "squat_depth_personal_target" ||
                        candidate.RuleId == "squat_knee_collapse" ||
                        candidate.RuleId == "squat_depth_excessive")
                    {
                        continue;
                    }

                    var ruleId = string.IsNullOrWhiteSpace(candidate.RuleId)
                        ? "unknown_posture"
                        : candidate.RuleId;
                    if (!rulesObservedThisFrame.Add(ruleId))
                    {
                        continue;
                    }

                    hasWarning = true;
                    var warningFrames = Increment(warningFramesByRule, ruleId);
                    if (candidate.Severity == FeedbackSeverity.Critical)
                    {
                        hasCritical = true;
                        Increment(criticalFramesByRule, ruleId);
                    }

                    // PersistenceRatio comes from a rolling analysis window. A single
                    // high-persistence sample can still be stale after the user fixes
                    // their posture, especially during a slow squat. Keep it as a
                    // diagnostic only after the same rule is observed repeatedly;
                    // final scoring below remains correction-aware.
                    if (warningFrames >= Mathf.Max(2, settings.minimumCriticalFrames) &&
                        candidate.PersistenceRatio >= settings.immediateViolationPersistenceRatio)
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

            foreach (var pair in criticalFramesByRule)
            {
                if (pair.Value >= settings.minimumCriticalFrames)
                {
                    return true;
                }
            }

            if (!HasEnoughEvidence(settings))
            {
                return false;
            }

            var minimumRuleFrames = Mathf.Max(2, settings.minimumCriticalFrames);
            foreach (var pair in warningFramesByRule)
            {
                var ratio = pair.Value / (float)ValidFrameCount;
                if (pair.Value >= minimumRuleFrames &&
                    ratio >= settings.minimumRepViolationRatio)
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsCorrect(RealtimePoseRuleSettings settings)
        {
            return HasEnoughEvidence(settings) && !HasConfirmedViolation(settings);
        }

        public void CollectConfirmedViolationRuleIds(
            RealtimePoseRuleSettings settings,
            ICollection<string> destination)
        {
            if (settings == null || destination == null)
            {
                return;
            }

            var hasEnoughEvidence = HasEnoughEvidence(settings);
            var minimumRuleFrames = Mathf.Max(2, settings.minimumCriticalFrames);
            foreach (var pair in warningFramesByRule)
            {
                criticalFramesByRule.TryGetValue(pair.Key, out var criticalFrames);
                var warningRatio = ValidFrameCount <= 0
                    ? 0f
                    : pair.Value / (float)ValidFrameCount;
                if (criticalFrames >= settings.minimumCriticalFrames ||
                    (hasEnoughEvidence &&
                     pair.Value >= minimumRuleFrames &&
                     warningRatio >= settings.minimumRepViolationRatio))
                {
                    destination.Add(pair.Key);
                }
            }
        }

        public void Reset()
        {
            ValidFrameCount = 0;
            WarningFrameCount = 0;
            CriticalFrameCount = 0;
            HasPersistentWarning = false;
            warningFramesByRule.Clear();
            criticalFramesByRule.Clear();
            rulesObservedThisFrame.Clear();
        }

        private static int Increment(
            IDictionary<string, int> counts,
            string key)
        {
            counts.TryGetValue(key, out var count);
            count++;
            counts[key] = count;
            return count;
        }
    }
}
