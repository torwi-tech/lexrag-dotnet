using LexRag.Core.Abstractions;
using LexRag.Core.Generation;
using LexRag.Core.Models;

namespace LexRag.Eval;

public sealed class RagEvaluator(IRagPipeline pipeline)
{
    public async Task<EvalReport> RunAsync(IReadOnlyList<EvalCase> cases, CancellationToken ct = default)
    {
        var results = new List<EvalCaseResult>(cases.Count);
        foreach (var c in cases)
        {
            var answer = await pipeline.AskAsync(c.Query, ct);
            var refused = !answer.Grounded
                && answer.Text.Contains(RagAnswer.NoAnswer, StringComparison.OrdinalIgnoreCase);
            var citationCorrectness = answer.Grounded
                ? CitationParser.Correctness(answer.Text, answer.Context)
                : 0.0;
            var citedExpected = c.ExpectedSourceFiles is not { Count: > 0 }
                || c.ExpectedSourceFiles.Any(exp => answer.Citations.Any(cit =>
                    cit.SourceFile.Equals(exp, StringComparison.OrdinalIgnoreCase)));

            results.Add(new EvalCaseResult
            {
                Case = c,
                Answer = answer.Text,
                Grounded = answer.Grounded,
                Refused = refused,
                CitationCorrectness = citationCorrectness,
                CitedExpectedSource = citedExpected,
            });
        }

        return new EvalReport { Results = results };
    }
}
