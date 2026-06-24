using LexRag.Core.Generation;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;

namespace LexRag.Orchestration;

// Agentic-RAG opt-in: the model decides when to call the search tool. DocumentSearchPlugin is an SK
// KernelFunction exposed to the chat loop as an AIFunction. Microsoft.Extensions.AI's function-invocation
// middleware runs the tool loop, so the same path works with the keyless fake and a real Azure model.
// (SK's own InvokePromptAsync auto-invocation only fires for connectors that natively advertise tool
// calling, which the keyless fake does not, so the loop is driven at the M.E.AI layer instead.)
public sealed class SemanticKernelRagService
{
    private readonly IChatClient _chatClient;
    private readonly AIFunction _searchTool;

    public SemanticKernelRagService(
        IChatClient chatClient,
        DocumentSearchPlugin plugin,
        LoggingFunctionFilter? filter = null)
    {
        _chatClient = chatClient;

        var kernel = Kernel.CreateBuilder().Build();
        if (filter is not null)
            kernel.FunctionInvocationFilters.Add(filter);
        var searchFunction = kernel.ImportPluginFromObject(plugin, "documents")["search_documents"];
        // AsAIFunction(Kernel) is the only form that binds the kernel the function needs at invoke time;
        // the non-obsolete alternatives (using the KernelFunction directly, or Clone) leave it unbound and
        // the tool returns nothing. Suppress this single deprecation narrowly until SK ships a bound replacement.
#pragma warning disable CS0618
        _searchTool = searchFunction.AsAIFunction(kernel);
#pragma warning restore CS0618
    }

    public async Task<string> AskAgenticAsync(string query, CancellationToken ct = default)
    {
        using var client = _chatClient.AsBuilder().UseFunctionInvocation().Build();

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, GroundingPrompt.SystemPrompt),
            new(ChatRole.User, $"""
                Use a ferramenta search_documents para obter trechos antes de responder.
                Pergunta: {query}
                """),
        };

        var options = new ChatOptions { Tools = [_searchTool], ToolMode = ChatToolMode.Auto };
        var response = await client.GetResponseAsync(messages, options, ct);
        return response.Text ?? string.Empty;
    }
}
