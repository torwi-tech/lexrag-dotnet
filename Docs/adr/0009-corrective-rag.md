# ADR 0009 — Corrective RAG (CRAG) as an explicit, opt-in pipeline

## Context
Single-shot retrieve→generate fails on poorly-phrased or composed questions: if the first retrieval is weak, the model either answers on bad context or refuses. We want the system to *notice* weak retrieval and try to fix it (the agentic behaviour the domain expects, "decide when to re-search") without sacrificing auditability.

## Decision
Add **`CragPipeline`** alongside the default `RagPipeline`: **retrieve → GRADE → if weak, reformulate the query and re-retrieve (bounded by `MaxCorrectiveRounds`, default 1) → generate**, answering the *original* question grounded in the best context found. Every step is recorded in `RagAnswer.Trace`.

- The agentic decision is an **`IRetrievalGrader`** (`LexicalRetrievalGrader` = query-term coverage now; LLM grader behind the same port later).
- Reformulation is an **`IQueryReformulator`** (`KeywordQueryReformulator` = drop stopwords/noise, keep signal; LLM rewriter later).
- The loop is **explicit code, not a model-driven agent loop**: deterministic and auditable, matching ADR 0006's stance for regulated flows.
- CRAG is **opt-in** (`/ask/crag`), not the default, because it costs extra retrievals (and, with a real grader, extra LLM calls). It earns its keep on hard questions rather than easy ones.

## Consequences
- Noisy/verbose in-domain questions get corrected and grounded; the trace shows the grade and any re-retrieval (auditable).
- Anti-hallucination still holds: if correction fails to find grounded context, generation **refuses** (verified end-to-end).
- More cost per query when it loops, bounded by `MaxCorrectiveRounds`; the grade short-circuits when retrieval is already good.
- The heuristic grader/reformulator are stand-ins; CRAG gets more out of a real LLM grader (which can judge semantic sufficiency, not just lexical coverage).

## Alternatives considered
- **Always run CRAG**: wasteful on the common easy case; made it opt-in instead.
- **Model-driven tool-calling loop** (let the model decide to re-search via function calling): less auditable; kept as the separate `SemanticKernelRagService` agentic path. CRAG here is the deterministic, regulated-flow-friendly version.

## Triggers for reassessment
- The lexical grader/reformulator misjudge sufficiency (re-retrieving when context was fine, or not when it was weak) → upgrade to the LLM-backed `IRetrievalGrader`/`IQueryReformulator` behind the same ports.
- `MaxCorrectiveRounds = 1` proves too few on hard composed questions (eval shows wins from extra rounds) → raise the bound and re-measure cost vs gain.
- The extra-retrieval cost of CRAG outweighs its accuracy lift on the live query mix → keep it opt-in but narrow the questions routed to `/ask/crag`, or gate it on a cheap difficulty signal.
- A semantic LLM grader makes CRAG strong enough to be the default → reconsider making it the default pipeline instead of opt-in.
