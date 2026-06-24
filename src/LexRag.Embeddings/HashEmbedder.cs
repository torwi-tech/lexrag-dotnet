using System.Text.RegularExpressions;
using LexRag.Core.Abstractions;
using LexRag.Core.Retrieval;

namespace LexRag.Embeddings;

// Key-free stand-in via feature hashing; same 1536 dims as text-embedding-3-small. Captures no real semantics.
public sealed partial class HashEmbedder(int dimensions = 1536) : IEmbedder
{
    public int Dimensions => dimensions;

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<float[]>>(inputs.Select(Embed).ToList());

    public float[] Embed(string text)
    {
        var v = new float[dimensions];
        foreach (Match token in TokenPattern().Matches(text.ToLowerInvariant()))
        {
            var h = Fnv1a(token.Value);
            var bucket = (int)(h % (uint)dimensions);
            var sign = (h & 0x8000_0000u) == 0 ? 1f : -1f;
            v[bucket] += sign;
        }
        VectorMath.NormalizeInPlace(v);
        return v;
    }

    // FNV-1a, not String.GetHashCode: the latter is per-process randomised and would break determinism.
    private static uint Fnv1a(string s)
    {
        const uint offset = 2166136261, prime = 16777619;
        var hash = offset;
        foreach (var c in s)
        {
            hash ^= c;
            hash *= prime;
        }
        return hash;
    }

    [GeneratedRegex(@"[\p{L}\p{Nd}]+")]
    private static partial Regex TokenPattern();
}
