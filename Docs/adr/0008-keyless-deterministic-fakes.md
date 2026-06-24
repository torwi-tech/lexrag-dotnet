# ADR 0008 — Deterministic fakes so the build/tests need no key or Docker

## Context
The two external dependencies of a RAG system are an embedding/LLM provider (needs an API key) and a vector database (needs Docker). Neither should be required to build, test, or demo the architecture.

## Decision
Put **every** external call behind a port and ship a deterministic fake for each:
- **`HashEmbedder`** (`IEmbedder`): signed feature hashing with **FNV-1a** (not `String.GetHashCode`, which is per-process randomised). Identical text → identical 1536-d vector (self-cosine ≈ 1.0); lexical overlap → positive cosine. Captures no real semantics, but makes retrieval rank sensibly in tests.
- **`ExtractiveChatClient`** (`IChatClient`): answers only from the retrieved passage with its citation, and refuses when no meaningful query term is grounded. A faithful, dumb stand-in for the grounding contract.
- **`InMemoryVectorIndex` + `InMemoryHybridRetriever`**: the full retrieve path (vector + BM25 + RRF) with no database.

The composition root selects real vs fake by presence of a key / connection string. Nothing downstream knows which is active.

## Consequences
- `dotnet build` + `dotnet test` are green on a machine with **no key and no Docker** (this build was produced exactly so).
- The same code runs against Azure OpenAI + pgvector by setting config, with no code change.
- The fakes are explicitly **not** quality stand-ins; they exist to exercise wiring and logic, and the README/HANDOFF say so plainly.

## Alternatives considered
- **Mocking frameworks in tests only**: would not let the *app* run end-to-end keyless (the demo and the eval endpoint both need a working pipeline).
- **Recorded fixtures (VCR-style)**: heavier, and a hash embedder is enough to make retrieval deterministic.

## Triggers for reassessment
- A real provider (Azure OpenAI + pgvector) is wired as the default path → demote the fakes to test-only and stop running the demo/eval against them.
- Retrieval/eval quality needs to be measured for real (not just wiring/logic) → `HashEmbedder` is insufficient; bring in real embeddings or recorded fixtures for the eval set.
- The eval gate (ADR 0006) must assert semantic faithfulness, which `ExtractiveChatClient` can't model → swap in a real `IChatClient` for the evaluated runs.
- CI gains a key/Docker budget and integration coverage matters more than keyless builds → add Testcontainers + live-provider tests alongside the fakes.
