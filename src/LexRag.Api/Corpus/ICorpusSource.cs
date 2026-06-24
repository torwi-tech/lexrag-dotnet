namespace LexRag.Api.Corpus;

public interface ICorpusSource
{
    IAsyncEnumerable<(string Name, string Text)> GetDocumentsAsync(CancellationToken ct = default);
}
