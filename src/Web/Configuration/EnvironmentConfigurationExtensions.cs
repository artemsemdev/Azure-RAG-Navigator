namespace RAGNavigator.Web.Configuration;

public static class EnvironmentConfigurationExtensions
{
    public static void AddMappedEnvironmentVariables(this ConfigurationManager config)
    {
        config.AddEnvironmentVariables();

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
            ["AUTH_MODE"] = "Security:AuthMode",
            ["JWT_AUTHORITY"] = "Security:Jwt:Authority",
            ["JWT_AUDIENCE"] = "Security:Jwt:Audience",
            ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "Observability:ApplicationInsightsConnectionString"
        };

        foreach (var (envVar, configKey) in envMappings)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(value))
                config[configKey] = value;
        }
    }
}
