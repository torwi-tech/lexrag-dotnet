using LexRag.Core.Models;

namespace LexRag.Core.Abstractions;

public interface IRetrievalGrader
{
    Task<RetrievalGrade> GradeAsync(string query, IReadOnlyList<RetrievedChunk> context, CancellationToken ct = default);
}

public interface IQueryReformulator
{
    Task<string> ReformulateAsync(string query, RetrievalGrade grade, CancellationToken ct = default);
}
