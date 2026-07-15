namespace Rag.Healthcare.Tts
{
    public interface ITtsService
    {
        bool IsSpeaking { get; }

        bool TrySpeak(string text, out string errorMessage);

        void Stop();
    }

    public enum TtsBackendEventType
    {
        Unknown = 0,
        Initialized = 1,
        Queued = 2,
        Started = 3,
        Finished = 4,
        Cancelled = 5,
        Dropped = 6,
        Failed = 7,
        Shutdown = 8,
        InterruptionBegan = 9,
        InterruptionEnded = 10,
        MediaServicesLost = 11,
        MediaServicesReset = 12,
        RouteChanged = 13,
        Paused = 14,
        Continued = 15
    }

    public readonly struct TtsBackendEvent
    {
        public TtsBackendEvent(
            int version,
            TtsBackendEventType type,
            int code,
            int droppedBefore,
            long sequence,
            long requestId,
            long generation)
        {
            Version = version;
            Type = type;
            Code = code;
            DroppedBefore = droppedBefore;
            Sequence = sequence;
            RequestId = requestId;
            Generation = generation;
        }

        public int Version { get; }
        public TtsBackendEventType Type { get; }
        public int Code { get; }
        public int DroppedBefore { get; }
        public long Sequence { get; }
        public long RequestId { get; }
        public long Generation { get; }

        public bool IsTerminal =>
            Type == TtsBackendEventType.Finished ||
            Type == TtsBackendEventType.Cancelled ||
            Type == TtsBackendEventType.Dropped ||
            Type == TtsBackendEventType.Failed ||
            Type == TtsBackendEventType.Shutdown;
    }

    /// <summary>
    /// Optional additive contract for native backends that expose request IDs and events.
    /// Other backends keep using <see cref="ITtsService"/> unchanged.
    /// </summary>
    public interface IQueuedTtsService : ITtsService
    {
        bool TryEnqueue(
            string text,
            TtsRequestPriority priority,
            int flags,
            out long requestId,
            out string errorMessage);

        bool TryPollEvent(out TtsBackendEvent backendEvent);
        bool AcknowledgeEvent(long sequence);
        bool TryGetEventMessage(long sequence, out string message);

        long NativeGeneration { get; }
        long DroppedEventCount { get; }
        int EventQueueCapacity { get; }
    }
}
