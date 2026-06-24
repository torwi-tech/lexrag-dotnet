using System.Text;
using FluentAssertions;
using LexRag.Core.Abstractions;
using LexRag.Core.Chunking;
using LexRag.Core.Models;
using LexRag.Core.Retrieval;
using LexRag.Embeddings;
using LexRag.Eval;
using LexRag.Index;
using LexRag.Ingestion;
using LexRag.Retrieval;

namespace LexRag.Tests;

public class PerturbationEvaluatorTests
{
    // Builds a seeded in-memory retriever with two documents.
    private static async Task<(IHybridRetriever Retriever, IReadOnlyList<EvalCase> Cases)> BuildAsync()
    {
        var options = new RagOptions();
        var store = new InMemoryChunkStore();
        var embedder = new HashEmbedder(options.EmbeddingDimensions);
        var vectorIndex = new InMemoryVectorIndex(store);
        var keyword = new InMemoryKeywordRetriever(store, new Bm25Scorer());
        var ingestor = new DocumentIngestor(
            new ExtensionTextExtractorRouter(new PdfTextExtractor(), new PlainTextExtractor()),
            new StructuralChunker(options), embedder, vectorIndex);

        await Ingest(ingestor, "sv25.txt",
            "É ilícita a prisão civil de depositário infiel, qualquer que seja a modalidade do depósito.");
        await Ingest(ingestor, "sumula314.txt",
            "Em execução fiscal, não localizados bens penhoráveis, suspende-se o processo por um ano, "
            + "findo o qual se inicia o prazo da prescrição quinquenal intercorrente.");

        var retriever = new InMemoryHybridRetriever(vectorIndex, keyword, embedder, options);

        var cases = new EvalCase[]
        {
            new("prisão civil depositário infiel", InDomain: true, ExpectedSourceFiles: ["sv25.txt"]),
            new("prescrição intercorrente execução fiscal", InDomain: true, ExpectedSourceFiles: ["sumula314.txt"]),
        };

        return (retriever, cases);
    }

    private static async Task Ingest(DocumentIngestor ingestor, string name, string text)
    {
        await using var s = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await ingestor.IngestAsync(name, s);
    }

    [Fact]
    public async Task Perturbation_harness_returns_one_result_per_perturbation_type()
    {
        var (retriever, cases) = await BuildAsync();
        var evaluator = new PerturbationEvaluator(retriever);

        var report = await evaluator.RunAsync(cases, k: 5);

        report.Total.Should().Be(2, "two labeled in-domain cases");
        report.Results.Should().NotBeEmpty("at least one perturbation type must be defined");
        // Each perturbation type must appear exactly once.
        report.Results.Select(r => r.PerturbationType).Distinct().Should().HaveSameCount(report.Results);
    }

    [Fact]
    public async Task Perturbation_harness_computes_delta_as_perturbed_minus_baseline()
    {
        var (retriever, cases) = await BuildAsync();
        var evaluator = new PerturbationEvaluator(retriever);

        var report = await evaluator.RunAsync(cases, k: 5);

        // DeltaVsBaseline must equal HitRate - BaselineHitRate for every perturbation.
        foreach (var r in report.Results)
        {
            r.DeltaVsBaseline.Should().BeApproximately(r.HitRate - report.BaselineHitRate, 1e-9,
                $"delta for '{r.PerturbationType}' must equal HitRate - BaselineHitRate");
        }
    }

    [Fact]
    public async Task Perturbation_harness_empty_corpus_returns_zero_total()
    {
        var options = new RagOptions();
        var store = new InMemoryChunkStore();
        var embedder = new HashEmbedder(options.EmbeddingDimensions);
        var vectorIndex = new InMemoryVectorIndex(store);
        var keyword = new InMemoryKeywordRetriever(store, new Bm25Scorer());
        var retriever = new InMemoryHybridRetriever(vectorIndex, keyword, embedder, options);

        // No labeled in-domain cases.
        var evaluator = new PerturbationEvaluator(retriever);
        var report = await evaluator.RunAsync([new("qualquer", InDomain: false)], k: 5);

        report.Total.Should().Be(0);
        report.Results.Should().BeEmpty();
    }
}

public class AblationRunnerTests
{
    private static async Task<(InMemoryChunkStore Store, IVectorIndex VectorIndex, IKeywordRetriever Keyword, IEmbedder Embedder, RagOptions Options)> SeedAsync()
    {
        var options = new RagOptions();
        var store = new InMemoryChunkStore();
        var embedder = new HashEmbedder(options.EmbeddingDimensions);
        var vectorIndex = new InMemoryVectorIndex(store);
        var keyword = new InMemoryKeywordRetriever(store, new Bm25Scorer());
        var ingestor = new DocumentIngestor(
            new ExtensionTextExtractorRouter(new PdfTextExtractor(), new PlainTextExtractor()),
            new StructuralChunker(options), embedder, vectorIndex);

        await Ingest(ingestor, "sv25.txt",
            "É ilícita a prisão civil de depositário infiel, qualquer que seja a modalidade do depósito.");
        await Ingest(ingestor, "sumula314.txt",
            "Em execução fiscal, não localizados bens penhoráveis, suspende-se o processo por um ano, "
            + "findo o qual se inicia o prazo da prescrição quinquenal intercorrente.");

        return (store, vectorIndex, keyword, embedder, options);
    }

    private static async Task Ingest(DocumentIngestor ingestor, string name, string text)
    {
        await using var s = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await ingestor.IngestAsync(name, s);
    }

    private static readonly IReadOnlyList<EvalCase> LabeledCases =
    [
        new("prisão civil depositário infiel", InDomain: true, ExpectedSourceFiles: ["sv25.txt"]),
        new("prescrição intercorrente execução fiscal", InDomain: true, ExpectedSourceFiles: ["sumula314.txt"]),
    ];

    [Fact]
    public async Task Ablation_returns_one_row_per_configuration()
    {
        var (_, vectorIndex, keyword, embedder, options) = await SeedAsync();
        var reranker = new LexicalCoverageReranker();

        var denseOnly = new DenseOnlyRetriever(vectorIndex, embedder);
        var sparseOnly = new SparseOnlyRetriever(keyword);
        var hybrid = new InMemoryHybridRetriever(vectorIndex, keyword, embedder, options);

        IReadOnlyList<AblationConfig> configs =
        [
            new("dense-only", "dense leg", denseOnly),
            new("sparse-only", "sparse leg", sparseOnly),
            new("hybrid-no-rerank", "hybrid without reranker", hybrid),
            new("hybrid-with-rerank", "hybrid + reranker", hybrid, reranker),
        ];

        var runner = new AblationRunner(configs);
        var report = await runner.RunAsync(LabeledCases, k: 5);

        report.Rows.Should().HaveCount(4, "one row per configuration");
        report.Rows.Select(r => r.Name).Should().BeEquivalentTo(
            ["dense-only", "sparse-only", "hybrid-no-rerank", "hybrid-with-rerank"]);
    }

    [Fact]
    public async Task Ablation_rows_contain_valid_metric_ranges()
    {
        var (_, vectorIndex, keyword, embedder, options) = await SeedAsync();

        IReadOnlyList<AblationConfig> configs =
        [
            new("hybrid", "baseline hybrid", new InMemoryHybridRetriever(vectorIndex, keyword, embedder, options)),
        ];

        var runner = new AblationRunner(configs);
        var report = await runner.RunAsync(LabeledCases, k: 5);

        report.LabeledCases.Should().Be(2);
        var row = report.Rows[0];
        row.HitRateAtK.Should().BeInRange(0.0, 1.0);
        row.RecallAtK.Should().BeInRange(0.0, 1.0);
        row.Mrr.Should().BeInRange(0.0, 1.0);
    }

    [Fact]
    public async Task Ablation_empty_cases_returns_zero_labeled()
    {
        var (_, vectorIndex, keyword, embedder, options) = await SeedAsync();
        var runner = new AblationRunner([
            new("hybrid", "desc", new InMemoryHybridRetriever(vectorIndex, keyword, embedder, options)),
        ]);

        var report = await runner.RunAsync([new("qualquer", InDomain: false)], k: 5);

        report.LabeledCases.Should().Be(0);
        // Each configuration still gets a row; metrics are zeroed because no labeled cases exist.
        report.Rows.Should().HaveCount(1);
        report.Rows[0].Total.Should().Be(0);
    }
}

// Robustness property: the BM25/lexical leg is a bag-of-words model, so word-order permutations
// of a query must yield the same top-hit result. This is the property that makes term-reversal a
// zero-delta perturbation for keyword retrieval -- not just a harness arithmetic check.
public class LexicalOrderInsensitivityTests
{
    private static async Task<InMemoryKeywordRetriever> BuildAsync()
    {
        var options = new RagOptions();
        var store = new InMemoryChunkStore();
        var embedder = new HashEmbedder(options.EmbeddingDimensions);
        var vectorIndex = new InMemoryVectorIndex(store);
        var ingestor = new DocumentIngestor(
            new ExtensionTextExtractorRouter(new PdfTextExtractor(), new PlainTextExtractor()),
            new StructuralChunker(options), embedder, vectorIndex);

        await Ingest(ingestor, "sv25.txt",
            "É ilícita a prisão civil de depositário infiel, qualquer que seja a modalidade do depósito.");
        await Ingest(ingestor, "sumula314.txt",
            "Em execução fiscal, não localizados bens penhoráveis, suspende-se o processo por um ano, " +
            "findo o qual se inicia o prazo da prescrição quinquenal intercorrente.");

        return new InMemoryKeywordRetriever(store, new Bm25Scorer());
    }

    private static async Task Ingest(DocumentIngestor ingestor, string name, string text)
    {
        await using var s = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        await ingestor.IngestAsync(name, s);
    }

    [Fact]
    public async Task Bm25_top_hit_is_identical_for_original_and_word_order_reversed_query()
    {
        // BM25 is bag-of-words: token order does not affect the score formula.
        // A reversed query must return the same top-ranked document as the original.
        var retriever = await BuildAsync();

        const string original = "prescrição intercorrente execução fiscal";
        var tokens = original.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        Array.Reverse(tokens);
        var reversed = string.Join(" ", tokens);

        var originalTop = (await retriever.SearchAsync(original, top: 3))[0];
        var reversedTop = (await retriever.SearchAsync(reversed, top: 3))[0];

        reversedTop.SourceFile.Should().Be(originalTop.SourceFile,
            "BM25 bag-of-words score is order-insensitive; reversing word order must not change the top hit");
        reversedTop.Score.Should().BeApproximately(originalTop.Score, 1e-9,
            "term frequencies and IDFs are identical regardless of query word order");
    }
}
