namespace RAGNavigator.Tests.Evaluation;

public sealed record RetrievalEvaluationResult
{
    public required string QuestionId { get; init; }
    public required IReadOnlyList<string> RetrievedSourceFiles { get; init; }
    public required IReadOnlyList<string> MatchedSourceFiles { get; init; }
    public required IReadOnlyList<string> MissingSourceFiles { get; init; }
    public required double SourceRecall { get; init; }
    public required double MinimumSourceRecall { get; init; }
    public bool Passed => SourceRecall >= MinimumSourceRecall;
}
