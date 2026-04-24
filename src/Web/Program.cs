using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.RateLimiting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using RAGNavigator.Application.Observability;
using RAGNavigator.Application.Services;
using RAGNavigator.Infrastructure;
using RAGNavigator.Web.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Map environment variables to configuration sections so either appsettings.json
// or env vars can be used. This makes local dev and CI/CD flexible.
builder.Configuration.AddEnvironmentVariables();
MapEnvironmentVariables(builder.Configuration);

builder.Services.AddRazorPages();
builder.Services.AddRAGNavigatorServices(builder.Configuration);
AddTelemetryExport(builder.Services, builder.Configuration);

// --- Rate Limiting (per-IP) ---
builder.Services.AddRateLimiter(options =>
{
    // Chat endpoint: 20 requests per minute per IP
    options.AddPolicy("chat", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // Reindex endpoint: 3 requests per hour per IP
    options.AddPolicy("reindex", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

// --- Error Handling (before other middleware) ---
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(error => error.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "An internal error occurred." });
    }));

    app.UseHsts();
}

// --- Security Middleware Pipeline ---
app.UseHttpsRedirection();
app.UseSecurityHeaders();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter(); // After UseRouting so endpoint rate limit policies are visible
app.MapRazorPages();

// --- Configuration ---
var debugEnabled = app.Configuration.GetValue("Security:DebugModeEnabled", app.Environment.IsDevelopment());
var adminKey = app.Configuration.GetValue<string>("Security:AdminApiKey") ?? "";
var chatApiKey = app.Configuration.GetValue<string>("Security:ChatApiKey") ?? "";
var requireChatApiKey = app.Configuration.GetValue("Security:RequireChatApiKey", false);

// --- API Endpoints ---

app.MapPost("/api/chat", async (
    HttpContext httpContext,
    ChatRequest request,
    RagOrchestrator orchestrator,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    // CSRF: reject requests without JSON content type (HTML forms can't send application/json)
    if (!HasJsonContentType(httpContext))
        return Results.BadRequest(new { error = "Content-Type must be application/json." });

    var chatAuthResult = AuthorizeChatRequest(httpContext, logger, chatApiKey, requireChatApiKey);
    if (chatAuthResult is not null)
        return chatAuthResult;

    if (string.IsNullOrWhiteSpace(request.Question))
        return Results.BadRequest(new { error = "Question is required." });

    if (request.Question.Length > 2000)
        return Results.BadRequest(new { error = "Question must be 2000 characters or fewer." });

    // Sanitize input: strip control chars and detect prompt injection
    var sanitized = InputSanitizer.Sanitize(request.Question);

    if (sanitized.IsSuspicious)
    {
        logger.LogWarning(
            "Prompt injection detected from {IP}: {Patterns}",
            httpContext.Connection.RemoteIpAddress,
            string.Join(", ", sanitized.MatchedPatterns));
    }

    // Gate debug mode: disabled in production unless explicitly enabled
    var allowDebug = request.DebugMode && debugEnabled;

    var response = await orchestrator.AskAsync(
        sanitized.SanitizedInput,
        allowDebug,
        cancellationToken);

    // Strip system prompt from debug info (never expose to client)
    if (response.Debug is not null)
    {
        response = response with
        {
            Debug = response.Debug with
            {
                FullPrompt = StripSystemPrompt(response.Debug.FullPrompt)
            }
        };
    }

    return Results.Ok(response);
})
.RequireRateLimiting("chat");

app.MapPost("/api/index/reindex", async (
    HttpContext httpContext,
    DocumentProcessor processor,
    IConfiguration configuration,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    // Admin key protection: reindex is a privileged operation
    if (string.IsNullOrEmpty(adminKey))
    {
        if (!app.Environment.IsDevelopment())
        {
            logger.LogError("Reindex endpoint disabled because Security:AdminApiKey is not configured.");
            return Results.Json(
                new { error = "Reindex is not configured." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
    else
    {
        var providedKey = httpContext.Request.Headers["X-Admin-Key"].FirstOrDefault();
        if (!IsAdminKeyValid(providedKey, adminKey))
        {
            logger.LogWarning(
                "Unauthorized reindex attempt from {IP}",
                httpContext.Connection.RemoteIpAddress);
            return Results.Json(new { error = "Unauthorized." }, statusCode: StatusCodes.Status401Unauthorized);
        }
    }

    var repoRoot = FindRepoRoot();
    var folders = new List<string>();

    // Path validation: SampleDataPath must be an absolute path under known roots
    var sampleDataPath = configuration.GetValue<string>("SampleDataPath");
    if (!string.IsNullOrWhiteSpace(sampleDataPath))
    {
        var resolvedPath = Path.GetFullPath(sampleDataPath);

        // Prevent path traversal: only allow paths under repo root or known data dirs
        if (repoRoot is not null && !resolvedPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("SampleDataPath traversal blocked: {Path}", resolvedPath);
            return Results.BadRequest(new { error = "Invalid SampleDataPath." });
        }

        folders.Add(resolvedPath);
    }
    else if (repoRoot is not null)
    {
        folders.Add(Path.Combine(repoRoot, "sample-data"));
    }

    // Architecture docs (indexed as part of the RAG corpus — see ADR-004)
    if (repoRoot is not null)
    {
        var archDocsPath = Path.Combine(repoRoot, "docs", "architecture");
        if (Directory.Exists(archDocsPath))
            folders.Add(archDocsPath);
    }

    if (folders.Count == 0)
        return Results.BadRequest(new { error = "No document folders found. Set SampleDataPath or run from the repo directory." });

    var summary = await processor.IngestDocumentsAsync(folders, cancellationToken);

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

app.MapGet("/api/index/documents", async (
    RAGNavigator.Application.Interfaces.ISearchIndexService indexService,
    CancellationToken cancellationToken) =>
{
    var documents = await indexService.GetIndexedDocumentsAsync(cancellationToken);
    return Results.Ok(documents);
});

app.Run();

// --- Helpers ---

static bool HasJsonContentType(HttpContext context)
{
    var contentType = context.Request.ContentType;
    return contentType is not null &&
           contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);
}

static bool IsAdminKeyValid(string? providedKey, string configuredKey)
{
    return IsFixedTimeSecretValid(providedKey, configuredKey);
}

static IResult? AuthorizeChatRequest(
    HttpContext context,
    ILogger logger,
    string configuredKey,
    bool requireKey)
{
    if (string.IsNullOrEmpty(configuredKey))
    {
        if (!requireKey)
            return null;

        logger.LogError("Chat endpoint disabled because Security:ChatApiKey is required but not configured.");
        return Results.Json(
            new { error = "Chat authentication is not configured." },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var providedKey = context.Request.Headers["X-Chat-Key"].FirstOrDefault();
    if (IsFixedTimeSecretValid(providedKey, configuredKey))
        return null;

    logger.LogWarning(
        "Unauthorized chat attempt from {IP}",
        context.Connection.RemoteIpAddress);

    return Results.Json(new { error = "Unauthorized." }, statusCode: StatusCodes.Status401Unauthorized);
}

static bool IsFixedTimeSecretValid(string? providedKey, string configuredKey)
{
    if (string.IsNullOrEmpty(providedKey) || string.IsNullOrEmpty(configuredKey))
        return false;

    var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));
    var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));

    return CryptographicOperations.FixedTimeEquals(providedHash, configuredHash);
}

static string StripSystemPrompt(string fullPrompt)
{
    // The full prompt contains system + user prompt. Only return the user prompt portion.
    const string marker = "## Retrieved Context";
    var idx = fullPrompt.IndexOf(marker, StringComparison.Ordinal);
    return idx >= 0 ? fullPrompt[idx..] : "[System prompt hidden]";
}

static void MapEnvironmentVariables(ConfigurationManager config)
{
    // Allow flat env vars like AZURE_OPENAI_ENDPOINT to map into structured config sections.
    // This pattern is common for Azure apps using App Service / Container Apps configuration.
    var envMappings = new Dictionary<string, string>
    {
        ["AZURE_OPENAI_ENDPOINT"] = "AzureOpenAI:Endpoint",
        ["AZURE_OPENAI_CHAT_DEPLOYMENT"] = "AzureOpenAI:ChatDeployment",
        ["AZURE_OPENAI_EMBEDDING_DEPLOYMENT"] = "AzureOpenAI:EmbeddingDeployment",
        ["AZURE_OPENAI_EMBEDDING_DIMENSIONS"] = "AzureOpenAI:EmbeddingDimensions",
        ["AZURE_OPENAI_API_KEY"] = "AzureOpenAI:ApiKey",
        ["AZURE_SEARCH_ENDPOINT"] = "AzureSearch:Endpoint",
        ["AZURE_SEARCH_INDEX_NAME"] = "AzureSearch:IndexName",
        ["AZURE_SEARCH_API_KEY"] = "AzureSearch:ApiKey",
        ["RAG_TOP_K"] = "Rag:TopK",
        ["RAG_MINIMUM_RELEVANCE_SCORE"] = "Rag:MinimumRelevanceScore",
        ["ADMIN_API_KEY"] = "Security:AdminApiKey",
        ["CHAT_API_KEY"] = "Security:ChatApiKey",
        ["REQUIRE_CHAT_API_KEY"] = "Security:RequireChatApiKey",
        ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "Observability:ApplicationInsightsConnectionString"
    };

    foreach (var (envVar, configKey) in envMappings)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrEmpty(value))
            config[configKey] = value;
    }
}

static void AddTelemetryExport(IServiceCollection services, IConfiguration configuration)
{
    var connectionString = configuration.GetValue<string>("Observability:ApplicationInsightsConnectionString");
    if (string.IsNullOrWhiteSpace(connectionString))
        return;

    services.AddOpenTelemetry()
        .UseAzureMonitor(options => options.ConnectionString = connectionString);

    services.ConfigureOpenTelemetryMeterProvider((_, metrics) =>
    {
        metrics.AddMeter(RagTelemetry.MeterName);
    });

    services.ConfigureOpenTelemetryTracerProvider((_, tracing) =>
    {
        tracing.AddSource(RagTelemetry.ActivitySourceName);
    });
}

static string? FindRepoRoot()
{
    // Walk up from the working directory to find the repo root (contains RAGNavigator.sln).
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "RAGNavigator.sln")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return null;
}

public record ChatRequest(string Question, bool DebugMode = false);

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }
