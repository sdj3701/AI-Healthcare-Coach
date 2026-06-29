using UnityEngine;

namespace AIHealthcareCoach.Tts
{
    public sealed class LogTtsService : ITtsService
    {
        public bool IsSpeaking => false;

        public bool TrySpeak(string text, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            Debug.Log($"[TTS] {text}");
            return true;
        }

        public void Stop()
        {
        }
    }
}
