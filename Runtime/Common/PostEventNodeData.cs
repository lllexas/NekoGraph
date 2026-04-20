using System;
using System.Collections.Generic;
using UnityEngine;
using NekoGraph;

/// <summary>
/// PostOffice 发送节点数据 - 在流程图任意环节发送 TriggerEvent 事件喵~
/// </summary>
[Serializable]
public class PostEventNodeData : BaseNodeData
{
    [Tooltip("要发送的事件枚举（仅当 EventName 为空时有效）喵~")]
    public TriggerEvent Event = TriggerEvent.GameStarted;

    [Tooltip("要发送的事件名（支持特性定义的自定义事件，为空时使用 Event 枚举）喵~")]
    public string EventName = "";

    [Tooltip("事件负载 Payload 的字符串表示（用于 Numeric/String/Boolean 协议）喵~")]
    public string PayloadValue = "";

    [InPort(0, "输入", NekoPortCapacity.Multi)]
    public List<string> InputNodeIDs = new List<string>();

    [OutPort(0, "输出", NekoPortCapacity.Multi)]
    public List<string> OutputNodeIDs = new List<string>();

    /// <summary>
    /// 获取实际的事件名喵~
    /// </summary>
    public string GetEventName()
    {
        return string.IsNullOrEmpty(EventName) ? Event.ToString() : EventName;
    }

    /// <summary>
    /// 设置事件名喵~
    /// </summary>
    public void SetEventName(string eventName)
    {
        EventName = eventName;
        if (Enum.TryParse<TriggerEvent>(eventName, out var enumValue))
        {
            Event = enumValue;
        }
    }

    /// <summary>
    /// 获取当前事件元数据喵~
    /// </summary>
    public TriggerRegistry.TriggerMeta GetMeta()
    {
        return TriggerRegistry.GetMeta(GetEventName());
    }

    /// <summary>
    /// 根据协议解析 Payload 喵~
    /// </summary>
    public object ParsePayload()
    {
        var meta = GetMeta();
        if (meta == null) return null;

        switch (meta.Info.Protocol)
        {
            case EventProtocol.None:
                return null;

            case EventProtocol.Numeric:
                if (float.TryParse(PayloadValue, out var floatVal))
                    return floatVal;
                if (int.TryParse(PayloadValue, out var intVal))
                    return intVal;
                return 0;

            case EventProtocol.String:
                return PayloadValue ?? "";

            case EventProtocol.Boolean:
                if (bool.TryParse(PayloadValue, out var boolVal))
                    return boolVal;
                return false;

            case EventProtocol.Vector:
                // 简单解析 "x,y,z" 格式喵~
                if (!string.IsNullOrEmpty(PayloadValue))
                {
                    var parts = PayloadValue.Split(',');
                    if (parts.Length >= 2 &&
                        float.TryParse(parts[0], out var x) &&
                        float.TryParse(parts[1], out var y))
                    {
                        float z = parts.Length >= 3 && float.TryParse(parts[2], out var zVal) ? zVal : 0f;
                        return new Vector3(x, y, z);
                    }
                }
                return Vector3.zero;

            case EventProtocol.Entity:
                // Entity 类型需要特殊处理，这里暂时返回 null
                // 实际使用时可能需要通过上下文获取实体引用喵~
                Debug.LogWarning("[PostEventNode] Entity 协议的事件需要通过代码设置 Payload，无法通过字符串配置喵~");
                return null;

            case EventProtocol.SpawnRequest:
                Debug.LogWarning("[PostEventNode] SpawnRequest 协议的事件需要通过代码设置 Payload，无法通过字符串配置喵~");
                return null;

            default:
                return null;
        }
    }

    public new void CopyFrom(BaseNodeData other)
    {
        base.CopyFrom(other);
        if (other is PostEventNodeData postOther)
        {
            Event = postOther.Event;
            EventName = postOther.EventName;
            PayloadValue = postOther.PayloadValue;
            InputNodeIDs = new List<string>(postOther.InputNodeIDs);
            OutputNodeIDs = new List<string>(postOther.OutputNodeIDs);
        }
    }
}
