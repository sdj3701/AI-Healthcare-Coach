using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Rag.Healthcare.Product
{
    public enum ConditionCheck
    {
        NotAnswered,
        Ready,
        MildDiscomfort,
        PainOrDizziness
    }

    [Serializable]
    public sealed class OnboardingSnapshot
    {
        public bool privacyNoticeAccepted;
        public bool cameraPurposeAccepted;
        public bool safetyNoticeAccepted;
        public bool coordinateLoggingEnabled;
        public bool optionalModelDownloadAccepted;
        public ConditionCheck condition;
        public string acceptedAtUtc;

        public bool CanStartWorkout =>
            privacyNoticeAccepted &&
            cameraPurposeAccepted &&
            safetyNoticeAccepted &&
            condition == ConditionCheck.Ready;
    }

    public sealed class OnboardingFlowController : MonoBehaviour
    {
        private const string PreferencesKey = "ahc.onboarding.v1";

        [SerializeField] private bool coordinateLoggingDefault;
        [SerializeField] private bool optionalModelDownloadDefault;

        public event Action<OnboardingSnapshot> Changed;
        public event Action<string> PermissionFailed;

        public OnboardingSnapshot Snapshot { get; private set; }
        public bool HasCameraPermission => Application.HasUserAuthorization(UserAuthorization.WebCam);

        private void Awake()
        {
            Load();
        }

        public void AcceptPrivacyNotice(bool accepted)
        {
            Snapshot.privacyNoticeAccepted = accepted;
            SaveAndNotify();
        }

        public void AcceptCameraPurpose(bool accepted)
        {
            Snapshot.cameraPurposeAccepted = accepted;
            SaveAndNotify();
        }

        public void AcceptSafetyNotice(bool accepted)
        {
            Snapshot.safetyNoticeAccepted = accepted;
            SaveAndNotify();
        }

        public void SetCondition(ConditionCheck condition)
        {
            Snapshot.condition = condition;
            SaveAndNotify();
        }

        public void SetCoordinateLogging(bool enabled)
        {
            Snapshot.coordinateLoggingEnabled = enabled;
            SaveAndNotify();
        }

        public void SetOptionalModelDownload(bool accepted)
        {
            Snapshot.optionalModelDownloadAccepted = accepted;
            SaveAndNotify();
        }

        public IEnumerator RequestCameraPermission()
        {
            if (HasCameraPermission)
            {
                yield break;
            }

            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            if (!HasCameraPermission)
            {
                PermissionFailed?.Invoke("카메라 권한이 거부되었습니다. 설정에서 권한을 허용해야 자세 코칭을 시작할 수 있습니다.");
            }
        }

        public bool DeleteAllLocalWorkoutData(out string error)
        {
            error = string.Empty;
            try
            {
                var paths = new[]
                {
                    Path.Combine(Application.persistentDataPath, "pose_sessions"),
                    Path.Combine(Application.persistentDataPath, "rag_sessions"),
                    Path.Combine(Application.persistentDataPath, "reports")
                };

                foreach (var path in paths)
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public void ResetConsent()
        {
            PlayerPrefs.DeleteKey(PreferencesKey);
            Snapshot = CreateDefault();
            Changed?.Invoke(Snapshot);
        }

        private void Load()
        {
            Snapshot = CreateDefault();
            var json = PlayerPrefs.GetString(PreferencesKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(json, Snapshot);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[OnboardingFlow] Saved preferences could not be read: " + exception.Message);
                }
            }
        }

        private OnboardingSnapshot CreateDefault()
        {
            return new OnboardingSnapshot
            {
                coordinateLoggingEnabled = coordinateLoggingDefault,
                optionalModelDownloadAccepted = optionalModelDownloadDefault,
                condition = ConditionCheck.NotAnswered
            };
        }

        private void SaveAndNotify()
        {
            Snapshot.acceptedAtUtc = DateTime.UtcNow.ToString("o");
            PlayerPrefs.SetString(PreferencesKey, JsonUtility.ToJson(Snapshot));
            PlayerPrefs.Save();
            Changed?.Invoke(Snapshot);
        }
    }
}
