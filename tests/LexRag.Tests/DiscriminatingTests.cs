using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using LexRag.Core.Abstractions;
using LexRag.Core.Chunking;
using LexRag.Core.Generation;
using LexRag.Core.Models;
using LexRag.Core.Retrieval;
using LexRag.Embeddings;
using LexRag.Eval;
using LexRag.Index;
using LexRag.Ingestion;
using LexRag.Orchestration;
using LexRag.Retrieval;
using Microsoft.Extensions.AI;

namespace LexRag.Tests;

// Each test asserts a specific ranking or scoring property rather than just line coverage.
// They all run keyless (no Azure, no Docker).

public class LexicalCoverageRerankerTests
{
    private static RetrievedChunk Chunk(string content, double rrfScore) => new()
    {
        ChunkKey = Guid.NewGuid(), SourceFile = "f.txt", ChunkIndex = 0,
        Content = content, Score = rrfScore,
    };

    [Fact]
    public async Task High_coverage_beats_high_rrf_when_coverage_differs()
    {
        // A candidate with HIGH term coverage but LOW incoming RRF must rank above
        // one with LOW coverage and HIGH RRF, because coverage is the primary signal.
        var reranker = new LexicalCoverageReranker();
        var query = "prescrição intercorrente execução fiscal";

        var highCoverageButLowRrf = Chunk(
            "A prescrição intercorrente na execução fiscal tem prazo definido.", rrfScore: 0.01);
        var lowCoverageButHighRrf = Chunk(
            "Decisão administrativa.", rrfScore: 0.99);

        var result = await reranker.RerankAsync(query, [highCoverageButLowRrf, lowCoverageButHighRrf], top: 2);

        result[0].Content.Should().Be(highCoverageButLowRrf.Content,
            "coverage (primary signal) must override incoming RRF when they differ");
    }

    [Fact]
    public async Task Equal_coverage_uses_rrf_as_tiebreaker()
    {
        // When two candidates have the SAME coverage, the one with the higher incoming RRF wins.
        var reranker = new LexicalCoverageReranker();
        var query = "prescrição fiscal";

        // Both contain "prescrição" and "fiscal" → same coverage.
        var higherRrf = Chunk("prescrição fiscal longa descrição sobre o tema A.", rrfScore: 0.9);
        var lowerRrf  = Chunk("prescrição fiscal longa descrição sobre o tema B.", rrfScore: 0.1);

        var result = await reranker.RerankAsync(query, [lowerRrf, higherRrf], top: 2);

        result[0].Content.Should().Be(higherRrf.Content,
            "when coverage is equal, the higher RRF score must win the tie");
    }
}

public class RagEvaluatorDiscriminatingTests
{
    private static IRagPipeline BuildPipelineWith(string docContent, string docName = "doc.txt")
    {
        var options = new RagOptions();
        var store = new InMemoryChunkStore();
        var embedder = new HashEmbedder(options.EmbeddingDimensions);
        var vectorIndex = new InMemoryVectorIndex(store);
        var keyword = new InMemoryKeywordRetriever(store, new Bm25Scorer());
        var hybrid = new InMemoryHybridRetriever(vectorIndex, keyword, embedder, options);
        var ingestor = new DocumentIngestor(
            new ExtensionTextExtractorRouter(new PdfTextExtractor(), new PlainTextExtractor()),
            new StructuralChunker(options), embedder, vectorIndex);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(docContent));
        ingestor.IngestAsync(docName, stream).GetAwaiter().GetResult();

        var generator = new GroundedAnswerGenerator(new ExtractiveChatClient(), options);
        return new RagPipeline(hybrid, new LexicalCoverageReranker(), generator, options);
    }

    [Fact]
    public async Task Out_of_domain_query_is_scored_as_refusal()
    {
        // A question with no matching corpus content should yield a refused answer
        // and be recorded as Refused=true, Passed=true (out-of-domain "passed" == refused).
        var pipeline = BuildPipelineWith(
            "A prescrição intercorrente na execução fiscal corre após um ano.");

        var evaluator = new RagEvaluator(pipeline);
        var outOfDomain = new EvalCase("Qual a capital da Austrália?", InDomain: false);

        var report = await evaluator.RunAsync([outOfDomain]);

        report.Results.Should().ContainSingle();
        var r = report.Results[0];
        r.Refused.Should().BeTrue("out-of-domain query must produce a refusal");
        r.Passed.Should().BeTrue("a correct refusal counts as passed for out-of-domain cases");
    }

    [Fact]
    public async Task Citation_correctness_is_below_one_when_cited_source_not_in_context()
    {
        // Arrange a pipeline that returns a grounded answer for an in-domain query;
        // then check citation correctness by directly running the evaluator against an answer
        // that cites a source not present in the retrieved context.

        // Use a stub pipeline that returns a pre-fabricated answer citing a hallucinated source.
        var stubAnswer = new RagAnswer
        {
            Text = "De acordo com os documentos, a execução fiscal prescreve. [Fonte: fantasma.txt, trecho 0]",
            Grounded = true,
            Citations = [new Citation("fantasma.txt", 0)],
            Context = [new RetrievedChunk
            {
                ChunkKey = Guid.NewGuid(), SourceFile = "real.txt", ChunkIndex = 0,
                Content = "execução fiscal prescreve após um ano", Score = 1.0,
            }],
        };
        var stub = new StubPipeline(stubAnswer);

        var evaluator = new RagEvaluator(stub);
        var inDomain = new EvalCase("prescrição execução fiscal", InDomain: true,
            ExpectedSourceFiles: ["real.txt"]);

        var report = await evaluator.RunAsync([inDomain]);

        report.Results[0].CitationCorrectness.Should().BeLessThan(1.0,
            "citing a source not present in the retrieved context must reduce citation correctness");
    }

    [Fact]
    public async Task Citation_correctness_when_cited_source_is_present_but_wrong_chunk()
    {
        // The answer cites (docA.txt, chunk 0). The context contains (docA.txt, chunk 0) AND (docB.txt, chunk 0).
        // docA is in context (so it is NOT a hallucinated file), but the correct answer comes from docB.
        // CitationParser.Correctness checks whether the cited (file, chunkIndex) pair appears in the retrieved
        // context — it does not know which source is "correct" for the claim, only whether the cited tuple
        // is present. So when the cited source IS in context, Correctness returns 1.0 regardless of which
        // source the evaluator considers "expected".
        //
        // Limitation: CitationParser.Correctness is a structural gate (anti-hallucination), not a semantic one.
        // It detects citations to files never retrieved; it cannot detect "retrieved but wrong for this claim".
        // That distinction requires the Layer 2 LLM-judge (ADR 0011). This test documents that known behaviour
        // so a future refactor does not silently break the contract.

        var chunkA = new RetrievedChunk
        {
            ChunkKey = Guid.NewGuid(), SourceFile = "docA.txt", ChunkIndex = 0,
            Content = "prescrição intercorrente na execução fiscal", Score = 0.9,
        };
        var chunkB = new RetrievedChunk
        {
            ChunkKey = Guid.NewGuid(), SourceFile = "docB.txt", ChunkIndex = 0,
            Content = "uso de algemas exige resistência ou receio de fuga", Score = 0.8,
        };

        // Answer cites docA (which IS in context), but the expected source is docB.
        var stubAnswer = new RagAnswer
        {
            Text = "Conforme os documentos, a execução fiscal tem prazo prescricional. [Fonte: docA.txt, trecho 0]",
            Grounded = true,
            Citations = [new Citation("docA.txt", 0)],
            Context = [chunkA, chunkB],
        };
        var stub = new StubPipeline(stubAnswer);

        var evaluator = new RagEvaluator(stub);
        // ExpectedSourceFiles says "docB" — the answer cited "docA" (present in context but wrong for the claim).
        var inDomain = new EvalCase("algemas uso correto", InDomain: true,
            ExpectedSourceFiles: ["docB.txt"]);

        var report = await evaluator.RunAsync([inDomain]);

        var result = report.Results[0];

        // CitationCorrectness is 1.0 because docA IS in context; the structural gate cannot see it is the wrong source.
        result.CitationCorrectness.Should().Be(1.0,
            "a source present in context always passes the structural citation gate, even when it is the wrong source for the claim");

        // CitedExpectedSource is false because docB (the expected source) was never cited.
        result.CitedExpectedSource.Should().BeFalse(
            "the answer cited docA, not the expected docB, so CitedExpectedSource must be false");

        // The case therefore does not pass (Grounded+Correctness=1.0 but CitedExpectedSource=false).
        result.Passed.Should().BeFalse(
            "passing requires both correct citation form and citing an expected source");
    }

    private sealed class StubPipeline(RagAnswer answer) : IRagPipeline
    {
        public Task<RagAnswer> AskAsync(string query, CancellationToken ct = default) => Task.FromResult(answer);
    }
}

public class Bm25IdfAndLengthNormTests
{
    [Fact]
    public void Common_term_contributes_less_idf_than_rare_term()
    {
        // "lei" appears in ALL docs → IDF near zero; "infiel" appears only in doc[0].
        // For the rare term, doc[0] wins convincingly. For the extremely common "lei",
        // the IDF contribution is negligible so all docs score low, but the doc with
        // the highest TF still ranks first among them.
        var corpus = new[]
        {
            "depositário infiel prisão civil lei",         // "infiel" here (rare), "lei" once
            "lei lei lei lei lei processual tributário",   // "lei" 5× (highest TF); wins for common query
            "lei constitucional emenda",                  // "lei" once
        };

        var scorer = new Bm25Scorer();
        var rareQuery  = scorer.Score(corpus, "infiel", top: 3);
        var commonQuery = scorer.Score(corpus, "lei", top: 3);

        // "infiel" is rare (only doc[0]), so doc[0] ranks first with a meaningful score.
        rareQuery[0].DocIndex.Should().Be(0, "only doc[0] contains the rare term 'infiel'");

        // "lei" is near-universal, so IDF is small, but TF still separates docs.
        // Doc[1] has the highest TF (5×) so it ranks first among docs that all have "lei".
        commonQuery[0].DocIndex.Should().Be(1,
            "doc[1] has the highest TF for 'lei'; even with low IDF, TF separates the ranking");

        // Key IDF property: the rare-term score for doc[0] is higher than the common-term score
        // for the best "lei" doc, because IDF drastically reduces the common-term contribution.
        rareQuery[0].Score.Should().BeGreaterThan(commonQuery[0].Score,
            "a rare term's IDF contribution is larger, so its BM25 score exceeds a common term's even with lower TF");
    }

    [Fact]
    public void Short_doc_with_the_term_is_not_unfairly_beaten_by_long_doc_with_same_term_count()
    {
        // BM25 length normalization: a short doc with 1 occurrence of a term should score at
        // least as well as a long doc with 1 occurrence (same TF, lower length → lower penalty).
        var shortDoc = "infiel depositário";
        var longDoc  = "texto muito longo sobre direito civil brasileiro com muitos termos variados e informações extras sobre legislação brasileira e jurisprudência dos tribunais superiores infiel";

        var corpus = new[] { shortDoc, longDoc };
        var scores = new Bm25Scorer().Score(corpus, "infiel", top: 2);

        scores.Should().HaveCount(2);
        var shortScore = scores.First(s => s.DocIndex == 0).Score;
        var longScore  = scores.First(s => s.DocIndex == 1).Score;

        shortScore.Should().BeGreaterThan(longScore,
            "BM25 length normalization must favor the shorter document when TF counts are equal");
    }
}

public class AgenticEndpointTests(KeylessApiFactory factory) : IClassFixture<KeylessApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task IngestAsync(string name, string content) =>
        (await _client.PostAsJsonAsync("/ingest/text", new { sourceFile = name, content }))
            .EnsureSuccessStatusCode();

    [Fact]
    public async Task Agentic_in_domain_returns_grounded_cited_answer_offline()
    {
        // ToolCallingExtractiveChatClient drives the tool-calling loop offline:
        // turn 1 emits FunctionCallContent, SK invokes DocumentSearchPlugin.SearchAsync,
        // turn 2 applies extractive grounding over the result producing a [Fonte: ...] citation.
        await IngestAsync("sv25.txt",
            "É ilícita a prisão civil de depositário infiel, qualquer que seja a modalidade do depósito.");

        var response = await _client.PostAsJsonAsync("/ask/agentic",
            new { query = "é lícita a prisão civil de depositário infiel?" });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var answer = body.GetProperty("answer").GetString();
        answer.Should().NotBeNullOrWhiteSpace("the agentic endpoint must return a non-empty answer");
        answer.Should().Contain("[Fonte:", "the tool-calling loop must have fired and the answer must be grounded with a citation");
    }
}

// E2E test: a fake IChatClient that returns a hallucinated citation is wired into the REAL
// GroundedAnswerGenerator over a real in-memory corpus. The generator's citation-correctness gate
// must reject the hallucinated source and mark the answer as Grounded=false.
// Unlike the StubPipeline tests above, this exercises the actual grounding gate, not a shortcut.
public class HallucinatedCitationE2ETests
{
    private static async Task<RagAnswer> RunWithHallucinatingClientAsync(
        string corpus, string docName, string query, string hallucinatedAnswer)
    {
        var opts = new RagOptions();
        var store = new InMemoryChunkStore();
        var embedder = new HashEmbedder(opts.EmbeddingDimensions);
        var vectorIndex = new InMemoryVectorIndex(store);
        var keyword = new InMemoryKeywordRetriever(store, new Bm25Scorer());
        var hybrid = new InMemoryHybridRetriever(vectorIndex, keyword, embedder, opts);
        var ingestor = new DocumentIngestor(
            new ExtensionTextExtractorRouter(new PdfTextExtractor(), new PlainTextExtractor()),
            new StructuralChunker(opts), embedder, vectorIndex);

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(corpus));
        await ingestor.IngestAsync(docName, stream);

        // HallucinatingChatClient always returns the fixed answer regardless of context.
        var generator = new GroundedAnswerGenerator(new HallucinatingChatClient(hallucinatedAnswer), opts);
        var pipeline = new RagPipeline(hybrid, new LexicalCoverageReranker(), generator, opts);
        return await pipeline.AskAsync(query);
    }

    [Fact]
    public async Task Hallucinated_citation_not_in_context_makes_answer_grounded_false()
    {
        // The corpus contains only "real.txt". The fake client cites "fantasma.txt" which was never
        // retrieved. The generator's CitationParser.Correctness check must detect the mismatch and
        // set Grounded=false, exercising the actual gate path (not a stub that skips the generator).
        const string answer = "De acordo com os documentos, o prazo prescricional é de cinco anos. " +
                              "[Fonte: fantasma.txt, trecho 0]";

        var result = await RunWithHallucinatingClientAsync(
            corpus: "A prescrição intercorrente na execução fiscal tem prazo de cinco anos.",
            docName: "real.txt",
            query: "prazo prescricional execução fiscal",
            hallucinatedAnswer: answer);

        result.Grounded.Should().BeFalse(
            "a citation to a source not present in the retrieved context must be rejected by the grounding gate");
    }

    // Minimal fake: ignores messages, always returns the pre-set answer string.
    private sealed class HallucinatingChatClient(string answer) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, answer);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }
}
