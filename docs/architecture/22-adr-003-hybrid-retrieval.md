# ADR-003: Hybrid Retrieval with Semantic Ranking

**Status:** Accepted (updated with semantic ranking)
**Date:** 2024-03-01
**Context:** RAG Navigator — choosing the retrieval strategy

## Context

Azure AI Search supports three retrieval modes:

1. **Keyword-only (BM25):** Traditional full-text search using term frequency and inverse document frequency.
2. **Vector-only (HNSW):** Semantic similarity search using embedding vectors.
3. **Hybrid (keyword + vector):** Both methods executed in parallel, results merged by Reciprocal Rank Fusion (RRF).

The choice affects answer quality, latency, and index complexity.

## Decision

Use **hybrid retrieval** combining BM25 keyword search and HNSW vector search, merged by Azure AI Search's built-in RRF algorithm.

## Rationale

### Keyword and Vector Search Have Complementary Strengths

| Scenario | Keyword (BM25) | Vector (HNSW) | Hybrid |
|----------|---------------|--------------|--------|
| Exact term match ("gRPC") | Excellent | Poor (may match semantically similar but wrong terms) | Excellent |
| Paraphrased question | Poor (different words) | Excellent | Excellent |
| Acronyms ("APIM", "AKS") | Excellent | Variable | Excellent |
| Error codes ("40613") | Excellent | Poor | Excellent |
| Conceptual questions | Poor | Excellent | Excellent |
| Typos in query | Poor | Good (embedding is robust to typos) | Good |

Hybrid search wins in every scenario because it captures both exact matches and semantic similarity.

### Research Supports Hybrid

Academic and industry benchmarks consistently show that hybrid retrieval outperforms either method alone. Microsoft's own documentation recommends hybrid search as the default for RAG applications.

### RRF Requires No Tuning

Reciprocal Rank Fusion merges ranked lists without requiring score normalization or weight tuning:

```
RRF_score(d) = Σ 1/(k + rank_i(d))
```

Where:
- `k` is a constant (default 60 in Azure AI Search).
- `rank_i(d)` is the rank of document `d` in the i-th result list.
- The sum is over all retrieval methods that returned the document.

This means:
- No need to normalize BM25 scores and cosine similarity scores to a common scale.
- No weight parameter to tune between keyword and vector results.
- Documents that appear in both result lists get naturally boosted.

### Implementation Is Simple

Azure AI Search handles RRF fusion server-side. The client just needs to:
1. Pass the query text (for keyword search).
2. Pass the query embedding (for vector search).
3. Azure AI Search returns a single, merged result list.

```csharp
var searchOptions = new SearchOptions { Size = topK };
searchOptions.VectorSearch = new VectorSearchOptions();
searchOptions.VectorSearch.Queries.Add(new VectorizedQuery(queryEmbedding) { ... });

// This single call executes both keyword and vector search, merged by RRF
var response = await _searchClient.SearchAsync<T>(queryText, searchOptions);
```

## Consequences

### Positive
- Best retrieval quality across diverse query types.
- No manual score normalization or weight tuning.
- Server-side fusion — no additional client logic.
- Works well with the mixed corpus (runbooks have exact steps, ADRs have conceptual reasoning).

### Negative
- Requires maintaining a vector field in the index (additional storage and indexing cost).
- Every query requires an embedding call (additional latency and cost).
- Vector dimensions (1536) are fixed to the embedding model.

### Trade-offs Accepted
- The embedding call adds ~100-300ms per query. This is acceptable given the LLM call dominates at 2-4 seconds.
- Vector storage adds ~6KB per chunk (1536 floats × 4 bytes). For 100 chunks, this is ~600KB — negligible.

## Alternatives Considered

| Alternative | Why Not |
|-------------|---------|
| **Keyword-only** | Fails on paraphrased or conceptual questions. Users would need to know the exact terminology in the documents. |
| **Vector-only** | Fails on exact term matches, acronyms, and error codes. Engineering documents heavily use specific terms. |
| **Hybrid without Semantic Ranker** | Simpler but misses the L2 re-ranking benefit. Semantic ranking is now available on the free tier of Basic SKU (1000 queries/month). |

## Semantic Ranking (L2 Re-Ranking)

Azure AI Search's semantic ranker provides an L2 re-ranking step on top of hybrid search:

```
Hybrid (BM25 + Vector) → RRF → Top-50 → Semantic Ranker → Top-5
```

This uses a Microsoft-trained cross-encoder model to re-rank results with deep language understanding. The semantic ranker is configured with:

- **Title field:** `DocumentTitle` — helps the model understand what each chunk is about.
- **Content field:** `Content` — the main text used for deep comprehension.
- **Keywords field:** `Section` — section headings provide additional topical signals.

```csharp
searchOptions.QueryType = SearchQueryType.Semantic;
searchOptions.SemanticSearch = new SemanticSearchOptions
{
    SemanticConfigurationName = "semantic-config",
    QueryCaption = new QueryCaption(QueryCaptionType.Extractive)
};
```

### Extractive Captions

Semantic ranking also produces extractive captions — short passages that the model identifies as most relevant to the query. These are surfaced in the debug panel alongside the reranker score (`@search.rerankerScore`).

### Cost

Semantic ranking is available at the **free tier** on Basic SKU (1000 queries/month). For higher volumes, the Standard semantic SKU is available.
