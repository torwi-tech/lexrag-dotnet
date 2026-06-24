# Architecture Decision Records — LexRAG

Short, honest records of the decisions that shape this spike. Each follows: **Context → Decision → Consequences → Alternatives considered**. They exist so the trade-offs can be defended out loud, not just shipped.

| # | Decision |
|---|----------|
| [0001](0001-vector-store-port-and-pgvector.md) | Vector store behind a port; pgvector via raw SQL is the default adapter |
| [0002](0002-cosine-distance.md) | Cosine distance for legal text |
| [0003](0003-hnsw-index.md) | HNSW index (m=16, ef_construction=128) over IVFFlat |
| [0004](0004-hybrid-retrieval-rrf.md) | Hybrid retrieval (dense + sparse lexical) fused with RRF |
| [0005](0005-structural-chunking.md) | Sentence-packing chunking with overlap |
| [0006](0006-grounding-anti-hallucination.md) | Structural grounding + citation + refusal |
| [0007](0007-confidentiality-data-boundary.md) | Confidentiality / data boundary |
| [0008](0008-keyless-deterministic-fakes.md) | Deterministic fakes so the build/tests need no key or Docker |
| [0009](0009-corrective-rag.md) | Corrective RAG (CRAG) as an explicit, opt-in pipeline |
| [0010](0010-lgpd-data-boundary.md) | LGPD: data boundary for LLM calls and telemetry |
| [0011](0011-eval-three-way-strategy.md) | Three-way eval: deterministic gate + LLM-judge + managed benchmark (agreement, not ranking) |
| [0012](0012-regulatory-conformance-mapping.md) | Regulatory conformance mapping (CNJ Res. 615/2025 + LGPD): technical guarantees to normative provisions |

Each ADR ends with a **Triggers for reassessment** section — the conditions under which the decision should be revisited.
