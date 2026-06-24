using Azure;
using Azure.AI.OpenAI;
using LexRag.Api.Corpus;
using LexRag.Api.Eval;
using LexRag.Core.Abstractions;
using LexRag.Core.Chunking;
using LexRag.Core.Models;
using LexRag.Core.Retrieval;
using LexRag.Embeddings;
using LexRag.Eval;
using LexRag.Index;
using LexRag.Ingestion;
using LexRag.Orchestration;
using LexRag.Retrieval;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pgvector.Npgsql;

namespace LexRag.Api;

public sealed record RagMode(string Embedder, string ChatClient, string VectorStore);

// Composition root: every "needs a key" / "needs Docker" decision is made here, with key-free fakes as the fallback.
public static class RagComposition
{
    public static IServiceCollection AddLexRag(this IServiceCollection services, IConfiguration config)
    {
        var options = new RagOptions();
        config.GetSection(RagOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        services.AddSingleton<Bm25Scorer>();
        services.AddSingleton<IChunker>(new StructuralChunker(options));

        // MMR is opt-in via Rag:Reranker=mmr; default is LexicalCoverageReranker so the pipeline is unchanged.
        if (options.Reranker.Equals("mmr", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IReranker>(new MmrReranker(options.MmrLambda));
        else
            services.AddSingleton<IReranker, LexicalCoverageReranker>();

        var embedderName = AddEmbeddingAndChat(services, config, options);
        var storeName = AddVectorStore(services, config);

        services.AddSingleton<PdfTextExtractor>();
        services.AddSingleton<PlainTextExtractor>();
        services.AddSingleton<ITextExtractorRouter, ExtensionTextExtractorRouter>();
        services.AddSingleton<IDocumentIngestor, DocumentIngestor>();

        services.AddSingleton<IAnswerGenerator, GroundedAnswerGenerator>();
        services.AddSingleton<IRagPipeline, RagPipeline>();

        services.AddSingleton<IRetrievalGrader, LexicalRetrievalGrader>();
        services.AddSingleton<IQueryReformulator, KeywordQueryReformulator>();
        services.AddSingleton<CragPipeline>();

        services.AddSingleton<DocumentSearchPlugin>();
        services.AddSingleton<LoggingFunctionFilter>();
        services.AddSingleton<SemanticKernelRagService>();

        services.AddSingleton<RagEvaluator>();
        services.AddSingleton<RetrievalEvaluator>();
        services.AddSingleton<LlmJudge>();

        // PerturbationEvaluator uses the main hybrid retriever; same InMemory path as RetrievalEvaluator.
        services.AddSingleton<PerturbationEvaluator>(sp =>
            new PerturbationEvaluator(sp.GetRequiredService<IHybridRetriever>()));

        // AblationRunner compares dense-only, sparse-only, hybrid, and rerank-on/off configurations.
        // Only registered for the in-memory path; pgvector ablation requires separate pipeline wiring.
        services.AddSingleton<AblationRunner>(sp => BuildAblationRunner(sp, storeName, options));

        AddDatasetAdapters(services, config);

        services.AddSingleton(new RagMode(embedderName, embedderName.StartsWith("Azure") ? "AzureOpenAI" : "tool-calling-fake", storeName));
        return services;
    }

    private static void AddDatasetAdapters(IServiceCollection services, IConfiguration config)
    {
        var dataset = config["Eval:Dataset"] ?? "static";

        if (dataset.Equals("juristcu", StringComparison.OrdinalIgnoreCase))
        {
            var jurisPath = config["Eval:JurisTcuPath"] ?? "data/juristcu";
            var maxDocs = int.TryParse(config["Eval:JurisTcuMaxDocuments"], out var d) ? d : 0;
            var maxQueries = int.TryParse(config["Eval:JurisTcuMaxQueries"], out var q) ? q : 0;
            var cutoff = int.TryParse(config["Eval:RelevanceCutoff"], out var c) ? c : 1;

            // NullLogger: ILoggerFactory isn't yet wired at composition time; callers that need logging
            // can be upgraded to ILoggerFactory injection without changing the interface.
            services.AddSingleton<ICorpusSource>(
                new JurisTcuCorpusSource(jurisPath, maxDocs, NullLogger.Instance));
            services.AddSingleton<IGoldenSetSource>(
                new JurisTcuGoldenSet(jurisPath, cutoff, maxQueries, NullLogger.Instance));
        }
        else
        {
            services.AddSingleton<ICorpusSource>(new StaticSamplesSource());
            services.AddSingleton<IGoldenSetSource, CuratedGoldenSet>();
        }
    }

    private static string AddEmbeddingAndChat(IServiceCollection services, IConfiguration config, RagOptions options)
    {
        var endpoint = config["AzureOpenAI:Endpoint"];
        var key = config["AzureOpenAI:Key"];
        if (IsReal(endpoint) && IsReal(key))
        {
            var client = new AzureOpenAIClient(new Uri(endpoint!), new AzureKeyCredential(key!));
            var embDeployment = config["AzureOpenAI:EmbeddingDeployment"] ?? "text-embedding-3-small";
            var chatDeployment = config["AzureOpenAI:ChatDeployment"] ?? "gpt-4o-mini";

            IEmbeddingGenerator<string, Embedding<float>> generator =
                client.GetEmbeddingClient(embDeployment).AsIEmbeddingGenerator();
            services.AddSingleton<IEmbedder>(new MeaiEmbedder(generator, options.EmbeddingDimensions));

            IChatClient chat = client.GetChatClient(chatDeployment).AsIChatClient();
            services.AddSingleton(chat);
            return "AzureOpenAI";
        }

        services.AddSingleton<IEmbedder>(new HashEmbedder(options.EmbeddingDimensions));
        // ToolCallingExtractiveChatClient handles both the agentic (tool-calling) path and the
        // direct-context path (GroundedAnswerGenerator), so it replaces ExtractiveChatClient entirely.
        services.AddSingleton<IChatClient, ToolCallingExtractiveChatClient>();
        return "hash-fake";
    }

    private static string AddVectorStore(IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Postgres") ?? config["Postgres:ConnectionString"];
        if (IsReal(connectionString))
        {
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            builder.UseVector();
            services.AddSingleton(builder.Build());
            services.AddSingleton<IVectorIndex, PgVectorIndex>();
            services.AddSingleton<IHybridRetriever, PgVectorHybridRetriever>();
            return "pgvector";
        }

        services.AddSingleton<InMemoryChunkStore>();
        services.AddSingleton<IVectorIndex, InMemoryVectorIndex>();
        services.AddSingleton<IKeywordRetriever, InMemoryKeywordRetriever>();
        services.AddSingleton<IHybridRetriever, InMemoryHybridRetriever>();
        return "in-memory";
    }

    private static AblationRunner BuildAblationRunner(IServiceProvider sp, string storeName, RagOptions options)
    {
        // Ablation over the in-memory path: dense-only, sparse-only, hybrid (no rerank), hybrid + rerank.
        // The pgvector path shares the same ablation shape but requires a live database; it is evaluated
        // externally when a connection string is configured.
        if (storeName != "in-memory")
        {
            // Return a runner with a single "hybrid" config pointing at the registered retriever so the
            // endpoint is always callable; ablation across legs requires the in-memory store.
            var hybrid = sp.GetRequiredService<IHybridRetriever>();
            return new AblationRunner([new AblationConfig("hybrid", "hybrid (pgvector leg active)", hybrid)]);
        }

        var store = sp.GetRequiredService<InMemoryChunkStore>();
        var embedder = sp.GetRequiredService<IEmbedder>();
        var vectorIndex = sp.GetRequiredService<IVectorIndex>();
        var keywordRetriever = sp.GetRequiredService<IKeywordRetriever>();
        var reranker = sp.GetRequiredService<IReranker>();

        var denseOnly = new DenseOnlyRetriever(vectorIndex, embedder);
        var sparseOnly = new SparseOnlyRetriever(keywordRetriever);
        var hybridRetriever = new InMemoryHybridRetriever(vectorIndex, keywordRetriever, embedder, options);

        IReadOnlyList<AblationConfig> configs =
        [
            new("dense-only",           "vector search (cosine, hash embedder), no sparse leg",    denseOnly),
            new("sparse-only",          "BM25 keyword leg only, no dense leg",                     sparseOnly),
            new("hybrid-no-rerank",     "RRF fusion of dense + sparse, no reranker",               hybridRetriever),
            new("hybrid-with-rerank",   "RRF fusion + LexicalCoverage reranker",                   hybridRetriever, reranker),
        ];

        return new AblationRunner(configs);
    }

    // "Real" only if present and not an unfilled <placeholder>.
    private static bool IsReal(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.TrimStart().StartsWith('<');
}
