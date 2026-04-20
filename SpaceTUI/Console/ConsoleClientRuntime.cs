using UnityEngine;
using NekoGraph;
using System;
using System.Collections.Generic;

namespace SpaceTUI
{
    /// <summary>
    /// Console 前端客户端运行时。
    /// 负责把 Query / Presentation 包转化为本地 console 会话或其他 TUI 展示动作。
    /// 它不是全局单例，而是绑定到某个具体的 ConsoleManager 宿主。
    /// </summary>
    public sealed class ConsoleClientRuntime
    {
        private static readonly Dictionary<string, Func<object, IConsoleSession>> SessionFactories =
            new(StringComparer.Ordinal);
        private static readonly Dictionary<string, Action<ConsoleManager, VFSQueryResult>> Presenters =
            new(StringComparer.Ordinal);

        private readonly ConsoleManager _console;

        public ConsoleClientRuntime(ConsoleManager console)
        {
            _console = console;
        }

        public ConsoleManager Console => _console;
        public bool HasActiveSession => _console != null && _console.HasSession;

        /// <summary>
        /// 尝试呈现一个 Query 结果。
        /// 当前先只搭架子，后续由各 PresentationType 逐步接入。
        /// </summary>
        public bool TryPresent(VFSQueryResult result)
        {
            if (_console == null || result == null)
                return false;

            if (!string.IsNullOrWhiteSpace(result.PresentationType) &&
                SessionFactories.TryGetValue(result.PresentationType, out var factory))
            {
                var session = factory?.Invoke(result.Payload);
                if (session != null)
                {
                    OpenSession(session);
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(result.PresentationType) &&
                Presenters.TryGetValue(result.PresentationType, out var presenter))
            {
                presenter?.Invoke(_console, result);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 统一入口：打开一个 console session。
        /// </summary>
        public void OpenSession(IConsoleSession session, bool replaceExisting = true)
        {
            _console?.BeginSession(session, replaceExisting);
        }

        /// <summary>
        /// 统一入口：关闭当前 console session。
        /// </summary>
        public void CloseSession(IConsoleSession session = null)
        {
            _console?.EndSession(session);
        }

        /// <summary>
        /// 统一入口：重置当前 console 的交互态。
        /// </summary>
        public void ResetInteractiveState()
        {
            _console?.ClearInteractiveState();
        }

        public static void RegisterSessionFactory(string presentationType, Func<object, IConsoleSession> factory)
        {
            if (string.IsNullOrWhiteSpace(presentationType) || factory == null)
                return;

            SessionFactories[presentationType] = factory;
        }

        public static void RegisterPresenter(string presentationType, Action<ConsoleManager, VFSQueryResult> presenter)
        {
            if (string.IsNullOrWhiteSpace(presentationType) || presenter == null)
                return;

            Presenters[presentationType] = presenter;
        }
    }
}
