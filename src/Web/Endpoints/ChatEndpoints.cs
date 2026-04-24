using System.Security.Cryptography;
using System.Text;
using RAGNavigator.Application.Services;
using RAGNavigator.Web.Configuration;

namespace RAGNavigator.Web.Endpoints;

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this WebApplication app, EndpointAuthOptions endpointAuth)
    {
        var debugEnabled = app.Configuration.GetValue("Security:DebugModeEnabled", app.Environment.IsDevelopment());
        var chatApiKey = app.Configuration.GetValue<string>("Security:ChatApiKey") ?? "";
        var requireChatApiKey = app.Configuration.GetValue("Security:RequireChatApiKey", false);

        var chatEndpoint = app.MapPost("/api/chat", async (
            HttpContext httpContext,
            ChatRequest request,
            RagOrchestrator orchestrator,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (!HasJsonContentType(httpContext))
                return Results.BadRequest(new { error = "Content-Type must be application/json." });

            var chatAuthResult = AuthorizeChatRequest(httpContext, logger, chatApiKey, requireChatApiKey);
            if (chatAuthResult is not null)
                return chatAuthResult;

            if (string.IsNullOrWhiteSpace(request.Question))
                return Results.BadRequest(new { error = "Question is required." });

            if (request.Question.Length > 2000)
                return Results.BadRequest(new { error = "Question must be 2000 characters or fewer." });

            var sanitized = InputSanitizer.Sanitize(request.Question);

            if (sanitized.IsSuspicious)
            {
                logger.LogWarning(
                    "Prompt injection detected from {IP}: {Patterns}",
                    httpContext.Connection.RemoteIpAddress,
                    string.Join(", ", sanitized.MatchedPatterns));
            }

            var allowDebug = request.DebugMode && debugEnabled;

            var response = await orchestrator.AskAsync(
                sanitized.SanitizedInput,
                allowDebug,
                cancellationToken);

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

        if (endpointAuth.UseBearer)
            chatEndpoint.RequireAuthorization("chat-user");
    }

    private static bool HasJsonContentType(HttpContext context)
    {
        var contentType = context.Request.ContentType;
        return contentType is not null &&
               contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static IResult? AuthorizeChatRequest(
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

    private static bool IsFixedTimeSecretValid(string? providedKey, string configuredKey)
    {
        if (string.IsNullOrEmpty(providedKey) || string.IsNullOrEmpty(configuredKey))
            return false;

        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));

        return CryptographicOperations.FixedTimeEquals(providedHash, configuredHash);
    }

    private static string StripSystemPrompt(string fullPrompt)
    {
        const string marker = "## Retrieved Context";
        var idx = fullPrompt.IndexOf(marker, StringComparison.Ordinal);
        return idx >= 0 ? fullPrompt[idx..] : "[System prompt hidden]";
    }
}

public record ChatRequest(string Question, bool DebugMode = false);
