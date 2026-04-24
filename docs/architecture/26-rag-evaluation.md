# RAG Evaluation

## Purpose

RAG quality cannot be protected by unit tests alone. Chunking, retrieval parameters, prompt changes, and corpus updates can all change answer quality without breaking compilation.

The evaluation harness provides a lightweight CI-friendly baseline for retrieval quality:

- A curated golden question catalog.
- Expected source files for each question.
- Source recall@k scoring.
- Pass/fail thresholds per question.

## Current Implementation

The initial harness lives in `tests/RAGNavigator.Tests/Evaluation`:

| Component | Responsibility |
|-----------|----------------|
| `GoldenQuestionCatalog` | Curated questions and expected source files |
| `RetrievalEvaluator` | Computes source recall for retrieved chunks |
| `RetrievalEvaluationResult` | Captures matched, missing, retrieved files, and pass/fail status |
| `RetrievalEvaluatorTests` | Verifies scorer behavior and validates that golden sources exist in the corpus |
| `LiveRetrievalEvaluationTests` | Optional Azure-backed evaluation that reindexes an isolated eval index and runs golden questions end-to-end |

The default harness is independent of Azure. This keeps the normal CI path deterministic and fast while creating a shared contract for live retrieval evaluation. The live test is opt-in and only runs when explicitly enabled.

## Metric

### Source Recall@K

For each golden question:

```
source_recall@k = matched_expected_source_files_in_top_k / expected_source_files
```

Example:

| Expected Sources | Retrieved Top 5 | Recall |
|------------------|-----------------|--------|
| `runbook-database-failover.md` | `runbook-database-failover.md`, `standard-observability.md` | 1.0 |
| `adr-001-event-driven-architecture.md`, `standard-api-design-guidelines.md` | `adr-001-event-driven-architecture.md` | 0.5 |

The default threshold is 1.0 because the current golden questions are designed around clear single-source or small multi-source expectations.

## Golden Question Coverage

The initial catalog covers:

| Area | Expected Source |
|------|-----------------|
| Database failover runbook | `runbook-database-failover.md` |
| February API outage | `postmortem-2024-02-api-outage.md` |
| Event-driven architecture decision | `adr-001-event-driven-architecture.md` |
| API design guidelines | `standard-api-design-guidelines.md` |
| RAG query pipeline | `06-query-sequence.md` |

## CI Usage

The current CI test run validates:

- The evaluator math.
- Top-K behavior.
- Case-insensitive source matching.
- Golden expected files exist in `sample-data/` or `docs/architecture/`.

### Default CI

```bash
dotnet test RAGNavigator.sln --configuration Release
```

The live Azure-backed evaluation is skipped unless `RAG_NAVIGATOR_RUN_LIVE_RETRIEVAL_EVAL=1`.

### Live Retrieval Evaluation

The live evaluation:

1. Builds the real application service graph.
2. Reindexes `sample-data/` and `docs/architecture/`.
3. Runs each golden question against Azure AI Search.
4. Computes source recall@k.
5. Deletes the evaluation index unless `RAG_EVAL_KEEP_INDEX=1`.

Required environment variables:

| Variable | Purpose |
|----------|---------|
| `RAG_NAVIGATOR_RUN_LIVE_RETRIEVAL_EVAL=1` | Explicitly enables live eval |
| `RAG_EVAL_SEARCH_INDEX_NAME` | Dedicated eval index; must start with `rag-navigator-eval` |
| `AZURE_OPENAI_ENDPOINT` | Azure OpenAI endpoint |
| `AZURE_OPENAI_EMBEDDING_DEPLOYMENT` | Embedding deployment |
| `AZURE_SEARCH_ENDPOINT` | Azure AI Search endpoint |
| `AZURE_OPENAI_API_KEY` | Optional when managed identity / `az login` is available |
| `AZURE_SEARCH_API_KEY` | Optional when managed identity / `az login` is available |

Example:

```bash
export RAG_NAVIGATOR_RUN_LIVE_RETRIEVAL_EVAL=1
export RAG_EVAL_SEARCH_INDEX_NAME="rag-navigator-eval-dev"
dotnet test RAGNavigator.sln --configuration Release --filter "Category=Integration"
```

The eval index name guard prevents accidentally deleting a non-evaluation search index during reindex.

## Next Steps

1. Store live evaluation output as a CI artifact.
2. Add answer-level checks:
   - Citation precision.
   - Groundedness.
   - Refusal accuracy for out-of-corpus questions.
3. Track eval trends over time for retrieval and prompt changes.
