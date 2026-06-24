using LexRag.Core.Abstractions;
using LexRag.Core.Models;
using LexRag.Core.Text;

namespace LexRag.Core.Retrieval;

// Key-free stand-in for an LLM grader so the CRAG loop is exercised without a model.
public sealed class LexicalRetrievalGrader(RagOptions options) : IRetrievalGrader
{
    public Task<RetrievalGrade> GradeAsync(string query, IReadOnlyList<RetrievedChunk> context, CancellationToken ct = default)
    {
        var terms = ContentTerms.Extract(query);
        if (terms.Count == 0 || context.Count == 0)
            return Task.FromResult(new RetrievalGrade(false, 0, "sem termos de busca ou sem contexto"));

        var haystack = string.Join(' ', context.Select(c => c.Content)).ToLowerInvariant();
        var covered = terms.Count(haystack.Contains);
        var coverage = (double)covered / terms.Count;
        var sufficient = coverage >= options.GradeSufficiencyThreshold;

        return Task.FromResult(new RetrievalGrade(
            sufficient, Math.Round(coverage, 4),
            $"cobertura {coverage:P0} ({covered}/{terms.Count} termos) vs limiar {options.GradeSufficiencyThreshold:P0}"));
    }
}

// Key-free stand-in for an LLM query-rewriter: collapse the ask to its content terms, shifting the BM25/RRF ranking.
public sealed class KeywordQueryReformulator : IQueryReformulator
{
    public Task<string> ReformulateAsync(string query, RetrievalGrade grade, CancellationToken ct = default)
    {
        var terms = ContentTerms.Extract(query);
        var reformulated = terms.Count > 0 ? string.Join(' ', terms) : query;
        return Task.FromResult(reformulated);
    }
}
