# Evaluation run records

Immutable, per-run artifacts from the LexRAG evaluation. Each file captures one execution of the
eval pipeline against a real provider, with the numbers and the exact parameters that produced them.
The strategy behind these runs is described in [ADR 0011](../Docs/adr/0011-eval-three-way-strategy.md);
the datasets and benchmarks in [eval-datasets.md](../Docs/eval-datasets.md).

## Convention

- **One file per run, never edited after the fact.** A run record is a historical fact. If a later
  run gives different numbers, it gets its own file — the old one is not overwritten. This keeps the
  history auditable and reproducible (the same idea as an MLflow/W&B run directory, kept lightweight
  as flat files for a small repo).
- **Naming:** `YYYY-MM-DD-<dataset>[-<suffix>].json`. The first run of a date is date-only; additional
  runs on the same date carry a suffix (`-rerun`, `-2`, an `HHMM`, ...) so they never collide. Each
  file also carries `run_id`, `run_index` and `run_timestamp_utc` inside.
- **Provenance is explicit.** Every record states whether it came from CI or a local run, and against
  which provider/stack. CI (`ci.yml`) runs only the keyless Layer 1; the semantic and LLM-judge layers
  are local runs against a real key, by design (ADR 0011) — so those numbers live here, not in CI logs.

## Runs

| File | Date | Dataset | Stack | Headline |
|------|------|---------|-------|----------|
| [2026-06-22-juristcu.json](2026-06-22-juristcu.json) | 2026-06-22 | JurisTCU (16k docs, 150 queries) | Azure + pgvector | recall@10 42% · agreement 84% (canonical run; includes the lexical baseline) |
| [2026-06-22-juristcu-rerun.json](2026-06-22-juristcu-rerun.json) | 2026-06-22 | JurisTCU (16k docs, 150 queries) | Azure + pgvector (fresh container) | recall@10 42.3% · agreement 84% (independent confirmatory re-run) |

## Reproducing a run

```bash
# 1. a pgvector instance + the JurisTCU CSVs (see Docs/eval-datasets.md)
# 2. a real Azure OpenAI key in appsettings.secrets.local.json (gitignored)
export ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=lexrag;Username=postgres;Password=postgres"
export Eval__Dataset=juristcu
export Eval__JurisTcuPath="$(pwd)/data/juristcu"
export Rag__TopK=10
export Eval__JudgeRuns=5
dotnet run --project src/LexRag.Api      # boot seeds + embeds the corpus into pgvector

curl -s -X POST localhost:5007/eval/retrieval   # Recall@K / Hit-rate@K / MRR
curl -s -X POST localhost:5007/eval/judge        # Layer 2: groundedness, relevance, agreement vs Layer 1
```

Numbers will not be bit-identical across runs — embeddings are deterministic, but ANN index ordering and
LLM-judge sampling introduce small variance. The durable findings (recall lift, one-directional
Layer 1 / Layer 2 divergence) are what the records are kept to show; absolute judge scores are indicative.
