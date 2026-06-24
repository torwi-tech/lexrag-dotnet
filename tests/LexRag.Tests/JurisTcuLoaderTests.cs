using FluentAssertions;
using LexRag.Api.Corpus;
using LexRag.Api.Eval;

namespace LexRag.Tests;

/// <summary>
/// Keyless fixture tests for the JurisTCU CSV loaders.
/// Uses temporary mini-CSVs in the real column format to exercise parsing logic
/// (HTML stripping, cutoff filtering, maxDocuments/maxQueries limits).
/// </summary>
public sealed class JurisTcuLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"juristcu-{Guid.NewGuid():N}");

    public JurisTcuLoaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private void WriteDocCsv(string content) =>
        File.WriteAllText(Path.Combine(_dir, "doc.csv"), content);

    private void WriteQueryCsv(string content) =>
        File.WriteAllText(Path.Combine(_dir, "query.csv"), content);

    private void WriteQrelCsv(string content) =>
        File.WriteAllText(Path.Combine(_dir, "qrel.csv"), content);

    // ── CorpusSource tests ────────────────────────────────────────────────────

    [Fact]
    public async Task CorpusSource_strips_html_tags_and_returns_key_as_name()
    {
        WriteDocCsv(
            """
            KEY,NUMACORDAO,ANOACORDAO,COLEGIADO,AREA,TEMA,SUBTEMA,ENUNCIADO,EXCERTO
            DOC001,1,2020,TCU,ADM,X,Y,"<b>Licitação</b> é <s>obrigatória</s> para contratos.",excerpt1
            DOC002,2,2021,TCU,ADM,X,Y,"<p>Dispensa deve ser <b>justificada</b>.</p>",excerpt2
            DOC003,3,2022,TCU,ADM,X,Y,"Texto simples sem tags.",excerpt3
            """);

        var source = new JurisTcuCorpusSource(_dir, maxDocuments: 0);
        var docs = await source.GetDocumentsAsync().ToListAsync();

        docs.Should().HaveCount(3);

        var doc1 = docs.Single(d => d.Name == "DOC001");
        doc1.Text.Should().Be("Licitação é obrigatória para contratos.");
        doc1.Text.Should().NotContain("<b>");

        var doc2 = docs.Single(d => d.Name == "DOC002");
        doc2.Text.Should().NotContain("<p>").And.NotContain("</p>").And.NotContain("<b>");
        doc2.Text.Should().Contain("Dispensa deve ser");

        var doc3 = docs.Single(d => d.Name == "DOC003");
        doc3.Text.Should().Be("Texto simples sem tags.");
    }

    [Fact]
    public async Task CorpusSource_decodes_html_entities()
    {
        WriteDocCsv(
            """
            KEY,NUMACORDAO,ANOACORDAO,COLEGIADO,AREA,TEMA,SUBTEMA,ENUNCIADO,EXCERTO
            ENT001,1,2020,TCU,ADM,X,Y,"Custo &amp; benefício &lt;avaliado&gt;.",exc
            """);

        var source = new JurisTcuCorpusSource(_dir, maxDocuments: 0);
        var docs = await source.GetDocumentsAsync().ToListAsync();

        docs.Single().Text.Should().Be("Custo & benefício <avaliado>.");
    }

    [Fact]
    public async Task CorpusSource_respects_maxDocuments_limit()
    {
        WriteDocCsv(
            """
            KEY,NUMACORDAO,ANOACORDAO,COLEGIADO,AREA,TEMA,SUBTEMA,ENUNCIADO,EXCERTO
            D1,1,2020,TCU,ADM,X,Y,Texto um.,e1
            D2,2,2021,TCU,ADM,X,Y,Texto dois.,e2
            D3,3,2022,TCU,ADM,X,Y,Texto três.,e3
            """);

        var source = new JurisTcuCorpusSource(_dir, maxDocuments: 2);
        var docs = await source.GetDocumentsAsync().ToListAsync();

        docs.Should().HaveCount(2);
    }

    [Fact]
    public async Task CorpusSource_skips_rows_with_empty_enunciado()
    {
        WriteDocCsv(
            """
            KEY,NUMACORDAO,ANOACORDAO,COLEGIADO,AREA,TEMA,SUBTEMA,ENUNCIADO,EXCERTO
            GOOD,1,2020,TCU,ADM,X,Y,Texto válido.,exc
            EMPTY,2,2021,TCU,ADM,X,Y,,exc
            """);

        var source = new JurisTcuCorpusSource(_dir, maxDocuments: 0);
        var docs = await source.GetDocumentsAsync().ToListAsync();

        docs.Should().HaveCount(1);
        docs.Single().Name.Should().Be("GOOD");
    }

    [Fact]
    public async Task CorpusSource_returns_empty_when_doc_csv_missing()
    {
        // _dir exists but doc.csv was never written
        var source = new JurisTcuCorpusSource(_dir, maxDocuments: 0);
        var docs = await source.GetDocumentsAsync().ToListAsync();
        docs.Should().BeEmpty();
    }

    // ── GoldenSet tests ───────────────────────────────────────────────────────

    [Fact]
    public void GoldenSet_builds_eval_cases_with_docs_above_cutoff()
    {
        WriteQueryCsv(
            """
            ID,TEXT,SOURCE
            1,licitação obrigatória,search log
            2,dispensa justificada,search log
            """);

        WriteQrelCsv(
            """
            QUERY_ID,DOC_ID,SCORE,ENGINE,RANK
            1,DOC_A,3,BM25,1
            1,DOC_B,1,BM25,2
            1,DOC_C,2,BM25,3
            2,DOC_D,1,BM25,1
            2,DOC_E,3,BM25,2
            """);

        // cutoff = 2 → SCORE 1 rows are filtered out (DOC_B, DOC_D)
        var gs = new JurisTcuGoldenSet(_dir, relevanceCutoff: 2, maxQueries: 0);
        var cases = gs.GetCases();

        cases.Should().HaveCount(2);

        var q1 = cases.Single(c => c.Query == "licitação obrigatória");
        q1.InDomain.Should().BeTrue();
        q1.ExpectedSourceFiles.Should().BeEquivalentTo(["DOC_A", "DOC_C"]);
        q1.ExpectedSourceFiles.Should().NotContain("DOC_B"); // SCORE 1 < cutoff 2

        var q2 = cases.Single(c => c.Query == "dispensa justificada");
        q2.ExpectedSourceFiles.Should().BeEquivalentTo(["DOC_E"]);
        q2.ExpectedSourceFiles.Should().NotContain("DOC_D"); // SCORE 1 < cutoff 2
    }

    [Fact]
    public void GoldenSet_respects_maxQueries_limit()
    {
        WriteQueryCsv(
            """
            ID,TEXT,SOURCE
            1,query um,search log
            2,query dois,search log
            3,query três,search log
            """);

        WriteQrelCsv(
            """
            QUERY_ID,DOC_ID,SCORE,ENGINE,RANK
            1,D1,3,BM25,1
            2,D2,3,BM25,1
            3,D3,3,BM25,1
            """);

        var gs = new JurisTcuGoldenSet(_dir, relevanceCutoff: 1, maxQueries: 2);
        gs.GetCases().Should().HaveCount(2);
    }

    [Fact]
    public void GoldenSet_excludes_queries_with_no_relevant_docs()
    {
        WriteQueryCsv(
            """
            ID,TEXT,SOURCE
            1,com relevância,search log
            2,sem relevância,search log
            """);

        // Query 2 has only low-score docs; with cutoff=2 it will have zero relevant docs → excluded
        WriteQrelCsv(
            """
            QUERY_ID,DOC_ID,SCORE,ENGINE,RANK
            1,D1,3,BM25,1
            2,D2,1,BM25,1
            """);

        var gs = new JurisTcuGoldenSet(_dir, relevanceCutoff: 2, maxQueries: 0);
        var cases = gs.GetCases();

        cases.Should().HaveCount(1);
        cases.Single().Query.Should().Be("com relevância");
    }

    [Fact]
    public void GoldenSet_returns_empty_when_files_missing()
    {
        var gs = new JurisTcuGoldenSet(_dir, relevanceCutoff: 1, maxQueries: 0);
        gs.GetCases().Should().BeEmpty();
    }
}

// Async LINQ helper (ToListAsync for IAsyncEnumerable)
file static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source) list.Add(item);
        return list;
    }
}
