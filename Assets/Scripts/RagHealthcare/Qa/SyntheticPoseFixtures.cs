using System;
using Rag.Healthcare.Pose;
using UnityEngine;

namespace Rag.Healthcare.Qa
{
    public static class SyntheticPoseFixtures
    {
        public static JointTrackingFrame Standing(float confidence = 0.95f) => Build(0f, 0f, confidence);
        public static JointTrackingFrame SquatBottom(float confidence = 0.95f) => Build(0.16f, 0f, confidence);
        public static JointTrackingFrame LeftKneeValgus(float confidence = 0.95f) => Build(0.16f, 0.08f, confidence);
        public static JointTrackingFrame LowConfidence() => Build(0f, 0f, 0.2f);

        private static JointTrackingFrame Build(float squatDepth, float leftKneeOffset, float confidence)
        {
            var names = PoseJointNames.MediaPipe33;
            var joints = new TrackedJoint[names.Length];
            for (var i = 0; i < names.Length; i++)
            {
                joints[i] = new TrackedJoint { name = names[i], x = 0.5f, y = 0.5f, z = 0f, visibility = confidence, confidence = confidence };
            }

            Set(joints, PoseJointNames.Nose, 0.5f, 0.15f + squatDepth * 0.7f);
            Set(joints, PoseJointNames.LeftShoulder, 0.42f, 0.30f + squatDepth * 0.6f);
            Set(joints, PoseJointNames.RightShoulder, 0.58f, 0.30f + squatDepth * 0.6f);
            Set(joints, PoseJointNames.LeftHip, 0.45f, 0.52f + squatDepth);
            Set(joints, PoseJointNames.RightHip, 0.55f, 0.52f + squatDepth);
            Set(joints, PoseJointNames.LeftKnee, 0.42f + leftKneeOffset, 0.70f + squatDepth * 0.15f);
            Set(joints, PoseJointNames.RightKnee, 0.58f, 0.70f + squatDepth * 0.15f);
            Set(joints, PoseJointNames.LeftAnkle, 0.40f, 0.88f);
            Set(joints, PoseJointNames.RightAnkle, 0.60f, 0.88f);
            Set(joints, PoseJointNames.LeftHeel, 0.39f, 0.91f);
            Set(joints, PoseJointNames.RightHeel, 0.59f, 0.91f);
            Set(joints, PoseJointNames.LeftFootIndex, 0.45f, 0.93f);
            Set(joints, PoseJointNames.RightFootIndex, 0.65f, 0.93f);

            return new JointTrackingFrame
            {
                id = Guid.NewGuid().ToString("N"),
                sessionId = "synthetic",
                timestampUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                joints = joints,
                feedback = Array.Empty<PoseFeedbackMessage>()
            };
        }

        private static void Set(TrackedJoint[] joints, string name, float x, float y)
        {
            var index = Array.FindIndex(joints, joint => joint.name == name);
            if (index < 0) return;
            joints[index].x = Mathf.Clamp01(x);
            joints[index].y = Mathf.Clamp01(y);
        }
    }
}
