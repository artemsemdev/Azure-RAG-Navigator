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

The harness is intentionally independent of Azure. This keeps the default CI path deterministic and fast while creating a shared contract for future live retrieval evaluation.

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

This does not yet run live Azure AI Search retrieval. It is the foundation for that next step.

## Next Steps

1. Add an opt-in integration test profile that:
   - Reindexes the sample corpus.
   - Runs each golden question against Azure AI Search.
   - Fails when source recall drops below threshold.
2. Store evaluation output as a CI artifact.
3. Add answer-level checks:
   - Citation precision.
   - Groundedness.
   - Refusal accuracy for out-of-corpus questions.
4. Track eval trends over time for retrieval and prompt changes.
