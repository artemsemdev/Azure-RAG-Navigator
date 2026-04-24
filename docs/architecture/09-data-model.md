# Data Model

## Overview

RAG Navigator uses a small, focused set of data models that flow through the ingestion and query pipelines. There is no relational database — all persistent storage is in the Azure AI Search index.

## Core Models

### DocumentChunk

The fundamental unit of storage and retrieval. Each chunk represents a semantically coherent fragment of a source document.

```
DocumentChunk
├── ChunkId: string          # Unique ID: "{documentId}_chunk_{index}"
├── DocumentId: string       # SHA-256 hash of the file name (first 12 hex chars)
├── DocumentTitle: string    # Extracted from the first # heading or file name
├── FileName: string         # Original file name (e.g., "runbook-database-failover.md")
├── Section: string          # Nearest heading above this chunk
├── ChunkIndex: int          # Sequential position within the document
├── Content: string          # The actual text content of the chunk
└── Embedding: float[1536]?  # Vector embedding (set during ingestion, not stored in model)
```

**Key design decisions:**
- `DocumentId` is derived from the file name, not the content. This means renaming a file creates a new document identity. This is intentional — the file name is the stable identifier in the source system.
- `Embedding` is nullable because it's set asynchronously after chunking but before indexing.
- `ChunkId` is deterministic given the same file and chunk index, enabling idempotent re-indexing.

### RetrievalResult

Wraps a `DocumentChunk` with its search relevance score.

```
RetrievalResult
├── Chunk: DocumentChunk     # The retrieved chunk
└── Score: double            # Relevance score from Azure AI Search (RRF-merged)
```

The score is the composite score from Reciprocal Rank Fusion, not a raw cosine similarity or BM25 score. Scores are not directly comparable across different queries.

### ChatResponse

The final output returned to the UI.

```
ChatResponse
├── Answer: string                    # LLM-generated answer text
├── Citations: Citation[]             # Parsed source references
└── Debug: DebugInfo?                 # Optional retrieval diagnostics
```

### Citation

A source reference parsed from the LLM's answer.

```
Citation
├── SourceId: string          # Local source id from the prompt, e.g. S1
├── FileName: string          # Source file name
├── DocumentTitle: string     # Document title
├── Section: string           # Section within the document
└── Snippet: string           # Short excerpt from the cited chunk (max 200 chars)
```

### DebugInfo

Diagnostic information for the debug panel.

```
DebugInfo
├── RetrievedChunks: RetrievedChunkDebug[]
└── FullPrompt: string                       # Complete prompt sent to the LLM

RetrievedChunkDebug
├── ChunkId: string
├── FileName: string
├── Section: string
├── Score: double
└── ContentPreview: string    # First 300 chars of chunk content
```

### IngestionSummary

Operational summary returned after a reindex run.

```
IngestionSummary
├── FoldersRequested: int
├── FilesFound: int
├── FilesProcessed: int
├── ChunksIndexed: int
└── FilesFailed: int
```

### SourceDocument

Aggregated view of a document in the index (for the sidebar document list).

```
SourceDocument
├── DocumentId: string
├── FileName: string
├── Title: string
└── ChunkCount: int           # Number of chunks this document was split into
```

## Data Flow

```
Source Files                    Application Models               Search Index
─────────────                  ──────────────────               ────────────
.md / .txt files   ──chunk──▶  DocumentChunk[]     ──upload──▶  SearchIndexDocument
                               (with Embedding)                  (persisted in Azure)

User Question      ──embed──▶  float[1536]         ──search──▶  SearchIndexDocument[]
                                                    ──map────▶  RetrievalResult[]
                                                    ──prompt──▶  ChatResponse
                                                                 (with Citations)
```

## Search Index Document (Persistence Model)

The `SearchIndexDocument` class maps directly to the Azure AI Search index schema. See [10-search-index-schema.md](10-search-index-schema.md) for the full schema definition.

```
SearchIndexDocument
├── ChunkId: string           # Key field
├── DocumentId: string        # Filterable
├── DocumentTitle: string     # Searchable
├── FileName: string          # Filterable, Facetable
├── Section: string           # Searchable
├── ChunkIndex: int           # Filterable, Sortable
├── Content: string           # Searchable (en.lucene analyzer)
└── ContentVector: float[]    # Vector field (1536 dimensions, HNSW, Cosine; validated at startup/upload)
```

## Model Ownership

| Model | Layer | Purpose |
|-------|-------|---------|
| `DocumentChunk` | Application | Core domain model, used everywhere |
| `RetrievalResult` | Application | Query pipeline output |
| `ChatResponse`, `Citation`, `DebugInfo`, `IngestionSummary` | Application | API response and operational summary models |
| `SourceDocument` | Application | Document list view model |
| `SearchIndexDocument` | Infrastructure | Azure AI Search persistence mapping |

The Application layer models have zero dependencies on Azure SDKs. The `SearchIndexDocument` in Infrastructure uses Azure Search SDK attributes for index schema generation.
