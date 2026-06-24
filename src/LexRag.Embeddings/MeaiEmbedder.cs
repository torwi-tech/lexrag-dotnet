using LexRag.Core.Abstractions;
using Microsoft.Extensions.AI;

namespace LexRag.Embeddings;

public sealed class MeaiEmbedder(
    IEmbeddingGenerator<string, Embedding<float>> generator,
    int dimensions = 1536) : IEmbedder
{
    public int Dimensions => dimensions;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
    {
        // Request the configured output size so a text-embedding-3 model matches the index schema (VECTOR(dim)).
        var options = new EmbeddingGenerationOptions { Dimensions = dimensions };
        var embeddings = await generator.GenerateAsync(inputs, options, ct);
        return embeddings.Select(e => e.Vector.ToArray()).ToList();
    }
}
