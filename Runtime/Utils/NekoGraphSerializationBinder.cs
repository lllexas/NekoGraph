using System;
using Newtonsoft.Json.Serialization;

/// <summary>
/// 程序集迁移序列化绑定器喵~
///
/// 历史存档的 $type 写的是 "SomeType, Assembly-CSharp"，
/// 但 NekoGraph 独立成程序集后，这些类型移到了 "NekoGraph.Runtime"。
///
/// 支持两种情形：
/// 1. 普通类型：  "BaseNodeData, Assembly-CSharp"
///    → 直接换程序集名查找
///
/// 2. 泛型类型：  "Dictionary`2[[String, mscorlib],[BaseNodeData, Assembly-CSharp]], mscorlib"
///    → 把 typeName 里嵌套的 ", Assembly-CSharp]" 替换后，用 Type.GetType 解析
/// </summary>
public class NekoGraphSerializationBinder : DefaultSerializationBinder
{
    public static readonly NekoGraphSerializationBinder Instance = new NekoGraphSerializationBinder();

    public override Type BindToType(string assemblyName, string typeName)
    {
        // 1. 先正常查，大多数情况在这里直接命中
        try { return base.BindToType(assemblyName, typeName); }
        catch { }

        // 2. 泛型类型：typeName 内部可能嵌着 ", Assembly-CSharp]"
        //    用替换后的完整名称让 Type.GetType 来解析
        if (typeName.Contains(", Assembly-CSharp"))
        {
            string migrated = MigrateEmbeddedAssembly(typeName);
            string fullName = $"{migrated}, {assemblyName}";
            var t = Type.GetType(fullName);
            if (t != null) return t;
        }

        // 3. 普通类型：assemblyName 本身是 Assembly-CSharp，换成 NekoGraph.Runtime 重试
        if (assemblyName == "Assembly-CSharp" || assemblyName == "Assembly-CSharp-firstpass")
        {
            try { return base.BindToType("NekoGraph.Runtime", typeName); }
            catch { }
        }

        throw new Exception($"[NekoGraph] 无法解析类型：'{typeName}, {assemblyName}' 喵~");
    }

    /// <summary>
    /// 把泛型参数列表里的 ", Assembly-CSharp]" 替换成 ", NekoGraph.Runtime]"
    /// 例：
    ///   Dictionary`2[[String,mscorlib],[BaseNodeData, Assembly-CSharp]]
    ///   → Dictionary`2[[String,mscorlib],[BaseNodeData, NekoGraph.Runtime]]
    /// </summary>
    private static string MigrateEmbeddedAssembly(string typeName)
    {
        // 只替换泛型参数闭合括号前的 Assembly-CSharp，避免误伤非泛型部分
        return typeName
            .Replace(", Assembly-CSharp]]", ", NekoGraph.Runtime]]")
            .Replace(", Assembly-CSharp]",  ", NekoGraph.Runtime]");
    }
}
