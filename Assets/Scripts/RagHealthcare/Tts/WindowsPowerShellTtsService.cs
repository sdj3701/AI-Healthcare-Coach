using System;
namespace Rag.Healthcare.Tts
{
    public sealed class WindowsPowerShellTtsService : ITtsService, IDisposable
    {
        private readonly AIHealthcareCoach.Tts.WindowsPowerShellTtsService inner;

        public WindowsPowerShellTtsService(int rate = 0, int volume = 100)
        {
            inner = new AIHealthcareCoach.Tts.WindowsPowerShellTtsService(rate, volume);
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
