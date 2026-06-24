# Arquitetura — lexrag-dotnet

Pipeline em camadas, cada uma com responsabilidade única e testável de forma isolada. O domínio é puro (arquitetura hexagonal): `LexRag.Core` não depende de nenhum adapter, restrição validada por um teste de arquitetura.

## Camadas

### 1. Ingestion (`LexRag.Ingestion`)
Documento (PDF via PdfPig, ou texto) passa pelo `StructuralChunker` (chunking por sentença com overlap), depois pelo `IEmbedder` e é gravado via upsert no `IVectorIndex`. A ingestão é idempotente: `ChunkKey` é um GUID determinístico derivado de `(arquivo#i)`, então re-ingerir o mesmo documento não duplica chunks.

### 2. Index (`LexRag.Index`)
Port `IVectorIndex` com duas implementações: **InMemory** (similaridade de cosseno em C#, para dev/test) e **pgvector** (Npgsql raw: HNSW, operador de cosseno `<=>` e `ef_search` ajustável por query). A escolha de SQL cru em vez do connector está registrada na ADR-0001.

### 3. Retrieval (`LexRag.Retrieval`)
Retrieval híbrido: perna densa (busca vetorial) e perna esparsa (BM25 / full-text) combinadas por **Reciprocal Rank Fusion (RRF)**, uma função pura, com a mesma fusão usada in-memory e em uma única consulta SQL no pgvector. Os pesos por perna da RRF são configuráveis (`RagOptions.DenseWeight` / `RagOptions.SparseWeight`), com default neutro. O resultado fundido passa por `IReranker`, que devolve o top-N. Há dois rerankers atrás dessa porta: o default `LexicalCoverageReranker` (ordena por cobertura de termos da query, com o score RRF como desempate) e o `MmrReranker` opt-in (Maximal Marginal Relevance, troca relevância por diversidade para reduzir redundância no top-N), selecionado por `Rag:Reranker=mmr`.

### 4. Orchestration (`LexRag.Orchestration`)
- `RagPipeline` (default, explícito): retrieve → rerank → generate. Determinístico e auditável.
- `CragPipeline` (opt-in): retrieve → grade → reformula e re-busca (limitado) → generate.
- `SemanticKernelRagService` (agêntico): expõe `DocumentSearchPlugin` como um SK `KernelFunction`, converte-o em `AIFunction` via `AsAIFunction(Kernel)`, e executa o loop de tool-calling através do middleware de function-invocation de `Microsoft.Extensions.AI` (`UseFunctionInvocation()`). O loop funciona com o fake keyless (`ToolCallingExtractiveChatClient`) e com o cliente real, porque a decisão de invocar a ferramenta é tomada na camada M.E.AI, não pelo Semantic Kernel. O plugin SK é o ponto de entrada para o `IHybridRetriever`; a decisão de quando chamá-lo fica com o chat client.
- `GroundedAnswerGenerator`: monta o prompt grounded, exige citação e recusa quando não há contexto suficiente. O fake keyless correspondente é o `ToolCallingExtractiveChatClient`.

### 5. Eval (`LexRag.Eval`)
- Geração: lexical faithfulness (proxy léxico; a faithfulness semântica é a Layer 2, ADR-0011), correção de citação e taxa de recusa (`RagEvaluator`).
- Retrieval: Recall@K, Hit-rate@K e MRR sobre um golden set (`RetrievalEvaluator`), medindo a busca separada da geração. Recall@K e Hit-rate@K divergem quando uma pergunta exige múltiplas fontes (multi-hop).
- Perturbação: `PerturbationEvaluator` aplica variantes determinísticas de superfície (casing, espaço, ordem de termos, stopword) a cada caso do golden set e mede o delta de hit-rate por tipo de perturbação. Keyless, sem Docker.
- Ablação: `AblationRunner` compara dense-only, sparse-only, hybrid (sem reranker) e hybrid+reranker sobre o mesmo golden set, produzindo uma tabela de métricas por configuração. Ela mede a contribuição de cada perna em vez de assumi-la. A corrida keyless committada ([`eval-results/2026-06-24-juristcu-ablation-keyless.json`](../eval-results/2026-06-24-juristcu-ablation-keyless.json)) mostra o que a medição revela: com o embedder de hash (não-semântico), a perna densa é o elo fraco (recall 10%), então sparse-only (recall 25%) supera o híbrido (18 a 23%) neste setup. O ganho do híbrido + RRF aparece com um embedder semântico real, consistente com o lift de recall medido (26% → 42%, ver `eval-datasets.md`).

### 6. API (`LexRag.Api`)
Minimal API e composition root (escolhe fake ou real e InMemory ou pgvector por configuração), com OpenTelemetry. Endpoints: `/ask`, `/ask/crag`, `/ask/agentic`, `/ingest/*`, `/eval`, `/eval/retrieval`, `/eval/perturbation`, `/eval/ablation`, `/eval/judge`, `/health`.

## Restrições arquiteturais
1. **Core sem dependência de adapter** (hexagonal), provado por teste de arquitetura.
2. **Todo modelo e store atrás de uma porta**: provider e store trocáveis por configuração.
3. **Anti-alucinação estrutural**: sem contexto, o sistema recusa antes de chamar o modelo.
4. **Orquestração explícita é o default** (auditável para o contexto jurídico); o caminho agêntico é opt-in.
5. **Sem segredo no repositório**; data boundary LGPD-aware (ADR-0010).

## Performance & custo

> Os tempos e custos abaixo são valores de referência (ordens de grandeza da documentação dos provedores e da literatura), não p50/p95 medidos neste PoC. O endpoint `/ask` instrumenta a latência por etapa (retrieval, rerank, geração); rodar com o provider real popula os números medidos. O ganho de qualidade foi medido (ver `eval-datasets.md`). Os valores de baseline para o caminho keyless in-memory podem ser medidos via `dotnet run -c Release --project bench/LexRag.Benchmarks`.

Duas fases têm perfis de custo opostos. Confundi-las leva a achar que "cada busca custa o tempo de indexar", o que não procede.

| | Ingestão (indexar) | Busca (query) |
|---|---|---|
| Embeda | todos os documentos | uma query |
| Frequência | 1× por documento (ou quando ele muda) | por request |
| Caminho | job offline / background | online, no request |
| Domina o tempo | volume do corpus × latência do embedder | geração do LLM |

**Busca.** Embeddar a query (uma chamada, dezenas a ~150 ms) → ANN no índice (pgvector HNSW: poucos ms mesmo com milhões de vetores) → rerank opcional (~100 ms) → geração grounded (centenas de ms a alguns segundos). A geração domina; o retrieval é barato. O custo da query **não** escala com o tamanho do corpus; escala com `TopK` e com o tamanho do prompt.

**Ingestão.** Custo único, amortizado sobre todas as buscas seguintes. Com o adapter pgvector os vetores são persistidos: o `CorpusSeeder` pula o seed quando o índice já tem dados (`Count > 0`), e a re-indexação atinge só documentos novos ou alterados (incremental), nunca o corpus inteiro. Embeddar 16k enunciados uma vez custa centavos no `text-embedding-3-small`.

**Por que o InMemory parece lento:** ele não persiste, então re-embeda o corpus a cada startup. É artefato de PoC, não característica de produção. O seed paraleliza a ingestão (concorrência limitada); uma ingestão de produção iria além: batch por chamada (Azure aceita até 2048 inputs), fila em background e backpressure por TPM.

**Cache, em camadas:**
1. O índice persistido já é o cache dos embeddings dos documentos: calculado 1×, reusado em toda busca.
2. Cache semântico de query (responder uma pergunta nova com a resposta de uma anterior suficientemente parecida, cosseno alto) corta embedding + retrieval + geração para perguntas recorrentes; encaixa como middleware antes do pipeline.
3. Cache de resposta para query idêntica, invalidado quando o corpus muda.

## Known Limitations & Escalation Paths
Trade-offs aceitos no escopo de prova de conceito. Cada um tem um caminho de evolução que não exige mudança de interface.

| # | Limitação | Onde | Escalation |
|---|-----------|------|-----------|
| 1 | Embedder e chat são fakes (lexicais, sem semântica real) | `Embeddings`, `Orchestration` | Registrar `IEmbedder` / `IChatClient` reais (Azure OpenAI): apenas configuração. |
| 2 | Reranking é heurístico: default `LexicalCoverageReranker` (cobertura de termos) e `MmrReranker` opt-in (diversidade, `Rag:Reranker=mmr`); pesos da RRF por perna configuráveis (`DenseWeight`/`SparseWeight`) | `Retrieval/Rerankers` | Cross-encoder real (Cohere/Jina) atrás de `IReranker`. |
| 3 | Grader e reformulator do CRAG são heurísticos | `Core/Retrieval/CragComponents` | Grader baseado em LLM (julga suficiência semântica) atrás de `IRetrievalGrader`. |
| 4 | InMemory store é O(n) | `Index/InMemoryVectorIndex` | pgvector (HNSW): adapter já existe; subir via docker-compose. |
| 5 | `MinRelevanceScore=0.0` deixa o gate por score inerte | `Core/Models/RagOptions` | Calibrar o limiar com dado real; recusar por score, além do caso de contexto vazio. |
| 6 | Single-tenant; multi-tenant / RLS fora de escopo | `Index` | Postgres Row-Level Security + autenticação, ao virar multi-tenant. |
| 7 | Eval apenas offline | `Eval` | Amostragem online (um percentual do tráfego) e alerta abaixo do threshold. |
| 8 | Layer 2 (LLM-judge, `Microsoft.Extensions.AI.Evaluation`) é opt-in/key-gated e não roda em CI; Layer 3 (Foundry) ainda não conectada | `Eval` | Rodar a Layer 2 no nightly/pre-release; conectar o exporter do Foundry atrás de uma porta (ADR 0011). |
| 9 | Perturbação cobre variantes de superfície (casing, espaço, ordem de termos, stopword); paráfrase semântica e distratores adversariais ainda fora do escopo | `Eval` | Próximo passo: variantes geradas por LLM (paráfrase semântica) e distratores de domínio adjacente para completar o quadrante adversarial. |

## ADRs
Índice em [`adr/`](adr/) (12 registros): vector store, distância de cosseno, índice HNSW, retrieval híbrido + RRF, chunking estrutural, grounding e anti-alucinação, confidencialidade, fakes keyless, CRAG, data boundary LGPD, estratégia de avaliação e mapa de conformidade regulatória (Res. CNJ 615/LGPD).
