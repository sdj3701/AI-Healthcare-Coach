using System;
using Rag.Healthcare.Pose;
using UnityEngine;

namespace Rag.Healthcare.Qa
{
    public static class SyntheticPoseFixtures
    {
        public static JointTrackingFrame Standing(float confidence = 0.95f) =>
            BuildPose(0.30f, 0.52f, 0.42f, 0.70f, 0f, confidence);

        public static JointTrackingFrame SquatDescent(float confidence = 0.95f) =>
            BuildPose(0.38f, 0.62f, 0.36f, 0.71f, 0f, confidence);

        // The hip center is below the knee center and the knee angle remains above
        // the excessive-depth warning floor.
        public static JointTrackingFrame SquatBottom(float confidence = 0.95f) =>
            BuildPose(0.43f, 0.74f, 0.35f, 0.72f, 0f, confidence);

        public static JointTrackingFrame HipAtKneeSquatBottom(
            float confidence = 0.95f) =>
            BuildPose(0.43f, 0.72f, 0.35f, 0.72f, 0f, confidence);

        public static JointTrackingFrame DeepKneeHipAboveSquatBottom(
            float confidence = 0.95f) =>
            BuildPose(0.43f, 0.69f, 0.35f, 0.72f, 0f, confidence);

        public static JointTrackingFrame SquatAscent(float confidence = 0.95f) =>
            BuildPose(0.37f, 0.60f, 0.37f, 0.71f, 0f, confidence);

        // Front-view projections can show clear hip travel while knee flexion looks
        // shallow. This reaches a recognizable reversal (~172°) but not sufficient
        // normalized hip drop, so depth guidance remains testable without a rep.
        public static JointTrackingFrame ShallowSquatBottom(float confidence = 0.95f) =>
            BuildPose(0.35f, 0.58f, 0.42f, 0.70f, 0f, confidence);

        public static JointTrackingFrame ShallowSquatAscent(float confidence = 0.95f) =>
            BuildPose(0.33f, 0.56f, 0.42f, 0.70f, 0f, confidence);

        public static JointTrackingFrame LeftKneeValgus(float confidence = 0.95f) =>
            BuildPose(0.43f, 0.68f, 0.35f, 0.72f, 0.08f, confidence);

        public static JointTrackingFrame LowConfidence() => Standing(0.2f);

        /// <summary>
        /// Timestamped joint-coordinate frames for a complete
        /// standing → descent → bottom → ascent → standing squat.
        /// Repeated stage frames make the sequence suitable for the stabilizer and
        /// temporal phase detector rather than only direct angle-unit tests.
        /// </summary>
        public static JointTrackingFrame[] SquatRepSequence(
            long startTimestampUnixMilliseconds = 1000L,
            long frameIntervalMilliseconds = 100L,
            float confidence = 0.95f)
        {
            var frames = new[]
            {
                Standing(confidence),
                Standing(confidence),
                SquatDescent(confidence),
                SquatDescent(confidence),
                SquatBottom(confidence),
                SquatBottom(confidence),
                SquatBottom(confidence),
                SquatAscent(confidence),
                SquatAscent(confidence),
                Standing(confidence),
                Standing(confidence)
            };

            ApplyTimestamps(
                frames,
                startTimestampUnixMilliseconds,
                frameIntervalMilliseconds);
            return frames;
        }

        public static JointTrackingFrame[] ShallowSquatSequence(
            long startTimestampUnixMilliseconds = 1000L,
            long frameIntervalMilliseconds = 100L,
            float confidence = 0.95f)
        {
            var frames = new[]
            {
                Standing(confidence),
                Standing(confidence),
                ShallowSquatBottom(confidence),
                ShallowSquatBottom(confidence),
                ShallowSquatBottom(confidence),
                ShallowSquatBottom(confidence),
                ShallowSquatAscent(confidence),
                Standing(confidence),
                Standing(confidence)
            };

            ApplyTimestamps(
                frames,
                startTimestampUnixMilliseconds,
                frameIntervalMilliseconds);
            return frames;
        }

        private static JointTrackingFrame BuildPose(
            float shoulderY,
            float hipY,
            float leftKneeX,
            float kneeY,
            float leftKneeOffset,
            float confidence)
        {
            var names = PoseJointNames.MediaPipe33;
            var joints = new TrackedJoint[names.Length];
            for (var i = 0; i < names.Length; i++)
            {
                joints[i] = new TrackedJoint { name = names[i], x = 0.5f, y = 0.5f, z = 0f, visibility = confidence, confidence = confidence };
            }

            Set(joints, PoseJointNames.Nose, 0.5f, shoulderY - 0.15f);
            Set(joints, PoseJointNames.LeftShoulder, 0.42f, shoulderY);
            Set(joints, PoseJointNames.RightShoulder, 0.58f, shoulderY);
            Set(joints, PoseJointNames.LeftHip, 0.45f, hipY);
            Set(joints, PoseJointNames.RightHip, 0.55f, hipY);
            Set(joints, PoseJointNames.LeftKnee, leftKneeX + leftKneeOffset, kneeY);
            Set(joints, PoseJointNames.RightKnee, 1f - leftKneeX, kneeY);
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

        private static void ApplyTimestamps(
            JointTrackingFrame[] frames,
            long startTimestampUnixMilliseconds,
            long frameIntervalMilliseconds)
        {
            if (frames == null)
            {
                return;
            }

            var interval = Math.Max(1L, frameIntervalMilliseconds);
            for (var i = 0; i < frames.Length; i++)
            {
                if (frames[i] != null)
                {
                    frames[i].timestampUnixMilliseconds =
                        startTimestampUnixMilliseconds + interval * i;
                }
            }
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
