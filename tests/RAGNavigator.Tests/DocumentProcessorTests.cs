using Microsoft.Extensions.Logging;
using NSubstitute;
using RAGNavigator.Application.Interfaces;
using RAGNavigator.Application.Models;
using RAGNavigator.Application.Services;
using Xunit;

namespace RAGNavigator.Tests;

public class DocumentProcessorTests : IDisposable
{
    private readonly IDocumentChunker _chunker = Substitute.For<IDocumentChunker>();
    private readonly IEmbeddingService _embeddingService = Substitute.For<IEmbeddingService>();
    private readonly ISearchIndexService _indexService = Substitute.For<ISearchIndexService>();
    private readonly ILogger<DocumentProcessor> _logger = Substitute.For<ILogger<DocumentProcessor>>();
    private readonly DocumentProcessor _processor;
    private readonly string _tempDir;

    private static readonly ReadOnlyMemory<float> FakeEmbedding = new float[] { 0.1f, 0.2f, 0.3f };

    public DocumentProcessorTests()
    {
        _processor = new DocumentProcessor(_chunker, _embeddingService, _indexService, _logger);
        _tempDir = Path.Combine(Path.GetTempPath(), $"rag-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task IngestDocumentsAsync_FullPipeline_ChunksEmbeddsAndUploads()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "doc.md"), "# Title\n\n## Section\n\nContent here.");

        var chunks = new List<DocumentChunk>
        {
            MakeChunk("doc.md", "Section", "Content here.", 0)
        };
        _chunker.Chunk(Arg.Any<string>(), "doc.md", Arg.Any<string>()).Returns(chunks);
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ReadOnlyMemory<float>> { FakeEmbedding });

        // Act
        var count = await _processor.IngestDocumentsAsync([_tempDir]);

        // Assert
        Assert.Equal(1, count);
        await _indexService.Received(1).CreateOrUpdateIndexAsync(Arg.Any<CancellationToken>());
        await _indexService.Received(1).DeleteAllDocumentsAsync(Arg.Any<CancellationToken>());
        await _indexService.Received(1).UploadChunksAsync(
            Arg.Is<IReadOnlyList<DocumentChunk>>(c => c.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestDocumentsAsync_MultipleFiles_ProcessesAll()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a.md"), "# Doc A\n\nContent A.");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "b.md"), "# Doc B\n\nContent B.");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "c.txt"), "Plain text content.");

        _chunker.Chunk(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci =>
            {
                var fileName = (string)ci[1];
                return new List<DocumentChunk> { MakeChunk(fileName, "Section", "Content", 0) };
            });
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var texts = (IReadOnlyList<string>)ci[0];
                return texts.Select(_ => FakeEmbedding).ToList();
            });

        // Act
        var count = await _processor.IngestDocumentsAsync([_tempDir]);

        // Assert — should find all 3 files (.md and .txt)
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task IngestDocumentsAsync_EmptyFolder_ReturnsZero()
    {
        // Arrange — empty temp directory, no files

        // Act
        var count = await _processor.IngestDocumentsAsync([_tempDir]);

        // Assert
        Assert.Equal(0, count);
        await _indexService.DidNotReceive().CreateOrUpdateIndexAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestDocumentsAsync_NonexistentFolder_ReturnsZero()
    {
        // Arrange
        var missing = Path.Combine(_tempDir, "does-not-exist");

        // Act
        var count = await _processor.IngestDocumentsAsync([missing]);

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task IngestDocumentsAsync_EmbeddingBatching_SendsBatchesOf16()
    {
        // Arrange — create 20 files so we get 20 chunks (exceeds batch size of 16)
        for (var i = 0; i < 20; i++)
            await File.WriteAllTextAsync(Path.Combine(_tempDir, $"doc{i:D2}.md"), $"# Doc {i}\n\nContent {i}.");

        _chunker.Chunk(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci =>
            {
                var fileName = (string)ci[1];
                return new List<DocumentChunk> { MakeChunk(fileName, "Section", "Content", 0) };
            });
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var texts = (IReadOnlyList<string>)ci[0];
                return texts.Select(_ => FakeEmbedding).ToList();
            });

        // Act
        var count = await _processor.IngestDocumentsAsync([_tempDir]);

        // Assert — 20 chunks total, should be 2 batches (16 + 4)
        Assert.Equal(20, count);
        await _embeddingService.Received(2).GenerateEmbeddingsAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestDocumentsAsync_SetsEmbeddingsOnChunks()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "embed.md"), "# Embed Test\n\nSome content.");

        var chunk = MakeChunk("embed.md", "Section", "Some content.", 0);
        _chunker.Chunk(Arg.Any<string>(), "embed.md", Arg.Any<string>())
            .Returns(new List<DocumentChunk> { chunk });

        var embedding = new ReadOnlyMemory<float>(new float[] { 1.0f, 2.0f, 3.0f });
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ReadOnlyMemory<float>> { embedding });

        // Act
        await _processor.IngestDocumentsAsync([_tempDir]);

        // Assert — the chunk should have its embedding set
        Assert.NotNull(chunk.Embedding);
        Assert.Equal(embedding.ToArray(), chunk.Embedding.Value.ToArray());
    }

    [Fact]
    public async Task IngestDocumentsAsync_EmbeddingCountMismatch_Throws()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "bad.md"), "# Bad\n\nContent.");

        _chunker.Chunk(Arg.Any<string>(), "bad.md", Arg.Any<string>())
            .Returns(new List<DocumentChunk> { MakeChunk("bad.md", "Section", "Content", 0) });
        // Return wrong number of embeddings
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ReadOnlyMemory<float>>()); // 0 instead of 1

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _processor.IngestDocumentsAsync([_tempDir]));
    }

    [Fact]
    public async Task IngestDocumentsAsync_CancellationRequested_ThrowsOperationCanceled()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "cancel.md"), "# Cancel\n\nContent.");
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        _chunker.Chunk(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(new List<DocumentChunk> { MakeChunk("cancel.md", "Section", "Content", 0) });

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _processor.IngestDocumentsAsync([_tempDir], cts.Token));
    }

    [Fact]
    public async Task IngestDocumentsAsync_InvalidCharactersInDocument_ProcessedWithoutError()
    {
        // Arrange — content with unicode, null bytes, and special characters
        var content = "# Special Chars\n\n" +
                      "Contains emoji: \ud83d\ude80\ud83c\udf1f\n" +
                      "Unicode: \u00e9\u00e8\u00ea\u00eb \u00fc\u00f6\u00e4 \u4f60\u597d \u0410\u0411\u0412\n" +
                      "Control chars: \t\r\n" +
                      "Symbols: \u00a9 \u00ae \u2122 \u00b0 \u00b1 \u2260 \u2264 \u2265\n" +
                      "Zero-width: \u200b\u200c\u200d\ufeff\n" +
                      "Backslash paths: C:\\Users\\test\\file.md\n" +
                      "Angle brackets: <script>alert('xss')</script>\n";

        await File.WriteAllTextAsync(Path.Combine(_tempDir, "special.md"), content);

        _chunker.Chunk(Arg.Any<string>(), "special.md", Arg.Any<string>())
            .Returns(new List<DocumentChunk> { MakeChunk("special.md", "Special Chars", content, 0) });
        _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ReadOnlyMemory<float>> { FakeEmbedding });

        // Act — should not throw
        var count = await _processor.IngestDocumentsAsync([_tempDir]);

        // Assert
        Assert.Equal(1, count);
        _chunker.Received(1).Chunk(Arg.Is<string>(s => s.Contains("\ud83d\ude80")), "special.md", Arg.Any<string>());
    }

    [Fact]
    public async Task IngestDocumentsAsync_MultipleFolders_AggregatesFiles()
    {
        // Arrange — two separate folders
        var dir2 = Path.Combine(Path.GetTempPath(), $"rag-test2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir2);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(_tempDir, "a.md"), "# A\n\nContent A.");
            await File.WriteAllTextAsync(Path.Combine(dir2, "b.md"), "# B\n\nContent B.");

            _chunker.Chunk(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(ci =>
                {
                    var fileName = (string)ci[1];
                    return new List<DocumentChunk> { MakeChunk(fileName, "Section", "Content", 0) };
                });
            _embeddingService.GenerateEmbeddingsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    var texts = (IReadOnlyList<string>)ci[0];
                    return texts.Select(_ => FakeEmbedding).ToList();
                });

            // Act
            var count = await _processor.IngestDocumentsAsync([_tempDir, dir2]);

            // Assert
            Assert.Equal(2, count);
        }
        finally
        {
            if (Directory.Exists(dir2))
                Directory.Delete(dir2, recursive: true);
        }
    }

    private static DocumentChunk MakeChunk(string fileName, string section, string content, int index)
    {
        return new DocumentChunk
        {
            ChunkId = $"{fileName}_{section}_{index}",
            DocumentId = "doc-test",
            DocumentTitle = Path.GetFileNameWithoutExtension(fileName),
            FileName = fileName,
            Section = section,
            ChunkIndex = index,
            Content = content
        };
    }
}
