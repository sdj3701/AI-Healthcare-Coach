using System;
using System.IO;
using Rag.Healthcare.Reports;
using UnityEngine;

namespace Rag.Healthcare.Sharing
{
    [Serializable]
    public sealed class SharePreview
    {
        public string title;
        public string summary;
        public string[] highlights;
        public string[] cautions;
        public string privacyNotice;
    }

    [Serializable]
    public sealed class SharePackage
    {
        public string token;
        public string createdAtUtc;
        public string expiresAtUtc;
        public bool revoked;
        public SharePreview preview;
    }

    [Serializable]
    public sealed class CoachFeedbackRequest
    {
        public string requestId;
        public string createdAtUtc;
        public string exercise;
        public string question;
        public SharePackage sharedReport;
        public string privacyNotice;
    }

    public sealed class WorkoutShareService
    {
        private readonly string directory = Path.Combine(Application.persistentDataPath, "shares");

        public SharePreview Preview(WorkoutReport report)
        {
            if (report == null) return null;
            return new SharePreview
            {
                title = report.headline,
                summary = report.summary,
                highlights = report.highlights,
                cautions = report.cautions,
                privacyNotice = "원본 영상, 사용자 이름, 기기 식별자, 전체 관절 좌표는 공유하지 않습니다."
            };
        }

        public SharePackage Create(WorkoutReport report, TimeSpan lifetime)
        {
            Directory.CreateDirectory(directory);
            var created = DateTime.UtcNow;
            var package = new SharePackage
            {
                token = Guid.NewGuid().ToString("N"),
                createdAtUtc = created.ToString("o"),
                expiresAtUtc = created.Add(lifetime <= TimeSpan.Zero ? TimeSpan.FromDays(7) : lifetime).ToString("o"),
                preview = Preview(report)
            };
            Save(package);
            return package;
        }

        public bool TryOpen(string token, out SharePackage package)
        {
            package = null;
            if (!TryPathFor(token, out var path)) return false;
            if (!File.Exists(path)) return false;
            package = JsonUtility.FromJson<SharePackage>(File.ReadAllText(path));
            return package != null && !package.revoked && DateTime.TryParse(package.expiresAtUtc, out var expiry) && expiry.ToUniversalTime() > DateTime.UtcNow;
        }

        public bool Revoke(string token)
        {
            if (!TryPathFor(token, out var path)) return false;
            if (!File.Exists(path)) return false;
            var package = JsonUtility.FromJson<SharePackage>(File.ReadAllText(path));
            if (package == null) return false;
            package.revoked = true;
            Save(package);
            return true;
        }

        public string ExportCoachRequest(string exercise, string question, SharePackage package)
        {
            Directory.CreateDirectory(directory);
            var request = new CoachFeedbackRequest
            {
                requestId = Guid.NewGuid().ToString("N"),
                createdAtUtc = DateTime.UtcNow.ToString("o"),
                exercise = exercise ?? string.Empty,
                question = question ?? string.Empty,
                sharedReport = package,
                privacyNotice = "사용자가 미리 본 요약 정보만 포함됩니다. 의료 진단 요청에는 사용할 수 없습니다."
            };
            var path = Path.Combine(directory, "coach_request_" + request.requestId + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(request, true));
            return path;
        }

        private void Save(SharePackage package)
        {
            if (!TryPathFor(package.token, out var path)) throw new InvalidOperationException("Share token is invalid.");
            File.WriteAllText(path, JsonUtility.ToJson(package, true));
        }

        private bool TryPathFor(string token, out string path)
        {
            path = string.Empty;
            if (!Guid.TryParseExact(token, "N", out var parsed)) return false;
            path = Path.Combine(directory, "share_" + parsed.ToString("N") + ".json");
            return true;
        }
    }
}
