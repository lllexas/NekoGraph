using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 命令运行时上下文喵~
/// 让数据驱动命令在日志里也能带上 Pack / Node / Path 等定位信息。
/// </summary>
public sealed class GraphCommandConsoleContext : IConsoleController
{
    public string PackID { get; }
    public string PackInstanceID { get; }
    public string NodeID { get; }
    public string NodeName { get; }
    public string NodeType { get; }
    public string CommandName { get; }
    public int SubjectLevel { get; }
    public int SignalDepth { get; }
    public string SignalPath { get; }

    public GraphCommandConsoleContext(
        BasePackData pack,
        string packInstanceID,
        BaseNodeData node,
        string commandName,
        int subjectLevel,
        SignalContext signal)
    {
        PackID = pack?.PackID ?? "(unknown-pack)";
        PackInstanceID = string.IsNullOrWhiteSpace(packInstanceID) ? "(unknown-instance)" : packInstanceID;
        NodeID = node?.NodeID ?? signal?.CurrentNodeId ?? "(unknown-node)";
        NodeName = GetDisplayName(node);
        NodeType = node?.GetType().Name ?? "(unknown-type)";
        CommandName = string.IsNullOrWhiteSpace(commandName) ? "(unknown-command)" : commandName;
        SubjectLevel = subjectLevel;
        SignalDepth = signal?.Depth ?? 0;
        SignalPath = BuildSignalPath(pack, signal, NodeID);
    }

    public void Log(string message, Color color)
    {
        var colorHex = ColorUtility.ToHtmlStringRGB(color);
        Debug.Log($"<color=#{colorHex}>{message}</color>");
    }

    public string BuildDebugPrefix()
    {
        return $"pack={PackID} node={NodeName}<{NodeType}>#{ShortId(NodeID)} depth={SignalDepth} subject={SubjectLevel}";
    }

    public static string SummarizeValue(object value)
    {
        if (value == null) return "null";
        if (value is string str) return string.IsNullOrWhiteSpace(str) ? "\"\"" : $"\"{str}\"";
        if (value is IEnumerable enumerable and not string)
        {
            var parts = new List<string>();
            foreach (var item in enumerable)
            {
                parts.Add(item?.ToString() ?? "null");
                if (parts.Count >= 5) break;
            }

            string suffix = parts.Count >= 5 ? ", ..." : string.Empty;
            return $"[{string.Join(", ", parts)}{suffix}]";
        }

        return value.ToString();
    }

    private static string BuildSignalPath(BasePackData pack, SignalContext signal, string currentNodeId)
    {
        if (pack?.Nodes == null) return "(path-unavailable)";

        var labels = new List<string>();
        if (signal?.TraveledPath != null)
        {
            foreach (var connection in signal.TraveledPath)
            {
                if (!string.IsNullOrWhiteSpace(connection.SourceNodeID))
                {
                    AppendLabel(labels, pack, connection.SourceNodeID);
                }

                if (!string.IsNullOrWhiteSpace(connection.TargetNodeID))
                {
                    AppendLabel(labels, pack, connection.TargetNodeID);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(currentNodeId))
        {
            string currentLabel = BuildNodeLabel(pack, currentNodeId);
            if (labels.Count == 0 || labels[^1] != currentLabel)
            {
                labels.Add(currentLabel);
            }
        }

        return labels.Count > 0 ? string.Join(" -> ", labels) : "(path-empty)";
    }

    private static void AppendLabel(List<string> labels, BasePackData pack, string nodeId)
    {
        string label = BuildNodeLabel(pack, nodeId);
        if (labels.Count == 0 || labels[^1] != label)
        {
            labels.Add(label);
        }
    }

    private static string BuildNodeLabel(BasePackData pack, string nodeId)
    {
        if (pack?.Nodes != null && pack.Nodes.TryGetValue(nodeId, out var node))
        {
            string name = GetDisplayName(node);
            return $"{name}#{ShortId(nodeId)}";
        }

        return $"missing#{ShortId(nodeId)}";
    }

    private static string ShortId(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId)) return "null";
        return nodeId.Length <= 8 ? nodeId : nodeId[..8];
    }

    private static string GetDisplayName(BaseNodeData node)
    {
        if (node == null) return "(unknown)";
        if (!string.IsNullOrWhiteSpace(node.Name)) return node.Name;

        return node switch
        {
            CommandNodeData commandNode => BuildCommandNodeName(commandNode),
            TriggerNodeData triggerNode => $"trigger:{triggerNode.GetEventName()}",
            PostEventNodeData postEventNode => $"post:{postEventNode.GetEventName()}",
            _ => node.GetType().Name
        };
    }

    private static string BuildCommandNodeName(CommandNodeData node)
    {
        if (node?.Command == null || string.IsNullOrWhiteSpace(node.Command.CommandName))
            return "command";

        string firstArg = null;
        if (node.Command.Parameters != null)
        {
            foreach (var parameter in node.Command.Parameters)
            {
                if (!string.IsNullOrWhiteSpace(parameter))
                {
                    firstArg = parameter;
                    break;
                }
            }
        }

        return string.IsNullOrWhiteSpace(firstArg)
            ? node.Command.CommandName
            : $"{node.Command.CommandName}:{firstArg}";
    }
}
