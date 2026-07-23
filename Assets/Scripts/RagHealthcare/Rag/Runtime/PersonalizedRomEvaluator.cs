using System.Collections.Generic;
using Rag.Healthcare.Product;
using UnityEngine;

namespace Rag.Healthcare.Rag.Runtime
{
    /// <summary>
    /// Deterministic coaching safety derate from UserProfileData (non-medical).
    /// </summary>
    public sealed class PersonalizedRomEvaluator
    {
        public RomSafetyProfile Evaluate(UserProfileData profile)
        {
            var result = new RomSafetyProfile();
            if (profile == null)
            {
                return result;
            }

            var injuries = profile.injuries;
            var reasons = new List<string>(4);

            if ((injuries & InjuryRegions.Knee) != 0)
            {
                result.minimumBottomKneeAngleDelta = MaxDelta(result.minimumBottomKneeAngleDelta, 25f);
                result.maximumBottomKneeAngleDelta = MaxDelta(result.maximumBottomKneeAngleDelta, 5f);
                result.suppressDeeperEncouragement = true;
                reasons.Add("Knee-friendly depth coaching");
            }

            if ((injuries & InjuryRegions.LowerBack) != 0)
            {
                result.maximumTorsoTiltDegreesDelta = MinDelta(result.maximumTorsoTiltDegreesDelta, -12f);
                result.minimumBottomKneeAngleDelta = MaxDelta(result.minimumBottomKneeAngleDelta, 15f);
                reasons.Add("Torso-tilt sensitive coaching");
            }

            if ((injuries & InjuryRegions.Shoulder) != 0)
            {
                reasons.Add("Shoulder noted for upper-body sessions");
            }

            if ((injuries & InjuryRegions.Neck) != 0)
            {
                reasons.Add("Neck-friendly gaze cues");
            }

            if (profile.skill == SkillLevel.Beginner)
            {
                result.maximumBottomKneeAngleDelta = MaxDelta(result.maximumBottomKneeAngleDelta, 5f);
                result.suppressDeeperEncouragement = true;
                reasons.Add("Beginner-friendly depth coaching");
            }

            result.derateReason = reasons.Count > 0
                ? string.Join("; ", reasons)
                : string.Empty;

            return result;
        }

        public RealtimePoseRuleSettings ApplyDerate(
            RealtimePoseRuleSettings baseSettings,
            RomSafetyProfile derate)
        {
            if (baseSettings == null)
            {
                return null;
            }

            var copy = CloneSettings(baseSettings);
            if (derate == null)
            {
                return copy;
            }

            copy.minimumBottomKneeAngle = ClampKnee(
                copy.minimumBottomKneeAngle + derate.minimumBottomKneeAngleDelta);
            copy.maximumBottomKneeAngle = ClampKnee(
                copy.maximumBottomKneeAngle + derate.maximumBottomKneeAngleDelta);
            copy.bottomKneeAngle = ClampKnee(
                copy.bottomKneeAngle + derate.bottomKneeAngleDelta);
            copy.maximumTorsoTiltDegrees = Mathf.Max(
                10f,
                copy.maximumTorsoTiltDegrees + derate.maximumTorsoTiltDegreesDelta);

            return copy;
        }

        private static RealtimePoseRuleSettings CloneSettings(RealtimePoseRuleSettings source)
        {
            var json = JsonUtility.ToJson(source);
            var copy = new RealtimePoseRuleSettings();
            JsonUtility.FromJsonOverwrite(json, copy);
            return copy;
        }

        private static float ClampKnee(float degrees)
        {
            return Mathf.Clamp(degrees, 0f, 180f);
        }

        /// <summary>Conservative merge for depth-floor deltas (larger = safer).</summary>
        private static float MaxDelta(float current, float candidate)
        {
            return Mathf.Max(current, candidate);
        }

        /// <summary>Conservative merge for torso-tilt deltas (more negative = safer).</summary>
        private static float MinDelta(float current, float candidate)
        {
            return Mathf.Min(current, candidate);
        }
    }
}
