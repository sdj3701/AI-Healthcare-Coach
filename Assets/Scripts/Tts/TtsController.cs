using System;
using UnityEngine;

namespace AIHealthcareCoach.Tts
{
    public sealed class TtsController : MonoBehaviour
    {
        [SerializeField] private TtsBackend backend = TtsBackend.WindowsPowerShell;
        [SerializeField, Range(-10, 10)] private int windowsVoiceRate;
        [SerializeField, Range(0, 100)] private int windowsVoiceVolume = 100;

        private ITtsService ttsService;

        public bool IsSpeaking => ttsService != null && ttsService.IsSpeaking;

        private void Awake()
        {
            EnsureService();
        }

        private void OnDestroy()
        {
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
            ttsService.Speak(trimmedText);
            statusMessage = $"재생 중: {trimmedText}";
            return true;
        }

        public void StopSpeaking(out string statusMessage)
        {
            EnsureService();
            ttsService.Stop();
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
    }
}
