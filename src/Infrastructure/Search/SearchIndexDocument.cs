using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace RAGNavigator.Infrastructure.Search;

/// <summary>
/// Maps to the Azure AI Search index schema.
/// Uses attributes to define the index programmatically.
/// </summary>
public sealed class SearchIndexDocument
{
    public const int ContentVectorDimensions = 1536;

    [SimpleField(IsKey = true, IsFilterable = true)]
    public string ChunkId { get; set; } = string.Empty;

    [SimpleField(IsFilterable = true)]
    public string DocumentId { get; set; } = string.Empty;

    [SearchableField]
    public string DocumentTitle { get; set; } = string.Empty;

    [SimpleField(IsFilterable = true, IsFacetable = true)]
    public string FileName { get; set; } = string.Empty;

    [SearchableField]
    public string Section { get; set; } = string.Empty;

    [SimpleField(IsFilterable = true, IsSortable = true)]
    public int ChunkIndex { get; set; }

    [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.EnLucene)]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 1536 dimensions for text-embedding-ada-002 / text-embedding-3-small.
    /// The vector field enables semantic similarity search alongside keyword search.
    /// </summary>
    [VectorSearchField(
        VectorSearchDimensions = ContentVectorDimensions,
        VectorSearchProfileName = "vector-profile")]
    public IReadOnlyList<float>? ContentVector { get; set; }
}
