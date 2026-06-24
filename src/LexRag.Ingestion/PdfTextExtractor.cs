using LexRag.Core.Abstractions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace LexRag.Ingestion;

// Content-order extraction: PDFs store glyphs in paint order, so naive concat scrambles multi-column text.
public sealed class PdfTextExtractor : ITextExtractor
{
    public async Task<string> ExtractTextAsync(Stream content, CancellationToken ct = default)
    {
        // PdfPig needs a seekable buffer; copy the (possibly network) stream first.
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        return await Task.Run(() =>
        {
            using var doc = PdfDocument.Open(bytes);
            var pages = doc.GetPages()
                .Select(page => ContentOrderTextExtractor.GetText(page))
                .Where(t => !string.IsNullOrWhiteSpace(t));
            return string.Join("\n\n", pages);
        }, ct);
    }
}
