using System;
using System.IO;
using UnityEngine;

namespace Rag.Healthcare.Content
{
    [Serializable]
    public sealed class ContentVersionManifest
    {
        public string schemaVersion;
        public string poseModelVersion;
        public string reportModelVersion;
        public string ruleCatalogVersion;
        public string promptVersion;
        public string safetyCopyVersion;

        public static ContentVersionManifest Load(string relativePath = "Versions/content_versions.json")
        {
            var path = Path.Combine(Application.streamingAssetsPath, relativePath);
            return File.Exists(path) ? JsonUtility.FromJson<ContentVersionManifest>(File.ReadAllText(path)) : null;
        }
    }
}
