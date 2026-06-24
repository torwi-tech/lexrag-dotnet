using LexRag.Api.Corpus;
using LexRag.Core.Abstractions;

namespace LexRag.Api;

public static class CorpusSeeder
{
    public static async Task SeedAsync(IServiceProvider services, ILogger logger, CancellationToken ct = default)
    {
        var index = services.GetRequiredService<IVectorIndex>();
        await index.EnsureCreatedAsync(ct);

        if (await index.CountAsync(ct) > 0)
        {
            logger.LogInformation("Corpus already populated ({Count} chunks) — skipping seed.",
                await index.CountAsync(ct));
            return;
        }

        var source = services.GetRequiredService<ICorpusSource>();
        var ingestor = services.GetRequiredService<IDocumentIngestor>();

        // A real embedder adds a network round-trip per document; seed with bounded concurrency so a large
        // corpus doesn't serialize into one call at a time. The store is a ConcurrentDictionary, the embedding
        // client is thread-safe, and retrieval is order-independent, so this stays correct.
        await Parallel.ForEachAsync(
            source.GetDocumentsAsync(ct),
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
            async (doc, token) =>
            {
                var (name, text) = doc;
                await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
                await ingestor.IngestAsync(name, stream, token);
            });

        var total = await index.CountAsync(ct);
        logger.LogInformation("Seeded {Total} chunks.", total);
    }
}
