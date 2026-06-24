namespace LexRag.Core.Retrieval;

public static class VectorMath
{
    public static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException($"Dimension mismatch: {a.Length} vs {b.Length}.");

        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        if (na == 0 || nb == 0) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    public static void NormalizeInPlace(float[] v)
    {
        double norm = 0;
        foreach (var x in v) norm += x * x;
        norm = Math.Sqrt(norm);
        if (norm == 0) return;
        for (var i = 0; i < v.Length; i++) v[i] = (float)(v[i] / norm);
    }
}
