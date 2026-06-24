# Evaluation datasets and benchmarks

How LexRAG's evaluation is grounded in real data. It covers the datasets the project uses, the ones it references but doesn't ingest (and why), and the known benchmarks behind each quality dimension we measure. It also names the gap: most standards for these dimensions are English or EU, and Brazilian-Portuguese legal resources are scarce.

The evaluation strategy itself (deterministic gate, LLM-judge, managed benchmark) is described in [ADR 0011](adr/0011-eval-three-way-strategy.md). This document is about the *data* that strategy runs on.

## 1. Datasets we use (real corpus, real relevance judgments)

These have permissive licenses and map onto the eval code through an `ICorpusSource` port, wired as opt-in adapters so the build still runs offline by default (the curated súmula samples stay the keyless default).

| Dataset | License | What it is | Status in LexRAG |
|---|---|---|---|
| [JurisTCU](https://arxiv.org/abs/2503.08379) | CC BY 4.0 | 16,045 jurisprudence documents from the Brazilian Federal Court of Accounts (TCU) plus 150 queries with graded relevance judgments (qrels, `SCORE` 0–3), in three groups: real-keyword, synthetic-keyword, synthetic-question | **Ingested.** The qrels map onto `EvalCase.ExpectedSourceFiles`, and `RetrievalEvaluator` computes Recall@K / Hit-rate@K / MRR. Baseline measured below. |
| [BR-TaxQA-R](https://huggingface.co/datasets/unicamp-dl/BR-TaxQA-R) | CC BY 4.0 (to confirm on ingestion) | 478 question-answer pairs with references (Brazilian personal income tax), 478 legal documents, and 7,204 CARF rulings | **Planned, not yet built.** Fits the answer eval (questions with references would feed `RagEvaluator`'s grounding/citation checks). The HF dataset ships a deprecated script-based loader, so the adapter will read the JSON files directly. |

PII corpora (AI4Privacy and similar) target a redaction layer that would live in a separate gateway service (a planned companion, not part of LexRAG); they are covered under [dimension benchmarks](#3-dimension-benchmarks-methodology-references), not here.

CC BY 4.0 requires attribution; see [Attribution](#attribution).

### Running against JurisTCU

The dataset is not committed (it is large and stays out of git). Fetch the three CSV files into a local cache, then point the app at them:

```bash
mkdir -p data/juristcu
base="https://huggingface.co/datasets/LeandroRibeiro/JurisTCU/resolve/main"
curl -L "$base/doc.csv"   -o data/juristcu/doc.csv     # ~91 MB corpus
curl -L "$base/query.csv" -o data/juristcu/query.csv
curl -L "$base/qrel.csv"  -o data/juristcu/qrel.csv

# select the JurisTCU adapters (default is the offline curated set)
export Eval__Dataset=juristcu
export Eval__JurisTcuPath="$(pwd)/data/juristcu"
dotnet run --project src/LexRag.Api
curl -s -X POST localhost:5007/eval/retrieval
```

The corpus document text is the `ENUNCIADO` field (the holding), with HTML stripped; relevance comes from `qrel.csv` (`SCORE >= 1` by default). `Eval__JurisTcuMaxQueries` and `Rag__TopK` let you cap the run.

### Baseline: keyless lexical retrieval

This run uses the deterministic hash embedder (no API key), over all 16,045 documents and the 150 official queries, at k=10. The lexical baseline is fully reproducible keyless (no Azure account needed); the semantic numbers below require a real Azure OpenAI run. JurisTCU's qrels are graded (`SCORE` from 0 to 3), so recall depends entirely on where the relevance line is drawn. The default `RelevanceCutoff` is 1, meaning any judged-relevant document counts. Same run, three cutoffs:

| Relevance cutoff | Relevant/query | Hit-rate@10 | Recall@10 | MRR |
|---|---|---|---|---|
| `SCORE >= 1` | 12.1 | 95% | 26% | 0.73 |
| `SCORE >= 2` | 8.6 | 94% | 38% | 0.72 |
| `SCORE >= 3` (only the most relevant) | 5.3 | 90% | 53% | 0.65 |

Two readings. First, the cutoff moves the number as much as the retriever does: raise the bar and the relevant set shrinks, the denominator with it, and recall climbs from 26% to 53%. A recall figure means nothing without the cutoff that produced it, which is the whole reason this table exists. Second, even at the strictest bar the lexical embedder turns up something relevant for about 90% of queries and usually near the top (MRR 0.65 to 0.73), yet it never covers most of the relevant set. That coverage gap is the ceiling of keyword matching, and closing it is what a real embedding model (Azure OpenAI) should do. The comparison is below.

#### Per-group breakdown (lexical baseline, SCORE >= 1, k=10)

JurisTCU tags each query's provenance in a `SOURCE` column, which splits the 150 queries into three groups of 50: `real-keyword` (terse queries from real TCU search logs), `synthetic-keyword` (keyword expressions an LLM generated from questions), and `synthetic-question` (full natural-language questions from an LLM). The `/eval/retrieval` endpoint reports this split in its `byGroup` field. The numbers below come from a measured keyless run (hash embedder, full 16,045-document corpus, 2026-06-23), saved as [`eval-results/2026-06-23-juristcu-lexical-bygroup.json`](../eval-results/2026-06-23-juristcu-lexical-bygroup.json):

| Group | n | Hit-rate@10 | Recall@10 | MRR |
|---|---|---|---|---|
| real-keyword | 50 | 84% | 18% | 0.48 |
| synthetic-keyword | 50 | 100% | 31% | 0.79 |
| synthetic-question | 50 | 100% | 30% | 0.91 |
| **aggregate** | **150** | **95%** | **26%** | **0.73** |

The split runs opposite to the intuitive guess, and that is the useful part. Lexical retrieval is weakest on the real user queries: short search-log terms (`real-keyword`) put a relevant document near the top only about half the time (MRR 0.48) and cover the least of the relevant set (recall 18%). The LLM-written queries share more distinctive vocabulary with the holding text, so keyword matching ranks them well (MRR 0.79 and 0.91). The implication lines up with the aggregate: the queries that matter most in production are the ones a keyword retriever serves worst, which is the clearest case for the semantic embedder below.

### Semantic retrieval (Azure OpenAI)

Same corpus, same 150 queries, same k=10 and cutoff `SCORE >= 1`, swapping only the embedder: `text-embedding-3-small` via Azure OpenAI (`MeaiEmbedder`) in place of the hash proxy, with no other code change since the embedder sits behind a port. Run 2026-06-22.

| Embedder | Hit-rate@10 | Recall@10 | MRR |
|---|---|---|---|
| Lexical (hash, keyless) | 95% | 26% | 0.73 |
| **Semantic (`text-embedding-3-small`)** | **100%** | **42%** | **0.88** |

The semantic embedder closes most of the coverage gap the keyword baseline could not: at the same relevance bar, recall climbs from 26% to 42% (about +62% relative), the first relevant document lands near the top almost every time (MRR 0.73 to 0.88), and nearly every query surfaces at least one relevant document (hit-rate 100% on this run). This is what the keyless baseline predicted: the lexical proxy finds *something* but does not *cover* the relevant set, and the semantic model does. The cutoff-sensitivity reading above still applies: this comparison is fixed at `SCORE >= 1`.

> These are the numbers from the canonical run ([`eval-results/2026-06-22-juristcu.json`](../eval-results/2026-06-22-juristcu.json)). An independent confirmatory re-run on a fresh pgvector container ([`-rerun.json`](../eval-results/2026-06-22-juristcu-rerun.json)) reproduced recall@10 at 42.3% and agreement at 84%, with hit-rate@10 at 98.7%; one or two queries shift on ANN index ordering (see [`eval-results/README.md`](../eval-results/README.md)). The durable finding is the recall lift; hit-rate hovers at ~99–100% across runs.

Refusal accuracy is not in this table. The JurisTCU golden set is entirely in-domain, so a refusal score over it reads 1.0 by construction (no out-of-corpus questions to refuse). Refusal is only meaningful on the curated súmula set, which includes deliberate out-of-corpus questions.

## 2. Datasets we reference but do not ingest

Documented for completeness and because knowing the landscape (and its licensing) is part of using datasets responsibly. Not ingested today.

| Dataset | Why not ingested | Could be used for |
|---|---|---|
| [JUÁ](https://arxiv.org/abs/2604.06098) | The paper is CC BY-NC-ND (non-commercial, no-derivatives); the dataset's own license and HuggingFace path are unconfirmed. Deferred until the dataset license is verified. | A second Portuguese legal IR track (jurisprudence + legislative/regulatory search) with relevance judgments. |
| [LeNER-Br](https://huggingface.co/datasets/peluz/lener_br) | HuggingFace lists the license as "unknown". Not redistributed/ingested without a confirmed license. | Named-entity recognition for the **doc-triage** field extractor (persons, organizations, legislation, legal-case references). |

## 3. Dimension benchmarks (methodology references)

The datasets above are Portuguese and legal. The *quality dimensions* LexRAG measures, however, have known benchmarks of their own, almost all English or multilingual-EU. We do not run these as data (they are out of domain and language), but they define the metrics our evaluation approximates, and the deterministic gate is designed so its keyless proxies line up with what these measure when a real model (Layer 2) is wired in.

| Dimension we measure | Reference benchmark | How our metric relates |
|---|---|---|
| Faithfulness / anti-hallucination | [RAGTruth](https://arxiv.org/abs/2401.00396) (~18k responses, span-level hallucination labels), [RAGBench](https://arxiv.org/html/2407.11005v1) | Our `LexicalFaithfulness` is a deterministic, keyless proxy (grounded + correct citation). A real semantic-faithfulness judge (Layer 2) is what these benchmarks score. |
| Citation / attribution | [ALCE](https://arxiv.org/abs/2305.14627) (fluency, correctness, citation quality; supports citing multiple passages per statement) | Maps directly onto our `CitationCorrectness`; ALCE's multi-passage citation is the same shape as our multi-hop cases. |
| Legal reasoning (broad) | [LegalBench](https://github.com/HazyResearch/legalbench/) (162 tasks), LexGLUE (~235k, EN), LEXTREME (24 EU languages, classification + NER) | Out of our current scope (we focus on retrieval + grounded answering), but these define the wider legal-reasoning evaluation space and the PT-BR gap. |
| RAG retrieval | [LegalBench-RAG](https://github.com/zeroentropy-ai/legalbenchrag) (6,858 query-answer pairs, character-level spans; English contracts) | The methodology model for retrieval evaluation; our Recall@K / Hit-rate@K / MRR follow the same query-to-relevant-span idea, in Portuguese over súmulas/jurisprudence. |
| PII / LGPD | [AI4Privacy](https://huggingface.co/datasets/ai4privacy/pii-masking-200k), PII Shield | PII redaction would live in a separate gateway service (roadmap), not in LexRAG. These corpora would test that redactor there; they aren't run here. |

## 4. The Brazilian-Portuguese gap

The honest summary: faithfulness, citation, refusal, and legal-reasoning benchmarks are mature in English/EU and thin in PT-BR. The Brazilian legal IR datasets that exist (JurisTCU, JUÁ, BR-TaxQA-R, LeNER-Br) cover retrieval, tax QA, and NER. There is no widely-adopted PT-BR equivalent of RAGTruth or ALCE for grounded legal answering. LexRAG's deterministic gate (faithfulness/citation/refusal + Recall@K/MRR over a curated súmula set) is, in part, a keyless stand-in for that missing PT-BR benchmark, and the `ICorpusSource` design lets a real PT-BR dataset (JurisTCU first) drive the numbers once a real embedder is configured.

## How they map to the eval code

- `EvalCase.ExpectedSourceFiles` (list) ← a dataset's qrels (relevant docs per query). JurisTCU plugs in here.
- `RetrievalEvaluator` → Recall@K, Hit-rate@K, MRR over those qrels.
- `RagEvaluator` → grounding, citation correctness, refusal, fed by question/reference datasets (BR-TaxQA-R, once its adapter is built).

## Attribution

- **JurisTCU**: *JurisTCU: A Brazilian Portuguese Information Retrieval Dataset with Query Relevance Judgments* (arXiv:2503.08379), CC BY 4.0.
- **BR-TaxQA-R**: *BR-TaxQA-R: A Dataset for Question Answering with References for Brazilian Personal Income Tax Law* (arXiv:2505.15916), CC BY 4.0.
