using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AIHealthcareCoach.Tts
{
    public sealed class MacOsSayTtsService : ITtsService, IDisposable
    {
        private const int MinRate = 80;
        private const int MaxRate = 320;

        private static bool didLoadKoreanVoice;
        private static string cachedKoreanVoiceName;

        private readonly string preferredVoiceName;
        private readonly int rate;
        private Process currentProcess;

        public MacOsSayTtsService(string preferredVoiceName, int rate)
        {
            this.preferredVoiceName = string.IsNullOrWhiteSpace(preferredVoiceName)
                ? string.Empty
                : preferredVoiceName.Trim();
            this.rate = Clamp(rate, MinRate, MaxRate);
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

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            try
            {
                var sayPath = ResolveSayPath();
                if (!File.Exists(sayPath))
                {
                    errorMessage = $"{sayPath} 파일을 찾을 수 없습니다.";
                    return false;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = sayPath,
                    Arguments = BuildArguments(text),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                currentProcess = Process.Start(startInfo);
                if (currentProcess == null)
                {
                    errorMessage = "macOS say TTS 프로세스를 시작하지 못했습니다.";
                    return false;
                }

                if (currentProcess.WaitForExit(100) && currentProcess.ExitCode != 0)
                {
                    errorMessage = GetProcessFailureDetails(currentProcess);
                    Debug.LogError($"macOS say TTS process failed with exit code {currentProcess.ExitCode}: {errorMessage}");
                    DisposeCurrentProcess();
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                errorMessage = exception.Message;
                Debug.LogError($"macOS say TTS failed: {exception.Message}");
                return false;
            }
#else
            errorMessage = "macOS say TTS는 macOS Editor/Standalone에서만 사용할 수 있습니다.";
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
                Debug.LogWarning($"Could not stop macOS TTS process: {exception.Message}");
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

        private string BuildArguments(string text)
        {
            var builder = new StringBuilder();
            var voiceName = ResolveVoiceName(text);

            if (!string.IsNullOrWhiteSpace(voiceName))
            {
                builder.Append("-v ");
                builder.Append(QuoteArgument(voiceName));
                builder.Append(' ');
            }

            builder.Append("-r ");
            builder.Append(rate.ToString(CultureInfo.InvariantCulture));
            builder.Append(" -- ");
            builder.Append(QuoteArgument(text));

            return builder.ToString();
        }

        private string ResolveVoiceName(string text)
        {
            if (!string.IsNullOrWhiteSpace(preferredVoiceName))
            {
                return preferredVoiceName;
            }

            return ContainsHangul(text) ? GetKoreanVoiceName() : string.Empty;
        }

        private static string GetKoreanVoiceName()
        {
            if (didLoadKoreanVoice)
            {
                return cachedKoreanVoiceName;
            }

            didLoadKoreanVoice = true;
            cachedKoreanVoiceName = FindVoiceForLocale("ko");
            return cachedKoreanVoiceName;
        }

        private static string FindVoiceForLocale(string languageCode)
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = ResolveSayPath(),
                    Arguments = "-v ?",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return string.Empty;
                }

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(1000);

                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var match = Regex.Match(line, @"^(?<name>.+?)\s+(?<locale>[a-z]{2}[_-][A-Z]{2})\b");
                    if (!match.Success)
                    {
                        continue;
                    }

                    var locale = match.Groups["locale"].Value;
                    if (locale.StartsWith(languageCode, StringComparison.OrdinalIgnoreCase))
                    {
                        return match.Groups["name"].Value.Trim();
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not inspect macOS say voices: {exception.Message}");
            }
#endif

            return string.Empty;
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

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string ResolveSayPath()
        {
            return "/usr/bin/say";
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        private static void LogProcessFailure(Process process)
        {
            if (process.ExitCode == 0)
            {
                return;
            }

            var details = GetProcessFailureDetails(process);
            Debug.LogError($"macOS say TTS process failed with exit code {process.ExitCode}: {details}");
        }

        private static string GetProcessFailureDetails(Process process)
        {
            var standardError = ReadProcessStream(process.StandardError);
            var standardOutput = ReadProcessStream(process.StandardOutput);
            var details = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;

            if (string.IsNullOrWhiteSpace(details))
            {
                details = "say exited without diagnostic output.";
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
