using Rag.Healthcare.Pose;
using Rag.Healthcare.Pose.Analysis;
using UnityEngine;

namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class PoseFeatureExtractor
    {
        // Below this projected horizontal span, a frontal pelvic-level estimate is
        // too sensitive to a single-landmark jitter or a near side-on camera view.
        private const float MinimumFrontalPelvisSpan = 0.08f;
        private const float MinimumBodyScale = 0.05f;
        private const float LegacyReferenceBodyScale = 0.5f;

        private readonly PoseFeatureFrame workingFeature = new PoseFeatureFrame();
        private bool hasPreviousFrame;
        private long previousTimestampUnixMilliseconds;
        private float previousHipCenterY;
        private float previousAverageKneeAngle;

        // The returned feature is a reusable view and is valid until the next Extract call.
        public PoseFeatureFrame Extract(PoseFrameView frameView, string exercise, float minimumVisibility)
        {
            return ExtractInternal(
                frameView,
                exercise,
                minimumVisibility,
                LegacyReferenceBodyScale);
        }

        // Prefer this overload in the live pipeline. The float overload remains for
        // source compatibility with existing QA/editor callers.
        public PoseFeatureFrame Extract(
            PoseFrameView frameView,
            string exercise,
            RealtimePoseRuleSettings settings)
        {
            return ExtractInternal(
                frameView,
                exercise,
                settings == null ? 0.45f : settings.MinimumVisibility,
                settings == null
                    ? LegacyReferenceBodyScale
                    : settings.OffsetNormalizationReferenceBodyScale);
        }

        private PoseFeatureFrame ExtractInternal(
            PoseFrameView frameView,
            string exercise,
            float minimumVisibility,
            float offsetReferenceBodyScale)
        {
            var feature = workingFeature;
            feature.Reset();
            feature.TimestampUnixMilliseconds = frameView == null ? 0L : frameView.TimestampUnixMilliseconds;
            feature.Exercise = string.IsNullOrWhiteSpace(exercise) ? "squat" : exercise;

            if (frameView == null)
            {
                return feature;
            }

            var validFeatureCount = 0;
            var totalFeatureCount = 8;
            var hasBodyScale = TryCalculateBodyScale(frameView, out var bodyScale);
            var offsetScale = hasBodyScale
                ? Mathf.Max(MinimumBodyScale, offsetReferenceBodyScale) / bodyScale
                : 1f;

            feature.HasLeftKneeAngle = TryCalculateKnee(
                frameView,
                PoseJointNames.LeftHip,
                PoseJointNames.LeftKnee,
                PoseJointNames.LeftAnkle,
                offsetScale,
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
                offsetScale,
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

            feature.HasPelvicTilt = TryCalculatePelvicTilt(frameView, out feature.PelvicTiltRatio);

            feature.HasShoulderLevel = TryCalculateLevelDelta(
                frameView,
                PoseJointNames.LeftShoulder,
                PoseJointNames.RightShoulder,
                out feature.ShoulderLevelDelta);
            if (feature.HasShoulderLevel)
            {
                validFeatureCount++;
            }

            feature.HasCenterBalance = TryCalculateCenterBalance(
                frameView,
                hasBodyScale ? bodyScale : 0f,
                offsetScale,
                out feature.CenterBalanceOffset,
                out feature.HipCenterY);
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
            hasPreviousFrame = true;
            previousTimestampUnixMilliseconds = feature.TimestampUnixMilliseconds;
            previousHipCenterY = feature.HipCenterY;
            previousAverageKneeAngle = feature.AverageKneeAngle;
            return feature;
        }

        public void Reset()
        {
            workingFeature.Reset();
            hasPreviousFrame = false;
            previousTimestampUnixMilliseconds = 0L;
            previousHipCenterY = 0f;
            previousAverageKneeAngle = 0f;
        }

        private static bool TryCalculateKnee(
            PoseFrameView frameView,
            string hipName,
            string kneeName,
            string ankleName,
            float offsetScale,
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
            kneeValgusOffset =
                PoseGeometry.DistancePointToLine(knee, hip, ankle) *
                Mathf.Max(0f, offsetScale);
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

            // Landmark coordinates use a screen origin at the top-left, so an
            // upright shoulder-minus-hip vector has a negative y component.
            torsoTiltDegrees = Vector2.Angle(torsoVector, Vector2.down);
            return true;
        }

        private static bool TryCalculatePelvicTilt(PoseFrameView frameView, out float pelvicTiltRatio)
        {
            pelvicTiltRatio = 0f;
            if (!TryGetPosition(frameView, PoseJointNames.LeftHip, out var leftHip) ||
                !TryGetPosition(frameView, PoseJointNames.RightHip, out var rightHip) ||
                !TryGetPosition(frameView, PoseJointNames.LeftShoulder, out var leftShoulder) ||
                !TryGetPosition(frameView, PoseJointNames.RightShoulder, out var rightShoulder))
            {
                return false;
            }

            var hipLine = rightHip - leftHip;
            var shoulderLine = rightShoulder - leftShoulder;
            // A nearly edge-on pose makes projected hip/shoulder width too small for
            // a stable frontal pelvic-level judgement. Treat it as unavailable instead.
            if (Mathf.Abs(hipLine.x) < MinimumFrontalPelvisSpan ||
                Mathf.Abs(shoulderLine.x) < MinimumFrontalPelvisSpan)
            {
                return false;
            }

            // Compare the pelvis to the shoulder line instead of screen-horizontal.
            // This removes camera-roll and whole-body lean that affect both lines in
            // the same way, while retaining a true pelvis-only asymmetry.
            var hipAngle = Mathf.Atan2(hipLine.y, hipLine.x) * Mathf.Rad2Deg;
            var shoulderAngle = Mathf.Atan2(shoulderLine.y, shoulderLine.x) * Mathf.Rad2Deg;
            var relativeAngle = Mathf.Abs(Mathf.DeltaAngle(hipAngle, shoulderAngle));
            if (relativeAngle > 90f)
            {
                relativeAngle = 180f - relativeAngle;
            }

            // tan(90°) is numerically unstable; any relative angle near it is already
            // an unambiguous outlier, so cap only the metric representation.
            pelvicTiltRatio = Mathf.Tan(Mathf.Min(relativeAngle, 85f) * Mathf.Deg2Rad);
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

        private static bool TryCalculateCenterBalance(
            PoseFrameView frameView,
            float bodyScale,
            float offsetScale,
            out float centerBalanceOffset,
            out float hipCenterY)
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
            centerBalanceOffset =
                Mathf.Abs(hipCenter.x - ankleCenter.x) *
                Mathf.Max(0f, offsetScale);
            // Translation- and scale-invariant vertical hip coordinate. It becomes
            // less negative (positive velocity) as the hips descend toward the feet.
            hipCenterY = bodyScale > MinimumBodyScale
                ? (hipCenter.y - ankleCenter.y) / bodyScale
                : hipCenter.y;
            return true;
        }

        private static bool TryCalculateBodyScale(PoseFrameView frameView, out float bodyScale)
        {
            bodyScale = 0f;
            if (!TryGetPosition(frameView, PoseJointNames.LeftShoulder, out var leftShoulder) ||
                !TryGetPosition(frameView, PoseJointNames.RightShoulder, out var rightShoulder) ||
                !TryGetPosition(frameView, PoseJointNames.LeftHip, out var leftHip) ||
                !TryGetPosition(frameView, PoseJointNames.RightHip, out var rightHip) ||
                !TryGetPosition(frameView, PoseJointNames.LeftKnee, out var leftKnee) ||
                !TryGetPosition(frameView, PoseJointNames.RightKnee, out var rightKnee) ||
                !TryGetPosition(frameView, PoseJointNames.LeftAnkle, out var leftAnkle) ||
                !TryGetPosition(frameView, PoseJointNames.RightAnkle, out var rightAnkle))
            {
                return false;
            }

            var shoulderCenter = PoseGeometry.Midpoint(leftShoulder, rightShoulder);
            var hipCenter = PoseGeometry.Midpoint(leftHip, rightHip);
            var averageThighLength =
                (Vector2.Distance(leftHip, leftKnee) +
                 Vector2.Distance(rightHip, rightKnee)) * 0.5f;
            var averageShinLength =
                (Vector2.Distance(leftKnee, leftAnkle) +
                 Vector2.Distance(rightKnee, rightAnkle)) * 0.5f;
            bodyScale =
                Vector2.Distance(shoulderCenter, hipCenter) +
                averageThighLength +
                averageShinLength;
            return bodyScale > MinimumBodyScale;
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
            if (!hasPreviousFrame || feature.TimestampUnixMilliseconds <= previousTimestampUnixMilliseconds)
            {
                return;
            }

            var deltaSeconds = (feature.TimestampUnixMilliseconds - previousTimestampUnixMilliseconds) / 1000f;
            if (deltaSeconds <= Mathf.Epsilon)
            {
                return;
            }

            feature.HipCenterYVelocityPerSecond = (feature.HipCenterY - previousHipCenterY) / deltaSeconds;
            feature.KneeAngleVelocityDegreesPerSecond = (feature.AverageKneeAngle - previousAverageKneeAngle) / deltaSeconds;
        }
    }
}
