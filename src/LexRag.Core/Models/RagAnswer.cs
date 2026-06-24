namespace LexRag.Core.Models;

public sealed record RagAnswer
{
    public required string Text { get; init; }
    // False when the system declined to answer because nothing relevant was retrieved.
    public required bool Grounded { get; init; }
    public IReadOnlyList<Citation> Citations { get; init; } = [];
    public IReadOnlyList<RetrievedChunk> Context { get; init; } = [];

    public IReadOnlyList<string> Trace { get; init; } = [];

    // Wall-clock per stage (ms). Null when the measurement was not captured (e.g. older call sites).
    public StageTiming? Timing { get; init; }

    public const string NoAnswer = "Não encontrei nos documentos fornecidos.";

    public static RagAnswer Refusal(IReadOnlyList<RetrievedChunk> context) => new()
    {
        Text = NoAnswer,
        Grounded = false,
        Citations = [],
        Context = context,
    };
}

// Wall-clock milliseconds per pipeline stage, measured with Stopwatch.
public sealed record StageTiming(double RetrieveMs, double RerankMs, double GenerateMs)
{
    public double TotalMs => RetrieveMs + RerankMs + GenerateMs;
}
