using System;

namespace Rag.Healthcare.Reports
{
    public interface IUnloadableOnDeviceModel : IOnDeviceLanguageModel
    {
        long EstimatedMemoryBytes { get; }
        bool Load(out string error);
        void Unload();
    }

    public sealed class OnDeviceModelLifecycle
    {
        private readonly IUnloadableOnDeviceModel model;
        public DateTime LastUsedUtc { get; private set; }

        public OnDeviceModelLifecycle(IUnloadableOnDeviceModel model)
        {
            this.model = model;
        }

        public bool Acquire(long memoryBudgetBytes, out string error)
        {
            error = string.Empty;
            if (model == null)
            {
                error = "Model runtime is missing.";
                return false;
            }
            if (model.EstimatedMemoryBytes > memoryBudgetBytes)
            {
                error = "Model exceeds the configured memory budget.";
                return false;
            }
            if (!model.IsReady && !model.Load(out error)) return false;
            LastUsedUtc = DateTime.UtcNow;
            return true;
        }

        public void Release() => model?.Unload();

        public bool ReleaseIfIdle(TimeSpan idleTime)
        {
            if (model == null || !model.IsReady || DateTime.UtcNow - LastUsedUtc < idleTime) return false;
            model.Unload();
            return true;
        }
    }
}
