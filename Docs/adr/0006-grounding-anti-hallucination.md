# ADR 0006 — Structural grounding + citation + refusal

## Context
RAG does **not** eliminate hallucination: Lexis/Westlaw legal assistants still hallucinate 17–33%. In justice, an unsourced or invented answer is a compliance failure (the supervision/citation expectations of Res. CNJ 615). Grounding, citation and refusal are hard requirements here.

## Decision
Three structural guarantees enforced in code, not by prompt wording alone:
1. **No context ⇒ no model call.** `GroundingPrompt.ShouldRefuse` short-circuits to the fixed refusal "Não encontrei nos documentos fornecidos." before any LLM is invoked. The model cannot invent what it is never asked.
2. **Citation required and checked.** The prompt mandates `[Fonte: {arquivo}, trecho {n}]`; `CitationParser` extracts citations and an answer is only marked `Grounded` if every citation maps to a retrieved chunk (`CitationParser.Correctness` == 1.0) and it is not the refusal phrase. A hallucinated source is never reported as grounded.
3. **Eval gate.** `RagEvaluator` measures lexical faithfulness (a deterministic proxy; semantic faithfulness is Layer 2, ADR 0011), citation correctness, and refusal accuracy on out-of-domain questions, run per release.

The key-free `ExtractiveChatClient` honours the same contract (it answers only from the retrieved passage and refuses when no meaningful query term is grounded), so the guarantee is testable without an LLM.

## Consequences
- Out-of-domain questions refuse instead of inventing (unit-tested + in the eval set).
- "Grounded" is a checkable property; citation correctness is measurable.
- A real model can still hallucinate *within* provided context. The risk is mitigated, not eliminated, which is why the eval gate and human review exist (see [agentic-workflow-dotnet](https://github.com/torwi-tech/agentic-workflow-dotnet) and ADR 0007).

## Alternatives considered
- **Prompt-only grounding**: relies on the model obeying. We keep the prompt but add the structural short-circuit and the citation check so a disobedient model still cannot pass off an ungrounded answer as grounded.

## Triggers for reassessment
- Lexical faithfulness or citation-correctness drops on the eval gate after a model swap → tighten the prompt contract or add a post-hoc verifier before serving answers.
- Refusal accuracy degrades (in-domain over-refusal or out-of-domain leakage) → re-tune `GroundingPrompt.ShouldRefuse` thresholds and the grounded-term check.
- The mandated `[Fonte: {arquivo}, trecho {n}]` format changes or a model emits citations the `CitationParser` can't extract → update the parser and the "Grounded" criterion together.
- Regulatory expectations evolve beyond citation + refusal (e.g. Res. CNJ 615 updates) → reassess what structural guarantees are required.
