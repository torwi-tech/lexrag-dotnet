using System.Text.RegularExpressions;
using LexRag.Core.Text;

namespace LexRag.Core.Retrieval;

// Pure-C# BM25 is the keyword leg for the no-database path; prod uses Postgres ts_rank over GIN.
public sealed partial class Bm25Scorer(double k1 = 1.5, double b = 0.75)
{
    public IReadOnlyList<(int DocIndex, double Score)> Score(IReadOnlyList<string> corpus, string query, int top)
    {
        if (corpus.Count == 0) return [];

        var docTokens = corpus.Select(Tokenize).ToArray();
        var docLengths = docTokens.Select(t => (double)t.Count).ToArray();
        var avgDocLength = docLengths.Average();
        if (avgDocLength == 0) return [];

        var df = new Dictionary<string, int>();
        foreach (var tokens in docTokens)
            foreach (var term in tokens.Distinct())
                df[term] = df.GetValueOrDefault(term) + 1;

        var queryTerms = Tokenize(query).Distinct().ToArray();
        var n = corpus.Count;

        var scored = new List<(int, double)>(n);
        for (var d = 0; d < n; d++)
        {
            var tf = docTokens[d].GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
            double score = 0;
            foreach (var term in queryTerms)
            {
                if (!tf.TryGetValue(term, out var freq)) continue;
                var idf = Math.Log(1 + (n - df[term] + 0.5) / (df[term] + 0.5));
                var denom = freq + k1 * (1 - b + b * docLengths[d] / avgDocLength);
                score += idf * (freq * (k1 + 1)) / denom;
            }

            if (score > 0) scored.Add((d, score));
        }

        return scored.OrderByDescending(s => s.Item2).Take(top).ToList();
    }

    // Fold diacritics before lowercasing so "execução" == "execucao", matching Postgres portuguese config.
    private static List<string> Tokenize(string text) =>
        TokenPattern().Matches(DiacriticFolder.Fold(text).ToLowerInvariant()).Select(m => m.Value).ToList();

    [GeneratedRegex(@"[\p{L}\p{Nd}]+")]
    private static partial Regex TokenPattern();
}
