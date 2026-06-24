using FsCheck;
using FsCheck.Xunit;
using LexRag.Core.Chunking;
using LexRag.Core.Models;
using LexRag.Core.Retrieval;

namespace LexRag.Tests;

public class RetrievalProperties
{
    // RRF sums 1/(k+rank) per list, so the fused score cannot depend on the order the legs are passed in.
    [Property]
    public bool Rrf_score_is_invariant_to_input_list_order(int[]? a, int[]? b)
    {
        var la = (a ?? []).Distinct().ToArray();
        var lb = (b ?? []).Distinct().ToArray();
        IReadOnlyList<IReadOnlyList<int>> ab = new[] { la, lb };
        IReadOnlyList<IReadOnlyList<int>> ba = new[] { lb, la };

        var f1 = ReciprocalRankFusion.Fuse(ab, x => x).ToDictionary(r => r.Item, r => Math.Round(r.Score, 9));
        var f2 = ReciprocalRankFusion.Fuse(ba, x => x).ToDictionary(r => r.Item, r => Math.Round(r.Score, 9));

        return f1.Count == f2.Count && f1.All(kv => f2.TryGetValue(kv.Key, out var s) && s == kv.Value);
    }

    [Property]
    public bool Chunking_never_produces_an_empty_chunk(NonNull<string> text)
    {
        var chunks = new StructuralChunker(new RagOptions()).Chunk(text.Get);
        return chunks.All(c => c.Length > 0);
    }
}
