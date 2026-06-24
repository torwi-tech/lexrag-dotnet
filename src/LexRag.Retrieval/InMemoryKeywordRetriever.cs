using LexRag.Core.Abstractions;
using LexRag.Core.Models;
using LexRag.Core.Retrieval;
using LexRag.Index;

namespace LexRag.Retrieval;

// BM25 sparse leg for the no-database path; mirrors Postgres ts_rank in PgVectorHybridRetriever.
public sealed class InMemoryKeywordRetriever(InMemoryChunkStore store, Bm25Scorer scorer) : IKeywordRetriever
{
    public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string query, int top, CancellationToken ct = default)
    {
        var chunks = store.All();
        var corpus = chunks.Select(c => c.Content).ToList();
        var scored = scorer.Score(corpus, query, top);

        var results = scored.Select(s => new RetrievedChunk
        {
            ChunkKey = chunks[s.DocIndex].ChunkKey,
            SourceFile = chunks[s.DocIndex].SourceFile,
            ChunkIndex = chunks[s.DocIndex].ChunkIndex,
            Content = chunks[s.DocIndex].Content,
            Score = s.Score,
        }).ToList();

        return Task.FromResult<IReadOnlyList<RetrievedChunk>>(results);
    }
}
