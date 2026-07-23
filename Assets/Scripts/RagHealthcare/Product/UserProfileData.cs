using System;

namespace Rag.Healthcare.Product
{
    public enum Gender
    {
        Unspecified = 0,
        Male,
        Female,
        Other
    }

    [Flags]
    public enum InjuryRegions
    {
        None = 0,
        Shoulder = 1 << 0,
        LowerBack = 1 << 1,
        Knee = 1 << 2,
        Neck = 1 << 3
    }

    public enum WorkoutGoal
    {
        Unspecified = 0,
        GeneralFitness,
        WeightLoss,
        MuscleGain,
        Mobility,
        Endurance
    }

    public enum WorkoutPlace
    {
        Unspecified = 0,
        Home,
        Gym,
        Outdoor
    }

    [Flags]
    public enum EquipmentFlags
    {
        None = 0,
        Bodyweight = 1 << 0,
        Dumbbell = 1 << 1,
        Barbell = 1 << 2,
        Machine = 1 << 3,
        Band = 1 << 4
    }

    public enum SkillLevel
    {
        Beginner = 0,
        Standard = 1,
        Advanced = 2
    }

    /// <summary>
    /// Coaching safety derate deltas applied to RealtimePoseRuleSettings (non-medical).
    /// </summary>
    [Serializable]
    public sealed class RomSafetyProfile
    {
        public float bottomKneeAngleDelta;
        public float minimumBottomKneeAngleDelta;
        public float maximumBottomKneeAngleDelta;
        public float maximumTorsoTiltDegreesDelta;
        public bool suppressDeeperEncouragement;
        public string derateReason = string.Empty;
    }

    [Serializable]
    public sealed class UserProfileData
    {
        public int schemaVersion = 1;
        public string createdAtUtc;
        public string updatedAtUtc;

        public int ageYears;
        public Gender gender = Gender.Unspecified;
        public float heightCm;
        public float weightKg;
        public bool bodyMetricsFromHealthProvider;

        public InjuryRegions injuries = InjuryRegions.None;
        public WorkoutGoal goal = WorkoutGoal.GeneralFitness;
        public WorkoutPlace place = WorkoutPlace.Home;
        public EquipmentFlags equipment = EquipmentFlags.Bodyweight;
        public int sessionsPerWeek;
        public SkillLevel skill = SkillLevel.Beginner;
        public bool onboardingCompleted;

        public RomSafetyProfile romSafety = new RomSafetyProfile();

        // Full-body calibration persistence (same profile blob as body/workout info).
        public bool calibrationCompleted;
        public string calibrationCompletedAtUtc;

        public bool IsComplete =>
            onboardingCompleted &&
            heightCm > 0f &&
            weightKg > 0f &&
            ageYears > 0 &&
            gender != Gender.Unspecified;

        public bool IsCalibrationComplete => calibrationCompleted;
    }
}
