using System;
using System.Collections.Generic;
using UnityEngine;

namespace NekoGraph
{

// =========================================================
// 基础流程节点策略 - Root/Spine/Leaf 节点处理器喵~
// =========================================================

/// <summary>
/// Root 节点策略 - 流程的起始锚点喵~
/// </summary>
public class RootNodeStrategy : NodeStrategy
{
    public override void OnSignalEnter(BaseNodeData data, SignalContext context, BasePackData pack, GraphRunner runner, string packIDKey)
    {
        if (data is not RootNodeData rootNode) return;

        if (runner.EnableDebugLog)
        {
            Debug.Log($"[RootNode] 流程启动：{rootNode.NodeID}");
        }

        // 运行时只认 Root 的语义输出字段 `_`
        EnqueueSignals(pack, rootNode._, context);
    }

    public override void OnEvent(BaseNodeData data, string eventName, object eventData, BasePackData pack, GraphRunner runner, string packIDKey)
    {
        // Root 节点不响应外部事件
    }
}

/// <summary>
/// Spine 节点策略 - 流程的逻辑骨架/无线输电继电器喵~
/// </summary>
 [Serializable]
internal sealed class SpineCallbackPayload
{
    public string SourceLeafNodeID;
}

public class SpineNodeStrategy : NodeStrategy, IBlockingNodeStrategy
{
    private readonly Dictionary<string, Action<object>> _callbackListeners = new Dictionary<string, Action<object>>();

    public override void OnSignalEnter(BaseNodeData data, SignalContext context, BasePackData pack, GraphRunner runner, string packIDKey)
    {
        if (data is not SpineNodeData spineNode) return;

        if (runner.EnableDebugLog)
        {
            Debug.Log($"[SpineNode] 信号中继：{spineNode.NodeID} (ProcessID: {spineNode.ProcessID})");
        }

        // 1. 激活关联的 Leaf A 节点
        ActivateLeafNodes(spineNode, context, pack, runner);

        // 2. Spine 本身成为阻塞点，等待对应 Leaf B 的回调事件再放行下一个 Spine
        RegisterLeafBCallback(spineNode, context, pack, runner);
    }

    public override void OnEvent(BaseNodeData data, string eventName, object eventData, BasePackData pack, GraphRunner runner, string packIDKey)
    {
        if (data is not SpineNodeData spineNode) return;

        // Spine 节点可以通过 Leaf B 节点的回调来响应事件
        if (runner.EnableDebugLog)
        {
            Debug.Log($"[SpineNode] 收到事件：{eventName} -> {spineNode.NodeID}");
        }
    }

    private void ActivateLeafNodes(SpineNodeData node, SignalContext context, BasePackData pack, GraphRunner runner)
    {
        // 查找所有与当前 Spine 节点共享 ProcessID 的 Leaf A 节点
        foreach (var leafKvp in pack.Nodes)
        {
            if (leafKvp.Value is LeafNode_A_Data leaf && leaf.ProcessID == node.ProcessID)
            {
                if (runner != null && runner.EnableDebugLog)
                {
                    Debug.Log($"[SpineNode] 激活 Leaf A: {leaf.NodeID} (ProcessID: {leaf.ProcessID})");
                }

                // 向 Leaf A 节点发送信号
                EnqueueSignal(pack, leaf.NodeID, context);
            }
        }
    }

    private void RegisterLeafBCallback(SpineNodeData node, SignalContext context, BasePackData pack, GraphRunner runner)
    {
        if (_callbackListeners.ContainsKey(node.NodeID))
            return;

        string eventName = BuildSpineCallbackEventName(pack, node.ProcessID);
        Action<object> callback = null;
        callback = payload =>
        {
            PostSystem.Instance.Off(eventName, callback);
            _callbackListeners.Remove(node.NodeID);

            if (runner != null && runner.EnableDebugLog)
            {
                Debug.Log($"[SpineNode] 收到 Leaf B 回调，放行下一个 Spine：{node.NodeID} (ProcessID: {node.ProcessID})");
            }

            string sourceLeafNodeId = (payload as SpineCallbackPayload)?.SourceLeafNodeID ?? context.CurrentNodeId;
            foreach (var nextSpineNodeId in node.NextSpineNodeIDs)
            {
                EnqueueSignal(pack, sourceLeafNodeId, nextSpineNodeId, context);
            }
        };

        PostSystem.Instance.On(eventName, callback);
        _callbackListeners[node.NodeID] = callback;

        if (runner != null && runner.EnableDebugLog)
        {
            Debug.Log($"[SpineNode] 已注册 Leaf B 回调监听：{eventName}");
        }
    }

    private static string BuildSpineCallbackEventName(BasePackData pack, string processId)
    {
        return $"spine.{pack.PackID}.{processId}";
    }

    // =========================================================
    // IBlockingNodeStrategy 实现 - 阻隔状态捕获与恢复喵~
    // =========================================================

    /// <summary>
    /// 捕获 Spine 节点的阻隔状态喵~
    /// </summary>
    public object CaptureBlockingState(BaseNodeData node)
    {
        if (node is not SpineNodeData spineNode) return null;

        // Spine 的阻隔状态：等待哪些 Leaf 完成
        // 通过查找共享 ProcessID 的 Leaf A 节点来确定
        return new SpineBlockingState
        {
            ProcessID = spineNode.ProcessID
        };
    }

    /// <summary>
    /// 恢复 Spine 节点的阻隔状态喵~
    /// </summary>
    public void RestoreBlockingState(BaseNodeData node, object state)
    {
        if (node is not SpineNodeData spineNode) return;
        if (state is not SpineBlockingState spineState) return;

        // Spine 节点读档后不需要特殊恢复，ProcessID 是持久化在节点数据中的
        // 这里保留方法用于未来扩展
    }

    /// <summary>
    /// Spine 阻隔状态数据结构喵~
    /// </summary>
    [Serializable]
    private class SpineBlockingState
    {
        public string ProcessID;
    }
}

/// <summary>
/// 销毁节点策略 - 销毁 Pack 实例并重置状态喵~
/// </summary>
public class DestroyNodeStrategy : NodeStrategy
{
    public override void OnSignalEnter(BaseNodeData data, SignalContext context, BasePackData pack, GraphRunner runner, string packIDKey)
    {
        if (runner.EnableDebugLog)
        {
            Debug.Log($"[DestroyNode] 销毁 Pack：{pack.PackID} (PackIDKey: {packIDKey})");
        }

        // 销毁当前 Pack 实例，强制重置状态喵~
        runner.UnloadPack(packIDKey);
    }

    public override void OnEvent(BaseNodeData data, string eventName, object eventData, BasePackData pack, GraphRunner runner, string packIDKey) { }
}

/// <summary>
/// Leaf A 节点策略 - 处理具体的执行演出喵~
/// </summary>
public class LeafNodeAStrategy : NodeStrategy
{
    public override void OnSignalEnter(BaseNodeData data, SignalContext context, BasePackData pack, GraphRunner runner, string packIDKey)
    {
        if (data is not LeafNode_A_Data leafNode) return;

        if (runner.EnableDebugLog)
        {
            Debug.Log($"[LeafNode A] 执行演出：{leafNode.NodeID} (ProcessID: {leafNode.ProcessID})");
        }

        // Leaf A 只负责把信号送入本阶段业务链，Leaf B 必须由业务链显式到达
        EnqueueSignals(pack, leafNode.OutputNodeIds, context);
    }

    public override void OnEvent(BaseNodeData data, string eventName, object eventData, BasePackData pack, GraphRunner runner, string packIDKey)
    {
        // Leaf A 节点通常不直接响应外部事件
    }

}

/// <summary>
/// Leaf B 节点策略 - 处理执行完毕的回调喵~
/// </summary>
public class LeafNodeBStrategy : NodeStrategy
{
    public override void OnSignalEnter(BaseNodeData data, SignalContext context, BasePackData pack, GraphRunner runner, string packIDKey)
    {
        if (data is not LeafNode_B_Data leafNode) return;

        if (runner.EnableDebugLog)
        {
            Debug.Log($"[LeafNode B] 执行回调：{leafNode.NodeID} (ProcessID: {leafNode.ProcessID})");
        }

        string callbackEventName = BuildSpineCallbackEventName(pack, leafNode.ProcessID);
        PostSystem.Instance.Send(callbackEventName, new SpineCallbackPayload
        {
            SourceLeafNodeID = leafNode.NodeID
        });

        if (runner.EnableDebugLog)
        {
            Debug.Log($"[LeafNode B] 已发送 Spine 回调事件：{callbackEventName}");
        }

    }

    public override void OnEvent(BaseNodeData data, string eventName, object eventData, BasePackData pack, GraphRunner runner, string packIDKey)
    {
        // Leaf B 节点通常不直接响应外部事件
    }

    private static string BuildSpineCallbackEventName(BasePackData pack, string processId)
    {
        return $"spine.{pack.PackID}.{processId}";
    }

}

}
