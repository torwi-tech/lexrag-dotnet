namespace LexRag.Core.Models;

public sealed record DocumentChunk
{
    // Stable id derived from file+index so re-ingesting the same file is idempotent.
    public required Guid ChunkKey { get; init; }
    public required string SourceFile { get; init; }
    public required int ChunkIndex { get; init; }
    public required string Content { get; init; }

    public float[] Embedding { get; init; } = [];
}
