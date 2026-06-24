using System.Globalization;
using System.Text;

namespace LexRag.Core.Text;

// NFD-decompose then strip combining marks so "execução" and "execucao" tokenize identically,
// matching the unaccent behaviour of Postgres portuguese ts_rank on the sparse leg.
public static class DiacriticFolder
{
    public static string Fold(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
