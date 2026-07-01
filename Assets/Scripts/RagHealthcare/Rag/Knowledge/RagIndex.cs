using System;
using System.Collections.Generic;
using Rag.Healthcare.Rag.Runtime;

namespace Rag.Healthcare.Rag.Knowledge
{
    public sealed class RagIndex
    {
        private readonly List<KnowledgeChunk> chunks = new List<KnowledgeChunk>();
        private readonly List<RetrievalResult> results = new List<RetrievalResult>(8);

        public int Count => chunks.Count;

        public void Rebuild(IReadOnlyList<KnowledgeChunk> sourceChunks)
        {
            chunks.Clear();
            if (sourceChunks == null)
            {
                return;
            }

            for (var i = 0; i < sourceChunks.Count; i++)
            {
                if (sourceChunks[i] != null)
                {
                    chunks.Add(sourceChunks[i]);
                }
            }
        }

        public IReadOnlyList<RetrievalResult> Retrieve(FeedbackEvent feedbackEvent, int maxResults)
        {
            results.Clear();

            if (feedbackEvent == null || chunks.Count == 0)
            {
                return results;
            }

            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var score = Score(chunk, feedbackEvent, out var reason);
                if (score <= 0f)
                {
                    continue;
                }

                results.Add(new RetrievalResult
                {
                    Chunk = chunk,
                    Score = score,
                    MatchReason = reason
                });
            }

            results.Sort((left, right) => right.Score.CompareTo(left.Score));
            if (maxResults > 0 && results.Count > maxResults)
            {
                results.RemoveRange(maxResults, results.Count - maxResults);
            }

            return results;
        }

        private static float Score(KnowledgeChunk chunk, FeedbackEvent feedbackEvent, out string reason)
        {
            reason = string.Empty;
            var score = 0f;

            if (EqualsText(chunk.RuleId, feedbackEvent.RuleId))
            {
                score += 10f;
                reason += "ruleId ";
            }

            if (EqualsText(chunk.Exercise, feedbackEvent.Exercise))
            {
                score += 3f;
                reason += "exercise ";
            }

            if (MatchesJoint(chunk.Joint, feedbackEvent.Joint))
            {
                score += 2f;
                reason += "joint ";
            }

            if (ContainsTag(chunk, feedbackEvent.RuleId))
            {
                score += 1f;
                reason += "tag ";
            }

            if (score <= 0f && EqualsText(chunk.Exercise, "squat") && EqualsText(feedbackEvent.Exercise, "squat"))
            {
                score = 0.25f;
                reason = "fallback";
            }

            return score;
        }

        private static bool EqualsText(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) &&
                   !string.IsNullOrWhiteSpace(right) &&
                   string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesJoint(string chunkJoint, string eventJoint)
        {
            if (string.IsNullOrWhiteSpace(chunkJoint) || string.IsNullOrWhiteSpace(eventJoint))
            {
                return false;
            }

            var chunk = chunkJoint.Trim();
            var joint = eventJoint.Trim();
            return joint.IndexOf(chunk, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   chunk.IndexOf(joint, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsTag(KnowledgeChunk chunk, string value)
        {
            if (chunk.Tags == null || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            for (var i = 0; i < chunk.Tags.Length; i++)
            {
                if (EqualsText(chunk.Tags[i], value))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
