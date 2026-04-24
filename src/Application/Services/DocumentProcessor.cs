using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RAGNavigator.Application.Interfaces;
using RAGNavigator.Application.Models;
using RAGNavigator.Application.Observability;

namespace RAGNavigator.Application.Services;

/// <summary>
/// Orchestrates the document ingestion pipeline:
/// read files → chunk → embed → index.
/// </summary>
public sealed class DocumentProcessor
{
    private readonly IDocumentChunker _chunker;
    private readonly IEmbeddingService _embeddingService;
    private readonly ISearchIndexService _indexService;
    private readonly ILogger<DocumentProcessor> _logger;

    // Azure OpenAI embeddings API supports batches of up to 16 inputs
    private const int EmbeddingBatchSize = 16;

    public DocumentProcessor(
        IDocumentChunker chunker,
        IEmbeddingService embeddingService,
        ISearchIndexService indexService,
        ILogger<DocumentProcessor> logger)
    {
        _chunker = chunker;
        _embeddingService = embeddingService;
        _indexService = indexService;
        _logger = logger;
    }

    public async Task<IngestionSummary> IngestDocumentsAsync(
        IReadOnlyList<string> folderPaths, CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        using var activity = RagTelemetry.StartActivity("rag.ingestion");

        activity?.SetTag("rag.folders_requested", folderPaths.Count);

        try
        {
            var files = new List<string>();
            foreach (var folderPath in folderPaths)
            {
                if (!Directory.Exists(folderPath))
                {
                    _logger.LogWarning("Folder not found, skipping: {FolderPath}", folderPath);
                    continue;
                }

                _logger.LogInformation("Scanning {FolderPath}", folderPath);
                files.AddRange(Directory.GetFiles(folderPath, "*.md"));
                files.AddRange(Directory.GetFiles(folderPath, "*.txt"));
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);

            if (files.Count == 0)
            {
                _logger.LogWarning("No .md or .txt files found in any configured folder");
                var emptySummary = new IngestionSummary
                {
                    FoldersRequested = folderPaths.Count,
                    FilesFound = 0,
                    FilesProcessed = 0,
                    ChunksIndexed = 0,
                    FilesFailed = 0
                };

                totalStopwatch.Stop();
                RagTelemetry.RecordIngestion(emptySummary, totalStopwatch.Elapsed.TotalMilliseconds);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return emptySummary;
            }

            activity?.SetTag("rag.files_found", files.Count);

            _logger.LogInformation("Found {FileCount} files to process across {FolderCount} folders",
                files.Count, folderPaths.Count);

            // Clear existing documents before re-indexing, then ensure the index
            // exists before upload. The current infrastructure implementation clears
            // by deleting the index, so create/update must happen after clearing.
            await _indexService.DeleteAllDocumentsAsync(cancellationToken);
            await _indexService.CreateOrUpdateIndexAsync(cancellationToken);

            var allChunks = new List<DocumentChunk>();

            var filesProcessed = 0;
            var filesFailed = 0;

            foreach (var filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileName = Path.GetFileName(filePath);
                try
                {
                    var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                    var title = ExtractTitle(content, fileName);

                    _logger.LogInformation("Chunking {FileName} ({Length} chars)", fileName, content.Length);

                    var chunks = _chunker.Chunk(content, fileName, title);
                    allChunks.AddRange(chunks);
                    filesProcessed++;

                    _logger.LogInformation("Produced {ChunkCount} chunks from {FileName}", chunks.Count, fileName);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    filesFailed++;
                    _logger.LogError(ex, "Failed to process {FileName}; skipping file", fileName);
                }
            }

            // Generate embeddings in batches
            _logger.LogInformation("Generating embeddings for {ChunkCount} chunks", allChunks.Count);

            for (var i = 0; i < allChunks.Count; i += EmbeddingBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = allChunks.Skip(i).Take(EmbeddingBatchSize).ToList();
                var texts = batch.Select(c => c.Content).ToList();

                var embeddings = await _embeddingService.GenerateEmbeddingsAsync(texts, cancellationToken);

                if (embeddings.Count != batch.Count)
                    throw new InvalidOperationException(
                        $"Embedding API returned {embeddings.Count} results for {batch.Count} inputs. " +
                        "Some inputs may have been filtered or rejected.");

                for (var j = 0; j < batch.Count; j++)
                {
                    batch[j].Embedding = embeddings[j];
                }

                _logger.LogInformation("Generated embeddings for batch {BatchStart}-{BatchEnd} of {Total}",
                    i + 1, Math.Min(i + EmbeddingBatchSize, allChunks.Count), allChunks.Count);
            }

            // Upload to search index
            if (allChunks.Count > 0)
            {
                _logger.LogInformation("Uploading {ChunkCount} chunks to search index", allChunks.Count);
                await _indexService.UploadChunksAsync(allChunks, cancellationToken);
            }
            else
            {
                _logger.LogWarning("No chunks produced from {FileCount} discovered files", files.Count);
            }

            _logger.LogInformation("Ingestion complete. {ChunkCount} chunks indexed from {FileCount} files",
                allChunks.Count, files.Count);

            var summary = new IngestionSummary
            {
                FoldersRequested = folderPaths.Count,
                FilesFound = files.Count,
                FilesProcessed = filesProcessed,
                ChunksIndexed = allChunks.Count,
                FilesFailed = filesFailed
            };

            totalStopwatch.Stop();
            RagTelemetry.RecordIngestion(summary, totalStopwatch.Elapsed.TotalMilliseconds);
            activity?.SetTag("rag.files_processed", summary.FilesProcessed);
            activity?.SetTag("rag.chunks_indexed", summary.ChunksIndexed);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return summary;
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            RagTelemetry.RecordIngestionError(totalStopwatch.Elapsed.TotalMilliseconds, ex.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            throw;
        }
    }

    private static string ExtractTitle(string content, string fileName)
    {
        // Try to extract title from first # heading
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("# ") && !trimmed.StartsWith("## "))
                return trimmed[2..].Trim();
        }

        // Fall back to file name without extension
        return Path.GetFileNameWithoutExtension(fileName)
            .Replace('-', ' ')
            .Replace('_', ' ');
    }
}
