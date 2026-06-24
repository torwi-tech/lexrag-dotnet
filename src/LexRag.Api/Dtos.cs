using LexRag.Core.Models;

namespace LexRag.Api;

public sealed record AskRequest(string Query);
public sealed record IngestTextRequest(string SourceFile, string Content);

public sealed record CitationDto(string SourceFile, int ChunkIndex);
public sealed record ContextDto(string SourceFile, int ChunkIndex, double Score, string Preview);

public sealed record StageTimingDto(double RetrieveMs, double RerankMs, double GenerateMs, double TotalMs);

public sealed record AskResponse(
    string Answer,
    bool Grounded,
    IReadOnlyList<CitationDto> Citations,
    IReadOnlyList<ContextDto> Context,
    IReadOnlyList<string> Trace,
    StageTimingDto? Timing = null)
{
    public static AskResponse From(RagAnswer a) => new(
        a.Text,
        a.Grounded,
        a.Citations.Select(c => new CitationDto(c.SourceFile, c.ChunkIndex)).ToList(),
        a.Context.Select(c => new ContextDto(
            c.SourceFile, c.ChunkIndex, Math.Round(c.Score, 4),
            c.Content.Length <= 160 ? c.Content : c.Content[..160] + "…")).ToList(),
        a.Trace,
        a.Timing is { } t ? new StageTimingDto(t.RetrieveMs, t.RerankMs, t.GenerateMs, t.TotalMs) : null);
}
