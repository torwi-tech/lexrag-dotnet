# ADR 0003 — HNSW index (m=16, ef_construction=128) over IVFFlat

## Context
pgvector supports two ANN indexes: IVFFlat and HNSW. We need good recall/latency on a corpus that grows incrementally (documents arrive over time).

## Decision
Use **HNSW** with `m = 16, ef_construction = 128`, and tune **`ef_search` at query time** (`SET hnsw.ef_search`) to trade recall for latency without rebuilding.

## Consequences
- Better recall/latency than IVFFlat at our scale, and **no training step**. IVFFlat needs representative data to build its lists, which is awkward when the corpus is still filling.
- `ef_construction = 128` (vs the library default 64) spends more at build time for better graph quality; `m = 16` is the standard connectivity default.
- HNSW indexes use more memory and are slower to build than IVFFlat, which is acceptable for a read-heavy retrieval workload.

## Alternatives considered
- **IVFFlat**: smaller/faster to build, but needs training data and gives lower recall at the same latency; a poor fit for an incrementally growing index.
- **No index (exact scan)**: fine for the in-memory demo, untenable in production.

## Triggers for reassessment
- Recall drops below target at scale even after raising `ef_search` → benchmark IVFFlat vs raising `ef_construction`/`m` and rebuilding.
- The HNSW graph exceeds the instance memory budget → consider IVFFlat or a managed/disk-based vector DB.
- Ingestion becomes write-heavy and HNSW build/insert cost dominates → reassess index type or move to batched offline index builds.
- Query p95 latency regresses as the corpus grows → re-tune `ef_search`, or partition/shard the index.
