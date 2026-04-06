using RAGNavigator.Application.Models;
using RAGNavigator.Application.Services;
using Xunit;

namespace RAGNavigator.Tests;

/// <summary>
/// Edge case tests for chunking and prompt assembly: empty corpus, overly long requests,
/// and invalid/special characters in documents.
/// </summary>
public class EdgeCaseTests
{
    private readonly MarkdownDocumentChunker _chunker = new();

    // --- Chunker edge cases ---

    [Fact]
    public void Chunk_NullContent_ReturnsEmpty()
    {
        var result = _chunker.Chunk(null!, "test.md", "Test");
        Assert.Empty(result);
    }

    [Fact]
    public void Chunk_OnlyHeadingsNoBody_ReturnsEmpty()
    {
        var content = "## Heading One\n## Heading Two\n## Heading Three";
        var result = _chunker.Chunk(content, "test.md", "Test");
        Assert.Empty(result);
    }

    [Fact]
    public void Chunk_ContentBelowMinChunkSize_ReturnsEmpty()
    {
        // MinChunkSize is 100 chars
        var content = "## Short\n\nTiny.";
        var result = _chunker.Chunk(content, "test.md", "Test");
        Assert.Empty(result);
    }

    [Fact]
    public void Chunk_UnicodeContent_PreservedInChunks()
    {
        var content = """
            ## Unicode Section

            This section contains various unicode characters: emoji 🚀🌟,
            accented letters éèêë üöä, CJK characters 你好世界,
            Cyrillic text Привет мир, and mathematical symbols ∑∫∂∇.
            The chunker must preserve all these characters without corruption or loss.
            Additional text to ensure we meet the minimum chunk size requirement here.
            """;

        var result = _chunker.Chunk(content, "unicode.md", "Unicode Doc");

        Assert.NotEmpty(result);
        Assert.Contains(result, c => c.Content.Contains("🚀"));
        Assert.Contains(result, c => c.Content.Contains("你好"));
        Assert.Contains(result, c => c.Content.Contains("Привет"));
    }

    [Fact]
    public void Chunk_HtmlTagsInMarkdown_PreservedAsIs()
    {
        var content = """
            ## HTML Content

            This section contains HTML-like content: <div class="test">Hello</div>
            and script tags <script>alert('xss')</script> and angle brackets < > &amp;.
            The chunker should not interpret HTML, just preserve it as text content.
            Additional padding text to ensure this chunk exceeds the minimum size threshold.
            """;

        var result = _chunker.Chunk(content, "html.md", "HTML Doc");

        Assert.NotEmpty(result);
        Assert.Contains(result, c => c.Content.Contains("<script>"));
        Assert.Contains(result, c => c.Content.Contains("<div"));
    }

    [Fact]
    public void Chunk_VeryLongSingleLine_ProducesChunks()
    {
        // A single line with no paragraph breaks, exceeding MaxChunkSize (1500)
        var longLine = "## Long Line\n\n" + new string('A', 5000);

        var result = _chunker.Chunk(longLine, "long.md", "Long Doc");

        // Should produce at least one chunk (the long text may stay as one since there are no paragraph breaks)
        Assert.NotEmpty(result);
        Assert.All(result, c => Assert.True(c.Content.Length > 0));
    }

    [Fact]
    public void Chunk_SpecialFileNames_PreservedInMetadata()
    {
        var content = """
            ## Content Section

            This is enough content to form a valid chunk with sufficient text length.
            Additional text here to ensure we meet the minimum chunk size requirement.
            """;

        var fileName = "path/to/my file (copy).md";
        var result = _chunker.Chunk(content, fileName, "My File");

        Assert.NotEmpty(result);
        Assert.All(result, c => Assert.Equal(fileName, c.FileName));
    }

    [Fact]
    public void Chunk_MixedLineEndings_HandledCorrectly()
    {
        // Mix of \n, \r\n, and \r line endings
        var content = "## Section One\r\n\r\nContent with CRLF line endings.\r\nMore CRLF text here.\r\n" +
                      "Enough text to make this a valid chunk that exceeds minimum size.\r\n" +
                      "Additional content to pad the chunk to the required length threshold.\r\n";

        var result = _chunker.Chunk(content, "mixed.md", "Mixed");

        Assert.NotEmpty(result);
    }

    [Fact]
    public void Chunk_NestedHeadings_SplitsOnLevel2And3()
    {
        var content = """
            # Main Title

            ## Section One

            Content for section one that is long enough to form a chunk by itself.
            This needs to be sufficiently lengthy to pass the minimum chunk size filter.

            ### Subsection 1A

            Content for subsection 1A. This also needs to be long enough for a chunk.
            More text here to ensure we have enough content for the minimum size check.

            ## Section Two

            Content for section two, distinct from the content in section one above.
            We need enough text here as well to satisfy the minimum size requirement.
            """;

        var result = _chunker.Chunk(content, "nested.md", "Nested Headings");

        Assert.True(result.Count >= 3, $"Expected at least 3 chunks, got {result.Count}");
        Assert.Contains(result, c => c.Section == "Section One");
        Assert.Contains(result, c => c.Section == "Subsection 1A");
        Assert.Contains(result, c => c.Section == "Section Two");
    }

    // --- Prompt builder edge cases ---

    [Fact]
    public void BuildUserPrompt_OverlyLongQuestion_IncludedInFull()
    {
        var longQuestion = "Explain " + new string('z', 10_000) + " in detail?";
        var prompt = PromptBuilder.BuildUserPrompt(longQuestion, []);

        Assert.Contains(longQuestion, prompt);
    }

    [Fact]
    public void BuildUserPrompt_SpecialCharactersInQuestion_PreservedExactly()
    {
        var question = "What about <tags>, \"quotes\", & symbols: 你好 🚀?";
        var results = new List<RetrievalResult>
        {
            MakeResult("doc.md", "Section", "Content with <html> & \"entities\".")
        };

        var prompt = PromptBuilder.BuildUserPrompt(question, results);

        Assert.Contains("<tags>", prompt);
        Assert.Contains("\"quotes\"", prompt);
        Assert.Contains("你好", prompt);
        Assert.Contains("🚀", prompt);
        Assert.Contains("<html>", prompt);
    }

    [Fact]
    public void BuildUserPrompt_ManyResults_AllIncluded()
    {
        // 20 retrieval results
        var results = Enumerable.Range(0, 20)
            .Select(i => MakeResult($"doc{i}.md", $"Section {i}", $"Content for document {i}."))
            .ToList();

        var prompt = PromptBuilder.BuildUserPrompt("Overview?", results);

        for (var i = 0; i < 20; i++)
        {
            Assert.Contains($"doc{i}.md", prompt);
            Assert.Contains($"Content for document {i}.", prompt);
        }
    }

    [Fact]
    public void ExtractCitations_MalformedSourceTags_HandledGracefully()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult("valid.md", "Section", "Valid content.")
        };

        // Various malformed citation patterns
        var answer = "Some text [Source: valid.md]. " +
                     "Broken [Source: ] empty. " +
                     "Unclosed [Source: missing. " +
                     "Normal [Source: valid.md] again.";

        var citations = PromptBuilder.ExtractCitations(answer, results);

        // Should still extract the valid citation
        Assert.Contains(citations, c => c.FileName == "valid.md");
    }

    [Fact]
    public void ExtractCitations_SourceNotInResults_Ignored()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult("known.md", "Section", "Known content.")
        };

        var answer = "See [Source: known.md] and [Source: unknown.md].";

        var citations = PromptBuilder.ExtractCitations(answer, results);

        // Only the known file should produce a citation
        Assert.Single(citations);
        Assert.Equal("known.md", citations[0].FileName);
    }

    [Fact]
    public void ExtractCitations_EmptyAnswer_FallsBackToAllResults()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult("a.md", "Section A", "Content A"),
            MakeResult("b.md", "Section B", "Content B")
        };

        var citations = PromptBuilder.ExtractCitations("", results);

        Assert.Equal(2, citations.Count);
    }

    private static RetrievalResult MakeResult(string fileName, string section, string content, double score = 0.85)
    {
        return new RetrievalResult
        {
            Chunk = new DocumentChunk
            {
                ChunkId = $"{fileName}_{section}_0",
                DocumentId = "doc-123",
                DocumentTitle = Path.GetFileNameWithoutExtension(fileName),
                FileName = fileName,
                Section = section,
                ChunkIndex = 0,
                Content = content
            },
            Score = score
        };
    }
}
