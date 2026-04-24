using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RAGNavigator.Application.Models;
using RAGNavigator.Infrastructure.Configuration;
using RAGNavigator.Infrastructure.Search;
using Xunit;

namespace RAGNavigator.Tests;

public class AzureSearchIndexServiceTests
{
    [Fact]
    public async Task UploadChunksAsync_EmbeddingDimensionMismatch_ThrowsBeforeUpload()
    {
        var service = new AzureSearchIndexService(
            indexClient: null!,
            searchClient: null!,
            options: Options.Create(new AzureSearchOptions
            {
                Endpoint = "https://fake.search.windows.net",
                IndexName = "test-index"
            }),
            logger: Substitute.For<ILogger<AzureSearchIndexService>>());

        var chunk = new DocumentChunk
        {
            ChunkId = "doc_chunk_0",
            DocumentId = "doc",
            DocumentTitle = "Doc",
            FileName = "doc.md",
            Section = "Section",
            ChunkIndex = 0,
            Content = "Content",
            Embedding = new float[] { 0.1f, 0.2f, 0.3f }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UploadChunksAsync([chunk]));

        Assert.Contains("embedding dimension 3", ex.Message);
        Assert.Contains(SearchIndexDocument.ContentVectorDimensions.ToString(), ex.Message);
    }
}
