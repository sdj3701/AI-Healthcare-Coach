using System;
using UnityEngine;

namespace Rag.Healthcare.Performance
{
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
