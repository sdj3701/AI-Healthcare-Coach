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

            if (phaseState != null &&
                phaseState.CurrentPhase == ExercisePhase.Bottom)
            {
                EvaluateSquatDepth(
                    feature,
                    stats,
                    phaseState,
                    settings);
                return results;
            }

            if (stats.FrameCount < settings.minimumRuleEvaluationFrames)
            {
                return results;
            }

            if (stats.ValidCoreFrameRatio < settings.minimumValidCoreFrameRatio)
            {
                AddTrackingUnavailableEvent(
                    feature,
                    stats,
                    phaseState);
                return results;
            }

            EvaluateKneeAlignment(feature, stats, phaseState, settings);
            EvaluateTorsoTilt(feature, stats, phaseState, settings);
            EvaluatePelvicTilt(feature, stats, phaseState, settings);
            EvaluateCenterBalance(feature, stats, phaseState, settings);
            EvaluateKneeSymmetry(feature, stats, phaseState, settings);
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
            if (phaseState == null ||
                phaseState.CurrentPhase != ExercisePhase.Bottom)
            {
                if (phaseState != null)
                {
                    phaseState.CurrentBottomDecision =
                        SquatBottomDecision.NotAtBottom;
                }

                return;
            }

            phaseState.CurrentKneeWidthRatio =
                feature.HasKneeWidthRatio
                    ? feature.KneeWidthRatio
                    : 0f;
            if (!feature.HasReliableSquatCore ||
                stats.FrameCount > 0 &&
                stats.ValidCoreFrameRatio <
                settings.MinimumValidCoreFrameRatio)
            {
                phaseState.CurrentBottomDecision =
                    SquatBottomDecision.TrackingUnavailable;
                AddTrackingUnavailableEvent(
                    feature,
                    stats,
                    phaseState);
                return;
            }

            var kneeCollapse =
                HasConfirmedKneeCollapse(feature, stats, settings);
            if (!phaseState.HasReachedHipToKneeDepthInCurrentRep)
            {
                // Safety exception: do not ask the user to descend farther while
                // inward knee collapse is already confirmed.
                if (kneeCollapse)
                {
                    EmitKneeCollapse(
                        feature,
                        stats,
                        phaseState);
                    return;
                }

                phaseState.CurrentBottomDecision =
                    SquatBottomDecision.HipHeightFailed;
                AddEvent(
                    "squat_depth_hip_height",
                    "squat_depth_hip_height",
                    PoseJointNames.LeftHip,
                    string.Empty,
                    FeedbackSeverity.Warning,
                    0.9f,
                    Mathf.Max(
                        stats.ShallowDepthViolationRatio,
                        0.5f),
                    feature.TimestampUnixMilliseconds,
                    "엉덩이와 무릎 높이가 충분히 가까워지지 않았습니다. 엉덩이를 조금 더 내려 주세요.",
                    phaseState,
                    "hipToKneeDepth",
                    phaseState.MaximumHipToKneeDepthInCurrentRep,
                    preferTemplateText: true);
                return;
            }

            if (kneeCollapse)
            {
                EmitKneeCollapse(
                    feature,
                    stats,
                    phaseState);
                return;
            }

            var maximumKneeAngle =
                phaseState.MaximumCountableBottomKneeAngle > 0f
                    ? phaseState.MaximumCountableBottomKneeAngle
                    : settings.MaximumCountableBottomKneeAngle;
            var minimumHipDrop =
                phaseState.MinimumBottomHipDrop > 0f
                    ? phaseState.MinimumBottomHipDrop
                    : settings.MinimumBottomHipDrop;
            var minimumKneeAngle =
                phaseState.MinimumKneeAngleInCurrentRep;
            var maximumHipDrop =
                phaseState.MaximumHipDropInCurrentRep;
            var hasPersonalDepth =
                minimumKneeAngle > 0f &&
                minimumKneeAngle < 180f &&
                minimumKneeAngle <= maximumKneeAngle ||
                maximumHipDrop >= minimumHipDrop;
            if (!hasPersonalDepth)
            {
                phaseState.CurrentBottomDecision =
                    SquatBottomDecision.PersonalDepthFailed;
                var personalDepth = AddEvent(
                    "squat_depth_personal_target",
                    "squat_depth_personal_target",
                    PoseJointNames.LeftHip,
                    string.Empty,
                    FeedbackSeverity.Warning,
                    0.9f,
                    Mathf.Max(
                        stats.ShallowDepthViolationRatio,
                        0.5f),
                    feature.TimestampUnixMilliseconds,
                    "정렬은 좋습니다. 현재 가능한 범위에서 조금 더 앉아 주세요.",
                    phaseState,
                    "minimumKneeAngle",
                    minimumKneeAngle,
                    preferTemplateText: true);
                personalDepth.Evidence["maximumHipDrop"] =
                    maximumHipDrop;
                personalDepth.Evidence["activeMaximumKneeAngle"] =
                    maximumKneeAngle;
                personalDepth.Evidence["activeMinimumHipDrop"] =
                    minimumHipDrop;
                return;
            }

            // A deep bottom is accepted once height, knee alignment, and the
            // personal depth target have passed. Minimum-knee-angle statistics
            // remain available for diagnostics but do not reject the rep.
            phaseState.CurrentBottomDecision =
                SquatBottomDecision.Passed;
            phaseState.HasPassedBottomDecisionInCurrentRep = true;
        }

        private static bool HasConfirmedKneeCollapse(
            PoseFeatureFrame feature,
            PoseWindowStats stats,
            RealtimePoseRuleSettings settings)
        {
            if (feature == null ||
                stats == null ||
                settings == null ||
                stats.KneeWidthObservationRatio <
                settings.MinimumKneeObservationRatio)
            {
                return false;
            }

            var widthViolation =
                stats.MaximumConsecutiveKneeCollapseFrames >=
                    settings.MinimumKneeCollapseFrames ||
                stats.KneeCollapseViolationRatio >=
                    settings.MinimumViolationRatio;
            if (!widthViolation)
            {
                return false;
            }

            var currentOffsetViolation =
                feature.HasLeftKneeValgus &&
                feature.LeftKneeValgusOffset >
                settings.MaximumKneeValgusOffset ||
                feature.HasRightKneeValgus &&
                feature.RightKneeValgusOffset >
                settings.MaximumKneeValgusOffset;
            var persistentOffsetViolation =
                stats.KneeAlignmentViolationRatio >=
                settings.MinimumViolationRatio;
            return currentOffsetViolation ||
                   persistentOffsetViolation;
        }

        private void EmitKneeCollapse(
            PoseFeatureFrame feature,
            PoseWindowStats stats,
            ExercisePhaseState phaseState)
        {
            phaseState.CurrentBottomDecision =
                SquatBottomDecision.KneeCollapseFailed;
            phaseState.HasKneeCollapseInCurrentRep = true;
            AddEvent(
                "squat_knee_collapse",
                "squat_knee_collapse",
                PoseJointNames.LeftKnee,
                string.Empty,
                FeedbackSeverity.Warning,
                0.95f,
                stats.KneeCollapseViolationRatio,
                feature.TimestampUnixMilliseconds,
                "무릎이 안쪽으로 모입니다. 무릎을 발끝 방향으로 조금 벌려 주세요.",
                phaseState,
                "kneeWidthRatio",
                stats.MinimumKneeWidthRatio,
                preferTemplateText: true);
        }

        private void AddTrackingUnavailableEvent(
            PoseFeatureFrame feature,
            PoseWindowStats stats,
            ExercisePhaseState phaseState)
        {
            if (phaseState != null)
            {
                phaseState.CurrentBottomDecision =
                    SquatBottomDecision.TrackingUnavailable;
            }

            AddEvent(
                "squat_visibility_low",
                "squat_visibility_low",
                "body",
                string.Empty,
                FeedbackSeverity.Info,
                0.7f,
                1f - stats.ValidCoreFrameRatio,
                feature.TimestampUnixMilliseconds,
                "카메라에 전신이 보이도록 위치를 조정해 주세요.",
                phaseState,
                "validCoreFrameRatio",
                stats.ValidCoreFrameRatio,
                preferTemplateText: true);
        }

        private FeedbackEvent AddEvent(
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
            float evidenceValue,
            bool preferTemplateText = false)
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
            feedbackEvent.PreferTemplateText = preferTemplateText;
            feedbackEvent.Phase = phaseState == null ? ExercisePhase.Unknown : phaseState.CurrentPhase;
            feedbackEvent.BottomDecision = phaseState == null
                ? SquatBottomDecision.NotAtBottom
                : phaseState.CurrentBottomDecision;
            feedbackEvent.Evidence.Clear();
            feedbackEvent.Evidence[evidenceKey] = evidenceValue;
            results.Add(feedbackEvent);
            return feedbackEvent;
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
