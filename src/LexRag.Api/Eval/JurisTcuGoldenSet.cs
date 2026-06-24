using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using LexRag.Eval;

namespace LexRag.Api.Eval;

// Builds EvalCases from JurisTCU query.csv + qrel.csv.
// DOC_IDs with SCORE >= relevanceCutoff are treated as ground-truth; others are noise.
public sealed class JurisTcuGoldenSet(
    string path,
    int relevanceCutoff,
    int maxQueries,
    ILogger? logger = null) : IGoldenSetSource
{
    private IReadOnlyList<EvalCase>? _cases;

    public IReadOnlyList<EvalCase> GetCases()
    {
        // Lazy-load once; safe because DI registers as singleton.
        return _cases ??= Load();
    }

    private IReadOnlyList<EvalCase> Load()
    {
        var queryFile = Path.Combine(path, "query.csv");
        var qrelFile = Path.Combine(path, "qrel.csv");

        if (!File.Exists(queryFile) || !File.Exists(qrelFile))
        {
            logger?.LogWarning(
                "JurisTCU query.csv or qrel.csv not found at {Path} — golden set returns empty.", path);
            return [];
        }

        var queries = LoadQueries(queryFile);
        var relevantDocs = LoadRelevantDocs(qrelFile);

        var cases = new List<EvalCase>();
        foreach (var (id, text, group) in queries)
        {
            if (maxQueries > 0 && cases.Count >= maxQueries) break;

            if (!relevantDocs.TryGetValue(id, out var docIds) || docIds.Count == 0) continue;

            cases.Add(new EvalCase(text, InDomain: true, ExpectedSourceFiles: docIds, QueryGroup: group));
        }

        logger?.LogInformation("JurisTCU golden set: {Count} eval cases loaded from {Path}.", cases.Count, path);
        return cases;
    }

    // Returns (Id, Text, QueryGroup). The group comes from the SOURCE column, which JurisTCU uses to mark each
    // query's provenance: "search log" (real user keywords), "expression from LLM question" (synthetic keywords),
    // "LLM" (synthetic questions). If SOURCE is absent it falls back to the 1-based position, since the dataset
    // ships the 150 queries in three contiguous blocks of 50 (arXiv:2503.08379).
    private static List<(string Id, string Text, string Group)> LoadQueries(string filePath)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true, BadDataFound = null };
        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, config);

        csv.Read();
        csv.ReadHeader();
        var hasSource = csv.HeaderRecord?.Contains("SOURCE", StringComparer.OrdinalIgnoreCase) ?? false;

        var result = new List<(string, string, string)>();
        var position = 0;
        while (csv.Read())
        {
            position++;
            var id = csv.GetField("ID");
            var text = csv.GetField("TEXT");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(text)) continue;

            var group = hasSource ? GroupFromSource(csv.GetField("SOURCE"), position) : InferGroup(position);
            result.Add((id!, text!, group));
        }
        return result;
    }

    private static string GroupFromSource(string? source, int position) => source?.Trim().ToLowerInvariant() switch
    {
        "search log" => "real-keyword",
        "expression from llm question" => "synthetic-keyword",
        "llm" => "synthetic-question",
        _ => InferGroup(position),
    };

    // Fallback: the dataset ships 150 queries in three contiguous blocks of 50.
    private static string InferGroup(int position) => position switch
    {
        <= 50 => "real-keyword",
        <= 100 => "synthetic-keyword",
        _ => "synthetic-question",
    };

    private Dictionary<string, IReadOnlyList<string>> LoadRelevantDocs(string filePath)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true, BadDataFound = null };
        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, config);

        var grouped = new Dictionary<string, List<string>>();
        csv.Read();
        csv.ReadHeader();
        while (csv.Read())
        {
            var queryId = csv.GetField("QUERY_ID");
            var docId = csv.GetField("DOC_ID");
            var scoreStr = csv.GetField("SCORE");

            if (string.IsNullOrWhiteSpace(queryId) || string.IsNullOrWhiteSpace(docId)) continue;
            if (!int.TryParse(scoreStr, out var score)) continue;
            if (score < relevanceCutoff) continue;

            if (!grouped.TryGetValue(queryId!, out var list))
            {
                list = [];
                grouped[queryId!] = list;
            }
            list.Add(docId!);
        }

        return grouped.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value);
    }
}
