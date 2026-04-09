using System;
using System.Collections.Generic;
using UnityEngine;

namespace NekoGraph
{

// =========================================================
// 命令节点策略喵~
// =========================================================

/// <summary>
/// CommandNode 策略 - 命令执行节点喵~
/// 负责执行各种游戏命令（如召唤单位、发放奖励、修改资源等）
///
/// 【重构后】直接使用 CommandRegistry.Execute() 统一入口喵~
/// </summary>
public class CommandNodeStrategy : NodeStrategy
{
    public override void OnSignalEnter(BaseNodeData data, SignalContext context, BasePackData pack, GraphRunner runner, string packInstanceID)
    {
        if (data is not CommandNodeData commandNode) return;

        if (runner.EnableDebugLog)
        {
            Debug.Log($"[CommandNode] 执行命令：{commandNode.Command.CommandName}");
        }

        ExecuteCommand(commandNode, context, pack, runner, packInstanceID);
        PropagateSignal(commandNode, context, pack);
    }

    public override void OnEvent(BaseNodeData data, string eventName, object eventData, BasePackData pack, GraphRunner runner, string packInstanceID)
    {
        // 命令节点通常不直接响应外部事件
    }

    private void ExecuteCommand(CommandNodeData node, SignalContext context, BasePackData pack, GraphRunner runner, string packInstanceID)
    {
        var command = node.Command;

        if (string.IsNullOrEmpty(command.CommandName))
        {
            Debug.LogWarning("[CommandNode] 命令名为空，跳过执行喵~");
            return;
        }

        var args = BuildCommandArgs(command, context);

        try
        {
            int subjectLevel = runner?.GetSubjectLevel() ?? PackAccessSubjects.Player;
            var consoleContext = new GraphCommandConsoleContext(
                pack,
                packInstanceID,
                node,
                command.CommandName,
                subjectLevel,
                context);
            var output = CommandRegistry.Execute(command.CommandName, subjectLevel, args, context.Args, consoleContext);

            if (output.Payload != null)
            {
                context.Args = output.Payload;
            }

            if (!string.IsNullOrEmpty(output.Message))
            {
                if (output.Result == CommandResult.Failed)
                    Debug.LogError($"[CommandNode] {output.Message}");
                else if (runner != null && runner.EnableDebugLog)
                    Debug.Log($"[CommandNode] {output.Message}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[CommandNode] 命令执行失败：{command.CommandName}, 错误：{e.Message}");
        }
    }

    private string[] BuildCommandArgs(CommandData command, SignalContext context)
    {
        var argsList = new List<string>();

        // Parameters 是唯一真源；Parameter 只是 Parameters[0] 的包装
        if (command.Parameters != null && command.Parameters.Count > 0)
        {
            foreach (var parameter in command.Parameters)
            {
                if (!string.IsNullOrWhiteSpace(parameter))
                {
                    argsList.Add(parameter);
                }
            }
        }

        if (context.Args != null)
        {
            if (context.Args is string strData)
            {
                argsList.Add(strData);
            }
            else if (context.Args is MissionArgs args)
            {
                if (!string.IsNullOrEmpty(args.StringKey))
                    argsList.Add(args.StringKey);
                if (args.Amount > 0)
                    argsList.Add(args.Amount.ToString());
            }
        }

        return argsList.ToArray();
    }

    private void PropagateSignal(CommandNodeData node, SignalContext context, BasePackData pack)
    {
        // 运行时只认 CommandNode 的语义输出字段 OutputNodeIDs
        EnqueueSignals(pack, node.OutputNodeIDs, context);
    }
}

}
