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

        [Header("Front camera squat phase")]
        [Tooltip("Normalized hip drop allowed while the body is still considered standing.")]
        [Range(0.005f, 0.15f)] public float standingHipDropTolerance = 0.025f;
        [Tooltip("Minimum normalized hip drop or knee excursion needed before a direction reversal can be a squat bottom.")]
        [Range(0.01f, 0.25f)] public float minimumRecognizableHipDrop = 0.04f;
        [Tooltip("Minimum normalized hip drop required, together with knee depth, for a completed rep.")]
        [Range(0.02f, 0.35f)] public float minimumBottomHipDrop = 0.08f;
        [Range(0.01f, 1f)] public float phaseHipVelocityDeadZonePerSecond = 0.08f;
        [Range(1f, 45f)] public float minimumPhaseKneeAngleExcursion = 8f;

        [Header("Adaptive squat completion")]
        [Tooltip("Baseline normalized hip/knee level. The accepted 2D gate also applies HipToKneeLevelTolerance.")]
        [Range(0f, 0.15f)] public float minimumHipToKneeDepth = 0f;
        [Tooltip("2D measurement tolerance above knee height. This absorbs camera projection and landmark jitter; secondary depth evidence is still required.")]
        [Range(0f, 0.08f)] public float hipToKneeLevelTolerance = 0.03f;
        [Tooltip("Consecutive reliable frames required inside the accepted 2D hip/knee level band.")]
        [Range(1, 6)] public int minimumHipToKneeDepthFrames = 2;
        [Tooltip("Secondary depth guardrail. A near-level hip/knee pose must also reach this knee angle or the configured hip-drop distance.")]
        [Range(90f, 160f)] public float maximumCountableBottomKneeAngle = 135f;
        [Tooltip("Accepted reps used to learn the session-specific bottom knee angle.")]
        [Range(1, 6)] public int adaptiveBottomSampleCount = 3;
        [Tooltip("Bounded recognition margin added to the learned knee angle. It never bypasses either stage of the depth gate.")]
        [Range(0f, 20f)] public float adaptiveBottomKneeAngleMargin = 8f;

        [Header("Sequential squat bottom decision")]
        [Tooltip("Knee width divided by ankle width. Values below this can indicate inward knee collapse.")]
        [Range(0.5f, 1f)] public float minimumKneeWidthRatio = 0.8f;
        [Tooltip("Minimum ankle stance width required before the knee-width ratio can be trusted.")]
        [Range(0.02f, 0.3f)] public float minimumKneeWidthStanceSpan = 0.08f;
        [Tooltip("Consecutive reliable frames required for inward knee collapse.")]
        [Range(1, 6)] public int minimumKneeCollapseFrames = 2;
        [Tooltip("Diagnostic-only count of consecutive frames below the legacy deep-angle threshold. It does not reject or speak feedback for a rep.")]
        [Range(1, 6)] public int minimumExcessiveDepthFrames = 2;

        [Header("Session squat depth personalization")]
        [Tooltip("Consecutive high-quality personal-depth failures required before adapting the session target.")]
        [Range(2, 6)] public int personalDepthFailureSampleCount = 3;
        [Tooltip("Maximum spread allowed between candidate minimum knee angles.")]
        [Range(1f, 20f)] public float maximumPersonalDepthKneeAngleSpread = 8f;
        [Tooltip("Maximum spread allowed between candidate standing-relative hip drops.")]
        [Range(0.005f, 0.08f)] public float maximumPersonalDepthHipDropSpread = 0.02f;
        [Tooltip("Margin added to the median failed knee angle when adapting the next rep.")]
        [Range(0f, 10f)] public float personalizedKneeAngleMargin = 3f;
        [Tooltip("Margin subtracted from the median failed hip drop when adapting the next rep.")]
        [Range(0f, 0.04f)] public float personalizedHipDropMargin = 0.01f;
        [Tooltip("Absolute ceiling for a session-personalized countable knee angle.")]
        [Range(135f, 160f)] public float maximumPersonalizedBottomKneeAngle = 150f;
        [Tooltip("Absolute floor for a session-personalized standing-relative hip drop.")]
        [Range(0.02f, 0.08f)] public float minimumPersonalizedBottomHipDrop = 0.05f;

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
        [Tooltip("Body-height reference used to keep legacy screen-space offset thresholds comparable after scale normalization.")]
        [Range(0.2f, 0.8f)] public float offsetNormalizationReferenceBodyScale = 0.5f;

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
        public float StandingHipDropTolerance => standingHipDropTolerance > 0f
            ? Mathf.Clamp(standingHipDropTolerance, 0.005f, 0.15f)
            : 0.025f;
        public float MinimumRecognizableHipDrop => minimumRecognizableHipDrop > 0f
            ? Mathf.Clamp(minimumRecognizableHipDrop, 0.01f, 0.25f)
            : 0.04f;
        public float MinimumBottomHipDrop => minimumBottomHipDrop > 0f
            ? Mathf.Clamp(minimumBottomHipDrop, 0.02f, 0.35f)
            : 0.08f;
        public float PhaseHipVelocityDeadZonePerSecond => phaseHipVelocityDeadZonePerSecond > 0f
            ? Mathf.Clamp(phaseHipVelocityDeadZonePerSecond, 0.01f, 1f)
            : 0.08f;
        public float MinimumPhaseKneeAngleExcursion => minimumPhaseKneeAngleExcursion > 0f
            ? Mathf.Clamp(minimumPhaseKneeAngleExcursion, 1f, 45f)
            : 8f;
        public float MinimumHipToKneeDepth =>
            Mathf.Clamp(minimumHipToKneeDepth, 0f, 0.15f);
        public float HipToKneeLevelTolerance => hipToKneeLevelTolerance > 0f
            ? Mathf.Clamp(hipToKneeLevelTolerance, 0f, 0.08f)
            : 0.03f;
        public float MinimumAcceptedHipToKneeDepth =>
            MinimumHipToKneeDepth - HipToKneeLevelTolerance;
        public int MinimumHipToKneeDepthFrames => minimumHipToKneeDepthFrames > 0
            ? Mathf.Clamp(minimumHipToKneeDepthFrames, 1, 6)
            : 2;
        public float MaximumCountableBottomKneeAngle =>
            maximumCountableBottomKneeAngle > 0f
                ? Mathf.Clamp(maximumCountableBottomKneeAngle, 90f, 160f)
                : 135f;
        public int AdaptiveBottomSampleCount => adaptiveBottomSampleCount > 0
            ? Mathf.Clamp(adaptiveBottomSampleCount, 1, 6)
            : 3;
        public float AdaptiveBottomKneeAngleMargin => adaptiveBottomKneeAngleMargin > 0f
            ? Mathf.Clamp(adaptiveBottomKneeAngleMargin, 0f, 20f)
            : 8f;
        public float MinimumKneeWidthRatio => minimumKneeWidthRatio > 0f
            ? Mathf.Clamp(minimumKneeWidthRatio, 0.5f, 1f)
            : 0.8f;
        public float MinimumKneeWidthStanceSpan =>
            minimumKneeWidthStanceSpan > 0f
                ? Mathf.Clamp(minimumKneeWidthStanceSpan, 0.02f, 0.3f)
                : 0.08f;
        public int MinimumKneeCollapseFrames => minimumKneeCollapseFrames > 0
            ? Mathf.Clamp(minimumKneeCollapseFrames, 1, 6)
            : 2;
        public int MinimumExcessiveDepthFrames =>
            minimumExcessiveDepthFrames > 0
                ? Mathf.Clamp(minimumExcessiveDepthFrames, 1, 6)
                : 2;
        public int PersonalDepthFailureSampleCount =>
            personalDepthFailureSampleCount > 0
                ? Mathf.Clamp(personalDepthFailureSampleCount, 2, 6)
                : 3;
        public float MaximumPersonalDepthKneeAngleSpread =>
            maximumPersonalDepthKneeAngleSpread > 0f
                ? Mathf.Clamp(maximumPersonalDepthKneeAngleSpread, 1f, 20f)
                : 8f;
        public float MaximumPersonalDepthHipDropSpread =>
            maximumPersonalDepthHipDropSpread > 0f
                ? Mathf.Clamp(maximumPersonalDepthHipDropSpread, 0.005f, 0.08f)
                : 0.02f;
        public float PersonalizedKneeAngleMargin =>
            personalizedKneeAngleMargin > 0f
                ? Mathf.Clamp(personalizedKneeAngleMargin, 0f, 10f)
                : 3f;
        public float PersonalizedHipDropMargin =>
            personalizedHipDropMargin > 0f
                ? Mathf.Clamp(personalizedHipDropMargin, 0f, 0.04f)
                : 0.01f;
        public float MaximumPersonalizedBottomKneeAngle =>
            maximumPersonalizedBottomKneeAngle > 0f
                ? Mathf.Clamp(
                    maximumPersonalizedBottomKneeAngle,
                    135f,
                    160f)
                : 150f;
        public float MinimumPersonalizedBottomHipDrop =>
            minimumPersonalizedBottomHipDrop > 0f
                ? Mathf.Clamp(
                    minimumPersonalizedBottomHipDrop,
                    0.02f,
                    0.08f)
                : 0.05f;
        public float OffsetNormalizationReferenceBodyScale => offsetNormalizationReferenceBodyScale > 0f
            ? Mathf.Clamp(offsetNormalizationReferenceBodyScale, 0.2f, 0.8f)
            : 0.5f;
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
