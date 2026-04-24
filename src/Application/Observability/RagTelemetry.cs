using System.Diagnostics;
using System.Diagnostics.Metrics;
using RAGNavigator.Application.Models;

namespace RAGNavigator.Application.Observability;

public static class RagTelemetry
{
    public const string ActivitySourceName = "RAGNavigator";
    public const string MeterName = "RAGNavigator";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Counter<long> QueryCount =
        Meter.CreateCounter<long>("ragnavigator.query.count");

    private static readonly Counter<long> QueryErrorCount =
        Meter.CreateCounter<long>("ragnavigator.query.error_count");

    private static readonly Counter<long> NoInfoCount =
        Meter.CreateCounter<long>("ragnavigator.answer.no_info_count");

    private static readonly Histogram<double> QueryDuration =
        Meter.CreateHistogram<double>("ragnavigator.query.duration", "ms");

    private static readonly Histogram<double> EmbeddingDuration =
        Meter.CreateHistogram<double>("ragnavigator.embedding.duration", "ms");

    private static readonly Histogram<double> SearchDuration =
        Meter.CreateHistogram<double>("ragnavigator.search.duration", "ms");

    private static readonly Histogram<double> LlmDuration =
        Meter.CreateHistogram<double>("ragnavigator.llm.duration", "ms");

    private static readonly Histogram<long> RetrievedChunks =
        Meter.CreateHistogram<long>("ragnavigator.retrieval.chunks_returned");

    private static readonly Histogram<long> RelevantChunks =
        Meter.CreateHistogram<long>("ragnavigator.retrieval.chunks_relevant");

    private static readonly Histogram<long> CitationsCount =
        Meter.CreateHistogram<long>("ragnavigator.citations.count");

    private static readonly Histogram<double> IndexDuration =
        Meter.CreateHistogram<double>("ragnavigator.index.duration", "ms");

    private static readonly Counter<long> IndexedChunks =
        Meter.CreateCounter<long>("ragnavigator.index.chunks_indexed");

    private static readonly Counter<long> IndexedFiles =
        Meter.CreateCounter<long>("ragnavigator.index.files_processed");

    private static readonly Counter<long> IndexErrorCount =
        Meter.CreateCounter<long>("ragnavigator.index.error_count");

    public static Activity? StartActivity(string name)
    {
        return ActivitySource.StartActivity(name);
    }

    public static void RecordQuery(
        double totalDurationMs,
        double embeddingDurationMs,
        double searchDurationMs,
        double llmDurationMs,
        int retrievedChunks,
        int relevantChunks,
        int citationsCount,
        bool noContext,
        bool includeDebugInfo)
    {
        var tags = new TagList
        {
            { "rag.success", true },
            { "rag.no_context", noContext },
            { "rag.debug", includeDebugInfo }
        };

        QueryCount.Add(1, tags);
        QueryDuration.Record(totalDurationMs, tags);
        EmbeddingDuration.Record(embeddingDurationMs, tags);
        SearchDuration.Record(searchDurationMs, tags);
        LlmDuration.Record(llmDurationMs, tags);
        RetrievedChunks.Record(retrievedChunks, tags);
        RelevantChunks.Record(relevantChunks, tags);
        CitationsCount.Record(citationsCount, tags);

        if (noContext)
            NoInfoCount.Add(1, tags);
    }

    public static void RecordQueryError(double totalDurationMs, string exceptionType)
    {
        var tags = new TagList
        {
            { "rag.success", false },
            { "error.type", exceptionType }
        };

        QueryErrorCount.Add(1, tags);
        QueryDuration.Record(totalDurationMs, tags);
    }

    public static void RecordIngestion(IngestionSummary summary, double totalDurationMs)
    {
        var tags = new TagList
        {
            { "rag.success", true },
            { "rag.files_failed", summary.FilesFailed }
        };

        IndexDuration.Record(totalDurationMs, tags);
        IndexedChunks.Add(summary.ChunksIndexed, tags);
        IndexedFiles.Add(summary.FilesProcessed, tags);
    }

    public static void RecordIngestionError(double totalDurationMs, string exceptionType)
    {
        var tags = new TagList
        {
            { "rag.success", false },
            { "error.type", exceptionType }
        };

        IndexErrorCount.Add(1, tags);
        IndexDuration.Record(totalDurationMs, tags);
    }
}
