using System.Text.RegularExpressions;
using LexRag.Core.Abstractions;
using LexRag.Core.Models;
using LexRag.Core.Text;

namespace LexRag.Retrieval;

public sealed class IdentityReranker : IReranker
{
    public Task<IReadOnlyList<RetrievedChunk>> RerankAsync(
        string query, IReadOnlyList<RetrievedChunk> candidates, int top, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RetrievedChunk>>(candidates.Take(top).ToList());
}

// Key-free stand-in for a model-backed cross-encoder: re-orders candidates by query-term coverage.
// Coverage (0..1) is the primary signal. The incoming RRF score (~0.03 at most for two legs at k=60)
// only breaks ties between chunks of equal coverage, so it never overrides a coverage difference.
// A real cross-encoder drops in behind the same IReranker port. The name says what this is, so the
// exposed score reads as coverage rather than a calibrated dense+lexical blend.
public sealed partial class LexicalCoverageReranker : IReranker
{
    public Task<IReadOnlyList<RetrievedChunk>> RerankAsync(
        string query, IReadOnlyList<RetrievedChunk> candidates, int top, CancellationToken ct = default)
    {
        var queryTerms = Tokenize(query).ToHashSet();
        if (queryTerms.Count == 0)
            return Task.FromResult<IReadOnlyList<RetrievedChunk>>(candidates.Take(top).ToList());

        var reranked = candidates
            .Select(c =>
            {
                var docTerms = Tokenize(c.Content).ToHashSet();
                var coverage = (double)queryTerms.Count(docTerms.Contains) / queryTerms.Count;
                return new { Chunk = c, Coverage = coverage };
            })
            // Coverage is the primary signal; the RRF score breaks ties only between equal coverage,
            // so a coverage difference always wins. The exposed score is the coverage, not a blended sum.
            .OrderByDescending(x => x.Coverage)
            .ThenByDescending(x => x.Chunk.Score)
            .Take(top)
            .Select(x => x.Chunk with { Score = x.Coverage })
            .ToList();

        return Task.FromResult<IReadOnlyList<RetrievedChunk>>(reranked);
    }

    private static IEnumerable<string> Tokenize(string text) =>
        TokenPattern().Matches(text.ToLowerInvariant()).Select(m => m.Value);

    [GeneratedRegex(@"[\p{L}\p{Nd}]+")]
    private static partial Regex TokenPattern();
}

// Maximal Marginal Relevance reranker: balances relevance with diversity by penalizing candidates
// similar to already-selected chunks. Jaccard over diacritic-folded tokens is the similarity measure,
// keeping it consistent with the sparse BM25 leg. lambda=1 degrades to pure relevance order.
public sealed partial class MmrReranker(double lambda = 0.7) : IReranker
{
    public Task<IReadOnlyList<RetrievedChunk>> RerankAsync(
        string query, IReadOnlyList<RetrievedChunk> candidates, int top, CancellationToken ct = default)
    {
        if (candidates.Count == 0)
            return Task.FromResult<IReadOnlyList<RetrievedChunk>>([]);

        var tokenSets = candidates
            .Select(c => Tokenize(c.Content))
            .ToArray();

        var selected = new List<RetrievedChunk>(top);
        // Track which candidate indices have been selected so Jaccard uses tokenSets[idx] directly,
        // avoiding an O(n) IndexOf scan and any reliance on ChunkKey uniqueness.
        var selectedIndices = new List<int>(top);
        var remaining = Enumerable.Range(0, candidates.Count).ToList();

        while (selected.Count < top && remaining.Count > 0)
        {
            var bestIdx = -1;
            var bestScore = double.NegativeInfinity;

            foreach (var i in remaining)
            {
                var relevance = candidates[i].Score;
                var maxSim = selectedIndices.Count == 0
                    ? 0.0
                    : selectedIndices.Max(si => Jaccard(tokenSets[i], tokenSets[si]));

                var mmrScore = lambda * relevance - (1 - lambda) * maxSim;
                if (mmrScore > bestScore)
                {
                    bestScore = mmrScore;
                    bestIdx = i;
                }
            }

            // Preserve the original relevance score so the MinRelevanceScore gate downstream
            // sees a proper relevance signal, not the penalized MMR objective (which can be negative).
            // The MMR ranking is encoded in selection ORDER, not in the Score field.
            selected.Add(candidates[bestIdx]);
            selectedIndices.Add(bestIdx);
            remaining.Remove(bestIdx);
        }

        return Task.FromResult<IReadOnlyList<RetrievedChunk>>(selected);
    }

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return 1.0;
        var intersection = a.Count(b.Contains);
        var union = a.Count + b.Count - intersection;
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static HashSet<string> Tokenize(string text) =>
        [.. MmrTokenPattern().Matches(DiacriticFolder.Fold(text.ToLowerInvariant())).Select(m => m.Value)];

    [GeneratedRegex(@"[\p{L}\p{Nd}]+")]
    private static partial Regex MmrTokenPattern();
}
