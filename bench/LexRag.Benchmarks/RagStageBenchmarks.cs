using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using LexRag.Core.Abstractions;
using LexRag.Core.Chunking;
using LexRag.Core.Models;
using LexRag.Core.Retrieval;
using LexRag.Embeddings;
using LexRag.Index;
using LexRag.Ingestion;
using LexRag.Orchestration;
using LexRag.Retrieval;

BenchmarkRunner.Run<RagStageBenchmarks>();

/// <summary>
/// Measures per-stage wall-clock cost on the keyless in-memory path (hash embedder + BM25).
/// These numbers are the baseline for the "Performance &amp; custo" section in architecture.md.
/// Run with: dotnet run -c Release --project bench/LexRag.Benchmarks
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class RagStageBenchmarks
{
    private IHybridRetriever _retriever = null!;
    private IReranker _reranker = null!;
    private IRagPipeline _pipeline = null!;
    private RagOptions _options = null!;
    private const string Query = "qual o prazo da prescricao intercorrente na execucao fiscal?";

    [GlobalSetup]
    public async Task Setup()
    {
        _options = new RagOptions();
        var embedder = new HashEmbedder(_options.EmbeddingDimensions);
        var store = new InMemoryChunkStore();
        var vectorIndex = new InMemoryVectorIndex(store);
        var keyword = new InMemoryKeywordRetriever(store, new Bm25Scorer());
        var chunker = new StructuralChunker(_options);
        var router = new ExtensionTextExtractorRouter(new PdfTextExtractor(), new PlainTextExtractor());
        var ingestor = new DocumentIngestor(router, chunker, embedder, vectorIndex);

        // Ingest a representative súmula so retrieval has something to rank.
        var corpus = new[]
        {
            ("stj-sumula314.txt", "Em execução fiscal, não localizados bens penhoráveis, suspende-se o processo por um ano, findo o qual se inicia a prescrição intercorrente."),
            ("stf-sv11.txt",      "Só é lícito o uso de algemas em casos de resistência e de fundado receio de fuga ou de perigo à integridade física própria ou alheia, por parte do preso ou de terceiros."),
            ("stf-sv25.txt",      "É ilícita a prisão civil de depositário infiel, qualquer que seja a modalidade do depósito."),
            ("stj-sumula7.txt",   "A pretensão de simples reexame de prova não enseja recurso especial."),
        };

        foreach (var (name, text) in corpus)
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            await ingestor.IngestAsync(name, stream);
        }

        _retriever = new InMemoryHybridRetriever(vectorIndex, keyword, embedder, _options);
        _reranker = new LexicalCoverageReranker();

        // Full pipeline for the end-to-end benchmark (retrieve + rerank + generate).
        var generator = new GroundedAnswerGenerator(new ToolCallingExtractiveChatClient(), _options);
        _pipeline = new RagPipeline(_retriever, _reranker, generator, _options);
    }

    /// <summary>Hybrid retrieval stage only (dense + sparse + RRF).</summary>
    [Benchmark]
    public Task<IReadOnlyList<RetrievedChunk>> Retrieve() =>
        _retriever.RetrieveAsync(Query, _options.CandidatePoolSize);

    /// <summary>Rerank stage only (LexicalCoverage; operates on already-retrieved candidates).</summary>
    [Benchmark]
    public async Task<IReadOnlyList<RetrievedChunk>> Rerank()
    {
        var candidates = await _retriever.RetrieveAsync(Query, _options.CandidatePoolSize);
        return await _reranker.RerankAsync(Query, candidates, _options.TopK);
    }

    /// <summary>End-to-end pipeline: retrieve + rerank + grounded generation (keyless extractive).</summary>
    [Benchmark]
    public Task<RagAnswer> FullPipeline() => _pipeline.AskAsync(Query);
}
