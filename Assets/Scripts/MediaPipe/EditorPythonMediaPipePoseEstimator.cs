using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AIHealthcareCoach.MediaPipe
{
    public sealed class EditorPythonMediaPipePoseEstimator : IPoseEstimator
    {
        private const int StartupTimeoutMs = 8000;
        private const int FrameTimeoutMs = 3000;

        private Process process;
        private Stream stdin;
        private StreamReader stdout;
        private readonly StringBuilder stderrBuffer = new StringBuilder(4096);
        private byte[] frameBytes;
        private bool isReady;

        public string BackendName
        {
            get { return "Editor Python MediaPipe"; }
        }

        public bool IsReady
        {
            get { return isReady && process != null && !process.HasExited; }
        }

        public string LastError { get; private set; }

        public bool Initialize(PoseEstimatorSettings settings)
        {
#if UNITY_EDITOR
            Dispose();

            var workerPath = ResolveWorkerPath(settings);
            if (!File.Exists(workerPath))
            {
                LastError = "Editor MediaPipe worker was not found: " + workerPath;
                return false;
            }

            try
            {
                stderrBuffer.Length = 0;
                var executable = ResolvePythonExecutable(settings.editorPythonExecutablePath);
                var arguments = BuildArguments(workerPath, settings);
                process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = executable,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    },
                    EnableRaisingEvents = true
                };

                process.ErrorDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                    {
                        if (stderrBuffer.Length < 4096)
                        {
                            stderrBuffer.AppendLine(args.Data);
                        }

                        Debug.LogWarning("[Editor Python MediaPipe] " + args.Data);
                    }
                };

                process.Start();
                process.BeginErrorReadLine();
                stdin = process.StandardInput.BaseStream;
                stdout = process.StandardOutput;

                var handshakeLine = ReadJsonLineWithTimeout(stdout, StartupTimeoutMs);
                if (string.IsNullOrWhiteSpace(handshakeLine))
                {
                    LastError = "Python MediaPipe worker did not report readiness. "
                                + "Install dependencies with: python3 -m pip install mediapipe numpy. "
                                + BuildProcessDiagnostic(executable);
                    Dispose();
                    return false;
                }

                var handshake = JsonUtility.FromJson<WorkerHandshake>(handshakeLine);
                if (handshake == null || !handshake.ready)
                {
                    LastError = handshake == null || string.IsNullOrWhiteSpace(handshake.errorMessage)
                        ? "Python MediaPipe worker failed to start. " + BuildProcessDiagnostic(executable)
                        : handshake.errorMessage;
                    Dispose();
                    return false;
                }

                isReady = true;
                LastError = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                LastError = "Failed to start Python MediaPipe worker: " + exception.Message;
                Dispose();
                return false;
            }
#else
            LastError = "Editor Python MediaPipe backend is only available in Unity Editor.";
            return false;
#endif
        }

        public bool TryProcessFrame(
            Color32[] rgbaPixels,
            int width,
            int height,
            long timestampMs,
            bool mirrored,
            int rotationAngle,
            out LandmarkFrame frame)
        {
            if (!IsReady)
            {
                frame = LandmarkFrame.Empty(timestampMs, "EDITOR_MEDIAPIPE_NOT_READY", LastError);
                return false;
            }

            if (rgbaPixels == null || rgbaPixels.Length == 0 || width <= 0 || height <= 0)
            {
                frame = LandmarkFrame.Empty(timestampMs, "INVALID_FRAME", "Frame pixels are empty.");
                return false;
            }

            try
            {
                EnsureFrameBytes(rgbaPixels, width, height);

                var header = new WorkerFrameHeader
                {
                    width = width,
                    height = height,
                    timestampMs = timestampMs,
                    mirrored = mirrored,
                    rotationAngle = rotationAngle
                };

                var headerBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(header) + "\n");
                stdin.Write(headerBytes, 0, headerBytes.Length);
                stdin.Write(frameBytes, 0, width * height * 4);
                stdin.Flush();

                var responseLine = ReadJsonLineWithTimeout(stdout, FrameTimeoutMs);
                if (string.IsNullOrWhiteSpace(responseLine))
                {
                    LastError = "Python MediaPipe worker did not return a frame result. " + BuildProcessDiagnostic(string.Empty);
                    frame = LandmarkFrame.Empty(timestampMs, "EDITOR_MEDIAPIPE_TIMEOUT", LastError);
                    Dispose();
                    return false;
                }

                frame = JsonUtility.FromJson<LandmarkFrame>(responseLine);
                if (frame == null)
                {
                    LastError = "Python MediaPipe worker returned invalid JSON.";
                    frame = LandmarkFrame.Empty(timestampMs, "EDITOR_MEDIAPIPE_INVALID_JSON", LastError);
                    return false;
                }

                if (!string.IsNullOrEmpty(frame.errorCode))
                {
                    LastError = frame.errorMessage;
                    return false;
                }

                LastError = string.Empty;
                return frame.HasPose;
            }
            catch (Exception exception)
            {
                LastError = "Python MediaPipe frame processing failed: " + exception.Message;
                frame = LandmarkFrame.Empty(timestampMs, "EDITOR_MEDIAPIPE_PROCESS_FAILED", LastError);
                Dispose();
                return false;
            }
        }

        public void Dispose()
        {
            isReady = false;

            try
            {
                if (process != null && !process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not stop Python MediaPipe worker: " + exception.Message);
            }

            stdin = null;
            stdout = null;

            if (process != null)
            {
                process.Dispose();
                process = null;
            }
        }

        private static string ResolveWorkerPath(PoseEstimatorSettings settings)
        {
            var relativePath = string.IsNullOrWhiteSpace(settings.editorPythonWorkerRelativePath)
                ? "MediaPipe/editor_pose_worker.py"
                : settings.editorPythonWorkerRelativePath.Trim();
            return Path.Combine(Application.streamingAssetsPath, relativePath);
        }

        private static string ResolvePythonExecutable(string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                return configuredPath.Trim();
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (!string.IsNullOrEmpty(projectRoot))
            {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
                var localVenvPython = Path.Combine(projectRoot, ".venv-mediapipe", "bin", "python");
                if (File.Exists(localVenvPython))
                {
                    return localVenvPython;
                }

                localVenvPython = Path.Combine(projectRoot, ".venv", "bin", "python");
                if (File.Exists(localVenvPython))
                {
                    return localVenvPython;
                }
#elif UNITY_EDITOR_WIN
                var localVenvPython = Path.Combine(projectRoot, ".venv-mediapipe", "Scripts", "python.exe");
                if (File.Exists(localVenvPython))
                {
                    return localVenvPython;
                }

                localVenvPython = Path.Combine(projectRoot, ".venv", "Scripts", "python.exe");
                if (File.Exists(localVenvPython))
                {
                    return localVenvPython;
                }
#endif
            }

#if UNITY_EDITOR_OSX
            if (File.Exists("/opt/homebrew/bin/python3"))
            {
                return "/opt/homebrew/bin/python3";
            }

            if (File.Exists("/usr/local/bin/python3"))
            {
                return "/usr/local/bin/python3";
            }

            if (File.Exists("/usr/bin/python3"))
            {
                return "/usr/bin/python3";
            }

            return "python3";
#elif UNITY_EDITOR_WIN
            return "python";
#else
            return "python3";
#endif
        }

        private static string BuildArguments(string workerPath, PoseEstimatorSettings settings)
        {
            var builder = new StringBuilder();
            builder.Append(QuoteArgument(workerPath));
            builder.Append(" --min_detection_confidence ");
            builder.Append(settings.minPoseDetectionConfidence.ToString(CultureInfo.InvariantCulture));
            builder.Append(" --min_tracking_confidence ");
            builder.Append(settings.minTrackingConfidence.ToString(CultureInfo.InvariantCulture));
            builder.Append(" --min_presence_confidence ");
            builder.Append(settings.minPosePresenceConfidence.ToString(CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private void EnsureFrameBytes(Color32[] rgbaPixels, int width, int height)
        {
            var requiredLength = width * height * 4;
            if (frameBytes == null || frameBytes.Length != requiredLength)
            {
                frameBytes = new byte[requiredLength];
            }

            var pixelCount = Mathf.Min(rgbaPixels.Length, width * height);
            for (var i = 0; i < pixelCount; i++)
            {
                var pixel = rgbaPixels[i];
                var offset = i * 4;
                frameBytes[offset] = pixel.r;
                frameBytes[offset + 1] = pixel.g;
                frameBytes[offset + 2] = pixel.b;
                frameBytes[offset + 3] = pixel.a;
            }
        }

        private static string ReadJsonLineWithTimeout(StreamReader reader, int timeoutMs)
        {
            var startedAt = Stopwatch.StartNew();

            while (startedAt.ElapsedMilliseconds < timeoutMs)
            {
                var remainingMs = Mathf.Max(1, timeoutMs - (int)startedAt.ElapsedMilliseconds);
                var readTask = Task.Run(() => reader.ReadLine());
                if (!readTask.Wait(remainingMs))
                {
                    return string.Empty;
                }

                var line = readTask.Result;
                if (line == null)
                {
                    return string.Empty;
                }

                if (line.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    return line;
                }

                Debug.LogWarning("[Editor Python MediaPipe stdout] " + line);
            }

            return string.Empty;
        }

        private string BuildProcessDiagnostic(string executable)
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(executable))
            {
                builder.Append("Python executable: ").Append(executable).Append(". ");
            }

            if (process != null && process.HasExited)
            {
                builder.Append("Exit code: ").Append(process.ExitCode).Append(". ");
            }

            if (stderrBuffer.Length > 0)
            {
                builder.Append("stderr: ").Append(stderrBuffer.ToString().Trim());
            }

            return builder.ToString();
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        [Serializable]
        private sealed class WorkerHandshake
        {
            public bool ready = false;
            public string errorCode = string.Empty;
            public string errorMessage = string.Empty;
        }

        [Serializable]
        private sealed class WorkerFrameHeader
        {
            public int width;
            public int height;
            public long timestampMs;
            public bool mirrored;
            public int rotationAngle;
        }
    }
}
