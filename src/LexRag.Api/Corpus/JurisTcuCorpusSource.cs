using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;

namespace LexRag.Api.Corpus;

// Streams acordão enunciados from the JurisTCU doc.csv.
// ENUNCIADO is the canonical holding text and indexes better than EXCERTO, which is longer but less structured.
// Strip HTML because the CSV ships with <b>, <s>, </p> markup from the source system.
public sealed partial class JurisTcuCorpusSource(
    string path,
    int maxDocuments,
    ILogger? logger = null) : ICorpusSource
{
    public async IAsyncEnumerable<(string Name, string Text)> GetDocumentsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var docCsv = Path.Combine(path, "doc.csv");
        if (!File.Exists(docCsv))
        {
            logger?.LogWarning("JurisTCU doc.csv not found at {Path} — corpus source returns empty.", docCsv);
            yield break;
        }

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null, // tolerate malformed rows rather than throwing
        };

        using var reader = new StreamReader(docCsv);
        using var csv = new CsvReader(reader, config);

        await csv.ReadAsync();
        csv.ReadHeader();

        var yielded = 0;
        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();

            var key = csv.GetField("KEY");
            var enunciado = csv.GetField("ENUNCIADO");

            if (string.IsNullOrWhiteSpace(enunciado)) continue;

            var text = StripHtml(enunciado);
            if (string.IsNullOrWhiteSpace(text)) continue;

            yield return (key!, text);
            yielded++;

            if (maxDocuments > 0 && yielded >= maxDocuments) break;
        }

        logger?.LogInformation("JurisTCU corpus: yielded {Count} documents from {Path}.", yielded, docCsv);
    }

    private static string StripHtml(string input)
    {
        var stripped = HtmlTagPattern().Replace(input, " ");
        // decode common HTML entities that appear in the JurisTCU export
        stripped = stripped
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&apos;", "'")
            .Replace("&nbsp;", " ");
        // collapse runs of whitespace introduced by tag removal
        return WhitespacePattern().Replace(stripped, " ").Trim();
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"\s{2,}", RegexOptions.Compiled)]
    private static partial Regex WhitespacePattern();
}
