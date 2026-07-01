using System.Collections.Generic;
using Rag.Healthcare.Pose;
using UnityEngine;

namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class RealtimePoseRuleEngine
    {
        private readonly List<FeedbackEvent> results = new List<FeedbackEvent>(8);

        public IReadOnlyList<FeedbackEvent> Evaluate(
            PoseFeatureFrame feature,
            PoseWindowStats stats,
            ExercisePhaseState phaseState,
            RealtimePoseRuleSettings settings)
        {
            results.Clear();

            if (feature == null || stats == null || settings == null)
            {
                return results;
            }

            if (stats.ValidCoreFrameRatio < settings.minimumValidCoreFrameRatio)
            {
                AddEvent(
                    "squat_visibility_low",
                    "squat_visibility_low",
                    "body",
                    string.Empty,
                    FeedbackSeverity.Info,
                    0.7f,
                    1f - stats.ValidCoreFrameRatio,
                    feature.TimestampUnixMilliseconds,
                    "카메라에 전신이 잘 보이도록 한 걸음 뒤로 이동해 주세요.",
                    phaseState,
                    "validCoreFrameRatio",
                    stats.ValidCoreFrameRatio);
                return results;
            }

            EvaluateKneeAlignment(feature, stats, phaseState, settings);
            EvaluateTorsoTilt(feature, stats, phaseState, settings);
            EvaluateCenterBalance(feature, stats, phaseState, settings);
            EvaluateKneeSymmetry(feature, stats, phaseState, settings);
            EvaluateSquatDepth(feature, stats, phaseState, settings);
            return results;
        }

        private void EvaluateKneeAlignment(
            PoseFeatureFrame feature,
            PoseWindowStats stats,
            ExercisePhaseState phaseState,
            RealtimePoseRuleSettings settings)
        {
            if (stats.KneeAlignmentViolationRatio < settings.minimumViolationRatio)
            {
                return;
            }

            var left = feature.HasLeftKneeValgus ? feature.LeftKneeValgusOffset : 0f;
            var right = feature.HasRightKneeValgus ? feature.RightKneeValgusOffset : 0f;
            var useLeft = left >= right;
            var side = useLeft ? "left" : "right";
            var sideKo = useLeft ? "왼쪽" : "오른쪽";
            var joint = useLeft ? PoseJointNames.LeftKnee : PoseJointNames.RightKnee;
            var offset = useLeft ? left : right;

            AddEvent(
                $"squat_{side}_knee_alignment",
                "squat_knee_alignment",
                joint,
                side,
                FeedbackSeverity.Warning,
                ConfidenceFromOffset(offset, settings.MaximumKneeValgusOffset),
                stats.KneeAlignmentViolationRatio,
                feature.TimestampUnixMilliseconds,
                $"{sideKo} 무릎이 발끝 방향에서 벗어납니다. 무릎과 발끝을 같은 방향으로 맞춰 주세요.",
                phaseState,
                "kneeValgusOffset",
                offset);
        }

        private void EvaluateTorsoTilt(
            PoseFeatureFrame feature,
            PoseWindowStats stats,
            ExercisePhaseState phaseState,
            RealtimePoseRuleSettings settings)
        {
            if (!feature.HasTorsoTilt ||
                stats.TorsoTiltViolationRatio < settings.minimumViolationRatio ||
                stats.AverageTorsoTiltDegrees <= settings.MaximumTorsoTiltDegrees)
            {
                return;
            }

            AddEvent(
                "squat_torso_tilt",
                "squat_torso_tilt",
                PoseJointNames.LeftShoulder,
                string.Empty,
                FeedbackSeverity.Warning,
                ConfidenceFromOffset(stats.AverageTorsoTiltDegrees, settings.MaximumTorsoTiltDegrees),
                stats.TorsoTiltViolationRatio,
                feature.TimestampUnixMilliseconds,
                "상체가 너무 앞으로 숙여집니다. 가슴을 열고 어깨를 골반 위에 올려 주세요.",
                phaseState,
                "torsoTiltDegrees",
                stats.AverageTorsoTiltDegrees);
        }

        private void EvaluateCenterBalance(
            PoseFeatureFrame feature,
            PoseWindowStats stats,
            ExercisePhaseState phaseState,
            RealtimePoseRuleSettings settings)
        {
            if (!feature.HasCenterBalance ||
                stats.CenterBalanceViolationRatio < settings.minimumViolationRatio ||
                stats.AverageCenterBalanceOffset <= settings.MaximumCenterBalanceOffset)
            {
                return;
            }

            AddEvent(
                "squat_center_balance",
                "squat_center_balance",
                PoseJointNames.LeftHip,
                string.Empty,
                FeedbackSeverity.Warning,
                ConfidenceFromOffset(stats.AverageCenterBalanceOffset, settings.MaximumCenterBalanceOffset),
                stats.CenterBalanceViolationRatio,
                feature.TimestampUnixMilliseconds,
                "중심이 한쪽으로 쏠립니다. 체중을 양발 중앙으로 다시 가져오세요.",
                phaseState,
                "centerBalanceOffset",
                stats.AverageCenterBalanceOffset);
        }

        private void EvaluateKneeSymmetry(
            PoseFeatureFrame feature,
            PoseWindowStats stats,
            ExercisePhaseState phaseState,
            RealtimePoseRuleSettings settings)
        {
            if (!feature.HasLeftKneeAngle ||
                !feature.HasRightKneeAngle ||
                stats.AverageKneeSymmetryDelta <= settings.MaximumLeftRightKneeAngleDelta)
            {
                return;
            }

            AddEvent(
                "squat_knee_symmetry",
                "squat_knee_symmetry",
                PoseJointNames.LeftKnee,
                string.Empty,
                FeedbackSeverity.Info,
                ConfidenceFromOffset(stats.AverageKneeSymmetryDelta, settings.MaximumLeftRightKneeAngleDelta),
                Mathf.Clamp01(stats.AverageKneeSymmetryDelta / Mathf.Max(1f, settings.MaximumLeftRightKneeAngleDelta * 2f)),
                feature.TimestampUnixMilliseconds,
                "좌우 무릎 굽힘이 다릅니다. 양쪽 다리에 체중을 고르게 실어 주세요.",
                phaseState,
                "kneeSymmetryDelta",
                stats.AverageKneeSymmetryDelta);
        }

        private void EvaluateSquatDepth(
            PoseFeatureFrame feature,
            PoseWindowStats stats,
            ExercisePhaseState phaseState,
            RealtimePoseRuleSettings settings)
        {
            if (phaseState == null || phaseState.CurrentPhase != ExercisePhase.Bottom)
            {
                return;
            }

            if (stats.AverageKneeAngle > settings.MaximumBottomKneeAngle)
            {
                AddEvent(
                    "squat_depth_shallow",
                    "squat_depth_shallow",
                    PoseJointNames.LeftKnee,
                    string.Empty,
                    FeedbackSeverity.Info,
                    ConfidenceFromOffset(stats.AverageKneeAngle, settings.MaximumBottomKneeAngle),
                    0.8f,
                    feature.TimestampUnixMilliseconds,
                    "가능한 범위 안에서 엉덩이를 조금 더 낮춰 주세요.",
                    phaseState,
                    "averageKneeAngle",
                    stats.AverageKneeAngle);
            }
            else if (stats.AverageKneeAngle < settings.MinimumBottomKneeAngle)
            {
                AddEvent(
                    "squat_depth_deep",
                    "squat_depth_deep",
                    PoseJointNames.LeftKnee,
                    string.Empty,
                    FeedbackSeverity.Warning,
                    ConfidenceFromOffset(settings.MinimumBottomKneeAngle, stats.AverageKneeAngle),
                    0.8f,
                    feature.TimestampUnixMilliseconds,
                    "너무 깊게 내려갔습니다. 무릎과 허리에 부담이 없도록 깊이를 조금 줄여 주세요.",
                    phaseState,
                    "averageKneeAngle",
                    stats.AverageKneeAngle);
            }
        }

        private void AddEvent(
            string id,
            string ruleId,
            string joint,
            string side,
            FeedbackSeverity severity,
            float confidence,
            float persistenceRatio,
            long timestampUnixMilliseconds,
            string text,
            ExercisePhaseState phaseState,
            string evidenceKey,
            float evidenceValue)
        {
            results.Add(new FeedbackEvent
            {
                Id = id,
                RuleId = ruleId,
                Exercise = phaseState == null || string.IsNullOrWhiteSpace(phaseState.Exercise) ? "squat" : phaseState.Exercise,
                Joint = joint,
                Side = side,
                Severity = severity,
                Confidence = Mathf.Clamp01(confidence),
                PersistenceRatio = Mathf.Clamp01(persistenceRatio),
                TimestampUnixMilliseconds = timestampUnixMilliseconds,
                TemplateText = text,
                Phase = phaseState == null ? ExercisePhase.Unknown : phaseState.CurrentPhase,
                Evidence = new Dictionary<string, float>
                {
                    [evidenceKey] = evidenceValue
                }
            });
        }

        private static float ConfidenceFromOffset(float value, float threshold)
        {
            if (threshold <= Mathf.Epsilon)
            {
                return 0.8f;
            }

            return Mathf.Clamp01(0.5f + ((value - threshold) / threshold) * 0.5f);
        }
    }
}
