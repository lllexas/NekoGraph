using System.Text.Json.Nodes;

namespace NekoGraph.Cli;

internal sealed class PackDocumentLoadResult
{
    public bool Success { get; init; }

    public PackDocument? Value { get; init; }

    public string? ErrorMessage { get; init; }
}

internal sealed class PackDocument
{
    public required string PackId { get; init; }

    public required string FilePath { get; init; }

    public required string RootNodeId { get; init; }

    public required Dictionary<string, PackNode> Nodes { get; init; }
}

internal sealed class PackNode
{
    public required string NodeId { get; init; }

    public required string TypeName { get; init; }

    public required string DisplayName { get; init; }

    public required List<string> OutgoingNodeIds { get; init; }

    public string? TriggerEvent { get; init; }

    public string? CommandName { get; init; }

    public string? ProcessId { get; init; }

    public string? MissionId { get; init; }

    public string? ComparerName { get; init; }

    public required List<string> ComparerParameters { get; init; }

    public required Dictionary<string, string> FieldSummary { get; init; }

    public required List<string> PassOutputs { get; init; }

    public required List<string> FailOutputs { get; init; }
}

internal static class PackDocumentLoader
{
    public static PackDocumentLoadResult Load(string filePath, string requestedPackId)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(filePath))?.AsObject();
            if (root is null)
            {
                return new PackDocumentLoadResult { ErrorMessage = $"Pack file is empty or invalid JSON: {filePath}" };
            }

            var packId = root["PackID"]?.GetValue<string>() ?? requestedPackId;
            var nodesNode = root["Nodes"]?.AsObject();
            if (nodesNode is null || nodesNode.Count == 0)
            {
                return new PackDocumentLoadResult { ErrorMessage = $"Pack '{packId}' has no Nodes object." };
            }

            var nodes = new Dictionary<string, PackNode>(StringComparer.Ordinal);
            foreach (var kvp in nodesNode)
            {
                if (kvp.Value is not JsonObject nodeObject)
                {
                    continue;
                }

                var nodeId = nodeObject["NodeID"]?.GetValue<string>() ?? kvp.Key;
                var typeName = NormalizeTypeName(nodeObject["$type"]?.GetValue<string>());
                var displayName = nodeObject["Name"]?.GetValue<string>() ?? nodeId;
                var outgoing = ReadOutgoing(nodeObject);
                var triggerEvent = ReadTriggerEvent(nodeObject["Event"]);
                var commandName = nodeObject["Command"]?["CommandName"]?.GetValue<string>();
                var processId = nodeObject["ProcessID"]?.GetValue<string>();
                var missionId = nodeObject["MissionID"]?.GetValue<string>();
                var comparerName = nodeObject["ComparerName"]?.GetValue<string>();
                var comparerParameters = ReadStringArray(nodeObject["Parameters"]);
                var fieldSummary = BuildFieldSummary(nodeObject, triggerEvent, commandName);
                var passOutputs = ReadStringArray(nodeObject["PassOutputs"]);
                var failOutputs = ReadStringArray(nodeObject["FailOutputs"]);

                nodes[nodeId] = new PackNode
                {
                    NodeId = nodeId,
                    TypeName = typeName,
                    DisplayName = displayName,
                    OutgoingNodeIds = outgoing,
                    TriggerEvent = triggerEvent,
                    CommandName = commandName,
                    ProcessId = processId,
                    MissionId = missionId,
                    ComparerName = comparerName,
                    ComparerParameters = comparerParameters,
                    FieldSummary = fieldSummary,
                    PassOutputs = passOutputs,
                    FailOutputs = failOutputs
                };
            }

            var rootNodeId = root["RootNodeId"]?.GetValue<string>() ??
                             root["RootNodeID"]?.GetValue<string>() ??
                             nodes.Values.FirstOrDefault(node => node.TypeName.Contains("RootNodeData", StringComparison.OrdinalIgnoreCase))?.NodeId;

            if (string.IsNullOrWhiteSpace(rootNodeId))
            {
                return new PackDocumentLoadResult { ErrorMessage = $"Pack '{packId}' has no RootNodeId." };
            }

            return new PackDocumentLoadResult
            {
                Success = true,
                Value = new PackDocument
                {
                    PackId = packId,
                    FilePath = filePath,
                    RootNodeId = rootNodeId,
                    Nodes = nodes
                }
            };
        }
        catch (Exception ex)
        {
            return new PackDocumentLoadResult { ErrorMessage = $"Failed to load pack JSON: {ex.Message}" };
        }
    }

    private static string NormalizeTypeName(string? rawTypeName)
    {
        if (string.IsNullOrWhiteSpace(rawTypeName))
        {
            return "Unknown";
        }

        var firstSegment = rawTypeName.Split(',')[0].Trim();
        var lastDot = firstSegment.LastIndexOf('.');
        return lastDot >= 0 ? firstSegment[(lastDot + 1)..] : firstSegment;
    }

    private static List<string> ReadOutgoing(JsonObject nodeObject)
    {
        var result = new List<string>();

        AppendConnectionTargets(nodeObject["OutputConnections"], result);
        AppendStringArray(nodeObject["SignalOutputs"], result);
        AppendStringArray(nodeObject["OutputNodeIDs"], result);
        AppendStringArray(nodeObject["OutPutNodeIDs"], result);
        AppendStringArray(nodeObject["OutputNodeIds"], result);
        AppendStringArray(nodeObject["NextSpineNodeIDs"], result);
        AppendStringArray(nodeObject["_"], result);
        AppendStringArray(nodeObject["PassOutputs"], result);
        AppendStringArray(nodeObject["FailOutputs"], result);

        return result
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static void AppendConnectionTargets(JsonNode? node, List<string> result)
    {
        if (node is not JsonArray array)
        {
            return;
        }

        foreach (var item in array)
        {
            var targetNodeId = item?["TargetNodeID"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(targetNodeId))
            {
                result.Add(targetNodeId);
            }
        }
    }

    private static void AppendStringArray(JsonNode? node, List<string> result)
    {
        if (node is JsonValue singleValue)
        {
            var value = singleValue.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                result.Add(value);
            }
            return;
        }

        if (node is not JsonArray array)
        {
            return;
        }

        foreach (var item in array)
        {
            var value = item?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(value))
            {
                result.Add(value);
            }
        }
    }

    private static List<string> ReadStringArray(JsonNode? node)
    {
        var result = new List<string>();
        AppendStringArray(node, result);
        return result
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string? ReadTriggerEvent(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue valueNode)
        {
            if (valueNode.TryGetValue<int>(out var enumIndex))
            {
                return TriggerEventCatalog.GetName(enumIndex);
            }

            if (valueNode.TryGetValue<string>(out var rawValue))
            {
                return rawValue;
            }
        }

        return node.ToString();
    }

    private static Dictionary<string, string> BuildFieldSummary(
        JsonObject nodeObject,
        string? triggerEvent,
        string? commandName)
    {
        var skipKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "$type",
            "NodeID",
            "Name",
            "EditorPosition",
            "OutputConnections",
            "InputNodeIDs",
            "OutputNodeIDs",
            "OutPutNodeIDs",
            "OutputNodeIds",
            "SignalOutputs",
            "PassOutputs",
            "FailOutputs",
            "NextSpineNodeIDs",
            "_"
        };

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var kvp in nodeObject)
        {
            if (skipKeys.Contains(kvp.Key) || kvp.Value is null)
            {
                continue;
            }

            if (kvp.Key.Equals("Event", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(triggerEvent))
            {
                result[kvp.Key] = triggerEvent;
                continue;
            }

            if (kvp.Key.Equals("Command", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(commandName))
            {
                result["CommandName"] = commandName;
                continue;
            }

            if (TryFormatFieldValue(kvp.Value, out var value))
            {
                result[kvp.Key] = value;
            }
        }

        return result;
    }

    private static bool TryFormatFieldValue(JsonNode node, out string value)
    {
        value = string.Empty;

        if (node is JsonValue)
        {
            value = JsonValueToReadableString(node);
            return true;
        }

        if (node is JsonArray array)
        {
            var items = array
                .Select(item =>
                {
                    if (item is null)
                    {
                        return null;
                    }

                    if (item is JsonValue)
                    {
                        return JsonValueToReadableString(item);
                    }

                    return null;
                })
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();

            if (items.Count == 0)
            {
                return false;
            }

            value = string.Join(", ", items);
            return true;
        }

        return false;
    }

    private static string JsonValueToReadableString(JsonNode node)
    {
        var raw = node.ToJsonString();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string>(raw) ?? raw.Trim('"');
        }
        catch
        {
            return raw.Trim('"');
        }
    }
}
