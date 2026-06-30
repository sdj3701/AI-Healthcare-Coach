using System.Collections.Generic;
using UnityEngine;

namespace Rag.Healthcare.Pose.Analysis
{
    public sealed class PoseFeedbackAnalyzer : MonoBehaviour
    {
        [SerializeField] private PoseRuleConfig config;

        [Header("Fallback Rules")]
        [SerializeField, Range(0f, 1f)] private float minimumVisibility = 0.5f;
        [SerializeField, Range(0f, 0.5f)] private float maximumKneeValgusOffset = 0.08f;
        [SerializeField, Range(0f, 180f)] private float minimumSquatKneeAngle = 70f;
        [SerializeField, Range(0f, 180f)] private float maximumSquatKneeAngle = 165f;
        [SerializeField, Range(0f, 90f)] private float maximumLeftRightKneeAngleDelta = 18f;
        [SerializeField, Range(0f, 90f)] private float maximumTorsoTiltDegrees = 35f;
        [SerializeField, Range(0f, 0.5f)] private float maximumHipLevelDelta = 0.08f;
        [SerializeField, Range(0f, 0.5f)] private float maximumShoulderLevelDelta = 0.08f;
        [SerializeField, Range(0f, 0.5f)] private float maximumCenterBalanceOffset = 0.12f;
        [SerializeField, Range(0f, 10f)] private float feedbackCooldownSeconds = 2f;

        private readonly Dictionary<string, float> lastFeedbackTimes = new Dictionary<string, float>();

        private float MinimumVisibility => config != null ? config.minimumVisibility : minimumVisibility;
        private float MaximumKneeValgusOffset => config != null ? config.maximumKneeValgusOffset : maximumKneeValgusOffset;
        private float MinimumSquatKneeAngle => config != null ? config.minimumSquatKneeAngle : minimumSquatKneeAngle;
        private float MaximumSquatKneeAngle => config != null ? config.maximumSquatKneeAngle : maximumSquatKneeAngle;
        private float MaximumLeftRightKneeAngleDelta => config != null ? config.maximumLeftRightKneeAngleDelta : maximumLeftRightKneeAngleDelta;
        private float MaximumTorsoTiltDegrees => config != null ? config.maximumTorsoTiltDegrees : maximumTorsoTiltDegrees;
        private float MaximumHipLevelDelta => config != null ? config.maximumHipLevelDelta : maximumHipLevelDelta;
        private float MaximumShoulderLevelDelta => config != null ? config.maximumShoulderLevelDelta : maximumShoulderLevelDelta;
        private float MaximumCenterBalanceOffset => config != null ? config.maximumCenterBalanceOffset : maximumCenterBalanceOffset;
        private float FeedbackCooldownSeconds => config != null ? config.feedbackCooldownSeconds : feedbackCooldownSeconds;

        public int Analyze(JointTrackingFrame frame, IList<PoseFeedbackMessage> results)
        {
            if (frame == null || frame.joints == null || results == null)
            {
                return 0;
            }

            var initialCount = results.Count;
            AnalyzeKnee(frame, "left", PoseJointNames.LeftHip, PoseJointNames.LeftKnee, PoseJointNames.LeftAnkle, results);
            AnalyzeKnee(frame, "right", PoseJointNames.RightHip, PoseJointNames.RightKnee, PoseJointNames.RightAnkle, results);
            AnalyzeKneeSymmetry(frame, results);
            AnalyzeTorsoTilt(frame, results);
            AnalyzeHipLevel(frame, results);
            AnalyzeShoulderLevel(frame, results);
            AnalyzeCenterBalance(frame, results);
            AnalyzeFootVisibility(frame, results);
            return results.Count - initialCount;
        }

        private void AnalyzeKnee(
            JointTrackingFrame frame,
            string side,
            string hipName,
            string kneeName,
            string ankleName,
            IList<PoseFeedbackMessage> results)
        {
            if (!TryGetTriplet(frame, hipName, kneeName, ankleName, out var hip, out var knee, out var ankle, out var confidence))
            {
                return;
            }

            var kneeAngle = PoseGeometry.Angle(hip, knee, ankle);
            var offset = PoseGeometry.DistancePointToLine(knee, hip, ankle);

            if (offset > MaximumKneeValgusOffset)
            {
                AddFeedback(
                    results,
                    $"{side}_knee_alignment",
                    $"{side} knee is drifting away from the hip-ankle line.",
                    kneeName,
                    FeedbackSeverity.Warning,
                    confidence);
            }

            if (kneeAngle > MaximumSquatKneeAngle)
            {
                AddFeedback(
                    results,
                    $"{side}_knee_bend_low",
                    $"{side} knee bend is shallow. Bend the knee a little more.",
                    kneeName,
                    FeedbackSeverity.Info,
                    confidence);
            }
            else if (kneeAngle < MinimumSquatKneeAngle)
            {
                AddFeedback(
                    results,
                    $"{side}_knee_bend_deep",
                    $"{side} knee bend is too deep. Reduce the depth slightly.",
                    kneeName,
                    FeedbackSeverity.Warning,
                    confidence);
            }
        }

        private void AnalyzeKneeSymmetry(JointTrackingFrame frame, IList<PoseFeedbackMessage> results)
        {
            if (!TryGetTriplet(frame, PoseJointNames.LeftHip, PoseJointNames.LeftKnee, PoseJointNames.LeftAnkle, out var leftHip, out var leftKnee, out var leftAnkle, out var leftConfidence) ||
                !TryGetTriplet(frame, PoseJointNames.RightHip, PoseJointNames.RightKnee, PoseJointNames.RightAnkle, out var rightHip, out var rightKnee, out var rightAnkle, out var rightConfidence))
            {
                return;
            }

            var leftAngle = PoseGeometry.Angle(leftHip, leftKnee, leftAnkle);
            var rightAngle = PoseGeometry.Angle(rightHip, rightKnee, rightAnkle);
            if (Mathf.Abs(leftAngle - rightAngle) <= MaximumLeftRightKneeAngleDelta)
            {
                return;
            }

            AddFeedback(
                results,
                "knee_symmetry",
                "Left and right knee bend are uneven.",
                PoseJointNames.LeftKnee,
                FeedbackSeverity.Info,
                Mathf.Min(leftConfidence, rightConfidence));
        }

        private void AnalyzeTorsoTilt(JointTrackingFrame frame, IList<PoseFeedbackMessage> results)
        {
            if (!PoseGeometry.TryGetJoint2D(frame, PoseJointNames.LeftShoulder, MinimumVisibility, out var leftShoulder, out var leftShoulderJoint) ||
                !PoseGeometry.TryGetJoint2D(frame, PoseJointNames.RightShoulder, MinimumVisibility, out var rightShoulder, out var rightShoulderJoint) ||
                !PoseGeometry.TryGetJoint2D(frame, PoseJointNames.LeftHip, MinimumVisibility, out var leftHip, out var leftHipJoint) ||
                !PoseGeometry.TryGetJoint2D(frame, PoseJointNames.RightHip, MinimumVisibility, out var rightHip, out var rightHipJoint))
            {
                return;
            }

            var shoulderMidpoint = PoseGeometry.Midpoint(leftShoulder, rightShoulder);
            var hipMidpoint = PoseGeometry.Midpoint(leftHip, rightHip);
            var torsoVector = shoulderMidpoint - hipMidpoint;
            if (torsoVector.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            var tilt = Vector2.Angle(torsoVector, Vector2.down);
            if (tilt <= MaximumTorsoTiltDegrees)
            {
                return;
            }

            var shoulderConfidence = Mathf.Min(
                PoseGeometry.GetJointScore(leftShoulderJoint),
                PoseGeometry.GetJointScore(rightShoulderJoint));
            var hipConfidence = Mathf.Min(
                PoseGeometry.GetJointScore(leftHipJoint),
                PoseGeometry.GetJointScore(rightHipJoint));
            var confidence = Mathf.Min(shoulderConfidence, hipConfidence);

            AddFeedback(
                results,
                "torso_tilt",
                "Torso is leaning too far. Keep shoulders stacked over the hips.",
                PoseJointNames.LeftShoulder,
                FeedbackSeverity.Warning,
                confidence);
        }

        private void AnalyzeHipLevel(JointTrackingFrame frame, IList<PoseFeedbackMessage> results)
        {
            if (!PoseGeometry.TryGetJoint2D(frame, PoseJointNames.LeftHip, MinimumVisibility, out var leftHip, out var leftHipJoint) ||
                !PoseGeometry.TryGetJoint2D(frame, PoseJointNames.RightHip, MinimumVisibility, out var rightHip, out var rightHipJoint))
            {
                return;
            }

            var delta = Mathf.Abs(leftHip.y - rightHip.y);
            if (delta <= MaximumHipLevelDelta)
            {
                return;
            }

            AddFeedback(
                results,
                "hip_level",
                "골반 높이가 한쪽으로 기울었습니다. 양쪽 골반을 수평에 가깝게 맞춰 주세요.",
                PoseJointNames.LeftHip,
                FeedbackSeverity.Warning,
                Mathf.Min(PoseGeometry.GetJointScore(leftHipJoint), PoseGeometry.GetJointScore(rightHipJoint)));
        }

        private void AnalyzeShoulderLevel(JointTrackingFrame frame, IList<PoseFeedbackMessage> results)
        {
            if (!PoseGeometry.TryGetJoint2D(frame, PoseJointNames.LeftShoulder, MinimumVisibility, out var leftShoulder, out var leftShoulderJoint) ||
                !PoseGeometry.TryGetJoint2D(frame, PoseJointNames.RightShoulder, MinimumVisibility, out var rightShoulder, out var rightShoulderJoint))
            {
                return;
            }

            var delta = Mathf.Abs(leftShoulder.y - rightShoulder.y);
            if (delta <= MaximumShoulderLevelDelta)
            {
                return;
            }

            AddFeedback(
                results,
                "shoulder_level",
                "어깨가 한쪽으로 기울었습니다. 가슴을 정면으로 두고 어깨 높이를 맞춰 주세요.",
                PoseJointNames.LeftShoulder,
                FeedbackSeverity.Info,
                Mathf.Min(PoseGeometry.GetJointScore(leftShoulderJoint), PoseGeometry.GetJointScore(rightShoulderJoint)));
        }

        private void AnalyzeCenterBalance(JointTrackingFrame frame, IList<PoseFeedbackMessage> results)
        {
            if (!PoseGeometry.TryGetJoint2D(frame, PoseJointNames.LeftHip, MinimumVisibility, out var leftHip, out var leftHipJoint) ||
                !PoseGeometry.TryGetJoint2D(frame, PoseJointNames.RightHip, MinimumVisibility, out var rightHip, out var rightHipJoint) ||
                !PoseGeometry.TryGetJoint2D(frame, PoseJointNames.LeftAnkle, MinimumVisibility, out var leftAnkle, out var leftAnkleJoint) ||
                !PoseGeometry.TryGetJoint2D(frame, PoseJointNames.RightAnkle, MinimumVisibility, out var rightAnkle, out var rightAnkleJoint))
            {
                return;
            }

            var hipCenter = PoseGeometry.Midpoint(leftHip, rightHip);
            var ankleCenter = PoseGeometry.Midpoint(leftAnkle, rightAnkle);
            var offset = Mathf.Abs(hipCenter.x - ankleCenter.x);
            if (offset <= MaximumCenterBalanceOffset)
            {
                return;
            }

            var confidence = Mathf.Min(
                Mathf.Min(PoseGeometry.GetJointScore(leftHipJoint), PoseGeometry.GetJointScore(rightHipJoint)),
                Mathf.Min(PoseGeometry.GetJointScore(leftAnkleJoint), PoseGeometry.GetJointScore(rightAnkleJoint)));

            AddFeedback(
                results,
                "center_balance",
                "몸의 중심이 발 중앙에서 벗어났습니다. 체중을 양발 가운데로 다시 가져오세요.",
                PoseJointNames.LeftHip,
                FeedbackSeverity.Warning,
                confidence);
        }

        private void AnalyzeFootVisibility(JointTrackingFrame frame, IList<PoseFeedbackMessage> results)
        {
            AnalyzeFootVisibility(frame, "left", PoseJointNames.LeftAnkle, PoseJointNames.LeftHeel, PoseJointNames.LeftFootIndex, results);
            AnalyzeFootVisibility(frame, "right", PoseJointNames.RightAnkle, PoseJointNames.RightHeel, PoseJointNames.RightFootIndex, results);
        }

        private void AnalyzeFootVisibility(
            JointTrackingFrame frame,
            string side,
            string ankleName,
            string heelName,
            string footIndexName,
            IList<PoseFeedbackMessage> results)
        {
            if (!PoseGeometry.TryGetJoint2D(frame, ankleName, MinimumVisibility, out _, out var ankle))
            {
                return;
            }

            var hasHeel = PoseGeometry.TryGetJoint2D(frame, heelName, MinimumVisibility, out _, out _);
            var hasFootIndex = PoseGeometry.TryGetJoint2D(frame, footIndexName, MinimumVisibility, out _, out _);
            if (hasHeel && hasFootIndex)
            {
                return;
            }

            AddFeedback(
                results,
                $"{side}_foot_visibility",
                $"{side} foot is not visible enough for stable posture analysis.",
                ankleName,
                FeedbackSeverity.Info,
                PoseGeometry.GetJointScore(ankle));
        }

        private bool TryGetTriplet(
            JointTrackingFrame frame,
            string firstName,
            string centerName,
            string thirdName,
            out Vector2 first,
            out Vector2 center,
            out Vector2 third,
            out float confidence)
        {
            first = default;
            center = default;
            third = default;
            confidence = 0f;
            var hasFirst = PoseGeometry.TryGetJoint2D(frame, firstName, MinimumVisibility, out first, out var firstJoint);
            var hasCenter = PoseGeometry.TryGetJoint2D(frame, centerName, MinimumVisibility, out center, out var centerJoint);
            var hasThird = PoseGeometry.TryGetJoint2D(frame, thirdName, MinimumVisibility, out third, out var thirdJoint);

            if (!hasFirst || !hasCenter || !hasThird)
            {
                return false;
            }

            confidence = Mathf.Min(
                PoseGeometry.GetJointScore(firstJoint),
                Mathf.Min(
                    PoseGeometry.GetJointScore(centerJoint),
                    PoseGeometry.GetJointScore(thirdJoint)));
            return true;
        }

        private void AddFeedback(
            IList<PoseFeedbackMessage> results,
            string id,
            string text,
            string joint,
            FeedbackSeverity severity,
            float confidence)
        {
            if (IsCoolingDown(id))
            {
                return;
            }

            lastFeedbackTimes[id] = Time.time;
            results.Add(new PoseFeedbackMessage
            {
                id = id,
                text = text,
                joint = joint,
                confidence = Mathf.Clamp01(confidence),
                severity = severity
            });
        }

        private bool IsCoolingDown(string id)
        {
            return lastFeedbackTimes.TryGetValue(id, out var lastTime) &&
                   Time.time - lastTime < FeedbackCooldownSeconds;
        }
    }
}
