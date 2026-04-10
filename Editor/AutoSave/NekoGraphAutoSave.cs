#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NekoGraph.Editor
{
    /// <summary>
    /// NekoGraph PackWindow 保存管理器
    /// ShaderGraph 风格：内存修改，手动/Ctrl+S 保存
    /// </summary>
    [InitializeOnLoad]
    public static class NekoGraphAutoSave
    {
        // 只有这些情况下才自动写磁盘
        private const bool SAVE_BEFORE_PLAY_MODE = true;
        private const bool SAVE_ON_BUILD = true;

        static NekoGraphAutoSave()
        {
            // Unity 原生保存事件（Ctrl+S）
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorApplication.wantsToQuit += OnWantsToQuit;

            // 监听保存快捷键 - 通过反射或定时检查实现
            EditorApplication.update += CheckSaveShortcut;
        }

        private static bool _wasCtrlSPressed;

        /// <summary>
        /// 检测 Ctrl+S 快捷键（Unity 没有直接的事件）
        /// </summary>
        private static void CheckSaveShortcut()
        {
            // 检测 Ctrl+S (Command+S on Mac)
            Event e = Event.current;
            if (e != null && e.type == EventType.KeyDown)
            {
                bool ctrl = e.control || e.command;
                if (ctrl && e.keyCode == KeyCode.S)
                {
                    if (!_wasCtrlSPressed)
                    {
                        _wasCtrlSPressed = true;
                        SaveAllWindows("Ctrl+S 快捷键");
                    }
                }
                else
                {
                    _wasCtrlSPressed = false;
                }
            }
        }

        /// <summary>
        /// 播放模式切换前保存
        /// </summary>
        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (SAVE_BEFORE_PLAY_MODE && state == PlayModeStateChange.ExitingEditMode)
            {
                var dirty = GetAllPackWindows().Where(w => w.IsDirty).ToList();
                if (dirty.Count > 0)
                {
                    // 提示用户
                    int result = EditorUtility.DisplayDialogComplex("未保存的 Pack",
                        $"有 {dirty.Count} 个 Pack 未保存，是否保存后再进入 Play Mode?",
                        "保存", "不保存", "取消");

                    switch (result)
                    {
                        case 0: // 保存
                            SaveAllWindows("进入 Play Mode");
                            break;
                        case 1: // 不保存
                            break;
                        default: // 取消 - 阻止进入 Play Mode
                            EditorApplication.isPlaying = false;
                            return;
                    }
                }
            }
        }

        /// <summary>
        /// 场景保存时连带保存 Pack（如果用户在场景上按 Ctrl+S）
        /// </summary>
        private static void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path)
        {
            // 只保存有有效路径的 Pack（已保存过的）
            var windows = GetAllPackWindows()
                .Where(w => w.IsDirty && w.HasValidAssetPath)
                .ToList();

            foreach (var window in windows)
            {
                window.SilentSave();
            }

            if (windows.Count > 0)
            {
                Debug.Log($"[NekoGraph] 随场景保存了 {windows.Count} 个 Pack");
            }
        }

        /// <summary>
        /// 退出 Unity 前提示
        /// </summary>
        private static bool OnWantsToQuit()
        {
            var dirtyWindows = GetAllPackWindows().Where(w => w.IsDirty).ToList();
            if (dirtyWindows.Count > 0)
            {
                string message = $"有 {dirtyWindows.Count} 个 Pack 编辑器窗口未保存:\n" +
                                 string.Join("\n", dirtyWindows.Select(w => $"- {w.Title}")) +
                                 "\n\n是否保存后再退出?";

                int result = EditorUtility.DisplayDialogComplex("未保存的更改",
                    message, "保存并退出", "不保存退出", "取消");

                switch (result)
                {
                    case 0: // 保存并退出
                        foreach (var window in dirtyWindows)
                        {
                            window.SilentSave();
                        }
                        return true;
                    case 1: // 不保存退出
                        return true;
                    default: // 取消
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 保存所有打开的 PackWindow
        /// </summary>
        public static void SaveAllWindows(string reason)
        {
            var windows = GetAllPackWindows().Where(w => w.IsDirty).ToList();
            if (windows.Count == 0) return;

            foreach (var window in windows)
            {
                window.SilentSave();
            }

            Debug.Log($"[NekoGraph] 因 [{reason}] 保存了 {windows.Count} 个 Pack");
        }

        /// <summary>
        /// 获取所有打开的 PackWindow
        /// </summary>
        private static IEnumerable<IPackWindowSaveable> GetAllPackWindows()
        {
            var windows = Resources.FindObjectsOfTypeAll<PackWindow>();
            return windows.Cast<IPackWindowSaveable>();
        }

        /// <summary>
        /// 手动保存所有（菜单项）
        /// </summary>
        [MenuItem("NekoGraph/💾 保存所有 Pack 窗口 %#s")] // Ctrl+Shift+S
        private static void ManualSaveAll()
        {
            SaveAllWindows("菜单项");
        }

        /// <summary>
        /// 检查未保存的 Pack
        /// </summary>
        [MenuItem("NekoGraph/🔍 检查未保存的 Pack")]
        private static void CheckDirtyWindows()
        {
            var dirtyWindows = GetAllPackWindows().Where(w => w.IsDirty).ToList();
            if (dirtyWindows.Count == 0)
            {
                EditorUtility.DisplayDialog("检查结果", "所有 Pack 窗口都已保存", "确定");
            }
            else
            {
                string message = $"发现 {dirtyWindows.Count} 个未保存的窗口:\n" +
                                 string.Join("\n", dirtyWindows.Select(w => $"- {w.Title}"));
                EditorUtility.DisplayDialog("检查结果", message, "确定");
            }
        }
    }

    /// <summary>
    /// PackWindow 保存接口
    /// </summary>
    public interface IPackWindowSaveable
    {
        bool IsDirty { get; }
        bool HasValidAssetPath { get; }
        string AssetPath { get; }
        string Title { get; }
        void SilentSave();
    }
}
#endif
