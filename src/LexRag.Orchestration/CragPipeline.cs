using LexRag.Core.Abstractions;
using LexRag.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LexRag.Orchestration;

// Corrective RAG: retrieve → grade → reformulate + re-retrieve on weak context (bounded) → generate. Separate
// pipeline because the extra retrievals only earn their keep on composed or poorly-phrased questions.
public sealed class CragPipeline(
    IHybridRetriever retriever,
    IReranker reranker,
    IRetrievalGrader grader,
    IQueryReformulator reformulator,
    IAnswerGenerator generator,
    RagOptions options,
    ILogger<CragPipeline>? logger = null) : IRagPipeline
{
    private readonly ILogger _log = logger ?? NullLogger<CragPipeline>.Instance;

    public async Task<RagAnswer> AskAsync(string query, CancellationToken ct = default)
    {
        using var activity = RagPipeline.ActivitySource.StartActivity("rag.crag");
        var trace = new List<string>();
        var searchQuery = query;

        var context = await RetrieveAsync(searchQuery, ct);
        var grade = await grader.GradeAsync(searchQuery, context, ct);
        trace.Add($"grade#0: {grade.Reason}");

        var round = 0;
        while (!grade.Sufficient && round < options.MaxCorrectiveRounds)
        {
            round++;
            var reformulated = await reformulator.ReformulateAsync(searchQuery, grade, ct);
            if (reformulated == searchQuery)
            {
                trace.Add($"corretivo#{round}: reformulação não mudou a query — encerrando correção");
                break;
            }

            trace.Add($"corretivo#{round}: re-busca com '{reformulated}'");
            searchQuery = reformulated;
            context = await RetrieveAsync(searchQuery, ct);
            grade = await grader.GradeAsync(searchQuery, context, ct);
            trace.Add($"grade#{round}: {grade.Reason}");
        }

        activity?.SetTag("crag.rounds", round);
        activity?.SetTag("crag.sufficient", grade.Sufficient);
        _log.LogInformation("CRAG: {Rounds} rodada(s) corretiva(s), suficiente={Sufficient}", round, grade.Sufficient);

        // Generation answers the original question, grounded in the best context found.
        var answer = await generator.GenerateAsync(query, context, ct);
        return answer with { Trace = trace };
    }

    private async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, CancellationToken ct)
    {
        var candidates = await retriever.RetrieveAsync(query, options.CandidatePoolSize, ct);
        return await reranker.RerankAsync(query, candidates, options.TopK, ct);
    }
}
