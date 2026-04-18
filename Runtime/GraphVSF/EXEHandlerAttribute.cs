using System;

/// <summary>
/// EXEHandler 属性 - 旧式的 VFS 执行处理器标记。
///
/// 已被以下新协议取代：
/// - 类级别：VFSResourceAttribute
/// - 方法级别：VFSExecuteAttribute
///
/// 仍保留此属性以兼容旧代码，但新代码不应继续使用。
/// </summary>
[Obsolete("EXEHandlerAttribute 已废弃。请改用 [VFSResource(...)] + [VFSExecute]。", false)]
[AttributeUsage(AttributeTargets.Method)]
public class EXEHandlerAttribute : Attribute
{
    /// <summary>
    /// 处理的 VFS 后缀名（如 ".prefab"、".entity"）。
    /// 不带点时会自动补全。
    /// </summary>
    public string Suffix { get; }

    /// <summary>
    /// 内容载荷对应的数据类型（用于编辑器字段提示和强类型验证）。
    /// 可为 null，表示纯自由 JSON。
    /// </summary>
    public Type DataType { get; }

    public EXEHandlerAttribute(string suffix, Type dataType = null)
    {
        Suffix = suffix;
        DataType = dataType;
    }
}

/// <summary>
/// VFSQueryHandler 属性 - 旧式的 VFS 查询/预览处理器标记。
///
/// 已被以下新协议取代：
/// - 类级别：VFSResourceAttribute
/// - 方法级别：VFSQueryAttribute
///
/// 仍保留此属性以兼容旧代码，但新代码不应继续使用。
/// </summary>
[Obsolete("VFSQueryHandlerAttribute 已废弃。请改用 [VFSResource(...)] + [VFSQuery]。", false)]
[AttributeUsage(AttributeTargets.Method)]
public class VFSQueryHandlerAttribute : Attribute
{
    /// <summary>
    /// 处理的 VFS 后缀名（如 ".msg"、".dialog"、".choice"）。
    /// 不带点时会自动补全。
    /// </summary>
    public string Suffix { get; }

    /// <summary>
    /// 内容载荷对应的数据类型（用于编辑器字段提示和强类型验证）。
    /// 可为 null，表示纯自由 JSON 或自由文本。
    /// </summary>
    public Type DataType { get; }

    public VFSQueryHandlerAttribute(string suffix, Type dataType = null)
    {
        Suffix = suffix;
        DataType = dataType;
    }
}
