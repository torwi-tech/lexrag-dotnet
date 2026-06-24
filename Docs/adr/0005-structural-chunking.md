# ADR 0005 — Sentence-packing chunking with overlap

## Context
Chunking drives retrieval quality more than the embedding model does. A blind fixed-character window cuts mid-sentence and destroys the unit of meaning a legal passage carries (a FATO/NORMA/PEDIDO clause).

## Decision
`StructuralChunker` packs **whole sentences** into chunks up to `ChunkSize`, then starts the next chunk by carrying over the trailing sentences within `ChunkOverlap`. Oversized "sentences" (unpunctuated dumps) are hard-windowed on word boundaries so no unit exceeds the cap. Sub-minimal trailing chunks are folded back into their predecessor. Size and overlap are **configuration** rather than hard-coded constants.

## Consequences
- Chunks respect sentence boundaries; a sentence straddling a boundary **survives in the neighbour** via overlap (unit-tested).
- `ChunkSize`/`ChunkOverlap` are tunable because chunk size is an **eval problem**: what's optimal depends on the corpus and is found by measuring retrieval rather than guessing.
- Not layout-aware: it does not parse headings/columns. A structural PDF layout pass (headings, tables) is future work.

## Alternatives considered
- **Fixed character window** (the skill's baseline): simplest, but cuts mid-sentence.
- **Token-based windowing**: needs a tokenizer dependency. Character/sentence packing is close enough for this spike and dependency-free.
- **Semantic chunking** (embedding-similarity splits): higher quality, much more compute; revisit if eval shows boundary problems.

## Triggers for reassessment
- Retrieval eval shows recall loss at chunk boundaries (FATO/NORMA/PEDIDO clauses split) → move to layout-aware chunking that parses headings/sections.
- The corpus shifts to PDFs with heavy structure (tables, multi-column, headnotes) that sentence-packing mangles → add a structural PDF layout pass.
- Sweeping `ChunkSize`/`ChunkOverlap` on the eval set stops moving the metric → the bottleneck is the strategy, not the params; try semantic chunking.
- A real embedding model adds a tokenizer dependency anyway → revisit token-based windowing now that the dependency cost is already paid.
