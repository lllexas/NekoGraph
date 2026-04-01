namespace NekoGraph.Cli;

internal static class RepositoryLocator
{
    public static string? FindRoot(string startDirectory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));

        while (current is not null)
        {
            var gitPath = Path.Combine(current.FullName, ".git");
            var assetsPath = Path.Combine(current.FullName, "Assets");

            if (Directory.Exists(gitPath) && Directory.Exists(assetsPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
