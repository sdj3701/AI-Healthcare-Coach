using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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

                LogProcessFailure(currentProcess);
                DisposeCurrentProcess();
                return false;
            }
        }

        public bool TrySpeak(string text, out string errorMessage)
        {
            errorMessage = string.Empty;
            Stop();

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            try
            {
                var command = BuildPowerShellCommand(text);

                var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                var startInfo = new ProcessStartInfo
                {
                    FileName = ResolvePowerShellPath(),
                    Arguments = "-NoProfile -NonInteractive -Sta -ExecutionPolicy Bypass -EncodedCommand " + encodedCommand,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                currentProcess = Process.Start(startInfo);
                if (currentProcess == null)
                {
                    errorMessage = "PowerShell TTS 프로세스를 시작하지 못했습니다.";
                    return false;
                }

                if (currentProcess.WaitForExit(100) && currentProcess.ExitCode != 0)
                {
                    errorMessage = GetProcessFailureDetails(currentProcess);
                    Debug.LogError($"Windows TTS process failed with exit code {currentProcess.ExitCode}: {errorMessage}");
                    DisposeCurrentProcess();
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                errorMessage = exception.Message;
                Debug.LogError($"Windows TTS failed: {exception.Message}");
                return false;
            }
#else
            errorMessage = "Windows PowerShell TTS는 Windows Editor/Standalone에서만 사용할 수 있습니다.";
            Debug.LogWarning(errorMessage);
            return false;
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

        private string BuildPowerShellCommand(string text)
        {
            var textBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
            var builder = new StringBuilder();

            builder.Append("$ErrorActionPreference='Stop';");
            builder.Append("$text=[System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('");
            builder.Append(textBase64);
            builder.Append("'));");
            builder.Append("Add-Type -AssemblyName System.Speech;");
            builder.Append("$synth=New-Object System.Speech.Synthesis.SpeechSynthesizer;");
            builder.Append("$synth.SetOutputToDefaultAudioDevice();");

            if (ContainsHangul(text))
            {
                builder.Append("$voice=$synth.GetInstalledVoices()|Where-Object{$_.Enabled -and $_.VoiceInfo.Culture.Name -eq 'ko-KR'}|Select-Object -First 1;");
                builder.Append("if($null -ne $voice){$synth.SelectVoice($voice.VoiceInfo.Name);}");
            }

            builder.Append("$synth.Rate=");
            builder.Append(rate.ToString(CultureInfo.InvariantCulture));
            builder.Append(";");
            builder.Append("$synth.Volume=");
            builder.Append(volume.ToString(CultureInfo.InvariantCulture));
            builder.Append(";");
            builder.Append("$synth.Speak($text);");
            builder.Append("$synth.Dispose();");

            return builder.ToString();
        }

        private static bool ContainsHangul(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                var value = text[i];
                if (value >= '\uAC00' && value <= '\uD7A3')
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolvePowerShellPath()
        {
            var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var powerShellPath = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            return File.Exists(powerShellPath) ? powerShellPath : "powershell.exe";
        }

        private static void LogProcessFailure(Process process)
        {
            if (process.ExitCode == 0)
            {
                return;
            }

            var details = GetProcessFailureDetails(process);
            Debug.LogError($"Windows TTS process failed with exit code {process.ExitCode}: {details}");
        }

        private static string GetProcessFailureDetails(Process process)
        {
            var standardError = ReadProcessStream(process.StandardError);
            var standardOutput = ReadProcessStream(process.StandardOutput);
            var details = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;

            if (string.IsNullOrWhiteSpace(details))
            {
                details = "PowerShell exited without diagnostic output.";
            }

            return details.Trim();
        }

        private static string ReadProcessStream(StreamReader reader)
        {
            try
            {
                return reader.ReadToEnd();
            }
            catch (Exception exception)
            {
                return $"Could not read process output: {exception.Message}";
            }
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
