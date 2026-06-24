using FluentAssertions;
using LexRag.Core.Retrieval;

namespace LexRag.Tests;

public class ReciprocalRankFusionTests
{
    [Fact]
    public void Item_ranked_high_in_both_lists_wins()
    {
        var listA = new[] { "a", "b", "c" };
        var listB = new[] { "a", "c", "b" };

        var fused = ReciprocalRankFusion.Fuse([listA, listB], x => x, k: 60);

        fused[0].Item.Should().Be("a"); // rank 1 in both legs
    }

    [Fact]
    public void Score_is_sum_of_reciprocal_ranks()
    {
        var listA = new[] { "a", "b" };
        var listB = new[] { "a", "b" };

        var fused = ReciprocalRankFusion.Fuse([listA, listB], x => x, k: 60);

        // a is rank 1 in both: 1/(60+1) + 1/(60+1)
        fused.Single(f => f.Item == "a").Score.Should().BeApproximately(2.0 / 61, 1e-9);
        // b is rank 2 in both: 1/(60+2) + 1/(60+2)
        fused.Single(f => f.Item == "b").Score.Should().BeApproximately(2.0 / 62, 1e-9);
    }

    [Fact]
    public void Top_limit_is_respected()
    {
        var list = new[] { "a", "b", "c", "d", "e" };
        var fused = ReciprocalRankFusion.Fuse([list], x => x, k: 60, top: 2);
        fused.Should().HaveCount(2);
    }

    [Fact]
    public void Fusion_dedups_items_across_lists()
    {
        var listA = new[] { "a", "b" };
        var listB = new[] { "b", "a" };
        var fused = ReciprocalRankFusion.Fuse([listA, listB], x => x);
        fused.Should().HaveCount(2); // a and b, not 4
    }

    [Fact]
    public void Non_positive_k_is_rejected()
    {
        var act = () => ReciprocalRankFusion.Fuse([new[] { "a" }], x => x, k: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
