using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AIHealthcareCoach.Tts
{
    public sealed class WindowsPowerShellTtsService : ITtsService, IDisposable
    {
        private readonly int rate;
        private readonly int volume;
        private Process currentProcess;

        public WindowsPowerShellTtsService(int rate, int volume)
        {
            this.rate = Clamp(rate, -10, 10);
            this.volume = Clamp(volume, 0, 100);
        }

        public bool IsSpeaking
        {
            get
            {
                if (currentProcess == null)
                {
                    return false;
                }

                if (!currentProcess.HasExited)
                {
                    return true;
                }

                DisposeCurrentProcess();
                return false;
            }
        }

        public void Speak(string text)
        {
            Stop();

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
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
            Debug.LogWarning("WindowsPowerShellTtsService is only available on Windows.");
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
                DisposeCurrentProcess();
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        private void DisposeCurrentProcess()
        {
            if (currentProcess == null)
            {
                return;
            }

            currentProcess.Dispose();
            currentProcess = null;
        }
    }
}
