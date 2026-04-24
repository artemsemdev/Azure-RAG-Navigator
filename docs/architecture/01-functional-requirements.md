# Functional Requirements

## Core Use Cases

### UC-1: Ask a Question

**Actor:** Engineer
**Trigger:** Engineer types a question in the chat interface.
**Flow:**
1. Engineer enters a natural-language question.
2. System generates an embedding vector for the query.
3. System executes hybrid search (keyword + vector) against Azure AI Search.
4. System retrieves the top-k most relevant document chunks.
5. If relevant context remains after filtering, system builds a grounded prompt with the question and retrieved context.
6. System sends the prompt to Azure OpenAI for answer generation.
7. System parses citations from the LLM response.
8. System returns the answer, citations, and source metadata.

**Alternate Flow:** If no relevant context remains after filtering, the orchestrator returns a deterministic "not enough information" response without calling the LLM.

### UC-2: Reindex Documents

**Actor:** Engineer / Admin
**Trigger:** Engineer clicks "Reindex" in the sidebar.
**Flow:**
1. System scans all configured document folders for `.md` and `.txt` files.
2. For each file: extract title, split into semantic chunks, preserve metadata.
3. System generates embeddings for all chunks in batches.
4. System deletes the existing search index and recreates it.
5. System uploads all chunks with embeddings and metadata to Azure AI Search.
6. System updates the document list in the sidebar.

### UC-3: View Indexed Documents

**Actor:** Engineer
**Trigger:** Page load or after reindexing.
**Flow:**
1. System queries Azure AI Search for all unique documents.
2. System displays document titles, file names, and chunk counts in the sidebar.

### UC-4: Inspect Retrieval (Debug Mode)

**Actor:** Engineer / Developer
**Trigger:** Engineer enables debug mode and asks a question.
**Flow:**
1. System performs the normal query flow.
2. System additionally returns: retrieved chunk IDs, relevance scores, content previews, and the full prompt sent to the LLM.
3. Debug panel displays this information alongside the answer.

## User Journeys

### New Engineer Onboarding

1. Engineer opens RAG Navigator.
2. Clicks "Reindex" to index the latest documents.
3. Asks: "What is the onboarding checklist for new platform engineers?"
4. Reads the grounded answer with citations to the onboarding guide.
5. Follows up: "What ADRs should I read first?"
6. Gets an answer citing the ADR documents.

### Incident Response

1. On-call engineer receives a database alert.
2. Opens RAG Navigator and asks: "How do I handle a database failover?"
3. Gets step-by-step instructions cited from the failover runbook.
4. Asks: "Have we had outages related to this before?"
5. Gets a summary of the postmortem with a link to the source document.

### Architecture Review

1. Engineering lead asks: "Why did we choose event-driven architecture?"
2. Gets the full decision rationale cited from ADR-001.
3. Asks: "What are the security risks in this system?"
4. Gets a grounded answer from the security and threat model documentation.

## Ingestion Flow

```
Input: File system folder(s) containing .md and .txt files
  │
  ├─ Scan for files
  ├─ For each file:
  │   ├─ Read content
  │   ├─ Extract title from first # heading (or file name)
  │   ├─ Split by ## / ### headings into sections
  │   ├─ Sub-split large sections (>1500 chars) on paragraph boundaries
  │   ├─ Apply 200-char overlap between chunks
  │   ├─ Discard fragments < 100 chars
  │   └─ Assign metadata: documentId, fileName, title, section, chunkIndex
  │
  ├─ Batch embed all chunks (16 per batch)
  ├─ Delete and recreate search index
  └─ Upload chunks with embeddings to Azure AI Search
```

## Query Flow

```
Input: User question (string)
  │
  ├─ Generate embedding for the question
  ├─ Execute hybrid search:
  │   ├─ BM25 keyword search on Content field
  │   └─ HNSW vector search on ContentVector field
  │   └─ Results merged by Reciprocal Rank Fusion (RRF)
  │
  ├─ Filter results below minimum relevance threshold
  ├─ If no relevant results remain:
  │   └─ Return deterministic "not enough information" response
  │
  ├─ Build grounded prompt:
  │   ├─ System: "Answer only from context, cite sources"
  │   └─ User: Retrieved chunks + escaped question
  │
  ├─ Send to Azure OpenAI (temperature=0.1)
  ├─ Parse [Source: filename] citations from response
  └─ Return: answer, citations[], optional debug info
```

## Admin / Reindex Actions

| Action | Endpoint | Method | Behavior |
|--------|----------|--------|----------|
| Reindex all documents | `/api/index/reindex` | POST | Requires admin key outside development; scans folders, chunks, embeds, deletes/recreates index, uploads |
| List indexed documents | `/api/index/documents` | GET | Returns unique documents with chunk counts |
| Ask a question | `/api/chat` | POST | Full RAG pipeline, returns answer + citations |
