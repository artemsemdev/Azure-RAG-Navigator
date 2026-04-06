namespace RAGNavigator.Application.Models;

/// <summary>
/// The final response returned to the user, including answer, citations, and optional debug info.
/// </summary>
public sealed class ChatResponse
{
    public required string Answer { get; init; }
    public required IReadOnlyList<Citation> Citations { get; init; }
    public DebugInfo? Debug { get; init; }
}

public sealed class Citation
{
    public required string FileName { get; init; }
    public required string DocumentTitle { get; init; }
    public required string Section { get; init; }
    public required string Snippet { get; init; }
}

public sealed class DebugInfo
{
    public required IReadOnlyList<RetrievedChunkDebug> RetrievedChunks { get; init; }
    public required string FullPrompt { get; init; }
}

public sealed class RetrievedChunkDebug
{
    public required string ChunkId { get; init; }
    public required string FileName { get; init; }
    public required string Section { get; init; }
    public required double Score { get; init; }
    public double RerankerScore { get; init; }
    public string? Caption { get; init; }
    public required string ContentPreview { get; init; }
}
