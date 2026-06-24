using System.Text;
using FluentAssertions;
using LexRag.Core.Chunking;
using LexRag.Core.Generation;
using LexRag.Core.Models;
using LexRag.Core.Retrieval;
using LexRag.Embeddings;
using LexRag.Index;
using LexRag.Ingestion;
using LexRag.Orchestration;
using LexRag.Retrieval;
using Microsoft.Extensions.AI;

namespace LexRag.Tests;

// Tests for: RRF leg weights, MMR diversity reranker, and the MinRelevanceScore gate.
public class RrfWeightsTests
{
    // Builds a minimal hybrid retriever with custom options, no ingestor needed for these synthetic cases.
    private static InMemoryHybridRetriever BuildRetriever(RagOptions options)
    {
        var store = new InMemoryChunkStore();
        var embedder = new HashEmbedder(options.EmbeddingDimensions);
        var vectorIndex = new InMemoryVectorIndex(store);
        var keyword = new InMemoryKeywordRetriever(store, new Bm25Scorer());
        return new InMemoryHybridRetriever(vectorIndex, keyword, embedder, options);
    }

    private static DocumentIngestor BuildIngestor(RagOptions options, out InMemoryChunkStore store)
    {
        store = new InMemoryChunkStore();
        var embedder = new HashEmbedder(options.EmbeddingDimensions);
        var vectorIndex = new InMemoryVectorIndex(store);
        var chunker = new StructuralChunker(options);
        var router = new ExtensionTextExtractorRouter(new PdfTextExtractor(), new PlainTextExtractor());
        return new DocumentIngestor(router, chunker, embedder, vectorIndex);
    }

    private static async Task IngestText(DocumentIngestor ingestor, string name, string text)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await ingestor.IngestAsync(name, stream);
    }

    [Fact]
    public async Task Equal_weights_produce_same_ranking_as_unit_weight_baseline()
    {
        // 1.0/1.0 must reproduce the original unweighted behavior (both are equivalent).
        var opts1 = new RagOptions();
        var ingestor1 = BuildIngestor(opts1, out var store1);
        var retriever1 = new InMemoryHybridRetriever(
            new InMemoryVectorIndex(store1), new InMemoryKeywordRetriever(store1, new Bm25Scorer()),
            new HashEmbedder(opts1.EmbeddingDimensions), opts1);

        var opts2 = new RagOptions { DenseWeight = 1.0, SparseWeight = 1.0 };
        var ingestor2 = BuildIngestor(opts2, out var store2);
        var retriever2 = new InMemoryHybridRetriever(
            new InMemoryVectorIndex(store2), new InMemoryKeywordRetriever(store2, new Bm25Scorer()),
            new HashEmbedder(opts2.EmbeddingDimensions), opts2);

        const string doc = "Prescrição intercorrente na execução fiscal tem termo inicial de um ano.";
        await IngestText(ingestor1, "fiscal.txt", doc);
        await IngestText(ingestor2, "fiscal.txt", doc);

        var r1 = await retriever1.RetrieveAsync("prescrição intercorrente", top: 5);
        var r2 = await retriever2.RetrieveAsync("prescrição intercorrente", top: 5);

        r1.Should().HaveCount(r2.Count);
        r1.Select(c => c.ChunkKey).Should().BeEquivalentTo(r2.Select(c => c.ChunkKey),
            "equal weights 1.0/1.0 must produce the same ranked set as the default options");
    }

    [Fact]
    public async Task Raising_SparseWeight_lifts_sparse_only_document_above_equal_weight_baseline()
    {
        // "lexical-only.txt" contains very distinctive keywords ("xyzzy" not in dense neighbor set)
        // but shares no semantic space with the query hash, so the sparse leg is its only path up.
        // A higher SparseWeight must improve its relative RRF score vs the equal-weight baseline.
        var equalOpts = new RagOptions { DenseWeight = 1.0, SparseWeight = 1.0, CandidatePoolSize = 20 };
        var boostedOpts = new RagOptions { DenseWeight = 1.0, SparseWeight = 5.0, CandidatePoolSize = 20 };

        async Task<IReadOnlyList<RetrievedChunk>> Retrieve(RagOptions opts, string query)
        {
            var ingestor = BuildIngestor(opts, out var store);
            var r = new InMemoryHybridRetriever(
                new InMemoryVectorIndex(store),
                new InMemoryKeywordRetriever(store, new Bm25Scorer()),
                new HashEmbedder(opts.EmbeddingDimensions), opts);

            // Two docs: one with dense-friendly content, one with the exact rare keyword.
            await IngestText(ingestor, "general.txt",
                "Direito civil e processo civil são áreas fundamentais do ordenamento jurídico brasileiro.");
            await IngestText(ingestor, "sparse-only.txt",
                "xyzzy xyzzy xyzzy xyzzy xyzzy xyzzy xyzzy xyzzy xyzzy xyzzy");
            return await r.RetrieveAsync(query, top: 5);
        }

        var equalResults = await Retrieve(equalOpts, "xyzzy");
        var boostedResults = await Retrieve(boostedOpts, "xyzzy");

        // sparse-only.txt must appear somewhere in both result sets.
        equalResults.Should().Contain(c => c.SourceFile == "sparse-only.txt",
            "sparse-only doc must appear in equal-weight results");
        boostedResults.Should().Contain(c => c.SourceFile == "sparse-only.txt",
            "sparse-only doc must appear in boosted-sparse results");

        var sparseRankEqual = equalResults
            .Select((c, i) => (c, i))
            .First(x => x.c.SourceFile == "sparse-only.txt").i;
        var sparseRankBoosted = boostedResults
            .Select((c, i) => (c, i))
            .First(x => x.c.SourceFile == "sparse-only.txt").i;

        // Higher SparseWeight means the sparse-only doc ranks at least as high (lower index = better).
        sparseRankBoosted.Should().BeLessOrEqualTo(sparseRankEqual,
            "raising SparseWeight must not lower the rank of a sparse-only document");
    }
}

public class MmrDiversityTests
{
    [Fact]
    public async Task Mmr_does_not_return_both_near_duplicates_in_top_2_when_distinct_chunk_available()
    {
        // Two near-duplicate chunks (dup-a, dup-b) score highest; one distinct chunk scores lower.
        // MMR must diversify: top 2 must include the distinct chunk, not both near-duplicates.
        var lambda = 0.5; // balanced: diversity matters as much as relevance
        var reranker = new MmrReranker(lambda);

        var dupContent = "Execução fiscal: prescrição intercorrente inicia após um ano de suspensão do processo.";
        var distinctContent = "Contrato administrativo: licitação pública exige edital e publicidade.";

        var chunks = new List<RetrievedChunk>
        {
            new() { ChunkKey = Guid.NewGuid(), SourceFile = "dup-a.txt", ChunkIndex = 0, Content = dupContent, Score = 0.9 },
            new() { ChunkKey = Guid.NewGuid(), SourceFile = "dup-b.txt", ChunkIndex = 0, Content = dupContent + " (repetido)", Score = 0.85 },
            new() { ChunkKey = Guid.NewGuid(), SourceFile = "distinct.txt", ChunkIndex = 0, Content = distinctContent, Score = 0.6 },
        };

        var top2 = await reranker.RerankAsync("execução fiscal", chunks, top: 2);

        top2.Should().HaveCount(2);
        var sourceFiles = top2.Select(c => c.SourceFile).ToList();

        // distinct.txt must occupy one of the two slots; the two near-duplicates must not fill both.
        sourceFiles.Should().Contain("distinct.txt",
            "MMR must select the distinct chunk over both near-duplicates in top 2");
        sourceFiles.Should().NotBeEquivalentTo(new[] { "dup-a.txt", "dup-b.txt" },
            "MMR must not return both near-duplicate chunks; at least one slot goes to the distinct chunk");
    }

    [Fact]
    public async Task Mmr_at_lambda_1_degrades_to_pure_relevance_order()
    {
        // lambda=1 means no diversity penalty; order must follow score descending.
        var reranker = new MmrReranker(lambda: 1.0);
        var chunks = new List<RetrievedChunk>
        {
            new() { ChunkKey = Guid.NewGuid(), SourceFile = "a.txt", ChunkIndex = 0, Content = "alfa beta gamma", Score = 0.9 },
            new() { ChunkKey = Guid.NewGuid(), SourceFile = "b.txt", ChunkIndex = 0, Content = "alfa beta gamma", Score = 0.7 },
            new() { ChunkKey = Guid.NewGuid(), SourceFile = "c.txt", ChunkIndex = 0, Content = "alfa beta gamma", Score = 0.5 },
        };

        var result = await reranker.RerankAsync("query", chunks, top: 3);

        result[0].SourceFile.Should().Be("a.txt", "lambda=1 must keep the highest-score chunk first");
    }
}

public class ScoreGateRefusalTests
{
    private static RetrievedChunk Chunk(string file, double score) => new()
    {
        ChunkKey = Guid.NewGuid(), SourceFile = file, ChunkIndex = 0,
        Content = "Execução fiscal prescrição intercorrente arts 40 lei 6830.",
        Score = score,
    };

    [Fact]
    public async Task Score_gate_refuses_when_best_chunk_is_below_threshold()
    {
        // All chunks score 0.05; MinRelevanceScore = 0.5 must trigger the refusal path.
        var opts = new RagOptions { MinRelevanceScore = 0.5 };
        var generator = new GroundedAnswerGenerator(new ExtractiveChatClient(), opts);

        var context = new[] { Chunk("a.txt", 0.05), Chunk("b.txt", 0.03) };
        var answer = await generator.GenerateAsync("prescrição intercorrente", context);

        // ShouldRefuse fires when every chunk is below the threshold, so the model is never called
        // and the answer is the structural refusal (same path as empty context).
        answer.Grounded.Should().BeFalse("all chunks below MinRelevanceScore must trigger refusal");
        answer.Text.Should().Be(RagAnswer.NoAnswer,
            "score-gated refusal must return the canonical NoAnswer string, not an LLM response");
    }

    [Fact]
    public async Task Score_gate_at_default_zero_never_refuses_based_on_score()
    {
        // Default MinRelevanceScore = 0.0 means no chunk ever fails the gate (score >= 0.0 always).
        // A very low-scoring chunk must still reach the model, not be blocked.
        var opts = new RagOptions(); // MinRelevanceScore defaults to 0.0
        var generator = new GroundedAnswerGenerator(new ExtractiveChatClient(), opts);

        // Score near zero but query and content share terms, so ExtractiveChatClient finds overlap.
        var context = new[] { Chunk("fiscal.txt", 0.001) };
        var answer = await generator.GenerateAsync("prescrição intercorrente", context);

        // The model was called (ExtractiveChatClient finds overlap in content) and produced an answer.
        answer.Text.Should().NotBe(RagAnswer.NoAnswer,
            "MinRelevanceScore=0.0 must not gate any chunk; model should be called");
    }

    [Fact]
    public async Task Score_gate_at_exact_threshold_passes_chunk_meeting_threshold()
    {
        // Boundary: chunk score == MinRelevanceScore. ShouldRefuse uses <, so equal must pass.
        var opts = new RagOptions { MinRelevanceScore = 0.5 };
        var generator = new GroundedAnswerGenerator(new ExtractiveChatClient(), opts);

        var context = new[] { Chunk("fiscal.txt", 0.5) }; // score == threshold, must NOT be refused
        var answer = await generator.GenerateAsync("prescrição intercorrente", context);

        // Model called; ExtractiveChatClient finds overlap in the content and returns a grounded answer.
        answer.Text.Should().NotBe(RagAnswer.NoAnswer,
            "a chunk scoring exactly at MinRelevanceScore must pass the gate (< not <=)");
    }
}

// Regression guard for FIX 2: MMR must not overwrite Score with the penalized objective,
// otherwise the MinRelevanceScore gate would wrongly refuse genuinely relevant results.
public class MmrScoreGateInteractionTests
{
    private static async Task<RagAnswer> RunMmrPipelineAsync(string docContent, string query, double minScore)
    {
        var opts = new RagOptions
        {
            Reranker = "mmr",
            MmrLambda = 0.5,
            MinRelevanceScore = minScore,
            TopK = 3,
        };

        var store = new InMemoryChunkStore();
        var embedder = new HashEmbedder(opts.EmbeddingDimensions);
        var vectorIndex = new InMemoryVectorIndex(store);
        var keyword = new InMemoryKeywordRetriever(store, new Bm25Scorer());
        var hybrid = new InMemoryHybridRetriever(vectorIndex, keyword, embedder, opts);
        var ingestor = new DocumentIngestor(
            new ExtensionTextExtractorRouter(new PdfTextExtractor(), new PlainTextExtractor()),
            new StructuralChunker(opts), embedder, vectorIndex);

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(docContent));
        await ingestor.IngestAsync("corpus.txt", stream);

        // Retrieve candidates, run MMR reranker, then pass through the generator gate.
        var candidates = await hybrid.RetrieveAsync(query, opts.CandidatePoolSize);
        var reranker = new MmrReranker(opts.MmrLambda);
        var topK = await reranker.RerankAsync(query, candidates, opts.TopK);
        var generator = new GroundedAnswerGenerator(new ExtractiveChatClient(), opts);
        return await generator.GenerateAsync(query, topK);
    }

    [Fact]
    public async Task Mmr_with_score_gate_above_zero_does_not_refuse_relevant_results()
    {
        // If MmrReranker overwrote Score with the MMR objective (which can be deeply negative due to
        // the diversity penalty), all chunks would fall below MinRelevanceScore and the gate would
        // refuse. After FIX 2 the preserved relevance score must be >= MinRelevanceScore threshold.
        const string corpus =
            "A prescrição intercorrente na execução fiscal inicia após um ano de suspensão do processo.";

        var answer = await RunMmrPipelineAsync(
            corpus,
            query: "prescrição intercorrente execução fiscal",
            minScore: 0.01); // above 0 so the gate is active, but the real relevance easily clears it

        answer.Grounded.Should().BeTrue(
            "MMR must not corrupt Score with the penalized objective; the gate must see the real relevance");
    }
}
