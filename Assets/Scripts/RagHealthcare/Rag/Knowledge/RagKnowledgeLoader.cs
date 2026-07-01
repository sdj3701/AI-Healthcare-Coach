using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Rag.Healthcare.Rag.Knowledge
{
    public sealed class RagKnowledgeLoader
    {
        public List<KnowledgeChunk> Load(string knowledgeRelativePath, bool includeDefaultKnowledge)
        {
            var chunks = new List<KnowledgeChunk>();
            var root = ResolveRoot(knowledgeRelativePath);

            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                foreach (var path in Directory.GetFiles(root, "*.md", SearchOption.AllDirectories))
                {
                    var chunk = TryLoadMarkdown(path, root);
                    if (chunk != null)
                    {
                        chunks.Add(chunk);
                    }
                }
            }

            if (includeDefaultKnowledge)
            {
                AddDefaultKnowledge(chunks);
            }

            return chunks;
        }

        private static string ResolveRoot(string knowledgeRelativePath)
        {
            var relative = string.IsNullOrWhiteSpace(knowledgeRelativePath)
                ? "RagKnowledge"
                : knowledgeRelativePath.Trim();

            if (Path.IsPathRooted(relative))
            {
                return relative;
            }

            return Path.Combine(Application.streamingAssetsPath, relative);
        }

        private static KnowledgeChunk TryLoadMarkdown(string path, string root)
        {
            try
            {
                var content = File.ReadAllText(path, Encoding.UTF8);
                var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var body = content;

                if (content.StartsWith("---", StringComparison.Ordinal))
                {
                    var secondMarker = content.IndexOf("\n---", 3, StringComparison.Ordinal);
                    if (secondMarker >= 0)
                    {
                        var frontMatter = content.Substring(3, secondMarker - 3);
                        body = content.Substring(secondMarker + 4).Trim();
                        ParseMetadata(frontMatter, metadata);
                    }
                }

                var id = Get(metadata, "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = Path.GetFileNameWithoutExtension(path);
                }

                return new KnowledgeChunk
                {
                    Id = id,
                    SourcePath = MakeRelativePath(path, root),
                    Exercise = Get(metadata, "exercise"),
                    RuleId = Get(metadata, "ruleId"),
                    Joint = Get(metadata, "joint"),
                    Tags = ParseTags(Get(metadata, "tags")),
                    RealtimeText = Get(metadata, "realtimeText"),
                    Text = body
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[RagKnowledgeLoader] Could not load '{path}': {exception.Message}");
                return null;
            }
        }

        private static void ParseMetadata(string frontMatter, Dictionary<string, string> metadata)
        {
            using var reader = new StringReader(frontMatter);
            while (reader.ReadLine() is { } line)
            {
                var separator = line.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, separator).Trim();
                var value = line.Substring(separator + 1).Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(key))
                {
                    metadata[key] = value;
                }
            }
        }

        private static string[] ParseTags(string rawTags)
        {
            if (string.IsNullOrWhiteSpace(rawTags))
            {
                return Array.Empty<string>();
            }

            rawTags = rawTags.Trim();
            if (rawTags.StartsWith("[", StringComparison.Ordinal) && rawTags.EndsWith("]", StringComparison.Ordinal))
            {
                rawTags = rawTags.Substring(1, rawTags.Length - 2);
            }

            var parts = rawTags.Split(',');
            var tags = new List<string>();
            foreach (var part in parts)
            {
                var tag = part.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    tags.Add(tag);
                }
            }

            return tags.ToArray();
        }

        private static string Get(Dictionary<string, string> metadata, string key)
        {
            return metadata.TryGetValue(key, out var value) ? value : string.Empty;
        }

        private static string MakeRelativePath(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            return path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void AddDefaultKnowledge(List<KnowledgeChunk> chunks)
        {
            chunks.Add(new KnowledgeChunk
            {
                Id = "default_squat_knee_alignment",
                Exercise = "squat",
                RuleId = "squat_knee_alignment",
                Joint = "knee",
                Tags = new[] { "alignment", "knee", "foot" },
                RealtimeText = "{sideKo} 무릎을 발끝 방향과 맞춰 주세요.",
                Text = "스쿼트 중 무릎은 발끝과 같은 방향을 향해야 합니다. 무릎이 안쪽으로 무너지면 무릎 관절에 부담이 커질 수 있습니다."
            });

            chunks.Add(new KnowledgeChunk
            {
                Id = "default_squat_torso_tilt",
                Exercise = "squat",
                RuleId = "squat_torso_tilt",
                Joint = "shoulder",
                Tags = new[] { "torso", "spine", "balance" },
                RealtimeText = "가슴을 열고 상체를 조금 더 세워 주세요.",
                Text = "스쿼트에서는 상체가 과도하게 앞으로 무너지지 않도록 가슴을 열고 골반 위에 어깨를 쌓는 느낌을 유지합니다."
            });

            chunks.Add(new KnowledgeChunk
            {
                Id = "default_squat_center_balance",
                Exercise = "squat",
                RuleId = "squat_center_balance",
                Joint = "hip",
                Tags = new[] { "balance", "center" },
                RealtimeText = "체중을 양발 중앙에 고르게 실어 주세요.",
                Text = "중심이 한쪽으로 쏠리면 좌우 다리 사용량이 달라지고 자세가 흔들릴 수 있습니다."
            });
        }
    }
}
