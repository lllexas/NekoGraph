#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NekoGraph.Editor
{
    /// <summary>
    /// 全局保存支持 - 拦截 Unity 的 Save 操作
    /// 当用户按 Ctrl+S 保存时，连带保存所有 dirty 的 Pack
    /// </summary>
    public class NekoGraphSaveShortcut : UnityEditor.AssetModificationProcessor
    {
        /// <summary>
        /// Unity 保存任何资产前调用（包括 Ctrl+S 保存场景）
        /// </summary>
        public static string[] OnWillSaveAssets(string[] paths)
        {
            // 保存所有 dirty 的 Pack 窗口
            var windows = Resources.FindObjectsOfTypeAll<PackWindow>()
                .Where(w => w.IsDirty)
                .ToList();

            if (windows.Count > 0)
            {
                foreach (var window in windows)
                {
                    window.SilentSave();
                }
                Debug.Log($"[NekoGraph] Ctrl+S 连带保存了 {windows.Count} 个 Pack");
            }

            return paths; // 原样返回，不拦截保存
        }
    }
}
#endif
