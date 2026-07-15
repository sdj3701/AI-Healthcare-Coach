using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Rag.Healthcare.Tts
{
    public sealed class IosNativeTtsService : IQueuedTtsService, IDisposable
    {
        private readonly AIHealthcareCoach.Tts.IosNativeTtsService inner =
            new AIHealthcareCoach.Tts.IosNativeTtsService();

#if UNITY_IOS && !UNITY_EDITOR
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct AhcTtsEventV1
        {
            public int version;
            public int type;
            public int code;
            public int droppedBefore;
            public long sequence;
            public long requestId;
            public long generation;
        }

        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        private static extern long AhcTtsEnqueue(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
            int priority,
            int flags);

        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        private static extern int AhcTtsPollEvent(out AhcTtsEventV1 backendEvent);

        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        private static extern int AhcTtsAcknowledgeEvent(long sequence);

        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        private static extern int AhcTtsGetEventMessage(
            long sequence,
            StringBuilder buffer,
            int capacity);

        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        private static extern long AhcTtsGetGeneration();

        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        private static extern long AhcTtsGetDroppedEventCount();

        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        private static extern int AhcTtsGetEventQueueCapacity();

        // Older exported Xcode projects do not contain the additive request/event ABI.
        // Cache that discovery so an EntryPointNotFoundException is never thrown every frame.
        private bool nativeQueueAbiUnavailable;
#endif

        public bool IsSpeaking => inner.IsSpeaking;
        public bool TrySpeak(string text, out string errorMessage) => inner.TrySpeak(text, out errorMessage);
        public void Stop() => inner.Stop();
        public void Dispose() => inner.Dispose();

        public long NativeGeneration
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                if (nativeQueueAbiUnavailable)
                {
                    return 0;
                }

                try
                {
                    return AhcTtsGetGeneration();
                }
                catch (EntryPointNotFoundException)
                {
                    nativeQueueAbiUnavailable = true;
                    return 0;
                }
#else
                return 0;
#endif
            }
        }

        public long DroppedEventCount
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                if (nativeQueueAbiUnavailable)
                {
                    return 0;
                }

                try
                {
                    return AhcTtsGetDroppedEventCount();
                }
                catch (EntryPointNotFoundException)
                {
                    nativeQueueAbiUnavailable = true;
                    return 0;
                }
#else
                return 0;
#endif
            }
        }

        public int EventQueueCapacity
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                if (nativeQueueAbiUnavailable)
                {
                    return 0;
                }

                try
                {
                    return AhcTtsGetEventQueueCapacity();
                }
                catch (EntryPointNotFoundException)
                {
                    nativeQueueAbiUnavailable = true;
                    return 0;
                }
#else
                return 0;
#endif
            }
        }

        public bool TryEnqueue(
            string text,
            TtsRequestPriority priority,
            int flags,
            out long requestId,
            out string errorMessage)
        {
            requestId = 0;
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                errorMessage = "TTS로 읽을 문장이 비어 있습니다.";
                return false;
            }

#if UNITY_IOS && !UNITY_EDITOR
            if (nativeQueueAbiUnavailable)
            {
                return inner.TrySpeak(text, out errorMessage);
            }

            try
            {
                requestId = AhcTtsEnqueue(text.Trim(), (int)priority, flags);
                if (requestId > 0)
                {
                    return true;
                }

                errorMessage = "iOS TTS queue가 요청을 수락하지 않았습니다.";
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                // Additive ABI fallback for an older exported Xcode project.
                // requestId == 0 tells the controller to use legacy IsSpeaking polling.
                nativeQueueAbiUnavailable = true;
                return inner.TrySpeak(text, out errorMessage);
            }
#else
            return inner.TrySpeak(text, out errorMessage);
#endif
        }

        public bool TryPollEvent(out TtsBackendEvent backendEvent)
        {
            backendEvent = default;
#if UNITY_IOS && !UNITY_EDITOR
            if (nativeQueueAbiUnavailable)
            {
                return false;
            }

            try
            {
                if (AhcTtsPollEvent(out var nativeEvent) <= 0)
                {
                    return false;
                }

                backendEvent = new TtsBackendEvent(
                    nativeEvent.version,
                    (TtsBackendEventType)nativeEvent.type,
                    nativeEvent.code,
                    nativeEvent.droppedBefore,
                    nativeEvent.sequence,
                    nativeEvent.requestId,
                    nativeEvent.generation);
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                nativeQueueAbiUnavailable = true;
                return false;
            }
#else
            return false;
#endif
        }

        public bool AcknowledgeEvent(long sequence)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (nativeQueueAbiUnavailable)
            {
                return false;
            }

            try
            {
                return AhcTtsAcknowledgeEvent(sequence) > 0;
            }
            catch (EntryPointNotFoundException)
            {
                nativeQueueAbiUnavailable = true;
                return false;
            }
#else
            return false;
#endif
        }

        public bool TryGetEventMessage(long sequence, out string message)
        {
            message = string.Empty;
#if UNITY_IOS && !UNITY_EDITOR
            if (nativeQueueAbiUnavailable)
            {
                return false;
            }

            try
            {
                const int initialCapacity = 256;
                var buffer = new StringBuilder(initialCapacity);
                var requiredBytes = AhcTtsGetEventMessage(sequence, buffer, buffer.Capacity);
                if (requiredBytes <= 0)
                {
                    return false;
                }

                if (requiredBytes > buffer.Capacity)
                {
                    buffer = new StringBuilder(requiredBytes);
                    if (AhcTtsGetEventMessage(sequence, buffer, buffer.Capacity) <= 0)
                    {
                        return false;
                    }
                }

                message = buffer.ToString();
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                nativeQueueAbiUnavailable = true;
                return false;
            }
#else
            return false;
#endif
        }
    }
}
