using LexRag.Api;
using LexRag.Api.Eval;
using LexRag.Core.Abstractions;
using LexRag.Core.Generation;
using LexRag.Core.Models;
using LexRag.Eval;
using LexRag.Orchestration;
using Microsoft.AspNetCore.Http.Features;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Real keys/connection strings live in user-secrets (never committed); with neither, the app runs on key-free fakes.
builder.Configuration.AddUserSecrets<Program>(optional: true);
// Optional local override (gitignored: appsettings.*.local.json) for machines where user-secrets is awkward.
// Skipped under the Testing environment so integration tests stay hermetic on the keyless fakes (ADR 0008),
// regardless of a real key configured locally. Absent by default, so the keyless fakes win either way.
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Configuration.AddJsonFile("appsettings.secrets.local.json", optional: true, reloadOnChange: false);

builder.Services.AddLexRag(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
// RFC 7807 ProblemDetails for all error responses (validation + unhandled exceptions).
builder.Services.AddProblemDetails();

// Upload cap. Both limits are needed: the Kestrel body limit and the multipart form limit.
const long maxUpload = 10 * 1024 * 1024;
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxUpload);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxUpload);

// WithLogging() routes ILogger through OTel so log records carry the active TraceId/SpanId.
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("LexRag.Api"))
    .WithTracing(t =>
    {
        t.AddSource("LexRag.Rag")
         .AddAspNetCoreInstrumentation(o => o.Filter = ctx => ctx.Request.Path != "/health")
         .AddHttpClientInstrumentation();
        if (string.IsNullOrWhiteSpace(otlpEndpoint)) t.AddConsoleExporter();
        else t.AddOtlpExporter();
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation()
         .AddMeter("LexRag.Rag"); // per-stage latency histograms (retrieve/rerank/generate)
        if (!string.IsNullOrWhiteSpace(otlpEndpoint)) m.AddOtlpExporter();
    })
    .WithLogging(l =>
    {
        if (!string.IsNullOrWhiteSpace(otlpEndpoint)) l.AddOtlpExporter();
    });

var app = builder.Build();
var mode = app.Services.GetRequiredService<RagMode>();

// Turns unhandled exceptions into ProblemDetails 500 responses instead of empty 500s.
app.UseExceptionHandler();

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await CorpusSeeder.SeedAsync(scope.ServiceProvider, logger);
}

app.MapGet("/", () => Results.Ok(new
{
    service = "LexRAG — assistente documental agêntico (.NET 8)",
    mode,
    endpoints = new[] { "GET /health", "POST /ask", "POST /ask/crag", "POST /ask/agentic", "POST /ingest/text", "POST /ingest/file", "POST /eval", "POST /eval/retrieval", "POST /eval/perturbation", "POST /eval/ablation", "POST /eval/judge" },
}));

app.MapGet("/health", async (IVectorIndex index, RagOptions ragOptions, CancellationToken ct) =>
{
    var chunkCount = await index.CountAsync(ct);
    return Results.Ok(new
    {
        status = "ok",
        mode,
        chunkCount,
        embeddingDimensions = ragOptions.EmbeddingDimensions,
    });
});

app.MapPost("/ask", async (AskRequest req, IRagPipeline pipeline, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Query))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["query is required"] });
    var answer = await pipeline.AskAsync(req.Query, ct);
    return Results.Ok(AskResponse.From(answer));
});

app.MapPost("/ask/crag", async (AskRequest req, CragPipeline crag, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Query))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["query is required"] });
    var answer = await crag.AskAsync(req.Query, ct);
    return Results.Ok(AskResponse.From(answer));
});

app.MapPost("/ask/agentic", async (AskRequest req, SemanticKernelRagService sk, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Query))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["query is required"] });
    var text = await sk.AskAgenticAsync(req.Query, ct);
    return Results.Ok(new { answer = text });
});

app.MapPost("/ingest/text", async (IngestTextRequest req, IDocumentIngestor ingestor, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Content))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["content"] = ["content is required"] });
    if (string.IsNullOrWhiteSpace(req.SourceFile))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["sourceFile"] = ["sourceFile is required"] });
    await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(req.Content));
    var chunks = await ingestor.IngestAsync(req.SourceFile, stream, ct);
    return Results.Ok(new { req.SourceFile, chunks });
});

app.MapPost("/ingest/file", async (IFormFile file, IDocumentIngestor ingestor, CancellationToken ct) =>
{
    // Never trust the client filename: strip any path, allowlist the extension.
    var safeName = Path.GetFileName(file.FileName);
    if (Path.GetExtension(safeName).ToLowerInvariant() is not (".txt" or ".pdf"))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["apenas .txt ou .pdf"] });

    await using var stream = file.OpenReadStream();
    var chunks = await ingestor.IngestAsync(safeName, stream, ct);
    return Results.Ok(new { fileName = safeName, chunks });
}).DisableAntiforgery();

app.MapPost("/eval", async (RagEvaluator evaluator, IGoldenSetSource goldenSet, CancellationToken ct) =>
{
    var report = await evaluator.RunAsync(goldenSet.GetCases(), ct);
    return Results.Ok(new
    {
        report.Total,
        report.Passed,
        lexicalFaithfulness = report.LexicalFaithfulness,
        citationCorrectness = report.CitationCorrectness,
        refusalAccuracy = report.RefusalAccuracy,
        summary = report.ToSummary(),
        cases = report.Results.Select(r => new { r.Case.Query, r.Case.InDomain, r.Grounded, r.Refused, r.Passed, r.Answer }),
    });
});

// Retrieval-only quality (Recall@K / Hit-rate / MRR) over the golden set. Measures the search stage
// rather than the generation, which most faithfulness dashboards don't capture.
app.MapPost("/eval/retrieval", async (RetrievalEvaluator evaluator, IGoldenSetSource goldenSet, RagOptions options, CancellationToken ct) =>
{
    var metrics = await evaluator.RunAsync(goldenSet.GetCases(), options.TopK, ct);
    return Results.Ok(new
    {
        metrics.K,
        metrics.Total,
        hitRateAtK = metrics.HitRateAtK,
        recallAtK = metrics.RecallAtK,
        mrr = metrics.Mrr,
        byGroup = metrics.ByGroup?.Select(g => new
        {
            group = g.Group, n = g.Total, hitRateAtK = g.HitRateAtK, recallAtK = g.RecallAtK, mrr = g.Mrr
        }),
        summary = metrics.ToSummary(),
    });
});

// Perturbation robustness: measures the hit-rate delta when queries are deterministically perturbed
// (casing, whitespace, term order, stopword reinsertion). Answers whether the retrieval pipeline is
// stable across surface-form variants of the same query — the 4th eval dimension named in architecture.md.
app.MapPost("/eval/perturbation", async (PerturbationEvaluator evaluator, IGoldenSetSource goldenSet, RagOptions options, CancellationToken ct) =>
{
    var report = await evaluator.RunAsync(goldenSet.GetCases(), options.TopK, ct);
    return Results.Ok(new
    {
        report.K,
        report.Total,
        baselineHitRate = report.BaselineHitRate,
        perturbations = report.Results.Select(r => new
        {
            type = r.PerturbationType,
            hitRate = r.HitRate,
            deltaVsBaseline = r.DeltaVsBaseline,
        }),
        summary = report.ToSummary(),
    });
});

// Ablation: compares dense-only, sparse-only, hybrid, and rerank-on/off configurations over the golden
// set. Shows that RRF fusion and the reranker are earned through measured recall gain, not decorative.
app.MapPost("/eval/ablation", async (AblationRunner runner, IGoldenSetSource goldenSet, RagOptions options, CancellationToken ct) =>
{
    var report = await runner.RunAsync(goldenSet.GetCases(), options.TopK, ct);
    return Results.Ok(new
    {
        report.K,
        report.LabeledCases,
        configurations = report.Rows.Select(r => new
        {
            name = r.Name,
            description = r.Description,
            n = r.Total,
            hitRateAtK = r.HitRateAtK,
            recallAtK = r.RecallAtK,
            mrr = r.Mrr,
        }),
        summary = report.ToSummary(),
    });
});

// Layer 2 of the three-way eval (ADR 0011): an LLM-as-judge scores semantic groundedness and relevance
// (what the deterministic lexical gate, Layer 1, can't see) and reports agreement with it. Key-gated: skips
// on the keyless fake, since judging a fake model is noise, mirroring the Docker-gated integration tests.
app.MapPost("/eval/judge", async (LlmJudge judge, IRagPipeline pipeline, IGoldenSetSource goldenSet, RagMode mode, RagOptions options, IConfiguration cfg, CancellationToken ct) =>
{
    if (mode.ChatClient != "AzureOpenAI")
        return Results.Json(new { skipped = true, reason = "LLM-judge (Layer 2) precisa de um chat model real; rode com AzureOpenAI. Key-gated por design (ADR 0011)." }, statusCode: 501);

    var max = int.TryParse(cfg["Eval:JudgeMaxCases"], out var m) && m > 0 ? m : 25;
    var runs = int.TryParse(cfg["Eval:JudgeRuns"], out var rr) && rr > 0 ? rr : 1;
    var cases = goldenSet.GetCases().Where(c => c.InDomain).Take(max).ToList();
    if (cases.Count == 0) return Results.Ok(new { cases = 0, runs, note = "no in-domain cases in the golden set" });

    // Generate each answer once; the judge then runs `runs` times over those fixed answers, so the reported
    // standard deviation isolates LLM-judge variance rather than mixing in generator variance. The answer text,
    // the Layer 1 verdict and the retrieved context are held constant across runs.
    var fixedCases = new List<(string Query, string Answer, string Grounding, bool L1)>(cases.Count);
    foreach (var c in cases)
    {
        var answer = await pipeline.AskAsync(c.Query, ct);
        var grounding = string.Join("\n\n", answer.Context.Select(x => x.Content));
        var l1 = answer.Grounded && CitationParser.Correctness(answer.Text, answer.Context) >= 1.0;
        fixedCases.Add((c.Query, answer.Text, grounding, l1));
    }

    var nn = fixedCases.Count;
    List<double> perRunG = [], perRunR = [], perRunL2 = [], perRunAgree = [];
    var firstRunDivergences = new List<object>();

    for (var run = 0; run < runs; run++)
    {
        double sumG = 0, sumR = 0;
        int l2Count = 0, agreeCount = 0;
        foreach (var fc in fixedCases)
        {
            var v = await judge.JudgeAsync(fc.Query, fc.Answer, fc.Grounding, ct);
            var l2 = v.Groundedness >= 4.0; // "grounded" threshold on the 1-5 judge scale
            sumG += v.Groundedness;
            sumR += v.Relevance;
            if (l2) l2Count++;
            if (fc.L1 == l2) agreeCount++;
            else if (run == 0)
                firstRunDivergences.Add(new { fc.Query, layer1Faithful = fc.L1, groundedness = v.Groundedness, relevance = v.Relevance, reason = v.GroundednessReason });
        }
        perRunG.Add(sumG / nn);
        perRunR.Add(sumR / nn);
        perRunL2.Add((double)l2Count / nn);
        perRunAgree.Add((double)agreeCount / nn);
    }

    static object MeanSd(List<double> xs, int digits)
    {
        var mean = xs.Average();
        var sd = xs.Count < 2 ? 0 : Math.Sqrt(xs.Sum(x => (x - mean) * (x - mean)) / (xs.Count - 1));
        return new { mean = Math.Round(mean, digits), sd = Math.Round(sd, 3) };
    }

    return Results.Ok(new
    {
        cases = nn,
        runs,
        scale = "1-5",
        layer1FaithfulRate = Math.Round((double)fixedCases.Count(c => c.L1) / nn, 3), // deterministic over fixed answers
        groundedness = MeanSd(perRunG, 2),
        relevance = MeanSd(perRunR, 2),
        layer2GroundedRate = MeanSd(perRunL2, 3),
        agreement = MeanSd(perRunAgree, 3),
        divergences = firstRunDivergences.Take(5),
        // Frozen parameters the comparison is fixed at, recorded with the run so a number is never read
        // without the configuration that produced it.
        runParams = new
        {
            dataset = cfg["Eval:Dataset"] ?? "static",
            relevanceCutoff = int.TryParse(cfg["Eval:RelevanceCutoff"], out var cu) ? cu : 1,
            topK = options.TopK,
            judgeModel = cfg["AzureOpenAI:ChatDeployment"],
            embeddingModel = cfg["AzureOpenAI:EmbeddingDeployment"],
        },
        note = "Respostas geradas uma vez; juiz rodado N vezes, entao sd isola a variancia do LLM-judge (nao do gerador). Agreement = concordancia vs Layer 1, nao ranking (ADR 0011).",
    });
});

app.Run();

// Exposed so WebApplicationFactory<Program> can drive integration tests.
public partial class Program;
