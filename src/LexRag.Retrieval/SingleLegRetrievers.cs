using LexRag.Core.Abstractions;
using LexRag.Core.Models;

namespace LexRag.Retrieval;

// Dense-only retriever: exposes the vector index leg as IHybridRetriever so it can be compared
// against the sparse-only and hybrid configurations in the ablation harness without changing ports.
public sealed class DenseOnlyRetriever(IVectorIndex vectorIndex, IEmbedder embedder) : IHybridRetriever
{
    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(
        string query, int top, CancellationToken ct = default)
    {
        var embedding = (await embedder.EmbedAsync([query], ct))[0];
        return await vectorIndex.SearchAsync(embedding, top, ct);
    }
}

// Sparse-only retriever: exposes the BM25/keyword leg as IHybridRetriever for the ablation harness.
public sealed class SparseOnlyRetriever(IKeywordRetriever keywordRetriever) : IHybridRetriever
{
    public Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(
        string query, int top, CancellationToken ct = default)
        => keywordRetriever.SearchAsync(query, top, ct);
}
