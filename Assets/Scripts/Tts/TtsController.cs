using System;
using UnityEngine;

namespace AIHealthcareCoach.Tts
{
    public sealed class TtsController : MonoBehaviour
    {
        [SerializeField] private TtsBackend backend = TtsBackend.Auto;
        [SerializeField, Range(-10, 10)] private int windowsVoiceRate;
        [SerializeField, Range(0, 100)] private int windowsVoiceVolume = 100;
        [SerializeField] private string macOsVoiceName;
        [SerializeField, Range(80, 320)] private int macOsVoiceRate = 185;
        [SerializeField] private TtsAudioDuckingController duckingController;

        private ITtsService ttsService;
        private bool wasSpeaking;

        public bool IsSpeaking
        {
            get { return ttsService != null && ttsService.IsSpeaking; }
        }

        public TtsAudioDuckingController DuckingController
        {
            get
            {
                EnsureDuckingController();
                return duckingController;
            }
        }

        private void Awake()
        {
            EnsureService();
            EnsureDuckingController();
        }

        private void Update()
        {
            var speaking = IsSpeaking;
            if (wasSpeaking && !speaking)
            {
                duckingController.EndDucking();
            }

            wasSpeaking = speaking;
        }

        private void OnDestroy()
        {
            if (duckingController != null)
            {
                duckingController.ForceRestore();
            }

            if (ttsService is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        public bool TrySpeak(string text, out string statusMessage)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                statusMessage = "읽을 문장을 입력하세요.";
                return false;
            }

            var trimmedText = text.Trim();
            EnsureService();
            EnsureDuckingController();

            if (!ttsService.TrySpeak(trimmedText, out var errorMessage))
            {
                duckingController.EndDucking();
                wasSpeaking = false;
                statusMessage = string.IsNullOrWhiteSpace(errorMessage)
                    ? "TTS 재생을 시작하지 못했습니다."
                    : $"TTS 재생을 시작하지 못했습니다: {errorMessage}";
                return false;
            }

            duckingController.BeginDucking();
            wasSpeaking = IsSpeaking;
            if (!wasSpeaking)
            {
                duckingController.EndDucking();
            }

            statusMessage = $"재생 중: {trimmedText}";
            return true;
        }

        public void StopSpeaking(out string statusMessage)
        {
            EnsureService();
            ttsService.Stop();
            EnsureDuckingController();
            duckingController.EndDucking();
            wasSpeaking = false;
            statusMessage = "재생을 중지했습니다.";
        }

        private void EnsureService()
        {
            if (ttsService != null)
            {
                return;
            }

            ttsService = CreateService(backend);
        }

        private ITtsService CreateService(TtsBackend requestedBackend)
        {
            return requestedBackend switch
            {
                TtsBackend.Auto => CreatePlatformDefaultService(),
                TtsBackend.WindowsPowerShell => new WindowsPowerShellTtsService(windowsVoiceRate, windowsVoiceVolume),
                TtsBackend.MacOsSay => new MacOsSayTtsService(macOsVoiceName, macOsVoiceRate),
                _ => new LogTtsService()
            };
        }

        private ITtsService CreatePlatformDefaultService()
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            return new MacOsSayTtsService(macOsVoiceName, macOsVoiceRate);
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return new WindowsPowerShellTtsService(windowsVoiceRate, windowsVoiceVolume);
#else
            return new LogTtsService();
#endif
        }

        private void EnsureDuckingController()
        {
            if (duckingController != null)
            {
                return;
            }

            duckingController = GetComponent<TtsAudioDuckingController>();
            if (duckingController == null)
            {
                duckingController = gameObject.AddComponent<TtsAudioDuckingController>();
            }
        }
    }
}
