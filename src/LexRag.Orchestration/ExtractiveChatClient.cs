using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace LexRag.Orchestration;

// Key-free extractive stand-in for a real IChatClient: answers only with a context passage + its citation, never invents.
public sealed partial class ExtractiveChatClient : IChatClient
{
    private const string Refusal = "Não encontrei nos documentos fornecidos.";

    private static readonly HashSet<string> Stopwords =
    [
        "qual", "quais", "como", "onde", "quando", "porque", "para", "pelo", "pela", "dos", "das",
        "que", "com", "uma", "uns", "umas", "por", "sobre", "the", "and", "what", "when", "where",
    ];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var prompt = string.Join("\n", messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text));
        var answer = Answer(prompt);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
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

    private static string Answer(string prompt)
    {
        var entries = ContextEntry().Matches(prompt)
            .Select(m => (File: m.Groups["file"].Value.Trim(),
                          Idx: m.Groups["idx"].Value.Trim(),
                          Body: m.Groups["body"].Value.Trim()))
            .Where(e => e.Body.Length > 0)
            .ToList();

        if (entries.Count == 0)
            return Refusal;

        var question = QuestionLine().Match(prompt) is { Success: true } q
            ? q.Groups["q"].Value
            : prompt;
        var questionTerms = Tokenize(question)
            .Where(t => t.Length > 2 && !Stopwords.Contains(t))
            .ToHashSet();

        var best = entries
            .Select(e => (Entry: e, Overlap: Tokenize(e.Body).Distinct().Count(questionTerms.Contains)))
            .OrderByDescending(x => x.Overlap)
            .First();

        // No grounded query term in the best passage means the corpus does not address the question, so refuse.
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

    [GeneratedRegex(@"\[Fonte:\s*(?<file>[^,\]]+?)\s*,\s*trecho\s*(?<idx>\d+)\s*\]\s*(?<body>.*?)(?=\[Fonte:|=====|$)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ContextEntry();

    [GeneratedRegex(@"Pergunta:\s*(?<q>.+)", RegexOptions.IgnoreCase)]
    private static partial Regex QuestionLine();

    [GeneratedRegex(@"[\p{L}\p{Nd}]+")]
    private static partial Regex TokenPattern();
}
