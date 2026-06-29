namespace AIHealthcareCoach.Tts
{
    public interface ITtsService
    {
        bool IsSpeaking { get; }

        bool TrySpeak(string text, out string errorMessage);

        void Stop();
    }
}
