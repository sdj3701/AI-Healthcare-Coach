using System;
using System.Collections.Generic;
using System.IO;
using Rag.Healthcare.Camera;
using Rag.Healthcare.Pose;
using UnityEngine;

namespace Rag.Healthcare.Performance
{
    public enum DevicePerformanceMode { Standard, LowSpec }

    [Serializable]
    public sealed class PerformanceProfile
    {
        public DevicePerformanceMode mode;
        public int cameraWidth;
        public int cameraHeight;
        public int cameraFps;
        public int poseFps;
        public bool onDeviceLlmEnabled;
        public int maximumSessionMinutes;
    }

    public sealed class RuntimeQualityController : MonoBehaviour
    {
        [SerializeField] private CameraCaptureSource cameraSource;
        [SerializeField] private JointTrackingController trackingController;

        public PerformanceProfile Current { get; private set; }

        private void Awake()
        {
            cameraSource ??= FindFirstObjectByType<CameraCaptureSource>();
            trackingController ??= FindFirstObjectByType<JointTrackingController>();
        }

        public PerformanceProfile Apply(DevicePerformanceMode mode)
        {
            Current = mode == DevicePerformanceMode.LowSpec
                ? new PerformanceProfile { mode = mode, cameraWidth = 640, cameraHeight = 480, cameraFps = 20, poseFps = 10, onDeviceLlmEnabled = false, maximumSessionMinutes = 8 }
                : CreateStandardProfile(mode);
            cameraSource?.ConfigureCapture(Current.cameraWidth, Current.cameraHeight, Current.cameraFps);
            trackingController?.ConfigureSamplingRate(Current.poseFps);
            return Current;
        }

        private static PerformanceProfile CreateStandardProfile(DevicePerformanceMode mode)
        {
#if UNITY_IOS && !UNITY_EDITOR
            // Keep the profile request aligned with CameraCaptureSource's iOS clamp.
            // 640x480 avoids known WebCamTexture rotation metadata issues.
            const int cameraWidth = 640;
            const int cameraHeight = 480;
#else
            const int cameraWidth = 1280;
            const int cameraHeight = 720;
#endif
            return new PerformanceProfile
            {
                mode = mode,
                cameraWidth = cameraWidth,
                cameraHeight = cameraHeight,
                cameraFps = 30,
                poseFps = 15,
                onDeviceLlmEnabled = true,
                maximumSessionMinutes = 15
            };
        }
    }

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

    public sealed class DevicePerformanceProfiler : MonoBehaviour
    {
        [SerializeField] private JointTrackingController trackingController;
        [SerializeField, Min(10f)] private float benchmarkSeconds = 600f;

        private float startedAt;
        private float frameFpsTotal;
        private float poseFpsTotal;
        private float inferenceTotal;
        private int samples;
        private long memoryPeak;
        private float batteryStart;
        private bool running;
        private bool lowMemory;

        public event Action<PerformanceBenchmarkResult> Completed;

        private void Awake() => trackingController ??= FindFirstObjectByType<JointTrackingController>();
        private void OnEnable() => Application.lowMemory += HandleLowMemory;
        private void OnDisable() => Application.lowMemory -= HandleLowMemory;

        public void Begin(float durationSeconds = -1f)
        {
            benchmarkSeconds = durationSeconds > 0f ? durationSeconds : benchmarkSeconds;
            startedAt = Time.realtimeSinceStartup;
            frameFpsTotal = poseFpsTotal = inferenceTotal = 0f;
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
            poseFpsTotal += trackingController == null ? 0f : trackingController.PoseFps;
            inferenceTotal += trackingController == null ? 0f : trackingController.LastInferenceMilliseconds;
            memoryPeak = Math.Max(memoryPeak, GC.GetTotalMemory(false));
            samples++;
            if (Time.realtimeSinceStartup - startedAt >= benchmarkSeconds) Finish();
        }

        public PerformanceBenchmarkResult Finish()
        {
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
            Completed?.Invoke(result);
            return result;
        }

        private void HandleLowMemory() => lowMemory = true;
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

    public sealed class DeviceHealthMonitor : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float maximumSessionMinutes = 15f;
        [SerializeField, Min(1f)] private float sustainedLowFpsSeconds = 20f;
        [SerializeField, Min(1f)] private float lowFpsThreshold = 12f;

        private float startedAt;
        private float lowFpsStartedAt = -1f;
        public event Action<string> Warning;

        private void OnEnable()
        {
            startedAt = Time.realtimeSinceStartup;
            Application.lowMemory += HandleLowMemory;
        }
        private void OnDisable() => Application.lowMemory -= HandleLowMemory;

        private void Update()
        {
            if (Time.realtimeSinceStartup - startedAt >= maximumSessionMinutes * 60f)
            {
                Warning?.Invoke("권장 세션 시간을 초과했습니다. 기기 발열을 줄이기 위해 잠시 쉬세요.");
                startedAt = Time.realtimeSinceStartup;
            }
            var fps = 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            if (fps < lowFpsThreshold)
            {
                if (lowFpsStartedAt < 0f) lowFpsStartedAt = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - lowFpsStartedAt >= sustainedLowFpsSeconds)
                {
                    Warning?.Invoke("성능 저하가 지속됩니다. 저사양 모드로 전환하거나 기기를 식혀 주세요.");
                    lowFpsStartedAt = Time.realtimeSinceStartup;
                }
            }
            else lowFpsStartedAt = -1f;
        }

        private void HandleLowMemory() => Warning?.Invoke("메모리가 부족합니다. 리포트 모델을 닫고 운동 세션을 저장하세요.");
    }
}
