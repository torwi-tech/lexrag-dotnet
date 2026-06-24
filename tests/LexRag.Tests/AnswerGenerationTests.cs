using FluentAssertions;
using LexRag.Core.Models;
using LexRag.Orchestration;
using Microsoft.Extensions.AI;

namespace LexRag.Tests;

public class AnswerGenerationTests
{
    private static readonly RagOptions Options = new();

    [Fact]
    public async Task No_context_refuses_without_ever_calling_the_model()
    {
        // The structural anti-hallucination guarantee: empty context ⇒ deterministic refusal,
        // and the (throwing) model is never invoked.
        var generator = new GroundedAnswerGenerator(new ThrowingChatClient(), Options);

        var answer = await generator.GenerateAsync("qualquer pergunta", context: []);

        answer.Grounded.Should().BeFalse();
        answer.Text.Should().Be(RagAnswer.NoAnswer);
    }

    [Fact]
    public async Task Relevant_context_produces_a_grounded_cited_answer()
    {
        var generator = new GroundedAnswerGenerator(new ExtractiveChatClient(), Options);
        var context = new[] { Chunk("stj.txt", 0,
            "A prescrição intercorrente na execução fiscal tem termo inicial após um ano de suspensão.") };

        var answer = await generator.GenerateAsync("prescrição intercorrente execução fiscal", context);

        answer.Grounded.Should().BeTrue();
        answer.Citations.Should().ContainSingle().Which.SourceFile.Should().Be("stj.txt");
    }

    [Fact]
    public async Task Context_that_does_not_address_the_question_is_refused()
    {
        // Anti-hallucination: chunks were retrieved, but none address the question → refuse.
        var generator = new GroundedAnswerGenerator(new ExtractiveChatClient(), Options);
        var context = new[] { Chunk("stj.txt", 0,
            "A prescrição intercorrente na execução fiscal tem termo inicial após um ano de suspensão.") };

        var answer = await generator.GenerateAsync("qual a capital da Austrália", context);

        answer.Grounded.Should().BeFalse();
        answer.Text.Should().Contain("Não encontrei");
    }

    private static RetrievedChunk Chunk(string file, int idx, string content) => new()
    {
        ChunkKey = Guid.NewGuid(), SourceFile = file, ChunkIndex = idx, Content = content, Score = 1.0,
    };

    private sealed class ThrowingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("model must not be called when context is empty");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("model must not be called when context is empty");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
