using System;
using Rag.Healthcare.Pose;
using UnityEngine;

namespace Rag.Healthcare.Tts
{
    public sealed class CoachTtsController : MonoBehaviour
    {
        [SerializeField] private TtsBackend backend = TtsBackend.Auto;
        [SerializeField] private bool speakOnStart = true;
        [SerializeField] private string startupMessage = "코치 시스템이 준비되었습니다.";
        [SerializeField, Range(-10, 10)] private int windowsVoiceRate;
        [SerializeField, Range(0, 100)] private int windowsVoiceVolume = 100;
        [SerializeField] private string macOsVoice = string.Empty;
        [SerializeField, Range(80, 320)] private int macOsWordsPerMinute = 185;

        private ITtsService ttsService;

        public event Action<string> PlaybackFailed;

        public bool IsSpeaking => ttsService != null && ttsService.IsSpeaking;
        public TtsBackend ActiveBackend { get; private set; } = TtsBackend.LogOnly;
        public string LastStatusMessage { get; private set; } = string.Empty;

        private void Awake()
        {
            ttsService = CreateTtsService();
            Debug.Log($"[TTS] Active backend: {ActiveBackend}");
        }

        private void Start()
        {
            if (speakOnStart)
            {
                Speak(startupMessage);
            }
        }

        private void OnDestroy()
        {
            if (ttsService is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        public void Speak(string message)
        {
            if (!TrySpeak(message, out var statusMessage) && !string.IsNullOrWhiteSpace(statusMessage))
            {
                Debug.LogWarning(statusMessage);
            }
        }

        public bool TrySpeak(string message, out string statusMessage)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                statusMessage = "TTS로 읽을 문장이 비어 있습니다.";
                LastStatusMessage = statusMessage;
                return false;
            }

            var trimmedMessage = message.Trim();
            ttsService ??= CreateTtsService();
            if (!ttsService.TrySpeak(trimmedMessage, out var errorMessage))
            {
                statusMessage = string.IsNullOrWhiteSpace(errorMessage)
                    ? $"{ActiveBackend} TTS 재생을 시작하지 못했습니다."
                    : $"{ActiveBackend} TTS 재생을 시작하지 못했습니다: {errorMessage}";
                LastStatusMessage = statusMessage;
                PlaybackFailed?.Invoke(statusMessage);
                return false;
            }

            statusMessage = $"{ActiveBackend} TTS 재생 중";
            LastStatusMessage = statusMessage;
            return true;
        }

        public void SpeakPoseFeedback(PoseFeedbackMessage feedback)
        {
            if (feedback == null)
            {
                return;
            }

            Speak(feedback.text);
        }

        public void Stop()
        {
            ttsService?.Stop();
        }

        [ContextMenu("Test Korean Coaching")]
        private void TestKoreanCoaching()
        {
            Speak("무릎을 발끝 방향으로 맞춰 주세요.");
        }

        private ITtsService CreateTtsService()
        {
            ActiveBackend = ResolveBackend();
            return ActiveBackend switch
            {
                TtsBackend.WindowsPowerShell => new WindowsPowerShellTtsService(windowsVoiceRate, windowsVoiceVolume),
                TtsBackend.MacOsSay => new MacOsSayTtsService(macOsVoice, macOsWordsPerMinute),
                TtsBackend.AndroidNative => new AndroidNativeTtsService(),
                TtsBackend.IosNative => new IosNativeTtsService(),
                _ => new LogTtsService()
            };
        }

        private TtsBackend ResolveBackend()
        {
            if (backend != TtsBackend.Auto)
            {
                return backend;
            }

            return TtsBackendResolver.ResolveAuto(Application.platform);
        }
    }
}
