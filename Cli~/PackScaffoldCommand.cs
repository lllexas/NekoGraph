using System.Text.Json;
using System.Text.Json.Nodes;

namespace NekoGraph.Cli;

internal static class PackScaffoldCommand
{
    private const string DefaultPackFolder = "NekoGraph/Packs";
    private static readonly string[] PackExtensions = [".json", ".txt", ".bytes"];

    public static int ExecuteCreate(string packId, string? storageName, string? resourcePath)
    {
        try
        {
            if (!TryGetRepositoryRoot(out var repositoryRoot, out var rootError))
            {
                Console.Error.WriteLine(rootError);
                return 1;
            }

            if (!TryNormalizePackId(packId, out var normalizedPackId, out var packIdError))
            {
                Console.Error.WriteLine(packIdError);
                return 1;
            }

            if (!TryParseStorage(storageName, out var storage, out var storageError))
            {
                Console.Error.WriteLine(storageError);
                return 1;
            }

            var effectiveResourcePath = NormalizeResourcePath(resourcePath, normalizedPackId);
            var targetFilePath = BuildPackFilePath(repositoryRoot, storage, effectiveResourcePath);
            var meta = LoadMetaLib(repositoryRoot);

            if (HasMetaId(meta, normalizedPackId))
            {
                Console.Error.WriteLine($"PackID '{normalizedPackId}' is already registered in MetaLib.");
                return 1;
            }

            if (FindPackFilesById(repositoryRoot, normalizedPackId).Count > 0)
            {
                Console.Error.WriteLine($"PackID '{normalizedPackId}' already exists as a pack file.");
                return 1;
            }

            if (File.Exists(targetFilePath))
            {
                Console.Error.WriteLine($"Target file already exists: {targetFilePath}");
                return 1;
            }

            var rootNodeId = "root_" + Guid.NewGuid().ToString("N")[..8];
            var pack = CreatePackJson(normalizedPackId, rootNodeId);

            Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);
            File.WriteAllText(targetFilePath, JsonSerializer.Serialize(pack, JsonOptions.NodeWrite));

            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                Command = "create-pack",
                PackId = normalizedPackId,
                Storage = storage,
                ResourcePath = effectiveResourcePath,
                FilePath = targetFilePath,
                RootNodeId = rootNodeId,
                Registered = false
            }, JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"create-pack failed: {ex.Message}");
            return 1;
        }
    }

    public static int ExecuteRegister(string packId, string? storageName, string? resourcePath)
    {
        try
        {
            if (!TryGetRepositoryRoot(out var repositoryRoot, out var rootError))
            {
                Console.Error.WriteLine(rootError);
                return 1;
            }

            if (!TryNormalizePackId(packId, out var normalizedPackId, out var packIdError))
            {
                Console.Error.WriteLine(packIdError);
                return 1;
            }

            if (!TryParseStorage(storageName, out var storage, out var storageError))
            {
                Console.Error.WriteLine(storageError);
                return 1;
            }

            var meta = LoadMetaLib(repositoryRoot);
            var filePath = ResolveRegisterTargetFile(repositoryRoot, normalizedPackId, storageName, resourcePath, storage, out var resolveError);
            if (filePath is null)
            {
                Console.Error.WriteLine(resolveError);
                return 1;
            }

            var detectedInfo = GetStorageInfo(repositoryRoot, filePath);
            if (detectedInfo is null)
            {
                Console.Error.WriteLine($"Pack file is not under Assets/Resources or Assets/StreamingAssets: {filePath}");
                return 1;
            }

            storage = detectedInfo.Value.Storage;
            var effectiveResourcePath = detectedInfo.Value.ResourcePath;

            if (!TryReadPackMetadata(filePath, out var packMeta, out var readError))
            {
                Console.Error.WriteLine(readError);
                return 1;
            }

            if (!string.Equals(packMeta.PackId, normalizedPackId, StringComparison.Ordinal))
            {
                Console.Error.WriteLine(
                    $"PackID mismatch. Requested '{normalizedPackId}', but file contains '{packMeta.PackId}'.");
                return 1;
            }

            if (TryGetMetaEntry(meta, normalizedPackId, out var existingEntry))
            {
                var existingStorage = existingEntry?["Storage"]?.GetValue<int>() ?? 0;
                var existingResourcePath = existingEntry?["ResourcePath"]?.GetValue<string>() ?? "";
                if (existingStorage != (int)storage || !string.Equals(existingResourcePath, effectiveResourcePath, StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"PackID '{normalizedPackId}' is already registered to '{existingResourcePath}'.");
                    return 1;
                }
            }

            var conflictingId = FindConflictingResourcePath(meta, effectiveResourcePath, storage, normalizedPackId);
            if (conflictingId is not null)
            {
                Console.Error.WriteLine($"ResourcePath '{effectiveResourcePath}' is already registered by '{conflictingId}'.");
                return 1;
            }

            meta[normalizedPackId] = CreateMetaEntry(packMeta, normalizedPackId, storage, effectiveResourcePath, filePath);
            SaveMetaLib(repositoryRoot, meta);

            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                Command = "register-pack",
                PackId = normalizedPackId,
                Storage = storage,
                ResourcePath = effectiveResourcePath,
                FilePath = filePath
            }, JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"register-pack failed: {ex.Message}");
            return 1;
        }
    }

    public static int ExecuteCreateProcess(string packId, string processId, bool attachToRoot)
    {
        try
        {
            var context = EditUnnamedCommand.LoadContext(packId, out var errorMessage);
            if (context is null)
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            if (string.IsNullOrWhiteSpace(processId))
            {
                Console.Error.WriteLine("processid cannot be empty.");
                return 1;
            }

            if (HasProcessIdConflict(context.Pack, processId))
            {
                Console.Error.WriteLine($"ProcessID '{processId}' is already used by an existing named node.");
                return 1;
            }

            var rootNodeObject = context.Nodes[context.Pack.RootNodeId]?.AsObject();
            if (rootNodeObject is null)
            {
                Console.Error.WriteLine("Root node JSON was not found.");
                return 1;
            }

            var (baseX, baseY) = GetSuggestedProcessAnchor(rootNodeObject, context.Nodes.Count);
            var spineId = Guid.NewGuid().ToString("N");
            var leafAId = Guid.NewGuid().ToString("N");
            var leafBId = Guid.NewGuid().ToString("N");

            var spine = CreateSpineNode(spineId, processId, baseX, baseY);
            var leafA = CreateLeafANode(leafAId, processId, baseX + 360f, baseY - 120f);
            var leafB = CreateLeafBNode(leafBId, processId, baseX + 360f, baseY + 120f);

            context.Nodes[spineId] = spine;
            context.Nodes[leafAId] = leafA;
            context.Nodes[leafBId] = leafB;

            if (attachToRoot)
            {
                AttachRootChild(rootNodeObject, spineId);
            }

            EditUnnamedCommand.TouchModifiedAt(context.Root);
            EditUnnamedCommand.SavePack(context);

            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                Command = "create-process",
                PackId = packId,
                ProcessId = processId,
                AttachToRoot = attachToRoot,
                SpineNodeId = spineId,
                LeafANodeId = leafAId,
                LeafBNodeId = leafBId,
                SourceFile = context.FilePath
            }, JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"create-process failed: {ex.Message}");
            return 1;
        }
    }

    private static bool TryGetRepositoryRoot(out string repositoryRoot, out string? errorMessage)
    {
        repositoryRoot = RepositoryLocator.FindRoot(Environment.CurrentDirectory) ?? "";
        errorMessage = null;
        if (!string.IsNullOrEmpty(repositoryRoot))
        {
            return true;
        }

        errorMessage = "Unable to locate repository root from current directory.";
        return false;
    }

    private static bool TryNormalizePackId(string packId, out string normalizedPackId, out string? errorMessage)
    {
        normalizedPackId = packId?.Trim() ?? "";
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(normalizedPackId))
        {
            errorMessage = "PackID cannot be empty.";
            return false;
        }

        if (normalizedPackId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            normalizedPackId.Contains('/') ||
            normalizedPackId.Contains('\\'))
        {
            errorMessage = $"PackID '{normalizedPackId}' contains invalid path characters.";
            return false;
        }

        return true;
    }

    private static bool TryParseStorage(string? storageName, out int storage, out string? errorMessage)
    {
        storage = 0;
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(storageName) || string.Equals(storageName, "Resources", StringComparison.OrdinalIgnoreCase))
        {
            storage = 0;
            return true;
        }

        if (string.Equals(storageName, "StreamingAssets", StringComparison.OrdinalIgnoreCase))
        {
            storage = 1;
            return true;
        }

        errorMessage = $"Unsupported storage '{storageName}'. Use Resources or StreamingAssets.";
        return false;
    }

    private static string NormalizeResourcePath(string? resourcePath, string packId)
    {
        var value = string.IsNullOrWhiteSpace(resourcePath) ? $"{DefaultPackFolder}/{packId}" : resourcePath.Trim();
        value = value.Replace('\\', '/').Trim('/');
        foreach (var extension in PackExtensions)
        {
            if (value.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                value = value[..^extension.Length];
                break;
            }
        }

        return value;
    }

    private static string BuildPackFilePath(string repositoryRoot, int storage, string resourcePath)
    {
        var baseFolder = storage == 1 ? "StreamingAssets" : "Resources";
        return Path.Combine(repositoryRoot, "Assets", baseFolder, resourcePath.Replace('/', Path.DirectorySeparatorChar) + ".json");
    }

    private static JsonObject LoadMetaLib(string repositoryRoot)
    {
        var metaPath = GetMetaLibPath(repositoryRoot);
        if (!File.Exists(metaPath))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(File.ReadAllText(metaPath))?.AsObject() ?? new JsonObject();
    }

    private static void SaveMetaLib(string repositoryRoot, JsonObject meta)
    {
        var metaPath = GetMetaLibPath(repositoryRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(metaPath)!);
        File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, JsonOptions.NodeWrite));
    }

    private static string GetMetaLibPath(string repositoryRoot)
    {
        return Path.Combine(repositoryRoot, "Assets", "Resources", "NekoGraph", "MetaLib.json");
    }

    private static bool HasMetaId(JsonObject meta, string packId)
    {
        return TryGetMetaEntry(meta, packId, out _);
    }

    private static bool TryGetMetaEntry(JsonObject meta, string packId, out JsonObject? entry)
    {
        entry = null;
        if (!meta.TryGetPropertyValue(packId, out var node) || node is not JsonObject entryObject)
        {
            return false;
        }

        entry = entryObject;
        return true;
    }

    private static string? FindConflictingResourcePath(JsonObject meta, string resourcePath, int storage, string allowedPackId)
    {
        foreach (var pair in meta)
        {
            if (string.Equals(pair.Key, allowedPackId, StringComparison.Ordinal))
            {
                continue;
            }

            if (pair.Value is not JsonObject entry)
            {
                continue;
            }

            var entryPath = entry["ResourcePath"]?.GetValue<string>() ?? "";
            var entryStorage = entry["Storage"]?.GetValue<int>() ?? 0;
            if (entryStorage == storage && string.Equals(entryPath, resourcePath, StringComparison.Ordinal))
            {
                return pair.Key;
            }
        }

        return null;
    }

    private static List<string> FindPackFilesById(string repositoryRoot, string packId)
    {
        return Directory
            .EnumerateFiles(repositoryRoot, "*", SearchOption.AllDirectories)
            .Where(path => PackExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), packId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string? ResolveRegisterTargetFile(
        string repositoryRoot,
        string packId,
        string? rawStorageName,
        string? rawResourcePath,
        int parsedStorage,
        out string? errorMessage)
    {
        errorMessage = null;
        if (!string.IsNullOrWhiteSpace(rawResourcePath))
        {
            var resourcePath = NormalizeResourcePath(rawResourcePath, packId);
            var filePath = BuildPackFilePath(repositoryRoot, parsedStorage, resourcePath);
            if (File.Exists(filePath))
            {
                return filePath;
            }

            errorMessage = $"Pack file was not found at '{filePath}'.";
            return null;
        }

        if (!string.IsNullOrWhiteSpace(rawStorageName))
        {
            var resourcePath = NormalizeResourcePath(null, packId);
            var filePath = BuildPackFilePath(repositoryRoot, parsedStorage, resourcePath);
            if (File.Exists(filePath))
            {
                return filePath;
            }
        }

        var candidates = FindPackFilesById(repositoryRoot, packId);
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        if (candidates.Count > 1)
        {
            errorMessage = $"Pack '{packId}' matched multiple files: {string.Join(", ", candidates)}";
            return null;
        }

        errorMessage = $"No file matched PackID '{packId}'.";
        return null;
    }

    private static (int Storage, string ResourcePath)? GetStorageInfo(string repositoryRoot, string filePath)
    {
        var normalized = Path.GetFullPath(filePath).Replace('\\', '/');
        var resourcesMarker = (repositoryRoot.Replace('\\', '/') + "/Assets/Resources/").Replace("//", "/");
        var streamingMarker = (repositoryRoot.Replace('\\', '/') + "/Assets/StreamingAssets/").Replace("//", "/");

        if (normalized.StartsWith(resourcesMarker, StringComparison.OrdinalIgnoreCase))
        {
            return (0, StripExtension(normalized[resourcesMarker.Length..]));
        }

        if (normalized.StartsWith(streamingMarker, StringComparison.OrdinalIgnoreCase))
        {
            return (1, StripExtension(normalized[streamingMarker.Length..]));
        }

        return null;
    }

    private static string StripExtension(string path)
    {
        var normalized = path.Replace('\\', '/');
        var lastDot = normalized.LastIndexOf('.');
        return lastDot >= 0 ? normalized[..lastDot] : normalized;
    }

    private static bool TryReadPackMetadata(string filePath, out PackMetadata metadata, out string? errorMessage)
    {
        metadata = default;
        errorMessage = null;

        var root = JsonNode.Parse(File.ReadAllText(filePath))?.AsObject();
        if (root is null)
        {
            errorMessage = $"Pack file could not be parsed: {filePath}";
            return false;
        }

        var packId = root["PackID"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(packId))
        {
            errorMessage = $"Pack file has no PackID: {filePath}";
            return false;
        }

        metadata = new PackMetadata(
            packId,
            root["DisplayName"]?.GetValue<string>(),
            root["Author"]?.GetValue<string>(),
            root["Version"]?.GetValue<string>(),
            root["Description"]?.GetValue<string>());
        return true;
    }

    private static JsonObject CreateMetaEntry(PackMetadata packMeta, string packId, int storage, string resourcePath, string filePath)
    {
        return new JsonObject
        {
            ["ID"] = packId,
            ["PackID"] = packId,
            ["Kind"] = 1,
            ["Storage"] = storage,
            ["ResourcePath"] = resourcePath,
            ["ObjectType"] = "BasePackData",
            ["GraphType"] = "Base",
            ["DisplayName"] = string.IsNullOrWhiteSpace(packMeta.DisplayName) ? packId : packMeta.DisplayName,
            ["Author"] = string.IsNullOrWhiteSpace(packMeta.Author) ? "NekoTeam" : packMeta.Author,
            ["Version"] = string.IsNullOrWhiteSpace(packMeta.Version) ? "1.0.0" : packMeta.Version,
            ["Description"] = packMeta.Description ?? "",
            ["CustomFields"] = new JsonObject
            {
                ["AssetPath"] = filePath.Replace('\\', '/')
            }
        };
    }

    private static JsonObject CreatePackJson(string packId, string rootNodeId)
    {
        var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return new JsonObject
        {
            ["PackID"] = packId,
            ["DisplayName"] = packId,
            ["Description"] = "",
            ["Author"] = "NekoTeam",
            ["Version"] = "1.0.0",
            ["ReadableFrom"] = 0,
            ["WritableFrom"] = 1000,
            ["CreatedAt"] = unixTime,
            ["ModifiedAt"] = unixTime,
            ["Nodes"] = new JsonObject
            {
                [rootNodeId] = new JsonObject
                {
                    ["$type"] = "RootNodeData, NekoGraph.Runtime",
                    ["_"] = new JsonArray(),
                    ["NodeID"] = rootNodeId,
                    ["Name"] = "Root",
                    ["EditorPosition"] = new JsonObject
                    {
                        ["$type"] = "SerializableVector2, NekoGraph.Runtime",
                        ["x"] = 0,
                        ["y"] = 0
                    },
                    ["OutputConnections"] = new JsonArray(),
                    ["IsChecked"] = false
                }
            },
            ["RootNodeId"] = rootNodeId,
            ["SidePara"] = new JsonObject(),
            ["System"] = 0,
            ["HasStarted"] = false,
            ["ActiveSignals"] = new JsonArray()
        };
    }

    private static bool HasProcessIdConflict(PackDocument pack, string processId)
    {
        return pack.Nodes.Values.Any(node =>
            !string.IsNullOrWhiteSpace(node.ProcessId) &&
            string.Equals(node.ProcessId, processId, StringComparison.Ordinal));
    }

    private static (float X, float Y) GetSuggestedProcessAnchor(JsonObject rootNodeObject, int nodeCount)
    {
        var x = rootNodeObject["EditorPosition"]?["x"]?.GetValue<float>() ?? 120f;
        var y = rootNodeObject["EditorPosition"]?["y"]?.GetValue<float>() ?? 160f;
        var laneIndex = Math.Max(0, (nodeCount - 1) / 3);
        return (x + 320f + laneIndex * 520f, y);
    }

    private static JsonObject CreateSpineNode(string nodeId, string processId, float x, float y)
    {
        return new JsonObject
        {
            ["$type"] = "SpineNodeData, NekoGraph.Runtime",
            ["ProcessID"] = processId,
            ["ParentSpineID"] = new JsonArray(),
            ["NextSpineNodeIDs"] = new JsonArray(),
            ["NodeID"] = nodeId,
            ["Name"] = $"Spine:{processId}",
            ["EditorPosition"] = CreateEditorPosition(x, y),
            ["OutputConnections"] = new JsonArray(),
            ["IsChecked"] = false
        };
    }

    private static JsonObject CreateLeafANode(string nodeId, string processId, float x, float y)
    {
        return new JsonObject
        {
            ["$type"] = "LeafNode_A_Data, NekoGraph.Runtime",
            ["ProcessID"] = processId,
            ["OutputNodeIds"] = new JsonArray(),
            ["NodeID"] = nodeId,
            ["Name"] = $"LeafA:{processId}",
            ["EditorPosition"] = CreateEditorPosition(x, y),
            ["OutputConnections"] = new JsonArray(),
            ["IsChecked"] = false
        };
    }

    private static JsonObject CreateLeafBNode(string nodeId, string processId, float x, float y)
    {
        return new JsonObject
        {
            ["$type"] = "LeafNode_B_Data, NekoGraph.Runtime",
            ["ProcessID"] = processId,
            ["InputNodeIDs"] = new JsonArray(),
            ["NodeID"] = nodeId,
            ["Name"] = $"LeafB:{processId}",
            ["EditorPosition"] = CreateEditorPosition(x, y),
            ["OutputConnections"] = new JsonArray(),
            ["IsChecked"] = false
        };
    }

    private static JsonObject CreateEditorPosition(float x, float y)
    {
        return new JsonObject
        {
            ["$type"] = "SerializableVector2, NekoGraph.Runtime",
            ["x"] = x,
            ["y"] = y
        };
    }

    private static void AttachRootChild(JsonObject rootNodeObject, string childId)
    {
        if (rootNodeObject["_"] is not JsonArray rootChildren)
        {
            rootChildren = new JsonArray();
            rootNodeObject["_"] = rootChildren;
        }

        if (!rootChildren.Any(item => string.Equals(item?.GetValue<string>(), childId, StringComparison.Ordinal)))
        {
            rootChildren.Add(childId);
        }

        if (rootNodeObject["OutputConnections"] is not JsonArray outputConnections)
        {
            outputConnections = new JsonArray();
            rootNodeObject["OutputConnections"] = outputConnections;
        }

        var exists = outputConnections.Any(item =>
            string.Equals(item?["TargetNodeID"]?.GetValue<string>(), childId, StringComparison.Ordinal));
        if (!exists)
        {
            outputConnections.Add(new JsonObject
            {
                ["$type"] = "ConnectionData, NekoGraph.Runtime",
                ["FromPortIndex"] = 0,
                ["TargetNodeID"] = childId,
                ["ToPortIndex"] = 0
            });
        }
    }

    private readonly record struct PackMetadata(
        string PackId,
        string? DisplayName,
        string? Author,
        string? Version,
        string? Description);
}
