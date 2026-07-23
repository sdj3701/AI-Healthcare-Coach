using System;
using Rag.Healthcare.Rag.Runtime;
using UnityEngine;

namespace Rag.Healthcare.Product
{
    /// <summary>
    /// Persists health/workout profile separately from consent onboarding (ahc.profile.v1).
    /// </summary>
    public sealed class OnboardingStatusManager : MonoBehaviour
    {
        private const string PreferencesKey = "ahc.profile.v1";

        public UserProfileData Profile { get; private set; }
        public bool HasCompletedProfile => Profile != null && Profile.IsComplete;
        public bool HasCompletedCalibration => Profile != null && Profile.IsCalibrationComplete;
        public event Action<UserProfileData> Changed;

        private void Awake()
        {
            Load();
        }

        public void SetBodyMetrics(int ageYears, Gender gender, float heightCm, float weightKg)
        {
            EnsureProfile();
            Profile.ageYears = ageYears;
            Profile.gender = gender;
            Profile.heightCm = heightCm;
            Profile.weightKg = weightKg;
            SaveAndNotify();
        }

        public void SetInjuries(InjuryRegions injuries)
        {
            EnsureProfile();
            Profile.injuries = injuries;
            SaveAndNotify();
        }

        public void SetGoalPlaceEquipment(WorkoutGoal goal, WorkoutPlace place, EquipmentFlags equipment)
        {
            EnsureProfile();
            Profile.goal = goal;
            Profile.place = place;
            Profile.equipment = equipment;
            SaveAndNotify();
        }

        public void SetFrequencyAndSkill(int sessionsPerWeek, SkillLevel skill)
        {
            EnsureProfile();
            Profile.sessionsPerWeek = sessionsPerWeek;
            Profile.skill = skill;
            SaveAndNotify();
        }

        public void CommitWorkoutPreferences(
            InjuryRegions injuries,
            WorkoutGoal goal,
            WorkoutPlace place,
            EquipmentFlags equipment,
            int sessionsPerWeek,
            SkillLevel skill,
            PersonalizedRomEvaluator evaluator)
        {
            EnsureProfile();
            Profile.injuries = injuries;
            Profile.goal = goal;
            Profile.place = place;
            Profile.equipment = equipment;
            Profile.sessionsPerWeek = sessionsPerWeek;
            Profile.skill = skill;
            Profile.onboardingCompleted = true;
            if (evaluator != null)
            {
                Profile.romSafety = evaluator.Evaluate(Profile);
            }

            SaveAndNotify();
        }

        public void CommitProfile(PersonalizedRomEvaluator evaluator)
        {
            EnsureProfile();
            Profile.onboardingCompleted = true;
            if (evaluator != null)
            {
                Profile.romSafety = evaluator.Evaluate(Profile);
            }

            SaveAndNotify();
        }

        public void MarkCalibrationComplete()
        {
            EnsureProfile();
            Profile.calibrationCompleted = true;
            Profile.calibrationCompletedAtUtc = DateTime.UtcNow.ToString("o");
            SaveAndNotify();
        }

        public void ClearCalibration()
        {
            EnsureProfile();
            Profile.calibrationCompleted = false;
            Profile.calibrationCompletedAtUtc = string.Empty;
            SaveAndNotify();
        }

        public void ResetProfile()
        {
            PlayerPrefs.DeleteKey(PreferencesKey);
            PlayerPrefs.Save();
            Profile = CreateDefault();
            Changed?.Invoke(Profile);
        }

        private void Load()
        {
            Profile = CreateDefault();
            var json = PlayerPrefs.GetString(PreferencesKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(json, Profile);
                    if (Profile.romSafety == null)
                    {
                        Profile.romSafety = new RomSafetyProfile();
                    }

                    if (!Profile.onboardingCompleted && HasLegacyCompletedProfile(Profile))
                    {
                        Profile.onboardingCompleted = true;
                        PlayerPrefs.SetString(PreferencesKey, JsonUtility.ToJson(Profile));
                        PlayerPrefs.Save();
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[OnboardingStatus] Saved profile could not be read: " + exception.Message);
                }
            }
        }

        private static UserProfileData CreateDefault()
        {
            return new UserProfileData();
        }

        private static bool HasLegacyCompletedProfile(UserProfileData profile)
        {
            return profile != null &&
                   profile.heightCm > 0f &&
                   profile.weightKg > 0f &&
                   profile.ageYears > 0 &&
                   profile.gender != Gender.Unspecified &&
                   profile.sessionsPerWeek > 0;
        }

        private void EnsureProfile()
        {
            if (Profile == null)
            {
                Profile = CreateDefault();
            }

            if (Profile.romSafety == null)
            {
                Profile.romSafety = new RomSafetyProfile();
            }
        }

        private void SaveAndNotify()
        {
            EnsureProfile();
            var now = DateTime.UtcNow.ToString("o");
            if (string.IsNullOrWhiteSpace(Profile.createdAtUtc))
            {
                Profile.createdAtUtc = now;
            }

            Profile.updatedAtUtc = now;
            PlayerPrefs.SetString(PreferencesKey, JsonUtility.ToJson(Profile));
            PlayerPrefs.Save();
            Changed?.Invoke(Profile);
        }
    }
}
