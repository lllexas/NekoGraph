using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 事件来源类型喵~
/// </summary>
public enum TriggerSource
{
    Enum,         // 来自 TriggerEvent 枚举
    Attribute     // 来自 [TriggerEventInfo] 特性
}

/// <summary>
/// 事件监听注册表 - 支持枚举和特性两种定义方式喵~
/// 职责：为编辑器提供分类显示和协议校验喵~
/// </summary>
public static class TriggerRegistry
{
    public class TriggerMeta
    {
        public string EventName;                // 事件名（统一使用字符串 Key）
        public TriggerEvent? EnumValue;         // 枚举值（仅当来源为 Enum 时有值）
        public EventInfoAttribute Info;         // 元数据信息
        public TriggerSource Source;            // 来源类型
        public Action<object> Handler;          // 可选的默认处理器（仅当特性定义时有值）
    }

    private static readonly Dictionary<string, TriggerMeta> _triggers = new Dictionary<string, TriggerMeta>();
    private static bool _isInitialized = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Initialize()
    {
        _triggers.Clear();
        
        // --- 方式 A: 扫描枚举 (向后兼容) ---
        var enumType = typeof(TriggerEvent);
        var fields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static);

        foreach (var field in fields)
        {
            var attr = field.GetCustomAttribute<EventInfoAttribute>();
            if (attr != null)
            {
                var eventValue = (TriggerEvent)field.GetValue(null);
                var eventName = eventValue.ToString();
                _triggers[eventName] = new TriggerMeta 
                { 
                    EventName = eventName,
                    EnumValue = eventValue,
                    Info = attr,
                    Source = TriggerSource.Enum
                };
            }
        }
        
        // --- 方式 B: 扫描特性 (新增扩展能力) ---
        var allTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(SafeGetTypes)
            .Where(t => t.IsClass && t.IsAbstract && t.IsSealed); // 静态类

        foreach (var type in allTypes)
        {
            var typeAttr = type.GetCustomAttribute<TriggerEventInfoAttribute>();
            if (typeAttr == null) continue;
            
            // 创建 EventInfoAttribute（从特性转换）
            var infoAttr = new EventInfoAttribute(
                typeAttr.Protocol, 
                typeAttr.DisplayName, 
                typeAttr.Category
            )
            {
                Tooltip = typeAttr.Tooltip
            };
            
            // 尝试找到 Handler 方法
            Action<object> handler = null;
            var handlerMethod = type.GetMethod(
                typeAttr.HandlerMethodName,
                BindingFlags.Public | BindingFlags.Static
            );
            
            if (handlerMethod != null && 
                handlerMethod.ReturnType == typeof(void) &&
                handlerMethod.GetParameters().Length == 1 &&
                handlerMethod.GetParameters()[0].ParameterType == typeof(object))
            {
                handler = (Action<object>)Delegate.CreateDelegate(typeof(Action<object>), handlerMethod);
            }
            
            _triggers[typeAttr.EventName] = new TriggerMeta
            {
                EventName = typeAttr.EventName,
                EnumValue = null,
                Info = infoAttr,
                Source = TriggerSource.Attribute,
                Handler = handler
            };
        }
        
        _isInitialized = true;
        Debug.Log($"[TriggerRegistry] 自动同步完成，加载了 {_triggers.Count} 个事件契约喵~ " +
                  $"(枚举：{_triggers.Count(m => m.Value.Source == TriggerSource.Enum)}, " +
                  $"特性：{_triggers.Count(m => m.Value.Source == TriggerSource.Attribute)})");
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (Exception e)
        {
            Debug.LogWarning($"[TriggerRegistry] 加载程序集 {assembly.GetName().Name} 失败：{e.Message}");
            return Array.Empty<Type>();
        }
    }

    private static void EnsureInitialized()
    {
        if (!_isInitialized || _triggers.Count == 0)
        {
            Initialize();
        }
    }

    /// <summary>
    /// 获取所有事件元数据喵~
    /// </summary>
    public static IEnumerable<TriggerMeta> GetAllTriggers()
    {
        EnsureInitialized();
        return _triggers.Values;
    }

    /// <summary>
    /// 根据事件名获取元数据喵~
    /// </summary>
    public static TriggerMeta GetMeta(string eventName)
    {
        EnsureInitialized();
        return _triggers.TryGetValue(eventName, out var meta) ? meta : null;
    }

    /// <summary>
    /// 根据枚举值获取元数据喵~
    /// </summary>
    public static TriggerMeta GetMeta(TriggerEvent evt)
    {
        return GetMeta(evt.ToString());
    }

    /// <summary>
    /// 根据事件名找到对应的枚举喵~（仅对 Enum 来源有效）
    /// </summary>
    public static TriggerEvent Parse(string eventName)
    {
        EnsureInitialized();
        if (Enum.TryParse<TriggerEvent>(eventName, out var result)) return result;
        return TriggerEvent.GameStarted; // 默认
    }
    
    /// <summary>
    /// 检查事件是否已注册喵~
    /// </summary>
    public static bool IsRegistered(string eventName)
    {
        EnsureInitialized();
        return _triggers.ContainsKey(eventName);
    }
}
