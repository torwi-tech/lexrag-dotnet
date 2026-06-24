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

public class RetrievalMetricsTests
{
    private static async Task<IHybridRetriever> SeededRetriever(RagOptions options)
    {
        var store = new InMemoryChunkStore();
        var embedder = new HashEmbedder(options.EmbeddingDimensions);
        var vectorIndex = new InMemoryVectorIndex(store);
        var keyword = new InMemoryKeywordRetriever(store, new Bm25Scorer());
        var ingestor = new DocumentIngestor(
            new ExtensionTextExtractorRouter(new PdfTextExtractor(), new PlainTextExtractor()),
            new StructuralChunker(options), embedder, vectorIndex);

        await Ingest(ingestor, "sv25.txt", "É ilícita a prisão civil de depositário infiel, qualquer que seja a modalidade do depósito.");
        await Ingest(ingestor, "sumula314.txt", "Em execução fiscal, não localizados bens penhoráveis, suspende-se o processo por um ano, findo o qual se inicia a prescrição intercorrente.");
        return new InMemoryHybridRetriever(vectorIndex, keyword, embedder, options);
    }

    private static async Task Ingest(DocumentIngestor ingestor, string name, string text)
    {
        await using var s = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await ingestor.IngestAsync(name, s);
    }

    [Fact]
    public async Task Retrieval_metrics_reward_finding_the_expected_document()
    {
        var options = new RagOptions();
        var evaluator = new RetrievalEvaluator(await SeededRetriever(options));

        var cases = new EvalCase[]
        {
            new("é lícita a prisão civil de depositário infiel?", InDomain: true, ExpectedSourceFiles: ["sv25.txt"]),
            new("prescrição intercorrente em execução fiscal sem bens penhoráveis", InDomain: true, ExpectedSourceFiles: ["sumula314.txt"]),
            new("Qual a capital da Austrália?", InDomain: false), // ignored (no label)
        };

        var m = await evaluator.RunAsync(cases, k: 5);

        m.Total.Should().Be(2);              // only the two labeled in-domain cases
        m.HitRateAtK.Should().Be(1.0);       // both expected docs are in the top-5
        m.RecallAtK.Should().Be(1.0);        // 1 of 1 source found per case → recall = 1.0
        m.Mrr.Should().BeGreaterThan(0.0);
    }

    [Fact]
    public async Task Empty_label_set_yields_zeroed_metrics()
    {
        var options = new RagOptions();
        var evaluator = new RetrievalEvaluator(await SeededRetriever(options));
        var m = await evaluator.RunAsync([new("qualquer", InDomain: false)], k: 5);
        m.Total.Should().Be(0);
        m.HitRateAtK.Should().Be(0.0);
    }

    [Fact]
    public async Task Recall_diverges_from_hit_rate_with_multiple_sources()
    {
        // proves that recall < hit-rate when only 1 of 2 expected sources is in the index
        var options = new RagOptions();
        var evaluator = new RetrievalEvaluator(await SeededRetriever(options));

        var cases = new EvalCase[]
        {
            new("é lícita a prisão civil de depositário infiel?", InDomain: true,
                ExpectedSourceFiles: ["sv25.txt", "ghost.txt"]), // ghost.txt not indexed
        };

        var m = await evaluator.RunAsync(cases, k: 5);

        m.HitRateAtK.Should().Be(1.0);   // ≥1 source found → hit
        m.RecallAtK.Should().Be(0.5);    // 1 of 2 sources found → 50% recall
    }
}
