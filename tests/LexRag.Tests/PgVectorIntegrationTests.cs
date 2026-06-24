using FluentAssertions;
using LexRag.Core.Models;
using LexRag.Embeddings;
using LexRag.Index;
using LexRag.Retrieval;
using Npgsql;
using Pgvector.Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace LexRag.Tests;

// Skips automatically when Docker is absent so the suite stays green everywhere.
public sealed class PgVectorIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private NpgsqlDataSource? _dataSource;

    public async Task InitializeAsync()
    {
        if (!DockerEnv.IsAvailable) return;

#pragma warning disable CS0618 // builder ctor; image is set explicitly via WithImage below
        _container = new PostgreSqlBuilder()
#pragma warning restore CS0618
            .WithImage("pgvector/pgvector:pg16")
            .WithDatabase("lexrag").WithUsername("lexrag").WithPassword("lexrag")
            .Build();
        await _container.StartAsync();

        var builder = new NpgsqlDataSourceBuilder(_container.GetConnectionString());
        builder.UseVector();
        _dataSource = builder.Build();
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null) await _dataSource.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
    }

    [SkippableFact]
    public async Task Ingested_chunk_has_self_similarity_near_one_and_hybrid_finds_it()
    {
        Skip.IfNot(DockerEnv.IsAvailable, "Docker is not available on this machine.");

        var options = new RagOptions();
        var embedder = new HashEmbedder(options.EmbeddingDimensions);
        var index = new PgVectorIndex(_dataSource!, options);
        await index.EnsureCreatedAsync();

        var content = "A prescrição intercorrente na execução fiscal corre após um ano de suspensão (art. 40).";
        var embedding = (await embedder.EmbedAsync([content]))[0];
        await index.UpsertAsync([new DocumentChunk
        {
            ChunkKey = Guid.NewGuid(), SourceFile = "stj.txt", ChunkIndex = 0, Content = content, Embedding = embedding,
        }]);

        (await index.CountAsync()).Should().Be(1);

        // Vector self-search: the chunk should be its own top hit with cosine ≈ 1.
        var hits = await index.SearchAsync(embedding, top: 1);
        hits.Should().ContainSingle();
        hits[0].Score.Should().BeApproximately(1.0, 1e-3);

        // Hybrid (vector + Portuguese full-text + RRF) should also surface it.
        var hybrid = new PgVectorHybridRetriever(_dataSource!, embedder, options);
        var fused = await hybrid.RetrieveAsync("prescrição intercorrente execução fiscal", top: 5);
        fused.Should().Contain(c => c.SourceFile == "stj.txt");
    }
}

internal static class DockerEnv
{
    public static bool IsAvailable =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST"))
        || File.Exists(@"\\.\pipe\docker_engine")
        || File.Exists("/var/run/docker.sock");
}
