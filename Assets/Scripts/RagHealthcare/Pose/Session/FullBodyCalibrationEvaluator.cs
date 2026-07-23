using Rag.Healthcare.Rag.Runtime;
using UnityEngine;

namespace Rag.Healthcare.Pose.Session
{
    /// <summary>
    /// Reusable report for session-start full-body visibility (stricter than analysis-gate quality).
    /// </summary>
    public sealed class FullBodyCalibrationReport
    {
        public bool HeadVisible;
        public bool ShouldersVisible;
        public bool PelvisVisible;
        public bool KneesVisible;
        public bool AnklesVisible;
        public float MinimumGroupScore;
        public float HeldSeconds;
        public bool IsCalibrated;
        public string GuidanceReason = string.Empty;

        public bool AllFullBodyVisible =>
            HeadVisible && ShouldersVisible && PelvisVisible && KneesVisible && AnklesVisible;

        public void Reset()
        {
            HeadVisible = false;
            ShouldersVisible = false;
            PelvisVisible = false;
            KneesVisible = false;
            AnklesVisible = false;
            MinimumGroupScore = 0f;
            HeldSeconds = 0f;
            IsCalibrated = false;
            GuidanceReason = string.Empty;
        }
    }

    /// <summary>
    /// Evaluates whether the full body (head/shoulders/pelvis/knees/ankles) is stably visible
    /// long enough to start a workout session.
    /// </summary>
    public sealed class FullBodyCalibrationEvaluator
    {
        private const string GuidanceStepBack = "카메라 뒤로 물러서주세요";
        private const string GuidanceShowFullBody = "전신이 보이도록 서 주세요";
        private const string GuidanceHoldPose = "자세를 유지하세요…";

        /// <summary>
        /// Body height (normalized ankle–shoulder) above which the subject is treated as too close.
        /// </summary>
        private const float TooCloseBodyHeight = 0.92f;

        private readonly FullBodyCalibrationReport report = new FullBodyCalibrationReport();

        public FullBodyCalibrationReport Latest => report;

        public FullBodyCalibrationReport Evaluate(
            JointTrackingFrame frame,
            CalibrationSettings settings,
            float deltaSeconds)
        {
            return Evaluate(frame, settings, null, deltaSeconds);
        }

        public FullBodyCalibrationReport Evaluate(
            JointTrackingFrame frame,
            CalibrationSettings settings,
            PoseTrackingQualityReport quality,
            float deltaSeconds)
        {
            if (settings == null)
            {
                settings = new CalibrationSettings();
            }

            var threshold = Mathf.Clamp01(settings.calibrationVisibilityThreshold);
            var holdTarget = Mathf.Max(0f, settings.calibrationHoldSeconds);
            var dt = Mathf.Max(0f, deltaSeconds);

            var headScore = GetJointScore(frame, PoseJointNames.Nose);
            var leftShoulder = GetJointScore(frame, PoseJointNames.LeftShoulder);
            var rightShoulder = GetJointScore(frame, PoseJointNames.RightShoulder);
            var leftHip = GetJointScore(frame, PoseJointNames.LeftHip);
            var rightHip = GetJointScore(frame, PoseJointNames.RightHip);
            var leftKnee = GetJointScore(frame, PoseJointNames.LeftKnee);
            var rightKnee = GetJointScore(frame, PoseJointNames.RightKnee);
            var leftAnkle = GetJointScore(frame, PoseJointNames.LeftAnkle);
            var rightAnkle = GetJointScore(frame, PoseJointNames.RightAnkle);

            var shoulderScore = Mathf.Min(leftShoulder, rightShoulder);
            var pelvisScore = Mathf.Min(leftHip, rightHip);
            var kneeScore = Mathf.Min(leftKnee, rightKnee);
            var ankleScore = Mathf.Min(leftAnkle, rightAnkle);

            report.HeadVisible = !settings.requireHeadLandmark || headScore >= threshold;
            report.ShouldersVisible = shoulderScore >= threshold;
            report.PelvisVisible = pelvisScore >= threshold;
            report.KneesVisible = kneeScore >= threshold;
            report.AnklesVisible = ankleScore >= threshold;

            var minScore = Mathf.Min(
                Mathf.Min(shoulderScore, pelvisScore),
                Mathf.Min(kneeScore, ankleScore));
            if (settings.requireHeadLandmark)
            {
                minScore = Mathf.Min(minScore, headScore);
            }

            report.MinimumGroupScore = minScore;

            if (report.AllFullBodyVisible)
            {
                report.HeldSeconds += dt;
            }
            else
            {
                report.HeldSeconds = 0f;
            }

            report.IsCalibrated = report.AllFullBodyVisible && report.HeldSeconds >= holdTarget;
            report.GuidanceReason = ResolveGuidance(report, quality);
            return report;
        }

        public void ResetHold()
        {
            report.HeldSeconds = 0f;
            report.IsCalibrated = false;
            if (report.AllFullBodyVisible)
            {
                report.GuidanceReason = GuidanceHoldPose;
            }
        }

        public void Reset()
        {
            report.Reset();
        }

        private static string ResolveGuidance(
            FullBodyCalibrationReport current,
            PoseTrackingQualityReport quality)
        {
            if (current.IsCalibrated)
            {
                return string.Empty;
            }

            if (current.AllFullBodyVisible)
            {
                return GuidanceHoldPose;
            }

            if (IsClippedOrTooClose(quality))
            {
                return GuidanceStepBack;
            }

            return GuidanceShowFullBody;
        }

        private static bool IsClippedOrTooClose(PoseTrackingQualityReport quality)
        {
            if (quality == null)
            {
                return false;
            }

            if (!quality.IsFullyInFrame)
            {
                return true;
            }

            return quality.BodyHeight >= TooCloseBodyHeight;
        }

        private static float GetJointScore(JointTrackingFrame frame, string jointName)
        {
            if (frame == null || !frame.TryGetJoint(jointName, out var joint) || joint == null)
            {
                return 0f;
            }

            return PoseFrameView.GetJointScore(joint);
        }
    }
}
