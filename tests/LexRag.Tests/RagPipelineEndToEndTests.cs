using System.Text;
using FluentAssertions;
using LexRag.Core.Chunking;
using LexRag.Core.Models;
using LexRag.Core.Retrieval;
using LexRag.Embeddings;
using LexRag.Index;
using LexRag.Ingestion;
using LexRag.Orchestration;
using LexRag.Retrieval;

namespace LexRag.Tests;

public class RagPipelineEndToEndTests
{
    private static (RagPipeline pipeline, DocumentIngestor ingestor) BuildStack()
    {
        var options = new RagOptions();
        var store = new InMemoryChunkStore();
        var embedder = new HashEmbedder(options.EmbeddingDimensions);
        var chunker = new StructuralChunker(options);
        var vectorIndex = new InMemoryVectorIndex(store);
        var keyword = new InMemoryKeywordRetriever(store, new Bm25Scorer());
        var hybrid = new InMemoryHybridRetriever(vectorIndex, keyword, embedder, options);
        var generator = new GroundedAnswerGenerator(new ExtractiveChatClient(), options);
        var pipeline = new RagPipeline(hybrid, new LexicalCoverageReranker(), generator, options);
        var router = new ExtensionTextExtractorRouter(new PdfTextExtractor(), new PlainTextExtractor());
        var ingestor = new DocumentIngestor(router, chunker, embedder, vectorIndex);
        return (pipeline, ingestor);
    }

    private static async Task Ingest(DocumentIngestor ingestor, string name, string content)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await ingestor.IngestAsync(name, stream);
    }

    [Fact]
    public async Task In_domain_question_is_grounded_and_cites_the_right_document()
    {
        var (pipeline, ingestor) = BuildStack();
        await Ingest(ingestor, "stj.txt",
            "A prescrição intercorrente na execução fiscal tem termo inicial após o prazo de um ano de suspensão do processo, nos termos do artigo 40 da Lei 6.830.");
        await Ingest(ingestor, "cnj.txt",
            "O uso de inteligência artificial no judiciário exige supervisão humana e citação das fontes consultadas.");

        var answer = await pipeline.AskAsync("Qual o termo inicial da prescrição intercorrente na execução fiscal?");

        answer.Grounded.Should().BeTrue();
        answer.Citations.Should().Contain(c => c.SourceFile == "stj.txt");
    }

    [Fact]
    public async Task Out_of_domain_question_is_refused_not_invented()
    {
        var (pipeline, ingestor) = BuildStack();
        await Ingest(ingestor, "stj.txt",
            "A prescrição intercorrente na execução fiscal tem termo inicial após um ano de suspensão.");

        var answer = await pipeline.AskAsync("Qual a capital da Austrália?");

        answer.Grounded.Should().BeFalse();
        answer.Text.Should().Contain("Não encontrei");
    }

    [Fact]
    public async Task Ingestion_writes_chunks_to_the_index()
    {
        var (_, ingestor) = BuildStack();
        var written = await Ingest_returning(ingestor, "doc.txt", "Conteúdo jurídico de exemplo para indexação.");
        written.Should().BeGreaterThan(0);
    }

    private static async Task<int> Ingest_returning(DocumentIngestor ingestor, string name, string content)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return await ingestor.IngestAsync(name, stream);
    }
}
