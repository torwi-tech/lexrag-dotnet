using LexRag.Core.Abstractions;

namespace LexRag.Eval;

// Measures retrieval quality on its own (did the search find the right docs?), separate from
// answer quality. Each case can label multiple expected sources; Recall@K measures how many of
// the N required docs were found in top-K, which diverges from Hit-rate@K (>=1 found) when there
// are multiple expected sources per query.
public sealed record RetrievalMetrics(
    int K,
    int Total,
    double HitRateAtK,
    double RecallAtK,
    double Mrr,
    IReadOnlyList<GroupMetrics>? ByGroup = null)
{
    public string ToSummary()
    {
        var agg = $"Retrieval@{K} — hit-rate {HitRateAtK:P0} · recall {RecallAtK:P0} · MRR {Mrr:0.000} (n={Total}; recall e hit-rate divergem quando há múltiplas fontes esperadas)";
        if (ByGroup is not { Count: > 0 }) return agg;

        var groups = string.Join(" | ", ByGroup.Select(g =>
            $"{g.Group} (n={g.Total}): hit-rate {g.HitRateAtK:P0} · recall {g.RecallAtK:P0} · MRR {g.Mrr:0.000}"));
        return $"{agg}\n  per-group: {groups}";
    }
}

// Per-query-group breakdown: same metrics sliced by EvalCase.QueryGroup.
public sealed record GroupMetrics(string Group, int Total, double HitRateAtK, double RecallAtK, double Mrr);

public sealed class RetrievalEvaluator(IHybridRetriever retriever)
{
    public async Task<RetrievalMetrics> RunAsync(IReadOnlyList<EvalCase> cases, int k, CancellationToken ct = default)
    {
        var labeled = cases.Where(c => c is { InDomain: true, ExpectedSourceFiles: { Count: > 0 } }).ToList();
        if (labeled.Count == 0) return new RetrievalMetrics(k, 0, 0, 0, 0);

        // Evaluate each case once, keeping per-case numbers for both the aggregate and per-group slices.
        var perCase = new List<(EvalCase Case, double Hit, double Recall, double Rr)>(labeled.Count);
        foreach (var c in labeled)
        {
            var results = await retriever.RetrieveAsync(c.Query, k, ct);
            var ranks = RanksOf(results, c.ExpectedSourceFiles!);

            var relevantFound = ranks.Count(r => r > 0);
            var totalRelevant = c.ExpectedSourceFiles!.Count;

            var hit = relevantFound >= 1 ? 1.0 : 0.0;
            var recall = (double)relevantFound / totalRelevant;
            var firstRank = ranks.Where(r => r > 0).DefaultIfEmpty(0).Min();
            var rr = firstRank > 0 ? 1.0 / firstRank : 0.0;

            perCase.Add((c, hit, recall, rr));
        }

        var aggregate = Aggregate(perCase.Select(x => (x.Hit, x.Recall, x.Rr)).ToList());

        // Per-group breakdown, only when at least one case carries a group label.
        IReadOnlyList<GroupMetrics>? byGroup = null;
        var hasGroups = labeled.Any(c => c.QueryGroup is not null);
        if (hasGroups)
        {
            byGroup = perCase
                .GroupBy(x => x.Case.QueryGroup ?? "unlabeled")
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var (hitRate, recall, mrr) = Aggregate(g.Select(x => (x.Hit, x.Recall, x.Rr)).ToList());
                    return new GroupMetrics(g.Key, g.Count(), hitRate, recall, mrr);
                })
                .ToList();
        }

        return new RetrievalMetrics(k, labeled.Count, aggregate.HitRate, aggregate.Recall, aggregate.Mrr, byGroup);
    }

    private static (double HitRate, double Recall, double Mrr) Aggregate(IReadOnlyList<(double Hit, double Recall, double Rr)> xs)
    {
        var n = xs.Count;
        return (xs.Sum(x => x.Hit) / n, xs.Sum(x => x.Recall) / n, xs.Sum(x => x.Rr) / n);
    }

    private static IReadOnlyList<int> RanksOf(IReadOnlyList<Core.Models.RetrievedChunk> results, IReadOnlyList<string> expectedSources)
    {
        var ranks = new int[expectedSources.Count];
        for (var s = 0; s < expectedSources.Count; s++)
        {
            ranks[s] = 0;
            for (var i = 0; i < results.Count; i++)
            {
                if (results[i].SourceFile.Equals(expectedSources[s], StringComparison.OrdinalIgnoreCase))
                {
                    ranks[s] = i + 1; // 1-based rank
                    break;
                }
            }
        }
        return ranks;
    }
}
