# Golden set datasheet

The evaluation harness runs over two golden sets, selected by `Eval:Dataset`. This page describes what is in each, how the relevance labels were derived, and the limits of each, so a number is never read without the instrument that produced it.

## 1. Curated set (default, keyless)

A small, hand-built set over the sample súmula corpus, used as the keyless default so the eval runs with no key and no download.

- **Size:** a handful of cases, illustrative rather than statistical.
- **In-domain vs out-of-domain:** it deliberately includes out-of-corpus questions (for example "Qual a capital da Austrália?") so that **refusal accuracy is measurable**. A golden set that is entirely in-domain cannot test refusal.
- **Labels:** the expected source file per case is set by hand (`EvalCase.ExpectedSourceFiles`).
- **Single vs multi-hop:** mostly single-source; the model allows multiple expected sources per case, which is what makes Recall@K diverge from Hit-rate@K.
- **Purpose:** a deterministic gate for faithfulness / citation / refusal + Recall@K / Hit-rate@K / MRR. Its absolute numbers are not meaningful at this size; its job is to catch regressions.

## 2. JurisTCU (real, CC BY 4.0)

The real benchmark: [JurisTCU](https://arxiv.org/abs/2503.08379), jurisprudence from the Brazilian Federal Court of Accounts (TCU).

- **Corpus:** 16,045 documents (the `ENUNCIADO` text of each).
- **Queries:** 150, in three groups: real-keyword, synthetic-keyword, synthetic-question.
- **Relevance labels (qrels):** graded, `SCORE` from 0 to 3, mapped onto `EvalCase.ExpectedSourceFiles` per query. Because the labels are graded, **recall depends entirely on the relevance cutoff**: at `SCORE >= 1`, `>= 2`, `>= 3` the relevant set shrinks (12.1, 8.6, 5.3 docs/query) and recall@10 moves from 26% to 53% on the lexical baseline. The cutoff is recorded with every run; see [`Docs/eval-datasets.md`](eval-datasets.md) and [`eval-results/`](../eval-results/).
- **In-domain only:** every JurisTCU query has relevant documents in the corpus, so **refusal accuracy over JurisTCU is 1.0 by construction** and is not reported as measured. Refusal is only meaningful on the curated set, which carries out-of-corpus questions.
- **Single vs multi-hop:** a query can have many relevant documents, so Recall@K (of N required sources, how many appear in top-K) is reported distinctly from Hit-rate@K (at least one) and MRR (rank of the first).

## Limitations of the golden sets

- The curated set is too small for statistical claims; it is a regression gate, not a benchmark.
- The JurisTCU labels are the dataset's own qrels; I did not re-annotate them, so their quality is the dataset's.
- Both are retrieval/citation golden sets; neither labels the *semantic faithfulness* of the generated answer. That is the Layer 2 LLM-judge's job, on a 25-case sample (ADR 0011).
- A PT-BR golden set for grounded legal answering (a RAGTruth/ALCE equivalent) does not exist; that gap is documented in [`Docs/eval-datasets.md`](eval-datasets.md).
