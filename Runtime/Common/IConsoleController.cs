using UnityEngine;

/// <summary>
/// 控制台控制器接口 - NekoGraph 命令系统与宿主控制台的唯一契约喵~
/// 命令砖块通过此接口输出日志，不依赖具体的 DeveloperConsole 实现。
/// </summary>
public interface IConsoleController
{
    void Log(string message, Color color);
}
