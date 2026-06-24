using FluentAssertions;
using LexRag.Core.Retrieval;
using LexRag.Embeddings;

namespace LexRag.Tests;

public class HashEmbedderTests
{
    private readonly HashEmbedder _embedder = new(dimensions: 1536);

    [Fact]
    public async Task Identical_text_embeds_to_self_similarity_one()
    {
        // Test gate: the vector of a chunk against itself must be ≈ 1.0.
        var v = (await _embedder.EmbedAsync(["prescrição intercorrente execução fiscal"]))[0];
        VectorMath.CosineSimilarity(v, v).Should().BeApproximately(1.0, 1e-6);
    }

    [Fact]
    public async Task Embedding_has_the_configured_dimensionality()
    {
        var v = (await _embedder.EmbedAsync(["qualquer texto"]))[0];
        v.Should().HaveCount(1536);
    }

    [Fact]
    public async Task Lexically_related_texts_are_closer_than_unrelated_ones()
    {
        var baseVec = (await _embedder.EmbedAsync(["prescrição intercorrente execução fiscal"]))[0];
        var related = (await _embedder.EmbedAsync(["execução fiscal e prescrição do crédito"]))[0];
        var unrelated = (await _embedder.EmbedAsync(["futebol campeonato gols do Pelé"]))[0];

        VectorMath.CosineSimilarity(baseVec, related)
            .Should().BeGreaterThan(VectorMath.CosineSimilarity(baseVec, unrelated));
    }

    [Fact]
    public void Deterministic_across_instances()
    {
        // Two separate instances must agree (FNV-1a, not randomised GetHashCode).
        var a = new HashEmbedder().Embed("texto idêntico");
        var b = new HashEmbedder().Embed("texto idêntico");
        a.Should().Equal(b);
    }
}

public class Bm25ScorerTests
{
    [Fact]
    public void Empty_corpus_scores_nothing()
    {
        new Bm25Scorer().Score([], "qualquer", 5).Should().BeEmpty();
    }

    [Fact]
    public void Exact_term_ranks_the_containing_document_first()
    {
        var corpus = new[]
        {
            "prescrição intercorrente execução fiscal artigo 40",
            "feriado local tempestividade do recurso",
            "inteligência artificial no judiciário",
        };

        var top = new Bm25Scorer().Score(corpus, "prescrição fiscal", top: 3);

        top.Should().NotBeEmpty();
        top[0].DocIndex.Should().Be(0);
    }
}
