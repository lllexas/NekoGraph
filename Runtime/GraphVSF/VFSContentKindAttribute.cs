using System;

namespace NekoGraph
{

    /// <summary>
    /// VFS 内容格式声明 - 在数据类型上标注载荷格式喵~
    ///
    /// 用法：
    ///   [VFSContentKind(VFSContentKind.Csv)]
    ///   public class ChoiceData { }
    ///
    ///   [EXEHandler(".choice", typeof(ChoiceData))]
    ///   public static HandleResult Handle(...) { }
    ///
    /// 编辑器会自动查询此 Attribute 来推断节点的内容类型。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public class VFSContentKindAttribute : Attribute
    {
        /// <summary>
        /// 该数据类型对应的 VFS 内容格式
        /// </summary>
        public VFSContentKind Kind { get; }

        public VFSContentKindAttribute(VFSContentKind kind)
        {
            Kind = kind;
        }
    }

}
