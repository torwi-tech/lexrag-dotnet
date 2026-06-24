# Observability

LexRAG instruments its pipeline with a custom `LexRag.Rag` ActivitySource and a matching Meter, both registered in `Program.cs` so they flow through OpenTelemetry. ASP.NET Core request spans and outbound HTTP calls are also instrumented.

## Spans and activities

All spans are emitted by the `LexRag.Rag` ActivitySource (`RagPipeline.ActivitySource`).

| Span name   | Emitted by      | Tags set |
|-------------|-----------------|----------|
| `rag.ask`   | `RagPipeline`   | `rag.query_length`, `rag.retrieved`, `rag.retrieve_ms`, `rag.reranked_topk`, `rag.rerank_ms`, `rag.grounded`, `rag.generate_ms` |
| `rag.crag`  | `CragPipeline`  | `crag.rounds`, `crag.sufficient` |

The `rag.ask` span is a child of the ASP.NET Core request span for `POST /ask` (added by `AddAspNetCoreInstrumentation`). The `/health` endpoint is filtered out of tracing to keep dashboards clean.

## Metrics

All metrics share the `LexRag.Rag` Meter. They appear in the `LexRag.Rag` meter group in OTLP-compatible dashboards.

| Metric name                  | Unit | What it measures |
|------------------------------|------|-----------------|
| `rag.stage.retrieve_ms`      | ms   | Hybrid retrieval wall-clock latency (per request) |
| `rag.stage.rerank_ms`        | ms   | Reranker wall-clock latency (per request) |
| `rag.stage.generate_ms`      | ms   | Grounded generation wall-clock latency (per request) |

ASP.NET Core instrumentation adds standard HTTP server metrics (`http.server.request.duration`, etc.).

## Viewing spans locally (console exporter)

When no `OTEL_EXPORTER_OTLP_ENDPOINT` is set, the app falls back to the OTel console exporter and prints every span to stdout. To see a trace:

```bash
# Start keyless (no key, no Docker needed)
dotnet run --project src/LexRag.Api

# In another terminal, hit /ask
curl -s localhost:5007/ask -H "Content-Type: application/json" \
  -d '{"query":"qual o prazo da prescricao intercorrente na execucao fiscal?"}'
```

The API process stdout will print the completed `rag.ask` span (and its ASP.NET Core parent) in OTel console format immediately after the response is sent. Look for lines beginning with `Activity.DisplayName:`.

## Example span output

See `Docs/sample-trace.txt` for a real captured span.

To capture a trace in an environment where Windows Smart App Control is not active (e.g. a Linux container):

```bash
docker run --rm -e ASPNETCORE_ENVIRONMENT=Development \
  -p 5007:5007 \
  -v "$(pwd):/app" -w /app \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet run --project src/LexRag.Api &

# wait for startup, then:
curl -s localhost:5007/ask -H "Content-Type: application/json" \
  -d '{"query":"qual o prazo da prescricao intercorrente na execucao fiscal?"}' \
  > /dev/null

# The container stdout shows the span.
```

## Sending to an OTLP backend (Aspire, Jaeger, Grafana)

Set `OTEL_EXPORTER_OTLP_ENDPOINT` to the OTLP gRPC endpoint (port 4317 by default). The console exporter is disabled when this variable is set.

```bash
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
dotnet run --project src/LexRag.Api
```

The Aspire dashboard (`dotnet run --project Aspire.AppHost`) provides a built-in OTLP endpoint and a trace viewer with no extra setup.
