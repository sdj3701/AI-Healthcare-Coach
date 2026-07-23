using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Scripting;

namespace Rag.Healthcare.Diagnostics
{
    /// <summary>
    /// Mirrors important messages to iOS NSLog so Xcode Console always receives them,
    /// including Release builds where IL2CPP strips Debug.Log calls.
    /// </summary>
    [Preserve]
    public static class IOSDeviceConsoleLog
    {
        private static bool forwarderInstalled;

        [ThreadStatic]
        private static bool suppressForwarder;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void AHCBootLogNative(string message);
#endif

        public static void Write(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            EnsureForwarderInstalled();

#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                AHCBootLogNative(message);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[IOSDeviceConsoleLog] Native log failed: " + exception.Message);
            }
#endif

            suppressForwarder = true;
            Debug.Log(message);
            suppressForwarder = false;
        }

        private static void EnsureForwarderInstalled()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (forwarderInstalled)
            {
                return;
            }

            forwarderInstalled = true;
            Application.logMessageReceivedThreaded += ForwardUnityLogToNative;
            AHCBootLogNative("IOSDeviceConsoleLog forwarder installed (development=" + Debug.isDebugBuild + ")");
#endif
        }

        private static void ForwardUnityLogToNative(string condition, string stackTrace, LogType type)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (suppressForwarder)
            {
                return;
            }

            var prefix = type switch
            {
                LogType.Error => "[UnityError]",
                LogType.Exception => "[UnityException]",
                LogType.Warning => "[UnityWarning]",
                LogType.Assert => "[UnityAssert]",
                _ => "[UnityLog]"
            };

            try
            {
                AHCBootLogNative(prefix + " " + condition);
                if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) &&
                    !string.IsNullOrEmpty(stackTrace))
                {
                    AHCBootLogNative(stackTrace);
                }
            }
            catch (Exception)
            {
                // Avoid recursive logging failures during startup.
            }
#endif
        }
    }
}
