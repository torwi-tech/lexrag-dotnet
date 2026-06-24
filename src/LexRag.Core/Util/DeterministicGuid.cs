using System.Security.Cryptography;
using System.Text;

namespace LexRag.Core.Util;

// Stable hash of (file, chunk index) produces a predictable key per chunk position.
// True idempotency on re-ingest (including shorter edits) requires DeleteBySourceFileAsync before Upsert.
public static class DeterministicGuid
{
    public static Guid From(string name)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(name));
        return new Guid(hash);
    }
}
