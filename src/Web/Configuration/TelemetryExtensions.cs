using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using RAGNavigator.Application.Observability;

namespace RAGNavigator.Web.Configuration;

public static class TelemetryExtensions
{
    public static IServiceCollection AddTelemetryExport(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetValue<string>("Observability:ApplicationInsightsConnectionString");
        if (string.IsNullOrWhiteSpace(connectionString))
            return services;

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

        return services;
    }
}
