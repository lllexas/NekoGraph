using System;

namespace NekoGraph
{
    /// <summary>
    /// Execute 能力委托：运行态入口，允许副作用并参与 GraphRunner 调度。
    /// </summary>
    public delegate HandleResult VFSExecuteDelegate(
        VFSResolvedContent content,
        SignalContext context,
        BasePackData pack,
        GraphRunner runner,
        string packInstanceID,
        Action continueAction
    );

    /// <summary>
    /// Query 能力委托：前台读取入口，返回前端可消费的展示描述。
    /// </summary>
    public delegate VFSQueryResult VFSQueryDelegate(
        VFSResolvedContent content,
        VFSQueryContext context
    );

    /// <summary>
    /// 单个 VFS 资源类型在注册表中的聚合描述。
    /// 一个后缀只应有一份资源声明，但可拥有多个能力槽中的任意子集。
    /// </summary>
    public sealed class VFSResourceDriver
    {
        public string Suffix { get; set; }
        public Type DataType { get; set; }
        public VFSExecuteDelegate Execute { get; set; }
        public VFSQueryDelegate Query { get; set; }

        public bool HasExecute => Execute != null;
        public bool HasQuery => Query != null;
    }
}
