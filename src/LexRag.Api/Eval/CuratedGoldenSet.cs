using LexRag.Eval;

namespace LexRag.Api.Eval;

// Wraps the hand-curated static golden set (offline, keyless, default).
public sealed class CuratedGoldenSet : IGoldenSetSource
{
    public IReadOnlyList<EvalCase> GetCases() => GoldenSet.Cases;
}
