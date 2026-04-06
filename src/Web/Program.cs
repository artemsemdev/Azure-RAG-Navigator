using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
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
    if (!string.IsNullOrEmpty(adminKey))
    {
        var providedKey = httpContext.Request.Headers["X-Admin-Key"].FirstOrDefault();
        if (providedKey != adminKey)
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

    var chunkCount = await processor.IngestDocumentsAsync(folders, cancellationToken);

    return Results.Ok(new { message = "Indexing complete.", chunksIndexed = chunkCount });
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
        ["AZURE_OPENAI_API_KEY"] = "AzureOpenAI:ApiKey",
        ["AZURE_SEARCH_ENDPOINT"] = "AzureSearch:Endpoint",
        ["AZURE_SEARCH_INDEX_NAME"] = "AzureSearch:IndexName",
        ["AZURE_SEARCH_API_KEY"] = "AzureSearch:ApiKey",
        ["ADMIN_API_KEY"] = "Security:AdminApiKey"
    };

    foreach (var (envVar, configKey) in envMappings)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrEmpty(value))
            config[configKey] = value;
    }
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
