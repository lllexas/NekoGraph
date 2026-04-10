#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.Callbacks;
using UnityEngine;

namespace NekoGraph.Editor
{
    /// <summary>
    /// NekoGraph 资源的自定义 Inspector 和双击处理
    /// </summary>
    [CustomEditor(typeof(NekoGraphImporter))]
    public class NekoGraphAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var importer = target as NekoGraphImporter;

            EditorGUILayout.Space(10);

            // 标题
            EditorGUILayout.LabelField("NekoGraph Pack", EditorStyles.largeLabel);
            EditorGUILayout.Space(5);

            // 打开编辑器按钮
            if (GUILayout.Button("在 Pack Editor 中打开", GUILayout.Height(30)))
            {
                OpenInEditor();
            }

            EditorGUILayout.Space(10);

            // 显示预览内容
            if (!string.IsNullOrEmpty(importer.PreviewJson))
            {
                EditorGUILayout.LabelField("内容预览:", EditorStyles.boldLabel);
                EditorGUILayout.TextArea(importer.PreviewJson, GUILayout.Height(200));
            }

            // 应用按钮
            EditorGUILayout.Space(10);
            if (GUILayout.Button("Apply"))
            {
                EditorUtility.SetDirty(target);
                AssetDatabase.WriteImportSettingsIfDirty(AssetDatabase.GetAssetPath(target));
                AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(target));
            }
        }

        /// <summary>
        /// 双击 .nekograph 文件时打开 PackWindow
        /// </summary>
        [OnOpenAsset(1)]
        public static bool OnOpenAsset(int instanceID, int line)
        {
            var asset = EditorUtility.InstanceIDToObject(instanceID) as TextAsset;
            if (asset == null) return false;

            string path = AssetDatabase.GetAssetPath(instanceID);
            if (string.IsNullOrEmpty(path)) return false;

            // 检查扩展名
            if (!path.EndsWith(".nekograph", System.StringComparison.OrdinalIgnoreCase))
                return false;

            // 打开 PackWindow
            PackWindow.OpenWithAsset(path);
            return true;
        }

        private void OpenInEditor()
        {
            string assetPath = AssetDatabase.GetAssetPath(target);
            PackWindow.OpenWithAsset(assetPath);
        }
    }
}
#endif
