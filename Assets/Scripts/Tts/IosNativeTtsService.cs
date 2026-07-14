using System;
using System.Runtime.InteropServices;

namespace AIHealthcareCoach.Tts
{
    public sealed class IosNativeTtsService : ITtsService, IDisposable
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void AhcTtsInitialize();

        [DllImport("__Internal")]
        private static extern int AhcTtsSpeak(string text);

        [DllImport("__Internal")]
        private static extern int AhcTtsIsSpeaking();

        [DllImport("__Internal")]
        private static extern void AhcTtsStop();

        [DllImport("__Internal")]
        private static extern void AhcTtsShutdown();
#endif

        public IosNativeTtsService()
        {
#if UNITY_IOS && !UNITY_EDITOR
            AhcTtsInitialize();
#endif
        }

        public bool IsSpeaking
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                return AhcTtsIsSpeaking() != 0;
#else
                return false;
#endif
            }
        }

        public bool TrySpeak(string text, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                errorMessage = "읽을 문장이 비어 있습니다.";
                return false;
            }

#if UNITY_IOS && !UNITY_EDITOR
            if (AhcTtsSpeak(text.Trim()) != 0)
            {
                return true;
            }

            errorMessage = "AVSpeechSynthesizer가 문장을 수락하지 않았습니다.";
            return false;
#else
            errorMessage = "iOS 네이티브 TTS는 iOS 실기기 빌드에서만 사용할 수 있습니다.";
            return false;
#endif
        }

        public void Stop()
        {
#if UNITY_IOS && !UNITY_EDITOR
            AhcTtsStop();
#endif
        }

        public void Dispose()
        {
#if UNITY_IOS && !UNITY_EDITOR
            AhcTtsShutdown();
#endif
        }
    }
}
