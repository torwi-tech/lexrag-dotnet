# ADR 0001 — Vector store behind a port; pgvector via raw SQL as the default adapter

## Context
The system needs a vector store. The stack lists `Microsoft.Extensions.VectorData` (the provider-agnostic SK abstraction with one-line provider swaps to Qdrant/Redis). We also need full control of a *hybrid* query (dense + full-text + RRF) in a single statement, and the build must stay green with prerelease packages.

## Decision
Define our own narrow port, `IVectorIndex` (and `IHybridRetriever`), and ship two adapters:
- **`PgVectorIndex` / `PgVectorHybridRetriever`**: **raw Npgsql + the `vector` type**. This is the default "production-leaning" path. Raw SQL is deliberate. The hybrid query fuses a HNSW cosine leg and a `ts_rank` full-text leg with RRF in one round-trip, which a generic `SearchAsync` connector call cannot express.
- **`InMemoryVectorIndex`**: brute-force cosine, for dev and tests (no Docker).

`Microsoft.Extensions.VectorData` connectors remain the **on-ramp for additional stores** (Qdrant, Redis, Azure AI Search): a new provider is a new `IVectorIndex` adapter, nothing downstream changes.

## Consequences
- Full control of the index (HNSW params), the distance operator, and the hybrid SQL.
- Insulated from churn in the prerelease `Connectors.PgVector` API.
- Cost: we hand-write SQL and the in-memory adapter instead of leaning on a connector. For a single store that is a net win; for many stores the connector abstraction would pay off.

## Alternatives considered
- **Use the SK `PostgresVectorStore` connector directly.** Cleaner for simple vector search, but its `SearchAsync` does not do BM25 + RRF fusion, and the preview API shifted across versions, a build-stability risk for an unattended build.
- **EF Core (with `FromSqlRaw`/`SqlQuery` for the hybrid query).** A fair alternative. EF runs raw SQL too, so "control of the hybrid SQL" is **not** an argument against it: the hybrid query (`<=>` + `ts_rank` + RRF) would be the same hand-written SQL string inside `FromSqlRaw`. The real trade-off is what EF adds *around* the query: **migrations**, CRUD/change-tracking, type-safe LINQ, and provider portability (useful in shops standardized on another RDBMS, such as SQL Server). Raw Npgsql was chosen for a *leaner PoC* (fewer dependencies, SQL fully visible, append-mostly workload, ordinal mapping is acceptable at this size). **For a production deployment the stronger choice is EF Core for schema/migrations/CRUD + `FromSqlRaw`/`SqlQuery` for the pgvector-specific hybrid query**, with raw SQL only where the ORM genuinely cannot help.

## Triggers for reassessment
- A second store is needed (Qdrant/Redis/Azure AI Search) → implement a new `IVectorIndex`/`IHybridRetriever` adapter, not rewrite the port.
- The `Connectors.PgVector` API stabilises (GA) and gains BM25 + RRF fusion → reconsider replacing the raw-SQL adapter with the connector.
- Hand-written Npgsql SQL becomes a maintenance burden across multiple stores → the generic connector abstraction starts to pay off.
- Schema needs relational features beyond append-mostly chunk rows (joins, migrations, cascades) → re-evaluate the no-EF-Core decision.
