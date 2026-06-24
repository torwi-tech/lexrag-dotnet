# ADR 0010 — LGPD: data boundary for LLM calls and telemetry

## Context
This spike's corpus is public (STF/STJ súmulas). In production for a real legal product the same pipeline would carry **sensitive case data**: parties, CPFs, case content. Two moments send data outside the process:
1. **The LLM/embedding call:** text leaves to a provider (Azure OpenAI / OpenAI), often hosted abroad.
2. **Telemetry:** if query/answer are attached to OTel spans (useful to debug eval), that content lands in an observability backend, possibly a foreign SaaS.

Both are **international transfers of personal data** under **LGPD Art. 33** (and touch Art. 7/11 legal basis and Art. 46 security). Ignoring this carries real compliance risk.

## Decision
Treat the data boundary as a first-class, configurable concern:
- **Provider with a data boundary:** default to **Azure OpenAI** with content-logging disabled and a region/data-residency that satisfies the transfer rule; the provider is behind `IChatClient`/`IEmbedder`, so swapping to an in-region or on-prem model (Ollama/self-hosted) is a composition change, not a rewrite.
- **PII never in telemetry by default:** spans carry IDs, scores, latency, and token counts, **not** raw `query`/`answer`. Debug content is opt-in and, when needed in production, correlated by a `sha256(query)` hash with a separate, in-region, access-controlled store (never the raw text in a foreign SaaS).
- **Redaction before the wire (roadmap):** in production, a PII redaction layer would mask sensitive values before any external call, using reversible tokenization with a vault when the answer must reference the real value. This is not implemented in this PoC.
- **Corpus is public-only in the spike:** no real personal data is shipped.

## Consequences
- Defensible posture for a regulated/legal deployment; the transfer question has an explicit answer instead of silence.
- Small cost: an extra config surface (region, content-logging flag, telemetry-content toggle) and a hashing step for debug correlation.
- The spike does **not** implement PII redaction, the vault, or the hash-correlated query store; they are documented escalation paths, not built (honest PoC scope).

## Alternatives considered
- **Send query/answer to telemetry for easier debugging:** convenient, but in production with real users it is an LGPD Art. 33 violation; rejected as a default (allowed only for this public-corpus spike, behind a flag).
- **Only local models (Ollama) for maximum sovereignty:** strongest data boundary, supported by `IChatClient`, but out of scope for the spike; noted as the on-prem option.

## Triggers for reassessment
- Real user/case data enters the pipeline → enforce redaction + remove content from telemetry + sign a DPA with the provider, or move to an in-region/on-prem model.
- Legal/compliance requires data never leaving national infrastructure → switch to a self-hosted model + self-hosted vector store + self-hosted telemetry.
- A provider offers a Brazilian region with a ZDR/DPA → revisit the default provider/region.
