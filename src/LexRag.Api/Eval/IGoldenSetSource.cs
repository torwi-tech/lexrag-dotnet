using LexRag.Eval;

namespace LexRag.Api.Eval;

public interface IGoldenSetSource
{
    IReadOnlyList<EvalCase> GetCases();
}
