namespace RAGNavigator.Tests.Evaluation;

public sealed record GoldenQuestion
{
    public required string Id { get; init; }
    public required string Question { get; init; }
    public required IReadOnlyList<string> ExpectedSourceFiles { get; init; }
    public int TopK { get; init; } = 5;
    public double MinimumSourceRecall { get; init; } = 1.0;
}
