using RAGNavigator.Application.Models;
using RAGNavigator.Application.Services;
using Xunit;

namespace RAGNavigator.Tests;

public class PromptBuilderTests
{
    [Fact]
    public void BuildUserPrompt_IncludesQuestionAndContext()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult("doc.md", "Section A", "Content about topic A."),
            MakeResult("other.md", "Section B", "Content about topic B.")
        };

        var prompt = PromptBuilder.BuildUserPrompt("What is topic A?", results);

        Assert.Contains("What is topic A?", prompt);
        Assert.Contains("Content about topic A.", prompt);
        Assert.Contains("Content about topic B.", prompt);
        Assert.Contains("doc.md", prompt);
        Assert.Contains("other.md", prompt);
        Assert.Contains("[S1] doc.md", prompt);
        Assert.Contains("[S2] other.md", prompt);
    }

    [Fact]
    public void BuildUserPrompt_EmptyResults_StillIncludesQuestion()
    {
        var prompt = PromptBuilder.BuildUserPrompt("What is nothing?", []);

        Assert.Contains("What is nothing?", prompt);
        Assert.Contains("Retrieved Context", prompt);
    }

    [Fact]
    public void BuildUserPrompt_IncludesSectionInfo()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult("guide.md", "Getting Started", "Follow these steps to begin.")
        };

        var prompt = PromptBuilder.BuildUserPrompt("How do I start?", results);

        Assert.Contains("Section: Getting Started", prompt);
    }

    [Fact]
    public void ExtractCitations_ParsesSourceReferences()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult("runbook.md", "Failover", "Steps for database failover."),
            MakeResult("adr.md", "Decision", "We chose event-driven architecture.")
        };

        var answer = "According to [S1], you should follow the failover steps. " +
                     "The architecture is based on [S2].";

        var citations = PromptBuilder.ExtractCitations(answer, results);

        Assert.Equal(2, citations.Count);
        Assert.Contains(citations, c => c.SourceId == "S1" && c.FileName == "runbook.md");
        Assert.Contains(citations, c => c.SourceId == "S2" && c.FileName == "adr.md");
    }

    [Fact]
    public void ExtractCitations_DeduplicatesRepeatedSources()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult("doc.md", "Section", "Some content here.")
        };

        var answer = "[S1] first mention. [S1] second mention.";

        var citations = PromptBuilder.ExtractCitations(answer, results);

        Assert.Single(citations);
        Assert.Equal("S1", citations[0].SourceId);
    }

    [Fact]
    public void ExtractCitations_NoCitationsInAnswer_FallsBackToAllResults()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult("a.md", "Section A", "Content A"),
            MakeResult("b.md", "Section B", "Content B")
        };

        var answer = "The answer is based on the provided documents.";

        var citations = PromptBuilder.ExtractCitations(answer, results);

        Assert.Equal(2, citations.Count);
        Assert.Equal(["S1", "S2"], citations.Select(c => c.SourceId).ToArray());
    }

    [Fact]
    public void ExtractCitations_IncludesSnippetAndSection()
    {
        var results = new List<RetrievalResult>
        {
            MakeResult("guide.md", "API Design", "Use plural nouns for REST resources.")
        };

        var answer = "As stated in [S1], use plural nouns.";

        var citations = PromptBuilder.ExtractCitations(answer, results);

        Assert.Single(citations);
        Assert.Equal("S1", citations[0].SourceId);
        Assert.Equal("API Design", citations[0].Section);
        Assert.Contains("plural nouns", citations[0].Snippet);
    }

    [Fact]
    public void SystemPrompt_ContainsKeyInstructions()
    {
        Assert.Contains("ONLY the provided context", PromptBuilder.SystemPrompt);
        Assert.Contains("[S1]", PromptBuilder.SystemPrompt);
        Assert.Contains("don't have enough information", PromptBuilder.SystemPrompt);
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
