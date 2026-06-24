using LexRag.Core.Abstractions;

namespace LexRag.Eval;

// Evaluates retrieval quality across multiple named configurations and returns a comparison table.
// Each configuration supplies its own IHybridRetriever (and optional IReranker) so any combination
// of dense-only, sparse-only, hybrid, rerank on/off, or pipeline variant can be compared without
// changing this class. All components use keyless in-memory implementations by default.
public sealed class AblationRunner(IReadOnlyList<AblationConfig> configurations)
{
    public async Task<AblationReport> RunAsync(
        IReadOnlyList<EvalCase> cases,
        int k,
        CancellationToken ct = default)
    {
        var labeled = cases
            .Where(c => c is { InDomain: true, ExpectedSourceFiles: { Count: > 0 } })
            .ToList();

        var rows = new List<AblationRow>(configurations.Count);
        foreach (var config in configurations)
        {
            var evaluator = new RetrievalEvaluator(
                config.Reranker is null
                    ? config.Retriever
                    : new RerankedRetrieverAdapter(config.Retriever, config.Reranker));

            var metrics = await evaluator.RunAsync(labeled, k, ct);
            rows.Add(new AblationRow(
                config.Name,
                config.Description,
                metrics.Total,
                metrics.HitRateAtK,
                metrics.RecallAtK,
                metrics.Mrr));
        }

        return new AblationReport(k, labeled.Count, rows);
    }

    // Wraps retriever + reranker into a single IHybridRetriever so RetrievalEvaluator sees one interface.
    // The reranker re-orders candidates but RetrievalEvaluator measures recall over the returned list,
    // so this correctly captures whether reranking improves recall at the requested K.
    private sealed class RerankedRetrieverAdapter(IHybridRetriever inner, IReranker reranker) : IHybridRetriever
    {
        public async Task<IReadOnlyList<Core.Models.RetrievedChunk>> RetrieveAsync(
            string query, int top, CancellationToken ct = default)
        {
            // Fetch a wider pool so the reranker has candidates to work with before truncating to K.
            var candidates = await inner.RetrieveAsync(query, top * 3, ct);
            return await reranker.RerankAsync(query, candidates, top, ct);
        }
    }
}

public sealed record AblationConfig(
    string Name,
    string Description,
    IHybridRetriever Retriever,
    IReranker? Reranker = null);

public sealed record AblationReport(
    int K,
    int LabeledCases,
    IReadOnlyList<AblationRow> Rows)
{
    public string ToSummary()
    {
        if (LabeledCases == 0) return $"Ablation@{K} — no labeled cases";
        var header = $"{"Config",-25} {"HitRate",8} {"Recall",8} {"MRR",8}";
        var sep = new string('-', header.Length);
        var lines = Rows.Select(r =>
            $"{r.Name,-25} {r.HitRateAtK,8:P0} {r.RecallAtK,8:P0} {r.Mrr,8:0.000}");
        return $"Ablation@{K} (n={LabeledCases})\n{sep}\n{header}\n{sep}\n"
               + string.Join("\n", lines);
    }
}

public sealed record AblationRow(
    string Name,
    string Description,
    int Total,
    double HitRateAtK,
    double RecallAtK,
    double Mrr);
