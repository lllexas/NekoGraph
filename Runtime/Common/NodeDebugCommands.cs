using System.Linq;
using UnityEngine;

/// <summary>
/// 图节点调试命令。
/// 主要服务于 CommandNode，在运行时输出带 Pack / Node / SignalPath 的日志。
/// </summary>
public static class NodeDebugCommands
{
    [CommandInfo("node_log", "Node Log", "Debug", new[] { "message" })]
    public static CommandOutput NodeLog(IConsoleController console, int subjectLevel, string[] args, object payload)
    {
        return WriteDebugLog("node_log", console, subjectLevel, args, payload);
    }

    [CommandInfo("debug_log", "Debug Log", "Debug", new[] { "message" })]
    public static CommandOutput DebugLog(IConsoleController console, int subjectLevel, string[] args, object payload)
    {
        return WriteDebugLog("debug_log", console, subjectLevel, args, payload);
    }

    private static CommandOutput WriteDebugLog(
        string commandName,
        IConsoleController console,
        int subjectLevel,
        string[] args,
        object payload)
    {
        var message = args != null && args.Length > 0
            ? string.Join(" ", args.Where(arg => !string.IsNullOrWhiteSpace(arg)))
            : "(empty)";

        if (console is GraphCommandConsoleContext graphConsole)
        {
            Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                null,
                "[{0}] {1} path={2} payload={3} message={4}",
                commandName,
                graphConsole.BuildDebugPrefix(),
                graphConsole.SignalPath,
                GraphCommandConsoleContext.SummarizeValue(payload),
                message);
        }
        else
        {
            Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                null,
                "[{0}] subject={1} payload={2} message={3}",
                commandName,
                subjectLevel,
                GraphCommandConsoleContext.SummarizeValue(payload),
                message);
        }

        return CommandOutput.Success($"{commandName}: {message}", payload);
    }
}
