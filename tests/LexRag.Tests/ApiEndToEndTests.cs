using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LexRag.Tests;

// Drives the Minimal API in-process. Ingests its own document so the assertions don't depend on
// whether the seeded sample files were copied into the test host's output directory.
public class ApiEndToEndTests(KeylessApiFactory factory) : IClassFixture<KeylessApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task IngestSumula() =>
        (await _client.PostAsJsonAsync("/ingest/text", new
        {
            sourceFile = "sv25.txt",
            content = "É ilícita a prisão civil de depositário infiel, qualquer que seja a modalidade do depósito.",
        })).EnsureSuccessStatusCode();

    [Fact]
    public async Task Health_is_ok()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ask_in_domain_is_grounded_and_cited()
    {
        await IngestSumula();
        var response = await _client.PostAsJsonAsync("/ask",
            new { query = "é lícita a prisão civil de depositário infiel?" });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("grounded").GetBoolean().Should().BeTrue();
        body.GetProperty("citations").EnumerateArray()
            .Select(c => c.GetProperty("sourceFile").GetString())
            .Should().Contain("sv25.txt");
    }

    [Fact]
    public async Task Ask_out_of_domain_refuses()
    {
        await IngestSumula();
        var response = await _client.PostAsJsonAsync("/ask", new { query = "Qual a capital da Austrália?" });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("grounded").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Crag_endpoint_returns_an_audit_trace()
    {
        await IngestSumula();
        var response = await _client.PostAsJsonAsync("/ask/crag",
            new { query = "por favor gostaria de saber sobre prisão civil de depositário infiel" });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("trace").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Empty_query_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/ask", new { query = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Health_exposes_chunk_count_and_embedding_dimensions()
    {
        await IngestSumula();

        var response = await _client.GetAsync("/health");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("ok");
        body.GetProperty("chunkCount").GetInt64().Should().BeGreaterThanOrEqualTo(1);
        body.GetProperty("embeddingDimensions").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Ingest_text_empty_content_returns_problem_details()
    {
        var response = await _client.PostAsJsonAsync("/ingest/text", new { sourceFile = "doc.txt", content = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // RFC 7807 ProblemDetails has "errors", not "error".
        body.TryGetProperty("errors", out _).Should().BeTrue("validation response must be ProblemDetails with 'errors'");
    }

    [Fact]
    public async Task Ingest_text_empty_sourceFile_returns_problem_details()
    {
        var response = await _client.PostAsJsonAsync("/ingest/text", new { sourceFile = "", content = "some content" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("errors", out _).Should().BeTrue("validation response must be ProblemDetails with 'errors'");
    }
}
