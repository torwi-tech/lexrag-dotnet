using LexRag.Core.Abstractions;

namespace LexRag.Eval;

// Fourth eval dimension: how much does retrieval quality drop when queries are perturbed?
// All perturbations are deterministic (no LLM); the harness measures recall delta per perturbation type
// so the review committee can see whether RRF/reranking provide robustness, not just peak recall.
public sealed class PerturbationEvaluator(IHybridRetriever retriever)
{
    // Run once with original queries, then once per perturbation type.
    // Returns a result per perturbation type with the hit-rate delta vs the original.
    public async Task<PerturbationReport> RunAsync(
        IReadOnlyList<EvalCase> cases,
        int k,
        CancellationToken ct = default)
    {
        var labeled = cases
            .Where(c => c is { InDomain: true, ExpectedSourceFiles: { Count: > 0 } })
            .ToList();

        if (labeled.Count == 0)
            return new PerturbationReport(k, 0, [], 0.0);

        var baselineHit = await HitRateAsync(labeled, q => q, k, ct);

        var perturbations = new List<PerturbationResult>();
        foreach (var (name, transform) in Perturbations)
        {
            var perturbedHit = await HitRateAsync(labeled, transform, k, ct);
            perturbations.Add(new PerturbationResult(name, perturbedHit, perturbedHit - baselineHit));
        }

        return new PerturbationReport(k, labeled.Count, perturbations, baselineHit);
    }

    // Deterministic perturbation types; each transform is a pure string function.
    private static readonly IReadOnlyList<(string Name, Func<string, string> Transform)> Perturbations =
        new List<(string, Func<string, string>)>
        {
            // Whitespace noise: inject a double-space after tokens at positions 0, 3, 6, ... (i % 3 == 0).
            // A retriever that normalises whitespace before tokenisation should be unaffected.
            ("whitespace-noise", WhitespaceNoise),

            // All-lowercase: BM25 and the hash embedder both lowercase internally, but tests that the
            // pipeline does not break when the casing guard is removed at the query layer.
            ("lowercase", q => q.ToLowerInvariant()),

            // All-uppercase: opposite extreme; verifies casing robustness the other way.
            ("uppercase", q => q.ToUpperInvariant()),

            // Stopword reinsertion: prepend a common Portuguese filler so term positions shift.
            // Deterministic: same filler for every query.
            ("stopword-prefix", q => "de acordo com a legislação vigente " + q),

            // Term reversal: reverse the word order of the query without changing the word set.
            // A bag-of-words retriever (BM25) should be unaffected; a positional one may degrade.
            ("term-reversal", TermReversal),
        };

    private static string WhitespaceNoise(string q)
    {
        var tokens = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<string>(tokens.Length * 2);
        for (var i = 0; i < tokens.Length; i++)
        {
            parts.Add(tokens[i]);
            if (i % 3 == 0 && i < tokens.Length - 1)
                parts.Add(" "); // extra space injected between every 3rd pair
        }
        return string.Join(" ", parts);
    }

    private static string TermReversal(string q)
    {
        var tokens = q.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        Array.Reverse(tokens);
        return string.Join(" ", tokens);
    }

    private async Task<double> HitRateAsync(
        IReadOnlyList<EvalCase> cases,
        Func<string, string> transform,
        int k,
        CancellationToken ct)
    {
        var hits = 0;
        foreach (var c in cases)
        {
            var results = await retriever.RetrieveAsync(transform(c.Query), k, ct);
            var found = results.Any(r => c.ExpectedSourceFiles!.Any(exp =>
                r.SourceFile.Equals(exp, StringComparison.OrdinalIgnoreCase)));
            if (found) hits++;
        }
        return cases.Count == 0 ? 0.0 : (double)hits / cases.Count;
    }
}

public sealed record PerturbationReport(
    int K,
    int Total,
    IReadOnlyList<PerturbationResult> Results,
    double BaselineHitRate)
{
    public string ToSummary()
    {
        if (Total == 0) return $"Perturbation@{K} — no labeled cases";
        var lines = Results.Select(r =>
            $"  {r.PerturbationType,-20}: hit-rate {r.HitRate:P0}  delta {r.DeltaVsBaseline:+0.00;-0.00;0.00}");
        return $"Perturbation@{K} — baseline hit-rate {BaselineHitRate:P0} (n={Total})\n"
               + string.Join("\n", lines);
    }
}

public sealed record PerturbationResult(
    string PerturbationType,
    double HitRate,
    double DeltaVsBaseline);
