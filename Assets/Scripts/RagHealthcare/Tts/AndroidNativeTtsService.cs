using System;

namespace Rag.Healthcare.Tts
{
    public sealed class AndroidNativeTtsService : ITtsService, IDisposable
    {
        private readonly AIHealthcareCoach.Tts.AndroidNativeTtsService inner =
            new AIHealthcareCoach.Tts.AndroidNativeTtsService();

        public bool IsSpeaking => inner.IsSpeaking;
        public bool TrySpeak(string text, out string errorMessage) => inner.TrySpeak(text, out errorMessage);
        public void Stop() => inner.Stop();
        public void Dispose() => inner.Dispose();
    }
}
