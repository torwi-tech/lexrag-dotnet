using System.Diagnostics;
using System.Diagnostics.Metrics;
using LexRag.Core.Abstractions;
using LexRag.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LexRag.Orchestration;

// Explicit code orchestration, not a model-driven loop: a regulated/legal flow must be deterministic and auditable.
public sealed class RagPipeline(
    IHybridRetriever retriever,
    IReranker reranker,
    IAnswerGenerator generator,
    RagOptions options,
    ILogger<RagPipeline>? logger = null) : IRagPipeline
{
    internal static readonly ActivitySource ActivitySource = new("LexRag.Rag");

    // Histograms share the Meter with the ActivitySource name so both appear under "LexRag.Rag" in OTel dashboards.
    private static readonly Meter Meter = new("LexRag.Rag");
    private static readonly Histogram<double> RetrieveHist = Meter.CreateHistogram<double>("rag.stage.retrieve_ms", "ms", "Retrieval stage wall-clock latency");
    private static readonly Histogram<double> RerankHist = Meter.CreateHistogram<double>("rag.stage.rerank_ms", "ms", "Rerank stage wall-clock latency");
    private static readonly Histogram<double> GenerateHist = Meter.CreateHistogram<double>("rag.stage.generate_ms", "ms", "Generation stage wall-clock latency");

    private readonly ILogger _log = logger ?? NullLogger<RagPipeline>.Instance;

    public async Task<RagAnswer> AskAsync(string query, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("rag.ask");
        activity?.SetTag("rag.query_length", query.Length);

        var sw = Stopwatch.StartNew();

        var candidates = await retriever.RetrieveAsync(query, options.CandidatePoolSize, ct);
        var retrieveMs = sw.Elapsed.TotalMilliseconds;
        RetrieveHist.Record(retrieveMs);
        activity?.SetTag("rag.retrieved", candidates.Count);
        activity?.SetTag("rag.retrieve_ms", retrieveMs);

        sw.Restart();
        var topK = await reranker.RerankAsync(query, candidates, options.TopK, ct);
        var rerankMs = sw.Elapsed.TotalMilliseconds;
        RerankHist.Record(rerankMs);
        activity?.SetTag("rag.reranked_topk", topK.Count);
        activity?.SetTag("rag.rerank_ms", rerankMs);

        sw.Restart();
        var answer = await generator.GenerateAsync(query, topK, ct);
        var generateMs = sw.Elapsed.TotalMilliseconds;
        GenerateHist.Record(generateMs);
        activity?.SetTag("rag.grounded", answer.Grounded);
        activity?.SetTag("rag.generate_ms", generateMs);

        _log.LogInformation(
            "RAG ask: grounded={Grounded}, citations={Citations}, context={Context}, retrieve={RetrieveMs:F1}ms, rerank={RerankMs:F1}ms, generate={GenerateMs:F1}ms",
            answer.Grounded, answer.Citations.Count, topK.Count, retrieveMs, rerankMs, generateMs);

        return answer with { Timing = new StageTiming(retrieveMs, rerankMs, generateMs) };
    }
}
