namespace NekoGraph
{
    /// <summary>
    /// VFS Handler 执行结果喵~
    /// </summary>
    public enum HandleResult
    {
        /// <summary>正常推进：VFSNodeStrategy 执行 EnqueueSignals 向下传播信号</summary>
        Push,

        /// <summary>挂起当前 signal，本体停留在当前节点；后续必须通过 ResumeToTarget 一类 API 显式决定下一跳</summary>
        Wait,
        
        /// <summary>执行错误：记录日志，不传播信号</summary>
        Error
    }
}
