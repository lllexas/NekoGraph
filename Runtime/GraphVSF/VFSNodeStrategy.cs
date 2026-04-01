using System;
using UnityEngine;

/// <summary>
/// VFSNode 策略 - VFS 节点执行处理器喵~
///
/// 职责：
/// - 目录节点（IsDirectory）：直接透传信号，不执行任何逻辑
/// - 文件节点（IsFile）：查找 ExeRegistry 中对应后缀的处理器并执行
/// - 始终向下游传播信号（同时传播 OutputConnections 和 ChildNodeIDs，与 CommandNodeStrategy 一致）
///
/// ExeRegistry 处理器由外部项目注册（[EXEHandler] 属性 + 静态方法），
/// NekoGraph 本身不内置任何后缀的具体逻辑，保持通用性喵~
/// </summary>
public class VFSNodeStrategy : NodeStrategy
{
    public override void OnSignalEnter(
        BaseNodeData data,
        SignalContext context,
        BasePackData pack,
        GraphRunner runner,
        string packInstanceID)
    {
        if (data is not VFSNodeData vfsNode) return;

        // 文件节点：查 ExeRegistry 执行对应后缀的处理器喵~
        if (vfsNode.IsFile && vfsNode.IsEnabled)
        {
            if (ExeRegistry.TryGetHandler(vfsNode.Extension, out var handler))
            {
                try
                {
                    handler.Invoke(vfsNode.DataJson, context, pack, runner, packInstanceID);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[VFSNodeStrategy] 执行后缀 '{vfsNode.Extension}' 的处理器失败：{e.Message} 喵~");
                }
            }
            else if (runner.EnableDebugLog)
            {
                Debug.LogWarning($"[VFSNodeStrategy] 未找到后缀 '{vfsNode.Extension}' 的 EXEHandler，跳过执行喵~");
            }
        }

        // 传播信号到下游节点（与 CommandNodeStrategy 保持一致，同时传播两个字段）喵~
        EnqueueSignals(pack, vfsNode.OutputConnections, context);
        EnqueueSignals(pack, vfsNode.ChildNodeIDs, context);
    }

    public override void OnEvent(
        BaseNodeData data,
        string eventName,
        object eventData,
        BasePackData pack,
        GraphRunner runner,
        string packInstanceID)
    {
        // VFS 节点暂不响应外部事件喵~
    }
}
