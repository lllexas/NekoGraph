using UnityEngine;
using NekoGraph;
using System;
using System.Linq;
using System.Reflection;

namespace SpaceTUI
{
    /// <summary>
    /// Console 前端客户端运行时。
    /// 负责把 Query / Presentation 包转化为本地 console 会话或其他 TUI 展示动作。
    /// 它不是全局单例，而是绑定到某个具体的 ConsoleManager 宿主。
    /// </summary>
    public sealed class ConsoleClientRuntime
    {
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

            switch (result.PresentationType)
            {
                case "social.msg":
                    return TryOpenReflectedSession(
                        result.Payload,
                        "VFSMsgQueryPayload",
                        "VFSMsgSession",
                        "Message");

                default:
                    Debug.Log($"[ConsoleClientRuntime] 未处理的 QueryResult: type={result.PresentationType ?? "(null)"} title={result.Title ?? "(null)"}");
                    return false;
            }
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

        private bool TryOpenReflectedSession(
            object payload,
            string expectedPayloadTypeName,
            string sessionTypeName,
            string requiredPayloadProperty = null)
        {
            if (payload == null)
                return false;

            Type payloadType = payload.GetType();
            if (!string.Equals(payloadType.Name, expectedPayloadTypeName, StringComparison.Ordinal))
                return false;

            if (!string.IsNullOrEmpty(requiredPayloadProperty))
            {
                PropertyInfo property = payloadType.GetProperty(requiredPayloadProperty, BindingFlags.Public | BindingFlags.Instance);
                if (property == null || property.GetValue(payload) == null)
                    return false;
            }

            Type sessionType = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType(sessionTypeName, throwOnError: false))
                .FirstOrDefault(type => type != null);

            if (sessionType == null)
            {
                Debug.LogWarning($"[ConsoleClientRuntime] 未找到 Session 类型：{sessionTypeName}");
                return false;
            }

            if (Activator.CreateInstance(sessionType, payload) is not IConsoleSession session)
            {
                Debug.LogWarning($"[ConsoleClientRuntime] 无法创建 Session：{sessionTypeName}");
                return false;
            }

            OpenSession(session);
            return true;
        }
    }
}
