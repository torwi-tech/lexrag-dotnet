# Limitations

I built LexRAG as a proof-of-concept to exercise a legal-RAG architecture end to end, not as a production system. Here is what it does **not** do yet, and how each gap would be closed. The same items, with file locations and escalation paths, are the limitations table in [`Docs/architecture.md`](Docs/architecture.md).

## Stand-ins, not the real thing

- **The default embedder and chat model are deterministic fakes:** a hash embedder (FNV-1a) and an extractive chat client. They let the build and the whole test suite run with no key and no Docker, but the hash embedder is a *lexical* proxy, not semantic. A real `IEmbedder` / `IChatClient` (Azure OpenAI) drops in by configuration; the retrieval gain measured with it (recall 26% lexical → 42% semantic) is in [`Docs/eval-datasets.md`](Docs/eval-datasets.md).
- **Reranking is a lexical-coverage stand-in** (`LexicalCoverageReranker`): it ranks by query-term coverage, with the RRF score breaking ties. A cross-encoder drops in behind the same `IReranker` port.
- **The CRAG grader and query reformulator are heuristics** (term coverage; keyword reduction that strips stopwords rather than adding terms), stand-ins for LLM-backed versions behind `IRetrievalGrader` / `IQueryReformulator`.

## Measured honestly, with the gaps named

- **Layer 1 faithfulness is a lexical/citation proxy** (`LexicalFaithfulness`), not semantic. Semantic faithfulness comes from the Layer 2 LLM-judge ([ADR 0011](Docs/adr/0011-eval-three-way-strategy.md)), which is key-gated and runs outside CI, not on every merge.
- **The agreement benchmark is small (25 cases) and the judge is non-deterministic.** I quantified the variance (84% ± 0 pp over 5 runs) rather than report a single number, and I note that groundedness saturating at 5/5 is part of *why* the spread is zero.
- **The score gate is implemented and tested** (`MinRelevanceScore = 0.0` by default): `GroundedAnswerGenerator` refuses when every retrieved chunk scores below `MinRelevanceScore`, the same refusal path as empty context. The default is 0.0 so the gate is inactive until calibrated on real data; see `tests/LexRag.Tests/ScoreGateTests.cs`.
- **Evaluation is offline only.** There is no online sampling of production traffic with alerting below a threshold.
- **Perturbation robustness has an initial harness** (`PerturbationEvaluator`): deterministic surface-form perturbations (casing, whitespace noise, stopword insertion, term reversal) are applied to each golden-set query and the hit-rate delta per perturbation type is reported. The harness is keyless and runs without Docker. It does not yet cover semantic paraphrases or adversarial distractor injection; those require LLM-generated variants and are the next step.
- **Layer 3 (Azure AI Foundry managed benchmark) is specced, not wired.**

## Out of scope for a proof-of-concept

- **The in-memory store is O(n)**; pgvector (HNSW) is the production path and the adapter already exists.
- **This PoC is single-tenant.** Multi-tenant isolation, tenant-aware retrieval and Row-Level Security are out of scope.
- **No frontend, no cloud deployment, no streaming, no fine-tuning, no formats beyond PDF and text.**

None of these are hidden: each is an [ADR](Docs/adr/) or a row in the architecture limitations table, and each has an escalation path that does not change a port.
