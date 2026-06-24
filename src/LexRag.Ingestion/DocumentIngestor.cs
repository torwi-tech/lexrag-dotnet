using LexRag.Core.Abstractions;
using LexRag.Core.Models;
using LexRag.Core.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LexRag.Ingestion;

public sealed class DocumentIngestor(
    ITextExtractorRouter router,
    IChunker chunker,
    IEmbedder embedder,
    IVectorIndex index,
    ILogger<DocumentIngestor>? logger = null) : IDocumentIngestor
{
    private readonly ILogger _log = logger ?? NullLogger<DocumentIngestor>.Instance;

    public async Task<int> IngestAsync(string sourceFile, Stream content, CancellationToken ct = default)
    {
        var text = await router.Resolve(sourceFile).ExtractTextAsync(content, ct);
        var pieces = chunker.Chunk(text);
        if (pieces.Count == 0)
        {
            _log.LogWarning("No chunks produced for {SourceFile} (empty or unreadable).", sourceFile);
            return 0;
        }

        var embeddings = await embedder.EmbedAsync(pieces, ct);
        var chunks = new List<DocumentChunk>(pieces.Count);
        for (var i = 0; i < pieces.Count; i++)
        {
            chunks.Add(new DocumentChunk
            {
                ChunkKey = DeterministicGuid.From($"{sourceFile}#{i}"),
                SourceFile = sourceFile,
                ChunkIndex = i,
                Content = pieces[i],
                Embedding = embeddings[i],
            });
        }

        // Delete any existing chunks for this file first so an edit that produces fewer chunks
        // cannot leave orphaned higher-index chunks in the store.
        await index.DeleteBySourceFileAsync(sourceFile, ct);
        await index.UpsertAsync(chunks, ct);
        _log.LogInformation("Ingested {Count} chunks from {SourceFile}.", chunks.Count, sourceFile);
        return chunks.Count;
    }
}
