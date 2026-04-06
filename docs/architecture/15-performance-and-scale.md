# Performance and Scale

## Query Latency Breakdown

End-to-end query latency is dominated by the LLM completion call:

| Stage | Typical Latency | % of Total | Scalable? |
|-------|----------------|------------|-----------|
| Query embedding | 100-300ms | 5-10% | Rate limit bound |
| Hybrid search + semantic ranking | 50-250ms | 3-8% | Index replicas |
| Score filtering | < 1ms | ~0% | N/A |
| Prompt construction | < 1ms | ~0% | N/A |
| LLM completion | 1.5-4s | 80-90% | PTU / model selection |
| Citation extraction | < 1ms | ~0% | N/A |
| **Total** | **~2-5s** | 100% | |

### Key Insight
Optimizing the search or chunking stages has minimal impact on user-perceived latency. The LLM call is the bottleneck.

## Likely Bottlenecks

### 1. Azure OpenAI Rate Limits (Primary)

Azure OpenAI enforces tokens-per-minute (TPM) and requests-per-minute (RPM) limits per deployment.

| Deployment | Typical Limit (Standard) | Impact |
|------------|-------------------------|--------|
| gpt-4o | 30K-150K TPM | ~15-75 concurrent queries |
| text-embedding-ada-002 | 120K-350K TPM | Rarely a bottleneck for queries |

**Mitigation:**
- Use provisioned throughput units (PTU) for predictable latency.
- Use gpt-4o-mini for lower cost and higher throughput.
- Implement request queuing with exponential backoff on 429 responses.

### 2. Azure AI Search Query Throughput

| SKU | Queries Per Second | Replicas |
|-----|-------------------|----------|
| Basic | ~15 QPS | 1-3 |
| Standard S1 | ~15 QPS per replica | 1-12 |

**Mitigation:** Add replicas to scale read throughput linearly.

### 3. Indexing Throughput

For reindexing, the bottleneck is embedding generation:
- 16 texts per embedding batch × ~200ms per batch = ~12.5 batches/second.
- 100 chunks ≈ 7 batches ≈ 1.5 seconds of embedding time.
- For 10,000 chunks: ~625 batches ≈ ~2 minutes.

**Mitigation:** Parallelize embedding batches (currently sequential). Use larger batch sizes where the API allows.

## Scaling Path

### Horizontal Scaling (Compute)

```
1 instance (demo)  →  2-3 instances (small team)  →  N instances behind load balancer
```

The application is stateless — all state is in Azure AI Search. Horizontal scaling requires no code changes, only deployment configuration.

### Vertical Scaling (Azure Services)

| Dimension | Current | Scale-Up Path |
|-----------|---------|---------------|
| Search throughput | Basic (1 replica) | Standard S1 with 3+ replicas |
| LLM throughput | Standard deployment | Provisioned throughput (PTU) |
| Index size | < 100 chunks | Standard S1 supports 15M documents |
| Embedding throughput | Sequential batches | Parallel batches with rate limiting |

## Caching Opportunities

| Cache Target | Hit Rate Potential | Implementation |
|-------------|-------------------|----------------|
| Query embeddings | Low (unique questions) | Redis / in-memory LRU |
| Search results | Low-medium (similar queries) | Redis with TTL |
| Full Q&A responses | Low (unique questions) | Redis with content hash key |
| Document list | High (changes only on reindex) | In-memory, invalidate on reindex |

**Assessment:** Caching provides limited value for unique queries but is useful for:
- The document list sidebar (changes rarely).
- Repeated identical questions (in a multi-user scenario).
- Embedding caching if the same text is re-embedded across operations.

## Top-K Tuning

The `topK` parameter (currently 5) controls how many chunks are included in the prompt:

| topK | Pros | Cons |
|------|------|------|
| 3 | Lower cost, faster LLM response | May miss relevant context |
| 5 | Good balance for most queries | Moderate prompt size |
| 8 | Comprehensive context | Higher cost, longer responses |
| 10+ | Maximum coverage | Prompt too long, diminishing returns, higher cost |

**Recommendation:** 5 is the right default for a corpus of this size. For larger corpora, consider adaptive top-k based on score distribution — include chunks until the relevance score drops below a threshold.

## Indexing Performance

### Current Corpus (~33 files, ~80 chunks)

| Phase | Duration |
|-------|----------|
| File reading + chunking | < 100ms |
| Embedding generation | 2-5s |
| Index delete + create | 1-2s |
| Chunk upload | 1-2s |
| **Total** | **~5-10s** |

### Projected at Scale

| Corpus Size | Estimated Index Time | Bottleneck |
|-------------|---------------------|------------|
| 100 documents, 500 chunks | 30-60 seconds | Embedding generation |
| 1,000 documents, 5,000 chunks | 5-10 minutes | Embedding generation |
| 10,000 documents, 50,000 chunks | 1-2 hours | Embedding API rate limits |

For large corpora, incremental indexing (only re-embed changed documents) is essential. The deterministic `DocumentId` based on file name enables change detection.
