using System;

/// <summary>
/// 【特性驱动事件定义】TriggerEventInfoAttribute - 用于在任意静态类上定义事件喵~
/// 
/// 使用示例：
/// [TriggerEventInfo("CustomEvent", EventProtocol.Entity, "🎉 自定义事件", "分类")]
/// public static class CustomEvents
/// {
///     public static void Handle(object payload) { ... }
/// }
/// 
/// 扫描规则：
/// - 标记在静态类上
/// - 类中可以包含可选的静态处理方法（无返回值，单个 object 参数）
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class TriggerEventInfoAttribute : Attribute
{
    public string EventName { get; }
    public EventProtocol Protocol { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public string Tooltip { get; set; }
    public string HandlerMethodName { get; set; }

    public TriggerEventInfoAttribute(string eventName, EventProtocol protocol, string displayName, string category)
    {
        EventName = eventName;
        Protocol = protocol;
        DisplayName = displayName;
        Category = category;
        Tooltip = "";
        HandlerMethodName = "Handle"; // 默认处理方法名
    }
}
