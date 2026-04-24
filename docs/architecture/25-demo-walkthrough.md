# Demo Walkthrough

## 5-Minute Interview Demo Script

This script is designed for a live demo during a technical interview. It covers the key capabilities while leaving room for architecture discussion.

### Setup (Before the Interview)

1. Ensure Azure services are running and configured.
2. Start the app: `cd src/Web && dotnet run`.
3. Open the browser to `http://localhost:5000`.
4. Click **Reindex** to ensure all documents are indexed.
5. Verify the document list shows both sample data and architecture documents.

### Demo Flow

#### 1. Orient the Audience (30 seconds)

> "This is RAG Navigator, an Engineering Knowledge Assistant I built to demonstrate a real Azure RAG pipeline. It indexes engineering documents — ADRs, runbooks, postmortems — and lets you ask questions with grounded, cited answers. Let me show you how it works."

Point out:
- Document list in the sidebar (titles and chunk counts).
- The mix of engineering docs and architecture docs.

#### 2. Ask a Practical Question (60 seconds)

Ask: **"How do we handle database failovers?"**

> "I'm asking a practical operations question. Watch what happens: the system embeds my question, runs a hybrid search combining keyword and vector matching, retrieves the most relevant chunks, and generates a grounded answer."

Point out:
- The answer cites the failover runbook.
- Citations show the source file, section, and a snippet.
- The answer includes specific steps, not generic advice.

#### 3. Enable Debug Mode (60 seconds)

Toggle **Debug Mode** on. Ask: **"What happened in the February API outage?"**

> "Debug mode shows the retrieval details. You can see the top-5 chunks retrieved, their relevance scores, and the full prompt sent to GPT-4o. This is the actual context the LLM used to generate its answer."

Point out:
- Retrieved chunks with scores.
- The full prompt structure: system instructions + context + question.
- How citations map back to specific chunks.

#### 4. Ask an Architecture Question (60 seconds)

Ask: **"Why did you choose hybrid retrieval instead of pure vector search?"**

> "The system indexes its own architecture documentation. So I can ask it about design decisions, and it answers by retrieving from ADR-003 — the hybrid retrieval decision record."

Point out:
- The answer cites the ADR with specific rationale.
- This demonstrates the system explaining its own architecture.

#### 5. Show the Architecture (60 seconds)

Navigate to the **Architecture** page.

> "The app also has architecture documentation pages. This isn't just a coding exercise — there's a full solution architecture: context diagrams, component diagrams, deployment topology, security model, cost analysis, and a Well-Architected review."

Briefly show:
- The architecture overview.
- The ADR index.
- The diagram section.

#### 6. Close with Trade-offs (30 seconds)

> "This is a demo-scoped project. I've been deliberate about what's simplified: no user auth, synchronous indexing, no streaming. But the architecture documentation is honest about these trade-offs and describes the production path for each one."

### Total: ~5 minutes

---

## Sample Architecture Questions

Prepare for these follow-up questions from interviewers:

### RAG Pipeline

| Question | Key Points |
|----------|-----------|
| "How does the chunking work?" | Heading-aware markdown splitting, 1500-char max, 200-char overlap, paragraph boundary sub-splitting |
| "Why not use Semantic Kernel?" | Transparency — direct SDK usage makes every pipeline step visible and explainable |
| "How do you prevent hallucination?" | System prompt grounding, low temperature (0.1), relevance score filtering, deterministic no-context response, citation extraction |
| "What if the answer isn't in the documents?" | If no relevant chunks remain after filtering, the orchestrator returns "not enough information" without calling the LLM |

### Architecture

| Question | Key Points |
|----------|-----------|
| "Why a modular monolith?" | Right-sized for scope; interface boundaries enable extraction; avoids distributed system overhead |
| "How would you scale this?" | Horizontal app scaling (stateless), Search replicas, OpenAI PTU, background ingestion |
| "What about security?" | Managed identity ready, API key fallback for dev, prompt injection defense, no user uploads |
| "What would you change for production?" | Add auth, streaming, health checks, Application Insights, circuit breakers, blue-green indexing |

### Azure-Specific

| Question | Key Points |
|----------|-----------|
| "Why Azure AI Search over Cosmos DB vector search?" | Purpose-built for search: hybrid search, RRF fusion, analyzers, scoring profiles, faceting |
| "How does DefaultAzureCredential work?" | Chain: env vars → managed identity → az login. No code change between dev and prod. |
| "What Azure RBAC roles does the app need?" | Cognitive Services OpenAI User + Search Index Data Contributor |
| "How do you handle Azure OpenAI rate limits?" | SDK built-in retries; production would add circuit breakers and queue-based backpressure |

---

## Talking Points for Trade-offs

### "This is simplified because..."

| Simplification | Production Alternative | Why Simplified |
|---------------|----------------------|----------------|
| No individual user identity | Azure AD / Entra ID | Optional chat API key exists; full user auth is out of demo scope |
| Synchronous reindexing | Background worker with progress tracking | Acceptable for ~30 docs, avoids async job complexity |
| No streaming | SSE token-by-token delivery | Requires additional endpoint and JS changes |
| Environment variable secrets | Azure Key Vault | Simpler local dev, production path documented |
| Full reindex only | Incremental indexing with change detection | Acceptable for demo corpus size |

### "This is production-ready because..."

| Feature | Why It's Strong |
|---------|----------------|
| Interface-based architecture | Any implementation can be swapped without touching business logic |
| Startup config validation | Fails fast with clear errors, not mysterious runtime exceptions |
| Structured logging | Ready for Application Insights integration |
| Managed identity support | No code changes needed for production auth |
| CancellationToken throughout | Proper async lifecycle management |
| Deterministic chunk IDs | Idempotent reindexing |
