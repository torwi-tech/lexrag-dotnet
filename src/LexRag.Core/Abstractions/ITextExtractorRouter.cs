namespace LexRag.Core.Abstractions;

public interface ITextExtractorRouter
{
    ITextExtractor Resolve(string sourceFile);
}
