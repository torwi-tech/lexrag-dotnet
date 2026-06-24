namespace LexRag.Eval;

// QueryGroup labels the dataset split so metrics can be reported per group rather than only in aggregate.
// Null means ungrouped (the static curated set has no groups; JurisTCU populates this field).
public sealed record EvalCase(
    string Query,
    bool InDomain,
    IReadOnlyList<string>? ExpectedSourceFiles = null,
    string? QueryGroup = null);

public sealed record EvalCaseResult
{
    public required EvalCase Case { get; init; }
    public required string Answer { get; init; }
    public required bool Grounded { get; init; }
    public required bool Refused { get; init; }
    public required double CitationCorrectness { get; init; }
    public required bool CitedExpectedSource { get; init; }

    // In-domain "passed" means the answer is grounded, correctly cited, and cites an expected source, so a
    // confidently-cited answer over the wrong retrieved context does not count as a pass.
    public bool Passed => Case.InDomain
        ? Grounded && CitationCorrectness >= 1.0 && CitedExpectedSource
        : Refused;
}

public sealed record EvalReport
{
    public required IReadOnlyList<EvalCaseResult> Results { get; init; }

    public int Total => Results.Count;
    public int Passed => Results.Count(r => r.Passed);

    // Faithfulness proxy: grounded + correct citation form only (Layer 1, deterministic). Unlike Passed it does
    // not require CitedExpectedSource; it scores answer/citation quality, not whether retrieval hit the right
    // source (that retrieval dimension lives in Passed and the Retrieval metrics). Semantic faithfulness is Layer 2.
    public double LexicalFaithfulness => Rate(Results.Where(r => r.Case.InDomain),
        r => r.Grounded && r.CitationCorrectness >= 1.0);

    public double CitationCorrectness => Results.Where(r => r.Case.InDomain).ToList() is { Count: > 0 } d
        ? d.Average(r => r.CitationCorrectness)
        : 1.0;

    public double RefusalAccuracy => Rate(Results.Where(r => !r.Case.InDomain), r => r.Refused);

    private static double Rate(IEnumerable<EvalCaseResult> subset, Func<EvalCaseResult, bool> predicate)
    {
        var list = subset.ToList();
        return list.Count == 0 ? 1.0 : (double)list.Count(predicate) / list.Count;
    }

    public string ToSummary() =>
        $"""
        LexRAG eval — {Passed}/{Total} passed
          lexical faithfulness (grounded + citation; Layer 1 proxy): {LexicalFaithfulness:P0}
          citation correctness (mean):             {CitationCorrectness:P0}
          refusal accuracy (out-of-domain "não sei"): {RefusalAccuracy:P0}
        """;
}
