using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace LexRag.Orchestration;

// Key-free IChatClient that participates in the MEAI/SK function-calling protocol AND handles
// the direct-context (no-tool) path used by GroundedAnswerGenerator.
//
// Turn 1 with tools offered: emit FunctionCallContent for the first search tool so SK's
//   FunctionChoiceBehavior.Auto loop invokes DocumentSearchPlugin.SearchAsync.
// Turn 2 after tool execution: SK appends a Tool-role message with FunctionResultContent;
//   apply extractive grounding over that result to produce a cited answer.
// Turn 1 with no tools: context is already embedded in the user prompt (GroundedAnswerGenerator
//   path); apply extractive grounding directly, same as ExtractiveChatClient.
public sealed partial class ToolCallingExtractiveChatClient : IChatClient
{
    private const string CallId = "fake-call-1";
    private const string Refusal = "Não encontrei nos documentos fornecidos.";

    private static readonly HashSet<string> Stopwords =
    [
        "qual", "quais", "como", "onde", "quando", "porque", "para", "pelo", "pela", "dos", "das",
        "que", "com", "uma", "uns", "umas", "por", "sobre", "the", "and", "what", "when", "where",
    ];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var messageList = messages.ToList();

        // Turn 2: Tool-role message present means SK already ran the plugin.
        var toolResult = messageList
            .Where(m => m.Role == ChatRole.Tool)
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .FirstOrDefault();

        if (toolResult is not null)
        {
            var query = ExtractUserQuery(messageList);
            var answer = GroundedAnswer(query, toolResult.Result?.ToString() ?? string.Empty);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
        }

        // Turn 1 with tools: delegate retrieval to the search plugin via a tool call.
        var searchTool = options?.Tools?
            .OfType<AIFunction>()
            .FirstOrDefault(t => t.Name.Contains("search", StringComparison.OrdinalIgnoreCase));

        if (searchTool is not null)
        {
            var query = ExtractUserQuery(messageList);
            var toolCall = new FunctionCallContent(
                callId: CallId,
                name: searchTool.Name,
                arguments: new Dictionary<string, object?> { ["query"] = query });

            // FinishReason.ToolCalls is what the SK auto-invocation loop checks to decide it must run
            // the tool and call back; without it the bare function-call message is taken as a final answer.
            var assistantMsg = new ChatMessage(ChatRole.Assistant, [toolCall]);
            return Task.FromResult(new ChatResponse(assistantMsg) { FinishReason = ChatFinishReason.ToolCalls });
        }

        // No tools: context already embedded in prompt (GroundedAnswerGenerator path).
        var prompt = string.Join("\n", messageList
            .Where(m => m.Role == ChatRole.User)
            .Select(m => m.Text ?? string.Empty));
        return Task.FromResult(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, GroundedAnswer(ExtractUserQuery(messageList), prompt))));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }

    private static string ExtractUserQuery(IReadOnlyList<ChatMessage> messages)
    {
        var fullText = string.Join("\n", messages
            .Where(m => m.Role == ChatRole.User)
            .Select(m => m.Text ?? string.Empty));

        var match = QuestionLine().Match(fullText);
        return match.Success ? match.Groups["q"].Value.Trim() : fullText.Trim();
    }

    private static string GroundedAnswer(string query, string context)
    {
        var entries = ContextEntry().Matches(context)
            .Select(m => (File: m.Groups["file"].Value.Trim(),
                          Idx: m.Groups["idx"].Value.Trim(),
                          Body: m.Groups["body"].Value.Trim()))
            .Where(e => e.Body.Length > 0)
            .ToList();

        if (entries.Count == 0)
            return Refusal;

        var questionTerms = Tokenize(query)
            .Where(t => t.Length > 2 && !Stopwords.Contains(t))
            .ToHashSet();

        var best = entries
            .Select(e => (Entry: e, Overlap: Tokenize(e.Body).Distinct().Count(questionTerms.Contains)))
            .OrderByDescending(x => x.Overlap)
            .First();

        if (best.Overlap == 0)
            return Refusal;

        var snippet = FirstSentences(best.Entry.Body, 320);
        return $"De acordo com os documentos, {snippet} [Fonte: {best.Entry.File}, trecho {best.Entry.Idx}]";
    }

    private static string FirstSentences(string text, int maxChars)
    {
        var trimmed = text.Length <= maxChars ? text : text[..maxChars];
        var lastStop = trimmed.LastIndexOfAny(['.', '!', '?']);
        return lastStop > 40 ? trimmed[..(lastStop + 1)] : trimmed;
    }

    private static IEnumerable<string> Tokenize(string text) =>
        TokenPattern().Matches(text.ToLowerInvariant()).Select(m => m.Value);

    // Matches the context block format produced by GroundingPrompt.BuildContextBlock.
    [GeneratedRegex(@"\[Fonte:\s*(?<file>[^,\]]+?)\s*,\s*trecho\s*(?<idx>\d+)\s*\]\s*(?<body>.*?)(?=\[Fonte:|=====|$)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ContextEntry();

    [GeneratedRegex(@"Pergunta:\s*(?<q>.+)", RegexOptions.IgnoreCase)]
    private static partial Regex QuestionLine();

    [GeneratedRegex(@"[\p{L}\p{Nd}]+")]
    private static partial Regex TokenPattern();
}
