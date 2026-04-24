using RAGNavigator.Application.Models;

namespace RAGNavigator.Tests.Evaluation;

public static class RetrievalEvaluator
{
    public static RetrievalEvaluationResult EvaluateSourceRecall(
        GoldenQuestion goldenQuestion,
        IReadOnlyList<RetrievalResult> retrievalResults)
    {
        if (goldenQuestion.ExpectedSourceFiles.Count == 0)
            throw new ArgumentException("At least one expected source file is required.", nameof(goldenQuestion));

        if (goldenQuestion.TopK <= 0)
            throw new ArgumentOutOfRangeException(nameof(goldenQuestion), "TopK must be positive.");

        var expected = new HashSet<string>(
            goldenQuestion.ExpectedSourceFiles.Select(NormalizeFileName),
            StringComparer.OrdinalIgnoreCase);

        var retrieved = retrievalResults
            .Take(goldenQuestion.TopK)
            .Select(r => NormalizeFileName(r.Chunk.FileName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var matched = expected
            .Where(e => retrieved.Contains(e, StringComparer.OrdinalIgnoreCase))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var missing = expected
            .Where(e => !retrieved.Contains(e, StringComparer.OrdinalIgnoreCase))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RetrievalEvaluationResult
        {
            QuestionId = goldenQuestion.Id,
            RetrievedSourceFiles = retrieved,
            MatchedSourceFiles = matched,
            MissingSourceFiles = missing,
            SourceRecall = matched.Count / (double)expected.Count,
            MinimumSourceRecall = goldenQuestion.MinimumSourceRecall
        };
    }

    private static string NormalizeFileName(string fileName) =>
        Path.GetFileName(fileName).Trim();
}
