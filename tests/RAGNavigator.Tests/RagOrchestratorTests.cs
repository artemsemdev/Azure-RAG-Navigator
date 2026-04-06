using Microsoft.Extensions.Logging;
using NSubstitute;
using RAGNavigator.Application.Interfaces;
using RAGNavigator.Application.Models;
using RAGNavigator.Application.Services;
using Xunit;

namespace RAGNavigator.Tests;

public class RagOrchestratorTests
{
    private readonly IEmbeddingService _embeddingService = Substitute.For<IEmbeddingService>();
    private readonly IRetrievalService _retrievalService = Substitute.For<IRetrievalService>();
    private readonly IChatCompletionService _chatService = Substitute.For<IChatCompletionService>();
    private readonly ILogger<RagOrchestrator> _logger = Substitute.For<ILogger<RagOrchestrator>>();
    private readonly RagOrchestrator _orchestrator;

    private static readonly ReadOnlyMemory<float> FakeEmbedding = new float[] { 0.1f, 0.2f, 0.3f };

    public RagOrchestratorTests()
    {
        _orchestrator = new RagOrchestrator(_embeddingService, _retrievalService, _chatService, _logger);
    }

    [Fact]
    public async Task AskAsync_FullPipeline_ReturnsAnswerWithCitations()
    {
        // Arrange
        var question = "How do I deploy the service?";
        var retrievalResults = new List<RetrievalResult>
        {
            MakeResult("runbook.md", "Deployment", "Run kubectl apply to deploy the service.", 0.92),
            MakeResult("guide.md", "CI/CD", "The pipeline triggers on merge to main.", 0.85)
        };

        _embeddingService.GenerateEmbeddingAsync(question, Arg.Any<CancellationToken>())
            .Returns(FakeEmbedding);
        _retrievalService.SearchAsync(question, Arg.Any<ReadOnlyMemory<float>>(), 5, Arg.Any<CancellationToken>())
            .Returns(retrievalResults);
        _chatService.GenerateAnswerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("To deploy, run kubectl apply [Source: runbook.md]. The CI/CD pipeline handles this [Source: guide.md].");

        // Act
        var response = await _orchestrator.AskAsync(question);

        // Assert
        Assert.Contains("kubectl apply", response.Answer);
        Assert.Equal(2, response.Citations.Count);
        Assert.Contains(response.Citations, c => c.FileName == "runbook.md");
        Assert.Contains(response.Citations, c => c.FileName == "guide.md");
        Assert.Null(response.Debug);
    }

    [Fact]
    public async Task AskAsync_WithDebugInfo_IncludesPromptAndChunks()
    {
        // Arrange
        var question = "What is the architecture?";
        var results = new List<RetrievalResult>
        {
            MakeResult("arch.md", "Overview", "Microservices with event-driven communication.", 0.90)
        };

        _embeddingService.GenerateEmbeddingAsync(question, Arg.Any<CancellationToken>())
            .Returns(FakeEmbedding);
        _retrievalService.SearchAsync(question, Arg.Any<ReadOnlyMemory<float>>(), 5, Arg.Any<CancellationToken>())
            .Returns(results);
        _chatService.GenerateAnswerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("The system uses microservices [Source: arch.md].");

        // Act
        var response = await _orchestrator.AskAsync(question, includeDebugInfo: true);

        // Assert
        Assert.NotNull(response.Debug);
        Assert.Single(response.Debug.RetrievedChunks);
        Assert.Equal("arch.md", response.Debug.RetrievedChunks[0].FileName);
        Assert.Contains("architecture", response.Debug.FullPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AskAsync_EmptyCorpus_ReturnsAnswerWithNoCitations()
    {
        // Arrange — retrieval returns nothing (empty corpus)
        var question = "Tell me about the auth system.";

        _embeddingService.GenerateEmbeddingAsync(question, Arg.Any<CancellationToken>())
            .Returns(FakeEmbedding);
        _retrievalService.SearchAsync(question, Arg.Any<ReadOnlyMemory<float>>(), 5, Arg.Any<CancellationToken>())
            .Returns(new List<RetrievalResult>());
        _chatService.GenerateAnswerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("I don't have enough information in the indexed documents to answer this question.");

        // Act
        var response = await _orchestrator.AskAsync(question);

        // Assert
        Assert.Contains("don't have enough information", response.Answer);
        Assert.Empty(response.Citations);
    }

    [Fact]
    public async Task AskAsync_FiltersLowScoreResults()
    {
        // Arrange — one result above threshold, one below (MinimumRelevanceScore = 0.01)
        var question = "What is our SLA?";
        var results = new List<RetrievalResult>
        {
            MakeResult("sla.md", "SLA", "99.9% uptime guarantee.", 0.80),
            MakeResult("noise.md", "Random", "Unrelated content.", 0.005)
        };

        _embeddingService.GenerateEmbeddingAsync(question, Arg.Any<CancellationToken>())
            .Returns(FakeEmbedding);
        _retrievalService.SearchAsync(question, Arg.Any<ReadOnlyMemory<float>>(), 5, Arg.Any<CancellationToken>())
            .Returns(results);
        _chatService.GenerateAnswerAsync(Arg.Any<string>(), Arg.Is<string>(p => !p.Contains("Unrelated content.")), Arg.Any<CancellationToken>())
            .Returns("Our SLA is 99.9% uptime [Source: sla.md].");

        // Act
        var response = await _orchestrator.AskAsync(question);

        // Assert — only the relevant result should produce a citation
        Assert.Contains(response.Citations, c => c.FileName == "sla.md");
        Assert.DoesNotContain(response.Citations, c => c.FileName == "noise.md");
    }

    [Fact]
    public async Task AskAsync_CallsServicesInCorrectOrder()
    {
        // Arrange
        var question = "How do backups work?";
        var callOrder = new List<string>();

        _embeddingService.GenerateEmbeddingAsync(question, Arg.Any<CancellationToken>())
            .Returns(ci => { callOrder.Add("embed"); return FakeEmbedding; });
        _retrievalService.SearchAsync(question, Arg.Any<ReadOnlyMemory<float>>(), 5, Arg.Any<CancellationToken>())
            .Returns(ci => { callOrder.Add("retrieve"); return new List<RetrievalResult>(); });
        _chatService.GenerateAnswerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => { callOrder.Add("chat"); return "No information available."; });

        // Act
        await _orchestrator.AskAsync(question);

        // Assert — pipeline must follow: embed → retrieve → chat
        Assert.Equal(new[] { "embed", "retrieve", "chat" }, callOrder);
    }

    [Fact]
    public async Task AskAsync_OverlyLongQuestion_PassedThroughToServices()
    {
        // Arrange — a very long question (10,000+ chars)
        var question = "What is " + new string('x', 10_000) + "?";

        _embeddingService.GenerateEmbeddingAsync(question, Arg.Any<CancellationToken>())
            .Returns(FakeEmbedding);
        _retrievalService.SearchAsync(question, Arg.Any<ReadOnlyMemory<float>>(), 5, Arg.Any<CancellationToken>())
            .Returns(new List<RetrievalResult>());
        _chatService.GenerateAnswerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("I don't have enough information.");

        // Act — should not throw
        var response = await _orchestrator.AskAsync(question);

        // Assert
        Assert.NotNull(response.Answer);
        await _embeddingService.Received(1).GenerateEmbeddingAsync(question, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AskAsync_CancellationToken_PropagatedToAllServices()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        _embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), token)
            .Returns(FakeEmbedding);
        _retrievalService.SearchAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(), 5, token)
            .Returns(new List<RetrievalResult>());
        _chatService.GenerateAnswerAsync(Arg.Any<string>(), Arg.Any<string>(), token)
            .Returns("Answer.");

        // Act
        await _orchestrator.AskAsync("test", cancellationToken: token);

        // Assert — verify each service received the cancellation token
        await _embeddingService.Received(1).GenerateEmbeddingAsync(Arg.Any<string>(), token);
        await _retrievalService.Received(1).SearchAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(), 5, token);
        await _chatService.Received(1).GenerateAnswerAsync(Arg.Any<string>(), Arg.Any<string>(), token);
    }

    private static RetrievalResult MakeResult(string fileName, string section, string content, double score)
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
