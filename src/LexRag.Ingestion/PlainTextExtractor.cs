using System.Text;
using LexRag.Core.Abstractions;

namespace LexRag.Ingestion;

public sealed class PlainTextExtractor : ITextExtractor
{
    public async Task<string> ExtractTextAsync(Stream content, CancellationToken ct = default)
    {
        using var reader = new StreamReader(content, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }
}
