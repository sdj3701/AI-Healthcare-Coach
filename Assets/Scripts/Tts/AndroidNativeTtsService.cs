using System;
using UnityEngine;

namespace AIHealthcareCoach.Tts
{
    public sealed class AndroidNativeTtsService : ITtsService, IDisposable
    {
        private const string BridgeClassName = "com.aihealthcarecoach.tts.AhcTtsBridge";

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaClass bridge;
#endif

        public AndroidNativeTtsService()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            bridge = new AndroidJavaClass(BridgeClassName);
            bridge.CallStatic("initialize");
#endif
        }

        public bool IsSpeaking
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return bridge != null && bridge.CallStatic<bool>("isSpeaking");
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

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (bridge != null && bridge.CallStatic<bool>("speak", text.Trim()))
                {
                    return true;
                }

                errorMessage = bridge == null ? "Android TTS bridge를 초기화하지 못했습니다." : bridge.CallStatic<string>("getLastError");
                return false;
            }
            catch (Exception exception)
            {
                errorMessage = exception.Message;
                Debug.LogError($"Android TTS failed: {exception.Message}");
                return false;
            }
#else
            errorMessage = "Android 네이티브 TTS는 Android 실기기 빌드에서만 사용할 수 있습니다.";
            return false;
#endif
        }

        public void Stop()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            bridge?.CallStatic("stop");
#endif
        }

        public void Dispose()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (bridge != null)
            {
                bridge.CallStatic("shutdown");
                bridge.Dispose();
                bridge = null;
            }
#endif
        }
    }
}
