using System;
using System.Collections.Generic;
using System.IO;
using AIHealthcareCoach.MediaPipe;
using Rag.Healthcare.Rag.Rules;
using UnityEngine;

namespace Rag.Healthcare.Reports
{
    [Serializable]
    public sealed class ReportGenerationSettings
    {
        [Range(0f, 1f)] public float temperature = 0.2f;
        public int seed = 42;
        public int maximumTokens = 320;
    }

    public interface IOnDeviceLanguageModel
    {
        bool IsReady { get; }
        string RuntimeName { get; }
        bool TryGenerate(string prompt, ReportGenerationSettings settings, out string response, out string error);
    }

    [Serializable]
    public sealed class WorkoutReport
    {
        public string schemaVersion = "1.0";
        public string sessionId;
        public string generatedAtUtc;
        public string generator;
        public string headline;
        public string summary;
        public string[] highlights;
        public string[] cautions;
        public string disclaimer;
        public string ruleCatalogVersion;
    }

    public sealed class OnDeviceReportService
    {
        private static readonly string[] ForbiddenTerms =
        {
            "진단", "처방", "완치", "치료됩니다", "재활 처방", "diagnosis", "prescription", "guaranteed cure"
        };

        private readonly VersionedRuleCatalog rules;
        private readonly IOnDeviceLanguageModel runtime;
        private readonly ReportGenerationSettings settings;

        public OnDeviceReportService(VersionedRuleCatalog rules, IOnDeviceLanguageModel runtime, ReportGenerationSettings settings)
        {
            this.rules = rules;
            this.runtime = runtime;
            this.settings = settings ?? new ReportGenerationSettings();
        }

        public WorkoutReport Generate(PoseSessionSummary summary)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            var fallback = BuildTemplateReport(summary);
            if (runtime == null || !runtime.IsReady) return fallback;

            var prompt = BuildConstrainedPrompt(summary);
            if (!runtime.TryGenerate(prompt, settings, out var response, out _) || !IsSafe(response)) return fallback;
            fallback.generator = runtime.RuntimeName;
            fallback.summary = AppendSafetyLanguage(response.Trim());
            return fallback;
        }

        private WorkoutReport BuildTemplateReport(PoseSessionSummary summary)
        {
            var highlights = new List<string>
            {
                $"운동 시간 {summary.durationSeconds:0}초",
                $"안정적으로 처리된 프레임 {summary.successfulFrames}개",
                $"평균 포즈 FPS {summary.averagePoseFps:0.0}"
            };
            var cautions = new List<string>();
            if (summary.warningFeedbackCount + summary.criticalFeedbackCount > 0)
                cautions.Add($"주의 피드백 {summary.warningFeedbackCount + summary.criticalFeedbackCount}회를 리플레이에서 확인하세요.");
            if (summary.droppedFrames > 0) cautions.Add("프레임 누락 구간은 자세 판정의 근거로 사용하지 않았습니다.");

            return new WorkoutReport
            {
                sessionId = summary.sessionId,
                generatedAtUtc = DateTime.UtcNow.ToString("o"),
                generator = "deterministic-template",
                headline = "오늘의 스쿼트 코칭 요약",
                summary = "기록된 관절 좌표와 규칙 이벤트만으로 생성한 운동 요약입니다.",
                highlights = highlights.ToArray(),
                cautions = cautions.ToArray(),
                disclaimer = "이 리포트는 일반적인 피트니스 코칭 정보이며 의료 진단·치료를 대신하지 않습니다. 통증이 있으면 운동을 중단하세요.",
                ruleCatalogVersion = rules?.ContentVersion ?? string.Empty
            };
        }

        private static string BuildConstrainedPrompt(PoseSessionSummary summary)
        {
            return "SYSTEM: You are an offline fitness posture coach. Use only the supplied numeric summary. " +
                   "Do not diagnose, prescribe treatment, mention diseases, or invent facts. Respond in Korean in 3 short sentences.\n" +
                   $"SESSION: duration={summary.durationSeconds:0.0}s, feedback={summary.feedbackCount}, warnings={summary.warningFeedbackCount}, " +
                   $"critical={summary.criticalFeedbackCount}, pose_fps={summary.averagePoseFps:0.0}, visibility={summary.averageVisibility:0.00}.";
        }

        private static bool IsSafe(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (var term in ForbiddenTerms)
                if (text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return true;
        }

        private static string AppendSafetyLanguage(string text) => text + " 통증이 있으면 운동을 중단하고 전문가와 상담하세요.";
    }

    public sealed class OnDeviceRuntimeVerifier
    {
        public RuntimeCapability Verify(string modelRelativePath, long minimumAvailableMemoryMb = 1200)
        {
            var path = Path.Combine(Application.streamingAssetsPath, modelRelativePath ?? string.Empty);
            var availableMemory = SystemInfo.systemMemorySize;
            return new RuntimeCapability
            {
                modelPath = path,
                modelPresent = File.Exists(path),
                availableMemoryMb = availableMemory,
                supported = File.Exists(path) && availableMemory >= minimumAvailableMemoryMb,
                reason = !File.Exists(path) ? "Model file is missing." : availableMemory < minimumAvailableMemoryMb ? "Available memory is below the runtime threshold." : "Runtime prerequisites passed."
            };
        }
    }

    [Serializable]
    public sealed class RuntimeCapability
    {
        public bool supported;
        public bool modelPresent;
        public long availableMemoryMb;
        public string modelPath;
        public string reason;
    }
}
