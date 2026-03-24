using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 命令注册表 - NekoGraph 命令系统基础设施喵~
///
/// 全域扫描所有程序集中带 [CommandInfo] 特性的静态方法并自动注册。
/// 命令砖块可定义在任意程序集的任意类中，无需继承或显式注册。
///
/// 命令砖块签名规范：
///   public static CommandOutput MyCmd(IConsoleController console, int subjectLevel, string[] args, object payload)
/// </summary>
// =========================================================
// 顶层类型定义（全局可用）喵~
// =========================================================

public enum CommandResult
{
    Success,
    Failed,
    Skipped,
    Pending
}

public class CommandOutput
{
    public CommandResult Result { get; set; }
    public string Message { get; set; }
    public object Payload { get; set; }

    public static CommandOutput Success(string message = null, object payload = null)
        => new CommandOutput { Result = CommandResult.Success, Message = message, Payload = payload };

    public static CommandOutput Fail(string error)
        => new CommandOutput { Result = CommandResult.Failed, Message = error };

    public static CommandOutput Skip()
        => new CommandOutput { Result = CommandResult.Skipped };

    public static CommandOutput Pending()
        => new CommandOutput { Result = CommandResult.Pending };
}

public static class CommandRegistry
{
    // 命令执行委托喵~
    public delegate CommandOutput CommandHandlerWithOutput(IConsoleController console, int subjectLevel, string[] args, object payload);

    // 命令处理器缓存喵~
    private static Dictionary<string, CommandHandlerWithOutput> _commandHandlers;
    private static Dictionary<string, CommandInfoAttribute> _commandMetadatas;
    private static bool _isInitialized = false;

    // =========================================================
    // 初始化：全域扫描喵~
    // =========================================================

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Initialize()
    {
        if (_isInitialized) return;

        _commandHandlers = new Dictionary<string, CommandHandlerWithOutput>();
        _commandMetadatas = new Dictionary<string, CommandInfoAttribute>();

        // 全域扫描：所有已加载程序集中带 [CommandInfo] 的公开静态方法喵~
        var allMethods = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(SafeGetTypes)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static));

        foreach (var method in allMethods)
        {
            var attr = method.GetCustomAttribute<CommandInfoAttribute>();
            if (attr == null) continue;

            var parameters = method.GetParameters();
            if (parameters.Length == 4 &&
                parameters[0].ParameterType == typeof(IConsoleController) &&
                parameters[1].ParameterType == typeof(int) &&
                parameters[2].ParameterType == typeof(string[]) &&
                parameters[3].ParameterType == typeof(object) &&
                method.ReturnType == typeof(CommandOutput))
            {
                var handler = (CommandHandlerWithOutput)Delegate.CreateDelegate(typeof(CommandHandlerWithOutput), method);
                _commandHandlers[attr.Name.ToLower()] = handler;
                _commandMetadatas[attr.Name.ToLower()] = attr;
            }
            else
            {
                Debug.LogWarning($"[CommandRegistry] 命令方法 {method.DeclaringType?.Name}.{method.Name} 签名不符合规范，已跳过喵~\n" +
                                 $"要求: (IConsoleController, int, string[], object) → CommandOutput");
            }
        }

        _isInitialized = true;
        Debug.Log($"[CommandRegistry] 初始化完成，共注册 {_commandHandlers.Count} 个命令喵~");
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch { return Array.Empty<Type>(); }
    }

    private static void EnsureInitialized()
    {
        if (!_isInitialized) Initialize();
    }

    // =========================================================
    // 执行入口喵~
    // =========================================================

    public static CommandOutput Execute(string commandName, int subjectLevel, string[] args, object payload = null, IConsoleController console = null)
    {
        EnsureInitialized();

        if (string.IsNullOrEmpty(commandName))
            return CommandOutput.Skip();

        string key = commandName.ToLower();
        if (_commandHandlers.TryGetValue(key, out var handler))
        {
            try
            {
                return handler.Invoke(console, subjectLevel, args, payload);
            }
            catch (Exception e)
            {
                Debug.LogError($"[CommandRegistry] 执行命令 {commandName} 失败：{e} 喵~");
                return CommandOutput.Fail($"执行命令 {commandName} 失败：{e.Message}");
            }
        }

        Debug.LogWarning($"[CommandRegistry] 未知命令：{commandName} 喵~");
        return CommandOutput.Fail($"未知命令：{commandName}");
    }

    // =========================================================
    // 元数据查询喵~
    // =========================================================

    public static Dictionary<string, CommandInfoAttribute> GetAllMetadatas()
    {
        EnsureInitialized();
        return new Dictionary<string, CommandInfoAttribute>(_commandMetadatas);
    }

    public static bool TryGetMetadata(string commandName, out CommandInfoAttribute metadata)
    {
        EnsureInitialized();
        return _commandMetadatas.TryGetValue(commandName.ToLower(), out metadata);
    }

    public static List<string> GetAllCommandNames()
    {
        EnsureInitialized();
        return new List<string>(_commandMetadatas.Keys);
    }
}
