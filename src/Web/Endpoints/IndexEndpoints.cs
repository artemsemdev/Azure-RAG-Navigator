using System.Security.Cryptography;
using System.Text;
using RAGNavigator.Application.Interfaces;
using RAGNavigator.Application.Services;
using RAGNavigator.Web.Configuration;
using RAGNavigator.Web.Services;

namespace RAGNavigator.Web.Endpoints;

public static class IndexEndpoints
{
    public static void MapIndexEndpoints(this WebApplication app, EndpointAuthOptions endpointAuth)
    {
        var adminKey = app.Configuration.GetValue<string>("Security:AdminApiKey") ?? "";

        var reindexEndpoint = app.MapPost("/api/index/reindex", async (
            HttpContext httpContext,
            DocumentProcessor processor,
            DocumentFolderResolver folderResolver,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (!endpointAuth.UseBearer)
            {
                var authResult = AuthorizeReindexRequest(httpContext, logger, adminKey, app.Environment);
                if (authResult is not null)
                    return authResult;
            }

            var resolution = folderResolver.ResolveFolders();
            if (resolution.Error is not null)
                return Results.BadRequest(new { error = resolution.Error });

            var summary = await processor.IngestDocumentsAsync(resolution.Folders, cancellationToken);

            return Results.Ok(new
            {
                message = "Indexing complete.",
                chunksIndexed = summary.ChunksIndexed,
                filesFound = summary.FilesFound,
                filesProcessed = summary.FilesProcessed,
                filesFailed = summary.FilesFailed
            });
        })
        .RequireRateLimiting("reindex");

        if (endpointAuth.UseBearer)
            reindexEndpoint.RequireAuthorization("reindex-admin");

        app.MapGet("/api/index/documents", async (
            ISearchIndexService indexService,
            CancellationToken cancellationToken) =>
        {
            var documents = await indexService.GetIndexedDocumentsAsync(cancellationToken);
            return Results.Ok(documents);
        });
    }

    private static IResult? AuthorizeReindexRequest(
        HttpContext context,
        ILogger logger,
        string configuredKey,
        IWebHostEnvironment environment)
    {
        if (string.IsNullOrEmpty(configuredKey))
        {
            if (environment.IsDevelopment())
                return null;

            logger.LogError("Reindex endpoint disabled because Security:AdminApiKey is not configured.");
            return Results.Json(
                new { error = "Reindex is not configured." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var providedKey = context.Request.Headers["X-Admin-Key"].FirstOrDefault();
        if (IsFixedTimeSecretValid(providedKey, configuredKey))
            return null;

        logger.LogWarning(
            "Unauthorized reindex attempt from {IP}",
            context.Connection.RemoteIpAddress);
        return Results.Json(new { error = "Unauthorized." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    private static bool IsFixedTimeSecretValid(string? providedKey, string configuredKey)
    {
        if (string.IsNullOrEmpty(providedKey) || string.IsNullOrEmpty(configuredKey))
            return false;

        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));

        return CryptographicOperations.FixedTimeEquals(providedHash, configuredHash);
    }
}
