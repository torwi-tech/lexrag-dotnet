using Azure;
using Azure.AI.OpenAI;
using FluentAssertions;
using LexRag.Eval;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LexRag.Tests;

// The LLM-judge is key-gated: it skips without a real Azure OpenAI model, the same way the pgvector
// integration test skips without Docker. It resolves the key from the SAME places the app does: the
// gitignored appsettings.secrets.local.json (the README's documented setup) or the AzureOpenAI__*
// environment variables, so following the README is enough to make it run instead of skip. The assertion
// is behavioral rather than a fixed score; a grounded answer must outscore one that contradicts the context.
public sealed class LlmJudgeTests
{
    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.secrets.local.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    private static readonly string? Endpoint = Real(Config["AzureOpenAI:Endpoint"]);
    private static readonly string? Key = Real(Config["AzureOpenAI:Key"]);
    private static readonly string ChatDeployment = Config["AzureOpenAI:ChatDeployment"] ?? "gpt-4.1-mini";

    private static bool Configured => Endpoint is not null && Key is not null;

    // Treat an unfilled <placeholder> or blank as "not configured".
    private static string? Real(string? v) =>
        !string.IsNullOrWhiteSpace(v) && !v.TrimStart().StartsWith('<') ? v : null;

    [SkippableFact]
    public async Task Grounded_answer_outscores_an_answer_that_ignores_the_context()
    {
        Skip.IfNot(Configured, "Azure OpenAI not configured; the LLM-judge is key-gated by design (ADR 0011).");

        IChatClient client = new AzureOpenAIClient(new Uri(Endpoint!), new AzureKeyCredential(Key!))
            .GetChatClient(ChatDeployment).AsIChatClient();
        var judge = new LlmJudge(client);

        const string context =
            "A prescrição intercorrente na execução fiscal corre após um ano de suspensão do processo (art. 40 da LEF).";
        const string query = "Quando começa a contar a prescrição intercorrente na execução fiscal?";

        var grounded = await judge.JudgeAsync(
            query, "Após um ano de suspensão do processo, nos termos do art. 40 da LEF.", context);
        var offTopic = await judge.JudgeAsync(
            query, "A capital da Austrália é Camberra.", context);

        grounded.Groundedness.Should().BeGreaterThan(offTopic.Groundedness);
        grounded.Groundedness.Should().BeGreaterThanOrEqualTo(4.0);
    }
}
