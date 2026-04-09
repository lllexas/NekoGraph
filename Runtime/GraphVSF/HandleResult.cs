namespace NekoGraph
{
    /// <summary>
    /// VFS Handler 执行结果喵~
    /// </summary>
    public enum HandleResult
    {
        /// <summary>正常推进：VFSNodeStrategy 执行 EnqueueSignals 向下传播信号</summary>
        Push,

        /// <summary>Handle 自理：VFSNodeStrategy 把 EnqueueSignals 作为委托传递给 Handle 决定何时传递</summary>
        Wait,
        
        /// <summary>执行错误：记录日志，不传播信号</summary>
        Error
    }
}
