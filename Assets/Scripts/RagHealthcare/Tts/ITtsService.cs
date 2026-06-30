namespace Rag.Healthcare.Tts
{
    public interface ITtsService
    {
        bool IsSpeaking { get; }

        void Speak(string text);

        void Stop();
    }
}
