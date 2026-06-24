namespace LexRag.Core.Models;

public sealed record Citation(string SourceFile, int ChunkIndex)
{
    public string Render() => $"[Fonte: {SourceFile}, trecho {ChunkIndex}]";
}
