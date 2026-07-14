using System;
using UnityEngine;

namespace Rag.Healthcare.Rag.Runtime
{
    [Serializable]
    public sealed class RealtimePoseRuleSettings
    {
        [Header("Landmark stability")]
        [Range(0f, 1f)] public float minimumVisibility = 0.45f;
        [Range(0.05f, 1f)] public float landmarkSmoothingAlpha = 0.35f;
        [Range(0.01f, 0.3f)] public float maximumNormalizedJointJump = 0.08f;
        [Range(0f, 0.5f)] public float lowConfidenceGraceSeconds = 0.2f;
        [Range(0, 4)] public int maximumConsecutiveOutlierFrames = 1;

        [Header("Temporal evidence")]
        [Range(0f, 1f)] public float minimumValidCoreFrameRatio = 0.45f;
        [Range(0f, 1f)] public float minimumViolationRatio = 0.35f;
        [Range(1, 30)] public int minimumRuleEvaluationFrames = 6;
        [Range(1, 30)] public int minimumValidRepFrames = 6;
        [Range(0f, 1f)] public float minimumRepViolationRatio = 0.35f;
        [Range(0f, 1f)] public float immediateViolationPersistenceRatio = 0.75f;
        [Range(1, 5)] public int minimumCriticalFrames = 2;

        [Header("Pose thresholds")]
        [Range(0f, 0.5f)] public float maximumKneeValgusOffset = 0.08f;
        [Range(0f, 180f)] public float standingKneeAngle = 160f;
        [Range(0f, 180f)] public float standingExitKneeAngle = 150f;
        [Range(0f, 180f)] public float bottomKneeAngle = 125f;
        [Range(0f, 180f)] public float bottomExitKneeAngle = 135f;
        [Range(0f, 180f)] public float maximumRecognizableBottomKneeAngle = 145f;
        [Range(0f, 180f)] public float maximumBottomKneeAngle = 135f;
        [Range(0f, 180f)] public float minimumBottomKneeAngle = 55f;
        [Range(0f, 90f)] public float maximumLeftRightKneeAngleDelta = 18f;
        [Range(0f, 90f)] public float maximumTorsoTiltDegrees = 35f;
        [Range(0f, 0.5f)] public float maximumCenterBalanceOffset = 0.12f;
        [Range(0f, 180f)] public float phaseVelocityDeadZoneDegreesPerSecond = 12f;
        [Range(0f, 0.5f)] public float minimumBottomDwellSeconds = 0.15f;

        public float MinimumVisibility => minimumVisibility;
        public float MinimumValidCoreFrameRatio => minimumValidCoreFrameRatio;
        public float MinimumViolationRatio => minimumViolationRatio;
        public float MaximumKneeValgusOffset => maximumKneeValgusOffset;
        public float StandingKneeAngle => standingKneeAngle;
        public float BottomKneeAngle => bottomKneeAngle;
        public float MaximumBottomKneeAngle => maximumBottomKneeAngle;
        public float MinimumBottomKneeAngle => minimumBottomKneeAngle;
        public float MaximumLeftRightKneeAngleDelta => maximumLeftRightKneeAngleDelta;
        public float MaximumTorsoTiltDegrees => maximumTorsoTiltDegrees;
        public float MaximumCenterBalanceOffset => maximumCenterBalanceOffset;
        public float PhaseVelocityDeadZoneDegreesPerSecond => phaseVelocityDeadZoneDegreesPerSecond;
    }
}
