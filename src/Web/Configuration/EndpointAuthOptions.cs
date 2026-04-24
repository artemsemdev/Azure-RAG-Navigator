namespace RAGNavigator.Web.Configuration;

public sealed record EndpointAuthOptions(
    string Mode,
    string? Authority,
    string? Audience,
    IReadOnlyList<string> AdminRoles)
{
    public bool UseBearer => string.Equals(Mode, "Bearer", StringComparison.OrdinalIgnoreCase);
}
