using System.Text.Json;
using System.Text.Json.Nodes;

namespace NekoGraph.Cli;

internal static class EditUnnamedCommand
{
    public static int ExecuteDestroyBridge(string packId, string fromNodeRef, string toNodeRef, int? fromPortIndex, int? toPortIndex)
    {
        try
        {
            var context = LoadContext(packId, out var errorMessage);
            if (context is null)
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            if (!BridgeQuery.TryResolveNodeRef(context.Pack, fromNodeRef, out var fromNode) ||
                !BridgeQuery.TryResolveNodeRef(context.Pack, toNodeRef, out var toNode))
            {
                Console.Error.WriteLine($"Node ref was not found. from={fromNodeRef}, to={toNodeRef}");
                return 1;
            }

            var fromNodeObject = context.Nodes[fromNode.NodeId]?.AsObject();
            var toNodeObject = context.Nodes[toNode.NodeId]?.AsObject();
            if (fromNodeObject is null || toNodeObject is null)
            {
                Console.Error.WriteLine("Node JSON was not found for bridge destruction.");
                return 1;
            }

            if (!BridgeQuery.TryResolveBridgePorts(
                    fromNodeObject,
                    toNodeObject,
                    fromNodeRef,
                    toNodeRef,
                    fromPortIndex,
                    toPortIndex,
                    out var effectiveFromPortIndex,
                    out var effectiveToPortIndex,
                    out errorMessage))
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            var bridgePath = BridgeQuery.FindUniqueBridgePath(context, fromNode.NodeId, toNode.NodeId, effectiveFromPortIndex, effectiveToPortIndex);
            if (bridgePath is null)
            {
                var paths = BridgeQuery.FindBridgePathsForDiagnosis(context, fromNode.NodeId, toNode.NodeId, effectiveFromPortIndex, effectiveToPortIndex);
                Console.Error.WriteLine(BridgeQuery.FormatBridgeDiagnosticError(fromNodeRef, toNodeRef, paths));
                return 1;
            }

            if (bridgePath.Count < 3)
            {
                Console.Error.WriteLine("There is no unnamed bridge to destroy between these named nodes.");
                return 1;
            }

            var firstUnnamedId = bridgePath[1];
            RemoveOutgoingTarget(fromNodeObject, firstUnnamedId, effectiveFromPortIndex, 0);

            var removedNodeIds = bridgePath.Skip(1).Take(bridgePath.Count - 2).ToList();
            foreach (var nodeId in removedNodeIds)
            {
                context.Nodes.Remove(nodeId);
            }

            TouchModifiedAt(context.Root);
            SavePack(context);

            Console.Out.WriteLine(JsonSerializer.Serialize(
                new DestroyBridgeReport(
                    "edit-destroy-bridge",
                    packId,
                    context.FilePath,
                    fromNodeRef,
                    toNodeRef,
                    removedNodeIds),
                JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"edit-destroy-bridge failed: {ex.Message}");
            return 1;
        }
    }

    public static int ExecuteCreateBridge(string packId, string fromNodeRef, string toNodeRef, string nodeKindList, int? fromPortIndex, int? toPortIndex)
    {
        try
        {
            var context = LoadContext(packId, out var errorMessage);
            if (context is null)
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            if (!BridgeQuery.TryResolveNodeRef(context.Pack, fromNodeRef, out var fromNode) ||
                !BridgeQuery.TryResolveNodeRef(context.Pack, toNodeRef, out var toNode))
            {
                Console.Error.WriteLine($"Node ref was not found. from={fromNodeRef}, to={toNodeRef}");
                return 1;
            }

            var fromNodeObject = context.Nodes[fromNode.NodeId]?.AsObject();
            var toNodeObject = context.Nodes[toNode.NodeId]?.AsObject();
            if (fromNodeObject is null || toNodeObject is null)
            {
                Console.Error.WriteLine("Named node JSON was not found for bridge creation.");
                return 1;
            }

            if (!BridgeQuery.TryResolveBridgePorts(
                    fromNodeObject,
                    toNodeObject,
                    fromNodeRef,
                    toNodeRef,
                    fromPortIndex,
                    toPortIndex,
                    out var effectiveFromPortIndex,
                    out var effectiveToPortIndex,
                    out errorMessage))
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            if (BridgeQuery.FindUniqueBridgePath(context, fromNode.NodeId, toNode.NodeId, effectiveFromPortIndex, effectiveToPortIndex) is not null)
            {
                Console.Error.WriteLine($"A bridge already exists from '{fromNodeRef}' to '{toNodeRef}'.");
                return 1;
            }

            var directOutgoing = BridgeQuery.ReadOutgoingEdges(fromNodeObject);
            if (directOutgoing.Any(edge =>
                    string.Equals(edge.ToNodeId, toNode.NodeId, StringComparison.Ordinal) &&
                    (!fromPortIndex.HasValue || edge.FromPortIndex == fromPortIndex.Value) &&
                    (!toPortIndex.HasValue || edge.ToPortIndex == toPortIndex.Value)))
            {
                Console.Error.WriteLine($"A direct edge already exists from '{fromNodeRef}' to '{toNodeRef}'.");
                return 1;
            }

            var nodeKinds = ParseNodeKinds(nodeKindList);

            var fromPos = ReadPosition(fromNodeObject);
            var toPos = ReadPosition(toNodeObject);
            if (nodeKinds.Count == 0)
            {
                AppendOutgoingTarget(fromNodeObject, toNode.NodeId, effectiveFromPortIndex, effectiveToPortIndex);
                TouchModifiedAt(context.Root);
                SavePack(context);

                Console.Out.WriteLine(JsonSerializer.Serialize(
                    new CreateBridgeReport(
                        "edit-create-bridge",
                        packId,
                        context.FilePath,
                        fromNodeRef,
                        toNodeRef,
                        [],
                        []),
                    JsonOptions.Default));
                return 0;
            }

            var dx = (toPos.x - fromPos.x) / (nodeKinds.Count + 1f);
            var dy = (toPos.y - fromPos.y) / (nodeKinds.Count + 1f);

            var createdNodeIds = new List<string>();
            for (var i = 0; i < nodeKinds.Count; i++)
            {
                var nodeId = Guid.NewGuid().ToString();
                var targetId = i == nodeKinds.Count - 1 ? toNode.NodeId : Guid.Empty.ToString();
                createdNodeIds.Add(nodeId);
            }

            for (var i = 0; i < nodeKinds.Count; i++)
            {
                var nodeId = createdNodeIds[i];
                var targetId = i == nodeKinds.Count - 1 ? toNode.NodeId : createdNodeIds[i + 1];
                var nodeObject = CreateUnnamedNode(
                    nodeKinds[i],
                    nodeId,
                    targetId,
                    (fromPos.x + dx * (i + 1f), fromPos.y + dy * (i + 1f)),
                    i == nodeKinds.Count - 1 ? effectiveToPortIndex : 0);
                context.Nodes[nodeId] = nodeObject;
            }

            AppendOutgoingTarget(fromNodeObject, createdNodeIds[0], effectiveFromPortIndex, 0);

            TouchModifiedAt(context.Root);
            SavePack(context);

            Console.Out.WriteLine(JsonSerializer.Serialize(
                new CreateBridgeReport(
                    "edit-create-bridge",
                    packId,
                    context.FilePath,
                    fromNodeRef,
                    toNodeRef,
                    nodeKinds,
                    createdNodeIds),
                JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"edit-create-bridge failed: {ex.Message}");
            return 1;
        }
    }

    public static int ExecuteInsertAt(string packId, string fromNamedRef, string? toNamedRef, int depthEdgeIndex, string nodeKind, int? fromPortIndex = null, int? toPortIndex = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(toNamedRef))
            {
                Console.Error.WriteLine("to-named-ref is required for deep bridge insertion.");
                return 1;
            }

            var context = LoadContext(packId, out var errorMessage);
            if (context is null)
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            var normalizedKind = NormalizeNodeKind(nodeKind);
            if (normalizedKind is null)
            {
                Console.Error.WriteLine($"Unsupported unnamed node kind: {nodeKind}");
                return 1;
            }

            if (!BridgeQuery.TryResolveNodeRef(context.Pack, fromNamedRef, out var fromNode) ||
                !BridgeQuery.TryResolveNodeRef(context.Pack, toNamedRef, out var toNode))
            {
                Console.Error.WriteLine($"Named node ref was not found. from={fromNamedRef}, to={toNamedRef}");
                return 1;
            }

            var fromNodeObject = context.Nodes[fromNode.NodeId]?.AsObject();
            var toNodeObject = context.Nodes[toNode.NodeId]?.AsObject();
            if (fromNodeObject is null || toNodeObject is null)
            {
                Console.Error.WriteLine("Node JSON was not found for bridge insertion.");
                return 1;
            }

            if (!BridgeQuery.TryResolveBridgePorts(
                    fromNodeObject, toNodeObject,
                    fromNamedRef, toNamedRef,
                    fromPortIndex, toPortIndex,
                    out var effectiveFromPort, out var effectiveToPort,
                    out errorMessage))
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            var bridgePath = BridgeQuery.FindUniqueBridgePath(context, fromNode.NodeId, toNode.NodeId, effectiveFromPort, effectiveToPort);
            if (bridgePath is null)
            {
                var paths = BridgeQuery.FindBridgePathsForDiagnosis(context, fromNode.NodeId, toNode.NodeId, effectiveFromPort, effectiveToPort);
                Console.Error.WriteLine(BridgeQuery.FormatBridgeDiagnosticError(fromNamedRef, toNamedRef, paths));
                return 1;
            }

            if (depthEdgeIndex < 0 || depthEdgeIndex >= bridgePath.Count - 1)
            {
                Console.Error.WriteLine($"depth-edge-index {depthEdgeIndex} is out of range for bridge length {bridgePath.Count - 1}.");
                return 1;
            }

            var edgeFromId = bridgePath[depthEdgeIndex];
            var edgeToId = bridgePath[depthEdgeIndex + 1];
            var edgeFromObject = context.Nodes[edgeFromId]?.AsObject();
            if (edgeFromObject is null)
            {
                Console.Error.WriteLine($"Bridge source JSON was not found: {edgeFromId}");
                return 1;
            }

            var newNodeId = Guid.NewGuid().ToString();
            var existingEdge = BridgeQuery.ReadOutgoingEdges(edgeFromObject)
                .FirstOrDefault(edge => string.Equals(edge.ToNodeId, edgeToId, StringComparison.Ordinal));
            var newNodeObject = CreateUnnamedNode(
                normalizedKind,
                newNodeId,
                edgeToId,
                GetMidpoint(edgeFromObject, context.Nodes[edgeToId]?.AsObject()),
                existingEdge?.ToPortIndex ?? 0);
            ReplaceOutgoingTarget(
                edgeFromObject,
                edgeToId,
                newNodeId,
                existingEdge?.FromPortIndex ?? 0,
                existingEdge?.ToPortIndex,
                0);
            context.Nodes[newNodeId] = newNodeObject;
            TouchModifiedAt(context.Root);
            SavePack(context);

            Console.Out.WriteLine(JsonSerializer.Serialize(
                new DeepUnnamedEditReport(
                    "edit-insert-unnamed-at",
                    packId,
                    context.FilePath,
                    fromNamedRef,
                    toNamedRef,
                    depthEdgeIndex,
                    normalizedKind,
                    newNodeId,
                    edgeFromId,
                    edgeToId),
                JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"edit-insert-unnamed-at failed: {ex.Message}");
            return 1;
        }
    }

    public static int ExecuteRemoveAt(string packId, string fromNamedRef, string? toNamedRef, int depthEdgeIndex)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(toNamedRef))
            {
                Console.Error.WriteLine("to-named-ref is required for deep bridge removal.");
                return 1;
            }

            var context = LoadContext(packId, out var errorMessage);
            if (context is null)
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            if (!BridgeQuery.TryResolveNodeRef(context.Pack, fromNamedRef, out var fromNode) ||
                !BridgeQuery.TryResolveNodeRef(context.Pack, toNamedRef, out var toNode))
            {
                Console.Error.WriteLine($"Named node ref was not found. from={fromNamedRef}, to={toNamedRef}");
                return 1;
            }

            var bridgePath = BridgeQuery.FindUniqueBridgePath(context, fromNode.NodeId, toNode.NodeId);
            if (bridgePath is null)
            {
                var paths = BridgeQuery.FindBridgePathsForDiagnosis(context, fromNode.NodeId, toNode.NodeId, null, null);
                Console.Error.WriteLine(BridgeQuery.FormatBridgeDiagnosticError(fromNamedRef, toNamedRef, paths));
                return 1;
            }

            if (depthEdgeIndex < 0 || depthEdgeIndex >= bridgePath.Count - 1)
            {
                Console.Error.WriteLine($"depth-edge-index {depthEdgeIndex} is out of range for bridge length {bridgePath.Count - 1}.");
                return 1;
            }

            var edgeFromId = bridgePath[depthEdgeIndex];
            var removeNodeId = bridgePath[depthEdgeIndex + 1];
            if (string.Equals(removeNodeId, toNode.NodeId, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("The target named node is not removable. Pick a depth edge whose next node is unnamed.");
                return 1;
            }

            if (!context.Pack.Nodes.TryGetValue(removeNodeId, out var removeNode) ||
                NamedNodeRef.TryBuild(removeNode) is not null)
            {
                Console.Error.WriteLine($"Depth edge {depthEdgeIndex} does not point to an unnamed bridge node.");
                return 1;
            }

            var nextNodeId = bridgePath[depthEdgeIndex + 2];
            var removeNodeObject = context.Nodes[removeNodeId]?.AsObject();
            var edgeFromObject = context.Nodes[edgeFromId]?.AsObject();
            if (removeNodeObject is null || edgeFromObject is null)
            {
                Console.Error.WriteLine("Bridge JSON was not found for removal.");
                return 1;
            }

            var incomingSources = FindIncomingSources(context.Nodes, removeNodeId);
            var outgoingTargets = BridgeQuery.ReadOutgoingTargets(removeNodeObject);
            if (incomingSources.Count != 1 ||
                !string.Equals(incomingSources[0], edgeFromId, StringComparison.Ordinal) ||
                outgoingTargets.Count != 1 ||
                !string.Equals(outgoingTargets[0], nextNodeId, StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"Unnamed bridge '{removeNodeId}' is not a single-step bridge and cannot be auto-removed.");
                return 1;
            }

            var incomingEdge = BridgeQuery.ReadOutgoingEdges(edgeFromObject)
                .FirstOrDefault(edge => string.Equals(edge.ToNodeId, removeNodeId, StringComparison.Ordinal));
            var outgoingEdge = BridgeQuery.ReadOutgoingEdges(removeNodeObject)
                .FirstOrDefault(edge => string.Equals(edge.ToNodeId, nextNodeId, StringComparison.Ordinal));

            ReplaceOutgoingTarget(
                edgeFromObject,
                removeNodeId,
                nextNodeId,
                incomingEdge?.FromPortIndex ?? 0,
                incomingEdge?.ToPortIndex,
                outgoingEdge?.ToPortIndex ?? 0);
            context.Nodes.Remove(removeNodeId);
            TouchModifiedAt(context.Root);
            SavePack(context);

            Console.Out.WriteLine(JsonSerializer.Serialize(
                new DeepUnnamedEditReport(
                    "edit-remove-unnamed-at",
                    packId,
                    context.FilePath,
                    fromNamedRef,
                    toNamedRef,
                    depthEdgeIndex,
                    NormalizeNodeKindFromType(removeNode.TypeName) ?? "unknown",
                    removeNodeId,
                    edgeFromId,
                    nextNodeId),
                JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"edit-remove-unnamed-at failed: {ex.Message}");
            return 1;
        }
    }

    public static int ExecuteInsert(string packId, string fromNamedRef, string toNamedRef, string nodeKind, int? fromPortIndex = null, int? toPortIndex = null)
    {
        try
        {
            var context = LoadContext(packId, out var errorMessage);
            if (context is null)
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            var normalizedKind = NormalizeNodeKind(nodeKind);
            if (normalizedKind is null)
            {
                Console.Error.WriteLine($"Unsupported unnamed node kind: {nodeKind}");
                return 1;
            }

            if (!BridgeQuery.TryResolveNodeRef(context.Pack, fromNamedRef, out var fromNode) ||
                !BridgeQuery.TryResolveNodeRef(context.Pack, toNamedRef, out var toNode))
            {
                Console.Error.WriteLine($"Named node ref was not found. from={fromNamedRef}, to={toNamedRef}");
                return 1;
            }

            var fromNodeObject = context.Nodes[fromNode.NodeId]?.AsObject();
            var toNodeObject = context.Nodes[toNode.NodeId]?.AsObject();
            if (fromNodeObject is null || toNodeObject is null)
            {
                Console.Error.WriteLine($"Source node JSON was not found: {fromNode.NodeId}");
                return 1;
            }

            if (!BridgeQuery.TryResolveBridgePorts(
                    fromNodeObject, toNodeObject,
                    fromNamedRef, toNamedRef,
                    fromPortIndex, toPortIndex,
                    out var effectiveFromPort, out var effectiveToPort,
                    out errorMessage))
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            var directEdge = BridgeQuery.ReadOutgoingEdges(fromNodeObject)
                .FirstOrDefault(edge =>
                    string.Equals(edge.ToNodeId, toNode.NodeId, StringComparison.Ordinal) &&
                    edge.FromPortIndex == effectiveFromPort &&
                    edge.ToPortIndex == effectiveToPort);

            if (directEdge is null)
            {
                Console.Error.WriteLine($"No direct edge exists from '{fromNamedRef}' (port {effectiveFromPort}) to '{toNamedRef}' (port {effectiveToPort}).");
                return 1;
            }

            var newNodeId = Guid.NewGuid().ToString();
            var newNodeObject = CreateUnnamedNode(
                normalizedKind,
                newNodeId,
                toNode.NodeId,
                GetMidpoint(fromNodeObject, context.Nodes[toNode.NodeId]?.AsObject()),
                effectiveToPort);

            ReplaceOutgoingTarget(
                fromNodeObject,
                toNode.NodeId,
                newNodeId,
                effectiveFromPort,
                directEdge.ToPortIndex,
                0);
            context.Nodes[newNodeId] = newNodeObject;
            TouchModifiedAt(context.Root);
            SavePack(context);

            var payload = new UnnamedEditReport(
                "edit-insert-unnamed",
                packId,
                context.FilePath,
                fromNamedRef,
                toNamedRef,
                normalizedKind,
                newNodeId);

            Console.Out.WriteLine(JsonSerializer.Serialize(payload, JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"edit-insert-unnamed failed: {ex.Message}");
            return 1;
        }
    }

    public static int ExecuteRemove(string packId, string fromNamedRef, string toNamedRef)
    {
        try
        {
            var context = LoadContext(packId, out var errorMessage);
            if (context is null)
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            if (!BridgeQuery.TryResolveNodeRef(context.Pack, fromNamedRef, out var fromNode) ||
                !BridgeQuery.TryResolveNodeRef(context.Pack, toNamedRef, out var toNode))
            {
                Console.Error.WriteLine($"Named node ref was not found. from={fromNamedRef}, to={toNamedRef}");
                return 1;
            }

            var fromNodeObject = context.Nodes[fromNode.NodeId]?.AsObject();
            if (fromNodeObject is null)
            {
                Console.Error.WriteLine($"Source node JSON was not found: {fromNode.NodeId}");
                return 1;
            }

            var candidates = BridgeQuery.ReadOutgoingTargets(fromNodeObject)
                .Where(nodeId => context.Pack.Nodes.TryGetValue(nodeId, out var candidateNode) &&
                                 NamedNodeRef.TryBuild(candidateNode) is null &&
                                 BridgeQuery.ReadOutgoingTargets(context.Nodes[nodeId]?.AsObject()).Contains(toNode.NodeId, StringComparer.Ordinal))
                .ToList();

            if (candidates.Count == 0)
            {
                Console.Error.WriteLine($"No single unnamed bridge exists from '{fromNamedRef}' to '{toNamedRef}'.");
                return 1;
            }

            if (candidates.Count > 1)
            {
                Console.Error.WriteLine($"Multiple unnamed bridge candidates exist from '{fromNamedRef}' to '{toNamedRef}': {string.Join(", ", candidates)}");
                return 1;
            }

            var removeNodeId = candidates[0];
            var removeNodeObject = context.Nodes[removeNodeId]?.AsObject();
            if (removeNodeObject is null)
            {
                Console.Error.WriteLine($"Unnamed bridge JSON was not found: {removeNodeId}");
                return 1;
            }

            var incomingSources = FindIncomingSources(context.Nodes, removeNodeId);
            var outgoingTargets = BridgeQuery.ReadOutgoingTargets(removeNodeObject);
            if (incomingSources.Count != 1 ||
                !string.Equals(incomingSources[0], fromNode.NodeId, StringComparison.Ordinal) ||
                outgoingTargets.Count != 1 ||
                !string.Equals(outgoingTargets[0], toNode.NodeId, StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"Unnamed bridge '{removeNodeId}' is not a single-edge bridge and cannot be auto-removed.");
                return 1;
            }

            var incomingEdge = BridgeQuery.ReadOutgoingEdges(fromNodeObject)
                .FirstOrDefault(edge => string.Equals(edge.ToNodeId, removeNodeId, StringComparison.Ordinal));
            var outgoingEdge = BridgeQuery.ReadOutgoingEdges(removeNodeObject)
                .FirstOrDefault(edge => string.Equals(edge.ToNodeId, toNode.NodeId, StringComparison.Ordinal));

            ReplaceOutgoingTarget(
                fromNodeObject,
                removeNodeId,
                toNode.NodeId,
                incomingEdge?.FromPortIndex ?? 0,
                incomingEdge?.ToPortIndex,
                outgoingEdge?.ToPortIndex ?? 0);
            context.Nodes.Remove(removeNodeId);
            TouchModifiedAt(context.Root);
            SavePack(context);

            var payload = new UnnamedEditReport(
                "edit-remove-unnamed",
                packId,
                context.FilePath,
                fromNamedRef,
                toNamedRef,
                NormalizeNodeKindFromType(context.Pack.Nodes[removeNodeId].TypeName) ?? "unknown",
                removeNodeId);

            Console.Out.WriteLine(JsonSerializer.Serialize(payload, JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"edit-remove-unnamed failed: {ex.Message}");
            return 1;
        }
    }

    internal static EditContext? LoadContext(string packId, out string? errorMessage)
    {
        errorMessage = null;
        var repositoryRoot = RepositoryLocator.FindRoot(Environment.CurrentDirectory);
        if (repositoryRoot is null)
        {
            errorMessage = "Unable to locate repository root from current directory.";
            return null;
        }

        var resolution = PackResolver.Resolve(repositoryRoot, packId);
        if (!resolution.Success)
        {
            errorMessage = resolution.ErrorMessage;
            return null;
        }

        var pack = PackDocumentLoader.Load(resolution.FilePath!, packId);
        if (!pack.Success || pack.Value is null)
        {
            errorMessage = pack.ErrorMessage;
            return null;
        }

        var root = JsonNode.Parse(File.ReadAllText(resolution.FilePath!))?.AsObject();
        var nodes = root?["Nodes"]?.AsObject();
        if (root is null || nodes is null)
        {
            errorMessage = $"Pack '{packId}' could not be parsed for editing.";
            return null;
        }

        return new EditContext
        {
            FilePath = resolution.FilePath!,
            Pack = pack.Value,
            Root = root,
            Nodes = nodes
        };
    }

    private static bool TryResolveNamedNode(PackDocument pack, string namedRef, out PackNode node)
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

    private static string? NormalizeNodeKind(string rawKind)
    {
        return rawKind.ToLowerInvariant() switch
        {
            "trigger" => "trigger",
            "comparer" => "comparer",
            "command" => "command",
            _ => null
        };
    }

    private static List<string> ParseNodeKinds(string rawNodeKindList)
    {
        if (string.Equals(rawNodeKindList, "none", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return rawNodeKindList
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeNodeKind)
            .Where(kind => kind is not null)
            .Select(kind => kind!)
            .ToList();
    }

    private static string? NormalizeNodeKindFromType(string typeName)
    {
        if (typeName.Contains("TriggerNode", StringComparison.OrdinalIgnoreCase)) return "trigger";
        if (typeName.Contains("ComparerNode", StringComparison.OrdinalIgnoreCase)) return "comparer";
        if (typeName.Contains("CommandNode", StringComparison.OrdinalIgnoreCase)) return "command";
        return null;
    }

    private static JsonObject CreateUnnamedNode(string nodeKind, string nodeId, string targetNodeId, (float x, float y) position, int toPortIndex)
    {
        return nodeKind switch
        {
            "trigger" => new JsonObject
            {
                ["$type"] = "TriggerNodeData, NekoGraph.Runtime",
                ["Event"] = 0,
                ["InputNodeIDs"] = new JsonArray(),
                ["SignalOutputs"] = new JsonArray(targetNodeId),
                ["NodeID"] = nodeId,
                ["EditorPosition"] = CreateEditorPosition(position.x, position.y),
                ["OutputConnections"] = new JsonArray(CreateConnectionData(0, targetNodeId, toPortIndex)),
                ["IsChecked"] = false
            },
            "comparer" => new JsonObject
            {
                ["$type"] = "ComparerNodeData, NekoGraph.Runtime",
                ["ComparerName"] = "",
                ["Parameters"] = new JsonArray(),
                ["InputNodeIDs"] = new JsonArray(),
                ["PassOutputs"] = new JsonArray(targetNodeId),
                ["FailOutputs"] = new JsonArray(),
                ["NodeID"] = nodeId,
                ["EditorPosition"] = CreateEditorPosition(position.x, position.y),
                ["OutputConnections"] = new JsonArray(CreateConnectionData(0, targetNodeId, toPortIndex)),
                ["IsChecked"] = false
            },
            "command" => new JsonObject
            {
                ["$type"] = "CommandNodeData, NekoGraph.Runtime",
                ["Command"] = new JsonObject
                {
                    ["$type"] = "CommandData, NekoGraph.Runtime",
                    ["CommandName"] = "",
                    ["Parameter"] = "",
                    ["Parameters"] = new JsonArray()
                },
                ["InputNodeIDs"] = new JsonArray(),
                ["OutputNodeIDs"] = new JsonArray(targetNodeId),
                ["NodeID"] = nodeId,
                ["EditorPosition"] = CreateEditorPosition(position.x, position.y),
                ["OutputConnections"] = new JsonArray(CreateConnectionData(0, targetNodeId, toPortIndex)),
                ["IsChecked"] = false
            },
            _ => throw new InvalidOperationException($"Unsupported unnamed node kind: {nodeKind}")
        };
    }

    private static JsonObject CreateEditorPosition(float x, float y) =>
        new()
        {
            ["$type"] = "SerializableVector2, NekoGraph.Runtime",
            ["x"] = x,
            ["y"] = y
        };

    private static JsonObject CreateConnectionData(int fromPortIndex, string targetNodeId, int toPortIndex) =>
        new()
        {
            ["$type"] = "ConnectionData, NekoGraph.Runtime",
            ["FromPortIndex"] = fromPortIndex,
            ["TargetNodeID"] = targetNodeId,
            ["ToPortIndex"] = toPortIndex
        };

    private static List<string> ReadOutgoingTargets(JsonObject? nodeObject)
    {
        if (nodeObject is null)
        {
            return [];
        }

        if (string.Equals(GetTypeName(nodeObject), "LeafNode_B_Data", StringComparison.Ordinal))
        {
            return [];
        }

        var result = new List<string>();
        AppendArrayStrings(nodeObject["_"], result);
        AppendArrayStrings(nodeObject["SignalOutputs"], result);
        AppendArrayStrings(nodeObject["OutputNodeIDs"], result);
        AppendArrayStrings(nodeObject["OutPutNodeIDs"], result);
        AppendArrayStrings(nodeObject["OutputNodeIds"], result);
        AppendArrayStrings(nodeObject["NextSpineNodeIDs"], result);
        AppendArrayStrings(nodeObject["PassOutputs"], result);
        AppendArrayStrings(nodeObject["FailOutputs"], result);

        if (nodeObject["OutputConnections"] is JsonArray connections)
        {
            foreach (var item in connections)
            {
                var targetNodeId = item?["TargetNodeID"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(targetNodeId))
                {
                    result.Add(targetNodeId);
                }
            }
        }

        return result
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static void ReplaceOutgoingTarget(
        JsonObject nodeObject,
        string oldTargetId,
        string newTargetId,
        int fromPortIndex,
        int? oldToPortIndex,
        int newToPortIndex)
    {
        foreach (var key in GetOutputArrayKeysForPort(nodeObject, fromPortIndex))
        {
            ReplaceArrayTarget(nodeObject[key], oldTargetId, newTargetId);
        }

        if (nodeObject["OutputConnections"] is JsonArray connections)
        {
            foreach (var item in connections.OfType<JsonObject>())
            {
                var targetNodeId = item["TargetNodeID"]?.GetValue<string>();
                var connectionFromPortIndex = item["FromPortIndex"]?.GetValue<int>() ?? 0;
                var connectionToPortIndex = item["ToPortIndex"]?.GetValue<int>() ?? 0;
                if (string.Equals(targetNodeId, oldTargetId, StringComparison.Ordinal) &&
                    connectionFromPortIndex == fromPortIndex &&
                    (!oldToPortIndex.HasValue || connectionToPortIndex == oldToPortIndex.Value))
                {
                    item["TargetNodeID"] = newTargetId;
                    item["ToPortIndex"] = newToPortIndex;
                }
            }
        }
    }

    private static void RemoveOutgoingTarget(JsonObject nodeObject, string targetNodeId, int fromPortIndex, int? toPortIndex)
    {
        foreach (var key in GetOutputArrayKeysForPort(nodeObject, fromPortIndex))
        {
            RemoveArrayTarget(nodeObject[key], targetNodeId);
        }

        if (nodeObject["OutputConnections"] is JsonArray connections)
        {
            for (var i = connections.Count - 1; i >= 0; i--)
            {
                var target = connections[i]?["TargetNodeID"]?.GetValue<string>();
                var connectionFromPortIndex = connections[i]?["FromPortIndex"]?.GetValue<int>() ?? 0;
                var connectionToPortIndex = connections[i]?["ToPortIndex"]?.GetValue<int>() ?? 0;
                if (string.Equals(target, targetNodeId, StringComparison.Ordinal) &&
                    connectionFromPortIndex == fromPortIndex &&
                    (!toPortIndex.HasValue || connectionToPortIndex == toPortIndex.Value))
                {
                    connections.RemoveAt(i);
                }
            }
        }
    }

    private static void AppendOutgoingTarget(JsonObject nodeObject, string targetNodeId, int fromPortIndex, int toPortIndex)
    {
        foreach (var key in GetOutputArrayKeysForPort(nodeObject, fromPortIndex))
        {
            AppendUniqueString(nodeObject, key, targetNodeId);
        }

        if (nodeObject["OutputConnections"] is JsonArray connections)
        {
            var alreadyExists = connections
                .OfType<JsonObject>()
                .Any(item =>
                    string.Equals(item["TargetNodeID"]?.GetValue<string>(), targetNodeId, StringComparison.Ordinal) &&
                    (item["FromPortIndex"]?.GetValue<int>() ?? 0) == fromPortIndex &&
                    (item["ToPortIndex"]?.GetValue<int>() ?? 0) == toPortIndex);

            if (!alreadyExists)
            {
                connections.Add(CreateConnectionData(fromPortIndex, targetNodeId, toPortIndex));
            }
        }
    }

    private static List<string> GetOutputArrayKeysForPort(JsonObject nodeObject, int fromPortIndex)
    {
        var schema = PortSchemaCatalog.GetSchema(GetTypeName(nodeObject));
        return schema.OutputFieldKeysByPort.TryGetValue(fromPortIndex, out var fieldKeys)
            ? fieldKeys
            : [];
    }

    private static int GetDefaultOutputPortIndex(JsonObject nodeObject)
    {
        return 0;
    }

    private static string GetTypeName(JsonObject nodeObject)
    {
        var rawTypeName = nodeObject["$type"]?.GetValue<string>() ?? string.Empty;
        var firstSegment = rawTypeName.Split(',')[0].Trim();
        var lastDot = firstSegment.LastIndexOf('.');
        return lastDot >= 0 ? firstSegment[(lastDot + 1)..] : firstSegment;
    }

    private static void AppendUniqueString(JsonObject nodeObject, string key, string value)
    {
        if (nodeObject[key] is not JsonArray array)
        {
            return;
        }

        if (!array.Any(item => string.Equals(item?.GetValue<string>(), value, StringComparison.Ordinal)))
        {
            array.Add(value);
        }
    }

    private static void ReplaceArrayTarget(JsonNode? node, string oldTargetId, string newTargetId)
    {
        if (node is not JsonArray array)
        {
            return;
        }

        for (var i = 0; i < array.Count; i++)
        {
            var value = array[i]?.GetValue<string>();
            if (string.Equals(value, oldTargetId, StringComparison.Ordinal))
            {
                array[i] = newTargetId;
            }
        }
    }

    private static void RemoveArrayTarget(JsonNode? node, string targetId)
    {
        if (node is not JsonArray array)
        {
            return;
        }

        for (var i = array.Count - 1; i >= 0; i--)
        {
            var value = array[i]?.GetValue<string>();
            if (string.Equals(value, targetId, StringComparison.Ordinal))
            {
                array.RemoveAt(i);
            }
        }
    }

    private static void AppendArrayStrings(JsonNode? node, List<string> result)
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
                result.Add(value);
            }
        }
    }

    private static List<string> FindIncomingSources(JsonObject nodes, string targetNodeId)
    {
        var result = new List<string>();
        foreach (var kvp in nodes)
        {
            if (kvp.Value is JsonObject nodeObject &&
                ReadOutgoingTargets(nodeObject).Contains(targetNodeId, StringComparer.Ordinal))
            {
                result.Add(kvp.Key);
            }
        }

        return result;
    }

    private static List<string>? FindUniqueBridgePath(EditContext context, string fromNodeId, string toNodeId)
    {
        var matches = new List<List<string>>();
        ExploreBridgePaths(context, fromNodeId, toNodeId, [fromNodeId], matches);
        return matches.Count == 1 ? matches[0] : null;
    }

    private static void ExploreBridgePaths(EditContext context, string currentNodeId, string targetNodeId, List<string> path, List<List<string>> matches)
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
        foreach (var nextNodeId in ReadOutgoingTargets(currentObject))
        {
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
            ExploreBridgePaths(context, nextNodeId, targetNodeId, path, matches);
            path.RemoveAt(path.Count - 1);
        }
    }

    private static (float x, float y) GetMidpoint(JsonObject? fromNode, JsonObject? toNode)
    {
        var from = ReadPosition(fromNode);
        var to = ReadPosition(toNode);
        return ((from.x + to.x) / 2f, (from.y + to.y) / 2f);
    }

    private static (float x, float y) ReadPosition(JsonObject? nodeObject)
    {
        if (nodeObject?["EditorPosition"] is not JsonObject position)
        {
            return (0f, 0f);
        }

        var x = position["x"]?.GetValue<float>() ?? 0f;
        var y = position["y"]?.GetValue<float>() ?? 0f;
        return (x, y);
    }

    internal static void TouchModifiedAt(JsonObject root)
    {
        root["ModifiedAt"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    internal static void SavePack(EditContext context)
    {
        File.WriteAllText(context.FilePath, JsonSerializer.Serialize(context.Root, JsonOptions.NodeWrite));
    }
}

internal sealed class EditContext
{
    public required string FilePath { get; init; }

    public required PackDocument Pack { get; init; }

    public required JsonObject Root { get; init; }

    public required JsonObject Nodes { get; init; }
}

internal sealed record UnnamedEditReport(
    string Command,
    string PackId,
    string SourceFile,
    string From,
    string To,
    string NodeKind,
    string NodeId);

internal sealed record DeepUnnamedEditReport(
    string Command,
    string PackId,
    string SourceFile,
    string From,
    string To,
    int DepthEdgeIndex,
    string NodeKind,
    string NodeId,
    string EdgeFromNodeId,
    string EdgeToNodeId);

internal sealed record CreateBridgeReport(
    string Command,
    string PackId,
    string SourceFile,
    string From,
    string To,
    List<string> NodeKinds,
    List<string> NodeIds);

internal sealed record DestroyBridgeReport(
    string Command,
    string PackId,
    string SourceFile,
    string From,
    string To,
    List<string> RemovedNodeIds);
