namespace RAGNavigator.Application.Models;

/// <summary>
/// A scored chunk returned from the search index during retrieval.
/// </summary>
public sealed class RetrievalResult
{
    public required DocumentChunk Chunk { get; init; }
    public required double Score { get; init; }
    public double RerankerScore { get; init; }
    public string? Caption { get; init; }
}
