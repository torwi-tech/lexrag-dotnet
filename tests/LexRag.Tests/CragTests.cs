using System.Text;
using FluentAssertions;
using LexRag.Core.Abstractions;
using LexRag.Core.Chunking;
using LexRag.Core.Models;
using LexRag.Core.Retrieval;
using LexRag.Embeddings;
using LexRag.Index;
using LexRag.Ingestion;
using LexRag.Orchestration;
using LexRag.Retrieval;

namespace LexRag.Tests;

public class CragGraderAndReformulatorTests
{
    private static readonly RagOptions Options = new() { GradeSufficiencyThreshold = 0.5 };

    private static RetrievedChunk Chunk(string content) => new()
    {
        ChunkKey = Guid.NewGuid(), SourceFile = "f.txt", ChunkIndex = 0, Content = content, Score = 1,
    };

    [Fact]
    public async Task Grade_is_sufficient_when_query_terms_are_covered()
    {
        var grader = new LexicalRetrievalGrader(Options);
        var grade = await grader.GradeAsync(
            "prescrição intercorrente execução fiscal",
            [Chunk("A prescrição intercorrente na execução fiscal corre após um ano.")]);
        grade.Sufficient.Should().BeTrue();
        grade.Coverage.Should().BeGreaterThanOrEqualTo(0.5);
    }

    [Fact]
    public async Task Grade_is_insufficient_when_context_misses_the_terms()
    {
        var grader = new LexicalRetrievalGrader(Options);
        var grade = await grader.GradeAsync(
            "prescrição intercorrente execução fiscal",
            [Chunk("Texto sobre formas de pagamento e prazos de entrega.")]);
        grade.Sufficient.Should().BeFalse();
    }

    [Fact]
    public async Task Reformulator_keeps_content_terms_and_drops_noise()
    {
        var reformulated = await new KeywordQueryReformulator()
            .ReformulateAsync("por favor, gostaria de saber qual a prescrição intercorrente",
                new RetrievalGrade(false, 0, "x"));
        reformulated.Should().Contain("prescrição").And.Contain("intercorrente");
        reformulated.Should().NotContain("favor").And.NotContain("gostaria");
    }
}

public class CragPipelineTests
{
    private static readonly RagOptions Options = new() { MaxCorrectiveRounds = 1, TopK = 3, CandidatePoolSize = 10 };

    private sealed class RecordingRetriever(string content) : IHybridRetriever
    {
        public int Calls { get; private set; }
        public List<string> Queries { get; } = [];
        public Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, int top, CancellationToken ct = default)
        {
            Calls++; Queries.Add(query);
            return Task.FromResult<IReadOnlyList<RetrievedChunk>>(
                [new() { ChunkKey = Guid.NewGuid(), SourceFile = "f.txt", ChunkIndex = 0, Content = content, Score = 1 }]);
        }
    }

    private sealed class StubGrader(Queue<bool> verdicts) : IRetrievalGrader
    {
        public Task<RetrievalGrade> GradeAsync(string query, IReadOnlyList<RetrievedChunk> context, CancellationToken ct = default)
            => Task.FromResult(new RetrievalGrade(verdicts.Dequeue(), 0, "stub"));
    }

    private sealed class StubReformulator(Func<string, string> rewrite) : IQueryReformulator
    {
        public Task<string> ReformulateAsync(string query, RetrievalGrade grade, CancellationToken ct = default)
            => Task.FromResult(rewrite(query));
    }

    private static CragPipeline Build(RecordingRetriever retriever, IRetrievalGrader grader, IQueryReformulator reformulator) =>
        new(retriever, new IdentityReranker(), grader, reformulator,
            new GroundedAnswerGenerator(new ExtractiveChatClient(), Options), Options);

    [Fact]
    public async Task Sufficient_on_first_try_does_not_re_retrieve()
    {
        var retriever = new RecordingRetriever("prescrição intercorrente execução fiscal corre após um ano");
        var crag = Build(retriever, new StubGrader(new([true])), new StubReformulator(q => q + " x"));

        var answer = await crag.AskAsync("prescrição intercorrente execução fiscal");

        retriever.Calls.Should().Be(1);
        answer.Trace.Should().NotContain(t => t.Contains("corretivo"));
    }

    [Fact]
    public async Task Insufficient_then_sufficient_triggers_one_corrective_round()
    {
        var retriever = new RecordingRetriever("prescrição intercorrente execução fiscal corre após um ano");
        var crag = Build(retriever, new StubGrader(new([false, true])), new StubReformulator(_ => "query reformulada"));

        var answer = await crag.AskAsync("pergunta original verbosa");

        retriever.Calls.Should().Be(2);                       // initial + 1 corrective re-retrieve
        retriever.Queries[1].Should().Be("query reformulada");
        answer.Trace.Should().Contain(t => t.Contains("corretivo#1"));
    }

    [Fact]
    public async Task A_noop_reformulation_stops_the_loop()
    {
        var retriever = new RecordingRetriever("conteúdo irrelevante");
        // Always insufficient, but the reformulator returns the same query → no point re-retrieving.
        var crag = Build(retriever, new StubGrader(new([false, false])), new StubReformulator(q => q));

        var answer = await crag.AskAsync("mesma query");

        retriever.Calls.Should().Be(1);
        answer.Trace.Should().Contain(t => t.Contains("não mudou"));
    }

    [Fact]
    public async Task End_to_end_keyless_corrects_a_noisy_query_and_grounds_the_answer()
    {
        var options = new RagOptions { MaxCorrectiveRounds = 1, GradeSufficiencyThreshold = 0.6 };
        var store = new InMemoryChunkStore();
        var embedder = new HashEmbedder(options.EmbeddingDimensions);
        var vectorIndex = new InMemoryVectorIndex(store);
        var keyword = new InMemoryKeywordRetriever(store, new Bm25Scorer());
        var hybrid = new InMemoryHybridRetriever(vectorIndex, keyword, embedder, options);
        var ingestor = new DocumentIngestor(
            new ExtensionTextExtractorRouter(new PdfTextExtractor(), new PlainTextExtractor()),
            new StructuralChunker(options), embedder, vectorIndex);
        await using (var s = new MemoryStream(Encoding.UTF8.GetBytes(
            "A prescrição intercorrente na execução fiscal corre após um ano de suspensão do processo.")))
            await ingestor.IngestAsync("stj.txt", s);

        var crag = new CragPipeline(hybrid, new LexicalCoverageReranker(),
            new LexicalRetrievalGrader(options), new KeywordQueryReformulator(),
            new GroundedAnswerGenerator(new ExtractiveChatClient(), options), options);

        // A noisy, verbose phrasing of an in-domain question.
        var answer = await crag.AskAsync("por favor gostaria de saber sobre a prescrição intercorrente na execução fiscal");

        answer.Grounded.Should().BeTrue();
        answer.Citations.Should().Contain(c => c.SourceFile == "stj.txt");
        answer.Trace.Should().NotBeEmpty();
    }
}
