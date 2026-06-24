# ADR 0004 — Hybrid retrieval (dense + sparse lexical) fused with Reciprocal Rank Fusion

## Context
Pure vector search misses **exact terms** like a process number (`1.234.567-8`), a statute article (`art. 40`), or a party name. Pure keyword search misses **paraphrase** ("termo inicial da prescrição" vs "quando começa a contar o prazo"). Legal queries need both.

## Decision
Run **two legs**, dense (HNSW cosine) and sparse (Postgres `ts_rank` full-text in production; in-memory BM25 in dev/tests), and fuse them with **Reciprocal Rank Fusion**: `score = Σ 1/(k + rank)`, `k = 60`. In production this is **one SQL statement** (two CTEs + `FULL OUTER JOIN` + RRF). RRF is a pure function (`ReciprocalRankFusion.Fuse`) reused by the in-memory path and unit-tested independently.

## Consequences
- Catches both semantic and exact-term matches, improving exact-term recall over vector-only.
- RRF needs **no per-corpus weight**. It fuses by rank, sidestepping the incomparable scales of cosine vs `ts_rank`. `k = 60` is the original paper's constant.
- Two legs cost more than one; bounded by `CandidatePoolSize` per leg before fusion.

## Alternatives considered
- **Weighted score blending** (`α·cosine + (1-α)·bm25`): requires normalising two incomparable score scales and tuning `α` per corpus. RRF avoids both.
- **Vector-only**: simpler, but demonstrably worse on exact-term legal queries.

## Triggers for reassessment
- `context_precision`/Success@1 degrades on the eval set → tune RRF `k`, rebalance `CandidatePoolSize` per leg, or add a cross-encoder reranker after fusion.
- One leg consistently dominates the fused top-k (the other adds no winners) → reconsider whether both legs are worth the cost, or weight by leg.
- The two-leg round-trip becomes a latency bottleneck at scale → revisit single-leg fallback or move fusion off the SQL path.
- An embedding/model upgrade closes the exact-term gap vector-only used to miss → re-benchmark hybrid vs vector-only before keeping both legs.
