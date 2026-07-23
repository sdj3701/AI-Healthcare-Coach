using System;
using UnityEngine;

namespace Rag.Healthcare.Product.Health
{
    /// <summary>
    /// MVP provider that returns manually entered body metrics.
    /// </summary>
    public sealed class ManualHealthDataProvider : MonoBehaviour, IHealthDataProvider
    {
        [SerializeField] private int ageYears;
        [SerializeField] private float heightCm;
        [SerializeField] private float weightKg;

        public string SourceName => "Manual";
        public bool IsAvailable => true;

        public void SetManual(int ageYears, float heightCm, float weightKg)
        {
            this.ageYears = ageYears;
            this.heightCm = heightCm;
            this.weightKg = weightKg;
        }

        public void TryFetchBodyMetrics(Action<HealthBodyMetrics> onResult)
        {
            if (onResult == null)
            {
                return;
            }

            onResult(new HealthBodyMetrics
            {
                AgeYears = ageYears,
                HeightCm = heightCm,
                WeightKg = weightKg,
                HasValue = ageYears > 0 && heightCm > 0f && weightKg > 0f
            });
        }
    }
}
