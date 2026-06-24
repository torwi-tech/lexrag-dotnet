using System.Text.RegularExpressions;
using LexRag.Core.Models;

namespace LexRag.Core.Generation;

public static partial class CitationParser
{
    public static IReadOnlyList<Citation> Parse(string answer)
    {
        if (string.IsNullOrEmpty(answer)) return [];

        var seen = new HashSet<(string, int)>();
        var citations = new List<Citation>();
        foreach (Match m in CitationPattern().Matches(answer))
        {
            var file = m.Groups["file"].Value.Trim();
            var idx = int.Parse(m.Groups["idx"].Value);
            if (seen.Add((file, idx)))
                citations.Add(new Citation(file, idx));
        }
        return citations;
    }

    // Fraction of citations whose (file, index) is actually in context; a hallucinated cite drags it below 1.0.
    public static double Correctness(string answer, IReadOnlyList<RetrievedChunk> context)
    {
        var citations = Parse(answer);
        if (citations.Count == 0) return 0;

        var grounded = context.Select(c => (c.SourceFile, c.ChunkIndex)).ToHashSet();
        var ok = citations.Count(c => grounded.Contains((c.SourceFile, c.ChunkIndex)));
        return (double)ok / citations.Count;
    }

    [GeneratedRegex(@"\[\s*Fonte:\s*(?<file>[^,\]]+?)\s*,\s*trecho\s*(?<idx>\d+)\s*\]",
        RegexOptions.IgnoreCase)]
    private static partial Regex CitationPattern();
}
