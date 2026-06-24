# ADR 0002 — Cosine distance for legal text

## Context
pgvector offers L2 (`<->`), inner product (`<#>`) and cosine (`<=>`). Legal documents vary wildly in length, from an ementa of three lines to an acórdão of forty pages.

## Decision
Use **cosine** (`vector_cosine_ops` / `<=>`). The fake `HashEmbedder` L2-normalises its output, and real embedding models (text-embedding-3-small) return unit-norm vectors, so cosine is the consistent choice across both paths.

## Consequences
- Ranking depends on **direction (topic) rather than magnitude (length)**, so a short headnote and a long opinion on the same point score comparably.
- The HNSW index is built with `vector_cosine_ops` to match the query operator (a mismatch silently disables the index).

## Alternatives considered
- **Inner product**: equivalent to cosine for unit vectors but unsafe if any vector is not normalised; cosine is the safe default.
- **L2**: biased by document length; wrong for a mixed-length legal corpus.

## Triggers for reassessment
- The embedding model is swapped for one that does not return unit-norm vectors → re-evaluate cosine vs explicit normalisation, or switch to inner product only after confirming normalisation.
- A profiling pass shows inner product (`<#>`) measurably faster at the same recall on confirmed unit-norm vectors → consider `<#>` for the hot path.
- The HNSW index operator class drifts from `vector_cosine_ops` (e.g. a migration changes it) → fix immediately, as a mismatch silently disables the index.
- Length/magnitude becomes a meaningful ranking signal for a future corpus → reconsider an L2 or hybrid distance.
