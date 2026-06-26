namespace AIHealthcareCoach.Tts
{
    public interface ITtsService
    {
        bool IsSpeaking { get; }

        void Speak(string text);

        void Stop();
    }
}
