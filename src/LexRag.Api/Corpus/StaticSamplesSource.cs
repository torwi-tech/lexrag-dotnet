namespace LexRag.Api.Corpus;

// Wraps the original samples/*.txt behaviour: offline reads from the default path, no document keys.
public sealed class StaticSamplesSource(ILogger? logger = null) : ICorpusSource
{
    public async IAsyncEnumerable<(string Name, string Text)> GetDocumentsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var samplesDir = Path.Combine(AppContext.BaseDirectory, "samples");
        if (!Directory.Exists(samplesDir))
        {
            logger?.LogWarning("Samples directory not found at {Dir} — corpus source returns empty.", samplesDir);
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(samplesDir, "*.txt"))
        {
            ct.ThrowIfCancellationRequested();
            var text = await File.ReadAllTextAsync(file, ct);
            yield return (Path.GetFileName(file), text);
        }
    }
}
