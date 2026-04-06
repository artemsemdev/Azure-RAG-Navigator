using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RAGNavigator.Application.Interfaces;
using RAGNavigator.Application.Models;
using RAGNavigator.Infrastructure.Configuration;

namespace RAGNavigator.Infrastructure.Search;

public sealed class AzureSearchIndexService : ISearchIndexService
{
    private readonly SearchIndexClient _indexClient;
    private readonly SearchClient _searchClient;
    private readonly AzureSearchOptions _options;
    private readonly ILogger<AzureSearchIndexService> _logger;

    public AzureSearchIndexService(
        SearchIndexClient indexClient,
        SearchClient searchClient,
        IOptions<AzureSearchOptions> options,
        ILogger<AzureSearchIndexService> logger)
    {
        _indexClient = indexClient;
        _searchClient = searchClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task CreateOrUpdateIndexAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating or updating search index: {IndexName}", _options.IndexName);

        var fieldBuilder = new FieldBuilder();
        var fields = fieldBuilder.Build(typeof(SearchIndexDocument));

        var vectorSearch = new VectorSearch();
        vectorSearch.Algorithms.Add(new HnswAlgorithmConfiguration("hnsw-config")
        {
            Parameters = new HnswParameters
            {
                Metric = VectorSearchAlgorithmMetric.Cosine,
                M = 4,
                EfConstruction = 400,
                EfSearch = 500
            }
        });
        vectorSearch.Profiles.Add(new VectorSearchProfile("vector-profile", "hnsw-config"));

        var semanticConfig = new SemanticConfiguration("semantic-config", new SemanticPrioritizedFields
        {
            TitleField = new SemanticField("DocumentTitle"),
            ContentFields = { new SemanticField("Content") },
            KeywordsFields = { new SemanticField("Section") }
        });

        var semanticSearch = new SemanticSearch
        {
            DefaultConfigurationName = "semantic-config",
            Configurations = { semanticConfig }
        };

        var index = new SearchIndex(_options.IndexName, fields)
        {
            VectorSearch = vectorSearch,
            SemanticSearch = semanticSearch
        };

        await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: cancellationToken);
        _logger.LogInformation("Search index '{IndexName}' is ready", _options.IndexName);
    }

    public async Task UploadChunksAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        var documents = chunks.Select(MapToIndexDocument).ToList();

        // Upload in batches of 100 (Azure AI Search limit is 1000 per batch)
        const int batchSize = 100;
        for (var i = 0; i < documents.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = documents.Skip(i).Take(batchSize).ToList();
            var response = await _searchClient.UploadDocumentsAsync(batch, cancellationToken: cancellationToken);

            var failed = response.Value.Results.Count(r => !r.Succeeded);
            if (failed > 0)
                _logger.LogWarning("{FailedCount} documents failed to upload in batch starting at {Index}", failed, i);

            _logger.LogDebug("Uploaded batch {Start}-{End} of {Total}",
                i + 1, Math.Min(i + batchSize, documents.Count), documents.Count);
        }
    }

    public async Task DeleteAllDocumentsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting index '{IndexName}' to clear all documents", _options.IndexName);

        try
        {
            // Delete and recreate the index. This is simpler and more reliable than
            // paginating through all documents, especially beyond the 1000-result limit.
            await _indexClient.DeleteIndexAsync(_options.IndexName, cancellationToken);
            _logger.LogInformation("Index '{IndexName}' deleted", _options.IndexName);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Index does not exist yet, nothing to delete");
        }
    }

    public async Task<IReadOnlyList<SourceDocument>> GetIndexedDocumentsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new SearchOptions
            {
                Select = { "DocumentId", "FileName", "DocumentTitle" },
                Size = 1000
            };

            var response = await _searchClient.SearchAsync<SearchIndexDocument>("*", options, cancellationToken);
            var documentMap = new Dictionary<string, (string FileName, string Title, int Count)>();

            await foreach (var result in response.Value.GetResultsAsync())
            {
                var doc = result.Document;
                if (documentMap.TryGetValue(doc.DocumentId, out var existing))
                {
                    documentMap[doc.DocumentId] = (existing.FileName, existing.Title, existing.Count + 1);
                }
                else
                {
                    documentMap[doc.DocumentId] = (doc.FileName, doc.DocumentTitle, 1);
                }
            }

            return documentMap.Select(kvp => new SourceDocument
            {
                DocumentId = kvp.Key,
                FileName = kvp.Value.FileName,
                Title = kvp.Value.Title,
                ChunkCount = kvp.Value.Count
            }).OrderBy(d => d.FileName).ToList();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return [];
        }
    }

    private static SearchIndexDocument MapToIndexDocument(DocumentChunk chunk)
    {
        return new SearchIndexDocument
        {
            ChunkId = chunk.ChunkId,
            DocumentId = chunk.DocumentId,
            DocumentTitle = chunk.DocumentTitle,
            FileName = chunk.FileName,
            Section = chunk.Section,
            ChunkIndex = chunk.ChunkIndex,
            Content = chunk.Content,
            ContentVector = chunk.Embedding?.ToArray()
        };
    }
}
