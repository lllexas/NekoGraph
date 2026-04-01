using System.Text.Json;

namespace NekoGraph.Cli;

internal static class RunFullCommand
{
    public static int Execute(string packId)
    {
        try
        {
            var repositoryRoot = RepositoryLocator.FindRoot(Environment.CurrentDirectory);
            if (repositoryRoot is null)
            {
                Console.Error.WriteLine("Unable to locate repository root from current directory.");
                return 1;
            }

            var resolution = PackResolver.Resolve(repositoryRoot, packId);
            if (!resolution.Success)
            {
                Console.Error.WriteLine(resolution.ErrorMessage);
                return 1;
            }

            var pack = PackDocumentLoader.Load(resolution.FilePath!, packId);
            if (!pack.Success)
            {
                Console.Error.WriteLine(pack.ErrorMessage);
                return 1;
            }

            var report = RunFullReportBuilder.Build(pack.Value!);
            var json = JsonSerializer.Serialize(report, JsonOptions.Default);
            Console.Out.WriteLine(json);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"run full failed: {ex.Message}");
            return 1;
        }
    }
}
