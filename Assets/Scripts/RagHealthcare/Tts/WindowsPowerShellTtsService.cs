using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Rag.Healthcare.Tts
{
    public sealed class WindowsPowerShellTtsService : ITtsService, IDisposable
    {
        private readonly int rate;
        private readonly int volume;
        private Process currentProcess;

        public WindowsPowerShellTtsService(int rate = 0, int volume = 100)
        {
            this.rate = Math.Max(-10, Math.Min(10, rate));
            this.volume = Math.Max(0, Math.Min(100, volume));
        }

        public bool IsSpeaking => currentProcess != null && !currentProcess.HasExited;

        public void Speak(string text)
        {
            Stop();

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                var textBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
                var command =
                    "$text=[System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('" + textBase64 + "'));" +
                    "Add-Type -AssemblyName System.Speech;" +
                    "$synth=New-Object System.Speech.Synthesis.SpeechSynthesizer;" +
                    "$synth.Rate=" + rate.ToString(CultureInfo.InvariantCulture) + ";" +
                    "$synth.Volume=" + volume.ToString(CultureInfo.InvariantCulture) + ";" +
                    "$synth.Speak($text);";

                var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encodedCommand,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                currentProcess = Process.Start(startInfo);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Windows TTS failed: {exception.Message}");
            }
#else
            Debug.LogWarning("WindowsPowerShellTtsService is only available in Windows Editor or Windows Standalone builds.");
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
                Debug.LogWarning($"Could not stop TTS process: {exception.Message}");
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
    }
}
