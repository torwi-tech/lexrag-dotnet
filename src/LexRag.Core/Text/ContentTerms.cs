using System.Text.RegularExpressions;

namespace LexRag.Core.Text;

// Shared so the CRAG grader and reformulator judge relevance with the same notion of "content word".
public static partial class ContentTerms
{
    private static readonly HashSet<string> Stopwords =
    [
        "qual", "quais", "como", "onde", "quando", "porque", "por", "para", "pelo", "pela",
        "dos", "das", "que", "com", "uma", "uns", "umas", "sobre", "the", "and", "what",
        "when", "where", "favor", "gostaria", "saber", "preciso", "poderia", "sobre",
    ];

    // Accents are kept here so the reformulated query preserves real terms; accent-insensitive
    // matching is the BM25 leg's job (Bm25Scorer folds), not the grader/reformulator's.
    public static IReadOnlyList<string> Extract(string text) =>
        TokenPattern().Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(t => t.Length > 2 && !Stopwords.Contains(t))
            .Distinct()
            .ToList();

    [GeneratedRegex(@"[\p{L}\p{Nd}-]+")]
    private static partial Regex TokenPattern();
}
