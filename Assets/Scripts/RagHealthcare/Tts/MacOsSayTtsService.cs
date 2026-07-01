using System;
using System.Diagnostics;
using System.Globalization;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Rag.Healthcare.Tts
{
    public sealed class MacOsSayTtsService : ITtsService, IDisposable
    {
        private readonly string voice;
        private readonly int wordsPerMinute;
        private Process currentProcess;

        public MacOsSayTtsService(string voice = "", int wordsPerMinute = 185)
        {
            this.voice = string.IsNullOrWhiteSpace(voice) ? string.Empty : voice.Trim();
            this.wordsPerMinute = Mathf.Clamp(wordsPerMinute, 80, 320);
        }

        public bool IsSpeaking => currentProcess != null && !currentProcess.HasExited;

        public void Speak(string text)
        {
            Stop();

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            try
            {
                var arguments = "-r " + wordsPerMinute.ToString(CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(voice))
                {
                    arguments += " -v " + QuoteArgument(voice);
                }

                arguments += " " + QuoteArgument(text);
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/say",
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                currentProcess = Process.Start(startInfo);
            }
            catch (Exception exception)
            {
                Debug.LogError($"macOS TTS failed: {exception.Message}");
            }
#else
            Debug.LogWarning("MacOsSayTtsService is only available in macOS Editor or macOS Standalone builds.");
#endif
        }

        public void Stop()
        {
            if (currentProcess == null)
            {
                return;
            }

            try
            {
                if (!currentProcess.HasExited)
                {
                    currentProcess.Kill();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not stop macOS TTS process: {exception.Message}");
            }
            finally
            {
                currentProcess.Dispose();
                currentProcess = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
