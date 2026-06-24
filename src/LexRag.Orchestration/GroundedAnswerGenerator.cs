using LexRag.Core.Abstractions;
using LexRag.Core.Generation;
using LexRag.Core.Models;
using Microsoft.Extensions.AI;

namespace LexRag.Orchestration;

// Anti-hallucination is structural: with no usable context the model is never called, so it cannot invent.
public sealed class GroundedAnswerGenerator(IChatClient chatClient, RagOptions options) : IAnswerGenerator
{
    public async Task<RagAnswer> GenerateAsync(
        string query, IReadOnlyList<RetrievedChunk> context, CancellationToken ct = default)
    {
        if (GroundingPrompt.ShouldRefuse(context, options.MinRelevanceScore))
            return RagAnswer.Refusal(context);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, GroundingPrompt.SystemPrompt),
            new(ChatRole.User, GroundingPrompt.BuildUserPrompt(query, context)),
        };

        var response = await chatClient.GetResponseAsync(
            messages, new ChatOptions { Temperature = 0 }, ct);

        var text = response.Text?.Trim() ?? string.Empty;
        var citations = CitationParser.Parse(text);
        var saysNoAnswer = text.Contains(RagAnswer.NoAnswer, StringComparison.OrdinalIgnoreCase);

        // A citation existing is not enough; the cited source must actually be in the retrieved context.
        // CitationParser.Correctness returns 1.0 only when every citation maps to a supplied chunk; a hallucinated
        // [Fonte: doc_inventado.txt] scores < 1.0 and must not be reported as grounded.
        var citationCorrectness = citations.Count > 0 ? CitationParser.Correctness(text, context) : 0.0;
        var grounded = citations.Count > 0 && citationCorrectness >= 1.0 && !saysNoAnswer;

        return new RagAnswer
        {
            Text = text,
            Grounded = grounded,
            Citations = citations,
            Context = context,
        };
    }
}
