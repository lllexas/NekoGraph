namespace NekoGraph.Cli;

internal static class RunFullReportBuilder
{
    private static PackDocument? _currentPack;

    public static RunFullReport Build(PackDocument pack)
    {
        _currentPack = pack;
        try
        {
            var paths = new List<RunPathReport>();
            var blockers = new List<BlockerReport>();
            var findings = new List<FindingReport>();
            var processIndex = BuildProcessIndex(pack);
            var reached = new HashSet<string>(StringComparer.Ordinal);
            var structurallyReachable = ComputeStructuralReachability(pack, processIndex);
            var firstPathToNode = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var queue = new Queue<PendingPath>();
            var maxDepth = Math.Max(pack.Nodes.Count * 4, 16);

            queue.Enqueue(new PendingPath([pack.RootNodeId], []));

            while (queue.Count > 0)
            {
                var pendingPath = queue.Dequeue();
                var currentPath = pendingPath.NodeIds;
                var currentTransitions = pendingPath.Transitions;
                var currentId = currentPath[^1];

                if (!pack.Nodes.TryGetValue(currentId, out var node))
                {
                    paths.Add(CreatePath(currentPath, currentTransitions, "error", "missing-reference", $"Node '{currentId}' does not exist."));
                    blockers.Add(new BlockerReport(currentId, "missing-reference", "Missing node reference.", [], currentPath));
                    continue;
                }

                reached.Add(currentId);
                if (!firstPathToNode.ContainsKey(currentId))
                {
                    firstPathToNode[currentId] = [.. currentPath];
                }

                if (currentPath.Count > maxDepth)
                {
                    paths.Add(CreatePath(currentPath, currentTransitions, "blocked", "cycle-guard-stop", "Signal expansion hit the depth guard."));
                    blockers.Add(new BlockerReport(node.NodeId, "cycle-guard-stop", "Depth guard stopped further expansion.", [], currentPath));
                    continue;
                }

                var runtimeEdges = GetRuntimeEdges(pack, node, processIndex);
                var runtimeOutgoing = runtimeEdges.Select(edge => edge.ToNodeId).Distinct(StringComparer.Ordinal).ToList();

                if (IsTrigger(node))
                {
                    var explanation = $"Trigger waits for external event '{node.TriggerEvent ?? "Unknown"}'.";
                    blockers.Add(new BlockerReport(node.NodeId, "event-wait", explanation, runtimeOutgoing, currentPath));
                }

                if (IsExplicitWait(node))
                {
                    const string explanation = "Node is modeled as an explicit wait point and needs external continuation.";
                    paths.Add(CreatePath(currentPath, currentTransitions, "blocked", "wait-node", explanation));
                    blockers.Add(new BlockerReport(node.NodeId, "wait-node", explanation, runtimeOutgoing, currentPath));
                    continue;
                }

                if (IsCommand(node) && string.IsNullOrWhiteSpace(node.CommandName))
                {
                    const string explanation = "Command node has no CommandName and will act as a broken execution point.";
                    paths.Add(CreatePath(currentPath, currentTransitions, "blocked", "command-failure", explanation));
                    blockers.Add(new BlockerReport(node.NodeId, "command-failure", explanation, runtimeOutgoing, currentPath));
                    continue;
                }

                if (IsDestroy(node))
                {
                    paths.Add(CreatePath(currentPath, currentTransitions, "terminated", "destroy", "Destroy node unloads the current pack instance."));
                    continue;
                }

                if (runtimeEdges.Count == 0)
                {
                    paths.Add(CreatePath(currentPath, currentTransitions, "terminated", "dead_end", "Node has no outgoing signal path."));
                    continue;
                }

                var expanded = false;
                foreach (var runtimeEdge in runtimeEdges)
                {
                    var nextNodeId = runtimeEdge.ToNodeId;
                    if (currentPath.Contains(nextNodeId, StringComparer.Ordinal))
                    {
                        var loopPath = new List<string>(currentPath) { nextNodeId };
                        var loopTransitions = new List<PathTransition>(currentTransitions) { new(runtimeEdge.FromNodeId, runtimeEdge.ToNodeId, runtimeEdge.EdgeType) };
                        paths.Add(CreatePath(loopPath, loopTransitions, "blocked", "cycle-guard-stop", $"Signal would revisit '{nextNodeId}'."));
                        blockers.Add(new BlockerReport(nextNodeId, "cycle-guard-stop", "Cycle detected in signal path.", [], loopPath));
                        continue;
                    }

                    var nextPath = new List<string>(currentPath) { nextNodeId };
                    var nextTransitions = new List<PathTransition>(currentTransitions) { new(runtimeEdge.FromNodeId, runtimeEdge.ToNodeId, runtimeEdge.EdgeType) };
                    queue.Enqueue(new PendingPath(nextPath, nextTransitions));
                    expanded = true;
                }

                if (!expanded)
                {
                    paths.Add(CreatePath(currentPath, currentTransitions, "terminated", "dead_end", "Node had no expandable outgoing path."));
                }
            }

            foreach (var node in pack.Nodes.Values.Where(node => !structurallyReachable.Contains(node.NodeId)))
            {
                findings.Add(new FindingReport(
                    "unreachable",
                    node.NodeId,
                    $"{node.DisplayName} is unreachable from root.",
                    "Connect it to an upstream signal path or remove the orphaned subgraph."));
            }

            foreach (var node in pack.Nodes.Values.Where(node => structurallyReachable.Contains(node.NodeId) && !reached.Contains(node.NodeId)))
            {
                findings.Add(new FindingReport(
                    "deferred-reachability",
                    node.NodeId,
                    $"{node.DisplayName} is downstream of a blocker and is not reached in the current run.",
                    "Inspect upstream blocking points to understand how this node becomes reachable."));
            }

            foreach (var node in pack.Nodes.Values.Where(node => GetRuntimeOutgoing(pack, node, processIndex).Any(target => !pack.Nodes.ContainsKey(target))))
            {
                findings.Add(new FindingReport(
                    "broken-edge",
                    node.NodeId,
                    $"{node.DisplayName} points to a missing node.",
                    "Repair or remove the broken outgoing edge."));
            }

            foreach (var node in pack.Nodes.Values.Where(IsComparer))
            {
                findings.Add(new FindingReport(
                    "conditional-branch",
                    node.NodeId,
                    $"{node.DisplayName} represents a runtime condition branch.",
                    "Interpret both pass and fail outputs as possible downstream paths unless payload constraints are known."));
            }

            return new RunFullReport
            {
                Command = "run-full",
                PackId = pack.PackId,
                SourceFile = pack.FilePath,
                RootNodeId = pack.RootNodeId,
                Overview = BuildOverview(pack, reached, structurallyReachable, blockers, paths),
                Summary = new RunSummary(
                    pack.Nodes.Count,
                    pack.Nodes.Values.Sum(node => GetRuntimeOutgoing(pack, node, processIndex).Count),
                    reached.Count,
                    pack.Nodes.Count - structurallyReachable.Count,
                    structurallyReachable.Count,
                    blockers.Count,
                    paths.Count(path => path.Status == "terminated"),
                    paths.Count),
                ExecutionModel = new ExecutionModel(
                    "Root signal injection",
                    "Trigger nodes are reported as blocking points but do not stop full-run path expansion. Explicit wait nodes still stop the current path.",
                    "Command nodes propagate unless CommandName is missing.",
                    "Destroy nodes terminate the path immediately.",
                    "Spine is a relay callback wrapper: Spine ~> LeafA activates a process segment, and LeafB ~> NextSpine relays completion to the next process segment.",
                    "Path transitions use edge tags: flow = ordinary edge, relay_callback = Spine/Leaf callback relay, comparer_pass/comparer_fail = comparer branch selection."),
                NodeProfiles = BuildNodeProfiles(pack),
                Reachability = pack.Nodes.Values
                    .Select(node => new ReachabilityEntry(
                        node.NodeId,
                        reached.Contains(node.NodeId),
                        structurallyReachable.Contains(node.NodeId),
                        firstPathToNode.TryGetValue(node.NodeId, out var path) ? path : null))
                    .OrderBy(entry => entry.NodeId)
                    .ToList(),
                ProcessViews = BuildProcessViews(pack, processIndex, reached, blockers, paths),
                MissionViews = BuildMissionViews(pack, processIndex, reached, blockers, paths),
                SpineBackbone = BuildSpineBackbone(pack, processIndex),
                LeafSegments = BuildLeafSegments(pack, processIndex, reached),
                MissionGroups = BuildMissionGroups(pack, processIndex, reached),
                Comparers = BuildComparers(pack, reached),
                TriggerListeners = BuildTriggerListeners(pack, reached),
                BlockingPoints = blockers
                    .DistinctBy(blocker => $"{blocker.NodeId}:{blocker.BlockerType}:{string.Join(">", blocker.Path)}")
                    .ToList(),
                Paths = paths,
                Findings = findings
            };
        }
        finally
        {
            _currentPack = null;
        }
    }

    private static OverviewBlock BuildOverview(
        PackDocument pack,
        HashSet<string> reached,
        HashSet<string> structurallyReachable,
        List<BlockerReport> blockers,
        List<RunPathReport> paths)
    {
        var terminatedPaths = paths.Count(path => path.Status == "terminated");
        var blockedPaths = paths.Count(path => path.Status == "blocked");
        var unreachableNodes = pack.Nodes.Count - structurallyReachable.Count;

        return new OverviewBlock(
            $"Pack '{pack.PackId}' expands from root '{pack.RootNodeId}' into {paths.Count} full-run paths.",
            $"{reached.Count} / {pack.Nodes.Count} nodes are runtime-reachable in full-run mode; {unreachableNodes} nodes are structurally unreachable.",
            $"{blockers.Count} blocking points were marked without stopping trigger paths; {terminatedPaths} paths terminated naturally and {blockedPaths} paths stopped hard.",
            "Read Paths together with Transitions: edge tags explain whether a hop is ordinary flow, Spine relay callback, or comparer branch selection.");
    }

    private static Dictionary<string, ProcessGroup> BuildProcessIndex(PackDocument pack)
    {
        var result = new Dictionary<string, ProcessGroup>(StringComparer.Ordinal);

        foreach (var node in pack.Nodes.Values)
        {
            if (string.IsNullOrWhiteSpace(node.ProcessId))
            {
                continue;
            }

            if (!result.TryGetValue(node.ProcessId, out var group))
            {
                group = new ProcessGroup();
                result[node.ProcessId] = group;
            }

            if (IsSpine(node))
            {
                group.SpineIds.Add(node.NodeId);
            }
            else if (IsLeafA(node))
            {
                group.LeafAIds.Add(node.NodeId);
            }
            else if (IsLeafB(node))
            {
                group.LeafBIds.Add(node.NodeId);
            }
        }

        return result;
    }

    private static List<SpineBackboneEntry> BuildSpineBackbone(
        PackDocument pack,
        Dictionary<string, ProcessGroup> processIndex)
    {
        var result = new List<SpineBackboneEntry>();

        foreach (var spineNode in pack.Nodes.Values.Where(IsSpine).OrderBy(node => node.NodeId))
        {
            var activatedLeafA = new List<string>();
            var callbackLeafB = new List<string>();
            var nextSpines = new List<string>();

            if (!string.IsNullOrWhiteSpace(spineNode.ProcessId) &&
                processIndex.TryGetValue(spineNode.ProcessId, out var processGroup))
            {
                activatedLeafA.AddRange(processGroup.LeafAIds);
                callbackLeafB.AddRange(processGroup.LeafBIds);
            }

            nextSpines.AddRange(
                spineNode.OutgoingNodeIds
                    .Where(nextId => pack.Nodes.TryGetValue(nextId, out var nextNode) && IsSpine(nextNode)));

            result.Add(new SpineBackboneEntry(
                spineNode.NodeId,
                BuildNodeLabel(spineNode),
                spineNode.ProcessId,
                activatedLeafA,
                activatedLeafA.Select(leafAId => new NodeLink(leafAId, BuildNodeLabel(pack, leafAId))).ToList(),
                callbackLeafB,
                callbackLeafB.Select(leafBId => new NodeLink(leafBId, BuildNodeLabel(pack, leafBId))).ToList(),
                nextSpines,
                nextSpines.Select(nextSpineId => new NodeLink(nextSpineId, BuildNodeLabel(pack, nextSpineId))).ToList(),
                nextSpines.Count > 1 ? "callback-fan-out" : "callback-relay"));
        }

        return result;
    }

    private static List<ProcessViewEntry> BuildProcessViews(
        PackDocument pack,
        Dictionary<string, ProcessGroup> processIndex,
        HashSet<string> reached,
        List<BlockerReport> blockers,
        List<RunPathReport> paths)
    {
        var result = new List<ProcessViewEntry>();

        foreach (var kvp in processIndex.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var processId = kvp.Key;
            var group = kvp.Value;
            var processNodeIds = CollectProcessNodeIds(pack, processId, group);
            var businessNodeIds = processNodeIds
                .Where(nodeId => pack.Nodes.TryGetValue(nodeId, out var node) && (IsTrigger(node) || IsComparer(node) || IsCommand(node) || node.MissionId is not null))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var localPaths = paths
                .Where(path => path.Debug.NodeIds.Any(processNodeIds.Contains))
                .Select(path => path.SemanticPath)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var localBlockers = blockers
                .Where(blocker => blocker.Path.Any(processNodeIds.Contains))
                .Select(blocker => new ProcessBlockerEntry(
                    blocker.NodeId,
                    BuildNodeLabel(pack, blocker.NodeId),
                    blocker.BlockerType,
                    blocker.Explanation))
                .DistinctBy(entry => $"{entry.NodeId}:{entry.BlockerType}")
                .ToList();

            var backbonePath = BuildProcessBackbonePath(pack, group);
            var segmentPaths = BuildProcessSegmentPaths(pack, processId, group);

            result.Add(new ProcessViewEntry(
                $"process:{processId}",
                processId,
                BuildProcessSummary(processId, group, businessNodeIds, localBlockers),
                backbonePath,
                segmentPaths,
                SummarizeSemanticPaths(localPaths, 3),
                SummarizeNodeLabels(pack, businessNodeIds, 8),
                SummarizeBlockers(localBlockers, 6),
                processNodeIds.All(reached.Contains) ? "fully-observed" :
                    processNodeIds.Any(reached.Contains) ? "partially-observed" :
                    "not-observed",
                new ProcessViewDebug(
                    group.SpineIds.Select(spineId => new NodeLink(spineId, BuildNodeLabel(pack, spineId))).ToList(),
                    group.LeafAIds.Select(leafAId => new NodeLink(leafAId, BuildNodeLabel(pack, leafAId))).ToList(),
                    group.LeafBIds.Select(leafBId => new NodeLink(leafBId, BuildNodeLabel(pack, leafBId))).ToList(),
                    businessNodeIds.Select(nodeId => new NodeLink(nodeId, BuildNodeLabel(pack, nodeId))).ToList(),
                    localBlockers)));
        }

        return result;
    }

    private static List<MissionViewEntry> BuildMissionViews(
        PackDocument pack,
        Dictionary<string, ProcessGroup> processIndex,
        HashSet<string> reached,
        List<BlockerReport> blockers,
        List<RunPathReport> paths)
    {
        var missionGroups = pack.Nodes.Values
            .Where(node => !string.IsNullOrWhiteSpace(node.MissionId))
            .GroupBy(node => node.MissionId!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        var result = new List<MissionViewEntry>();

        foreach (var group in missionGroups)
        {
            var missionId = group.Key;
            var missionNodes = group.ToList();
            var missionNodeIds = missionNodes.Select(node => node.NodeId).ToHashSet(StringComparer.Ordinal);
            var relatedPaths = paths
                .Where(path => path.Debug.NodeIds.Any(missionNodeIds.Contains))
                .Select(path => path.SemanticPath)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var relatedBlockers = blockers
                .Where(blocker => blocker.Path.Any(missionNodeIds.Contains) || blocker.UnlocksTo.Any(missionNodeIds.Contains))
                .Select(blocker => new ProcessBlockerEntry(
                    blocker.NodeId,
                    BuildNodeLabel(pack, blocker.NodeId),
                    blocker.BlockerType,
                    blocker.Explanation))
                .DistinctBy(entry => $"{entry.NodeId}:{entry.BlockerType}")
                .ToList();
            var processIds = InferMissionProcessIds(pack, processIndex, missionNodes);
            var businessNodes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var processId in processIds)
            {
                if (processIndex.TryGetValue(processId, out var processGroup))
                {
                    foreach (var nodeId in CollectProcessNodeIds(pack, processId, processGroup))
                    {
                        if (pack.Nodes.TryGetValue(nodeId, out var node) && (IsTrigger(node) || IsComparer(node) || IsCommand(node)))
                        {
                            businessNodes.Add(nodeId);
                        }
                    }
                }
            }

            result.Add(new MissionViewEntry(
                $"mission:{missionId}",
                missionId,
                BuildMissionSummary(missionId, missionNodes, processIds, relatedBlockers),
                BuildMissionComposition(missionNodes),
                processIds,
                SummarizeSemanticPaths(relatedPaths, 3),
                missionNodes.Select(node => BuildNodeLabel(node)).Distinct(StringComparer.Ordinal).ToList(),
                SummarizeNodeLabels(pack, businessNodes, 8),
                SummarizeBlockers(relatedBlockers, 6),
                missionNodes.Any(node => reached.Contains(node.NodeId)) ? "mission-path-observed" : "mission-path-not-observed",
                new MissionViewDebug(
                    missionNodes.Select(node => new NodeLink(node.NodeId, BuildNodeLabel(node))).ToList(),
                    businessNodes.Select(nodeId => new NodeLink(nodeId, BuildNodeLabel(pack, nodeId))).ToList(),
                    relatedBlockers)));
        }

        return result;
    }

    private static string BuildProcessSummary(
        string processId,
        ProcessGroup group,
        List<string> businessNodeIds,
        List<ProcessBlockerEntry> blockers)
    {
        var spineCount = group.SpineIds.Count;
        var leafACount = group.LeafAIds.Count;
        var leafBCount = group.LeafBIds.Count;
        var businessCount = businessNodeIds.Count;
        var blockerCount = blockers.Count;

        return $"Process '{processId}' has {spineCount} spine, {leafACount} LeafA, {leafBCount} LeafB, {businessCount} business nodes, and {blockerCount} blocking points.";
    }

    private static string BuildMissionSummary(
        string missionId,
        List<PackNode> missionNodes,
        List<string> processIds,
        List<ProcessBlockerEntry> blockers)
    {
        var composition = BuildMissionComposition(missionNodes);
        return $"Mission '{missionId}' composes {composition}, appears in {processIds.Count} process scope(s), and touches {blockers.Count} blocking points.";
    }

    private static List<string> SummarizeSemanticPaths(List<string> semanticPaths, int maxCount)
    {
        return semanticPaths
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path.Length)
            .ThenBy(path => path, StringComparer.Ordinal)
            .Take(maxCount)
            .ToList();
    }

    private static List<string> SummarizeNodeLabels(PackDocument pack, IEnumerable<string> nodeIds, int maxCount)
    {
        return nodeIds
            .Select(nodeId => BuildNodeLabel(pack, nodeId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(label => label, StringComparer.Ordinal)
            .Take(maxCount)
            .ToList();
    }

    private static List<string> SummarizeBlockers(List<ProcessBlockerEntry> blockers, int maxCount)
    {
        return blockers
            .Select(blocker => $"{blocker.Label} [{blocker.BlockerType}]")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(text => text, StringComparer.Ordinal)
            .Take(maxCount)
            .ToList();
    }

    private static HashSet<string> CollectProcessNodeIds(PackDocument pack, string processId, ProcessGroup group)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spineId in group.SpineIds)
        {
            result.Add(spineId);
        }
        foreach (var leafAId in group.LeafAIds)
        {
            if (!pack.Nodes.TryGetValue(leafAId, out var leafA))
            {
                continue;
            }

            result.Add(leafAId);
            var queue = new Queue<string>(leafA.OutgoingNodeIds);
            var visited = new HashSet<string>(StringComparer.Ordinal);

            while (queue.Count > 0)
            {
                var currentId = queue.Dequeue();
                if (!visited.Add(currentId) || !result.Add(currentId))
                {
                    continue;
                }

                if (!pack.Nodes.TryGetValue(currentId, out var currentNode))
                {
                    continue;
                }

                if (IsLeafB(currentNode))
                {
                    continue;
                }

                foreach (var nextId in currentNode.OutgoingNodeIds)
                {
                    queue.Enqueue(nextId);
                }
            }
        }

        foreach (var leafBId in group.LeafBIds)
        {
            result.Add(leafBId);
        }

        return result;
    }

    private static string BuildProcessBackbonePath(PackDocument pack, ProcessGroup group)
    {
        var spines = group.SpineIds
            .Select(spineId => BuildNodeLabel(pack, spineId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return spines.Count == 0 ? string.Empty : string.Join(" -> ", spines);
    }

    private static List<string> BuildProcessSegmentPaths(PackDocument pack, string processId, ProcessGroup group)
    {
        var result = new List<string>();
        foreach (var leafAId in group.LeafAIds)
        {
            if (!pack.Nodes.TryGetValue(leafAId, out var leafA))
            {
                continue;
            }

            var segmentNodes = new List<string> { BuildNodeLabel(leafA) };
            foreach (var nextId in leafA.OutgoingNodeIds)
            {
                segmentNodes.Add(BuildNodeLabel(pack, nextId));
            }

            foreach (var leafBId in group.LeafBIds)
            {
                segmentNodes.Add(BuildNodeLabel(pack, leafBId));
            }

            result.Add(string.Join(" -> ", segmentNodes.Distinct(StringComparer.Ordinal)));
        }

        if (result.Count == 0)
        {
            result.Add($"Process {processId} has no LeafA -> ... -> LeafB segment.");
        }

        return result;
    }

    private static List<LeafSegmentEntry> BuildLeafSegments(
        PackDocument pack,
        Dictionary<string, ProcessGroup> processIndex,
        HashSet<string> reached)
    {
        var result = new List<LeafSegmentEntry>();

        foreach (var kvp in processIndex.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var processId = kvp.Key;
            var group = kvp.Value;

            var entryFromSpines = pack.Nodes.Values
                .Where(node => IsSpine(node) && node.OutgoingNodeIds.Any(nextId => group.SpineIds.Contains(nextId, StringComparer.Ordinal)))
                .Select(node => node.NodeId)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var callbackToSpines = new List<string>();
            foreach (var spineId in group.SpineIds)
            {
                if (pack.Nodes.TryGetValue(spineId, out var spineNode))
                {
                    callbackToSpines.AddRange(
                        spineNode.OutgoingNodeIds.Where(nextId =>
                            pack.Nodes.TryGetValue(nextId, out var nextNode) && IsSpine(nextNode)));
                }
            }

            foreach (var leafAId in group.LeafAIds.DefaultIfEmpty(string.Empty))
            {
                PackNode? leafANode = null;
                var leafAExists = !string.IsNullOrWhiteSpace(leafAId) && pack.Nodes.TryGetValue(leafAId, out leafANode);
                var explicitFlowTargets = leafAExists && leafANode is not null
                    ? leafANode.OutgoingNodeIds
                    : new List<string>();
                var leafBIds = group.LeafBIds.ToList();

                result.Add(new LeafSegmentEntry(
                    processId,
                    string.IsNullOrWhiteSpace(leafAId) ? null : leafAId,
                    string.IsNullOrWhiteSpace(leafAId) ? null : BuildNodeLabel(pack, leafAId),
                    leafBIds,
                    leafBIds.Select(leafBId => new NodeLink(leafBId, BuildNodeLabel(pack, leafBId))).ToList(),
                    entryFromSpines,
                    entryFromSpines.Select(spineId => new NodeLink(spineId, BuildNodeLabel(pack, spineId))).ToList(),
                    explicitFlowTargets,
                    explicitFlowTargets.Select(targetId => new NodeLink(targetId, BuildNodeLabel(pack, targetId))).ToList(),
                    callbackToSpines.Distinct(StringComparer.Ordinal).ToList(),
                    callbackToSpines.Distinct(StringComparer.Ordinal).Select(spineId => new NodeLink(spineId, BuildNodeLabel(pack, spineId))).ToList(),
                    "relay-segment",
                    BuildSegmentStatus(leafAId, leafBIds, reached)));
            }
        }

        return result;
    }

    private static string BuildSegmentStatus(string? leafAId, List<string> leafBIds, HashSet<string> reached)
    {
        var leafAReached = !string.IsNullOrWhiteSpace(leafAId) && reached.Contains(leafAId);
        var anyLeafBReached = leafBIds.Any(reached.Contains);

        if (leafAReached && anyLeafBReached)
        {
            return "segment-completed-callback-observed";
        }

        if (leafAReached)
        {
            return "segment-entered";
        }

        return "segment-not-entered";
    }

    private static List<MissionGroupEntry> BuildMissionGroups(
        PackDocument pack,
        Dictionary<string, ProcessGroup> processIndex,
        HashSet<string> reached)
    {
        var missionNodes = pack.Nodes.Values
            .Where(node => !string.IsNullOrWhiteSpace(node.MissionId))
            .GroupBy(node => node.MissionId!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        var result = new List<MissionGroupEntry>();

        foreach (var group in missionNodes)
        {
            var nodes = group.ToList();
            var processIds = InferMissionProcessIds(pack, processIndex, nodes);

            result.Add(new MissionGroupEntry(
                group.Key,
                nodes.Where(IsMissionA).Select(node => node.NodeId).ToList(),
                nodes.Where(IsMissionA).Select(node => new NodeLink(node.NodeId, BuildNodeLabel(node))).ToList(),
                nodes.Where(IsMissionS).Select(node => node.NodeId).ToList(),
                nodes.Where(IsMissionS).Select(node => new NodeLink(node.NodeId, BuildNodeLabel(node))).ToList(),
                nodes.Where(IsMissionF).Select(node => node.NodeId).ToList(),
                nodes.Where(IsMissionF).Select(node => new NodeLink(node.NodeId, BuildNodeLabel(node))).ToList(),
                nodes.Where(IsMissionR).Select(node => node.NodeId).ToList(),
                nodes.Where(IsMissionR).Select(node => new NodeLink(node.NodeId, BuildNodeLabel(node))).ToList(),
                processIds,
                BuildMissionComposition(nodes),
                BuildMissionRuntimeStatus(nodes, reached)));
        }

        return result;
    }

    private static List<string> InferMissionProcessIds(
        PackDocument pack,
        Dictionary<string, ProcessGroup> processIndex,
        List<PackNode> missionNodes)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var missionNodeIds = missionNodes.Select(node => node.NodeId).ToHashSet(StringComparer.Ordinal);

        foreach (var kvp in processIndex)
        {
            var processId = kvp.Key;
            var group = kvp.Value;

            foreach (var leafAId in group.LeafAIds)
            {
                if (!pack.Nodes.TryGetValue(leafAId, out var leafA))
                {
                    continue;
                }

                var visited = new HashSet<string>(StringComparer.Ordinal);
                var queue = new Queue<string>(leafA.OutgoingNodeIds);

                while (queue.Count > 0)
                {
                    var currentId = queue.Dequeue();
                    if (!visited.Add(currentId))
                    {
                        continue;
                    }

                    if (missionNodeIds.Contains(currentId))
                    {
                        result.Add(processId);
                    }

                    if (!pack.Nodes.TryGetValue(currentId, out var currentNode))
                    {
                        continue;
                    }

                    if (IsLeafB(currentNode) || IsSpine(currentNode))
                    {
                        continue;
                    }

                    foreach (var nextId in currentNode.OutgoingNodeIds)
                    {
                        queue.Enqueue(nextId);
                    }
                }
            }
        }

        return result.ToList();
    }

    private static string BuildMissionComposition(List<PackNode> nodes)
    {
        var parts = new List<string>();
        if (nodes.Any(IsMissionA)) parts.Add("A");
        if (nodes.Any(IsMissionS)) parts.Add("S");
        if (nodes.Any(IsMissionF)) parts.Add("F");
        if (nodes.Any(IsMissionR)) parts.Add("R");
        return parts.Count == 0 ? "unknown" : string.Join("+", parts);
    }

    private static string BuildMissionRuntimeStatus(List<PackNode> nodes, HashSet<string> reached)
    {
        if (nodes.Any(node => reached.Contains(node.NodeId)))
        {
            return "mission-path-observed";
        }

        return "mission-path-not-observed";
    }

    private static List<ComparerEntry> BuildComparers(PackDocument pack, HashSet<string> reached)
    {
        return pack.Nodes.Values
            .Where(IsComparer)
            .OrderBy(node => node.NodeId, StringComparer.Ordinal)
            .Select(node => new ComparerEntry(
                node.NodeId,
                BuildNodeLabel(node),
                node.ComparerName,
                node.ComparerParameters,
                node.PassOutputs,
                node.PassOutputs.Select(targetId => new NodeLink(targetId, BuildNodeLabel(pack, targetId))).ToList(),
                node.FailOutputs,
                node.FailOutputs.Select(targetId => new NodeLink(targetId, BuildNodeLabel(pack, targetId))).ToList(),
                "binary-branch",
                BuildComparerSpec(node),
                reached.Contains(node.NodeId) ? "branch-observed" : "branch-not-observed",
                node.FieldSummary))
            .ToList();
    }

    private static string BuildComparerSpec(PackNode node)
    {
        if (string.IsNullOrWhiteSpace(node.ComparerName))
        {
            return "unknown comparer";
        }

        if (node.ComparerParameters.Count == 0)
        {
            return node.ComparerName;
        }

        return $"{node.ComparerName} {string.Join(' ', node.ComparerParameters)}";
    }

    private static List<TriggerListenerEntry> BuildTriggerListeners(PackDocument pack, HashSet<string> reached)
    {
        return pack.Nodes.Values
            .Where(IsTrigger)
            .OrderBy(node => node.NodeId, StringComparer.Ordinal)
            .Select(node => new TriggerListenerEntry(
                node.NodeId,
                BuildNodeLabel(node),
                node.TriggerEvent,
                InferTriggerProtocol(node.TriggerEvent),
                "external-event-listener",
                node.OutgoingNodeIds,
                node.OutgoingNodeIds.Select(targetId => new NodeLink(targetId, BuildNodeLabel(pack, targetId))).ToList(),
                reached.Contains(node.NodeId) ? "listener-armed" : "listener-not-armed",
                $"Waiting for event '{node.TriggerEvent ?? "Unknown"}' before releasing signal.",
                node.FieldSummary))
            .ToList();
    }

    private static List<NodeProfileEntry> BuildNodeProfiles(PackDocument pack)
    {
        return pack.Nodes.Values
            .OrderBy(node => node.NodeId, StringComparer.Ordinal)
            .Select(node => new NodeProfileEntry(
                node.NodeId,
                BuildNodeLabel(node),
                node.TypeName,
                node.FieldSummary,
                NamedNodeRef.TryBuild(node)))
            .ToList();
    }

    private static string BuildNodeLabel(PackDocument pack, string nodeId)
    {
        return pack.Nodes.TryGetValue(nodeId, out var node) ? BuildNodeLabel(node) : nodeId;
    }

    private static string BuildNodeLabel(PackNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.DisplayName) && !string.Equals(node.DisplayName, node.NodeId, StringComparison.Ordinal))
        {
            return $"{node.DisplayName} [{ShortType(node.TypeName)}]";
        }

        if (IsSpine(node))
        {
            return $"Spine({node.ProcessId ?? node.NodeId})";
        }

        if (IsLeafA(node))
        {
            return $"LeafA({node.ProcessId ?? node.NodeId})";
        }

        if (IsLeafB(node))
        {
            return $"LeafB({node.ProcessId ?? node.NodeId})";
        }

        if (IsTrigger(node))
        {
            return $"Trigger({node.TriggerEvent ?? "Unknown"})";
        }

        if (IsComparer(node))
        {
            return $"Comparer({BuildComparerSpec(node)})";
        }

        if (IsCommand(node))
        {
            return $"Command({node.CommandName ?? node.NodeId})";
        }

        if (IsMissionA(node))
        {
            return $"MissionA({node.MissionId ?? node.NodeId})";
        }

        if (IsMissionS(node))
        {
            return $"MissionS({node.MissionId ?? node.NodeId})";
        }

        if (IsMissionF(node))
        {
            return $"MissionF({node.MissionId ?? node.NodeId})";
        }

        if (IsMissionR(node))
        {
            return $"MissionR({node.MissionId ?? node.NodeId})";
        }

        if (node.TypeName.Contains("RootNode", StringComparison.OrdinalIgnoreCase))
        {
            return "Root";
        }

        return $"{ShortType(node.TypeName)}({node.NodeId})";
    }

    private static string ShortType(string typeName)
    {
        return typeName
            .Replace("NodeData", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("_Data", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string InferTriggerProtocol(string? eventName)
    {
        return eventName switch
        {
            "GameStarted" => "none",
            "GameTickUpdated" => "numeric",
            "UnitSpawned" => "entity",
            "UnitKilled" => "entity",
            "UnitDamaged" => "entity",
            "MoneyChanged" => "numeric",
            "ResourceChanged" => "numeric",
            "BuildingConstructed" => "entity",
            "MissionCompleted" => "string",
            "ResearchCompleted" => "string",
            "GroundClicked" => "vector",
            "UnitSelected" => "entity",
            "SocialOption1" => "none",
            "SocialOption2" => "none",
            "SocialOption3" => "none",
            "SocialOption4" => "none",
            "BaseUnderAttack" => "boolean",
            _ => "unknown"
        };
    }

    private static List<RuntimeEdge> GetRuntimeEdges(
        PackDocument pack,
        PackNode node,
        Dictionary<string, ProcessGroup> processIndex)
    {
        var result = new List<RuntimeEdge>();

        if (IsSpine(node) &&
            !string.IsNullOrWhiteSpace(node.ProcessId) &&
            processIndex.TryGetValue(node.ProcessId, out var spineGroup))
        {
            result.AddRange(spineGroup.LeafAIds.Select(nextId => new RuntimeEdge(node.NodeId, nextId, "relay-callback")));
            return result
                .Where(edge => !string.IsNullOrWhiteSpace(edge.ToNodeId))
                .DistinctBy(edge => $"{edge.FromNodeId}:{edge.ToNodeId}:{edge.EdgeType}")
                .ToList();
        }

        if (IsLeafA(node) &&
            !string.IsNullOrWhiteSpace(node.ProcessId) &&
            processIndex.TryGetValue(node.ProcessId, out var leafGroup))
        {
            result.AddRange(node.OutgoingNodeIds.Select(nextId => new RuntimeEdge(node.NodeId, nextId, "flow")));
            result.AddRange(leafGroup.LeafBIds.Select(nextId => new RuntimeEdge(node.NodeId, nextId, "flow")));
            return result
                .Where(edge => !string.IsNullOrWhiteSpace(edge.ToNodeId))
                .DistinctBy(edge => $"{edge.FromNodeId}:{edge.ToNodeId}:{edge.EdgeType}")
                .ToList();
        }

        if (IsLeafB(node) &&
            !string.IsNullOrWhiteSpace(node.ProcessId) &&
            processIndex.TryGetValue(node.ProcessId, out var callbackGroup))
        {
            result.AddRange(node.OutgoingNodeIds.Select(nextId => new RuntimeEdge(node.NodeId, nextId, "flow")));
            foreach (var spineId in callbackGroup.SpineIds)
            {
                if (pack.Nodes.TryGetValue(spineId, out var spineNode))
                {
                    result.AddRange(spineNode.OutgoingNodeIds.Select(nextId => new RuntimeEdge(node.NodeId, nextId, "relay-callback")));
                }
            }

            return result
                .Where(edge => !string.IsNullOrWhiteSpace(edge.ToNodeId))
                .DistinctBy(edge => $"{edge.FromNodeId}:{edge.ToNodeId}:{edge.EdgeType}")
                .ToList();
        }

        if (IsComparer(node))
        {
            var branchTargets = node.PassOutputs
                .Concat(node.FailOutputs)
                .ToHashSet(StringComparer.Ordinal);

            result.AddRange(
                node.OutgoingNodeIds
                    .Where(nextId => !branchTargets.Contains(nextId))
                    .Select(nextId => new RuntimeEdge(node.NodeId, nextId, "flow")));
            result.AddRange(node.PassOutputs.Select(nextId => new RuntimeEdge(node.NodeId, nextId, "comparer-pass")));
            result.AddRange(node.FailOutputs.Select(nextId => new RuntimeEdge(node.NodeId, nextId, "comparer-fail")));
            return result
                .Where(edge => !string.IsNullOrWhiteSpace(edge.ToNodeId))
                .DistinctBy(edge => $"{edge.FromNodeId}:{edge.ToNodeId}:{edge.EdgeType}")
                .ToList();
        }

        result.AddRange(node.OutgoingNodeIds.Select(nextId => new RuntimeEdge(node.NodeId, nextId, "flow")));

        return result
            .Where(edge => !string.IsNullOrWhiteSpace(edge.ToNodeId))
            .DistinctBy(edge => $"{edge.FromNodeId}:{edge.ToNodeId}:{edge.EdgeType}")
            .ToList();
    }

    private static List<string> GetRuntimeOutgoing(
        PackDocument pack,
        PackNode node,
        Dictionary<string, ProcessGroup> processIndex)
    {
        return GetRuntimeEdges(pack, node, processIndex)
            .Select(edge => edge.ToNodeId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsTrigger(PackNode node) =>
        node.TypeName.Contains("TriggerNode", StringComparison.OrdinalIgnoreCase);

    private static bool IsSpine(PackNode node) =>
        node.TypeName.Contains("SpineNode", StringComparison.OrdinalIgnoreCase);

    private static bool IsLeafA(PackNode node) =>
        node.TypeName.Contains("LeafNode_A", StringComparison.OrdinalIgnoreCase);

    private static bool IsLeafB(PackNode node) =>
        node.TypeName.Contains("LeafNode_B", StringComparison.OrdinalIgnoreCase);

    private static bool IsDestroy(PackNode node) =>
        node.TypeName.Contains("DestroyNode", StringComparison.OrdinalIgnoreCase);

    private static bool IsCommand(PackNode node) =>
        node.TypeName.Contains("CommandNode", StringComparison.OrdinalIgnoreCase);

    private static bool IsComparer(PackNode node) =>
        node.TypeName.Contains("ComparerNode", StringComparison.OrdinalIgnoreCase);

    private static bool IsMissionA(PackNode node) =>
        node.TypeName.Contains("MissionNode_A", StringComparison.OrdinalIgnoreCase);

    private static bool IsMissionS(PackNode node) =>
        node.TypeName.Contains("MissionNode_S", StringComparison.OrdinalIgnoreCase);

    private static bool IsMissionF(PackNode node) =>
        node.TypeName.Contains("MissionNode_F", StringComparison.OrdinalIgnoreCase);

    private static bool IsMissionR(PackNode node) =>
        node.TypeName.Contains("MissionNode_R", StringComparison.OrdinalIgnoreCase);

    private static bool IsExplicitWait(PackNode node) =>
        node.TypeName.Contains("WaitNode", StringComparison.OrdinalIgnoreCase) ||
        node.TypeName.Contains("SocialWait", StringComparison.OrdinalIgnoreCase);

    private static RunPathReport CreatePath(
        List<string> nodeIds,
        List<PathTransition> transitions,
        string status,
        string terminalReason,
        string explanation)
    {
        var nodeRefs = nodeIds.Select(nodeId => new NodeLink(nodeId, BuildNodeLabel(_currentPack!, nodeId))).ToList();
        var semanticNodes = nodeRefs.Select(nodeRef => nodeRef.Label).ToList();
        var semanticPath = BuildSemanticPath(nodeRefs, transitions);
        var debug = new RunPathDebug(nodeIds, nodeRefs, transitions);
        return new RunPathReport(
            semanticNodes,
            semanticPath,
            status,
            terminalReason,
            explanation,
            debug);
    }

    private static string BuildSemanticPath(List<NodeLink> nodeRefs, List<PathTransition> transitions)
    {
        if (nodeRefs.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string> { nodeRefs[0].Label };
        for (var index = 0; index < transitions.Count && index + 1 < nodeRefs.Count; index++)
        {
            parts.Add(FormatEdge(transitions[index].EdgeType));
            parts.Add(nodeRefs[index + 1].Label);
        }

        return string.Join(" ", parts);
    }

    private static string FormatEdge(string edgeType)
    {
        return edgeType switch
        {
            "relay-callback" => "~>",
            "comparer-pass" => "-pass->",
            "comparer-fail" => "-fail->",
            _ => "->"
        };
    }

    private static HashSet<string> ComputeStructuralReachability(
        PackDocument pack,
        Dictionary<string, ProcessGroup> processIndex)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(pack.RootNodeId);

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (!reached.Add(nodeId))
            {
                continue;
            }

            if (!pack.Nodes.TryGetValue(nodeId, out var node))
            {
                continue;
            }

            foreach (var nextNodeId in GetRuntimeOutgoing(pack, node, processIndex))
            {
                if (!reached.Contains(nextNodeId))
                {
                    queue.Enqueue(nextNodeId);
                }
            }
        }

        return reached;
    }
}

internal sealed class RunFullReport
{
    public required string Command { get; init; }

    public required string PackId { get; init; }

    public required string SourceFile { get; init; }

    public required string RootNodeId { get; init; }

    public required OverviewBlock Overview { get; init; }

    public required RunSummary Summary { get; init; }

    public required ExecutionModel ExecutionModel { get; init; }

    public required List<NodeProfileEntry> NodeProfiles { get; init; }

    public required List<ReachabilityEntry> Reachability { get; init; }

    public required List<ProcessViewEntry> ProcessViews { get; init; }

    public required List<MissionViewEntry> MissionViews { get; init; }

    public required List<SpineBackboneEntry> SpineBackbone { get; init; }

    public required List<LeafSegmentEntry> LeafSegments { get; init; }

    public required List<MissionGroupEntry> MissionGroups { get; init; }

    public required List<ComparerEntry> Comparers { get; init; }

    public required List<TriggerListenerEntry> TriggerListeners { get; init; }

    public required List<BlockerReport> BlockingPoints { get; init; }

    public required List<RunPathReport> Paths { get; init; }

    public required List<FindingReport> Findings { get; init; }
}

internal sealed record RunSummary(
    int TotalNodes,
    int TotalEdges,
    int RuntimeReachableNodes,
    int UnreachableNodes,
    int StructurallyReachableNodes,
    int BlockingPointCount,
    int TerminatedPathCount,
    int PathCount);

internal sealed record OverviewBlock(
    string PackSummary,
    string ReachabilitySummary,
    string BlockingSummary,
    string ReadingHint);

internal sealed record ExecutionModel(
    string EntryRule,
    string BlockingRule,
    string CommandRule,
    string TerminationRule,
    string SpineSemantics,
    string PathReadingRule);

internal sealed record ReachabilityEntry(
    string NodeId,
    bool RuntimeReachable,
    bool StructurallyReachable,
    List<string>? FirstReachPath);

internal sealed record ProcessViewEntry(
    string AccessKey,
    string ProcessId,
    string Summary,
    string BackbonePath,
    List<string> SegmentPaths,
    List<string> RelatedSemanticPaths,
    List<string> BusinessNodes,
    List<string> Blockers,
    string RuntimeStatus,
    ProcessViewDebug Debug);

internal sealed record MissionViewEntry(
    string AccessKey,
    string MissionId,
    string Summary,
    string Composition,
    List<string> ProcessIds,
    List<string> RelatedSemanticPaths,
    List<string> MissionNodes,
    List<string> BusinessNodes,
    List<string> Blockers,
    string RuntimeStatus,
    MissionViewDebug Debug);

internal sealed record ProcessViewDebug(
    List<NodeLink> SpineRefs,
    List<NodeLink> LeafARefs,
    List<NodeLink> LeafBRefs,
    List<NodeLink> BusinessNodeRefs,
    List<ProcessBlockerEntry> Blockers);

internal sealed record MissionViewDebug(
    List<NodeLink> MissionNodeRefs,
    List<NodeLink> BusinessNodeRefs,
    List<ProcessBlockerEntry> Blockers);

internal sealed record ProcessBlockerEntry(
    string NodeId,
    string Label,
    string BlockerType,
    string Explanation);

internal sealed record NodeProfileEntry(
    string NodeId,
    string Label,
    string TypeName,
    Dictionary<string, string> Fields,
    string? AccessKey = null);

internal sealed record NodeLink(
    string NodeId,
    string Label,
    string? AccessKey = null);

internal sealed record SpineBackboneEntry(
    string SpineNodeId,
    string SpineLabel,
    string? ProcessId,
    List<string> ActivatedLeafA,
    List<NodeLink> ActivatedLeafARefs,
    List<string> CallbackLeafB,
    List<NodeLink> CallbackLeafBRefs,
    List<string> NextSpines,
    List<NodeLink> NextSpineRefs,
    string RelayType);

internal sealed record LeafSegmentEntry(
    string ProcessId,
    string? LeafA,
    string? LeafALabel,
    List<string> LeafB,
    List<NodeLink> LeafBRefs,
    List<string> EntryFromSpines,
    List<NodeLink> EntryFromSpineRefs,
    List<string> ExplicitFlowTargets,
    List<NodeLink> ExplicitFlowTargetRefs,
    List<string> CallbackToSpines,
    List<NodeLink> CallbackToSpineRefs,
    string SegmentType,
    string Status);

internal sealed record MissionGroupEntry(
    string MissionId,
    List<string> MissionA,
    List<NodeLink> MissionARefs,
    List<string> MissionS,
    List<NodeLink> MissionSRefs,
    List<string> MissionF,
    List<NodeLink> MissionFRefs,
    List<string> MissionR,
    List<NodeLink> MissionRRefs,
    List<string> ProcessIds,
    string Composition,
    string RuntimeStatus);

internal sealed record ComparerEntry(
    string NodeId,
    string Label,
    string? ComparerName,
    List<string> Parameters,
    List<string> PassOutputs,
    List<NodeLink> PassOutputRefs,
    List<string> FailOutputs,
    List<NodeLink> FailOutputRefs,
    string FlowMode,
    string ComparisonSpec,
    string RuntimeBranchStatus,
    Dictionary<string, string> Fields);

internal sealed record TriggerListenerEntry(
    string NodeId,
    string Label,
    string? Event,
    string EventProtocol,
    string ListenerType,
    List<string> UnlocksTo,
    List<NodeLink> UnlockRefs,
    string TriggerState,
    string WaitingReason,
    Dictionary<string, string> Fields);

internal sealed record BlockerReport(
    string NodeId,
    string BlockerType,
    string Explanation,
    List<string> UnlocksTo,
    List<string> Path);

internal sealed record RunPathReport(
    List<string> SemanticNodes,
    string SemanticPath,
    string Status,
    string TerminalReason,
    string Explanation,
    RunPathDebug Debug);

internal sealed record RunPathDebug(
    List<string> NodeIds,
    List<NodeLink> NodeRefs,
    List<PathTransition> Transitions);

internal sealed record PathTransition(
    string FromNodeId,
    string ToNodeId,
    string EdgeType);

internal sealed record PendingPath(
    List<string> NodeIds,
    List<PathTransition> Transitions);

internal sealed record RuntimeEdge(
    string FromNodeId,
    string ToNodeId,
    string EdgeType);

internal sealed record FindingReport(
    string FindingType,
    string NodeId,
    string Message,
    string Recommendation);

internal sealed class ProcessGroup
{
    public List<string> SpineIds { get; } = [];

    public List<string> LeafAIds { get; } = [];

    public List<string> LeafBIds { get; } = [];
}
