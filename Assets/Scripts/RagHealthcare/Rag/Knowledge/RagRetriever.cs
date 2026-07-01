using System.Collections.Generic;
using Rag.Healthcare.Rag.Runtime;
using UnityEngine;

namespace Rag.Healthcare.Rag.Knowledge
{
    public sealed class RagRetriever : MonoBehaviour
    {
        [SerializeField] private string knowledgeRelativePath = "RagKnowledge";
        [SerializeField] private bool includeDefaultKnowledge = true;
        [SerializeField, Min(1)] private int maxResults = 3;
        [SerializeField] private bool loadOnAwake = true;

        private readonly RagKnowledgeLoader loader = new RagKnowledgeLoader();
        private readonly RagIndex index = new RagIndex();
        private List<KnowledgeChunk> chunks;
        private bool isLoaded;

        public int ChunkCount => index.Count;

        private void Awake()
        {
            if (loadOnAwake)
            {
                Reload();
            }
        }

        public void Reload()
        {
            chunks = loader.Load(knowledgeRelativePath, includeDefaultKnowledge);
            index.Rebuild(chunks);
            isLoaded = true;
            Debug.Log($"[RagRetriever] Loaded {index.Count} knowledge chunks.");
        }

        public IReadOnlyList<RetrievalResult> Retrieve(FeedbackEvent feedbackEvent)
        {
            if (!isLoaded)
            {
                Reload();
            }

            return index.Retrieve(feedbackEvent, maxResults);
        }
    }
}
