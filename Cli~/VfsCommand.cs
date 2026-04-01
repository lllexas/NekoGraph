using System.Text.Json;
using System.Text.Json.Nodes;

namespace NekoGraph.Cli;

internal static class VfsCommand
{
    private sealed record VfsNodeReport(
        string NodeId,
        string Name,
        string Extension,
        string PathSegment,
        bool IsDirectory,
        bool IsFile,
        int ChildCount,
        string? DataJson);

    public static int ExecuteList(string packId, string path)
    {
        try
        {
            var context = EditUnnamedCommand.LoadContext(packId, out var errorMessage);
            if (context is null)
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            path = NormalizePath(path);
            if (!TryResolvePath(context, path, out var nodeId, out var nodeObject))
            {
                Console.Error.WriteLine($"VFS path was not found: {path}");
                return 1;
            }

            if (!IsDirectory(nodeObject, nodeId, context))
            {
                Console.Error.WriteLine($"VFS path is not a directory: {path}");
                return 1;
            }

            var children = GetChildren(context, nodeId)
                .Select(childId => CreateNodeReport(context, childId))
                .OrderBy(item => item.PathSegment, StringComparer.Ordinal)
                .ToList();

            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                Command = "vfs-ls",
                PackId = packId,
                Path = path,
                Count = children.Count,
                Children = children
            }, JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"vfs-ls failed: {ex.Message}");
            return 1;
        }
    }

    public static int ExecuteShow(string packId, string path)
    {
        try
        {
            var context = EditUnnamedCommand.LoadContext(packId, out var errorMessage);
            if (context is null)
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            path = NormalizePath(path);
            if (!TryResolvePath(context, path, out var nodeId, out _))
            {
                Console.Error.WriteLine($"VFS path was not found: {path}");
                return 1;
            }

            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                Command = "vfs-show",
                PackId = packId,
                Path = path,
                Node = CreateNodeReport(context, nodeId)
            }, JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"vfs-show failed: {ex.Message}");
            return 1;
        }
    }

    public static int ExecuteMkdir(string packId, string path)
    {
        try
        {
            var context = EditUnnamedCommand.LoadContext(packId, out var errorMessage);
            if (context is null)
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            path = NormalizePath(path);
            if (path == "/")
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { Command = "vfs-mkdir", PackId = packId, Path = path, CreatedNodeIds = Array.Empty<string>() }, JsonOptions.Default));
                return 0;
            }

            var createdNodeIds = EnsureDirectory(context, path);
            EditUnnamedCommand.TouchModifiedAt(context.Root);
            EditUnnamedCommand.SavePack(context);

            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                Command = "vfs-mkdir",
                PackId = packId,
                Path = path,
                CreatedNodeIds = createdNodeIds
            }, JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"vfs-mkdir failed: {ex.Message}");
            return 1;
        }
    }

    public static int ExecuteWrite(string packId, string path, string dataJson)
    {
        try
        {
            var context = EditUnnamedCommand.LoadContext(packId, out var errorMessage);
            if (context is null)
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            path = NormalizePath(path);
            if (path == "/")
            {
                Console.Error.WriteLine("Cannot write to VFS root '/'.");
                return 1;
            }

            var parentPath = GetParentPath(path);
            EnsureDirectory(context, parentPath);

            var fileSegment = GetFileName(path);
            var (name, extension) = SplitFileSegment(fileSegment);

            string nodeId;
            JsonObject nodeObject;
            if (TryResolvePath(context, path, out var existingNodeId, out var existingNode))
            {
                if (IsDirectory(existingNode, existingNodeId, context))
                {
                    Console.Error.WriteLine($"VFS path is a directory and cannot be overwritten as a file: {path}");
                    return 1;
                }

                nodeId = existingNodeId;
                nodeObject = existingNode;
            }
            else
            {
                if (!TryResolvePath(context, parentPath, out var parentNodeId, out _))
                {
                    Console.Error.WriteLine($"Parent directory was not found: {parentPath}");
                    return 1;
                }

                nodeId = Guid.NewGuid().ToString("N");
                nodeObject = CreateVfsNode(nodeId, name, extension, dataJson, isDirectory: false);
                context.Nodes[nodeId] = nodeObject;
                AttachChild(context, parentNodeId, nodeId);
            }

            nodeObject["Name"] = name;
            nodeObject["Extension"] = extension;
            nodeObject["DataJson"] = dataJson;
            nodeObject["IsEnabled"] = true;
            nodeObject["ParentNodeID"] = GetParentPath(path) == "/" ? context.Pack.RootNodeId : ResolveNodeId(context, GetParentPath(path));

            EditUnnamedCommand.TouchModifiedAt(context.Root);
            EditUnnamedCommand.SavePack(context);

            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                Command = "vfs-write",
                PackId = packId,
                Path = path,
                NodeId = nodeId
            }, JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"vfs-write failed: {ex.Message}");
            return 1;
        }
    }

    public static int ExecuteDelete(string packId, string path)
    {
        try
        {
            var context = EditUnnamedCommand.LoadContext(packId, out var errorMessage);
            if (context is null)
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            path = NormalizePath(path);
            if (path == "/")
            {
                Console.Error.WriteLine("Cannot delete VFS root '/'.");
                return 1;
            }

            if (!TryResolvePath(context, path, out var nodeId, out _))
            {
                Console.Error.WriteLine($"VFS path was not found: {path}");
                return 1;
            }

            var removeIds = CollectDescendants(context, nodeId);
            removeIds.Add(nodeId);

            var parentPath = GetParentPath(path);
            if (TryResolvePath(context, parentPath, out var parentId, out _))
            {
                DetachChild(context, parentId, nodeId);
            }

            foreach (var removeId in removeIds)
            {
                context.Nodes.Remove(removeId);
            }

            EditUnnamedCommand.TouchModifiedAt(context.Root);
            EditUnnamedCommand.SavePack(context);

            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                Command = "vfs-delete",
                PackId = packId,
                Path = path,
                RemovedNodeIds = removeIds
            }, JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"vfs-delete failed: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveNodeId(EditContext context, string path)
    {
        if (!TryResolvePath(context, path, out var nodeId, out _))
        {
            throw new InvalidOperationException($"VFS path was not found: {path}");
        }

        return nodeId;
    }

    private static List<string> EnsureDirectory(EditContext context, string path)
    {
        path = NormalizePath(path);
        var created = new List<string>();
        if (path == "/")
        {
            return created;
        }

        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentId = context.Pack.RootNodeId;
        var currentPath = "";

        foreach (var segment in segments)
        {
            currentPath += "/" + segment;
            if (TryResolvePath(context, currentPath, out var existingId, out var existingNode))
            {
                if (!IsDirectory(existingNode, existingId, context))
                {
                    throw new InvalidOperationException($"Path is occupied by a file: {currentPath}");
                }

                currentId = existingId;
                continue;
            }

            var nodeId = Guid.NewGuid().ToString("N");
            var dirNode = CreateVfsNode(nodeId, segment, "", null, isDirectory: true);
            dirNode["ParentNodeID"] = currentId;
            context.Nodes[nodeId] = dirNode;
            AttachChild(context, currentId, nodeId);
            created.Add(nodeId);
            currentId = nodeId;
        }

        return created;
    }

    private static JsonObject CreateVfsNode(string nodeId, string name, string extension, string? dataJson, bool isDirectory)
    {
        return new JsonObject
        {
            ["$type"] = "VFSNodeData, NekoGraph.Runtime",
            ["ParentNodeID"] = null,
            ["ChildNodeIDs"] = new JsonArray(),
            ["DataJson"] = dataJson is null ? null : JsonValue.Create(dataJson),
            ["IsEnabled"] = true,
            ["Description"] = "",
            ["MimeType"] = null,
            ["NodeID"] = nodeId,
            ["Name"] = name,
            ["EditorPosition"] = CreateEditorPosition(0f, 0f),
            ["OutputConnections"] = new JsonArray(),
            ["IsChecked"] = false,
            ["Extension"] = extension,
            ["IsDirectory"] = isDirectory,
            ["IsFile"] = !isDirectory
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

    private static void AttachChild(EditContext context, string parentId, string childId)
    {
        var parent = context.Nodes[parentId]?.AsObject() ?? throw new InvalidOperationException($"Parent node JSON was not found: {parentId}");
        var child = context.Nodes[childId]?.AsObject() ?? throw new InvalidOperationException($"Child node JSON was not found: {childId}");

        var connections = EnsureArray(parent, "OutputConnections");
        if (!connections.Any(item => string.Equals(item?["TargetNodeID"]?.GetValue<string>(), childId, StringComparison.Ordinal)))
        {
            connections.Add(new JsonObject
            {
                ["$type"] = "ConnectionData, NekoGraph.Runtime",
                ["FromPortIndex"] = 0,
                ["TargetNodeID"] = childId,
                ["ToPortIndex"] = 0
            });
        }

        if (IsRootNode(parent))
        {
            var rootChildren = EnsureArray(parent, "_");
            if (!rootChildren.Any(item => string.Equals(item?.GetValue<string>(), childId, StringComparison.Ordinal)))
            {
                rootChildren.Add(childId);
            }
        }
        else if (IsVfsNode(parent))
        {
            var childIds = EnsureArray(parent, "ChildNodeIDs");
            if (!childIds.Any(item => string.Equals(item?.GetValue<string>(), childId, StringComparison.Ordinal)))
            {
                childIds.Add(childId);
            }
        }

        child["ParentNodeID"] = parentId;
    }

    private static void DetachChild(EditContext context, string parentId, string childId)
    {
        var parent = context.Nodes[parentId]?.AsObject();
        if (parent is null)
        {
            return;
        }

        RemoveConnectionTargets(parent, childId);
        RemoveStringArrayValue(parent["_"] as JsonArray, childId);
        RemoveStringArrayValue(parent["ChildNodeIDs"] as JsonArray, childId);
    }

    private static void RemoveConnectionTargets(JsonObject node, string targetNodeId)
    {
        if (node["OutputConnections"] is not JsonArray array)
        {
            return;
        }

        for (var i = array.Count - 1; i >= 0; i--)
        {
            if (string.Equals(array[i]?["TargetNodeID"]?.GetValue<string>(), targetNodeId, StringComparison.Ordinal))
            {
                array.RemoveAt(i);
            }
        }
    }

    private static void RemoveStringArrayValue(JsonArray? array, string value)
    {
        if (array is null)
        {
            return;
        }

        for (var i = array.Count - 1; i >= 0; i--)
        {
            if (string.Equals(array[i]?.GetValue<string>(), value, StringComparison.Ordinal))
            {
                array.RemoveAt(i);
            }
        }
    }

    private static List<string> CollectDescendants(EditContext context, string nodeId)
    {
        var result = new List<string>();
        foreach (var childId in GetChildren(context, nodeId))
        {
            result.AddRange(CollectDescendants(context, childId));
            result.Add(childId);
        }

        return result
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> GetChildren(EditContext context, string nodeId)
    {
        if (!context.Nodes.TryGetPropertyValue(nodeId, out var node) || node is not JsonObject nodeObject)
        {
            return [];
        }

        var result = new List<string>();
        if (nodeObject["OutputConnections"] is JsonArray outputConnections)
        {
            foreach (var item in outputConnections)
            {
                var targetNodeId = item?["TargetNodeID"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(targetNodeId))
                {
                    result.Add(targetNodeId);
                }
            }
        }

        return result.Distinct(StringComparer.Ordinal).ToList();
    }

    private static bool TryResolvePath(EditContext context, string path, out string nodeId, out JsonObject nodeObject)
    {
        path = NormalizePath(path);
        nodeId = context.Pack.RootNodeId;
        nodeObject = context.Nodes[nodeId]?.AsObject() ?? throw new InvalidOperationException($"Root node JSON was not found: {nodeId}");

        if (path == "/")
        {
            return true;
        }

        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            string? nextNodeId = null;
            JsonObject? nextNodeObject = null;
            foreach (var childId in GetChildren(context, nodeId))
            {
                if (!context.Nodes.TryGetPropertyValue(childId, out var childNode) || childNode is not JsonObject childObject)
                {
                    continue;
                }

                if (string.Equals(GetPathSegment(childObject, childId), segment, StringComparison.Ordinal))
                {
                    nextNodeId = childId;
                    nextNodeObject = childObject;
                    break;
                }
            }

            if (nextNodeId is null || nextNodeObject is null)
            {
                nodeId = null!;
                nodeObject = null!;
                return false;
            }

            nodeId = nextNodeId;
            nodeObject = nextNodeObject;
        }

        return true;
    }

    private static string GetPathSegment(JsonObject nodeObject, string nodeId)
    {
        var name = nodeObject["Name"]?.GetValue<string>() ?? nodeId;
        var extension = nodeObject["Extension"]?.GetValue<string>() ?? "";
        return string.IsNullOrEmpty(extension) ? name : name + extension;
    }

    private static bool IsDirectory(JsonObject nodeObject, string nodeId, EditContext context)
    {
        if (string.Equals(nodeId, context.Pack.RootNodeId, StringComparison.Ordinal))
        {
            return true;
        }

        var extension = nodeObject["Extension"]?.GetValue<string>() ?? "";
        return string.IsNullOrEmpty(extension);
    }

    private static bool IsRootNode(JsonObject nodeObject)
    {
        var typeName = nodeObject["$type"]?.GetValue<string>() ?? "";
        return typeName.Contains("RootNodeData", StringComparison.Ordinal);
    }

    private static bool IsVfsNode(JsonObject nodeObject)
    {
        var typeName = nodeObject["$type"]?.GetValue<string>() ?? "";
        return typeName.Contains("VFSNodeData", StringComparison.Ordinal);
    }

    private static JsonArray EnsureArray(JsonObject node, string propertyName)
    {
        if (node[propertyName] is JsonArray array)
        {
            return array;
        }

        array = new JsonArray();
        node[propertyName] = array;
        return array;
    }

    private static VfsNodeReport CreateNodeReport(EditContext context, string nodeId)
    {
        var node = context.Nodes[nodeId]?.AsObject() ?? throw new InvalidOperationException($"Node JSON was not found: {nodeId}");
        var extension = node["Extension"]?.GetValue<string>() ?? "";
        var isDirectory = IsDirectory(node, nodeId, context);
        var dataJson = node["DataJson"]?.GetValue<string>();
        return new VfsNodeReport(
            nodeId,
            node["Name"]?.GetValue<string>() ?? nodeId,
            extension,
            GetPathSegment(node, nodeId),
            isDirectory,
            !isDirectory,
            GetChildren(context, nodeId).Count,
            dataJson);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        path = path.Replace('\\', '/').Trim();
        if (!path.StartsWith("/"))
        {
            path = "/" + path;
        }

        while (path.Contains("//", StringComparison.Ordinal))
        {
            path = path.Replace("//", "/", StringComparison.Ordinal);
        }

        if (path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal))
        {
            path = path[..^1];
        }

        return path;
    }

    private static string GetParentPath(string path)
    {
        path = NormalizePath(path);
        if (path == "/")
        {
            return "/";
        }

        var lastSlash = path.LastIndexOf('/');
        return lastSlash <= 0 ? "/" : path[..lastSlash];
    }

    private static string GetFileName(string path)
    {
        path = NormalizePath(path);
        var lastSlash = path.LastIndexOf('/');
        return lastSlash < 0 ? path : path[(lastSlash + 1)..];
    }

    private static (string name, string extension) SplitFileSegment(string segment)
    {
        var dotIndex = segment.LastIndexOf('.');
        if (dotIndex <= 0 || dotIndex == segment.Length - 1)
        {
            throw new InvalidOperationException($"VFS file path must include a file extension: {segment}");
        }

        return (segment[..dotIndex], segment[dotIndex..]);
    }
}
