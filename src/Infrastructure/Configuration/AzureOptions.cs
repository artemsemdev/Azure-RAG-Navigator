using System.ComponentModel.DataAnnotations;

namespace RAGNavigator.Infrastructure.Configuration;

public sealed class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    [Required]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public string ChatDeployment { get; set; } = string.Empty;

    [Required]
    public string EmbeddingDeployment { get; set; } = string.Empty;

    /// <summary>
    /// Must match the Azure AI Search vector field dimensions.
    /// text-embedding-ada-002 and text-embedding-3-small use 1536 dimensions.
    /// </summary>
    [Range(1, 4096)]
    public int EmbeddingDimensions { get; set; } = 1536;

    /// <summary>
    /// Optional API key for local development. When empty, DefaultAzureCredential is used.
    /// In production, prefer managed identity — no key needed.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class AzureSearchOptions
{
    public const string SectionName = "AzureSearch";

    [Required]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public string IndexName { get; set; } = "rag-navigator-index";

    /// <summary>
    /// Optional API key for local development. When empty, DefaultAzureCredential is used.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
