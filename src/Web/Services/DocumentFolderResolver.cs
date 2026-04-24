namespace RAGNavigator.Web.Services;

public sealed class DocumentFolderResolver
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DocumentFolderResolver> _logger;
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

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

            if (repoRoot is not null && !IsPathWithinDirectory(resolvedPath, repoRoot))
            {
                _logger.LogWarning("SampleDataPath traversal blocked: {Path}", resolvedPath);
                return DocumentFolderResolution.Failed("Invalid SampleDataPath.");
            }

            folders.Add(resolvedPath);
        }
        else if (repoRoot is not null)
        {
            folders.Add(Path.Join(repoRoot, "sample-data"));
        }

        if (repoRoot is not null)
        {
            var archDocsPath = Path.Join(repoRoot, "docs", "architecture");
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
            if (File.Exists(Path.Join(dir.FullName, "RAGNavigator.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }

    private static bool IsPathWithinDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory);
        var relativePath = Path.GetRelativePath(fullDirectory, fullPath);

        return relativePath == "." ||
            (!relativePath.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison) &&
             !relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, PathComparison) &&
             !string.Equals(relativePath, "..", PathComparison) &&
             !Path.IsPathRooted(relativePath));
    }
}

public sealed record DocumentFolderResolution(IReadOnlyList<string> Folders, string? Error)
{
    public static DocumentFolderResolution Succeeded(IReadOnlyList<string> folders) =>
        new(folders, Error: null);

    public static DocumentFolderResolution Failed(string error) =>
        new([], error);
}
