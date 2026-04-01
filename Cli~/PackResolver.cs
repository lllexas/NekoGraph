using System.Text.Json.Nodes;

namespace NekoGraph.Cli;

internal sealed class PackResolutionResult
{
    public bool Success { get; init; }

    public string? FilePath { get; init; }

    public string? ErrorMessage { get; init; }
}

internal static class PackResolver
{
    private static readonly string[] PackExtensions = [".json", ".txt", ".bytes"];

    public static PackResolutionResult Resolve(string repositoryRoot, string packId)
    {
        var metaResolved = ResolveFromMetaLib(repositoryRoot, packId);
        if (metaResolved.Success)
        {
            return metaResolved;
        }

        var fallback = ResolveByPackId(repositoryRoot, packId);
        if (fallback.Success)
        {
            return fallback;
        }

        return new PackResolutionResult
        {
            ErrorMessage =
                $"Pack '{packId}' was not found. MetaLib lookup failed and no matching pack file was found by PackID."
        };
    }

    private static PackResolutionResult ResolveFromMetaLib(string repositoryRoot, string packId)
    {
        var metaPath = Path.Combine(repositoryRoot, "Assets", "Resources", "NekoGraph", "MetaLib.json");
        if (!File.Exists(metaPath))
        {
            return new PackResolutionResult { ErrorMessage = "MetaLib.json not found." };
        }

        try
        {
            var metaNode = JsonNode.Parse(File.ReadAllText(metaPath))?.AsObject();
            if (metaNode is null || !metaNode.TryGetPropertyValue(packId, out var entryNode) || entryNode is not JsonObject entry)
            {
                return new PackResolutionResult { ErrorMessage = $"Pack '{packId}' not registered in MetaLib." };
            }

            var storage = entry["Storage"]?.GetValue<string>() ?? "Resources";
            var resourcePath = entry["ResourcePath"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                return new PackResolutionResult { ErrorMessage = $"Pack '{packId}' has no ResourcePath in MetaLib." };
            }

            var filePath = ResolveResourcePath(repositoryRoot, storage, resourcePath);
            if (filePath is null)
            {
                return new PackResolutionResult
                {
                    ErrorMessage = $"Pack '{packId}' is registered but the resource file was not found for '{resourcePath}'."
                };
            }

            return new PackResolutionResult { Success = true, FilePath = filePath };
        }
        catch (Exception ex)
        {
            return new PackResolutionResult { ErrorMessage = $"MetaLib parse failed: {ex.Message}" };
        }
    }

    private static string? ResolveResourcePath(string repositoryRoot, string storage, string resourcePath)
    {
        if (string.Equals(storage, "StreamingAssets", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var extension in PackExtensions)
            {
                var candidate = Path.Combine(repositoryRoot, "Assets", "StreamingAssets", resourcePath + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        else
        {
            foreach (var extension in PackExtensions)
            {
                var candidate = Path.Combine(repositoryRoot, "Assets", "Resources", resourcePath + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static PackResolutionResult ResolveByPackId(string repositoryRoot, string packId)
    {
        var candidates = Directory
            .EnumerateFiles(repositoryRoot, "*", SearchOption.AllDirectories)
            .Where(path => PackExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path =>
                string.Equals(Path.GetFileNameWithoutExtension(path), packId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 1)
        {
            return new PackResolutionResult { Success = true, FilePath = candidates[0] };
        }

        if (candidates.Count > 1)
        {
            return new PackResolutionResult
            {
                ErrorMessage = $"Pack '{packId}' matched multiple files: {string.Join(", ", candidates)}"
            };
        }

        return new PackResolutionResult { ErrorMessage = $"No file matched PackID '{packId}'." };
    }
}
