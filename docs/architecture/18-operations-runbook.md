# Operations Runbook

## How to Reindex Documents

### Via the UI
1. Open RAG Navigator in the browser.
2. Click the **Reindex** button in the left sidebar.
3. Wait for the toast notification confirming completion.
4. The document list in the sidebar updates automatically.

### Via the API
```bash
curl -X POST http://localhost:5000/api/index/reindex \
  -H "X-Admin-Key: $ADMIN_API_KEY"
```

Expected response:
```json
{
  "message": "Indexing complete.",
  "chunksIndexed": 82
}
```

### Via the CLI (development)
```bash
cd src/Web
dotnet run
# In another terminal:
curl -X POST http://localhost:5000/api/index/reindex
```

In Development, the admin key may be omitted if `ADMIN_API_KEY` is not configured. In non-development environments, `ADMIN_API_KEY` must be configured or reindexing returns HTTP 503.

## How to Diagnose Failed Ingestion

### Symptoms
- Reindex returns an error or zero chunks.
- Document list is empty after reindex.
- Toast notification shows an error message.

### Diagnostic Steps

1. **Check the sample-data folder exists:**
   ```bash
   ls -la sample-data/
   ls -la docs/architecture/
   ```
   If the folders are empty or missing, the app has no documents to index.

2. **Check application logs:**
   Look for log messages from `DocumentProcessor`:
   ```
   [Information] Starting document ingestion from /path/to/sample-data
   [Information] Found 7 files to process
   [Warning] No .md or .txt files found in /path/to/sample-data
   ```

3. **Check Azure OpenAI connectivity:**
   - Verify `AZURE_OPENAI_ENDPOINT` is set correctly.
   - Verify the API key or `az login` session is valid.
   - Check Azure OpenAI deployment names match configuration.
   - Test connectivity:
     ```bash
     curl -H "api-key: $AZURE_OPENAI_API_KEY" \
       "$AZURE_OPENAI_ENDPOINT/openai/deployments?api-version=2024-02-01"
     ```

4. **Check Azure AI Search connectivity:**
   - Verify `AZURE_SEARCH_ENDPOINT` is set correctly.
   - Verify the search API key or `az login` session is valid.
   - Test connectivity:
     ```bash
     curl -H "api-key: $AZURE_SEARCH_API_KEY" \
       "$AZURE_SEARCH_ENDPOINT/indexes?api-version=2024-07-01"
     ```

5. **Check for rate limiting:**
   If embedding generation fails partway through, the Azure OpenAI deployment may be rate-limited. Look for HTTP 429 errors in logs. Wait and retry.

## How to Troubleshoot Poor Answers

### Symptoms
- Answers are vague or not grounded in the documents.
- Citations are missing or incorrect.
- The LLM says "not enough information" when the answer exists in the corpus.

### Diagnostic Steps

1. **Enable Debug Mode:**
   - Toggle the "Debug Mode" checkbox in the sidebar.
   - Re-ask the question.
   - Inspect the debug panel:
     - Are the right chunks being retrieved?
     - What are the relevance scores?
     - Does the full prompt include the relevant content?

2. **Check chunk quality:**
   If the relevant content is split awkwardly across chunks, the chunker may be breaking it at the wrong point. Review the chunk boundaries in the debug panel.

3. **Check retrieval quality:**
   - If the right document appears but the wrong section is retrieved, the query may be too ambiguous.
   - Try rephrasing the question to use terms that appear in the target section.

4. **Check if the document is indexed:**
   - Verify the document appears in the sidebar document list.
   - If it doesn't, check that the file is in a scanned folder and has a `.md` or `.txt` extension.

5. **Check the system prompt:**
   If the LLM is not citing sources properly, the system prompt may have been modified. The current system prompt is defined in `PromptBuilder.SystemPrompt`.

6. **Compare keyword vs. vector relevance:**
   The debug panel shows the RRF-merged scores. If scores are very low (< 0.01), the query may not match the document content in either keyword or semantic space.

## How to Validate Azure Configuration

### Required Configuration

| Setting | Environment Variable | Example |
|---------|---------------------|---------|
| OpenAI Endpoint | `AZURE_OPENAI_ENDPOINT` | `https://my-openai.openai.azure.com/` |
| Chat Deployment | `AZURE_OPENAI_CHAT_DEPLOYMENT` | `gpt-4o` |
| Embedding Deployment | `AZURE_OPENAI_EMBEDDING_DEPLOYMENT` | `text-embedding-ada-002` |
| OpenAI API Key | `AZURE_OPENAI_API_KEY` | (32-char key, or empty for managed identity) |
| Search Endpoint | `AZURE_SEARCH_ENDPOINT` | `https://my-search.search.windows.net` |
| Search Index Name | `AZURE_SEARCH_INDEX_NAME` | `rag-navigator-index` |
| Search API Key | `AZURE_SEARCH_API_KEY` | (52-char key, or empty for managed identity) |
| Reindex Admin Key | `ADMIN_API_KEY` | Required outside Development |
| Retrieval Top K | `RAG_TOP_K` | `5` |
| Minimum Relevance Score | `RAG_MINIMUM_RELEVANCE_SCORE` | `0.01` |

### Startup Validation

The application validates configuration on startup using `ValidateOnStart()`. If required settings are missing, the app will fail to start with a clear error message:

```
DataAnnotation validation failed for 'AzureOpenAIOptions' members: 'Endpoint' with the error: 'The Endpoint field is required.'
```

### Quick Validation Script

```bash
echo "OpenAI Endpoint: ${AZURE_OPENAI_ENDPOINT:-(not set)}"
echo "Chat Deployment: ${AZURE_OPENAI_CHAT_DEPLOYMENT:-(not set)}"
echo "Embedding Deployment: ${AZURE_OPENAI_EMBEDDING_DEPLOYMENT:-(not set)}"
echo "OpenAI API Key: ${AZURE_OPENAI_API_KEY:+(set)}"
echo "Search Endpoint: ${AZURE_SEARCH_ENDPOINT:-(not set)}"
echo "Search Index: ${AZURE_SEARCH_INDEX_NAME:-(not set)}"
echo "Search API Key: ${AZURE_SEARCH_API_KEY:+(set)}"
```

## Common Support Actions

### Reset the Search Index

If the index is corrupted or in an unexpected state:

```bash
# Delete the index manually
curl -X DELETE \
  -H "api-key: $AZURE_SEARCH_API_KEY" \
  "$AZURE_SEARCH_ENDPOINT/indexes/rag-navigator-index?api-version=2024-07-01"

# Reindex
curl -X POST http://localhost:5000/api/index/reindex
```

### Check Index Contents

```bash
# Count documents in the index
curl -H "api-key: $AZURE_SEARCH_API_KEY" \
  "$AZURE_SEARCH_ENDPOINT/indexes/rag-navigator-index/docs/\$count?api-version=2024-07-01"

# Search for a specific document
curl -H "api-key: $AZURE_SEARCH_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"search":"*","filter":"FileName eq '\''runbook-database-failover.md'\''","select":"ChunkId,Section,ChunkIndex"}' \
  "$AZURE_SEARCH_ENDPOINT/indexes/rag-navigator-index/docs/search?api-version=2024-07-01"
```

### Update a Single Document

The current implementation only supports full reindex. To update a single document:
1. Modify the file in the source folder.
2. Run a full reindex.

For production, implement incremental indexing based on file modification timestamps.
