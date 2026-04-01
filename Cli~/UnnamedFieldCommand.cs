using System.Text.Json;
using System.Text.Json.Nodes;

namespace NekoGraph.Cli;

internal static class UnnamedFieldCommand
{
    public static int ExecuteQueryFields(string packId, string fromNamedRef, string toNamedRef, int unnamedNodeIndex, int? fromPortIndex, int? toPortIndex)
    {
        try
        {
            var resolved = ResolveUnnamedNode(packId, fromNamedRef, toNamedRef, unnamedNodeIndex, fromPortIndex, toPortIndex, out var errorMessage);
            if (resolved is null)
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            var payload = BuildFieldQueryReport("query-fields", packId, fromNamedRef, toNamedRef, unnamedNodeIndex, fromPortIndex, toPortIndex, resolved);
            Console.Out.WriteLine(JsonSerializer.Serialize(payload, JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"query-fields failed: {ex.Message}");
            return 1;
        }
    }

    public static int ExecuteEditField(string packId, string fromNamedRef, string toNamedRef, int unnamedNodeIndex, string fieldName, string fieldValue, int? fromPortIndex, int? toPortIndex)
    {
        try
        {
            var resolved = ResolveUnnamedNode(packId, fromNamedRef, toNamedRef, unnamedNodeIndex, fromPortIndex, toPortIndex, out var errorMessage);
            if (resolved is null)
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            if (!TryResolveBusinessField(resolved.Node, fieldName, out var fieldSpec))
            {
                Console.Error.WriteLine($"Field '{fieldName}' is not editable for node kind '{resolved.NodeKind}'.");
                Console.Error.WriteLine($"Editable fields: {string.Join(", ", GetEditableFieldNames(resolved.Node))}");
                return 1;
            }

            var oldValue = ReadFieldValue(resolved.NodeObject, fieldSpec);
            WriteFieldValue(resolved.NodeObject, fieldSpec, fieldValue);
            EditUnnamedCommand.TouchModifiedAt(resolved.Context.Root);
            EditUnnamedCommand.SavePack(resolved.Context);

            var payload = new UnnamedFieldEditReport(
                "edit-field",
                packId,
                resolved.Context.FilePath,
                fromNamedRef,
                toNamedRef,
                unnamedNodeIndex,
                resolved.NodeKind,
                resolved.Label,
                fieldSpec.Name,
                oldValue,
                ReadFieldValue(resolved.NodeObject, fieldSpec),
                GetEditableFieldNames(resolved.Node));

            Console.Out.WriteLine(JsonSerializer.Serialize(payload, JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"edit-field failed: {ex.Message}");
            return 1;
        }
    }

    private static UnnamedNodeResolution? ResolveUnnamedNode(string packId, string fromNamedRef, string toNamedRef, int unnamedNodeIndex, int? fromPortIndex, int? toPortIndex, out string? errorMessage)
    {
        errorMessage = null;

        var context = EditUnnamedCommand.LoadContext(packId, out errorMessage);
        if (context is null)
        {
            return null;
        }

        if (!BridgeQuery.TryResolveNodeRef(context.Pack, fromNamedRef, out var fromNode) ||
            !BridgeQuery.TryResolveNodeRef(context.Pack, toNamedRef, out var toNode))
        {
            errorMessage = $"Node ref was not found. from={fromNamedRef}, to={toNamedRef}";
            return null;
        }

        var fromNodeObject = context.Nodes[fromNode.NodeId]?.AsObject();
        var toNodeObject = context.Nodes[toNode.NodeId]?.AsObject();
        if (fromNodeObject is null || toNodeObject is null)
        {
            errorMessage = "Node JSON was not found for bridge field resolution.";
            return null;
        }

        if (!BridgeQuery.TryResolveBridgePorts(
                fromNodeObject,
                toNodeObject,
                fromNamedRef,
                toNamedRef,
                fromPortIndex,
                toPortIndex,
                out var effectiveFromPortIndex,
                out var effectiveToPortIndex,
                out errorMessage))
        {
            return null;
        }

        var bridgePath = BridgeQuery.FindUniqueBridgePath(context, fromNode.NodeId, toNode.NodeId, effectiveFromPortIndex, effectiveToPortIndex);
        if (bridgePath is null)
        {
            var paths = BridgeQuery.FindBridgePathsForDiagnosis(context, fromNode.NodeId, toNode.NodeId, effectiveFromPortIndex, effectiveToPortIndex);
            errorMessage = BridgeQuery.FormatBridgeDiagnosticError(fromNamedRef, toNamedRef, paths);
            return null;
        }

        var unnamedNodeIds = bridgePath
            .Skip(1)
            .Take(Math.Max(bridgePath.Count - 2, 0))
            .ToList();

        if (unnamedNodeIndex < 0 || unnamedNodeIndex >= unnamedNodeIds.Count)
        {
            errorMessage = $"unnamed-node-index {unnamedNodeIndex} is out of range for bridge unnamed node count {unnamedNodeIds.Count}.";
            return null;
        }

        var nodeId = unnamedNodeIds[unnamedNodeIndex];
        if (!context.Pack.Nodes.TryGetValue(nodeId, out var node))
        {
            errorMessage = $"Unnamed node '{nodeId}' could not be resolved from pack.";
            return null;
        }

        var nodeObject = context.Nodes[nodeId]?.AsObject();
        if (nodeObject is null)
        {
            errorMessage = $"Unnamed node JSON was not found: {nodeId}";
            return null;
        }

        var previousNodeId = bridgePath[unnamedNodeIndex];
        var nextNodeId = bridgePath[unnamedNodeIndex + 2];
        var previousLabel = context.Pack.Nodes.TryGetValue(previousNodeId, out var previousNode)
            ? BuildNodeLabel(previousNode)
            : previousNodeId;
        var nextLabel = context.Pack.Nodes.TryGetValue(nextNodeId, out var nextNode)
            ? BuildNodeLabel(nextNode)
            : nextNodeId;

        // Calculate bridge type: direct (0 unnamed nodes) or bridged (≥1 unnamed nodes)
        var unnamedNodeCount = unnamedNodeIds.Count;
        var bridgeType = unnamedNodeCount == 0 ? "direct" : "bridged";

        return new UnnamedNodeResolution(
            context,
            node,
            nodeObject,
            GetNodeKind(node),
            BuildNodeLabel(node),
            unnamedNodeCount,
            bridgeType,
            new NodeLink(previousNodeId, previousLabel, context.Pack.Nodes.TryGetValue(previousNodeId, out var prev) ? NamedNodeRef.TryBuild(prev) : null),
            new NodeLink(nextNodeId, nextLabel, context.Pack.Nodes.TryGetValue(nextNodeId, out var nxt) ? NamedNodeRef.TryBuild(nxt) : null));
    }

    private static UnnamedFieldQueryReport BuildFieldQueryReport(string command, string packId, string fromNamedRef, string toNamedRef, int unnamedNodeIndex, int? fromPortIndex, int? toPortIndex, UnnamedNodeResolution resolved)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fieldName in GetEditableFieldNames(resolved.Node))
        {
            if (TryResolveBusinessField(resolved.Node, fieldName, out var fieldSpec))
            {
                fields[fieldName] = ReadFieldValue(resolved.NodeObject, fieldSpec);
            }
        }

        return new UnnamedFieldQueryReport(
            command,
            packId,
            fromNamedRef,
            toNamedRef,
            fromPortIndex,
            toPortIndex,
            unnamedNodeIndex,
            resolved.NodeKind,
            resolved.Label,
            fields,
            GetEditableFieldNames(resolved.Node),
            new BridgeFieldContext(
                resolved.UnnamedNodeCount,
                resolved.BridgeType,
                resolved.PreviousNode,
                resolved.NextNode));
    }

    private static List<string> GetEditableFieldNames(PackNode node)
    {
        return GetNodeKind(node) switch
        {
            "trigger" => ["Event"],
            "comparer" => ["ComparerName", "Parameters"],
            "command" => ["Command.CommandName", "Command.Parameter", "Command.Parameters"],
            _ => []
        };
    }

    private static bool TryResolveBusinessField(PackNode node, string fieldName, out BusinessFieldSpec fieldSpec)
    {
        fieldSpec = default!;
        var normalizedFieldName = fieldName.Trim();

        switch (GetNodeKind(node))
        {
            case "trigger" when string.Equals(normalizedFieldName, "Event", StringComparison.Ordinal):
                fieldSpec = new BusinessFieldSpec("Event", "int-or-name", false);
                return true;

            case "comparer" when string.Equals(normalizedFieldName, "ComparerName", StringComparison.Ordinal):
                fieldSpec = new BusinessFieldSpec("ComparerName", "string", false);
                return true;

            case "comparer" when string.Equals(normalizedFieldName, "Parameters", StringComparison.Ordinal):
                fieldSpec = new BusinessFieldSpec("Parameters", "string-array", true);
                return true;

            case "command" when string.Equals(normalizedFieldName, "Command.CommandName", StringComparison.Ordinal):
                fieldSpec = new BusinessFieldSpec("Command.CommandName", "string", false);
                return true;

            case "command" when string.Equals(normalizedFieldName, "Command.Parameter", StringComparison.Ordinal):
                fieldSpec = new BusinessFieldSpec("Command.Parameter", "string", false);
                return true;

            case "command" when string.Equals(normalizedFieldName, "Command.Parameters", StringComparison.Ordinal):
                fieldSpec = new BusinessFieldSpec("Command.Parameters", "string-array", true);
                return true;

            default:
                return false;
        }
    }

    private static string ReadFieldValue(JsonObject nodeObject, BusinessFieldSpec fieldSpec)
    {
        var node = ResolveFieldNode(nodeObject, fieldSpec.Name, out var finalKey);
        if (node is null)
        {
            return string.Empty;
        }

        var valueNode = node[finalKey];
        if (valueNode is null)
        {
            return string.Empty;
        }

        if (valueNode is JsonArray array)
        {
            return array.ToJsonString(JsonOptions.Default);
        }

        if (valueNode is JsonValue value)
        {
            if (value.TryGetValue<string>(out var stringValue))
            {
                return stringValue;
            }

            return value.ToJsonString();
        }

        return valueNode.ToJsonString(JsonOptions.Default);
    }

    private static void WriteFieldValue(JsonObject nodeObject, BusinessFieldSpec fieldSpec, string rawValue)
    {
        var node = ResolveFieldNode(nodeObject, fieldSpec.Name, out var finalKey, createMissing: true)
            ?? throw new InvalidOperationException($"Field path '{fieldSpec.Name}' could not be resolved.");

        if (fieldSpec.Type == "int-or-name")
        {
            if (int.TryParse(rawValue, out var intValue))
            {
                node[finalKey] = intValue;
                return;
            }

            node[finalKey] = rawValue;
            return;
        }

        if (fieldSpec.IsArray)
        {
            node[finalKey] = ParseStringArrayValue(rawValue);
            return;
        }

        node[finalKey] = rawValue;
    }

    private static JsonArray ParseStringArrayValue(string rawValue)
    {
        var trimmed = rawValue.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                if (JsonNode.Parse(trimmed) is JsonArray parsedArray)
                {
                    var array = new JsonArray();
                    foreach (var item in parsedArray)
                    {
                        array.Add(item?.GetValue<string>() ?? item?.ToJsonString() ?? string.Empty);
                    }
                    return array;
                }
            }
            catch
            {
            }
        }

        return new JsonArray(trimmed);
    }

    private static JsonObject? ResolveFieldNode(JsonObject root, string fieldPath, out string finalKey, bool createMissing = false)
    {
        var segments = fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            finalKey = string.Empty;
            return null;
        }

        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            if (current[segment] is JsonObject nextObject)
            {
                current = nextObject;
                continue;
            }

            if (!createMissing)
            {
                finalKey = string.Empty;
                return null;
            }

            nextObject = new JsonObject();
            current[segment] = nextObject;
            current = nextObject;
        }

        finalKey = segments[^1];
        return current;
    }

    private static string GetNodeKind(PackNode node)
    {
        if (node.TypeName.Contains("TriggerNode", StringComparison.OrdinalIgnoreCase)) return "trigger";
        if (node.TypeName.Contains("ComparerNode", StringComparison.OrdinalIgnoreCase)) return "comparer";
        if (node.TypeName.Contains("CommandNode", StringComparison.OrdinalIgnoreCase)) return "command";
        return "unknown";
    }

    private static string BuildNodeLabel(PackNode node)
    {
        return GetNodeKind(node) switch
        {
            "trigger" => $"Trigger({node.TriggerEvent ?? node.NodeId})",
            "comparer" => string.IsNullOrWhiteSpace(node.ComparerName)
                ? $"Comparer({node.NodeId})"
                : $"Comparer({node.ComparerName}{FormatComparerParameters(node.ComparerParameters)})",
            "command" => $"Command({node.CommandName ?? node.NodeId})",
            _ => node.NodeId
        };
    }

    private static string FormatComparerParameters(List<string> parameters)
    {
        return parameters.Count == 0
            ? string.Empty
            : $" {string.Join(' ', parameters)}";
    }
}

internal sealed record UnnamedFieldQueryReport(
    string Command,
    string PackId,
    string From,
    string To,
    int? FromPortIndex,
    int? ToPortIndex,
    int UnnamedNodeIndex,
    string NodeKind,
    string Label,
    Dictionary<string, string> Fields,
    List<string> EditableFields,
    BridgeFieldContext BridgeContext);

internal sealed record UnnamedFieldEditReport(
    string Command,
    string PackId,
    string SourceFile,
    string From,
    string To,
    int UnnamedNodeIndex,
    string NodeKind,
    string Label,
    string FieldName,
    string OldValue,
    string NewValue,
    List<string> EditableFields);

internal sealed record BridgeFieldContext(
    int UnnamedNodeCount,
    string BridgeType,
    NodeLink PreviousNode,
    NodeLink NextNode);

internal sealed record BusinessFieldSpec(
    string Name,
    string Type,
    bool IsArray);

internal sealed record UnnamedNodeResolution(
    EditContext Context,
    PackNode Node,
    JsonObject NodeObject,
    string NodeKind,
    string Label,
    int UnnamedNodeCount,
    string BridgeType,
    NodeLink PreviousNode,
    NodeLink NextNode);
