using System;
using System.Collections.Generic;
using UnityEngine;

namespace NekoGraph
{

/// <summary>
/// ComparerNode 策略 - 逻辑判断与信号调度中心喵~
/// </summary>
public class ComparerNodeStrategy : NodeStrategy
{
    public static ComparerNodeStrategy Instance { get; private set; }

    static ComparerNodeStrategy()
    {
        Instance = new ComparerNodeStrategy();
    }

    private ComparerNodeStrategy() { }

    public override void OnSignalEnter(BaseNodeData data, SignalContext context, BasePackData pack, GraphRunner runner, string packIDKey)
    {
        if (data is not ComparerNodeData comparerNode) return;

        // 1. 执行比较逻辑喵~
        object payload = context.Args;
        var result = ComparerRegistry.Execute(comparerNode.ComparerName, payload, comparerNode.Parameters.ToArray());

        if (runner.EnableDebugLog)
        {
            Debug.Log($"[ComparerNode] 判定结果：{result} (NodeID: {comparerNode.NodeID}) 喵~");
        }

        // 2. 信号分流喵~
        if (result == ComparerResult.Pass)
        {
            // 通过：走绿灯喵！
            Propagate(comparerNode.PassOutputs, context, pack);
        }
        else
        {
            // 契约校验失败，红色警告！
            if (result == ComparerResult.TypeMismatch)
            {
                Debug.LogError($"<color=red>[ComparerNode] 类型失配！</color> 比较器 [{comparerNode.ComparerName}] 无法处理当前 Payload 类型 [{payload?.GetType().Name ?? "null"}] 喵！\n" +
                               $"请检查 Trigger 绑定的事件协议是否与 Comparer 匹配喵~");
            }

            // 不通过：走红灯喵！
            Propagate(comparerNode.FailOutputs, context, pack);
            BacktraceAndReactivateTrigger(context, pack, runner, packIDKey);
        }
    }

    /// <summary>
    /// 信号传播喵~
    /// </summary>
    private void Propagate(List<string> targets, SignalContext context, BasePackData pack)
    {
        if (targets == null) return;
        EnqueueSignals(pack, targets, context);
    }

    /// <summary>
    /// 顺着信号路径往回找，重新激活上一个 Trigger 节点喵！
    /// </summary>
    private void BacktraceAndReactivateTrigger(SignalContext context, BasePackData pack, GraphRunner runner, string packIDKey)
    {
        // 信号路径是从近到远排列的，我们倒着找喵~
        // 注意：TraveledPath 存储的是 ConnectionData 列表
        var path = context.TraveledPath;
        if (path == null || path.Count == 0) return;

        // 倒序遍历，找到最近的一个 Trigger 节点喵~
        for (int i = path.Count - 1; i >= 0; i--)
        {
            var conn = path[i];

            // 优先使用 SourceNodeID，如果没有则使用当前节点 ID 回溯
            string nodeId = !string.IsNullOrEmpty(conn.SourceNodeID) ?
                conn.SourceNodeID :
                FindSourceNodeId(conn, pack);

            if (!string.IsNullOrEmpty(nodeId) &&
                pack.Nodes.TryGetValue(nodeId, out var node) &&
                node is TriggerNodeData triggerNode)
            {
                if (runner != null && runner.EnableDebugLog)
                {
                    Debug.Log($"[ComparerNode] 检测到失败，回溯并重连 Trigger: {triggerNode.Event} (NodeID: {nodeId}) 喵！");
                }

                // 重新通过 Strategy 激活该 Trigger 的监听喵~
                TriggerNodeStrategy.Instance.OnSignalEnter(triggerNode, context, pack, runner, packIDKey);
                break; // 找到最近的一个就够了喵~
            }
        }
    }

    /// <summary>
    /// 从连接数据中查找源节点 ID 喵~
    /// </summary>
    private string FindSourceNodeId(ConnectionData conn, BasePackData pack)
    {
        // 遍历所有节点，按各自的语义输出字段查找来源
        foreach (var kvp in pack.Nodes)
        {
            if (kvp.Value is BaseNodeData node)
            {
                foreach (var targetNodeId in GetSemanticOutputTargets(node, conn.FromPortIndex))
                {
                    if (targetNodeId == conn.TargetNodeID)
                        return kvp.Key;
                }
            }
        }
        return null;
    }

    private static IEnumerable<string> GetSemanticOutputTargets(BaseNodeData node, int fromPortIndex)
    {
        switch (node)
        {
            case RootNodeData rootNode:
                return rootNode._ != null ? rootNode._ : Array.Empty<string>();
            case SpineNodeData spineNode:
                return spineNode.NextSpineNodeIDs != null ? spineNode.NextSpineNodeIDs : Array.Empty<string>();
            case LeafNode_A_Data leafNodeA:
                return leafNodeA.OutputNodeIds != null ? leafNodeA.OutputNodeIds : Array.Empty<string>();
            case CommandNodeData commandNode:
                return commandNode.OutputNodeIDs != null ? commandNode.OutputNodeIDs : Array.Empty<string>();
            case TriggerNodeData triggerNode:
                return fromPortIndex == 0
                    ? (triggerNode.SignalOutputs != null ? triggerNode.SignalOutputs : Array.Empty<string>())
                    : Array.Empty<string>();
            case ComparerNodeData comparerNode:
                return fromPortIndex switch
                {
                    0 => comparerNode.PassOutputs != null ? comparerNode.PassOutputs : Array.Empty<string>(),
                    1 => comparerNode.FailOutputs != null ? comparerNode.FailOutputs : Array.Empty<string>(),
                    _ => Array.Empty<string>()
                };
            case MissionNode_A_Data missionNodeA:
                return missionNodeA.OutPutNodeIDs != null ? missionNodeA.OutPutNodeIDs : Array.Empty<string>();
            case MissionNode_S_Data missionNodeS:
                return missionNodeS.OutPutNodeIDs != null ? missionNodeS.OutPutNodeIDs : Array.Empty<string>();
            case MissionNode_F_Data missionNodeF:
                return missionNodeF.OutPutNodeIDs != null ? missionNodeF.OutPutNodeIDs : Array.Empty<string>();
            case MissionNode_R_Data missionNodeR:
                return missionNodeR.OutPutNodeIDs != null ? missionNodeR.OutPutNodeIDs : Array.Empty<string>();
            case SocialMsgContentNodeData socialMsgContentNode:
                return socialMsgContentNode.Out != null ? socialMsgContentNode.Out : Array.Empty<string>();
            case ChoiceTextNodeData choiceTextNode:
                return choiceTextNode.Out != null ? choiceTextNode.Out : Array.Empty<string>();
            case VFSNodeData vfsNode:
                return vfsNode.ChildNodeIDs != null ? vfsNode.ChildNodeIDs : Array.Empty<string>();
            default:
                return Array.Empty<string>();
        }
    }

    public override void OnEvent(BaseNodeData data, string eventName, object eventData, BasePackData pack, GraphRunner runner, string packIDKey) { }
}

}
