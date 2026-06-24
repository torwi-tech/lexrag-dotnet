# ADR 0007 — Confidentiality / data boundary

## Context
A legal assistant touches sensitive material. Even though this spike's corpus is **public** (STJ/STF/CNJ decisions), the architecture has to take the data boundary seriously, because the same shape would later carry confidential case data.

## Decision
- **Corpus is public-only** in this spike. The bundled samples are **real official acts in the public domain** (STF Súmulas Vinculantes 11/13/25, STJ Súmulas 314/7; Lei 9.610/98 art. 8 excludes official texts and judicial decisions from copyright). No sensitive or private data is shipped.
- **Provider isolation:** every model call goes through `IChatClient` / `IEmbedder`. Choosing a provider with a Zero-Data-Retention / no-training data boundary (e.g. Azure OpenAI with content-logging disabled) is a one-line composition change.
- **No secrets in the repo:** keys live in user-secrets / environment; `appsettings.json` ships only `<placeholders>`; the data boundary (region, deployment) is config.
- **Single-tenant by scope:** this spike is single-tenant. Tenant isolation via Postgres Row-Level Security is the escalation path before any confidential or multi-tenant data. It is a design direction, not wired in this PoC.

## Consequences
- The spike is safe to publish and demo.
- The path to a confidential deployment is explicit: swap the provider, enable RLS, keep the same ports.

## Alternatives considered
- **Local-only models (Ollama)** for maximum confidentiality: supported by `IChatClient`, but out of scope for the spike; noted as an option.

## Triggers for reassessment
- The corpus moves beyond public official acts to confidential or private case data → introduce tenant isolation (Row-Level Security) and re-audit the data boundary before ingest.
- More than one tenant shares the deployment → activate the roadmapped RLS and enforce tenant isolation in every query path.
- The chosen provider lacks a Zero-Data-Retention / no-training guarantee for the target region → switch provider composition or move to local-only (Ollama).
- A compliance regime (e.g. LGPD audit, contractual data residency) is imposed on the deployment → re-evaluate provider region, logging, and whether local-only models are now required.
