using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 命令注册表信息 - 用于编辑器 UI 展示的命令元数据喵~
///
/// ═══════════════════════════════════════════════════════════════
/// ✅ 重构后：元数据自动从 CommandRegistry 的 [CommandInfo] Attribute 读取
/// ═══════════════════════════════════════════════════════════════
///
/// 添加新命令时只需要在 CommandRegistry.cs 中：
/// 1. 添加静态方法
/// 2. 贴上 [CommandInfo] 特性
///
/// 元数据会自动同步，无需手动维护两份东西喵~
///
/// ═══════════════════════════════════════════════════════════════
/// 
/// 【修复版】只返回有实际内容的分类，避免显示空分类和老旧信息喵~
/// </summary>
public static class CommandRegistryInfo
{
    /// <summary>
    /// 命令元数据信息喵~
    /// </summary>
    [Serializable]
    public class CommandInfo
    {
        public string CommandName;        // 内部名：如 "spawn"
        public string DisplayName;        // 显示名：如 "🏗️ 召唤单位"
        public string Category;           // 分类：如 "Entity"
        public string[] ParameterNames;   // 参数名：如 ["BlueprintID", "Position", "Team"]
        public string Tooltip;            // 提示信息
        public Color EditorColor;         // 编辑器颜色
    }

    // 注册表喵~
    private static Dictionary<string, CommandInfo> _commands = new Dictionary<string, CommandInfo>();

    // 是否已初始化喵~
    private static bool _isInitialized = false;

    // =========================================================
    // 初始化：从 CommandRegistry 的 Attribute 自动读取喵~
    // =========================================================

    [RuntimeInitializeOnLoadMethod]
    private static void Initialize()
    {
        if (_isInitialized) return;
        InitializeInternal();
    }

    /// <summary>
    /// 内部初始化方法（编辑器也可调用）喵~
    /// </summary>
    private static void InitializeInternal()
    {
        if (_isInitialized) return;

        _commands.Clear();

        // 从 CommandRegistry 的 [CommandInfo] Attribute 自动读取元数据喵~
        var metadatas = CommandRegistry.GetAllMetadatas();

        foreach (var kvp in metadatas)
        {
            var attr = kvp.Value;
            RegisterCommand(
                attr.Name,
                attr.DisplayName,
                attr.Category,
                attr.Parameters,
                attr.Tooltip,
                attr.ParsedColor
            );
        }

        _isInitialized = true;
        Debug.Log($"[CommandRegistryInfo] 初始化完成，共加载 {_commands.Count} 个命令元数据喵~");
    }

    // =========================================================
    // 注册 API 喵~
    // =========================================================

    /// <summary>
    /// 注册一个命令喵~
    /// </summary>
    private static void RegisterCommand(string commandName, string displayName,
                                        string category, string[] parameterNames,
                                        string tooltip, Color editorColor)
    {
        _commands[commandName] = new CommandInfo
        {
            CommandName = commandName,
            DisplayName = displayName,
            Category = category,
            ParameterNames = parameterNames ?? Array.Empty<string>(),
            Tooltip = tooltip,
            EditorColor = editorColor
        };
    }

    /// <summary>
    /// 获取所有命令（按分类分组）喵~
    /// </summary>
    public static IGrouping<string, CommandInfo>[] GetCommandsByCategory()
    {
        EnsureInitialized();
        return _commands.Values
            .OrderBy(c => c.Category)
            .ThenBy(c => c.DisplayName)
            .GroupBy(c => c.Category)
            .ToArray();
    }

    /// <summary>
    /// 获取所有命令列表（用于下拉框）喵~
    /// </summary>
    public static List<string> GetAllCommandDisplayNames()
    {
        EnsureInitialized();
        return _commands.Values
            .OrderBy(c => c.DisplayName)
            .Select(c => c.DisplayName)
            .ToList();
    }

    /// <summary>
    /// 获取所有分类列表（只返回有实际命令的分类）喵~
    /// </summary>
    public static List<string> GetAllCategories()
    {
        EnsureInitialized();
        
        // 动态统计有实际命令的分类，按字母排序喵~
        return _commands.Values
            .Select(c => c.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToList();
    }

    /// <summary>
    /// 获取指定分类下的命令显示名列表喵~
    /// 如果分类为空，返回 ["(无)"] 而不是空列表喵~
    /// </summary>
    public static List<string> GetCommandsInCategory(string category)
    {
        EnsureInitialized();
        var commands = _commands.Values
            .Where(c => c.Category == category)
            .OrderBy(c => c.DisplayName)
            .Select(c => c.DisplayName)
            .ToList();
        
        // 如果分类为空，返回"(无)"而不是空列表喵~
        if (commands.Count == 0)
        {
            commands.Add("(无)");
        }
        
        return commands;
    }

    /// <summary>
    /// 获取命令的分类喵~
    /// 找不到返回 "(未分类)" 而不是硬编码的默认值喵~
    /// </summary>
    public static string GetCategoryFromCommandName(string commandName)
    {
        EnsureInitialized();
        if (_commands.TryGetValue(commandName, out var info))
            return info.Category;
        return "(未分类)";
    }

    /// <summary>
    /// 根据显示名获取命令名喵~
    /// </summary>
    public static string GetCommandNameFromDisplayName(string displayName)
    {
        EnsureInitialized();
        // 特殊处理"(无)"选项喵~
        if (displayName == "(无)") return "";
        
        foreach (var cmd in _commands.Values)
        {
            if (cmd.DisplayName == displayName)
                return cmd.CommandName;
        }
        return displayName;
    }

    /// <summary>
    /// 根据命令名获取显示名喵~
    /// 找不到返回 "(未知命令)" 而不是原样返回喵~
    /// </summary>
    public static string GetDisplayNameFromCommandName(string commandName)
    {
        EnsureInitialized();
        if (_commands.TryGetValue(commandName, out var info))
            return info.DisplayName;
        return "(未知命令)";
    }

    /// <summary>
    /// 获取命令详情喵~
    /// </summary>
    public static bool TryGetCommandInfo(string commandName, out CommandInfo info)
    {
        EnsureInitialized();
        return _commands.TryGetValue(commandName.ToLower(), out info);
    }

    /// <summary>
    /// 确保已初始化喵~
    /// </summary>
    private static void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            InitializeInternal();
        }
    }

    // =========================================================
    /// 编辑器初始化入口喵~
    // =========================================================
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void EditorInitialize()
    {
        InitializeInternal();
    }
#endif
}
