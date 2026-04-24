using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RAGNavigator.Application.Configuration;
using RAGNavigator.Application.Interfaces;
using RAGNavigator.Application.Services;
using RAGNavigator.Infrastructure.AI;
using RAGNavigator.Infrastructure.Configuration;
using RAGNavigator.Infrastructure.Search;

namespace RAGNavigator.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRAGNavigatorServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind configuration sections
        services.AddOptions<AzureOpenAIOptions>()
            .Bind(configuration.GetSection(AzureOpenAIOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                o => o.EmbeddingDimensions == SearchIndexDocument.ContentVectorDimensions,
                $"AzureOpenAI:EmbeddingDimensions must be {SearchIndexDocument.ContentVectorDimensions} " +
                "to match the Azure AI Search ContentVector field.")
            .ValidateOnStart();

        services.AddOptions<AzureSearchOptions>()
            .Bind(configuration.GetSection(AzureSearchOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RagOptions>()
            .Bind(configuration.GetSection(RagOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<RagOptions>>().Value);

        // Azure OpenAI client
        // Uses API key if provided, otherwise falls back to DefaultAzureCredential.
        // In production on Azure, managed identity is preferred — just deploy without
        // setting the ApiKey and ensure the app's identity has
        // "Cognitive Services OpenAI User" role on the Azure OpenAI resource.
        services.AddSingleton<AzureOpenAIClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AzureOpenAIOptions>>().Value;
            var endpoint = new Uri(options.Endpoint);

            if (!string.IsNullOrEmpty(options.ApiKey))
                return new AzureOpenAIClient(endpoint, new AzureKeyCredential(options.ApiKey));

            return new AzureOpenAIClient(endpoint, new DefaultAzureCredential());
        });

        // Azure AI Search clients
        // Same pattern: API key for local dev, DefaultAzureCredential for production.
        // The app identity needs "Search Index Data Contributor" role on the Search resource.
        services.AddSingleton<SearchIndexClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AzureSearchOptions>>().Value;
            var endpoint = new Uri(options.Endpoint);

            if (!string.IsNullOrEmpty(options.ApiKey))
                return new SearchIndexClient(endpoint, new AzureKeyCredential(options.ApiKey));

            return new SearchIndexClient(endpoint, new DefaultAzureCredential());
        });

        services.AddSingleton<SearchClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AzureSearchOptions>>().Value;
            var endpoint = new Uri(options.Endpoint);

            if (!string.IsNullOrEmpty(options.ApiKey))
                return new SearchClient(endpoint, options.IndexName, new AzureKeyCredential(options.ApiKey));

            return new SearchClient(endpoint, options.IndexName, new DefaultAzureCredential());
        });

        // Application services
        services.AddSingleton<IDocumentChunker, MarkdownDocumentChunker>();
        services.AddSingleton<IEmbeddingService, AzureOpenAIEmbeddingService>();
        services.AddSingleton<IChatCompletionService, AzureOpenAIChatService>();
        services.AddSingleton<ISearchIndexService, AzureSearchIndexService>();
        services.AddSingleton<IRetrievalService, AzureSearchRetrievalService>();

        // Orchestrators
        services.AddSingleton<DocumentProcessor>();
        services.AddSingleton<RagOrchestrator>();

        return services;
    }
}
