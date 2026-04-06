# Query Sequence Diagram

## Overview

This diagram traces the complete lifecycle of a user question through the RAG pipeline, from the UI to the final answer with citations.

## Sequence Diagram

```mermaid
sequenceDiagram
    actor User as Engineer
    participant UI as Web UI
    participant API as /api/chat
    participant RO as RagOrchestrator
    participant ES as EmbeddingService
    participant RS as RetrievalService
    participant PB as PromptBuilder
    participant CS as ChatCompletionService
    participant AOAI as Azure OpenAI
    participant AIS as Azure AI Search

    User->>UI: Types question
    UI->>API: POST /api/chat {question, debugMode}
    API->>RO: AskAsync(question, debugMode)

    Note over RO: Step 1: Embed the query
    RO->>ES: GenerateEmbeddingAsync(question)
    ES->>AOAI: POST /embeddings
    AOAI-->>ES: float[1536]
    ES-->>RO: queryEmbedding

    Note over RO: Step 2: Hybrid retrieval + semantic re-ranking
    RO->>RS: SearchAsync(question, queryEmbedding, topK=5)
    RS->>AIS: Hybrid query (text + vector + semantic)
    Note over AIS: BM25 keyword search<br/>+ HNSW vector search<br/>merged by RRF<br/>→ Semantic re-ranking (L2)
    AIS-->>RS: Re-ranked results + captions
    RS-->>RO: RetrievalResult[] (chunks + scores + rerankerScore)

    Note over RO: Step 3: Filter low-relevance results
    RO->>RO: Filter by minimum score threshold

    Note over RO: Step 4: Build grounded prompt
    RO->>PB: BuildUserPrompt(question, relevantResults)
    PB-->>RO: Structured prompt with context + question

    Note over RO: Step 5: Generate answer
    RO->>CS: GenerateAnswerAsync(systemPrompt, userPrompt)
    CS->>AOAI: POST /chat/completions (temp=0.1)
    AOAI-->>CS: Answer text with [Source: ...] citations
    CS-->>RO: answer string

    Note over RO: Step 6: Extract citations
    RO->>PB: ExtractCitations(answer, relevantResults)
    PB-->>RO: Citation[] (fileName, section, snippet)

    RO-->>API: ChatResponse {answer, citations, debug?}
    API-->>UI: JSON response
    UI-->>User: Rendered answer + citations panel

    opt Debug Mode Enabled
        UI->>UI: Render debug panel with<br/>chunks, scores, reranker scores,<br/>captions, full prompt
    end
```

## Latency Breakdown

| Step | Typical Duration | Notes |
|------|-----------------|-------|
| Query embedding | 100-300ms | Single text → 1536-dim vector |
| Hybrid search + semantic ranking | 50-250ms | Includes L2 semantic re-ranking |
| Score filtering | < 1ms | In-memory filter |
| Prompt construction | < 1ms | String concatenation |
| LLM generation | 1.5-4s | Dominant latency; depends on answer length |
| Citation extraction | < 1ms | Regex parsing |
| **Total** | **~2-5s** | LLM call is 80-90% of total time |

## Error Handling

| Failure Point | Behavior |
|--------------|----------|
| Embedding call fails | Exception propagates → HTTP 500 → UI shows error message |
| Search call fails | Exception propagates → HTTP 500 → UI shows error message |
| LLM returns empty content | Graceful fallback message about content filtering |
| LLM does not include citations | PromptBuilder falls back to all retrieved chunks as citations |
| No relevant results found | LLM is instructed to say "not enough information" |

## Design Notes

- **Temperature 0.1** is deliberately low to maximize factual grounding. Higher temperatures would produce more creative but less reliable answers.
- **Top-K = 5** balances context richness against prompt token budget. More chunks provide better coverage but increase cost and latency.
- **Minimum relevance threshold** filters out noise from the search results before they reach the LLM, reducing the risk of distraction by irrelevant content.
