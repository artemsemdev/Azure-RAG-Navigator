using RAGNavigator.Infrastructure;
using RAGNavigator.Web.Configuration;
using RAGNavigator.Web.Endpoints;
using RAGNavigator.Web.Middleware;
using RAGNavigator.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddMappedEnvironmentVariables();
var endpointAuth = builder.Configuration.BindEndpointAuthOptions();

builder.Services.AddRazorPages();
builder.Services.AddRAGNavigatorServices(builder.Configuration);
builder.Services.AddSingleton<DocumentFolderResolver>();
builder.Services.AddEndpointAuthentication(endpointAuth, builder.Environment);
builder.Services.AddTelemetryExport(builder.Configuration);
builder.Services.AddEndpointRateLimiting();

var app = builder.Build();

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

app.UseHttpsRedirection();
app.UseSecurityHeaders();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();

if (endpointAuth.UseBearer)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapRazorPages();
app.MapChatEndpoints(endpointAuth);
app.MapIndexEndpoints(endpointAuth);

app.Run();

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }
