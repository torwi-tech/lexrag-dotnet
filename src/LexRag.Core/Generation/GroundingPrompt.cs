using System.Text;
using LexRag.Core.Models;

namespace LexRag.Core.Generation;

public static class GroundingPrompt
{
    public const string SystemPrompt = """
        Você é um assistente jurídico. Responda SOMENTE com base nos TRECHOS fornecidos.
        Regras inegociáveis:
        - Se a resposta não estiver nos trechos, responda exatamente: "Não encontrei nos documentos fornecidos."
        - NUNCA invente fatos, números de processo, artigos de lei ou jurisprudência.
        - Cite SEMPRE a fonte de cada afirmação no formato [Fonte: {arquivo}, trecho {n}].
        - Seja conciso e objetivo. Não emita opinião jurídica; apenas reporte o que os trechos dizem.
        """;

    // Refuse deterministically with no LLM call when there is no usable context.
    public static bool ShouldRefuse(IReadOnlyList<RetrievedChunk> context, double minScore) =>
        context.Count == 0 || context.All(c => c.Score < minScore);

    public static string BuildContextBlock(IReadOnlyList<RetrievedChunk> context)
    {
        var sb = new StringBuilder();
        foreach (var c in context)
        {
            sb.Append('[').Append("Fonte: ").Append(c.SourceFile)
              .Append(", trecho ").Append(c.ChunkIndex).Append("]\n");
            sb.Append(c.Content.Trim()).Append("\n\n");
        }
        return sb.ToString().TrimEnd();
    }

    public static string BuildUserPrompt(string query, IReadOnlyList<RetrievedChunk> context) => $"""
        === CONTEXTO ===
        {BuildContextBlock(context)}
        ================
        Pergunta: {query}
        """;
}
