using RAGNavigator.Application.Models;
using Xunit;

namespace RAGNavigator.Tests.Evaluation;

public class RetrievalEvaluatorTests
{
    [Fact]
    public void GoldenQuestionCatalog_AllExpectedSourcesExistInCorpus()
    {
        var repoRoot = FindRepoRoot();
        var corpusFiles = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "sample-data"), "*.*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(repoRoot, "docs", "architecture"), "*.*", SearchOption.AllDirectories))
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var expectedFiles = GoldenQuestionCatalog.All
            .SelectMany(q => q.ExpectedSourceFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.NotEmpty(GoldenQuestionCatalog.All);
        Assert.All(expectedFiles, file => Assert.Contains(file, corpusFiles));
    }

    [Fact]
    public void EvaluateSourceRecall_AllExpectedSourcesInTopK_Passes()
    {
        var question = new GoldenQuestion
        {
            Id = "multi-source",
            Question = "What supports this answer?",
            ExpectedSourceFiles = ["a.md", "b.md"],
            TopK = 3,
            MinimumSourceRecall = 1.0
        };

        var results = new List<RetrievalResult>
        {
            MakeResult("A.md", 0.95),
            MakeResult("noise.md", 0.90),
            MakeResult("b.md", 0.85)
        };

        var evaluation = RetrievalEvaluator.EvaluateSourceRecall(question, results);

        Assert.True(evaluation.Passed);
        Assert.Equal(1.0, evaluation.SourceRecall);
        Assert.Equal(["a.md", "b.md"], evaluation.MatchedSourceFiles);
        Assert.Empty(evaluation.MissingSourceFiles);
    }

    [Fact]
    public void EvaluateSourceRecall_MissingExpectedSource_FailsWhenBelowThreshold()
    {
        var question = new GoldenQuestion
        {
            Id = "missing-source",
            Question = "What supports this answer?",
            ExpectedSourceFiles = ["a.md", "b.md"],
            TopK = 3,
            MinimumSourceRecall = 1.0
        };

        var results = new List<RetrievalResult>
        {
            MakeResult("a.md", 0.95),
            MakeResult("noise.md", 0.90)
        };

        var evaluation = RetrievalEvaluator.EvaluateSourceRecall(question, results);

        Assert.False(evaluation.Passed);
        Assert.Equal(0.5, evaluation.SourceRecall);
        Assert.Equal(["a.md"], evaluation.MatchedSourceFiles);
        Assert.Equal(["b.md"], evaluation.MissingSourceFiles);
    }

    [Fact]
    public void EvaluateSourceRecall_OnlyCountsResultsInsideTopK()
    {
        var question = new GoldenQuestion
        {
            Id = "top-k",
            Question = "What supports this answer?",
            ExpectedSourceFiles = ["target.md"],
            TopK = 2
        };

        var results = new List<RetrievalResult>
        {
            MakeResult("noise-1.md", 0.99),
            MakeResult("noise-2.md", 0.98),
            MakeResult("target.md", 0.97)
        };

        var evaluation = RetrievalEvaluator.EvaluateSourceRecall(question, results);

        Assert.False(evaluation.Passed);
        Assert.Equal(0.0, evaluation.SourceRecall);
        Assert.Equal(["target.md"], evaluation.MissingSourceFiles);
    }

    private static RetrievalResult MakeResult(string fileName, double score) =>
        new()
        {
            Chunk = new DocumentChunk
            {
                ChunkId = $"{fileName}-chunk-0",
                DocumentId = fileName,
                DocumentTitle = Path.GetFileNameWithoutExtension(fileName),
                FileName = fileName,
                Section = "Test",
                ChunkIndex = 0,
                Content = "Test content"
            },
            Score = score
        };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RAGNavigator.sln")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
