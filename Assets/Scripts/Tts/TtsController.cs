using System;
using UnityEngine;

namespace AIHealthcareCoach.Tts
{
    public sealed class TtsController : MonoBehaviour
    {
        [SerializeField] private TtsBackend backend = TtsBackend.WindowsPowerShell;
        [SerializeField, Range(-10, 10)] private int windowsVoiceRate;
        [SerializeField, Range(0, 100)] private int windowsVoiceVolume = 100;
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

            duckingController.BeginDucking();
            ttsService.Speak(trimmedText);

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

            ttsService = backend switch
            {
                TtsBackend.WindowsPowerShell => new WindowsPowerShellTtsService(windowsVoiceRate, windowsVoiceVolume),
                _ => new LogTtsService()
            };
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
