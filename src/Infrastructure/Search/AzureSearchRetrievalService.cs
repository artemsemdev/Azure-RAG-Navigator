using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using RAGNavigator.Application.Interfaces;
using RAGNavigator.Application.Models;

namespace RAGNavigator.Infrastructure.Search;

/// <summary>
/// Performs hybrid retrieval (keyword + vector) against Azure AI Search.
///
/// Hybrid search combines:
/// - BM25 keyword search on the Content field for exact term matching
/// - Vector similarity search on the ContentVector field for semantic matching
///
/// Azure AI Search uses Reciprocal Rank Fusion (RRF) to merge results from both
/// retrieval methods. Semantic ranking (L2) then re-scores the top results using
/// a deep learning model for improved relevance.
/// </summary>
public sealed class AzureSearchRetrievalService : IRetrievalService
{
    private readonly SearchClient _searchClient;
    private readonly ILogger<AzureSearchRetrievalService> _logger;

    public AzureSearchRetrievalService(
        SearchClient searchClient,
        ILogger<AzureSearchRetrievalService> logger)
    {
        _searchClient = searchClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        string query,
        ReadOnlyMemory<float> queryEmbedding,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Running hybrid search for: {Query}", query);

        var searchOptions = new SearchOptions
        {
            Size = topK,
            Select =
            {
                "ChunkId", "DocumentId", "DocumentTitle",
                "FileName", "Section", "ChunkIndex", "Content"
            },
            QueryType = SearchQueryType.Semantic,
            SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = "semantic-config",
                QueryCaption = new QueryCaption(QueryCaptionType.Extractive)
            }
        };

        // Add vector query for semantic similarity
        var vectorQuery = new VectorizedQuery(queryEmbedding)
        {
            KNearestNeighborsCount = topK,
        };
        vectorQuery.Fields.Add("ContentVector");
        searchOptions.VectorSearch = new VectorSearchOptions();
        searchOptions.VectorSearch.Queries.Add(vectorQuery);

        // Execute hybrid search (keyword text in SearchText + vector query)
        var response = await _searchClient.SearchAsync<SearchIndexDocument>(
            query, searchOptions, cancellationToken);

        var results = new List<RetrievalResult>();

        await foreach (var result in response.Value.GetResultsAsync())
        {
            var captions = result.SemanticSearch?.Captions;
            var caption = captions is { Count: > 0 } ? captions[0] : null;

            results.Add(new RetrievalResult
            {
                Chunk = MapToChunk(result.Document),
                Score = result.Score ?? 0,
                RerankerScore = result.SemanticSearch?.RerankerScore ?? 0,
                Caption = caption?.Text
            });
        }

        _logger.LogDebug("Hybrid search returned {Count} results", results.Count);
        return results;
    }

    private static DocumentChunk MapToChunk(SearchIndexDocument doc)
    {
        return new DocumentChunk
        {
            ChunkId = doc.ChunkId,
            DocumentId = doc.DocumentId,
            DocumentTitle = doc.DocumentTitle,
            FileName = doc.FileName,
            Section = doc.Section,
            ChunkIndex = doc.ChunkIndex,
            Content = doc.Content
        };
    }
}
