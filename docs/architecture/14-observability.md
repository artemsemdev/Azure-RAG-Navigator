# Observability

## Current Implementation

### Structured Logging

The application uses `Microsoft.Extensions.Logging.ILogger` with structured log messages throughout the pipeline:

| Component | Log Examples |
|-----------|-------------|
| `DocumentProcessor` | File count, chunk count per file, embedding batch progress, total indexed |
| `RagOrchestrator` | Question received, relevant chunk count, query pipeline completion |
| `AzureOpenAIEmbeddingService` | Batch sizes |
| `AzureOpenAIChatService` | Prompt length, response length, empty content warnings |
| `AzureSearchIndexService` | Index creation, document upload/delete counts, failures |
| `AzureSearchRetrievalService` | Query execution, result count |

All log messages use structured parameters (not string interpolation) for machine-parseable output:

```csharp
_logger.LogInformation("Produced {ChunkCount} chunks from {FileName}", chunks.Count, fileName);
```

### Debug Mode

The UI exposes a debug panel that shows:
- Retrieved chunks with chunk IDs, file names, and sections
- Relevance scores for each chunk
- The full prompt sent to the LLM

This is useful for developers and reviewers to understand and evaluate retrieval quality.

## Production Observability Stack

### Recommended Architecture

```
Application → Application Insights SDK → Log Analytics Workspace → Grafana / Azure Dashboards
                                                                  → Azure Monitor Alerts
```

### Integration Path

1. Add `Microsoft.ApplicationInsights.AspNetCore` package.
2. Call `builder.Services.AddApplicationInsightsTelemetry()` in `Program.cs`.
3. Configure the connection string via environment variable: `APPLICATIONINSIGHTS_CONNECTION_STRING`.

This enables automatic collection of:
- HTTP request telemetry (duration, status codes)
- Dependency calls (Azure OpenAI, Azure AI Search)
- Exceptions
- W3C distributed traces

The Terraform deployment also supports platform diagnostic settings. When `log_analytics_workspace_id` is provided, Azure OpenAI, Azure AI Search, and App Service diagnostics are sent to the configured Log Analytics workspace.

## Metrics to Track

### Request Metrics

| Metric | Type | Description |
|--------|------|-------------|
| `ragnavigator.query.duration` | Histogram | End-to-end query latency |
| `ragnavigator.query.count` | Counter | Total queries processed |
| `ragnavigator.query.error_count` | Counter | Failed queries |
| `ragnavigator.embedding.duration` | Histogram | Embedding generation latency |
| `ragnavigator.search.duration` | Histogram | Hybrid search latency |
| `ragnavigator.llm.duration` | Histogram | LLM completion latency |
| `ragnavigator.llm.tokens` | Counter | Tokens consumed (prompt + completion) |

### Indexing Metrics

| Metric | Type | Description |
|--------|------|-------------|
| `ragnavigator.index.duration` | Histogram | Full reindex duration |
| `ragnavigator.index.chunks_total` | Gauge | Total chunks in the index |
| `ragnavigator.index.documents_total` | Gauge | Total documents indexed |
| `ragnavigator.index.errors` | Counter | Indexing failures |

### Quality Metrics

| Metric | Type | Description |
|--------|------|-------------|
| `ragnavigator.retrieval.chunks_returned` | Histogram | Chunks returned per query |
| `ragnavigator.retrieval.max_score` | Histogram | Highest relevance score per query |
| `ragnavigator.citations.count` | Histogram | Citations per answer |
| `ragnavigator.answer.no_info_rate` | Counter | Queries where the LLM said "not enough information" |

## Distributed Tracing

### Trace Correlation

Each query creates a logical trace spanning:

```
[Trace: query-abc123]
├── [Span: EmbedQuery]         100ms    Azure OpenAI
├── [Span: HybridSearch]        80ms    Azure AI Search
├── [Span: BuildPrompt]          1ms    In-process
├── [Span: GenerateAnswer]    2500ms    Azure OpenAI
└── [Span: ExtractCitations]     1ms    In-process
```

With Application Insights, the `Activity` API in .NET automatically propagates trace context through HTTP calls to Azure services.

### Request Correlation

The `CancellationToken` passed through all async methods already supports request-level lifecycle management. Adding trace IDs would enable:
- Correlating a slow query to a specific Azure OpenAI call.
- Identifying which search queries produce poor results.
- Debugging end-to-end latency for specific users.

## Alert Ideas

| Alert | Condition | Severity | Response |
|-------|-----------|----------|----------|
| High error rate | 5xx rate > 10% for 5 min | P2 | Check Azure service health |
| Slow queries | P95 latency > 10s for 10 min | P3 | Check Azure OpenAI throttling |
| LLM empty responses | > 5 empty content responses in 1 hour | P3 | Check content filter configuration |
| Index empty | Document count = 0 after reindex | P2 | Investigate reindex failure |
| Embedding failures | > 3 failures in 5 min | P2 | Check Azure OpenAI availability |
| High "no info" rate | > 50% of queries produce "not enough information" | P3 | Review corpus coverage |

## Example Telemetry Events

### Successful Query

```json
{
  "timestamp": "2024-03-15T14:30:00.123Z",
  "level": "Information",
  "message": "Query completed",
  "properties": {
    "questionLength": 28,
    "questionFingerprint": "8b6d2a9c4f10",
    "embeddingDurationMs": 120,
    "searchDurationMs": 85,
    "llmDurationMs": 2400,
    "totalDurationMs": 2610,
    "chunksRetrieved": 5,
    "chunksRelevant": 4,
    "citationsProduced": 2,
    "traceId": "abc123def456"
  }
}
```

### Failed Indexing

```json
{
  "timestamp": "2024-03-15T15:00:00.456Z",
  "level": "Error",
  "message": "Embedding generation failed for batch",
  "properties": {
    "batchStart": 17,
    "batchEnd": 32,
    "error": "Azure OpenAI rate limit exceeded",
    "retryAfterSeconds": 30,
    "traceId": "xyz789"
  }
}
```

## What Is Not Logged

Following security best practices:
- User questions are not logged raw in standard application logs; query processing logs use length and a short SHA-256 fingerprint.
- Full LLM prompts are available only in debug mode, not in standard logs.
- API keys and credentials are never logged.
- Document content beyond metadata is not logged.
