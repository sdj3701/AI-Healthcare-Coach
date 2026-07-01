namespace Rag.Healthcare.Rag.Knowledge
{
    public sealed class RetrievalResult
    {
        public KnowledgeChunk Chunk;
        public float Score;
        public string MatchReason;
    }
}
