namespace RAGNavigator.Web.Services;

public sealed class DocumentFolderResolver
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DocumentFolderResolver> _logger;

    public DocumentFolderResolver(
        IConfiguration configuration,
        ILogger<DocumentFolderResolver> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public DocumentFolderResolution ResolveFolders()
    {
        var repoRoot = FindRepoRoot();
        var folders = new List<string>();

        var sampleDataPath = _configuration.GetValue<string>("SampleDataPath");
        if (!string.IsNullOrWhiteSpace(sampleDataPath))
        {
            var resolvedPath = Path.GetFullPath(sampleDataPath);

            if (repoRoot is not null && !resolvedPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("SampleDataPath traversal blocked: {Path}", resolvedPath);
                return DocumentFolderResolution.Failed("Invalid SampleDataPath.");
            }

            folders.Add(resolvedPath);
        }
        else if (repoRoot is not null)
        {
            folders.Add(Path.Combine(repoRoot, "sample-data"));
        }

        if (repoRoot is not null)
        {
            var archDocsPath = Path.Combine(repoRoot, "docs", "architecture");
            if (Directory.Exists(archDocsPath))
                folders.Add(archDocsPath);
        }

        return folders.Count == 0
            ? DocumentFolderResolution.Failed("No document folders found. Set SampleDataPath or run from the repo directory.")
            : DocumentFolderResolution.Succeeded(folders);
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RAGNavigator.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }
}

public sealed record DocumentFolderResolution(IReadOnlyList<string> Folders, string? Error)
{
    public static DocumentFolderResolution Succeeded(IReadOnlyList<string> folders) =>
        new(folders, Error: null);

    public static DocumentFolderResolution Failed(string error) =>
        new([], error);
}
