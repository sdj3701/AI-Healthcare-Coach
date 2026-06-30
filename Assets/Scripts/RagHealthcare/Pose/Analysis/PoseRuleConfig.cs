using UnityEngine;

namespace Rag.Healthcare.Pose.Analysis
{
    [CreateAssetMenu(menuName = "Rag Healthcare/Pose Rule Config")]
    public sealed class PoseRuleConfig : ScriptableObject
    {
        [Range(0f, 1f)] public float minimumVisibility = 0.5f;
        [Range(0f, 0.5f)] public float maximumKneeValgusOffset = 0.08f;
        [Range(0f, 180f)] public float minimumSquatKneeAngle = 70f;
        [Range(0f, 180f)] public float maximumSquatKneeAngle = 165f;
        [Range(0f, 90f)] public float maximumLeftRightKneeAngleDelta = 18f;
        [Range(0f, 90f)] public float maximumTorsoTiltDegrees = 35f;
        [Range(0f, 0.5f)] public float maximumHipLevelDelta = 0.08f;
        [Range(0f, 0.5f)] public float maximumShoulderLevelDelta = 0.08f;
        [Range(0f, 0.5f)] public float maximumCenterBalanceOffset = 0.12f;
        [Range(0f, 10f)] public float feedbackCooldownSeconds = 2f;
    }
}
