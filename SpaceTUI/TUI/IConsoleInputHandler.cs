using UnityEngine;

namespace SpaceTUI
{
    /// <summary>
    /// 控制台导航键类型。
    /// </summary>
    public enum ConsoleNavKey
    {
        Up,
        Down,
        Left,
        Right,
        Home,
        End
    }

    /// <summary>
    /// 按键信息结构体 - 打包原始按键状态喵~
    /// </summary>
    public struct KeyInfo
    {
        public KeyCode keyCode;
        public bool isShiftDown;
        public bool isCtrlDown;
        public bool isAltDown;
    }

    /// <summary>
    /// 控制台会话的输入侧协议。
    /// 一个 session 可以接管 console 的按键、提交、导航、确认和取消。
    /// </summary>
    public interface IConsoleSessionInput
    {
        bool HandleKey(KeyInfo key);
        bool HandleSubmit(string input);
        bool HandleNavigation(ConsoleNavKey key);
        bool HandleConfirm();
        bool HandleCancel();
    }

    /// <summary>
    /// 控制台会话的显示侧协议。
    /// session 可声明是否接管底部输入行，以及使用什么提示文本。
    /// </summary>
    public interface IConsoleSessionPresentation
    {
        bool ShouldRenderInputLine { get; }
        string GetInputPrompt(string fallbackPrompt);
    }

    /// <summary>
    /// 控制台会话协议。
    /// 这是 console 交互态的正式抽象，取代“input handler / slot”这种早期命名。
    /// </summary>
    public interface IConsoleSession : IConsoleSessionInput, IConsoleSessionPresentation
    {
        string SessionId { get; }
        string SessionName { get; }
        void OnSessionEnter(ConsoleManager console);
        void OnSessionExit(ConsoleManager console);
    }

    /// <summary>
    /// 控制台会话基类。
    /// 默认保持输入行可见，并沿用宿主 console 的默认 prompt。
    /// </summary>
    public abstract class ConsoleSessionBase : IConsoleSession, IConsoleInputHandler, IConsoleInputLineState
    {
        public virtual string SessionId => GetType().Name;

        public virtual string SessionName => GetType().Name;

        public virtual bool ShouldRenderInputLine => true;

        public virtual string GetInputPrompt(string fallbackPrompt) => fallbackPrompt;

        public virtual void OnSessionEnter(ConsoleManager console)
        {
        }

        public virtual void OnSessionExit(ConsoleManager console)
        {
        }

        public abstract bool HandleKey(KeyInfo key);
        public abstract bool HandleSubmit(string input);
        public abstract bool HandleNavigation(ConsoleNavKey key);
        public abstract bool HandleConfirm();
        public abstract bool HandleCancel();
    }

    /// <summary>
    /// 旧名兼容层：控制台输入处理器。
    /// 新代码请直接使用 IConsoleSession / IConsoleSessionInput。
    /// </summary>
    [System.Obsolete("IConsoleInputHandler 已重命名为 IConsoleSessionInput / IConsoleSession。新代码请改用 session 语义。", false)]
    public interface IConsoleInputHandler : IConsoleSessionInput
    {
    }

    /// <summary>
    /// 旧名兼容层：输入行显示策略。
    /// 新代码请直接使用 IConsoleSessionPresentation / IConsoleSession。
    /// </summary>
    [System.Obsolete("IConsoleInputLineState 已重命名为 IConsoleSessionPresentation / IConsoleSession。新代码请改用 session 语义。", false)]
    public interface IConsoleInputLineState : IConsoleSessionPresentation
    {
    }
}
