using LexRag.Core.Abstractions;

namespace LexRag.Ingestion;

public sealed class ExtensionTextExtractorRouter(
    PdfTextExtractor pdf,
    PlainTextExtractor plain) : ITextExtractorRouter
{
    public ITextExtractor Resolve(string sourceFile) =>
        Path.GetExtension(sourceFile).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            ? pdf
            : plain;
}
