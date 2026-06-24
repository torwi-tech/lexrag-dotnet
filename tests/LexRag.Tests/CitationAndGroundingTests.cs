using FluentAssertions;
using LexRag.Core.Generation;
using LexRag.Core.Models;

namespace LexRag.Tests;

public class CitationParserTests
{
    [Fact]
    public void Parses_and_dedups_citations()
    {
        const string answer = "Texto [Fonte: acordao.pdf, trecho 3] e mais [Fonte: acordao.pdf, trecho 3] " +
                              "e ainda [Fonte: nota.txt, trecho 0].";
        var citations = CitationParser.Parse(answer);

        citations.Should().HaveCount(2);
        citations.Should().Contain(new Citation("acordao.pdf", 3));
        citations.Should().Contain(new Citation("nota.txt", 0));
    }

    [Fact]
    public void Correctness_is_one_when_every_citation_is_in_context()
    {
        var context = new[] { Chunk("acordao.pdf", 3), Chunk("nota.txt", 0) };
        var answer = "Resposta [Fonte: acordao.pdf, trecho 3].";
        CitationParser.Correctness(answer, context).Should().Be(1.0);
    }

    [Fact]
    public void Correctness_drops_when_a_citation_is_hallucinated()
    {
        var context = new[] { Chunk("acordao.pdf", 3) };
        // One real, one invented (trecho 99 not in context).
        var answer = "X [Fonte: acordao.pdf, trecho 3] Y [Fonte: acordao.pdf, trecho 99].";
        CitationParser.Correctness(answer, context).Should().Be(0.5);
    }

    private static RetrievedChunk Chunk(string file, int idx) => new()
    {
        ChunkKey = Guid.NewGuid(), SourceFile = file, ChunkIndex = idx, Content = "...", Score = 1.0,
    };
}

public class GroundingPromptTests
{
    [Fact]
    public void Refuses_when_context_is_empty()
    {
        GroundingPrompt.ShouldRefuse([], minScore: 0.0).Should().BeTrue();
    }

    [Fact]
    public void Does_not_refuse_when_a_chunk_clears_the_threshold()
    {
        var context = new[] { new RetrievedChunk
        {
            ChunkKey = Guid.NewGuid(), SourceFile = "f", ChunkIndex = 0, Content = "x", Score = 0.5,
        }};
        GroundingPrompt.ShouldRefuse(context, minScore: 0.1).Should().BeFalse();
    }

    [Fact]
    public void Context_block_renders_citable_source_markers()
    {
        var context = new[] { new RetrievedChunk
        {
            ChunkKey = Guid.NewGuid(), SourceFile = "acordao.pdf", ChunkIndex = 2, Content = "conteúdo", Score = 1,
        }};
        GroundingPrompt.BuildContextBlock(context).Should().Contain("[Fonte: acordao.pdf, trecho 2]");
    }
}
