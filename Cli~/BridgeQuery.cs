using System.Text.Json.Nodes;

namespace NekoGraph.Cli;

internal static class BridgeQuery
{
    public static bool TryResolveBridgePorts(
        JsonObject fromNodeObject,
        JsonObject toNodeObject,
        string fromNodeRef,
        string toNodeRef,
        int? requestedFromPortIndex,
        int? requestedToPortIndex,
        out int effectiveFromPortIndex,
        out int effectiveToPortIndex,
        out string? errorMessage)
    {
        effectiveFromPortIndex = -1;
        effectiveToPortIndex = -1;
        errorMessage = null;

        var outputPorts = GetAvailableOutputPorts(fromNodeObject);
        if (!TryResolvePortIndex(outputPorts, requestedFromPortIndex, "from", fromNodeRef, out effectiveFromPortIndex, out errorMessage))
        {
            return false;
        }

        var inputPorts = GetAvailableInputPorts(toNodeObject);
        if (!TryResolvePortIndex(inputPorts, requestedToPortIndex, "to", toNodeRef, out effectiveToPortIndex, out errorMessage))
        {
            return false;
        }

        return true;
    }

    public static bool TryResolveNodeRef(PackDocument pack, string nodeRef, out PackNode node)
    {
        if (TryResolveNamedNode(pack, nodeRef, out node))
        {
            return true;
        }

        if (pack.Nodes.TryGetValue(nodeRef, out node!))
        {
            return true;
        }

        node = null!;
        return false;
    }

    public static bool TryResolveNamedNode(PackDocument pack, string namedRef, out PackNode node)
    {
        node = null!;
        foreach (var candidate in pack.Nodes.Values)
        {
            if (string.Equals(NamedNodeRef.TryBuild(candidate), namedRef, StringComparison.Ordinal))
            {
                node = candidate;
                return true;
            }
        }

        return false;
    }

    public static List<string>? FindUniqueBridgePath(EditContext context, string fromNodeId, string toNodeId)
    {
        return FindUniqueBridgePath(context, fromNodeId, toNodeId, null, null);
    }

    public static List<string>? FindUniqueBridgePath(EditContext context, string fromNodeId, string toNodeId, int? fromPortIndex, int? toPortIndex)
    {
        var matches = new List<List<string>>();
        ExploreBridgePaths(context, fromNodeId, toNodeId, fromPortIndex, toPortIndex, [fromNodeId], matches);
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>
    /// Returns up to <paramref name="cap"/> bridge paths for diagnostic purposes.
    /// Use when FindUniqueBridgePath returns null to produce a meaningful error message.
    /// </summary>
    public static List<List<string>> FindBridgePathsForDiagnosis(EditContext context, string fromNodeId, string toNodeId, int? fromPortIndex, int? toPortIndex, int cap = 5)
    {
        var matches = new List<List<string>>();
        ExploreBridgePathsAll(context, fromNodeId, toNodeId, fromPortIndex, toPortIndex, [fromNodeId], matches, cap);
        return matches;
    }

    public static string FormatBridgeDiagnosticError(string fromRef, string toRef, List<List<string>> paths)
    {
        if (paths.Count == 0)
        {
            return $"No bridge path found from '{fromRef}' to '{toRef}'. " +
                   "Check that the nodes exist and are connected, or use --run --full to inspect the pack structure.";
        }

        var lines = new System.Text.StringBuilder();
        lines.AppendLine($"Multiple bridge paths found from '{fromRef}' to '{toRef}' ({paths.Count} path(s) — showing up to 5).");
        lines.AppendLine("Use --from-port or --to-port to select a specific path, or query --bridge first.");
        for (var i = 0; i < paths.Count; i++)
        {
            lines.AppendLine($"  path {i}: {string.Join(" -> ", paths[i])}");
        }
        return lines.ToString().TrimEnd();
    }

    public static List<string> ReadOutgoingTargets(JsonObject? nodeObject)
    {
        return ReadOutgoingEdges(nodeObject)
            .Select(edge => edge.ToNodeId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static List<BridgeEdge> ReadOutgoingEdges(JsonObject? nodeObject)
    {
        if (nodeObject is null)
        {
            return [];
        }

        var result = new List<BridgeEdge>();
        AppendArrayEdges(nodeObject["_"], result, 0, 0);
        AppendArrayEdges(nodeObject["SignalOutputs"], result, 0, 0);
        AppendArrayEdges(nodeObject["OutputNodeIDs"], result, 0, 0);
        AppendArrayEdges(nodeObject["OutPutNodeIDs"], result, 0, 0);
        AppendArrayEdges(nodeObject["OutputNodeIds"], result, 0, 0);
        AppendArrayEdges(nodeObject["NextSpineNodeIDs"], result, 0, 0);
        AppendArrayEdges(nodeObject["PassOutputs"], result, 0, 0);
        AppendArrayEdges(nodeObject["FailOutputs"], result, 1, 0);

        if (nodeObject["OutputConnections"] is JsonArray connections)
        {
            foreach (var item in connections)
            {
                var targetNodeId = item?["TargetNodeID"]?.GetValue<string>();
                var fromPortIndex = item?["FromPortIndex"]?.GetValue<int>() ?? 0;
                var toPortIndex = item?["ToPortIndex"]?.GetValue<int>() ?? 0;
                if (!string.IsNullOrWhiteSpace(targetNodeId))
                {
                    result.Add(new BridgeEdge(targetNodeId, fromPortIndex, toPortIndex));
                }
            }
        }

        return result
            .Where(edge => !string.IsNullOrWhiteSpace(edge.ToNodeId))
            .DistinctBy(edge => $"{edge.ToNodeId}:{edge.FromPortIndex}:{edge.ToPortIndex}")
            .ToList();
    }

    public static List<int> GetAvailableOutputPorts(JsonObject nodeObject)
    {
        var schema = PortSchemaCatalog.GetSchema(GetTypeName(nodeObject));
        if (schema.OutputPorts.Count > 0)
        {
            return schema.OutputPorts;
        }

        var result = new HashSet<int>();
        if (nodeObject["OutputConnections"] is JsonArray connections)
        {
            foreach (var item in connections)
            {
                result.Add(item?["FromPortIndex"]?.GetValue<int>() ?? 0);
            }
        }

        return result.OrderBy(value => value).ToList();
    }

    public static List<int> GetAvailableInputPorts(JsonObject nodeObject)
    {
        var schema = PortSchemaCatalog.GetSchema(GetTypeName(nodeObject));
        if (schema.InputPorts.Count > 0)
        {
            return schema.InputPorts;
        }

        return [];
    }

    private static void ExploreBridgePathsAll(EditContext context, string fromNodeId, string toNodeId, int? fromPortIndex, int? toPortIndex, List<string> path, List<List<string>> matches, int cap)
    {
        if (matches.Count >= cap)
        {
            return;
        }

        if (path[^1] == toNodeId)
        {
            matches.Add([.. path]);
            return;
        }

        var currentObject = context.Nodes[path[^1]]?.AsObject();
        foreach (var edge in ReadOutgoingEdges(currentObject))
        {
            if (path.Count == 1 && fromPortIndex.HasValue && edge.FromPortIndex != fromPortIndex.Value)
            {
                continue;
            }

            if (string.Equals(edge.ToNodeId, toNodeId, StringComparison.Ordinal) &&
                toPortIndex.HasValue &&
                edge.ToPortIndex != toPortIndex.Value)
            {
                continue;
            }

            var nextNodeId = edge.ToNodeId;
            if (path.Contains(nextNodeId, StringComparer.Ordinal))
            {
                continue;
            }

            if (!context.Pack.Nodes.TryGetValue(nextNodeId, out var nextNode))
            {
                continue;
            }

            var nextNamedRef = NamedNodeRef.TryBuild(nextNode);
            if (!string.Equals(nextNodeId, toNodeId, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(nextNamedRef))
            {
                continue;
            }

            path.Add(nextNodeId);
            ExploreBridgePathsAll(context, fromNodeId, toNodeId, null, toPortIndex, path, matches, cap);
            path.RemoveAt(path.Count - 1);
        }
    }

    private static void ExploreBridgePaths(EditContext context, string currentNodeId, string targetNodeId, int? fromPortIndex, int? toPortIndex, List<string> path, List<List<string>> matches)
    {
        if (matches.Count > 1)
        {
            return;
        }

        if (currentNodeId == targetNodeId)
        {
            matches.Add([.. path]);
            return;
        }

        var currentObject = context.Nodes[currentNodeId]?.AsObject();
        foreach (var edge in ReadOutgoingEdges(currentObject))
        {
            if (path.Count == 1 && fromPortIndex.HasValue && edge.FromPortIndex != fromPortIndex.Value)
            {
                continue;
            }

            if (string.Equals(edge.ToNodeId, targetNodeId, StringComparison.Ordinal) &&
                toPortIndex.HasValue &&
                edge.ToPortIndex != toPortIndex.Value)
            {
                continue;
            }

            var nextNodeId = edge.ToNodeId;
            if (path.Contains(nextNodeId, StringComparer.Ordinal))
            {
                continue;
            }

            if (!context.Pack.Nodes.TryGetValue(nextNodeId, out var nextNode))
            {
                continue;
            }

            var nextNamedRef = NamedNodeRef.TryBuild(nextNode);
            if (!string.Equals(nextNodeId, targetNodeId, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(nextNamedRef))
            {
                continue;
            }

            path.Add(nextNodeId);
            ExploreBridgePaths(context, nextNodeId, targetNodeId, null, toPortIndex, path, matches);
            path.RemoveAt(path.Count - 1);
        }
    }

    private static void AppendArrayEdges(JsonNode? node, List<BridgeEdge> result, int fromPortIndex, int toPortIndex)
    {
        if (node is not JsonArray array)
        {
            return;
        }

        foreach (var item in array)
        {
            var value = item?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(value))
            {
                result.Add(new BridgeEdge(value, fromPortIndex, toPortIndex));
            }
        }
    }

    private static bool TryResolvePortIndex(
        List<int> availablePorts,
        int? requestedPortIndex,
        string side,
        string nodeRef,
        out int effectivePortIndex,
        out string? errorMessage)
    {
        effectivePortIndex = -1;
        errorMessage = null;

        if (availablePorts.Count == 0)
        {
            errorMessage = $"Node '{nodeRef}' has no available {side} port.";
            return false;
        }

        if (requestedPortIndex.HasValue)
        {
            if (!availablePorts.Contains(requestedPortIndex.Value))
            {
                errorMessage = $"Node '{nodeRef}' does not have {side} port {requestedPortIndex.Value}. Available {side} ports: {string.Join(", ", availablePorts)}";
                return false;
            }

            effectivePortIndex = requestedPortIndex.Value;
            return true;
        }

        if (!availablePorts.Contains(0))
        {
            errorMessage = $"Node '{nodeRef}' does not support the default {side} port 0. Available {side} ports: {string.Join(", ", availablePorts)}";
            return false;
        }

        effectivePortIndex = 0;
        return true;
    }

    private static string GetTypeName(JsonObject nodeObject)
    {
        var rawTypeName = nodeObject["$type"]?.GetValue<string>() ?? string.Empty;
        var firstSegment = rawTypeName.Split(',')[0].Trim();
        var lastDot = firstSegment.LastIndexOf('.');
        return lastDot >= 0 ? firstSegment[(lastDot + 1)..] : firstSegment;
    }
}

internal sealed record BridgeEdge(
    string ToNodeId,
    int FromPortIndex,
    int ToPortIndex);
