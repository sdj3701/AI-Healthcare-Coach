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

        public bool IsSpeaking => ttsService != null && ttsService.IsSpeaking;

        private void Awake()
        {
            ttsService = CreateTtsService();
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
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            ttsService ??= CreateTtsService();
            ttsService.Speak(message);
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
            return ResolveBackend() switch
            {
                TtsBackend.WindowsPowerShell => new WindowsPowerShellTtsService(windowsVoiceRate, windowsVoiceVolume),
                TtsBackend.MacOsSay => new MacOsSayTtsService(macOsVoice, macOsWordsPerMinute),
                _ => new LogTtsService()
            };
        }

        private TtsBackend ResolveBackend()
        {
            if (backend != TtsBackend.Auto)
            {
                return backend;
            }

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            return TtsBackend.MacOsSay;
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return TtsBackend.WindowsPowerShell;
#else
            return TtsBackend.LogOnly;
#endif
        }
    }
}
