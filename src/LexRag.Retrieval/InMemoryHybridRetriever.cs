using LexRag.Core.Abstractions;
using LexRag.Core.Models;
using LexRag.Core.Retrieval;

namespace LexRag.Retrieval;

// Runs the dense and sparse legs and fuses with RRF in C#, the same fusion the production SQL does.
public sealed class InMemoryHybridRetriever(
    IVectorIndex vectorIndex,
    IKeywordRetriever keywordRetriever,
    IEmbedder embedder,
    RagOptions options) : IHybridRetriever
{
    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, int top, CancellationToken ct = default)
    {
        var pool = options.CandidatePoolSize;
        var queryEmbedding = (await embedder.EmbedAsync([query], ct))[0];

        var dense = await vectorIndex.SearchAsync(queryEmbedding, pool, ct);
        var sparse = await keywordRetriever.SearchAsync(query, pool, ct);

        // Dense leg first, sparse leg second — weights must match that order.
        double[] weights = [options.DenseWeight, options.SparseWeight];
        return ReciprocalRankFusion.Fuse([dense, sparse], options.RrfK, top, weights);
    }
}
