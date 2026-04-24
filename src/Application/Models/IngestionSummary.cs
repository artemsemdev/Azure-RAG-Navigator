namespace RAGNavigator.Application.Models;

public sealed record IngestionSummary
{
    public required int FoldersRequested { get; init; }
    public required int FilesFound { get; init; }
    public required int FilesProcessed { get; init; }
    public required int ChunksIndexed { get; init; }
    public required int FilesFailed { get; init; }
}
