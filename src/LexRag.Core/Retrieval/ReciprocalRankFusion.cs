using LexRag.Core.Models;

namespace LexRag.Core.Retrieval;

// Fuses ranked lists by rank, not score: cosine and ts_rank live on incomparable scales, so
// rank-based fusion avoids a per-corpus weight. k damps deep ranks (60 = original RRF paper).
// Optional per-leg weights multiply each leg's 1/(k+rank) contribution before summing,
// so a higher SparseWeight leans the result toward exact lexical matches.
public static class ReciprocalRankFusion
{
    public static IReadOnlyList<FusedResult<T>> Fuse<T, TKey>(
        IReadOnlyList<IReadOnlyList<T>> rankedLists,
        Func<T, TKey> keySelector,
        int k = 60,
        int? top = null,
        IReadOnlyList<double>? weights = null)
        where TKey : notnull
    {
        if (k <= 0) throw new ArgumentOutOfRangeException(nameof(k), "RRF k must be positive.");

        var scores = new Dictionary<TKey, double>();
        var representative = new Dictionary<TKey, T>();

        for (var listIdx = 0; listIdx < rankedLists.Count; listIdx++)
        {
            var list = rankedLists[listIdx];
            // Weight defaults to 1.0 when none provided or list index exceeds the weights array.
            var weight = weights != null && listIdx < weights.Count ? weights[listIdx] : 1.0;
            for (var rank = 0; rank < list.Count; rank++)
            {
                var item = list[rank];
                var key = keySelector(item);
                scores[key] = scores.GetValueOrDefault(key) + weight * (1.0 / (k + rank + 1));
                representative.TryAdd(key, item);
            }
        }

        var fused = scores
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Select(kv => new FusedResult<T>(representative[kv.Key], kv.Value))
            .ToList();

        return top is { } n && n < fused.Count ? fused[..n] : fused;
    }

    public static IReadOnlyList<RetrievedChunk> Fuse(
        IReadOnlyList<IReadOnlyList<RetrievedChunk>> rankedLists,
        int k = 60,
        int? top = null,
        IReadOnlyList<double>? weights = null)
    {
        var fused = Fuse(rankedLists, c => c.ChunkKey, k, top, weights);
        return fused.Select(f => f.Item with { Score = f.Score }).ToList();
    }
}

public sealed record FusedResult<T>(T Item, double Score);
