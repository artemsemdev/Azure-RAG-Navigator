# Cost Considerations

## Cost Drivers

### Azure OpenAI

| Operation | Model | Pricing (Pay-as-you-go) | Usage Pattern |
|-----------|-------|------------------------|---------------|
| Chat completion (input) | gpt-4o | ~$2.50 / 1M tokens | ~2K tokens per query (context + question) |
| Chat completion (output) | gpt-4o | ~$10.00 / 1M tokens | ~500 tokens per answer |
| Embeddings | text-embedding-ada-002 | ~$0.10 / 1M tokens | ~200 tokens per chunk/query |

**Per-query cost estimate:** ~$0.003-0.008 (varies with context size and answer length).
**Per-reindex cost (80 chunks):** ~$0.002 (embedding only).

**Cost-saving options:**
- Use **gpt-4o-mini** (~10x cheaper) for less complex queries.
- Reduce `topK` to send fewer chunks in the prompt.
- Cache embeddings for frequently asked questions.

### Azure AI Search

| SKU | Monthly Cost | Included |
|-----|-------------|----------|
| Free | $0 | 50MB, 3 indexes, no replicas, no semantic ranker |
| Basic | ~$75/month | 2GB, 15 indexes, 3 replicas, semantic ranker (free tier: 1000 queries/month) |
| Standard S1 | ~$250/month | 25GB, 50 indexes, 12 replicas, semantic ranker (standard tier) |

**For demo:** Basic tier is sufficient. Semantic ranking is enabled at the free tier (1000 queries/month), which is more than enough for demo and small-team use. The Free tier works for development but has aggressive limits.

**Key cost insight:** Azure AI Search is the only fixed monthly cost. Everything else (OpenAI, hosting) scales with usage.

### Hosting

| Option | Monthly Cost | Notes |
|--------|-------------|-------|
| Local `dotnet run` | $0 | Development only |
| Azure App Service (B1) | ~$15/month | Shared compute, sufficient for demo |
| Azure Container Apps | ~$30/month | Consumption plan, scales to zero |
| Azure Container Apps (dedicated) | ~$80/month | Guaranteed capacity |

### Telemetry

| Service | Monthly Cost |
|---------|-------------|
| Application Insights (5GB/month free) | $0 for demo |
| Log Analytics (5GB/month free) | $0 for demo |
| Beyond free tier | ~$2.30/GB ingested |

## Monthly Cost Estimates

### Demo Usage (1-10 queries/day)

| Resource | Cost |
|----------|------|
| Azure AI Search (Basic) | $75 |
| Azure OpenAI (queries) | < $1 |
| Azure OpenAI (indexing) | < $0.01 |
| Hosting (local) | $0 |
| **Total** | **~$76/month** |

### Light Production (100 queries/day)

| Resource | Cost |
|----------|------|
| Azure AI Search (Basic) | $75 |
| Azure OpenAI (queries) | ~$15-25 |
| Azure OpenAI (indexing) | < $1 |
| Hosting (Container Apps) | ~$30 |
| Telemetry | $0 (within free tier) |
| **Total** | **~$120-130/month** |

### Medium Production (1,000 queries/day)

| Resource | Cost |
|----------|------|
| Azure AI Search (Standard S1) | $250 |
| Azure OpenAI (queries) | ~$100-250 |
| Hosting (Container Apps, 2 replicas) | ~$80 |
| Telemetry | ~$10 |
| **Total** | **~$440-590/month** |

## Cost Control Ideas

### For Demo

1. **Use Free tier for Azure AI Search during development.** Switch to Basic only when you need replicas or more storage.
2. **Use gpt-4o-mini** for testing. Switch to gpt-4o only for final demo or when answer quality matters.
3. **Delete Azure resources when not in use.** The Azure AI Search Basic tier costs $75/month even when idle.
4. **Set Azure OpenAI deployment quotas** to limit accidental spend.

### For Production

1. **Monitor token consumption daily.** Set up Azure Cost Management alerts at 80% of budget.
2. **Implement rate limiting** to prevent runaway costs from automated or abusive clients.
3. **Use PTU for predictable costs** if query volume is steady and high enough to justify commitment.
4. **Cache repeated queries** — even a 10% cache hit rate saves proportionally on LLM costs.
5. **Optimize prompts** — shorter system prompts and fewer retrieved chunks reduce per-query token usage.
6. **Incremental indexing** — re-embed only changed documents instead of full re-index.

## Cost vs. Quality Trade-offs

| Knob | Cost Impact | Quality Impact |
|------|------------|----------------|
| gpt-4o → gpt-4o-mini | -90% LLM cost | Slightly less nuanced answers |
| topK 5 → 3 | -30% prompt tokens | May miss relevant context |
| text-embedding-ada-002 → text-embedding-3-small | Similar cost | Comparable quality, newer model |
| Basic → Free Search | -$75/month | No replicas, 50MB limit |
| Disable debug mode | Negligible | No retrieval visibility |
