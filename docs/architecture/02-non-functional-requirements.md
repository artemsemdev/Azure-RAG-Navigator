# Non-Functional Requirements

This document defines the non-functional requirements for RAG Navigator, distinguishing between what is implemented in the current demo and what would be expected in a production deployment.

## Availability

| Aspect | Demo (Current) | Production Target |
|--------|---------------|-------------------|
| Uptime SLA | Best-effort, local dev | 99.9% (aligned with Azure SLA composition) |
| Redundancy | Single instance | Multi-instance behind load balancer |
| Failover | None | Azure Container Apps with min 2 replicas |
| Index availability | Single index, no replica | Azure AI Search with replicas |

**Current implementation:** The app runs as a single process. If it crashes, there is no automatic recovery. This is acceptable for a demo.

**Production path:** Deploy to Azure Container Apps with at least 2 replicas. Azure AI Search Basic tier supports 3 replicas for high availability.

## Latency

| Operation | Demo Target | Production Target |
|-----------|-------------|-------------------|
| Query (end-to-end) | < 5 seconds | < 3 seconds |
| Embedding generation | < 500ms | < 300ms |
| Hybrid search | < 200ms | < 100ms |
| LLM answer generation | < 4 seconds | < 2 seconds (with streaming) |
| Reindex (full corpus) | < 60 seconds | < 30 seconds (background) |

**Current implementation:** The dominant latency contributor is the Azure OpenAI chat completion call (typically 2-4 seconds). Embedding and search are fast. No streaming is implemented — the user waits for the full response.

**Production path:** Add streaming (SSE) for token-by-token response delivery. Cache frequent query embeddings. Semantic ranking (L2 re-ranking) is already enabled for improved retrieval relevance.

## Security

| Aspect | Demo (Current) | Production Target |
|--------|---------------|-------------------|
| Authentication | None (open access) | Azure AD / Entra ID |
| Transport | HTTP (local dev) | HTTPS with TLS 1.2+ |
| Secret management | Environment variables | Azure Key Vault |
| Input validation | Basic null/empty checks | Input sanitization + length limits |
| Prompt injection | LLM instruction only | Input filtering + output validation |
| Data classification | Internal engineering docs | Classify and enforce per-document access |

**Current implementation:** No authentication. API keys in environment variables. The LLM system prompt provides instruction-level grounding only.

**Production path:** Add Azure AD authentication. Store secrets in Key Vault. Implement prompt injection detection. Apply RBAC-based document filtering in search queries.

## Maintainability

| Aspect | Implementation |
|--------|---------------|
| Architecture | Three-layer modular monolith (Application, Infrastructure, Web) |
| Dependency injection | All services registered through interfaces |
| Configuration | Strongly-typed options with startup validation |
| Testing | Unit tests for core logic (chunking, prompt assembly) |
| Code conventions | Consistent naming, async/await, cancellation tokens |
| Documentation | Architecture docs, inline comments for non-obvious logic |

**Design principle:** Every service boundary is defined by an interface in the Application layer. Infrastructure implementations can be replaced without touching business logic.

## Observability

| Aspect | Demo (Current) | Production Target |
|--------|---------------|-------------------|
| Logging | `ILogger` with structured messages | Application Insights with W3C trace context |
| Metrics | None | Request duration, search latency, embedding latency |
| Tracing | None | Distributed traces across embedding → search → LLM |
| Alerting | None | Error rate, latency P95, index health |
| Debug mode | UI panel showing chunks + scores + prompt | Retain for dev; gate behind auth in production |

**Current implementation:** Structured logging through `Microsoft.Extensions.Logging`. Debug mode exposes retrieval details in the UI. No metrics or distributed tracing.

**Production path:** Add Application Insights SDK. Emit custom metrics for each pipeline stage. Create Grafana dashboards for query latency breakdown.

## Scalability

| Dimension | Demo (Current) | Scaling Path |
|-----------|---------------|--------------|
| Concurrent users | 1-5 | Horizontal app scaling + connection pooling |
| Corpus size | ~30 chunks | Azure AI Search supports millions of documents |
| Index throughput | Synchronous, single-threaded | Background jobs with parallel embedding batches |
| LLM throughput | Sequential requests | Request queuing + rate limit handling |

**Current bottleneck:** Azure OpenAI rate limits (tokens per minute) are the primary scaling constraint. The app makes one LLM call per question and one embedding call per question.

**Production path:** Implement retry with exponential backoff for rate limits. Use provisioned throughput units (PTU) for predictable latency. Queue long-running indexing jobs.

## Cost Awareness

| Resource | Cost Driver | Demo Estimate |
|----------|------------|---------------|
| Azure OpenAI (chat) | ~$0.005 per query (gpt-4o) | < $1/day for demo usage |
| Azure OpenAI (embeddings) | ~$0.0001 per 1K tokens | < $0.01 per full reindex |
| Azure AI Search | Basic tier: ~$75/month | Fixed monthly cost |
| Hosting | Local dev: free | Container Apps: ~$30/month minimum |

**Current implementation:** Costs are minimal for demo usage. The most significant fixed cost is the Azure AI Search service (Basic tier).

**Production path:** Monitor token consumption. Use gpt-4o-mini for cost-sensitive workloads. Consider Free tier of Azure AI Search during development.
