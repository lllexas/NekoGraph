using System;

/// <summary>
/// VFSResource 属性 - 声明一种 VFS 后缀资源的身份信息。
///
/// 设计目标：
/// 1. 在类级别唯一声明资源的 Suffix 和 DataType
/// 2. 避免 Execute / Query 分别重复书写同一份资源元数据
/// 3. 为未来扩展更多能力标签保留统一挂载点
///
/// 示例：
///   [VFSResource(".msg", typeof(SocialMessageVFSData))]
///   public static class MsgResource
///   {
///       [VFSExecute]
///       public static HandleResult Execute(...)
///
///       [VFSQuery]
///       public static VFSQueryResult Query(...)
///   }
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class VFSResourceAttribute : Attribute
{
    /// <summary>
    /// 资源对应的 VFS 后缀名（如 ".msg"、".dialog"、".choice"）
    /// 不带点时会自动补全喵~
    /// </summary>
    public string Suffix { get; }

    /// <summary>
    /// 资源内容对应的数据类型。
    /// 可为 null，表示自由 JSON、自由文本或运行时自行解析。
    /// </summary>
    public Type DataType { get; }

    public VFSResourceAttribute(string suffix, Type dataType = null)
    {
        Suffix = suffix;
        DataType = dataType;
    }
}

/// <summary>
/// VFSExecute 属性 - 标记某个 VFS 资源的执行能力方法。
///
/// 资源的 Suffix / DataType 由所在类上的 VFSResourceAttribute 声明；
/// 此标签只表示“这个方法是 Execute 能力入口”。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class VFSExecuteAttribute : Attribute
{
}

/// <summary>
/// VFSQuery 属性 - 标记某个 VFS 资源的查询/预览能力方法。
///
/// 资源的 Suffix / DataType 由所在类上的 VFSResourceAttribute 声明；
/// 此标签只表示“这个方法是 Query 能力入口”。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class VFSQueryAttribute : Attribute
{
}
