using System;
using UnityEngine;
using System.Collections.Generic;

namespace NekoGraph
{

/// <summary>
/// VFSNode 策略 - VFS 节点执行处理器喵~
///
/// 职责：
/// - 目录节点（IsDirectory）：直接透传信号，不执行任何逻辑
/// - 文件节点（IsFile）：查找 ExeRegistry 中对应后缀的处理器并执行
/// - 始终向下游传播信号，运行时只认 ChildNodeIDs
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
        string packIDKey)
    {
        if (data is not VFSNodeData vfsNode) return;

        // 文件节点：查 ExeRegistry 执行对应后缀的处理器喵~
        var result = HandleResult.Push; // 默认继续传播
        List<string> suspendedIds = null;
        System.Action continueAction = null;
        if (vfsNode.IsFile && vfsNode.IsEnabled)
        {
            if (ExeRegistry.TryGetHandler(vfsNode.Extension, out var handler))
            {
                // continueAction 在 handler 返回前构建，此时还不知道是否 Wait，先用占位喵~
                try
                {
                    var content = VFSContentResolver.Resolve(vfsNode);
                    continueAction = () => ResumeSuspendedSignals(pack, suspendedIds);
                    result = handler.Invoke(content, context, pack, runner, packIDKey, continueAction);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[VFSNodeStrategy] 执行后缀 '{vfsNode.Extension}' 的处理器失败：{e.Message} 喵~");
                    Debug.LogException(e);
                    result = HandleResult.Error;
                }
            }
            else if (runner.EnableDebugLog)
            {
                Debug.LogWarning($"[VFSNodeStrategy] 未找到后缀 '{vfsNode.Extension}' 的 EXEHandler，跳过执行喵~");
            }
        }
        else if (!vfsNode.IsFile || !vfsNode.IsEnabled)
        {
            // 目录节点或禁用节点：直接传播
            EnqueueSignals(pack, vfsNode.ChildNodeIDs, context);
            return;
        }

        // 根据 Handle 返回值决定是否传播信号喵~
        if (result == HandleResult.Push)
        {
            EnqueueSignals(pack, vfsNode.ChildNodeIDs, context);
        }
        else if (result == HandleResult.Wait)
        {
            // Wait 模式：后续信号保存到挂起字典，闭包持有 ID 列表，恢复时精确 Remove 喵~
            suspendedIds = SuspendSignals(pack, vfsNode.ChildNodeIDs, context);
        }
        // HandleResult.Error: 已记录错误，不传播信号
        

    }

    public override void OnEvent(
        BaseNodeData data,
        string eventName,
        object eventData,
        BasePackData pack,
        GraphRunner runner,
        string packIDKey)
    {
        // VFS 节点暂不响应外部事件喵~
    }

    /// <summary>
    /// 暂停信号到挂起字典 - Wait 状态下使用喵~
    /// 返回被挂起的子信号 ID 列表，供 continueAction 闭包持有以精确恢复喵~
    /// </summary>
    private List<string> SuspendSignals(BasePackData pack, IEnumerable<string> targetIds, SignalContext context)
    {
        if (pack.Nodes.TryGetValue(context.CurrentNodeId, out var currentNode))
            currentNode.IsChecked = true;

        var suspendedIds = new List<string>();
        foreach (var targetId in targetIds)
        {
            var newSignal = context.Clone(copyPath: true);
            newSignal.RecordConnection(new ConnectionData(context.CurrentNodeId, -1, targetId, -1));
            newSignal.CurrentNodeId = targetId;
            pack.SuspendedSignals[newSignal.SignalId] = newSignal;
            suspendedIds.Add(newSignal.SignalId);
        }
        return suspendedIds;
    }

    /// <summary>
    /// 从挂起字典恢复信号到活跃队列 - continueAction 回调时调用喵~
    /// 精确 Remove 闭包持有的 key，不影响其他挂起信号喵~
    /// </summary>
    private void ResumeSuspendedSignals(BasePackData pack, List<string> suspendedIds)
    {
        foreach (var id in suspendedIds)
        {
            if (pack.SuspendedSignals.TryGetValue(id, out var signal))
            {
                pack.ActiveSignals.Enqueue(signal);
                pack.SuspendedSignals.Remove(id);
            }
        }
    }
}

}
