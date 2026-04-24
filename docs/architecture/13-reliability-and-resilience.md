# Reliability and Resilience

## Overview

This document describes the reliability characteristics of RAG Navigator, distinguishing between what the demo implements and what a production deployment would require.

## Current Implementation

### Retry Behavior

The Azure SDKs (`Azure.AI.OpenAI`, `Azure.Search.Documents`) include built-in retry policies:
- Default retry count: 3
- Exponential backoff with jitter
- Retries on transient HTTP errors (408, 429, 500, 502, 503, 504)

The application does not add additional retry logic on top of the SDK defaults. For the demo scope, the SDK's built-in policies are sufficient.

### Timeout Behavior

- Azure SDK clients use default HTTP timeouts (typically 100 seconds).
- ASP.NET Core request timeout defaults apply.
- No custom timeout configuration is implemented.

**Production improvement:** Configure explicit timeouts per operation:

| Operation | Recommended Timeout |
|-----------|-------------------|
| Embedding generation | 30 seconds |
| Search query | 15 seconds |
| Chat completion | 60 seconds |
| Index upload | 120 seconds |

### Graceful Degradation

| Failure | Current Behavior | Production Improvement |
|---------|-----------------|----------------------|
| Azure OpenAI unavailable | HTTP 500 returned, UI shows error | Retry with backoff, show cached answer if available |
| Azure AI Search unavailable | HTTP 500 returned, UI shows error | Return "search unavailable" with suggestion to retry |
| LLM returns empty content | Graceful fallback message | Same, plus alert to operations team |
| No relevant search results | Deterministic "not enough information" response without LLM call | Same, plus suggest alternative queries |
| File read error during ingestion | Exception stops ingestion | Skip file, log error, continue with remaining files |

### Failure Handling: Indexing

The current ingestion pipeline is all-or-nothing:
1. If any file fails to read, the entire ingestion stops.
2. If embedding generation fails mid-batch, remaining chunks are not processed.
3. If index upload fails, the index may be in a partially updated state.

**Production improvements:**
- Process files independently — skip failures, log warnings, continue.
- Implement per-batch retry for embedding generation.
- Use a transactional indexing approach (blue-green indexes) to avoid partial state.
- Emit a summary report at the end: files processed, files skipped, errors encountered.

### Failure Handling: Querying

The query pipeline has these failure modes:

| Step | Failure Mode | Recovery |
|------|-------------|----------|
| Embedding generation | API timeout or rate limit | SDK retries; if exhausted, return error to user |
| Hybrid search | Index unavailable | Return error with "search unavailable" message |
| LLM completion | API timeout or rate limit | SDK retries; if exhausted, return error to user |
| LLM content filtered | Empty content response | Return fallback message about content filtering |
| Citation parsing | No citations found | Fall back to showing all retrieved chunks |

## Demo Assumptions vs. Production Requirements

| Aspect | Demo Assumption | Production Requirement |
|--------|----------------|----------------------|
| Single instance | Acceptable for local dev | Multi-instance with health checks |
| No circuit breaker | SDK retries are sufficient | Circuit breaker pattern for cascading failure prevention |
| Synchronous ingestion | Acceptable for small corpus | Background job with progress tracking |
| No health endpoint | Not needed for local dev | `/health` and `/health/ready` endpoints |
| No graceful shutdown | Not critical for demo | Drain in-flight requests on SIGTERM |

## Health Check Design (Production)

```
GET /health          → 200 OK (process is running)
GET /health/ready    → 200 OK if:
                        - Azure OpenAI endpoint reachable
                        - Azure AI Search index exists and is queryable
                        - Configuration is valid
                     → 503 Service Unavailable if any check fails
```

## Resilience Patterns for Production

### 1. Circuit Breaker
Wrap Azure OpenAI calls with a circuit breaker (e.g., Polly) to prevent cascading failures when the service is degraded. After N consecutive failures, stop sending requests for a cooldown period.

### 2. Bulkhead Isolation
Separate the HTTP client pools for embedding, search, and chat completion so that slowness in one service doesn't exhaust connections for the others.

### 3. Timeout + Cancellation
Thread `CancellationToken` through all async operations (already implemented in the current code) so that client disconnections cancel in-flight work promptly.

### 4. Fallback Responses
If the LLM is unavailable, return the raw retrieved chunks without a generated answer. This provides partial value even during an outage.

### 5. Blue-Green Indexing
Create a new index alongside the existing one. Once the new index is fully populated and validated, swap the search client to point to the new index. Delete the old index.
