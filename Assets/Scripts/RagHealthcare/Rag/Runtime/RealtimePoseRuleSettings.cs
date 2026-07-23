using System;
using UnityEngine;

namespace Rag.Healthcare.Rag.Runtime
{
    [Serializable]
    public sealed class RealtimePoseRuleSettings
    {
        [Header("Landmark stability")]
        [Range(0f, 1f)] public float minimumVisibility = 0.45f;
#if UNITY_IOS && !UNITY_EDITOR
        [Range(0.05f, 1f)] public float landmarkSmoothingAlpha = 0.55f;
#else
        [Range(0.05f, 1f)] public float landmarkSmoothingAlpha = 0.35f;
#endif
        [Range(0.01f, 0.3f)] public float maximumNormalizedJointJump = 0.12f;
        [Range(0f, 0.5f)] public float lowConfidenceGraceSeconds = 0.35f;
        [Range(0, 4)] public int maximumConsecutiveOutlierFrames = 3;

        [Header("Adaptive landmark stability")]
        [Range(0.05f, 0.8f)] public float stationarySmoothingAlpha = 0.24f;
        [Range(0.2f, 1f)] public float movingSmoothingAlpha = 0.72f;
        [Range(0.1f, 3f)] public float adaptiveMotionSpeed = 1.2f;
        [Range(0.1f, 1.5f)] public float maximumBodyScaleJointJump = 0.6f;
        [Range(0.01f, 0.15f)] public float minimumBodyScaleJointJump = 0.04f;
        [Range(0.05f, 0.3f)] public float maximumTrackingHoldSeconds = 0.25f;

        [Header("Front camera tracking quality")]
        [Range(0f, 1f)] public float minimumTrackingQualityConfidence = 0.45f;
        [Range(0.02f, 0.25f)] public float minimumFrontalBodySpan = 0.08f;
        [Range(0.15f, 0.8f)] public float minimumTrackedBodyHeight = 0.35f;
        [Range(0f, 0.08f)] public float minimumBodyFrameMargin = 0.01f;
        [Range(1, 10)] public int trackingQualityGoodFrames = 3;
        [Range(1, 10)] public int trackingQualityUnavailableFrames = 3;

        [Header("Temporal evidence")]
        [Range(0f, 1f)] public float minimumValidCoreFrameRatio = 0.45f;
        [Range(0f, 1f)] public float minimumViolationRatio = 0.35f;
        [Range(1, 30)] public int minimumRuleEvaluationFrames = 6;
        [Range(1, 30)] public int minimumValidRepFrames = 4;
        [Range(0f, 1f)] public float minimumRepViolationRatio = 0.35f;
        [Range(0f, 1f)] public float immediateViolationPersistenceRatio = 0.75f;
        [Range(1, 5)] public int minimumCriticalFrames = 2;

        [Header("Pose thresholds")]
        [Range(0f, 0.5f)] public float maximumKneeValgusOffset = 0.15f;
        [Range(0f, 1f)] public float minimumKneeObservationRatio = 0.6f;
        [Range(0f, 180f)] public float standingKneeAngle = 150f;
        [Range(0f, 180f)] public float standingExitKneeAngle = 140f;
        [Range(0f, 180f)] public float bottomKneeAngle = 125f;
        [Range(0f, 180f)] public float bottomExitKneeAngle = 150f;
        [Range(0f, 180f)] public float maximumRecognizableBottomKneeAngle = 175f;
        [Range(0f, 180f)] public float maximumBottomKneeAngle = 170f;
        [Range(0f, 180f)] public float minimumBottomKneeAngle = 55f;
        [Range(0f, 90f)] public float maximumLeftRightKneeAngleDelta = 18f;
        [Range(0f, 90f)] public float maximumTorsoTiltDegrees = 42f;
        [Tooltip("Maximum pelvis-to-shoulder relative slope. Disabled for near edge-on poses.")]
        [Range(0.05f, 1f)] public float maximumPelvicTiltRatio = 0.25f;
        [Range(0f, 0.5f)] public float maximumCenterBalanceOffset = 0.16f;
        [Range(0f, 180f)] public float phaseVelocityDeadZoneDegreesPerSecond = 12f;
        [Range(0f, 0.5f)] public float minimumBottomDwellSeconds = 0.15f;

        public float MinimumVisibility => minimumVisibility;
        // Unity can deserialize newly added fields as zero on older scene/prefab data.
        // These accessors keep that migration case from disabling or freezing tracking.
        public float StationarySmoothingAlpha => stationarySmoothingAlpha > 0f
            ? Mathf.Clamp(stationarySmoothingAlpha, 0.05f, 0.8f)
            : 0.24f;
        public float MovingSmoothingAlpha => movingSmoothingAlpha > 0f
            ? Mathf.Clamp(movingSmoothingAlpha, 0.2f, 1f)
            : 0.72f;
        public float AdaptiveMotionSpeed => adaptiveMotionSpeed > 0f
            ? Mathf.Clamp(adaptiveMotionSpeed, 0.1f, 3f)
            : 1.2f;
        public float MaximumBodyScaleJointJump => maximumBodyScaleJointJump > 0f
            ? Mathf.Clamp(maximumBodyScaleJointJump, 0.1f, 1.5f)
            : 0.6f;
        public float MinimumBodyScaleJointJump => minimumBodyScaleJointJump > 0f
            ? Mathf.Clamp(minimumBodyScaleJointJump, 0.01f, 0.15f)
            : 0.04f;
        public float MaximumTrackingHoldSeconds => maximumTrackingHoldSeconds > 0f
            ? Mathf.Clamp(maximumTrackingHoldSeconds, 0.05f, 0.3f)
            : 0.25f;
        public float MinimumTrackingQualityConfidence => minimumTrackingQualityConfidence > 0f
            ? Mathf.Clamp01(minimumTrackingQualityConfidence)
            : Mathf.Max(0.01f, MinimumVisibility);
        public float MinimumFrontalBodySpan => minimumFrontalBodySpan > 0f
            ? Mathf.Clamp(minimumFrontalBodySpan, 0.02f, 0.25f)
            : 0.08f;
        public float MinimumTrackedBodyHeight => minimumTrackedBodyHeight > 0f
            ? Mathf.Clamp(minimumTrackedBodyHeight, 0.15f, 0.8f)
            : 0.35f;
        public float MinimumBodyFrameMargin => Mathf.Clamp(minimumBodyFrameMargin, 0f, 0.08f);
        public int TrackingQualityGoodFrames => trackingQualityGoodFrames > 0
            ? Mathf.Clamp(trackingQualityGoodFrames, 1, 10)
            : 3;
        public int TrackingQualityUnavailableFrames => trackingQualityUnavailableFrames > 0
            ? Mathf.Clamp(trackingQualityUnavailableFrames, 1, 10)
            : 3;
        public float MinimumValidCoreFrameRatio => minimumValidCoreFrameRatio;
        public float MinimumViolationRatio => minimumViolationRatio;
        public float MaximumKneeValgusOffset => maximumKneeValgusOffset;
        public float MinimumKneeObservationRatio => minimumKneeObservationRatio;
        public float StandingKneeAngle => standingKneeAngle;
        public float BottomKneeAngle => bottomKneeAngle;
        public float MaximumRecognizableBottomKneeAngle => maximumRecognizableBottomKneeAngle;
        public float MaximumBottomKneeAngle => maximumBottomKneeAngle;
        public float MinimumBottomKneeAngle => minimumBottomKneeAngle;
        public float MaximumLeftRightKneeAngleDelta => maximumLeftRightKneeAngleDelta;
        public float MaximumTorsoTiltDegrees => maximumTorsoTiltDegrees;
        public float MaximumPelvicTiltRatio => maximumPelvicTiltRatio;
        public float MaximumCenterBalanceOffset => maximumCenterBalanceOffset;
        public float PhaseVelocityDeadZoneDegreesPerSecond => phaseVelocityDeadZoneDegreesPerSecond;
    }
}
