using System;
using System.Collections.Generic;
using System.IO;
using Rag.Healthcare.Pose;
using UnityEngine;

namespace Rag.Healthcare.Performance
{
    [Serializable]
    public sealed class PerformanceBenchmarkResult
    {
        public string startedAtUtc;
        public string endedAtUtc;
        public float durationSeconds;
        public float averageFrameFps;
        public float averagePoseFps;
        public float averageInferenceMs;
        public int droppedFrames;
        public long managedMemoryPeakBytes;
        public float batteryStart;
        public float batteryEnd;
        public float batteryDrop;
        public bool lowMemorySignalReceived;
    }

    [Serializable]
    public sealed class PerformanceAcceptanceReport
    {
        public bool applicable;
        public bool passed;
        public string[] reasons;
    }

    [Serializable]
    public sealed class PerformanceBenchReport
    {
        public int schemaVersion;
        public string pbi;
        public string benchKind;
        public string deviceModel;
        public string operatingSystem;
        public string unityVersion;
        public PerformanceBenchmarkResult result;
        public PerformanceAcceptanceReport acceptance;
        public string savedAtUtc;
        public string savedPath;
    }

    [Serializable]
    public sealed class PerformanceAcceptance
    {
        public bool passed;
        public string[] failures;
    }

    public static class PerformanceAcceptanceEvaluator
    {
        public static PerformanceAcceptance Evaluate(PerformanceBenchmarkResult result)
        {
            var failures = new List<string>();
            if (result == null) failures.Add("Benchmark result is missing.");
            else
            {
                if (result.durationSeconds < 590f) failures.Add("10-minute session was not completed.");
                if (result.averagePoseFps < 10f) failures.Add("Average pose FPS is below 10.");
                if (result.averageInferenceMs > 100f) failures.Add("Average inference latency exceeds 100 ms.");
                if (result.droppedFrames > 0 && result.averagePoseFps > 0f && result.droppedFrames / result.durationSeconds > 1f) failures.Add("Frame drops exceed one per second.");
                if (result.lowMemorySignalReceived) failures.Add("A low-memory signal occurred.");
            }
            return new PerformanceAcceptance { passed = failures.Count == 0, failures = failures.ToArray() };
        }
    }

    public sealed class DevicePerformanceProfiler : MonoBehaviour
    {
        public const string BenchKind60s = "60s";
        public const string BenchKind10m = "10m";
        public const float Duration60sSeconds = 60f;
        public const float Duration10mSeconds = 600f;

        [SerializeField] private JointTrackingController trackingController;
        [SerializeField, Min(10f)] private float benchmarkSeconds = Duration10mSeconds;

        private float startedAt;
        private float frameFpsTotal;
        private float poseFpsTotal;
        private float inferenceTotal;
        private float lastPoseFps;
        private float lastInferenceMs;
        private int samples;
        private long memoryPeak;
        private float batteryStart;
        private bool running;
        private bool lowMemory;
        private string benchKind = BenchKind10m;

        public event Action<PerformanceBenchmarkResult> Completed;

        public bool IsRunning => running;
        public string BenchKind => benchKind;
        public float TargetSeconds => benchmarkSeconds;
        public float ElapsedSeconds => running ? Mathf.Max(0f, Time.realtimeSinceStartup - startedAt) : 0f;
        public float RemainingSeconds => running ? Mathf.Max(0f, benchmarkSeconds - ElapsedSeconds) : 0f;
        public float LivePoseFps => lastPoseFps;
        public float LiveInferenceMs => lastInferenceMs;
        public long LiveMemoryPeakBytes => memoryPeak;
        public string LastSavedPath { get; private set; }
        public PerformanceBenchReport LastReport { get; private set; }

        private void Awake() => trackingController ??= FindFirstObjectByType<JointTrackingController>();
        private void OnEnable() => Application.lowMemory += HandleLowMemory;
        private void OnDisable() => Application.lowMemory -= HandleLowMemory;

        public void BeginBench(string kind)
        {
            benchKind = NormalizeBenchKind(kind);
            Begin(ResolveDurationSeconds(benchKind));
        }

        public void Begin(float durationSeconds = -1f)
        {
            if (durationSeconds > 0f)
            {
                benchmarkSeconds = durationSeconds;
                benchKind = InferBenchKind(durationSeconds);
            }
            else if (string.IsNullOrWhiteSpace(benchKind))
            {
                benchKind = InferBenchKind(benchmarkSeconds);
            }

            startedAt = Time.realtimeSinceStartup;
            frameFpsTotal = poseFpsTotal = inferenceTotal = 0f;
            lastPoseFps = lastInferenceMs = 0f;
            samples = 0;
            memoryPeak = 0;
            batteryStart = SystemInfo.batteryLevel;
            lowMemory = false;
            running = true;
        }

        private void Update()
        {
            if (!running) return;
            frameFpsTotal += 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            lastPoseFps = trackingController == null ? 0f : trackingController.PoseFps;
            lastInferenceMs = trackingController == null ? 0f : trackingController.LastInferenceMilliseconds;
            poseFpsTotal += lastPoseFps;
            inferenceTotal += lastInferenceMs;
            memoryPeak = Math.Max(memoryPeak, GC.GetTotalMemory(false));
            samples++;
            if (Time.realtimeSinceStartup - startedAt >= benchmarkSeconds) Finish();
        }

        public PerformanceBenchmarkResult Finish()
        {
            if (!running)
            {
                return LastReport == null ? null : LastReport.result;
            }

            running = false;
            var batteryEnd = SystemInfo.batteryLevel;
            var result = new PerformanceBenchmarkResult
            {
                startedAtUtc = DateTime.UtcNow.AddSeconds(-(Time.realtimeSinceStartup - startedAt)).ToString("o"),
                endedAtUtc = DateTime.UtcNow.ToString("o"),
                durationSeconds = Mathf.Max(0f, Time.realtimeSinceStartup - startedAt),
                averageFrameFps = samples == 0 ? 0f : frameFpsTotal / samples,
                averagePoseFps = samples == 0 ? 0f : poseFpsTotal / samples,
                averageInferenceMs = samples == 0 ? 0f : inferenceTotal / samples,
                droppedFrames = trackingController == null ? 0 : trackingController.DroppedFrameCount,
                managedMemoryPeakBytes = memoryPeak,
                batteryStart = batteryStart,
                batteryEnd = batteryEnd,
                batteryDrop = batteryStart < 0f || batteryEnd < 0f ? -1f : Mathf.Max(0f, batteryStart - batteryEnd),
                lowMemorySignalReceived = lowMemory
            };

            var report = BuildAndSaveReport(result);
            LastReport = report;
            LastSavedPath = report == null ? string.Empty : report.savedPath;
            Completed?.Invoke(result);
            return result;
        }

        private PerformanceBenchReport BuildAndSaveReport(PerformanceBenchmarkResult result)
        {
            var acceptance = BuildAcceptance(benchKind, result);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var deviceModel = SanitizeFileToken(SystemInfo.deviceModel);
            var fileName = "perf_bench_" + benchKind + "_" + timestamp + "_" + deviceModel + ".json";
            var directory = Path.Combine(Application.persistentDataPath, "performance");
            Directory.CreateDirectory(directory);
            var savedPath = Path.Combine(directory, fileName);

            var report = new PerformanceBenchReport
            {
                schemaVersion = 1,
                pbi = "PBI-085",
                benchKind = benchKind,
                deviceModel = SystemInfo.deviceModel,
                operatingSystem = SystemInfo.operatingSystem,
                unityVersion = Application.unityVersion,
                result = result,
                acceptance = acceptance,
                savedAtUtc = DateTime.UtcNow.ToString("o"),
                savedPath = savedPath
            };

            try
            {
                File.WriteAllText(savedPath, JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[DevicePerformanceProfiler] Failed to save bench report: " + exception.Message);
                report.savedPath = string.Empty;
            }

            return report;
        }

        private static PerformanceAcceptanceReport BuildAcceptance(string kind, PerformanceBenchmarkResult result)
        {
            if (string.Equals(kind, BenchKind10m, StringComparison.Ordinal))
            {
                var evaluated = PerformanceAcceptanceEvaluator.Evaluate(result);
                return new PerformanceAcceptanceReport
                {
                    applicable = true,
                    passed = evaluated.passed,
                    reasons = evaluated.failures ?? Array.Empty<string>()
                };
            }

            return new PerformanceAcceptanceReport
            {
                applicable = false,
                passed = false,
                reasons = new[] { "60s smoke bench; 10m acceptance not applied" }
            };
        }

        public static string NormalizeBenchKind(string kind)
        {
            if (string.Equals(kind, BenchKind10m, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, "10min", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, "600", StringComparison.OrdinalIgnoreCase))
            {
                return BenchKind10m;
            }

            return BenchKind60s;
        }

        public static float ResolveDurationSeconds(string kind)
        {
            return string.Equals(NormalizeBenchKind(kind), BenchKind10m, StringComparison.Ordinal)
                ? Duration10mSeconds
                : Duration60sSeconds;
        }

        private static string InferBenchKind(float durationSeconds)
        {
            if (durationSeconds >= 590f)
            {
                return BenchKind10m;
            }

            return BenchKind60s;
        }

        private static string SanitizeFileToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown-device";
            }

            var chars = value.Trim().ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                {
                    continue;
                }

                chars[i] = '_';
            }

            var sanitized = new string(chars);
            return string.IsNullOrWhiteSpace(sanitized) ? "unknown-device" : sanitized;
        }

        private void HandleLowMemory() => lowMemory = true;
    }
}
