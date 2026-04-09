using System;
using System.Collections.Generic;
using UnityEngine;

namespace NekoGraph
{

/// <summary>
/// PostOffice 发送节点策略 - 在流程图任意环节发送 TriggerEvent 事件喵~
/// 
/// 职责：
/// 1. 当信号进入时，调用 PostOffice.Send 发送指定事件
/// 2. 将 Payload 注入到 SignalContext 传递给下游节点
/// 3. 传播信号到输出端口连接的节点
/// </summary>
public class PostEventNodeStrategy : NodeStrategy
{
    public static PostEventNodeStrategy Instance { get; private set; }

    static PostEventNodeStrategy()
    {
        Instance = new PostEventNodeStrategy();
    }

    private PostEventNodeStrategy() { }

    public override void OnSignalEnter(BaseNodeData data, SignalContext context, BasePackData pack, GraphRunner runner, string packInstanceID)
    {
        if (data is not PostEventNodeData postEventNode) return;

        var eventName = postEventNode.GetEventName();
        var meta = postEventNode.GetMeta();

        if (runner != null && runner.EnableDebugLog)
        {
            Debug.Log($"[PostEventNode] 准备发送事件：{eventName} (NodeID: {postEventNode.NodeID}) 喵~");
        }

        // --- 协议检查喵~ ---
        if (meta == null)
        {
            Debug.LogError($"[PostEventNode] 未定义的事件：{eventName} 喵~");
            PropagateSignal(postEventNode, context, pack);
            return;
        }

        // --- 解析 Payload 喵~ ---
        object payload = null;

        // Entity 协议需要特殊处理：从上下文中获取
        if (meta.Info.Protocol == EventProtocol.Entity)
        {
            // 尝试从 SignalContext 中获取 Entity 类型的 Payload
            if (context.Args != null)
            {
                payload = context.Args;
            }
            else
            {
                Debug.LogWarning($"[PostEventNode] 事件 [{eventName}] 需要 Entity 类型的 Payload，但上下文中未提供喵~");
            }
        }
        else
        {
            // 其他协议从配置字符串解析
            payload = postEventNode.ParsePayload();
        }

#if UNITY_EDITOR || DEBUG
        // --- 运行时契约检查喵~ ---
        if (!ValidatePayload(meta.Info.Protocol, payload))
        {
            Debug.LogError($"<color=red>[PostEventNode] 契约违例！</color> 事件 [{eventName}] 预期的协议是 [{meta.Info.Protocol}]，" +
                           $"但实际传了 [{payload?.GetType().Name ?? "null"}] 喵~\n" +
                           $"请检查 PayloadValue 配置是否正确喵~");
            PropagateSignal(postEventNode, context, pack);
            return;
        }
#endif

        // --- 发送事件喵~ ---
        try
        {
            PostOffice.Send(eventName, payload);

            if (runner != null && runner.EnableDebugLog)
            {
                Debug.Log($"[PostEventNode] 事件 [{eventName}] 已成功发送喵~");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[PostEventNode] 发送事件 [{eventName}] 失败：{e}");
        }

        // --- 传播信号到下游喵~ ---
        // 将 Payload 注入到 Context 中，供下游节点使用
        if (payload != null)
        {
            context.Args = payload;
        }

        PropagateSignal(postEventNode, context, pack);
    }

    public override void OnEvent(BaseNodeData data, string eventName, object eventData, BasePackData pack, GraphRunner runner, string packInstanceID)
    {
        // PostEvent 节点不直接响应外部事件
    }

    /// <summary>
    /// 传播信号到输出端口连接的节点喵~
    /// </summary>
    private void PropagateSignal(PostEventNodeData node, SignalContext context, BasePackData pack)
    {
        if (node.OutputNodeIDs == null || node.OutputNodeIDs.Count == 0) return;

        EnqueueSignals(pack, node.OutputNodeIDs, context);
    }

    /// <summary>
    /// 验证 Payload 是否符合协议喵~
    /// </summary>
    private bool ValidatePayload(EventProtocol protocol, object payload)
    {
        switch (protocol)
        {
            case EventProtocol.None:
                return payload == null;

            case EventProtocol.Numeric:
                return payload is float || payload is int || payload is double || payload is long;

            case EventProtocol.String:
                return payload is string;

            case EventProtocol.Vector:
                return payload is Vector3 || payload is Vector2;

            case EventProtocol.Boolean:
                return payload is bool;

            case EventProtocol.Entity:
                return payload != null;

            default:
                return true;
        }
    }
}

}
