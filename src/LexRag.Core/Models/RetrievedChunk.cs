namespace LexRag.Core.Models;

public sealed record RetrievedChunk
{
    public required Guid ChunkKey { get; init; }
    public required string SourceFile { get; init; }
    public required int ChunkIndex { get; init; }
    public required string Content { get; init; }

    // Higher is better regardless of retriever (cosine, ts_rank, or fused RRF).
    public required double Score { get; init; }
}
