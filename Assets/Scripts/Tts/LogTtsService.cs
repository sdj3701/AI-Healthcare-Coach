using UnityEngine;

namespace AIHealthcareCoach.Tts
{
    public sealed class LogTtsService : ITtsService
    {
        public bool IsSpeaking => false;

        public void Speak(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Debug.Log($"[TTS] {text}");
        }

        public void Stop()
        {
        }
    }
}
