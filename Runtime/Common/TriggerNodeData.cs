using System;
using System.Collections.Generic;
using UnityEngine;
using NekoGraph;

/// <summary>
/// 监听器节点数据 - 支持枚举和特性两种事件定义方式喵~
/// </summary>
[Serializable]
public class TriggerNodeData : BaseNodeData
{
    [Tooltip("监听的全局事件枚举喵~（仅当 EventName 为空时有效）")]
    public TriggerEvent Event = TriggerEvent.GameStarted;

    [Tooltip("事件名（支持特性定义的自定义事件，为空时使用 Event 枚举）喵~")]
    public string EventName = "";

    [InPort(0, "激活", NekoPortCapacity.Multi)]
    public List<string> InputNodeIDs = new List<string>();

    [OutPort(0, "信号包 (Signal)", NekoPortCapacity.Multi)]
    public List<string> SignalOutputs = new List<string>();

    /// <summary>
    /// 获取实际的事件名（优先使用 EventName，否则使用 Event 枚举的字符串）喵~
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
        // 如果能解析为枚举，同步更新 Event 字段（向后兼容）喵~
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

    public new void CopyFrom(BaseNodeData other)
    {
        base.CopyFrom(other);
        if (other is TriggerNodeData triggerOther)
        {
            Event = triggerOther.Event;
            EventName = triggerOther.EventName;
            InputNodeIDs = new List<string>(triggerOther.InputNodeIDs);
            SignalOutputs = new List<string>(triggerOther.SignalOutputs);
        }
    }

    /// <summary>
    /// 运行时注册方法喵~
    /// </summary>
    public void Register(Action<object> onTriggered)
    {
        // 底层订阅使用事件名字符串作为 Key 喵~
        PostSystem.Instance.On(GetEventName(), onTriggered);
    }

    /// <summary>
    /// 运行时注销方法喵~
    /// </summary>
    public void Unregister(Action<object> onTriggered)
    {
        PostSystem.Instance.Off(GetEventName(), onTriggered);
    }
}
