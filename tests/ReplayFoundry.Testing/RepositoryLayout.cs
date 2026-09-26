namespace ReplayFoundry.Testing;

public static class RepositoryLayout
{
    private static readonly Lazy<string> RootDirectory = new(FindRoot);

    public static string Root => RootDirectory.Value;

    public static string GetPath(params string[] relativeSegments) =>
        Path.GetFullPath(Path.Combine([Root, .. relativeSegments]));

    public static string DesktopPath(params string[] relativeSegments)
    {
        string[] path = ["src", "ReplayFoundry.Desktop", .. relativeSegments];
        return GetPath(path);
    }

    public static string VisualSemanticHostPath(
        params string[] relativeSegments)
    {
        string[] path =
            ["src", "ReplayFoundry.VisualSemanticHost", .. relativeSegments];
        return GetPath(path);
    }

    private static string FindRoot()
    {
        foreach (string start in CandidateRoots())
        {
            string? root = FindRootFrom(start);
            if (root is not null)
            {
                return root;
            }
        }

        throw new DirectoryNotFoundException(
            "The ReplayFoundry repository root could not be located.");
    }

    private static IEnumerable<string> CandidateRoots()
    {
        yield return Environment.CurrentDirectory;
        yield return AppContext.BaseDirectory;
    }

    private static string? FindRootFrom(string start)
    {
        for (var directory = new DirectoryInfo(start);
             directory is not null;
             directory = directory.Parent)
        {
            if (IsRepositoryRoot(directory.FullName))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static bool IsRepositoryRoot(string path) =>
        File.Exists(Path.Combine(path, "ReplayFoundry.slnx")) &&
        Directory.Exists(Path.Combine(path, "src")) &&
        Directory.Exists(Path.Combine(path, "tools")) &&
        Directory.Exists(Path.Combine(path, "tests"));
}
