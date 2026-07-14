using System;
using AIHealthcareCoach.MediaPipe;
using Rag.Healthcare.Rag.Rules;
using UnityEngine;

namespace Rag.Healthcare.Reports
{
    public sealed class WorkoutReportPresenter : MonoBehaviour
    {
        public event Action<WorkoutReport> ReportReady;
        public WorkoutReport Current { get; private set; }

        public bool BuildReport(PoseSessionSummary summary, IOnDeviceLanguageModel runtime, out string error)
        {
            error = string.Empty;
            if (summary == null)
            {
                error = "Session summary is missing.";
                return false;
            }

            var catalog = new VersionedRuleCatalog();
            catalog.LoadStreamingAssetsFile("RagKnowledge/rules/rules_v1.json", out _);
            var service = new OnDeviceReportService(catalog, runtime, new ReportGenerationSettings());
            Current = service.Generate(summary);
            ReportReady?.Invoke(Current);
            return true;
        }
    }
}
