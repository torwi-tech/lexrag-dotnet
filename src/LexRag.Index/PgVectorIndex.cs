using LexRag.Core.Abstractions;
using LexRag.Core.Models;
using Npgsql;
using Pgvector;

namespace LexRag.Index;

// Raw Npgsql (not a connector) for full control of the HNSW index, the <=> cosine operator, and the hybrid fusion SQL.
public sealed class PgVectorIndex(NpgsqlDataSource dataSource, RagOptions options) : IVectorIndex
{
    public async Task EnsureCreatedAsync(CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using (var cmd = new NpgsqlCommand(PgVectorSchema.CreateSql, conn))
            await cmd.ExecuteNonQueryAsync(ct);
        // The `vector` type now exists. Npgsql cached the type catalog on first connect, before the
        // extension existed on a fresh DB, so reload it or binding a `Vector` parameter fails.
        await conn.ReloadTypesAsync();
    }

    public async Task UpsertAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken ct = default)
    {
        if (chunks.Count == 0) return;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var batch = new NpgsqlBatch(conn);
        foreach (var c in chunks)
        {
            var command = new NpgsqlBatchCommand(
                """
                INSERT INTO document_chunks (chunk_key, source_file, chunk_index, content, embedding)
                VALUES ($1, $2, $3, $4, $5)
                ON CONFLICT (chunk_key) DO UPDATE
                  SET source_file = EXCLUDED.source_file,
                      chunk_index = EXCLUDED.chunk_index,
                      content     = EXCLUDED.content,
                      embedding   = EXCLUDED.embedding;
                """);
            command.Parameters.Add(new NpgsqlParameter { Value = c.ChunkKey });
            command.Parameters.Add(new NpgsqlParameter { Value = c.SourceFile });
            command.Parameters.Add(new NpgsqlParameter { Value = c.ChunkIndex });
            command.Parameters.Add(new NpgsqlParameter { Value = c.Content });
            command.Parameters.Add(new NpgsqlParameter { Value = new Vector(c.Embedding) });
            batch.BatchCommands.Add(command);
        }
        await batch.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteBySourceFileAsync(string sourceFile, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            "DELETE FROM document_chunks WHERE source_file = $1;");
        cmd.Parameters.Add(new NpgsqlParameter { Value = sourceFile });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(float[] queryEmbedding, int top, CancellationToken ct = default)
    {
        const string sql =
            """
            SELECT chunk_key, source_file, chunk_index, content,
                   1 - (embedding <=> $1) AS score
            FROM document_chunks
            WHERE embedding IS NOT NULL
            ORDER BY embedding <=> $1
            LIMIT $2;
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await ApplyEfSearchAsync(conn, options.EfSearch, ct);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter { Value = new Vector(queryEmbedding) });
        cmd.Parameters.Add(new NpgsqlParameter { Value = top });

        return await ReadChunksAsync(cmd, ct);
    }

    public static async Task ApplyEfSearchAsync(NpgsqlConnection connection, int efSearch, CancellationToken ct)
    {
        // `efSearch` is an int from config (not user text), and Postgres SET takes no bound parameter,
        // so this is controlled int interpolation. Clamp anyway so a bad override can't reach the SET.
        var safe = Math.Clamp(efSearch, 1, 1000);
        await using var cmd = new NpgsqlCommand($"SET hnsw.ef_search = {safe};", connection);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand("SELECT count(*) FROM document_chunks;");
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    public static async Task<IReadOnlyList<RetrievedChunk>> ReadChunksAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var results = new List<RetrievedChunk>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new RetrievedChunk
            {
                ChunkKey = reader.GetGuid(0),
                SourceFile = reader.GetString(1),
                ChunkIndex = reader.GetInt32(2),
                Content = reader.GetString(3),
                Score = reader.GetDouble(4),
            });
        }
        return results;
    }
}
