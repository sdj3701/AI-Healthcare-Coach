using System;
using Rag.Healthcare.Pose;
using UnityEngine;

namespace Rag.Healthcare.Tts
{
    public sealed class CoachTtsController : MonoBehaviour
    {
        [SerializeField] private TtsBackend backend = TtsBackend.WindowsPowerShell;
        [SerializeField] private bool speakOnStart = true;
        [SerializeField] private string startupMessage = "코칭 시스템이 준비되었습니다.";
        [SerializeField, Range(-10, 10)] private int windowsVoiceRate;
        [SerializeField, Range(0, 100)] private int windowsVoiceVolume = 100;

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
            Speak("무릎이 안쪽으로 모이고 있어요. 무릎을 발끝 방향으로 맞춰주세요.");
        }

        private ITtsService CreateTtsService()
        {
            return backend switch
            {
                TtsBackend.WindowsPowerShell => new WindowsPowerShellTtsService(windowsVoiceRate, windowsVoiceVolume),
                _ => new LogTtsService()
            };
        }
    }
}
