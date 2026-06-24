using LexRag.Core.Models;

namespace LexRag.Core.Abstractions;

public interface ITextExtractor
{
    Task<string> ExtractTextAsync(Stream content, CancellationToken ct = default);
}

public interface IChunker
{
    IReadOnlyList<string> Chunk(string text);
}

// The one seam that needs an API key in prod; a deterministic fake keeps everything keyless in test.
public interface IEmbedder
{
    int Dimensions { get; }
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default);
}

public interface IVectorIndex
{
    Task EnsureCreatedAsync(CancellationToken ct = default);
    Task UpsertAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken ct = default);
    // Remove all chunks for a source file so a re-ingest of a shorter document leaves no orphans.
    Task DeleteBySourceFileAsync(string sourceFile, CancellationToken ct = default);
    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(float[] queryEmbedding, int top, CancellationToken ct = default);
    Task<long> CountAsync(CancellationToken ct = default);
}

public interface IKeywordRetriever
{
    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string query, int top, CancellationToken ct = default);
}

public interface IHybridRetriever
{
    Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, int top, CancellationToken ct = default);
}

public interface IReranker
{
    Task<IReadOnlyList<RetrievedChunk>> RerankAsync(string query, IReadOnlyList<RetrievedChunk> candidates, int top, CancellationToken ct = default);
}

public interface IAnswerGenerator
{
    Task<RagAnswer> GenerateAsync(string query, IReadOnlyList<RetrievedChunk> context, CancellationToken ct = default);
}

public interface IDocumentIngestor
{
    Task<int> IngestAsync(string sourceFile, Stream content, CancellationToken ct = default);
}

public interface IRagPipeline
{
    Task<RagAnswer> AskAsync(string query, CancellationToken ct = default);
}
