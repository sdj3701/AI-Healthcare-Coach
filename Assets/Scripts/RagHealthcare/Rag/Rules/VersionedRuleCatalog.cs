using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Rag.Healthcare.Rag.Rules
{
    [Serializable]
    public sealed class RuleDefinition
    {
        public string ruleId;
        public string exercise;
        public string severity;
        public string title;
        public string realtimeText;
        public string reportText;
        public string source;
        public bool enabled;
    }

    [Serializable]
    public sealed class RuleCatalogData
    {
        public string schemaVersion;
        public string contentVersion;
        public RuleDefinition[] rules;
    }

    public sealed class VersionedRuleCatalog
    {
        private readonly Dictionary<string, RuleDefinition> rules = new Dictionary<string, RuleDefinition>(StringComparer.OrdinalIgnoreCase);

        public string SchemaVersion { get; private set; } = string.Empty;
        public string ContentVersion { get; private set; } = string.Empty;

        public bool LoadJson(string json, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Rule catalog JSON is empty.";
                return false;
            }

            try
            {
                var data = JsonUtility.FromJson<RuleCatalogData>(json);
                if (data?.rules == null || string.IsNullOrWhiteSpace(data.contentVersion))
                {
                    error = "Rule catalog is missing a version or rules.";
                    return false;
                }

                rules.Clear();
                foreach (var rule in data.rules)
                {
                    if (rule == null || string.IsNullOrWhiteSpace(rule.ruleId) || !rule.enabled) continue;
                    rules[rule.ruleId] = rule;
                }
                SchemaVersion = data.schemaVersion ?? string.Empty;
                ContentVersion = data.contentVersion;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public bool LoadStreamingAssetsFile(string relativePath, out string error)
        {
            var path = Path.Combine(Application.streamingAssetsPath, relativePath ?? string.Empty);
            if (!File.Exists(path))
            {
                error = "Rule catalog file was not found: " + path;
                return false;
            }
            return LoadJson(File.ReadAllText(path), out error);
        }

        public bool TryGet(string ruleId, out RuleDefinition definition) => rules.TryGetValue(ruleId ?? string.Empty, out definition);
        public IReadOnlyCollection<RuleDefinition> All => rules.Values;
    }
}
