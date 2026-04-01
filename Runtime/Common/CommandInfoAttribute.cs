using System;
using UnityEngine;

/// <summary>
/// 命令信息特性 - 标记命令砖块方法，NekoGraph 全域扫描自动发现喵~
/// 宿主程序集中任意静态方法加此 Attribute 即可成为命令砖块。
///
/// 使用示例：
/// [CommandInfo("spawn", "🏗️ 召唤单位", "Entity", new[] { "BlueprintID", "Position", "Team" })]
/// public static CommandOutput Spawn(IConsoleController console, int subjectLevel, string[] args, object payload) { ... }
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class CommandInfoAttribute : Attribute
{
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string Category { get; set; }
    public string[] Parameters { get; set; }
    public string Tooltip { get; set; }
    public string Color { get; set; }

    public Color ParsedColor
    {
        get
        {
            if (string.IsNullOrEmpty(Color))
                return UnityEngine.Color.white;

            string[] parts = Color.Split(',');
            if (parts.Length >= 3 &&
                float.TryParse(parts[0], out float r) &&
                float.TryParse(parts[1], out float g) &&
                float.TryParse(parts[2], out float b))
            {
                float a = parts.Length >= 4 && float.TryParse(parts[3], out float alpha) ? alpha : 1f;
                return new Color(r, g, b, a);
            }
            return UnityEngine.Color.white;
        }
    }

    public CommandInfoAttribute(string name, string displayName, string category, string[] parameters = null)
    {
        Name = name;
        DisplayName = displayName;
        Category = category;
        Parameters = parameters ?? Array.Empty<string>();
        Tooltip = "";
        Color = "0.5,0.5,0.5";
    }
}
