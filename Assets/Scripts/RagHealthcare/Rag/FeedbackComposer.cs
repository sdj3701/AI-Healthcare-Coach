using System.Collections.Generic;
using Rag.Healthcare.Pose;
using Rag.Healthcare.Rag.Knowledge;
using Rag.Healthcare.Rag.Runtime;
using UnityEngine;

namespace Rag.Healthcare.Rag.Composition
{
    public sealed class FeedbackComposer
    {
        public PoseFeedbackMessage Compose(FeedbackEvent feedbackEvent, IReadOnlyList<RetrievalResult> retrievalResults, int maxTextLength)
        {
            if (feedbackEvent == null)
            {
                return null;
            }

            var text = SelectRetrievedRealtimeText(feedbackEvent, retrievalResults, maxTextLength);
            if (string.IsNullOrWhiteSpace(text))
            {
                text = feedbackEvent.TemplateText;
            }

            text = ApplyPlaceholders(text, feedbackEvent);
            if (maxTextLength > 0 && text.Length > maxTextLength)
            {
                text = feedbackEvent.TemplateText;
            }

            return new PoseFeedbackMessage
            {
                id = feedbackEvent.Id,
                joint = feedbackEvent.Joint,
                severity = feedbackEvent.Severity,
                confidence = Mathf.Clamp01(feedbackEvent.Confidence),
                text = text
            };
        }

        private static string SelectRetrievedRealtimeText(
            FeedbackEvent feedbackEvent,
            IReadOnlyList<RetrievalResult> retrievalResults,
            int maxTextLength)
        {
            if (retrievalResults == null)
            {
                return string.Empty;
            }

            for (var i = 0; i < retrievalResults.Count; i++)
            {
                var chunk = retrievalResults[i]?.Chunk;
                if (chunk == null || string.IsNullOrWhiteSpace(chunk.RealtimeText))
                {
                    continue;
                }

                var candidate = ApplyPlaceholders(chunk.RealtimeText, feedbackEvent);
                if (maxTextLength <= 0 || candidate.Length <= maxTextLength)
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static string ApplyPlaceholders(string text, FeedbackEvent feedbackEvent)
        {
            if (string.IsNullOrWhiteSpace(text) || feedbackEvent == null)
            {
                return text;
            }

            return text
                .Replace("{sideKo}", ToKoreanSide(feedbackEvent.Side))
                .Replace("{jointKo}", ToKoreanJoint(feedbackEvent.Joint))
                .Replace("{exerciseKo}", ToKoreanExercise(feedbackEvent.Exercise));
        }

        private static string ToKoreanSide(string side)
        {
            return side switch
            {
                "left" => "왼쪽",
                "right" => "오른쪽",
                _ => string.Empty
            };
        }

        private static string ToKoreanJoint(string joint)
        {
            if (string.IsNullOrWhiteSpace(joint))
            {
                return string.Empty;
            }

            if (joint.Contains("knee"))
            {
                return "무릎";
            }

            if (joint.Contains("hip"))
            {
                return "골반";
            }

            if (joint.Contains("shoulder"))
            {
                return "어깨";
            }

            if (joint.Contains("ankle"))
            {
                return "발목";
            }

            return joint;
        }

        private static string ToKoreanExercise(string exercise)
        {
            return string.Equals(exercise, "squat", System.StringComparison.OrdinalIgnoreCase)
                ? "스쿼트"
                : exercise;
        }
    }
}
