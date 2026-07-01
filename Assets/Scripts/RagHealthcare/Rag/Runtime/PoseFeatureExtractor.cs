using Rag.Healthcare.Pose;
using Rag.Healthcare.Pose.Analysis;
using UnityEngine;

namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class PoseFeatureExtractor
    {
        private PoseFeatureFrame previousFrame;

        public PoseFeatureFrame Extract(PoseFrameView frameView, string exercise, float minimumVisibility)
        {
            var feature = new PoseFeatureFrame
            {
                TimestampUnixMilliseconds = frameView == null ? 0L : frameView.TimestampUnixMilliseconds,
                Exercise = string.IsNullOrWhiteSpace(exercise) ? "squat" : exercise
            };

            if (frameView == null)
            {
                return feature;
            }

            var validFeatureCount = 0;
            var totalFeatureCount = 8;

            feature.HasLeftKneeAngle = TryCalculateKnee(
                frameView,
                PoseJointNames.LeftHip,
                PoseJointNames.LeftKnee,
                PoseJointNames.LeftAnkle,
                out feature.LeftKneeAngle,
                out feature.LeftKneeValgusOffset);
            feature.HasLeftKneeValgus = feature.HasLeftKneeAngle;
            if (feature.HasLeftKneeAngle)
            {
                validFeatureCount++;
            }

            feature.HasRightKneeAngle = TryCalculateKnee(
                frameView,
                PoseJointNames.RightHip,
                PoseJointNames.RightKnee,
                PoseJointNames.RightAnkle,
                out feature.RightKneeAngle,
                out feature.RightKneeValgusOffset);
            feature.HasRightKneeValgus = feature.HasRightKneeAngle;
            if (feature.HasRightKneeAngle)
            {
                validFeatureCount++;
            }

            if (feature.HasLeftKneeAngle && feature.HasRightKneeAngle)
            {
                feature.AverageKneeAngle = (feature.LeftKneeAngle + feature.RightKneeAngle) * 0.5f;
            }
            else if (feature.HasLeftKneeAngle)
            {
                feature.AverageKneeAngle = feature.LeftKneeAngle;
            }
            else if (feature.HasRightKneeAngle)
            {
                feature.AverageKneeAngle = feature.RightKneeAngle;
            }

            feature.HasTorsoTilt = TryCalculateTorsoTilt(frameView, out feature.TorsoTiltDegrees);
            if (feature.HasTorsoTilt)
            {
                validFeatureCount++;
            }

            feature.HasHipLevel = TryCalculateLevelDelta(
                frameView,
                PoseJointNames.LeftHip,
                PoseJointNames.RightHip,
                out feature.HipLevelDelta);
            if (feature.HasHipLevel)
            {
                validFeatureCount++;
            }

            feature.HasShoulderLevel = TryCalculateLevelDelta(
                frameView,
                PoseJointNames.LeftShoulder,
                PoseJointNames.RightShoulder,
                out feature.ShoulderLevelDelta);
            if (feature.HasShoulderLevel)
            {
                validFeatureCount++;
            }

            feature.HasCenterBalance = TryCalculateCenterBalance(frameView, out feature.CenterBalanceOffset, out feature.HipCenterY);
            if (feature.HasCenterBalance)
            {
                validFeatureCount++;
            }

            feature.HasLeftFootVisibility = HasFootVisibility(frameView, PoseJointNames.LeftAnkle, PoseJointNames.LeftHeel, PoseJointNames.LeftFootIndex);
            if (feature.HasLeftFootVisibility)
            {
                validFeatureCount++;
            }

            feature.HasRightFootVisibility = HasFootVisibility(frameView, PoseJointNames.RightAnkle, PoseJointNames.RightHeel, PoseJointNames.RightFootIndex);
            if (feature.HasRightFootVisibility)
            {
                validFeatureCount++;
            }

            feature.ValidityScore = totalFeatureCount <= 0 ? 0f : validFeatureCount / (float)totalFeatureCount;
            ApplyVelocity(feature);
            previousFrame = feature;
            return feature;
        }

        public void Reset()
        {
            previousFrame = null;
        }

        private static bool TryCalculateKnee(
            PoseFrameView frameView,
            string hipName,
            string kneeName,
            string ankleName,
            out float kneeAngle,
            out float kneeValgusOffset)
        {
            kneeAngle = 0f;
            kneeValgusOffset = 0f;

            if (!TryGetPosition(frameView, hipName, out var hip) ||
                !TryGetPosition(frameView, kneeName, out var knee) ||
                !TryGetPosition(frameView, ankleName, out var ankle))
            {
                return false;
            }

            kneeAngle = PoseGeometry.Angle(hip, knee, ankle);
            kneeValgusOffset = PoseGeometry.DistancePointToLine(knee, hip, ankle);
            return true;
        }

        private static bool TryCalculateTorsoTilt(PoseFrameView frameView, out float torsoTiltDegrees)
        {
            torsoTiltDegrees = 0f;

            if (!TryGetPosition(frameView, PoseJointNames.LeftShoulder, out var leftShoulder) ||
                !TryGetPosition(frameView, PoseJointNames.RightShoulder, out var rightShoulder) ||
                !TryGetPosition(frameView, PoseJointNames.LeftHip, out var leftHip) ||
                !TryGetPosition(frameView, PoseJointNames.RightHip, out var rightHip))
            {
                return false;
            }

            var shoulderCenter = PoseGeometry.Midpoint(leftShoulder, rightShoulder);
            var hipCenter = PoseGeometry.Midpoint(leftHip, rightHip);
            var torsoVector = shoulderCenter - hipCenter;
            if (torsoVector.sqrMagnitude <= Mathf.Epsilon)
            {
                return false;
            }

            torsoTiltDegrees = Vector2.Angle(torsoVector, Vector2.down);
            return true;
        }

        private static bool TryCalculateLevelDelta(PoseFrameView frameView, string leftName, string rightName, out float delta)
        {
            delta = 0f;

            if (!TryGetPosition(frameView, leftName, out var left) ||
                !TryGetPosition(frameView, rightName, out var right))
            {
                return false;
            }

            delta = Mathf.Abs(left.y - right.y);
            return true;
        }

        private static bool TryCalculateCenterBalance(PoseFrameView frameView, out float centerBalanceOffset, out float hipCenterY)
        {
            centerBalanceOffset = 0f;
            hipCenterY = 0f;

            if (!TryGetPosition(frameView, PoseJointNames.LeftHip, out var leftHip) ||
                !TryGetPosition(frameView, PoseJointNames.RightHip, out var rightHip) ||
                !TryGetPosition(frameView, PoseJointNames.LeftAnkle, out var leftAnkle) ||
                !TryGetPosition(frameView, PoseJointNames.RightAnkle, out var rightAnkle))
            {
                return false;
            }

            var hipCenter = PoseGeometry.Midpoint(leftHip, rightHip);
            var ankleCenter = PoseGeometry.Midpoint(leftAnkle, rightAnkle);
            centerBalanceOffset = Mathf.Abs(hipCenter.x - ankleCenter.x);
            hipCenterY = hipCenter.y;
            return true;
        }

        private static bool HasFootVisibility(PoseFrameView frameView, string ankleName, string heelName, string footIndexName)
        {
            return frameView.TryGetJoint(ankleName, out _) &&
                   frameView.TryGetJoint(heelName, out _) &&
                   frameView.TryGetJoint(footIndexName, out _);
        }

        private static bool TryGetPosition(PoseFrameView frameView, string jointName, out Vector2 position)
        {
            position = default;

            if (frameView == null || !frameView.TryGetJoint(jointName, out var joint))
            {
                return false;
            }

            position = new Vector2(joint.x, joint.y);
            return true;
        }

        private void ApplyVelocity(PoseFeatureFrame feature)
        {
            if (previousFrame == null || feature.TimestampUnixMilliseconds <= previousFrame.TimestampUnixMilliseconds)
            {
                return;
            }

            var deltaSeconds = (feature.TimestampUnixMilliseconds - previousFrame.TimestampUnixMilliseconds) / 1000f;
            if (deltaSeconds <= Mathf.Epsilon)
            {
                return;
            }

            feature.HipCenterYVelocityPerSecond = (feature.HipCenterY - previousFrame.HipCenterY) / deltaSeconds;
            feature.KneeAngleVelocityDegreesPerSecond = (feature.AverageKneeAngle - previousFrame.AverageKneeAngle) / deltaSeconds;
        }
    }
}
