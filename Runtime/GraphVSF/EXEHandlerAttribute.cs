using System;

/// <summary>
/// EXEHandler 属性 - 标记 VFS 文件节点的后缀执行处理器喵~
///
/// 用法：在公开静态方法上标注后缀名，ExeRegistry 会在初始化时自动扫描并注册。
///
/// 方法签名规范（推荐）：
///   public static void MyHandler(VFSResolvedContent content, SignalContext context, BasePackData pack, GraphRunner runner, string packInstanceID)
///
/// 兼容旧签名：
///   public static void MyHandler(string dataJson, SignalContext context, BasePackData pack, GraphRunner runner, string packInstanceID)
///
/// 示例：
///   [EXEHandler(".prefab", typeof(PrefabSpawnData))]
///   public static void HandlePrefab(VFSResolvedContent content, SignalContext ctx, BasePackData pack, GraphRunner runner, string instanceID)
///   {
///       var data = content.ParseJson&lt;PrefabSpawnData&gt;();
///       // ...
///   }
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class EXEHandlerAttribute : Attribute
{
    /// <summary>
    /// 处理的 VFS 后缀名（如 ".prefab"、".entity"）
    /// 不带点时会自动补全喵~
    /// </summary>
    public string Suffix { get; }

    /// <summary>
    /// 内容载荷对应的数据类型（用于编辑器字段提示和强类型验证）
    /// 可为 null，表示纯自由 JSON
    /// </summary>
    public Type DataType { get; }

    public EXEHandlerAttribute(string suffix, Type dataType = null)
    {
        Suffix = suffix;
        DataType = dataType;
    }
}
