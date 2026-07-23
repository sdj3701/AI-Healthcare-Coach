using Rag.Healthcare.Pose;
using UnityEngine;

namespace Rag.Healthcare.Rag.Runtime
{
    public enum PoseTrackingQualityState
    {
        Unavailable,
        Degraded,
        Good
    }

    public sealed class PoseTrackingQualityReport
    {
        public PoseTrackingQualityState State;
        public float MinimumCoreConfidence;
        public float BodyHeight;
        public float HipSpan;
        public float ShoulderSpan;
        public bool HasReliableCore;
        public bool IsFrontal;
        public bool IsFullyInFrame;
        public string Reason = string.Empty;

        public bool AllowsPoseAnalysis => State == PoseTrackingQualityState.Good;

        public void Reset()
        {
            State = PoseTrackingQualityState.Unavailable;
            MinimumCoreConfidence = 0f;
            BodyHeight = 0f;
            HipSpan = 0f;
            ShoulderSpan = 0f;
            HasReliableCore = false;
            IsFrontal = false;
            IsFullyInFrame = false;
            Reason = string.Empty;
        }
    }

    /// <summary>
    /// Evaluates raw (pre-stabilization) landmarks so a held coordinate can never
    /// make an occluded body look analysis-ready. The report instance is reused.
    /// </summary>
    public sealed class PoseTrackingQualityEvaluator
    {
        private const string ReasonAcquiring = "관절 위치를 안정화하는 중입니다.";
        private const string ReasonCoreMissing = "양쪽 어깨·골반·무릎·발목이 보이도록 맞춰 주세요.";
        private const string ReasonTooSmall = "전신이 조금 더 크게 보이도록 휴대폰 거리를 조절해 주세요.";
        private const string ReasonNotFrontal = "정면을 향하고 몸을 화면 가운데에 맞춰 주세요.";
        private const string ReasonClipped = "발끝까지 화면 안에 들어오도록 한 걸음 뒤로 이동해 주세요.";

        private readonly PoseTrackingQualityReport report = new PoseTrackingQualityReport();
        private int consecutiveGoodFrames;
        private int consecutiveUnavailableFrames;

        public PoseTrackingQualityReport Evaluate(JointTrackingFrame frame, RealtimePoseRuleSettings settings)
        {
            report.Reset();
            if (frame == null || frame.joints == null || settings == null)
            {
                ApplyUnavailable(1, ReasonCoreMissing);
                return report;
            }

            if (!TryGetCore(frame, out var core, out var minimumConfidence))
            {
                ApplyUnavailable(settings.TrackingQualityUnavailableFrames, ReasonCoreMissing);
                return report;
            }

            report.MinimumCoreConfidence = minimumConfidence;
            report.HasReliableCore = minimumConfidence >= settings.MinimumTrackingQualityConfidence;
            report.HipSpan = Mathf.Abs(core.RightHip.x - core.LeftHip.x);
            report.ShoulderSpan = Mathf.Abs(core.RightShoulder.x - core.LeftShoulder.x);
            var shoulderCenter = (core.LeftShoulder + core.RightShoulder) * 0.5f;
            var ankleCenter = (core.LeftAnkle + core.RightAnkle) * 0.5f;
            report.BodyHeight = Mathf.Abs(ankleCenter.y - shoulderCenter.y);
            report.IsFrontal = report.HipSpan >= settings.MinimumFrontalBodySpan &&
                               report.ShoulderSpan >= settings.MinimumFrontalBodySpan;

            var margin = settings.MinimumBodyFrameMargin;
            report.IsFullyInFrame = core.LeftShoulder.y > margin &&
                                    core.RightShoulder.y > margin &&
                                    core.LeftAnkle.y < 1f - margin &&
                                    core.RightAnkle.y < 1f - margin;

            if (!report.HasReliableCore)
            {
                ApplyUnavailable(settings.TrackingQualityUnavailableFrames, ReasonCoreMissing);
                return report;
            }

            consecutiveUnavailableFrames = 0;
            if (report.BodyHeight < settings.MinimumTrackedBodyHeight)
            {
                ApplyDegraded(ReasonTooSmall);
                return report;
            }

            if (!report.IsFullyInFrame)
            {
                ApplyDegraded(ReasonClipped);
                return report;
            }

            if (!report.IsFrontal)
            {
                ApplyDegraded(ReasonNotFrontal);
                return report;
            }

            consecutiveGoodFrames++;
            if (consecutiveGoodFrames < settings.TrackingQualityGoodFrames)
            {
                report.State = PoseTrackingQualityState.Degraded;
                report.Reason = ReasonAcquiring;
                return report;
            }

            report.State = PoseTrackingQualityState.Good;
            report.Reason = string.Empty;
            return report;
        }

        public void Reset()
        {
            consecutiveGoodFrames = 0;
            consecutiveUnavailableFrames = 0;
            report.Reset();
        }

        private void ApplyUnavailable(int unavailableFrameThreshold, string reason)
        {
            consecutiveGoodFrames = 0;
            consecutiveUnavailableFrames++;
            report.State = consecutiveUnavailableFrames >= Mathf.Max(1, unavailableFrameThreshold)
                ? PoseTrackingQualityState.Unavailable
                : PoseTrackingQualityState.Degraded;
            report.Reason = reason;
        }

        private void ApplyDegraded(string reason)
        {
            consecutiveGoodFrames = 0;
            report.State = PoseTrackingQualityState.Degraded;
            report.Reason = reason;
        }

        private static bool TryGetCore(JointTrackingFrame frame, out CoreJoints core, out float minimumConfidence)
        {
            core = default;
            minimumConfidence = 0f;
            if (!TryGet(frame, PoseJointNames.LeftShoulder, out core.LeftShoulder, out var leftShoulderScore) ||
                !TryGet(frame, PoseJointNames.RightShoulder, out core.RightShoulder, out var rightShoulderScore) ||
                !TryGet(frame, PoseJointNames.LeftHip, out core.LeftHip, out var leftHipScore) ||
                !TryGet(frame, PoseJointNames.RightHip, out core.RightHip, out var rightHipScore) ||
                !TryGet(frame, PoseJointNames.LeftKnee, out core.LeftKnee, out var leftKneeScore) ||
                !TryGet(frame, PoseJointNames.RightKnee, out core.RightKnee, out var rightKneeScore) ||
                !TryGet(frame, PoseJointNames.LeftAnkle, out core.LeftAnkle, out var leftAnkleScore) ||
                !TryGet(frame, PoseJointNames.RightAnkle, out core.RightAnkle, out var rightAnkleScore))
            {
                return false;
            }

            minimumConfidence = Mathf.Min(
                Mathf.Min(Mathf.Min(leftShoulderScore, rightShoulderScore), Mathf.Min(leftHipScore, rightHipScore)),
                Mathf.Min(Mathf.Min(leftKneeScore, rightKneeScore), Mathf.Min(leftAnkleScore, rightAnkleScore)));
            return true;
        }

        private static bool TryGet(JointTrackingFrame frame, string jointName, out Vector2 position, out float score)
        {
            position = default;
            score = 0f;
            if (frame == null || !frame.TryGetJoint(jointName, out var joint) || joint == null)
            {
                return false;
            }

            position = new Vector2(joint.x, joint.y);
            score = PoseFrameView.GetJointScore(joint);
            return true;
        }

        private struct CoreJoints
        {
            public Vector2 LeftShoulder;
            public Vector2 RightShoulder;
            public Vector2 LeftHip;
            public Vector2 RightHip;
            public Vector2 LeftKnee;
            public Vector2 RightKnee;
            public Vector2 LeftAnkle;
            public Vector2 RightAnkle;
        }
    }
}
