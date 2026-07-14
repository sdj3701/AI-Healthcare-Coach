using System;
using System.IO;
using Rag.Healthcare.Pose;
using UnityEngine;

namespace Rag.Healthcare.Rag.Rules
{
    [Serializable]
    public sealed class ExerciseRuleProfile
    {
        public string exerciseId;
        public string primaryJoint;
        public float minimumJointConfidence;
        public float maximumLeftRightAngleDelta;
        public float maximumTorsoTiltDegrees;
        public bool enabled;
    }

    public static class ExerciseRuleProfiles
    {
        public static readonly ExerciseRuleProfile Squat = new ExerciseRuleProfile
        {
            exerciseId = "squat", primaryJoint = "knee", minimumJointConfidence = 0.55f,
            maximumLeftRightAngleDelta = 18f, maximumTorsoTiltDegrees = 35f, enabled = true
        };

        public static readonly ExerciseRuleProfile Lunge = new ExerciseRuleProfile
        {
            exerciseId = "lunge", primaryJoint = "front_knee", minimumJointConfidence = 0.6f,
            maximumLeftRightAngleDelta = 35f, maximumTorsoTiltDegrees = 32f, enabled = true
        };

        public static bool ValidateReusableProfile(ExerciseRuleProfile profile, out string error)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.exerciseId))
            {
                error = "Exercise ID is required.";
                return false;
            }
            if (profile.minimumJointConfidence < 0f || profile.minimumJointConfidence > 1f)
            {
                error = "Joint confidence must be between 0 and 1.";
                return false;
            }
            error = string.Empty;
            return true;
        }
    }

    [Serializable]
    public sealed class ReusableRuleEvaluation
    {
        public bool valid;
        public bool lowConfidence;
        public bool leftRightImbalance;
        public float leftRightDeltaDegrees;
        public string message;
    }

    public static class ReusableLowerBodyRuleEvaluator
    {
        public static ReusableRuleEvaluation Evaluate(JointTrackingFrame frame, ExerciseRuleProfile profile)
        {
            if (!ExerciseRuleProfiles.ValidateReusableProfile(profile, out var error))
                return new ReusableRuleEvaluation { message = error };
            if (!TryAngle(frame, PoseJointNames.LeftHip, PoseJointNames.LeftKnee, PoseJointNames.LeftAnkle, profile.minimumJointConfidence, out var left) ||
                !TryAngle(frame, PoseJointNames.RightHip, PoseJointNames.RightKnee, PoseJointNames.RightAnkle, profile.minimumJointConfidence, out var right))
                return new ReusableRuleEvaluation { valid = true, lowConfidence = true, message = "관절 신뢰도가 낮아 판정을 중지했습니다." };
            var delta = Mathf.Abs(left - right);
            return new ReusableRuleEvaluation
            {
                valid = true,
                leftRightDeltaDegrees = delta,
                leftRightImbalance = delta > profile.maximumLeftRightAngleDelta,
                message = delta > profile.maximumLeftRightAngleDelta ? "좌우 무릎 각도 차이를 줄여 주세요." : "좌우 하체 각도가 안정적입니다."
            };
        }

        private static bool TryAngle(JointTrackingFrame frame, string aName, string bName, string cName, float confidence, out float angle)
        {
            angle = 0f;
            if (frame == null || !frame.TryGetJoint(aName, out var a) || !frame.TryGetJoint(bName, out var b) || !frame.TryGetJoint(cName, out var c)) return false;
            if (Score(a) < confidence || Score(b) < confidence || Score(c) < confidence) return false;
            angle = Vector2.Angle(new Vector2(a.x - b.x, a.y - b.y), new Vector2(c.x - b.x, c.y - b.y));
            return true;
        }

        private static float Score(TrackedJoint joint) => Mathf.Clamp01(Mathf.Max(joint.visibility, joint.confidence));
    }

    [Serializable]
    public sealed class ChallengeDefinition
    {
        public string id;
        public string title;
        public string exerciseId;
        public int targetRepetitions;
        public int days;
        public string safetyText;
        public string reviewer;
        public string reviewStatus;
    }

    [Serializable]
    public sealed class ChallengeCatalog
    {
        public string version;
        public ChallengeDefinition[] challenges;

        public static ChallengeCatalog Load(string relativePath)
        {
            var path = Path.Combine(Application.streamingAssetsPath, relativePath);
            return File.Exists(path) ? JsonUtility.FromJson<ChallengeCatalog>(File.ReadAllText(path)) : null;
        }
    }
}
