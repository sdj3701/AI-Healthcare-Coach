using UnityEngine;

namespace Rag.Healthcare.Tts
{
    public static class TtsBackendResolver
    {
        public static TtsBackend ResolveAuto(RuntimePlatform platform)
        {
            return platform switch
            {
                RuntimePlatform.WindowsEditor => TtsBackend.WindowsPowerShell,
                RuntimePlatform.WindowsPlayer => TtsBackend.WindowsPowerShell,
                RuntimePlatform.OSXEditor => TtsBackend.MacOsSay,
                RuntimePlatform.OSXPlayer => TtsBackend.MacOsSay,
                RuntimePlatform.Android => TtsBackend.AndroidNative,
                RuntimePlatform.IPhonePlayer => TtsBackend.IosNative,
                _ => TtsBackend.LogOnly
            };
        }
    }
}
