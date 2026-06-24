using System.Text;
using FluentAssertions;
using LexRag.Core.Chunking;
using LexRag.Core.Models;
using LexRag.Core.Retrieval;
using LexRag.Embeddings;
using LexRag.Index;
using LexRag.Ingestion;
using LexRag.Retrieval;

namespace LexRag.Tests;

public class DiacriticFoldingRetrievalTests
{
    // Builds a minimal in-memory keyword retriever — no embedder or dense leg needed.
    private static (InMemoryKeywordRetriever retriever, DocumentIngestor ingestor) BuildStack()
    {
        var options = new RagOptions();
        var store = new InMemoryChunkStore();
        var embedder = new HashEmbedder(options.EmbeddingDimensions);
        var vectorIndex = new InMemoryVectorIndex(store);
        var chunker = new StructuralChunker(options);
        var router = new ExtensionTextExtractorRouter(new PdfTextExtractor(), new PlainTextExtractor());
        var ingestor = new DocumentIngestor(router, chunker, embedder, vectorIndex);
        var retriever = new InMemoryKeywordRetriever(store, new Bm25Scorer());
        return (retriever, ingestor);
    }

    private static async Task IngestText(DocumentIngestor ingestor, string name, string text)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await ingestor.IngestAsync(name, stream);
    }

    [Fact]
    public async Task Accented_query_finds_chunk_indexed_with_accented_term()
    {
        // "execução" in corpus; query "execucao" (no accent) must still hit it.
        var (retriever, ingestor) = BuildStack();
        await IngestText(ingestor, "fiscal.txt",
            "A execução fiscal suspende-se por um ano antes da prescrição intercorrente.");

        var results = await retriever.SearchAsync("execucao", top: 5);

        results.Should().NotBeEmpty("accent-folded query must hit the accented term in the corpus");
        results.Should().Contain(r => r.SourceFile == "fiscal.txt");
    }

    [Fact]
    public async Task Unaccented_corpus_term_found_by_accented_query()
    {
        // Reverse: corpus has no accent, query does — must still match.
        var (retriever, ingestor) = BuildStack();
        await IngestText(ingestor, "doc.txt",
            "A execucao fiscal suspende-se por um ano antes da prescricao intercorrente.");

        var results = await retriever.SearchAsync("execução", top: 5);

        results.Should().NotBeEmpty("accented query must hit unaccented corpus term after folding");
    }
}

public class IdempotentReingestionTests
{
    private static (InMemoryVectorIndex index, InMemoryChunkStore store, DocumentIngestor ingestor) BuildStack()
    {
        var options = new RagOptions();
        var store = new InMemoryChunkStore();
        var embedder = new HashEmbedder(options.EmbeddingDimensions);
        var vectorIndex = new InMemoryVectorIndex(store);
        var chunker = new StructuralChunker(options);
        var router = new ExtensionTextExtractorRouter(new PdfTextExtractor(), new PlainTextExtractor());
        var ingestor = new DocumentIngestor(router, chunker, embedder, vectorIndex);
        return (vectorIndex, store, ingestor);
    }

    private static async Task<int> IngestText(DocumentIngestor ingestor, string name, string text)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return await ingestor.IngestAsync(name, stream);
    }

    [Fact]
    public async Task Reingesting_shorter_document_leaves_no_orphan_chunks()
    {
        // Ingest a 3-chunk document, then re-ingest a 1-chunk version of the same file.
        // The store must contain exactly the chunks from the second ingest and no orphans.
        var (_, store, ingestor) = BuildStack();

        // Build a body large enough to force >= 3 chunks at the default chunk size.
        var longContent = string.Join(" ",
            Enumerable.Range(1, 60).Select(i =>
                $"Parágrafo jurídico número {i} sobre execução fiscal e prescrição intercorrente nos termos do artigo 40."));

        var firstCount = await IngestText(ingestor, "doc.txt", longContent);
        firstCount.Should().BeGreaterThan(1, "the long document must produce more than one chunk");

        // Re-ingest a short version of the same file (guaranteed single chunk).
        var shortContent = "Versão editada do documento: apenas este parágrafo permanece.";
        var secondCount = await IngestText(ingestor, "doc.txt", shortContent);

        var allChunks = store.All();
        allChunks.Should().HaveCount(secondCount,
            "re-ingesting a shorter document must remove orphan chunks from the first ingest");
        allChunks.Should().OnlyContain(c => c.SourceFile == "doc.txt");
    }

    [Fact]
    public async Task Reingesting_same_length_document_still_reflects_new_content()
    {
        // Regression guard: same-or-more-chunks re-ingest must not regress.
        var (_, store, ingestor) = BuildStack();

        await IngestText(ingestor, "doc.txt", "Conteúdo original sobre licitação pública.");
        await IngestText(ingestor, "doc.txt", "Conteúdo atualizado sobre licitação pública e contrato.");

        var chunks = store.All();
        chunks.Should().NotBeEmpty();
        chunks.Should().OnlyContain(c => c.SourceFile == "doc.txt");
        chunks.Should().Contain(c => c.Content.Contains("atualizado"),
            "store must reflect the updated content after re-ingest");
    }
}
