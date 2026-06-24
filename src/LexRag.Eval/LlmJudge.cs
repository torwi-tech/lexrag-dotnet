using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;

namespace LexRag.Eval;

// Layer 2 of the three-way eval (ADR 0011): an LLM-as-judge scoring semantic groundedness and relevance,
// the dimensions the deterministic lexical gate (Layer 1) cannot see. Key-gated: only meaningful with a real
// chat model, so callers run it behind the same kind of opt-in guard as the Docker-gated integration tests.
public sealed class LlmJudge(IChatClient chatClient)
{
    private readonly ChatConfiguration _config = new(chatClient);
    private readonly GroundednessEvaluator _groundedness = new();
    private readonly RelevanceEvaluator _relevance = new();

    public async Task<JudgeVerdict> JudgeAsync(string query, string answer, string groundingContext, CancellationToken ct = default)
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, query) };
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, answer));

        var grounded = await _groundedness.EvaluateAsync(
            messages, response, _config,
            additionalContext: [new GroundednessEvaluatorContext(groundingContext)],
            cancellationToken: ct);
        var relevant = await _relevance.EvaluateAsync(messages, response, _config, cancellationToken: ct);

        var g = grounded.Get<NumericMetric>(GroundednessEvaluator.GroundednessMetricName);
        var r = relevant.Get<NumericMetric>(RelevanceEvaluator.RelevanceMetricName);

        return new JudgeVerdict(g.Value ?? 0d, g.Reason, r.Value ?? 0d, r.Reason);
    }
}

public sealed record JudgeVerdict(double Groundedness, string? GroundednessReason, double Relevance, string? RelevanceReason);
