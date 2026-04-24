using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RAGNavigator.Application.Interfaces;
using RAGNavigator.Application.Services;
using RAGNavigator.Infrastructure;
using Xunit;

namespace RAGNavigator.Tests.Evaluation;

public class LiveRetrievalEvaluationTests
{
    private const string RunFlag = "RAG_NAVIGATOR_RUN_LIVE_RETRIEVAL_EVAL";
    private const string EvalIndexNameVariable = "RAG_EVAL_SEARCH_INDEX_NAME";
    private const string EvalIndexPrefix = "rag-navigator-eval";

    private readonly ITestOutputHelper _output;

    public LiveRetrievalEvaluationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GoldenQuestions_MeetSourceRecallThreshold_WhenLiveEvalEnabled()
    {
        if (!LiveEvalEnabled())
        {
            _output.WriteLine($"Live retrieval evaluation skipped. Set {RunFlag}=1 to enable.");
            return;
        }

        var evalIndexName = GetRequiredEvalIndexName();
        var configuration = BuildConfiguration(evalIndexName);
        var serviceProvider = BuildServiceProvider(configuration);

        var indexService = serviceProvider.GetRequiredService<ISearchIndexService>();

        try
        {
            await ReindexCorpusAsync(serviceProvider);

            var embeddingService = serviceProvider.GetRequiredService<IEmbeddingService>();
            var retrievalService = serviceProvider.GetRequiredService<IRetrievalService>();
            var failures = new List<string>();

            foreach (var goldenQuestion in GoldenQuestionCatalog.All)
            {
                var embedding = await embeddingService.GenerateEmbeddingAsync(goldenQuestion.Question);
                var results = await retrievalService.SearchAsync(
                    goldenQuestion.Question,
                    embedding,
                    goldenQuestion.TopK);

                var evaluation = RetrievalEvaluator.EvaluateSourceRecall(goldenQuestion, results);
                _output.WriteLine(
                    $"{evaluation.QuestionId}: source_recall@{goldenQuestion.TopK}={evaluation.SourceRecall:0.00}; " +
                    $"matched=[{string.Join(", ", evaluation.MatchedSourceFiles)}]; " +
                    $"missing=[{string.Join(", ", evaluation.MissingSourceFiles)}]");

                if (!evaluation.Passed)
                {
                    failures.Add(
                        $"{evaluation.QuestionId}: expected recall >= {evaluation.MinimumSourceRecall:0.00}, " +
                        $"actual {evaluation.SourceRecall:0.00}; missing [{string.Join(", ", evaluation.MissingSourceFiles)}]");
                }
            }

            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }
        finally
        {
            if (!KeepEvalIndex())
                await indexService.DeleteAllDocumentsAsync();
        }
    }

    private static ServiceProvider BuildServiceProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddRAGNavigatorServices(configuration);

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static IConfiguration BuildConfiguration(string evalIndexName)
    {
        var values = new Dictionary<string, string?>
        {
            ["AzureOpenAI:Endpoint"] = RequiredEnv("AZURE_OPENAI_ENDPOINT"),
            ["AzureOpenAI:ChatDeployment"] = OptionalEnv("AZURE_OPENAI_CHAT_DEPLOYMENT", "gpt-4o"),
            ["AzureOpenAI:EmbeddingDeployment"] = RequiredEnv("AZURE_OPENAI_EMBEDDING_DEPLOYMENT"),
            ["AzureOpenAI:ApiKey"] = OptionalEnv("AZURE_OPENAI_API_KEY", string.Empty),
            ["AzureSearch:Endpoint"] = RequiredEnv("AZURE_SEARCH_ENDPOINT"),
            ["AzureSearch:IndexName"] = evalIndexName,
            ["AzureSearch:ApiKey"] = OptionalEnv("AZURE_SEARCH_API_KEY", string.Empty),
            ["Rag:TopK"] = OptionalEnv("RAG_TOP_K", "5"),
            ["Rag:MinimumRelevanceScore"] = OptionalEnv("RAG_MINIMUM_RELEVANCE_SCORE", "0.01")
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static async Task ReindexCorpusAsync(IServiceProvider serviceProvider)
    {
        var repoRoot = FindRepoRoot();
        var folders = new[]
        {
            Path.Combine(repoRoot, "sample-data"),
            Path.Combine(repoRoot, "docs", "architecture")
        };

        var processor = serviceProvider.GetRequiredService<DocumentProcessor>();
        await processor.IngestDocumentsAsync(folders);
    }

    private static string GetRequiredEvalIndexName()
    {
        var indexName = Environment.GetEnvironmentVariable(EvalIndexNameVariable);
        if (string.IsNullOrWhiteSpace(indexName))
        {
            throw new InvalidOperationException(
                $"{EvalIndexNameVariable} must be set when {RunFlag}=1. " +
                $"Use a dedicated evaluation index, for example '{EvalIndexPrefix}-dev'.");
        }

        if (!indexName.StartsWith(EvalIndexPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{EvalIndexNameVariable} must start with '{EvalIndexPrefix}' to avoid deleting a non-evaluation index.");
        }

        return indexName;
    }

    private static bool LiveEvalEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable(RunFlag), "1", StringComparison.Ordinal);

    private static bool KeepEvalIndex() =>
        string.Equals(Environment.GetEnvironmentVariable("RAG_EVAL_KEEP_INDEX"), "1", StringComparison.Ordinal);

    private static string RequiredEnv(string name) =>
        Environment.GetEnvironmentVariable(name) ??
        throw new InvalidOperationException($"{name} must be set when {RunFlag}=1.");

    private static string OptionalEnv(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) ?? fallback;

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
