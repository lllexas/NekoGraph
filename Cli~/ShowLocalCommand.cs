using System.Text.Json;

namespace NekoGraph.Cli;

internal static class ShowLocalCommand
{
    public static int ExecuteBridge(string packId, string fromNodeRef, string toNodeRef, int? fromPortIndex, int? toPortIndex)
    {
        try
        {
            var report = LoadReport(packId, out var errorMessage, out var pack);
            if (report is null || pack is null)
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            if (!BridgeQuery.TryResolveNodeRef(pack, fromNodeRef, out var fromNode) ||
                !BridgeQuery.TryResolveNodeRef(pack, toNodeRef, out var toNode))
            {
                Console.Error.WriteLine($"Node ref was not found. from={fromNodeRef}, to={toNodeRef}");
                return 1;
            }

            var context = LoadEditContext(packId, out errorMessage);
            if (context is null)
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            var fromNodeObject = context.Nodes[fromNode.NodeId]?.AsObject();
            var toNodeObject = context.Nodes[toNode.NodeId]?.AsObject();
            if (fromNodeObject is null || toNodeObject is null)
            {
                Console.Error.WriteLine("Node JSON was not found for bridge query.");
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

            var nodes = new List<NodeLink>();
            foreach (var nodeId in bridgePath)
            {
                if (pack.Nodes.TryGetValue(nodeId, out var node))
                {
                    nodes.Add(CreateNodeLink(node));
                }
                else
                {
                    nodes.Add(new NodeLink(nodeId, nodeId));
                }
            }

            var segments = new List<BridgeSegmentReport>();
            for (var i = 0; i < bridgePath.Count - 1; i++)
            {
                var fromId = bridgePath[i];
                var nextId = bridgePath[i + 1];
                var fromLink = nodes[i];
                var toLink = nodes[i + 1];

                segments.Add(new BridgeSegmentReport(
                    i,
                    fromLink,
                    toLink,
                    BuildSegmentKind(pack, fromId, nextId)));
            }

            // Calculate unnamed node count and bridge type
            // bridgePath includes from-node and to-node, so unnamed nodes = total - 2
            var unnamedNodeCount = bridgePath.Count - 2;
            var bridgeType = unnamedNodeCount switch
            {
                0 => "direct",    // 直连：两个具名节点直接相连，中间没有匿名节点
                _ => "bridged"    // 桥接：中间有≥1 个匿名节点
            };

            var payload = new BridgeQueryReport(
                "query-bridge",
                packId,
                fromNodeRef,
                toNodeRef,
                effectiveFromPortIndex,
                effectiveToPortIndex,
                unnamedNodeCount,
                bridgeType,
                nodes,
                segments);

            Console.Out.WriteLine(JsonSerializer.Serialize(payload, JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"query-bridge failed: {ex.Message}");
            return 1;
        }
    }

    public static int ExecuteNode(string packId, string namedNodeRef)
    {
        try
        {
            var report = LoadReport(packId, out var errorMessage, out var pack);
            if (report is null || pack is null)
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            var node = pack.Nodes.Values.FirstOrDefault(candidate =>
                string.Equals(NamedNodeRef.TryBuild(candidate), namedNodeRef, StringComparison.Ordinal));

            if (node is null)
            {
                var known = pack.Nodes.Values
                    .Select(NamedNodeRef.TryBuild)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList();
                Console.Error.WriteLine($"Named node '{namedNodeRef}' was not found in pack '{packId}'.");
                Console.Error.WriteLine($"Known named nodes: {string.Join(", ", known)}");
                return 1;
            }

            var relatedPaths = report.Paths
                .Where(path => path.Debug.NodeIds.Contains(node.NodeId, StringComparer.Ordinal))
                .OrderBy(path => path.SemanticPath.Length)
                .ThenBy(path => path.SemanticPath, StringComparer.Ordinal)
                .ToList();

            var incomingNamedNodes = new List<NodeLink>();
            var outgoingNamedNodes = new List<NodeLink>();

            foreach (var path in relatedPaths)
            {
                var index = path.Debug.NodeIds.FindIndex(id => string.Equals(id, node.NodeId, StringComparison.Ordinal));
                if (index < 0)
                {
                    continue;
                }

                for (var i = index - 1; i >= 0; i--)
                {
                    if (pack.Nodes.TryGetValue(path.Debug.NodeIds[i], out var previousNode))
                    {
                        var accessKey = NamedNodeRef.TryBuild(previousNode);
                        if (!string.IsNullOrWhiteSpace(accessKey))
                        {
                            incomingNamedNodes.Add(CreateNodeLink(previousNode));
                            break;
                        }
                    }
                }

                for (var i = index + 1; i < path.Debug.NodeIds.Count; i++)
                {
                    if (pack.Nodes.TryGetValue(path.Debug.NodeIds[i], out var nextNode))
                    {
                        var accessKey = NamedNodeRef.TryBuild(nextNode);
                        if (!string.IsNullOrWhiteSpace(accessKey))
                        {
                            outgoingNamedNodes.Add(CreateNodeLink(nextNode));
                            break;
                        }
                    }
                }
            }

            var payload = new NodeLocalReport(
                "show-node",
                packId,
                namedNodeRef,
                node.NodeId,
                BuildNodeLabel(node),
                node.TypeName,
                node.FieldSummary,
                incomingNamedNodes
                    .DistinctBy(link => link.AccessKey ?? link.NodeId)
                    .ToList(),
                outgoingNamedNodes
                    .DistinctBy(link => link.AccessKey ?? link.NodeId)
                    .ToList(),
                relatedPaths.Select(path => path.SemanticPath).Distinct(StringComparer.Ordinal).Take(6).ToList(),
                new NodeLocalDebug(
                    CreateNodeLink(node),
                    node.OutgoingNodeIds.Select(targetId => pack.Nodes.TryGetValue(targetId, out var targetNode)
                        ? CreateNodeLink(targetNode)
                        : new NodeLink(targetId, targetId))
                        .ToList()));

            Console.Out.WriteLine(JsonSerializer.Serialize(payload, JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"show-node failed: {ex.Message}");
            return 1;
        }
    }

    public static int ExecuteProcess(string packId, string processId)
    {
        try
        {
            var report = LoadReport(packId, out var errorMessage, out _);
            if (report is null)
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            var processView = report.ProcessViews.FirstOrDefault(view =>
                string.Equals(view.ProcessId, processId, StringComparison.Ordinal) ||
                string.Equals(view.AccessKey, $"process:{processId}", StringComparison.Ordinal));

            if (processView is null)
            {
                var known = report.ProcessViews
                    .Select(view => view.ProcessId)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList();
                Console.Error.WriteLine($"Process '{processId}' was not found in pack '{packId}'.");
                Console.Error.WriteLine($"Known processes: {string.Join(", ", known)}");
                return 1;
            }

            var payload = new ProcessLocalReport(
                "show-process",
                packId,
                processView.AccessKey,
                processView.ProcessId,
                processView.Summary,
                processView.BackbonePath,
                processView.SegmentPaths,
                processView.BusinessNodes,
                processView.Blockers,
                processView.RelatedSemanticPaths,
                processView.RuntimeStatus,
                processView.Debug);

            Console.Out.WriteLine(JsonSerializer.Serialize(payload, JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"show-process failed: {ex.Message}");
            return 1;
        }
    }

    public static int ExecuteMission(string packId, string missionId)
    {
        try
        {
            var report = LoadReport(packId, out var errorMessage, out _);
            if (report is null)
            {
                Console.Error.WriteLine(errorMessage);
                return 1;
            }

            var missionView = report.MissionViews.FirstOrDefault(view =>
                string.Equals(view.MissionId, missionId, StringComparison.Ordinal) ||
                string.Equals(view.AccessKey, $"mission:{missionId}", StringComparison.Ordinal));

            if (missionView is null)
            {
                var known = report.MissionViews
                    .Select(view => view.MissionId)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList();
                Console.Error.WriteLine($"Mission '{missionId}' was not found in pack '{packId}'.");
                Console.Error.WriteLine($"Known missions: {string.Join(", ", known)}");
                return 1;
            }

            var payload = new MissionLocalReport(
                "show-mission",
                packId,
                missionView.AccessKey,
                missionView.MissionId,
                missionView.Summary,
                missionView.Composition,
                missionView.ProcessIds,
                missionView.MissionNodes,
                missionView.BusinessNodes,
                missionView.Blockers,
                missionView.RelatedSemanticPaths,
                missionView.RuntimeStatus,
                missionView.Debug);

            Console.Out.WriteLine(JsonSerializer.Serialize(payload, JsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"show-mission failed: {ex.Message}");
            return 1;
        }
    }

    private static RunFullReport? LoadReport(string packId, out string? errorMessage, out PackDocument? packDocument)
    {
        errorMessage = null;
        packDocument = null;

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
        if (!pack.Success)
        {
            errorMessage = pack.ErrorMessage;
            return null;
        }

        packDocument = pack.Value!;
        return RunFullReportBuilder.Build(packDocument);
    }

    private static EditContext? LoadEditContext(string packId, out string? errorMessage)
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

        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(resolution.FilePath!))?.AsObject();
        var nodes = root?["Nodes"]?.AsObject();
        if (root is null || nodes is null)
        {
            errorMessage = $"Pack '{packId}' could not be parsed for bridge query.";
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

    private static string BuildSegmentKind(PackDocument pack, string fromId, string toId)
    {
        var fromNamed = pack.Nodes.TryGetValue(fromId, out var fromNode) ? NamedNodeRef.TryBuild(fromNode) : null;
        var toNamed = pack.Nodes.TryGetValue(toId, out var toNode) ? NamedNodeRef.TryBuild(toNode) : null;

        if (!string.IsNullOrWhiteSpace(fromNamed) && string.IsNullOrWhiteSpace(toNamed))
        {
            return "named-to-unnamed";
        }

        if (string.IsNullOrWhiteSpace(fromNamed) && string.IsNullOrWhiteSpace(toNamed))
        {
            return "unnamed-to-unnamed";
        }

        if (string.IsNullOrWhiteSpace(fromNamed) && !string.IsNullOrWhiteSpace(toNamed))
        {
            return "unnamed-to-named";
        }

        return "named-to-named";
    }

    private static string BuildNodeLabel(PackNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.DisplayName) && !string.Equals(node.DisplayName, node.NodeId, StringComparison.Ordinal))
        {
            return $"{node.DisplayName} [{ShortType(node.TypeName)}]";
        }

        var accessKey = NamedNodeRef.TryBuild(node);
        if (!string.IsNullOrWhiteSpace(accessKey))
        {
            return accessKey switch
            {
                var key when key.StartsWith("spine:", StringComparison.Ordinal) => $"Spine({node.ProcessId ?? node.NodeId})",
                var key when key.StartsWith("leaf-a:", StringComparison.Ordinal) => $"LeafA({node.ProcessId ?? node.NodeId})",
                var key when key.StartsWith("leaf-b:", StringComparison.Ordinal) => $"LeafB({node.ProcessId ?? node.NodeId})",
                var key when key.StartsWith("mission-a:", StringComparison.Ordinal) => $"MissionA({node.MissionId ?? node.NodeId})",
                var key when key.StartsWith("mission-s:", StringComparison.Ordinal) => $"MissionS({node.MissionId ?? node.NodeId})",
                var key when key.StartsWith("mission-f:", StringComparison.Ordinal) => $"MissionF({node.MissionId ?? node.NodeId})",
                var key when key.StartsWith("mission-r:", StringComparison.Ordinal) => $"MissionR({node.MissionId ?? node.NodeId})",
                _ => node.NodeId
            };
        }

        if (node.TypeName.Contains("TriggerNode", StringComparison.OrdinalIgnoreCase))
        {
            return $"Trigger({node.TriggerEvent ?? "Unknown"})";
        }

        if (node.TypeName.Contains("ComparerNode", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(node.ComparerName)
                ? $"Comparer({node.NodeId})"
                : $"Comparer({node.ComparerName}{FormatComparerParameters(node.ComparerParameters)})";
        }

        if (node.TypeName.Contains("CommandNode", StringComparison.OrdinalIgnoreCase))
        {
            return $"Command({node.CommandName ?? node.NodeId})";
        }

        if (node.TypeName.Contains("RootNode", StringComparison.OrdinalIgnoreCase))
        {
            return "Root";
        }

        return $"{ShortType(node.TypeName)}({node.NodeId})";
    }

    private static NodeLink CreateNodeLink(PackNode node) =>
        new(node.NodeId, BuildNodeLabel(node), NamedNodeRef.TryBuild(node));

    private static string ShortType(string typeName)
    {
        return typeName switch
        {
            var value when value.Contains("RootNode", StringComparison.OrdinalIgnoreCase) => "Root",
            var value when value.Contains("NodeData", StringComparison.OrdinalIgnoreCase) => value.Replace("NodeData", string.Empty, StringComparison.OrdinalIgnoreCase).Trim('_'),
            _ => typeName
        };
    }

    private static string FormatComparerParameters(List<string> parameters)
    {
        return parameters.Count == 0
            ? string.Empty
            : $" {string.Join(' ', parameters)}";
    }
}

internal sealed record ProcessLocalReport(
    string Command,
    string PackId,
    string AccessKey,
    string ProcessId,
    string Summary,
    string BackbonePath,
    List<string> SegmentPaths,
    List<string> BusinessNodes,
    List<string> Blockers,
    List<string> RelatedSemanticPaths,
    string RuntimeStatus,
    ProcessViewDebug Debug);

internal sealed record MissionLocalReport(
    string Command,
    string PackId,
    string AccessKey,
    string MissionId,
    string Summary,
    string Composition,
    List<string> ProcessIds,
    List<string> MissionNodes,
    List<string> BusinessNodes,
    List<string> Blockers,
    List<string> RelatedSemanticPaths,
    string RuntimeStatus,
    MissionViewDebug Debug);

internal sealed record NodeLocalReport(
    string Command,
    string PackId,
    string AccessKey,
    string NodeId,
    string Label,
    string TypeName,
    Dictionary<string, string> Fields,
    List<NodeLink> IncomingNamedNodes,
    List<NodeLink> OutgoingNamedNodes,
    List<string> RelatedSemanticPaths,
    NodeLocalDebug Debug);

internal sealed record NodeLocalDebug(
    NodeLink Node,
    List<NodeLink> RawOutgoingNodes);

internal sealed record BridgeQueryReport(
    string Command,
    string PackId,
    string From,
    string To,
    int? FromPortIndex,
    int? ToPortIndex,
    int UnnamedNodeCount,
    string BridgeType,
    List<NodeLink> Nodes,
    List<BridgeSegmentReport> Segments);

internal sealed record BridgeSegmentReport(
    int DepthEdgeIndex,
    NodeLink From,
    NodeLink To,
    string SegmentKind);
