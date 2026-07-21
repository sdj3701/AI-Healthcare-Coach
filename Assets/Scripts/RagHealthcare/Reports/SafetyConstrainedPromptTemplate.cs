using System;
using System.Globalization;
using AIHealthcareCoach.MediaPipe;

namespace Rag.Healthcare.Reports
{
    /// <summary>
    /// Deterministic safety-constrained prompt for on-device workout report generation (PBI-061).
    /// Extension point for PBI-062: append approved rule_id catalog context lines after the SESSION block.
    /// </summary>
    public static class SafetyConstrainedPromptTemplate
    {
        public const string SafetyDirectives =
            "You are an offline fitness posture coach. Use only the supplied numeric summary. " +
            "Do not diagnose, prescribe treatment, mention diseases, or invent facts. Respond in Korean in 3 short sentences.";

        public static string Build(PoseSessionSummary summary)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));

            var duration = summary.durationSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            var feedback = summary.feedbackCount.ToString(CultureInfo.InvariantCulture);
            var warnings = summary.warningFeedbackCount.ToString(CultureInfo.InvariantCulture);
            var critical = summary.criticalFeedbackCount.ToString(CultureInfo.InvariantCulture);
            var poseFps = summary.averagePoseFps.ToString("0.0", CultureInfo.InvariantCulture);
            var visibility = summary.averageVisibility.ToString("0.00", CultureInfo.InvariantCulture);

            return "SYSTEM: " + SafetyDirectives + "\n" +
                   "SESSION: duration=" + duration + "s, feedback=" + feedback + ", warnings=" + warnings + ", " +
                   "critical=" + critical + ", pose_fps=" + poseFps + ", visibility=" + visibility + ".";
        }
    }
}
