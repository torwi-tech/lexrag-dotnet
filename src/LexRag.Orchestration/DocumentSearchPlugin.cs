using System.ComponentModel;
using LexRag.Core.Abstractions;
using LexRag.Core.Generation;
using LexRag.Core.Models;
using Microsoft.SemanticKernel;

namespace LexRag.Orchestration;

public sealed class DocumentSearchPlugin(IHybridRetriever retriever, RagOptions options)
{
    [KernelFunction("search_documents")]
    [Description("Busca trechos relevantes no acervo jurídico (decisões STJ/STF/CNJ) para uma pergunta. " +
                 "Retorna os trechos já formatados com a fonte para citação.")]
    // Returns a plain string on purpose: rich object graphs break SK chat-history serialization.
    public async Task<string> SearchAsync(
        [Description("A pergunta do usuário ou os termos de busca")] string query,
        CancellationToken ct = default)
    {
        var hits = await retriever.RetrieveAsync(query, options.TopK, ct);
        return hits.Count == 0
            ? "Nenhum trecho relevante encontrado no acervo."
            : GroundingPrompt.BuildContextBlock(hits);
    }
}
