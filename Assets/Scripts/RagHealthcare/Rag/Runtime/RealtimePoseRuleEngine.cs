using System.Collections.Generic;
using Rag.Healthcare.Pose;
using UnityEngine;

namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class RealtimePoseRuleEngine
    {
        private readonly List<FeedbackEvent> results = new List<FeedbackEvent>(8);
        private readonly List<FeedbackEvent> eventPool = new List<FeedbackEvent>(8);
        private int usedEventCount;

        public IReadOnlyList<FeedbackEvent> Evaluate(
            PoseFeatureFrame feature,
            PoseWindowStats stats,
            ExercisePhaseState phaseState,
            RealtimePoseRuleSettings settings)
        {
            results.Clear();
            usedEventCount = 0;

            if (feature == null || stats == null || settings == null)
            {
                return results;
            }

            if (stats.FrameCount < settings.minimumRuleEvaluationFrames)
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
            EvaluatePelvicTilt(feature, stats, phaseState, settings);
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
            if (phaseState == null ||
                (phaseState.CurrentPhase != ExercisePhase.Descent &&
                 phaseState.CurrentPhase != ExercisePhase.Bottom &&
                 phaseState.CurrentPhase != ExercisePhase.Ascent))
            {
                return;
            }

            var leftQualifies =
                stats.LeftKneeObservationRatio >= settings.MinimumKneeObservationRatio &&
                stats.LeftKneeAlignmentViolationRatio >= settings.MinimumViolationRatio;
            var rightQualifies =
                stats.RightKneeObservationRatio >= settings.MinimumKneeObservationRatio &&
                stats.RightKneeAlignmentViolationRatio >= settings.MinimumViolationRatio;

            if (!leftQualifies && !rightQualifies)
            {
                return;
            }

            var left = feature.HasLeftKneeValgus ? feature.LeftKneeValgusOffset : 0f;
            var right = feature.HasRightKneeValgus ? feature.RightKneeValgusOffset : 0f;
            var useLeft = leftQualifies && (!rightQualifies || left >= right);
            var side = useLeft ? "left" : "right";
            var joint = useLeft ? PoseJointNames.LeftKnee : PoseJointNames.RightKnee;
            var offset = useLeft ? left : right;
            var persistenceRatio = useLeft
                ? stats.LeftKneeAlignmentViolationRatio
                : stats.RightKneeAlignmentViolationRatio;
            var mildCeiling = settings.MaximumKneeValgusOffset * 1.4f;
            var mild = offset <= mildCeiling;
            var severity = mild ? FeedbackSeverity.Info : FeedbackSeverity.Warning;
            var message = mild
                ? (useLeft
                    ? "왼쪽 무릎이 살짝 벌어집니다. 발끝과 같은 방향을 유지해 보세요."
                    : "오른쪽 무릎이 살짝 벌어집니다. 발끝과 같은 방향을 유지해 보세요.")
                : (useLeft
                    ? "왼쪽 무릎이 발끝 방향에서 벗어납니다. 무릎과 발끝을 같은 방향으로 맞춰 주세요."
                    : "오른쪽 무릎이 발끝 방향에서 벗어납니다. 무릎과 발끝을 같은 방향으로 맞춰 주세요.");

            AddEvent(
                useLeft ? "squat_left_knee_alignment" : "squat_right_knee_alignment",
                "squat_knee_alignment",
                joint,
                side,
                severity,
                ConfidenceFromOffset(offset, settings.MaximumKneeValgusOffset),
                persistenceRatio,
                feature.TimestampUnixMilliseconds,
                message,
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

        private void EvaluatePelvicTilt(
            PoseFeatureFrame feature,
            PoseWindowStats stats,
            ExercisePhaseState phaseState,
            RealtimePoseRuleSettings settings)
        {
            if (phaseState == null ||
                (phaseState.CurrentPhase != ExercisePhase.Descent &&
                 phaseState.CurrentPhase != ExercisePhase.Bottom &&
                 phaseState.CurrentPhase != ExercisePhase.Ascent))
            {
                return;
            }

            if (!feature.HasReliableSquatCore ||
                !feature.HasPelvicTilt ||
                stats.PelvicTiltViolationRatio < settings.minimumViolationRatio ||
                stats.AveragePelvicTiltRatio <= settings.MaximumPelvicTiltRatio)
            {
                return;
            }

            AddEvent(
                "squat_pelvic_tilt",
                "squat_pelvic_tilt",
                PoseJointNames.LeftHip,
                string.Empty,
                FeedbackSeverity.Warning,
                ConfidenceFromOffset(stats.AveragePelvicTiltRatio, settings.MaximumPelvicTiltRatio),
                stats.PelvicTiltViolationRatio,
                feature.TimestampUnixMilliseconds,
                "골반 높이가 한쪽으로 기웁니다. 양발에 체중을 고르게 싣고 골반을 수평에 가깝게 맞춰 주세요.",
                phaseState,
                "pelvicTiltRatio",
                stats.AveragePelvicTiltRatio);
        }

        private void EvaluateKneeSymmetry(
            PoseFeatureFrame feature,
            PoseWindowStats stats,
            ExercisePhaseState phaseState,
            RealtimePoseRuleSettings settings)
        {
            if (stats.LeftKneeObservationRatio < settings.MinimumKneeObservationRatio ||
                stats.RightKneeObservationRatio < settings.MinimumKneeObservationRatio)
            {
                return;
            }

            if (!feature.HasLeftKneeAngle ||
                !feature.HasRightKneeAngle ||
                stats.KneeSymmetryViolationRatio < settings.minimumViolationRatio ||
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
                stats.KneeSymmetryViolationRatio,
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

            // Prefer this-rep minimum when available; window min alone can miss the deepest squat
            // if those frames rolled out. PoseWindowStats sets MinimumKneeAngle=0 when kneeAngleCount==0.
            var depthAngle = stats.MinimumKneeAngle;
            var repMin = phaseState.MinimumKneeAngleInCurrentRep;
            if (repMin > 0f && repMin < 180f)
            {
                depthAngle = depthAngle <= 0f ? repMin : Mathf.Min(depthAngle, repMin);
            }

            // Once Bottom has been recognized for this rep, never nag about shallow depth.
            if (phaseState.HasReachedBottomInCurrentRep)
            {
                if (depthAngle < settings.MinimumBottomKneeAngle)
                {
                    AddEvent(
                        "squat_depth_deep",
                        "squat_depth_deep",
                        PoseJointNames.LeftKnee,
                        string.Empty,
                        FeedbackSeverity.Warning,
                        ConfidenceFromOffset(settings.MinimumBottomKneeAngle, depthAngle),
                        stats.DeepDepthViolationRatio,
                        feature.TimestampUnixMilliseconds,
                        "너무 깊게 내려갔습니다. 무릎과 허리에 부담이 없도록 깊이를 조금 줄여 주세요.",
                        phaseState,
                        "averageKneeAngle",
                        depthAngle);
                }

                return;
            }

            if (!phaseState.HasHipToKneeDepth ||
                float.IsNegativeInfinity(
                    phaseState.MaximumHipToKneeDepthInCurrentRep))
            {
                return;
            }

            // The learned knee angle may help recognize Bottom, but it must never
            // replace this absolute anti-abuse floor.
            if (!phaseState.HasReachedHipToKneeDepthInCurrentRep)
            {
                var maximumDepth =
                    phaseState.MaximumHipToKneeDepthInCurrentRep;
                var shortage = Mathf.Max(
                    0f,
                    settings.MinimumHipToKneeDepth - maximumDepth);
                AddEvent(
                    "squat_depth_shallow",
                    "squat_depth_shallow",
                    PoseJointNames.LeftHip,
                    string.Empty,
                    FeedbackSeverity.Warning,
                    Mathf.Clamp01(0.65f + shortage * 4f),
                    Mathf.Max(
                        stats.ShallowDepthViolationRatio,
                        0.5f),
                    feature.TimestampUnixMilliseconds,
                    "엉덩이를 무릎 높이까지 내려가야 횟수로 인정됩니다.",
                    phaseState,
                    "hipToKneeDepth",
                    maximumDepth);
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
            var feedbackEvent = RentEvent();
            feedbackEvent.Id = id;
            feedbackEvent.RuleId = ruleId;
            feedbackEvent.Exercise = phaseState == null || string.IsNullOrWhiteSpace(phaseState.Exercise) ? "squat" : phaseState.Exercise;
            feedbackEvent.Joint = joint;
            feedbackEvent.Side = side;
            feedbackEvent.Severity = severity;
            feedbackEvent.Confidence = Mathf.Clamp01(confidence);
            feedbackEvent.PersistenceRatio = Mathf.Clamp01(persistenceRatio);
            feedbackEvent.TimestampUnixMilliseconds = timestampUnixMilliseconds;
            feedbackEvent.TemplateText = text;
            feedbackEvent.Phase = phaseState == null ? ExercisePhase.Unknown : phaseState.CurrentPhase;
            feedbackEvent.Evidence.Clear();
            feedbackEvent.Evidence[evidenceKey] = evidenceValue;
            results.Add(feedbackEvent);
        }

        private FeedbackEvent RentEvent()
        {
            if (usedEventCount < eventPool.Count)
            {
                return eventPool[usedEventCount++];
            }

            var feedbackEvent = new FeedbackEvent
            {
                Evidence = new Dictionary<string, float>(1)
            };
            eventPool.Add(feedbackEvent);
            usedEventCount++;
            return feedbackEvent;
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
