# ADR 0011 — Three-way RAG evaluation: deterministic gate, LLM-judge, managed benchmark

## Context
A RAG system can pass `dotnet build` and every unit test and still be *wrong in the way that matters*: it can answer fluently from bad context, drop citations, or fail to refuse out-of-domain questions. Quality here is a measured property, not a compiled one. The question is **how** to measure it without (a) making CI depend on a paid LLM, (b) fooling ourselves with a cheap metric that doesn't track real quality, or (c) hand-waving "it looks good."

Three families of evaluation are available to a .NET shop, and they are not interchangeable. They trade cost, determinism and depth differently:

1. **Hand-rolled deterministic metrics**, already built here: `RagEvaluator` (faithfulness / citation-presence / refusal-correctness) and `RetrievalEvaluator` (Recall@K / Hit-rate@K / MRR over a golden set, with `ExpectedSourceFiles` as the qrels). Keyless, deterministic, millisecond-fast.
2. **`Microsoft.Extensions.AI.Evaluation`**, the .NET-native **LLM-as-judge**: `Groundedness`, `Relevance(Truth)`, `RetrievalEvaluator`, `Completeness`. Needs a real chat model. Qualitative, non-deterministic.
3. **Azure AI Foundry evaluation**, managed cloud evaluation (e.g. `document_retrieval` with qrels), dashboards, run history. Needs an Azure account. Managed, reportable, vendor-coupled.

## Decision
Adopt a **layered evaluation strategy**, each layer at the cadence its cost/determinism allows, and **benchmark the layers against each other for *agreement*, not for "who scores highest."**

- **Layer 1, deterministic gate (keyless, every PR/CI run).** The hand-rolled metrics are the *gate*. They run in CI with no key and no Docker (ADR 0008). A drop in Hit-rate@K/MRR below a conservative floor fails the CI `eval-regression` job before merge. Faithfulness/citation/refusal run in the unit/e2e test suite.
  - **What the CI gate actually checks.** The `eval-regression` CI job starts the API in `Testing` mode (keyless fakes, no key needed), calls `POST /eval/retrieval`, and asserts Recall@K >= 0.50, Hit-rate@K >= 0.70, MRR >= 0.50 over the **committed curated corpus** (14 labeled queries; 5 súmula files). The 150-query JurisTCU golden set is large (~91 MB) and is NOT committed, so it is NOT part of the CI gate. The JurisTCU baselines (hit-rate 95%, recall 26%, MRR 0.73, lexical) are documented in `Docs/eval-datasets.md` and are reproducible locally via `scripts/eval-reproduce.sh` after the dataset is fetched. The CI floors were chosen safely below the curated-set values a correct keyless pipeline produces; they catch any retrieval regression but make no claim about the JurisTCU numbers.
  - **Retrieval metric is Recall@K, reported distinctly from Hit-rate@K.** A golden case can require *multiple* sources (multi-hop questions whose answer needs two passages). Recall@K measures *of the N required sources, how many appear in top-K*; Hit-rate@K measures *at least one*; MRR is the rank of the first relevant source. The three diverge on multi-hop cases: a case with two required sources where only one is retrieved scores hit-rate 1.0 but recall 0.5.
  - **Layer 1 faithfulness is an explicit lexical/citation proxy** (`LexicalFaithfulness` = grounded AND correct citation), coherent with the deterministic extractive chat fake. It is *not* semantic faithfulness, which is what Layer 2's LLM-judge adds. The name carries the honesty so the number is never read as more than it is.
- **Layer 2, LLM-judge (key-gated, nightly/pre-release).** `Microsoft.Extensions.AI.Evaluation` behind an opt-in evaluator, gated on a real key via a `[SkippableFact]` (skips with no key, exactly like the Testcontainers integration tests). It judges *semantic* groundedness/relevance that the lexical gate cannot, but it costs money, adds latency, and is non-deterministic (run with temperature 0 and report variance). It does **not** gate merges.
- **Layer 3, managed benchmark (account-gated, periodic).** An Azure AI Foundry exporter behind a port, for managed runs/dashboards and cross-system comparison. Useful for a reportable, shareable quality history; not part of the inner loop.

**The benchmark question is correlation, not ranking.** A judge scoring 0.8 and the deterministic metric scoring 0.6 does not mean one is "better"; they measure different things. What we measure is:
- **Agreement:** on the same golden set, do Layer 1 and Layer 2 *rank the same answers* as good/bad? High agreement means the cheap deterministic gate is a trustworthy CI proxy and the judge is reserved for depth. **Divergence is the valuable finding**, since it pinpoints where the lexical proxy misleads (e.g. a faithful answer phrased without lexical overlap that the cheap metric under-scores).
- **Cost / latency / determinism:** deterministic = free / instant / reproducible; LLM-judge = per-eval cost + latency + variance; Foundry = managed cost + network + vendor coupling.
- **Fitness by cadence:** gate (every PR) uses Layer 1; nightly/release uses Layer 2; reportable/cross-system uses Layer 3.

Implementation note: Layer 2 is now wired and run against a real Azure key (see Results below); Layer 3 (Foundry) requires a managed account and stays **specced, wired when the account exists**, since shipping eval code I cannot run would repeat the "build-green ≠ runtime-correct" trap. Layer 1 stays the keyless source of truth.

## Consequences
- CI stays **keyless, deterministic and free** while still gating on real quality (faithfulness/citation/refusal + retrieval metrics), with no paid dependency in the merge path.
- The deterministic gate is *checked as a proxy* (n=25, exploratory) against the agreement benchmark instead of trusted blindly; where it diverges from the judge, we know exactly which dimension to distrust.
- Three numbers that look comparable are explicitly **not** treated as a leaderboard. The doc and the benchmark report frame them as different instruments, which is the honest story to tell out loud.
- Layers 2/3 are opt-in and behind ports/skippable tests, so the portfolio runs end-to-end today without a key, and the judge/managed layers light up by config when a key/account is present.
- Cost is bounded: the expensive layers run on a cadence (nightly/release), not per PR.

## Results — first agreement run (2026-06-22)

Layers 1 and 2 run against the same golden set on the real stack: JurisTCU (16,045 documents), Azure OpenAI `text-embedding-3-small` for retrieval and `gpt-4.1-mini` for generation and as the judge. Retrieval over 150 queries, k=10, cutoff `SCORE >= 1`:

| Embedder | Recall@10 | Hit-rate@10 | MRR |
|---|---|---|---|
| Lexical (hash, keyless) | 26% | 95% | 0.73 |
| Semantic (Azure) | 42% | 100% | 0.88 |

Layer 2 LLM-judge (`Microsoft.Extensions.AI.Evaluation` Groundedness + Relevance, 25 in-domain cases, mean ± sd over 5 runs):

| Metric | Value |
|---|---|
| Mean groundedness (judge, 1–5) | 5.0 ± 0 |
| Mean relevance (judge, 1–5) | 4.5 ± 0.09 |
| Layer 1 faithful (deterministic gate) | 84% (21/25) |
| Layer 2 grounded (judge ≥ 4) | 100% |
| **Agreement (L1 == L2)** | **84% ± 0** (21/25) |

The 16% disagreement is **entirely one-directional**: every divergent case is one the lexical gate marks *not faithful* while the judge scores groundedness 5/5 (`acréscimos e supressões`, `parcelas de maior relevância e valor significativo`, `despesa sem cobertura contratual`, `fraude a licitação`). The lexical gate never passed an answer the judge rejected. Two readings, both the ones this strategy was built to surface:

1. **The deterministic gate is conservative, not wrong.** Its strict citation-overlap check (`CitationCorrectness >= 1.0`) under-counts: it fails answers that are semantically grounded but phrased without the exact lexical citation form. As a *merge gate* that is the safe direction (no false pass was observed in this sample); as a *quality estimate* it is pessimistic, and the judge recovers the false negatives, exactly the divergence the "Triggers for reassessment" section anticipated.
2. **Groundedness saturates at 5/5 on this sample, consistent with the structural anti-hallucination** (ADR 0006): the generator answers only from retrieved context, and the semantic judge did not detect drift on these 25 cases. The result supports the structural grounding strategy but does not prove it. Because groundedness saturates at the ceiling, the durable finding is the one-directional divergence (the lexical gate is conservative), not the absolute judge score. Relevance at 4.5 (not saturated) shows the judge is still discriminating.

Stability: across **5 runs** over the same fixed answers (so the spread isolates judge variance, not generation), agreement holds at **84% ± 0 pp** and groundedness at **5.0 ± 0**; the only non-saturated metric, relevance, sits at **4.5 ± 0.09**. The judge is effectively deterministic on this golden set at temperature 0, so the 16% divergence is signal, not sampling noise (σ well under a 3-pp bar). Note this ± is *judge* variance over fixed answers; a separate confirmatory re-run that re-embedded the corpus and re-generated answers ([`eval-results/2026-06-22-juristcu-rerun.json`](../../eval-results/2026-06-22-juristcu-rerun.json)) gave relevance 4.61 and recall@10 42.3%. The *between-run* variance (corpus re-seeded, ANN order, fresh generation) is wider than the judge-only ± here, and both stay within the indicative band the provenance note describes.

Caveats kept explicit: **25 cases**, agreement **21/25** (the judge is paid per call, so n is small by design); groundedness saturating at 5/5 is part of *why* agreement shows zero spread (there is no room to move above the threshold), so the stability rests on the constant divergence set and the relevance spread; the judge is `gpt-4.1-mini`, while the `Microsoft.Extensions.AI.Evaluation` groundedness prompt is tuned for GPT-4o, so a stronger judge could shift the absolute scores. The *direction* of the divergence is the durable finding; the absolute judge scores are indicative.

Provenance: these numbers are from a **local run against Azure OpenAI**, not from CI: `ci.yml` runs the keyless Layer 1 only (the judge skips without a key, by design). The raw output and the parameters are versioned at [`eval-results/2026-06-22-juristcu.json`](../../eval-results/2026-06-22-juristcu.json); with a key, `POST /eval/retrieval` and `POST /eval/judge` (`Eval__JudgeRuns=5`) reproduce them within the reported spread.

## Alternatives considered
- **RAGAS (Python):** widely-cited RAG metrics (faithfulness, answer/context relevancy, context recall). Rejected as the primary path for a .NET shop: it forces a second toolchain and a subprocess/HTTP bridge in CI, with cross-runtime version friction. Kept as a **conceptual reference** for what the .NET-native `Microsoft.Extensions.AI.Evaluation` metrics should approximate.
- **One approach only.** Just the deterministic gate is cheap but blind to semantics (can't catch a faithful-but-lexically-different answer). Just the LLM-judge can't gate CI (cost, non-determinism, key dependency). The layered split is what lets each do the job it's good at.
- **LLM-judge as the merge gate.** Rejected: non-deterministic and paid in the hot path; a flaky/cost-bearing gate erodes trust and blocks merges on model variance.

## Reference datasets and benchmarks

The data this strategy runs on, and the benchmarks that define each dimension, are catalogued in [`Docs/eval-datasets.md`](../eval-datasets.md). In short:

- **Ingested (CC BY 4.0):** [JurisTCU](https://arxiv.org/abs/2503.08379) (qrels → Recall@K/Hit-rate@K/MRR). [BR-TaxQA-R](https://huggingface.co/datasets/unicamp-dl/BR-TaxQA-R) (QA with references → faithfulness/citation) is **planned, not yet ingested**; its adapter is not built. [AI4Privacy](https://huggingface.co/datasets/ai4privacy/pii-masking-200k) is a candidate for a separate PII redaction gateway (roadmap; license to confirm).
- **Referenced, not ingested (PT-BR, license to confirm):** [JUÁ](https://arxiv.org/abs/2604.06098) (CC BY-NC-ND paper; dataset license unconfirmed) as a second Portuguese legal IR track, and [LeNER-Br](https://huggingface.co/datasets/peluz/lener_br) (HF license "unknown") for doc-triage NER.
- **Dimension methodology (English/EU):** [LegalBench-RAG](https://github.com/zeroentropy-ai/legalbenchrag) (retrieval), [RAGTruth](https://arxiv.org/abs/2401.00396) (faithfulness), [ALCE](https://arxiv.org/abs/2305.14627) (citation), [LegalBench](https://github.com/HazyResearch/legalbench/) / LexGLUE / LEXTREME (legal reasoning). The PT-BR equivalents are scarce, and that gap is the point.

## Triggers for reassessment
- The agreement benchmark shows the deterministic gate **diverging** from the judge on real queries → promote the judge (or a cheap calibrated variant) into CI for the diverging slice, or add a deterministic metric that captures the missed dimension.
- LLM-judge cost/latency makes even nightly runs painful → sample the golden set, or distill the judge's rubric into a cheaper deterministic check.
- Azure AI Foundry coupling becomes a portability concern → keep Layer 3 strictly behind its port and treat it as one exporter among several (OpenTelemetry-style), not the system of record.
- The golden set grows/labels drift → re-establish the qrels and re-run the agreement benchmark before trusting any layer's numbers.
