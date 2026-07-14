using System;

namespace Rag.Healthcare.Tts
{
    public sealed class IosNativeTtsService : ITtsService, IDisposable
    {
        private readonly AIHealthcareCoach.Tts.IosNativeTtsService inner =
            new AIHealthcareCoach.Tts.IosNativeTtsService();

        public bool IsSpeaking => inner.IsSpeaking;
        public bool TrySpeak(string text, out string errorMessage) => inner.TrySpeak(text, out errorMessage);
        public void Stop() => inner.Stop();
        public void Dispose() => inner.Dispose();
    }
}
