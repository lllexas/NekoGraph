#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Trigger 注册表信息 - 用于编辑器 UI 展示的事件元数据喵~
/// 
/// 提供类似 CommandRegistryInfo 的分类查询功能，支持双级下拉菜单喵~
/// 
/// ═══════════════════════════════════════════════════════════════
/// ✅ 元数据自动从 TriggerRegistry 的 [EventInfo] 和 [TriggerEventInfo] 读取
/// ═══════════════════════════════════════════════════════════════
/// 
/// 【修复版】只返回有实际内容的分类，避免显示空分类和老旧信息喵~
/// </summary>
public static class TriggerRegistryInfo
{
    /// <summary>
    /// 事件元数据信息喵~
    /// </summary>
    [Serializable]
    public class EventInfo
    {
        public string EventName;          // 内部名：如 "GameStarted" 或 "主线推进 A"
        public string DisplayName;        // 显示名：如 "🔔 游戏开始" 或 "📖 主线推进 A"
        public string Category;           // 分类：如 "系统" 或 "主线剧情"
        public EventProtocol Protocol;    // 协议类型
        public string Tooltip;            // 提示信息
    }

    // 注册表喵~
    private static Dictionary<string, EventInfo> _events = new Dictionary<string, EventInfo>();

    // 是否已初始化喵~
    private static bool _isInitialized = false;

    // =========================================================
    // 初始化：从 TriggerRegistry 自动读取喵~
    // =========================================================

    [RuntimeInitializeOnLoadMethod]
    private static void Initialize()
    {
        if (_isInitialized) return;
        InitializeInternal();
    }

    /// <summary>
    /// 内部初始化方法（编辑器也可调用）喵~
    /// </summary>
    private static void InitializeInternal()
    {
        if (_isInitialized) return;

        _events.Clear();

        // 从 TriggerRegistry 自动读取所有事件元数据喵~
        var triggers = TriggerRegistry.GetAllTriggers();

        foreach (var trigger in triggers)
        {
            RegisterEvent(
                trigger.EventName,
                trigger.Info.DisplayName,
                trigger.Info.Category,
                trigger.Info.Protocol,
                trigger.Info.Tooltip
            );
        }

        _isInitialized = true;
        Debug.Log($"[TriggerRegistryInfo] 初始化完成，共加载 {_events.Count} 个事件元数据喵~");
    }

    // =========================================================
    // 注册 API 喵~
    // =========================================================

    /// <summary>
    /// 注册一个事件喵~
    /// </summary>
    private static void RegisterEvent(string eventName, string displayName,
                                       string category, EventProtocol protocol,
                                       string tooltip)
    {
        _events[eventName] = new EventInfo
        {
            EventName = eventName,
            DisplayName = displayName,
            Category = category,
            Protocol = protocol,
            Tooltip = tooltip
        };
    }

    /// <summary>
    /// 获取所有分类列表（只返回有实际事件的分类）喵~
    /// </summary>
    public static List<string> GetAllCategories()
    {
        EnsureInitialized();
        
        // 动态统计有实际事件的分类，按字母排序喵~
        return _events.Values
            .Select(e => e.Category)
            .Distinct()
            .OrderBy(e => e)
            .ToList();
    }

    /// <summary>
    /// 获取指定分类下的事件显示名列表喵~
    /// 如果分类为空，返回 ["(无)"] 而不是空列表喵~
    /// </summary>
    public static List<string> GetEventsInCategory(string category)
    {
        EnsureInitialized();
        var events = _events.Values
            .Where(e => e.Category == category)
            .OrderBy(e => e.DisplayName)
            .Select(e => e.DisplayName)
            .ToList();
        
        // 如果分类为空，返回"(无)"而不是空列表喵~
        if (events.Count == 0)
        {
            events.Add("(无)");
        }
        
        return events;
    }

    /// <summary>
    /// 获取所有事件列表（用于下拉框）喵~
    /// </summary>
    public static List<string> GetAllEventDisplayNames()
    {
        EnsureInitialized();
        return _events.Values
            .OrderBy(e => e.DisplayName)
            .Select(e => e.DisplayName)
            .ToList();
    }

    /// <summary>
    /// 获取事件的分类喵~
    /// 找不到返回 "(未分类)" 而不是硬编码的默认值喵~
    /// </summary>
    public static string GetCategoryFromEventName(string eventName)
    {
        EnsureInitialized();
        if (_events.TryGetValue(eventName, out var info))
            return info.Category;
        return "(未分类)";
    }

    /// <summary>
    /// 根据事件名获取显示名喵~
    /// 找不到返回 "(未知事件)" 而不是原样返回喵~
    /// </summary>
    public static string GetDisplayNameFromEventName(string eventName)
    {
        EnsureInitialized();
        if (_events.TryGetValue(eventName, out var info))
            return info.DisplayName;
        return "(未知事件)";
    }

    /// <summary>
    /// 根据显示名找回事件名喵~
    /// </summary>
    public static string GetEventNameFromDisplayName(string displayName)
    {
        EnsureInitialized();
        
        // 特殊处理"(无)"选项喵~
        if (displayName == "(无)") return "";
        
        foreach (var kvp in _events)
        {
            if (kvp.Value.DisplayName == displayName)
                return kvp.Key;
        }
        return displayName;
    }

    /// <summary>
    /// 获取事件详情喵~
    /// </summary>
    public static bool TryGetEventInfo(string eventName, out EventInfo info)
    {
        EnsureInitialized();
        return _events.TryGetValue(eventName, out info);
    }

    /// <summary>
    /// 根据显示名获取事件详情喵~
    /// </summary>
    public static bool TryGetEventInfoByDisplayName(string displayName, out EventInfo info)
    {
        EnsureInitialized();
        foreach (var kvp in _events)
        {
            if (kvp.Value.DisplayName == displayName)
            {
                info = kvp.Value;
                return true;
            }
        }
        info = null;
        return false;
    }

    /// <summary>
    /// 获取事件的协议类型喵~
    /// </summary>
    public static EventProtocol GetProtocolFromEventName(string eventName)
    {
        EnsureInitialized();
        if (_events.TryGetValue(eventName, out var info))
            return info.Protocol;
        return EventProtocol.None;
    }

    /// <summary>
    /// 确保已初始化喵~
    /// </summary>
    private static void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            InitializeInternal();
        }
    }

    // =========================================================
    /// 编辑器初始化入口喵~
    // =========================================================
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void EditorInitialize()
    {
        InitializeInternal();
    }
#endif
}
#endif
