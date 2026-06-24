using LexRag.Core.Abstractions;
using LexRag.Core.Models;
using LexRag.Core.Retrieval;

namespace LexRag.Index;

// Brute-force O(n) cosine for the dev/test path; prod uses HNSW in pgvector.
public sealed class InMemoryVectorIndex(InMemoryChunkStore store) : IVectorIndex
{
    public Task EnsureCreatedAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task UpsertAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken ct = default)
    {
        store.Upsert(chunks);
        return Task.CompletedTask;
    }

    public Task DeleteBySourceFileAsync(string sourceFile, CancellationToken ct = default)
    {
        store.DeleteBySourceFile(sourceFile);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(float[] queryEmbedding, int top, CancellationToken ct = default)
    {
        var hits = store.All()
            .Where(c => c.Embedding.Length == queryEmbedding.Length)
            .Select(c => new RetrievedChunk
            {
                ChunkKey = c.ChunkKey,
                SourceFile = c.SourceFile,
                ChunkIndex = c.ChunkIndex,
                Content = c.Content,
                Score = VectorMath.CosineSimilarity(queryEmbedding, c.Embedding),
            })
            .OrderByDescending(c => c.Score)
            .Take(top)
            .ToList();

        return Task.FromResult<IReadOnlyList<RetrievedChunk>>(hits);
    }

    public Task<long> CountAsync(CancellationToken ct = default) => Task.FromResult(store.Count);
}
