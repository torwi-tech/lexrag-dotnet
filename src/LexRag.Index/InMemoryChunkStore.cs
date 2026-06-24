using System.Collections.Concurrent;
using LexRag.Core.Models;

namespace LexRag.Index;

// Single source of truth shared by the in-memory vector index and keyword retriever so both legs see the same corpus.
public sealed class InMemoryChunkStore
{
    private readonly ConcurrentDictionary<Guid, DocumentChunk> _chunks = new();

    public void Upsert(IEnumerable<DocumentChunk> chunks)
    {
        foreach (var c in chunks)
            _chunks[c.ChunkKey] = c;
    }

    // Removes all chunks for a source file before a re-ingest so a shorter edit cannot leave orphans.
    public void DeleteBySourceFile(string sourceFile)
    {
        var keys = _chunks.Values
            .Where(c => c.SourceFile == sourceFile)
            .Select(c => c.ChunkKey)
            .ToList();
        foreach (var key in keys)
            _chunks.TryRemove(key, out _);
    }

    public IReadOnlyList<DocumentChunk> All() => _chunks.Values.ToList();

    public long Count => _chunks.Count;
}
