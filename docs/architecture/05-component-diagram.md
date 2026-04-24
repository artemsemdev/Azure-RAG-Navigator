# Component Diagram

## Overview

This diagram details the internal components of the RAG Navigator application, showing how the Application layer interfaces connect to Infrastructure implementations and how the Web layer orchestrates the two primary workflows: document ingestion and question answering.

## Component Diagram

```mermaid
graph TB
    subgraph "Web Layer"
        UI[Razor Pages UI]
        ChatAPI["POST /api/chat"]
        IndexAPI["POST /api/index/reindex"]
        DocsAPI["GET /api/index/documents"]
    end

    subgraph "Application Layer"
        RO[RagOrchestrator]
        DP[DocumentProcessor]
        PB[PromptBuilder]
        CH[MarkdownDocumentChunker]

        IE[IEmbeddingService]
        IR[IRetrievalService]
        IC[IChatCompletionService]
        IS[ISearchIndexService]
        IDC[IDocumentChunker]
    end

    subgraph "Infrastructure Layer"
        AES[AzureOpenAI<br/>EmbeddingService]
        ACS[AzureOpenAI<br/>ChatService]
        AIS[AzureSearch<br/>IndexService]
        ARS[AzureSearch<br/>RetrievalService]
    end

    subgraph "Azure Services"
        AOAI[Azure OpenAI]
        SEARCH[Azure AI Search]
    end

    %% Web → Application
    ChatAPI --> RO
    IndexAPI --> DP
    DocsAPI --> IS

    %% Orchestrator dependencies
    RO --> IE
    RO --> IR
    RO --> IC
    RO --> PB

    %% Processor dependencies
    DP --> IDC
    DP --> IE
    DP --> IS

    %% Interface implementations
    IDC -.->|implements| CH
    IE -.->|implements| AES
    IR -.->|implements| ARS
    IC -.->|implements| ACS
    IS -.->|implements| AIS

    %% Infrastructure → Azure
    AES --> AOAI
    ACS --> AOAI
    AIS --> SEARCH
    ARS --> SEARCH

    style AOAI fill:#0078D4,color:#fff
    style SEARCH fill:#0078D4,color:#fff
```

## Component Responsibilities

### Application Layer (No Azure Dependencies)

| Component | Responsibility |
|-----------|---------------|
| **RagOrchestrator** | Orchestrates the full query pipeline: embed query → retrieve → build prompt → generate answer → extract citations |
| **DocumentProcessor** | Orchestrates ingestion: read files → chunk → embed → index |
| **MarkdownDocumentChunker** | Splits markdown documents into semantic chunks on heading boundaries with overlap |
| **PromptBuilder** | Constructs the system and user prompts for grounded generation; parses citations from LLM output |

### Interfaces (Application Layer Contracts)

| Interface | Contract |
|-----------|----------|
| **IDocumentChunker** | `Chunk(content, fileName, title)` → `DocumentChunk[]` |
| **IEmbeddingService** | `GenerateEmbeddingAsync(text)` → `float[]`; batch variant for multiple texts |
| **ISearchIndexService** | `CreateOrUpdateIndexAsync()`, `UploadChunksAsync()`, `DeleteAllDocumentsAsync()`, `GetIndexedDocumentsAsync()` |
| **IRetrievalService** | `SearchAsync(query, queryEmbedding, topK)` → `RetrievalResult[]` |
| **IChatCompletionService** | `GenerateAnswerAsync(systemPrompt, userPrompt)` → `string` |

### Infrastructure Layer (Azure SDK Implementations)

| Component | Azure SDK | Purpose |
|-----------|-----------|---------|
| **AzureOpenAIEmbeddingService** | `Azure.AI.OpenAI` → `EmbeddingClient` | Generates 1536-dimensional vectors for text |
| **AzureOpenAIChatService** | `Azure.AI.OpenAI` → `ChatClient` | Sends grounded prompts to GPT-4o |
| **AzureSearchIndexService** | `Azure.Search.Documents` → `SearchIndexClient` + `SearchClient` | Creates index schema, uploads/deletes documents |
| **AzureSearchRetrievalService** | `Azure.Search.Documents` → `SearchClient` | Executes hybrid (keyword + vector) queries |

### Web Layer

| Component | Responsibility |
|-----------|---------------|
| **Program** | Composition root: wires configuration, DI, middleware, Razor Pages, and endpoint modules |
| **ChatEndpoints** | Handles `/api/chat` HTTP validation, endpoint auth gate, prompt-injection logging, debug gating, and response shaping |
| **IndexEndpoints** | Handles index document listing and reindex endpoint authorization/orchestration |
| **DocumentFolderResolver** | Resolves and validates source folders for reindexing, including `SampleDataPath` traversal protection |
| **Configuration extensions** | Isolate environment variable mapping, endpoint auth, telemetry export, and rate limiting setup |

## Dependency Flow

```
Web → Application → Infrastructure → Azure SDKs → Azure Services
```

- The Web layer depends on Application (orchestrators) and Infrastructure (DI registration only).
- The Application layer defines interfaces and depends on nothing external.
- The Infrastructure layer implements Application interfaces using Azure SDKs.
- Dependencies flow inward, following the Dependency Inversion Principle.

## Key Design Decisions

1. **Static PromptBuilder:** Prompt construction is a pure function with no state or external dependencies, so it's implemented as a static class for simplicity.
2. **Chunker is synchronous:** Document chunking is CPU-bound string manipulation with no I/O, so `IDocumentChunker` returns synchronously.
3. **Orchestrators are not interfaces:** `RagOrchestrator` and `DocumentProcessor` are concrete classes because they contain the core business logic — they are the "use cases," not infrastructure concerns.
