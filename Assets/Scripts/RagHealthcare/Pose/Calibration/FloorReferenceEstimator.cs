using System;
using UnityEngine;

namespace Rag.Healthcare.Pose.Calibration
{
    [Serializable]
    public sealed class FloorReference
    {
        public bool valid;
        public float floorY;
        public Vector2 leftFootDirection;
        public Vector2 rightFootDirection;
        public float confidence;
    }

    public static class FloorReferenceEstimator
    {
        public static FloorReference Estimate(JointTrackingFrame frame, float minimumConfidence = 0.5f)
        {
            if (!TryJoint(frame, PoseJointNames.LeftHeel, minimumConfidence, out var leftHeel) ||
                !TryJoint(frame, PoseJointNames.LeftFootIndex, minimumConfidence, out var leftToe) ||
                !TryJoint(frame, PoseJointNames.RightHeel, minimumConfidence, out var rightHeel) ||
                !TryJoint(frame, PoseJointNames.RightFootIndex, minimumConfidence, out var rightToe))
            {
                return new FloorReference();
            }

            var left = new Vector2(leftToe.x - leftHeel.x, leftToe.y - leftHeel.y).normalized;
            var right = new Vector2(rightToe.x - rightHeel.x, rightToe.y - rightHeel.y).normalized;
            var confidence = Mathf.Min(
                Mathf.Min(Score(leftHeel), Score(leftToe)),
                Mathf.Min(Score(rightHeel), Score(rightToe)));

            return new FloorReference
            {
                valid = true,
                floorY = Mathf.Max(Mathf.Max(leftHeel.y, leftToe.y), Mathf.Max(rightHeel.y, rightToe.y)),
                leftFootDirection = left,
                rightFootDirection = right,
                confidence = confidence
            };
        }

        private static bool TryJoint(JointTrackingFrame frame, string name, float threshold, out TrackedJoint joint)
        {
            joint = null;
            return frame != null && frame.TryGetJoint(name, out joint) && Score(joint) >= threshold;
        }

        private static float Score(TrackedJoint joint) => Mathf.Clamp01(Mathf.Max(joint.visibility, joint.confidence));
    }
}
