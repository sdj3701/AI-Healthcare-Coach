using System;
using UnityEngine;

namespace Rag.Healthcare.Rag.Runtime
{
    [Serializable]
    public sealed class RealtimePoseRuleSettings
    {
        [Range(0f, 1f)] public float minimumVisibility = 0.5f;
        [Range(0f, 1f)] public float minimumValidCoreFrameRatio = 0.55f;
        [Range(0f, 1f)] public float minimumViolationRatio = 0.45f;

        [Range(0f, 0.5f)] public float maximumKneeValgusOffset = 0.08f;
        [Range(0f, 180f)] public float standingKneeAngle = 160f;
        [Range(0f, 180f)] public float bottomKneeAngle = 110f;
        [Range(0f, 180f)] public float maximumBottomKneeAngle = 135f;
        [Range(0f, 180f)] public float minimumBottomKneeAngle = 55f;
        [Range(0f, 90f)] public float maximumLeftRightKneeAngleDelta = 18f;
        [Range(0f, 90f)] public float maximumTorsoTiltDegrees = 35f;
        [Range(0f, 0.5f)] public float maximumCenterBalanceOffset = 0.12f;
        [Range(0f, 180f)] public float phaseVelocityDeadZoneDegreesPerSecond = 8f;

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
