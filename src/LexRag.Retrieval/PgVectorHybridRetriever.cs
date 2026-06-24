using LexRag.Core.Abstractions;
using LexRag.Core.Models;
using LexRag.Index;
using Npgsql;
using Pgvector;

namespace LexRag.Retrieval;

// Dense (HNSW cosine) and sparse (ts_rank) legs ranked and fused with RRF in one SQL round-trip.
public sealed class PgVectorHybridRetriever(
    NpgsqlDataSource dataSource,
    IEmbedder embedder,
    RagOptions options) : IHybridRetriever
{
    // $6 = dense weight, $7 = sparse weight; defaults of 1.0 produce the original equal-weight RRF.
    private const string Sql =
        """
        WITH vec AS (
            SELECT chunk_key, source_file, chunk_index, content,
                   ROW_NUMBER() OVER (ORDER BY embedding <=> $1) AS rank
            FROM document_chunks
            WHERE embedding IS NOT NULL
            ORDER BY embedding <=> $1
            LIMIT $3
        ),
        fts AS (
            SELECT chunk_key, source_file, chunk_index, content,
                   ROW_NUMBER() OVER (
                       ORDER BY ts_rank(to_tsvector('portuguese', content),
                                        plainto_tsquery('portuguese', $2)) DESC) AS rank
            FROM document_chunks
            WHERE to_tsvector('portuguese', content) @@ plainto_tsquery('portuguese', $2)
            ORDER BY ts_rank(to_tsvector('portuguese', content),
                             plainto_tsquery('portuguese', $2)) DESC
            LIMIT $3
        )
        SELECT COALESCE(v.chunk_key,   f.chunk_key)   AS chunk_key,
               COALESCE(v.source_file, f.source_file) AS source_file,
               COALESCE(v.chunk_index, f.chunk_index) AS chunk_index,
               COALESCE(v.content,     f.content)     AS content,
               COALESCE($6 * 1.0 / ($4 + v.rank), 0)
             + COALESCE($7 * 1.0 / ($4 + f.rank), 0)  AS score
        FROM vec v
        FULL OUTER JOIN fts f ON v.chunk_key = f.chunk_key
        ORDER BY score DESC
        LIMIT $5;
        """;

    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, int top, CancellationToken ct = default)
    {
        var embedding = (await embedder.EmbedAsync([query], ct))[0];

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await PgVectorIndex.ApplyEfSearchAsync(conn, options.EfSearch, ct);

        await using var cmd = new NpgsqlCommand(Sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter { Value = new Vector(embedding) });
        cmd.Parameters.Add(new NpgsqlParameter { Value = query });
        cmd.Parameters.Add(new NpgsqlParameter { Value = options.CandidatePoolSize });
        cmd.Parameters.Add(new NpgsqlParameter { Value = options.RrfK });
        cmd.Parameters.Add(new NpgsqlParameter { Value = top });
        cmd.Parameters.Add(new NpgsqlParameter { Value = options.DenseWeight });
        cmd.Parameters.Add(new NpgsqlParameter { Value = options.SparseWeight });

        return await PgVectorIndex.ReadChunksAsync(cmd, ct);
    }
}
