using System;
namespace Rag.Healthcare.Tts
{
    public sealed class MacOsSayTtsService : ITtsService, IDisposable
    {
        private readonly AIHealthcareCoach.Tts.MacOsSayTtsService inner;

        public MacOsSayTtsService(string voice = "", int wordsPerMinute = 185)
        {
            inner = new AIHealthcareCoach.Tts.MacOsSayTtsService(voice, wordsPerMinute);
        }

        public bool IsSpeaking => inner.IsSpeaking;

        public bool TrySpeak(string text, out string errorMessage)
        {
            return inner.TrySpeak(text, out errorMessage);
        }

        public void Stop() => inner.Stop();

        public void Dispose() => inner.Dispose();
    }
}
