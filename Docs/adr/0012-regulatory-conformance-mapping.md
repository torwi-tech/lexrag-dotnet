# ADR 0012 — Regulatory conformance mapping (CNJ Resolution 615/2025 + LGPD)

> **Context note.** LexRAG targets the **Brazilian legal sector** (Judiciary and legal practice). The regulatory anchors in this ADR (CNJ Resolution 615/2025 and the LGPD) are Brazil-specific; this is why conformance, not only retrieval quality, is a first-class concern.
>
> **Nota de contexto.** O LexRAG é voltado para o **setor jurídico brasileiro** (Judiciário e prática jurídica). As âncoras normativas deste ADR (Resolução CNJ 615/2025 e a LGPD) são específicas do Brasil; por isso a conformidade, e não só a qualidade de retrieval, é preocupação de primeira ordem.
>
> This ADR is bilingual: **English** first, **Português (PT-BR)** below.

---

## English

### Context

A legal RAG system is not judged only on retrieval quality. In the Brazilian Judiciary it is judged on **regulatory conformance**. CNJ Resolution No. 615 of 11 March 2025 (in force since 14 July 2025, the 120-day deadline set in art. 47), which replaced Resolution 332/2020 (art. 46), is the central framework for AI in the Judiciary; it has since been amended by Resolution 674/2026 (which changed its art. 15). It requires effective human oversight, explainability, traceability, risk classification with registration in **Sinapses**, and LGPD compliance regarding personal data and judicial secrecy (*segredo de justiça*).

Critical scope point: Resolution 615 binds the **court that adopts** the solution, not directly the vendor. But the norm affects vendors in practice. Whoever delivers AI to the Judiciary needs the product to **let the court comply** (explainability, traceability, human oversight). Case law applying 615 already exists: the TJDFT, on its own motion, vacated a judgment that indicated it had been drafted with AI assistance, for violating the natural-judge principle (*juiz natural*) and the non-delegability of jurisdiction. The cost of non-conformance is **nullity, not a fine**.

This ADR does not declare LexRAG "615-compliant". It declares which technical guarantees address which regulatory requirements, and which gaps remain.

### Decision

Maintain an explicit, versioned **conformance matrix** linking each LexRAG technical guarantee to the regulatory provision it helps the adopting court satisfy:

| Technical guarantee | Helps the court satisfy |
|---|---|
| Structural grounding + refusal ([ADR 0006](0006-grounding-anti-hallucination.md)) | Human oversight and the prohibition on solely-automated decisions: the system refuses rather than fabricates, and never decides; it only assists, under human review. |
| Traceable citation `[Fonte: doc, trecho N]` ([ADR 0006](0006-grounding-anti-hallucination.md)) | Explainability and auditability required of solutions by risk level (Sinapses / algorithmic impact assessment): every statement traces back to its source. |
| Data boundary on LLM calls and telemetry ([ADR 0007](0007-confidentiality-data-boundary.md), [ADR 0010](0010-lgpd-data-boundary.md)) | Art. 7 of Resolution 615 (representative data; cautions on judicial secrecy and personal-data protection under the LGPD) and avoidance of cross-border content transfer without a legal basis. |
| Evaluation harness with a deterministic gate ([ADR 0011](0011-eval-three-way-strategy.md)) | The continuous monitoring and periodic review that 615 requires as ongoing governance, not a one-off assessment. |

### Consequences

- The repository speaks the legal buyer's language: each technical decision has a traceable normative anchor, not just an engineering justification.
- The matrix exposes gaps honestly: the proof-of-concept does **not** register in Sinapses nor perform an algorithmic impact assessment (these are the court's institutional processes, not the software's); the system provides the inputs (traces, explainability) that those processes consume.
- Conformance becomes a verifiable, versioned property, subject to drift when the norm changes, hence the triggers below.

### Alternatives considered

- **Not mapping (leave it implicit).** Rejected: "it is safe / explainable" without a normative anchor is an empty claim before a legal evaluator.
- **Claiming full conformance.** Rejected as false and risky: Resolution 615 binds the court; the vendor delivers conformance *capability*, not conformance itself. Overclaiming here destroys credibility.
- **Implementing Sinapses registration / algorithmic impact assessment in the PoC.** Rejected by scope: these are the adopting body's processes; the software supplies the auditable artifacts, it does not perform the registration.

### Triggers for reassessment

- **Regulatory update.** Resolution 615 was amended by Resolution 674/2026 (art. 15); this matrix anchors on art. 7 and the human-oversight requirements, and should be reverified against the amended text and any CNIAJ acts before being relied on.
- **Repositioning.** If LexRAG is framed as a law-practice / notary tool rather than internal court use, the incidence of 615 changes; re-evaluate the declared scope.
- **New case law** on AI use in the Judiciary that changes what counts as sufficient human oversight → re-evaluate grounding / refusal / human-review.
- **Risk reclassification** (low → high risk) → algorithmic impact assessment and a public summary in Sinapses become required; document that path.

---

## Português (PT-BR)

### Contexto

Um sistema de RAG jurídico não é avaliado só por qualidade de retrieval. No Judiciário brasileiro ele é avaliado por **conformidade regulatória**. A Resolução CNJ nº 615, de 11 de março de 2025 (em vigor desde 14/07/2025, prazo de 120 dias do art. 47), que revogou a Res. 332/2020 (art. 46), é o marco central de IA no Judiciário; desde então foi alterada pela Resolução 674/2026 (que mudou seu art. 15). Ela exige supervisão humana efetiva, explicabilidade, rastreabilidade, classificação de risco com cadastro no **Sinapses**, e observância da LGPD quanto a dados pessoais e segredo de justiça.

Ponto crítico de escopo: a 615 obriga o **tribunal que adota** a solução, não diretamente o fornecedor. Mas a norma impacta fornecedores na prática: quem entrega IA ao Judiciário precisa que o produto **permita ao tribunal cumprir** a 615 (explicabilidade, rastreabilidade, supervisão humana). Já há jurisprudência aplicando a 615: o TJDFT cassou de ofício sentença que indicava ter sido elaborada com auxílio de IA, por violação ao juiz natural e à indelegabilidade jurisdicional. O custo de não-conformidade é **nulidade, não multa**.

Este ADR não declara o LexRAG "conforme à 615". Declara quais garantias técnicas do sistema atendem quais exigências, e quais lacunas permanecem.

### Decisão

Manter uma **matriz de conformidade** explícita e versionada, ligando cada garantia técnica do LexRAG ao dispositivo regulatório que ela ajuda o tribunal adotante a cumprir:

| Garantia técnica | Ajuda o tribunal a cumprir |
|---|---|
| Grounding estrutural + refusal ([ADR 0006](0006-grounding-anti-hallucination.md)) | Supervisão humana e a vedação à decisão exclusivamente automatizada: o sistema recusa em vez de inventar, e nunca decide; apenas apoia, sob revisão humana. |
| Citação rastreável `[Fonte: doc, trecho N]` ([ADR 0006](0006-grounding-anti-hallucination.md)) | Explicabilidade e auditabilidade exigidas por nível de risco (Sinapses / avaliação de impacto algorítmico): toda afirmação é rastreável à fonte. |
| Data boundary nas chamadas a LLM e na telemetria ([ADR 0007](0007-confidentiality-data-boundary.md), [ADR 0010](0010-lgpd-data-boundary.md)) | Art. 7º da 615 (dados representativos; cautelas quanto a segredo de justiça e proteção de dados pessoais nos termos da LGPD) e evita transferência internacional de conteúdo sem base legal. |
| Eval harness com gate determinístico ([ADR 0011](0011-eval-three-way-strategy.md)) | O monitoramento e a revisão periódica que a 615 exige como governança contínua, não como avaliação única. |

### Consequências

- O repo fala a língua do comprador jurídico: cada decisão técnica tem uma âncora normativa rastreável, não só justificativa de engenharia.
- A matriz expõe lacunas honestamente: o PoC **não** faz cadastro no Sinapses nem avaliação de impacto algorítmico (são processos institucionais do tribunal, não do software); o sistema entrega os insumos (rastros, explicabilidade) que esses processos consomem.
- Conformidade vira propriedade verificável e versionada, sujeita a drift quando a norma muda; daí os triggers.

### Alternativas consideradas

- **Não mapear (deixar implícito).** Rejeitado: "é seguro/explicável" sem âncora normativa é alegação vazia diante de um avaliador jurídico.
- **Afirmar conformidade plena.** Rejeitado por ser falso e arriscado: a 615 obriga o tribunal; o fornecedor entrega *capacidade* de conformidade, não conformidade em si. Overclaim aqui destrói credibilidade.
- **Implementar Sinapses / AIA no PoC.** Rejeitado por escopo: são processos do órgão adotante; o software fornece os artefatos auditáveis, não executa o cadastro.

### Triggers para reavaliação

- **Atualização normativa.** A 615 foi alterada pela Res. 674/2026 (art. 15); esta matriz se ancora no art. 7º e nas exigências de supervisão humana, e deve ser revalidada contra o texto alterado e atos do CNIAJ antes de ser usada.
- **Reposicionamento.** O LexRAG ser enquadrado como ferramenta de advocacia/cartório em vez de uso interno do tribunal → a incidência da 615 muda; reavaliar o escopo declarado.
- **Nova jurisprudência** sobre uso de IA no Judiciário que altere o que conta como supervisão humana suficiente → reavaliar grounding / refusal / revisão humana.
- **Reclassificação de risco** (baixo → alto risco) → passam a incidir avaliação de impacto algorítmico e sumário público no Sinapses; documentar o caminho.
