using System.Text.RegularExpressions;
using LexRag.Core.Abstractions;
using LexRag.Core.Models;

namespace LexRag.Core.Chunking;

// Packs whole sentences (not a blind char window) so a clause survives intact; overlap
// re-carries a boundary sentence into the next chunk.
public sealed partial class StructuralChunker(RagOptions options) : IChunker
{
    private const int MinChunkChars = 50;

    public IReadOnlyList<string> Chunk(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var units = SplitIntoUnits(text, options.ChunkSize);
        if (units.Count == 0)
            return [];

        var chunks = new List<string>();
        var current = new List<string>();
        var currentLen = 0;

        foreach (var unit in units)
        {
            var unitLen = unit.Length + 1; // +1 for the joining space
            if (currentLen + unitLen > options.ChunkSize && current.Count > 0)
            {
                chunks.Add(string.Join(' ', current));
                current = TailWithin(current, options.ChunkOverlap);
                currentLen = current.Sum(s => s.Length + 1);
            }

            current.Add(unit);
            currentLen += unitLen;
        }

        if (current.Count > 0)
            chunks.Add(string.Join(' ', current));

        return MergeTinyTail(chunks);
    }

    private static List<string> SplitIntoUnits(string text, int cap)
    {
        var normalized = WhitespaceRuns().Replace(text.Trim(), " ");
        var sentences = SentenceBoundary().Split(normalized)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0);

        var units = new List<string>();
        foreach (var sentence in sentences)
        {
            if (sentence.Length <= cap)
            {
                units.Add(sentence);
                continue;
            }

            // Hard-window an oversized unit (unpunctuated dump) on word boundaries so nothing exceeds the cap.
            for (var i = 0; i < sentence.Length;)
            {
                var len = Math.Min(cap, sentence.Length - i);
                var end = i + len;
                if (end < sentence.Length)
                {
                    var lastSpace = sentence.LastIndexOf(' ', end - 1, len);
                    if (lastSpace > i) end = lastSpace;
                }
                units.Add(sentence[i..end].Trim());
                i = end;
            }
        }

        return units;
    }

    private static List<string> TailWithin(List<string> units, int overlapBudget)
    {
        var tail = new List<string>();
        var acc = 0;
        for (var i = units.Count - 1; i >= 0; i--)
        {
            acc += units[i].Length + 1;
            if (acc > overlapBudget) break;
            tail.Insert(0, units[i]);
        }
        return tail;
    }

    private static List<string> MergeTinyTail(List<string> chunks)
    {
        if (chunks.Count >= 2 && chunks[^1].Length < MinChunkChars)
        {
            chunks[^2] = chunks[^2] + " " + chunks[^1];
            chunks.RemoveAt(chunks.Count - 1);
        }
        return chunks;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRuns();

    // Avoid splitting "art. 1º" / "§2º": only break on end punctuation followed by a capital/digit.
    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-ZÀ-Ý0-9])")]
    private static partial Regex SentenceBoundary();
}
