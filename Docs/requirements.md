# Requirements — lexrag-dotnet

Prova de conceito de RAG jurídico em .NET 8. O foco é demonstrar a arquitetura de produto (RAG com citação, ação, avaliação e observabilidade), e não escala de produção.

## Functional Requirements

| FR | Descrição |
|----|-----------|
| FR-01 | Ingerir documentos (PDF via PdfPig, ou texto): chunking → embeddings → índice vetorial. |
| FR-02 | Chunking estrutural por sentença com overlap, configurável (tamanho e overlap). |
| FR-03 | Retrieval híbrido: vetorial (cosseno) + full-text / BM25, fundidos por RRF. |
| FR-04 | Reranking do top-N atrás de `IReranker` (heurístico no momento; cross-encoder plugável). |
| FR-05 | Geração com grounding e citação rastreável no formato `[Fonte: arquivo, trecho N]`. |
| FR-06 | Anti-alucinação: pergunta fora do acervo retorna "Não encontrei nos documentos fornecidos". |
| FR-07 | CRAG: avaliar o retrieval e, quando fraco, reformular e re-buscar (limitado) antes de gerar. |
| FR-08 | VectorStore abstraído (`IVectorIndex`): InMemory (dev/test) e pgvector (raw SQL, HNSW/cosine); plugável para Qdrant ou Azure AI Search. |
| FR-09 | Eval: lexical faithfulness (proxy; semântica = Layer 2, ADR-0011), correção de citação e taxa de recusa, além de métricas de retrieval (Recall@K, Hit-rate@K, MRR) sobre golden set. |
| FR-10 | Observabilidade com OpenTelemetry (traces, metrics e logs; correlação entre trace e log). |
| FR-11 | Rodar sem chave e sem Docker via fakes determinísticos (hash embedder, extractive chat). |

## Non-Functional Requirements

| NFR | Descrição |
|-----|-----------|
| NFR-01 | .NET 8 (`net8.0`). |
| NFR-02 | Arquitetura hexagonal: `LexRag.Core` sem dependência de camada externa, validado por teste de arquitetura (NetArchTest). |
| NFR-03 | Cada camada testável de forma isolada; `dotnet build` verde; testes em seis estilos (unit, theory, property-based, architecture, integration, e2e). |
| NFR-04 | Sem segredo no repositório: chaves em user-secrets; `appsettings.json` contém apenas `<placeholders>`. |
| NFR-05 | Degradação graciosa: sem chave, usa fakes; sem Docker, usa InMemory; o teste de integração pgvector é pulado automaticamente. |
| NFR-06 | Data boundary LGPD-aware (ADR-0010): PII fora da telemetria por padrão; provider com fronteira de dados configurável. |

## Fora de Escopo

Autenticação e multi-tenant completos; UI / frontend; deploy em nuvem ou Kubernetes; fine-tuning; streaming; formatos além de PDF e txt; escala de produção; cross-encoder e grader-LLM reais (as interfaces estão prontas, a implementação fica como evolução).

## Decisões de domínio

| # | Pergunta | Decisão | ADR |
|---|----------|---------|-----|
| 1 | Vector store? | pgvector (port próprio; raw SQL para o caminho híbrido) | 0001 |
| 2 | Distância? | Cosseno | 0002 |
| 3 | Índice ANN? | HNSW (m=16, ef_construction=128) | 0003 |
| 4 | Retrieval? | Híbrido + RRF | 0004 |
| 5 | Anti-alucinação? | Grounding + citação + recusa + eval | 0006 |
| 6 | Sem chave / Docker? | Fakes determinísticos | 0008 |
| 7 | RAG agêntico? | CRAG (opt-in) | 0009 |
| 8 | Dado pessoal e transferência? | Data boundary LGPD-aware | 0010 |
