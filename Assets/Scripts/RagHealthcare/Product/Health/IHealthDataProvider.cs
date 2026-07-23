using System;

namespace Rag.Healthcare.Product.Health
{
    public struct HealthBodyMetrics
    {
        public int AgeYears;
        public float HeightCm;
        public float WeightKg;
        public bool HasValue;
    }

    /// <summary>
    /// Abstraction for body metrics sources (Manual MVP; HealthKit / Google Fit / InBody later).
    /// </summary>
    public interface IHealthDataProvider
    {
        string SourceName { get; }
        bool IsAvailable { get; }
        void TryFetchBodyMetrics(Action<HealthBodyMetrics> onResult);
    }
}
