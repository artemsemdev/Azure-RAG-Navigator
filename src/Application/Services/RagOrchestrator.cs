using Microsoft.Extensions.Logging;
using RAGNavigator.Application.Interfaces;
using RAGNavigator.Application.Models;

namespace RAGNavigator.Application.Services;

/// <summary>
/// Orchestrates the full RAG pipeline: embed query → retrieve → build prompt → generate answer.
/// </summary>
public sealed class RagOrchestrator
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IRetrievalService _retrievalService;
    private readonly IChatCompletionService _chatService;
    private readonly ILogger<RagOrchestrator> _logger;

    private const int TopK = 5;
    private const double MinimumRelevanceScore = 0.01;

    public RagOrchestrator(
        IEmbeddingService embeddingService,
        IRetrievalService retrievalService,
        IChatCompletionService chatService,
        ILogger<RagOrchestrator> logger)
    {
        _embeddingService = embeddingService;
        _retrievalService = retrievalService;
        _chatService = chatService;
        _logger = logger;
    }

    public async Task<ChatResponse> AskAsync(
        string question,
        bool includeDebugInfo = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing question: {Question}", question);

        // Step 1: Generate embedding for the query
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(question, cancellationToken);

        // Step 2: Hybrid retrieval (keyword + vector) with semantic re-ranking
        var results = await _retrievalService.SearchAsync(question, queryEmbedding, TopK, cancellationToken);

        // Filter out low-relevance results
        var relevantResults = results
            .Where(r => r.Score >= MinimumRelevanceScore)
            .ToList();

        _logger.LogInformation("Retrieved {Count} relevant chunks (of {Total} total)",
            relevantResults.Count, results.Count);

        // Step 3: Build grounded prompt
        var userPrompt = PromptBuilder.BuildUserPrompt(question, relevantResults);

        // Step 4: Generate answer
        var answer = await _chatService.GenerateAnswerAsync(
            PromptBuilder.SystemPrompt,
            userPrompt,
            cancellationToken);

        // Step 5: Extract citations
        var citations = PromptBuilder.ExtractCitations(answer, relevantResults);

        // Build response
        var response = new ChatResponse
        {
            Answer = answer,
            Citations = citations,
            Debug = includeDebugInfo ? BuildDebugInfo(relevantResults, userPrompt) : null
        };

        return response;
    }

    private static DebugInfo BuildDebugInfo(IReadOnlyList<RetrievalResult> results, string fullPrompt)
    {
        return new DebugInfo
        {
            FullPrompt = fullPrompt,
            RetrievedChunks = results.Select(r => new RetrievedChunkDebug
            {
                ChunkId = r.Chunk.ChunkId,
                FileName = r.Chunk.FileName,
                Section = r.Chunk.Section,
                Score = r.Score,
                RerankerScore = r.RerankerScore,
                Caption = r.Caption,
                ContentPreview = r.Chunk.Content.Length > 300
                    ? r.Chunk.Content[..300] + "..."
                    : r.Chunk.Content
            }).ToList()
        };
    }
}
