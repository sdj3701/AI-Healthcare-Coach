using System.Collections.Generic;
using Rag.Healthcare.Pose;
using UnityEngine;

namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class FeedbackPrioritizer
    {
        private readonly Dictionary<string, float> lastSpokenTimes = new Dictionary<string, float>();

        public bool TrySelect(
            IReadOnlyList<FeedbackEvent> candidates,
            float duplicateCooldownSeconds,
            float minimumGlobalIntervalSeconds,
            out FeedbackEvent selected)
        {
            selected = null;

            if (candidates == null || candidates.Count == 0)
            {
                return false;
            }

            var now = Time.unscaledTime;
            var bestScore = float.MinValue;
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.RuleId))
                {
                    continue;
                }

                if (lastSpokenTimes.TryGetValue(candidate.Id, out var lastTime) &&
                    now - lastTime < duplicateCooldownSeconds)
                {
                    continue;
                }

                var score = SeverityWeight(candidate.Severity)
                            + candidate.Confidence * 2f
                            + candidate.PersistenceRatio * 2f;

                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                selected = candidate;
            }

            if (selected == null)
            {
                return false;
            }

            if (lastSpokenTimes.TryGetValue("_global", out var lastGlobalTime) &&
                now - lastGlobalTime < minimumGlobalIntervalSeconds)
            {
                return false;
            }

            lastSpokenTimes[selected.Id] = now;
            lastSpokenTimes["_global"] = now;
            return true;
        }

        public void Reset()
        {
            lastSpokenTimes.Clear();
        }

        private static float SeverityWeight(FeedbackSeverity severity)
        {
            return severity switch
            {
                FeedbackSeverity.Critical => 100f,
                FeedbackSeverity.Warning => 50f,
                _ => 10f
            };
        }
    }
}
