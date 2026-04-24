using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace RAGNavigator.Web.Configuration;

public static class EndpointAuthExtensions
{
    public static EndpointAuthOptions BindEndpointAuthOptions(this IConfiguration configuration)
    {
        var mode = configuration.GetValue("Security:AuthMode", "ApiKey");
        if (!string.Equals(mode, "ApiKey", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(mode, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Security:AuthMode must be either 'ApiKey' or 'Bearer'.");
        }

        var options = new EndpointAuthOptions(
            Mode: mode,
            Authority: configuration.GetValue<string>("Security:Jwt:Authority"),
            Audience: configuration.GetValue<string>("Security:Jwt:Audience"),
            AdminRoles: configuration.GetSection("Security:Jwt:AdminRoles").Get<string[]>() ?? ["RAGNavigator.Admin"]);

        if (options.UseBearer &&
            (string.IsNullOrWhiteSpace(options.Authority) || string.IsNullOrWhiteSpace(options.Audience)))
        {
            throw new InvalidOperationException(
                "Security:Jwt:Authority and Security:Jwt:Audience are required when Security:AuthMode is 'Bearer'.");
        }

        return options;
    }

    public static IServiceCollection AddEndpointAuthentication(
        this IServiceCollection services,
        EndpointAuthOptions options,
        IWebHostEnvironment environment)
    {
        if (!options.UseBearer)
            return services;

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.Authority = options.Authority;
                jwt.Audience = options.Audience;
                jwt.RequireHttpsMetadata = !environment.IsDevelopment();
            });

        services.AddAuthorization(auth =>
        {
            auth.AddPolicy("chat-user", policy => policy.RequireAuthenticatedUser());
            auth.AddPolicy("reindex-admin", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole(options.AdminRoles);
            });
        });

        return services;
    }
}
