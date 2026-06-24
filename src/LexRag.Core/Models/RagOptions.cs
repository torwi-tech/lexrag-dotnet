namespace LexRag.Core.Models;

public sealed class RagOptions
{
    public const string SectionName = "Rag";

    // 1536 = text-embedding-3-small; the deterministic fake matches it.
    public int EmbeddingDimensions { get; set; } = 1536;

    public int ChunkSize { get; set; } = 2000;

    public int ChunkOverlap { get; set; } = 200;

    public int CandidatePoolSize { get; set; } = 50;

    // 60 is the canonical RRF constant from the original paper.
    public int RrfK { get; set; } = 60;

    // Applied per query (SET hnsw.ef_search) so recall can change without rebuilding the index.
    public int EfSearch { get; set; } = 100;

    public int TopK { get; set; } = 5;

    public double MinRelevanceScore { get; set; } = 0.0;

    // Leg weights for RRF: multiply each leg's 1/(k+rank) by its weight before summing.
    // Defaults of 1.0 preserve the original equal-weight behavior.
    public double DenseWeight { get; set; } = 1.0;
    public double SparseWeight { get; set; } = 1.0;

    // MMR diversity reranker lambda (0=max diversity, 1=max relevance). Only active when Reranker=mmr.
    public double MmrLambda { get; set; } = 0.7;

    // Selects the active reranker. "mmr" activates MmrReranker; anything else uses the default LexicalCoverageReranker.
    public string Reranker { get; set; } = "lexical";

    public double GradeSufficiencyThreshold { get; set; } = 0.5;

    public int MaxCorrectiveRounds { get; set; } = 1;
}
