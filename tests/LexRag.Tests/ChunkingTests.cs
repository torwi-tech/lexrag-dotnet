using FluentAssertions;
using LexRag.Core.Chunking;
using LexRag.Core.Models;

namespace LexRag.Tests;

public class ChunkingTests
{
    private static StructuralChunker Chunker(int size, int overlap) =>
        new(new RagOptions { ChunkSize = size, ChunkOverlap = overlap });

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t  \n")]
    public void Whitespace_yields_no_chunks(string text) =>
        Chunker(200, 50).Chunk(text).Should().BeEmpty();

    [Fact]
    public void Text_under_chunk_size_is_a_single_chunk()
    {
        var chunks = Chunker(2000, 200).Chunk("Um parágrafo curto sobre prescrição intercorrente.");
        chunks.Should().ContainSingle();
    }

    [Fact]
    public void Long_text_splits_into_multiple_bounded_chunks()
    {
        var text = string.Join(" ",
            Enumerable.Range(1, 40).Select(i => $"Frase numero {i} sobre o caso concreto."));
        var chunks = Chunker(200, 80).Chunk(text);

        chunks.Count.Should().BeGreaterThan(1);
        // Bounded: never wildly larger than the budget (overlap + one tipping sentence).
        chunks.Should().OnlyContain(c => c.Length <= 200 + 80 + 60);
    }

    [Fact]
    public void Overlap_carries_a_boundary_sentence_into_the_next_chunk()
    {
        var text = string.Join(" ",
            Enumerable.Range(1, 40).Select(i => $"Frase numero {i} sobre o caso concreto."));
        var chunks = Chunker(200, 80).Chunk(text);

        // The next chunk starts with sentences carried from the tail of the previous one.
        var firstSentenceOfSecond = chunks[1].Split('.')[0].Trim();
        chunks[0].Should().Contain(firstSentenceOfSecond);
    }

    [Fact]
    public void No_content_is_lost_across_chunks()
    {
        var text = string.Join(" ",
            Enumerable.Range(1, 40).Select(i => $"Frase numero {i} sobre o caso concreto."));
        var joined = string.Join(" ", Chunker(200, 80).Chunk(text));

        foreach (var i in Enumerable.Range(1, 40))
            joined.Should().Contain($"numero {i} ");
    }
}
