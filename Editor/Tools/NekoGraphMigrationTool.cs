#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NekoGraph.Editor
{
    /// <summary>
    /// NekoGraph Pack 文件批量转换工具
    /// 将 .json 文件转换为 .nekograph 扩展名
    /// </summary>
    public class NekoGraphMigrationWindow : EditorWindow
    {
        private string _sourceFolder = "Assets/Resources";
        private bool _recursive = true;
        private bool _dryRun = true; // 默认试运行，不实际执行
        private Vector2 _scrollPos;
        private List<MigrationItem> _previewItems = new();
        private bool _showPreview;

        private class MigrationItem
        {
            public string SourcePath;
            public string TargetPath;
            public bool IsValidNekoGraph;
            public string Status;
            public bool WillMigrate;
        }

        [MenuItem("Tools/NekoGraph/批量转换扩展名 (.json → .nekograph)")]
        public static void ShowWindow()
        {
            var window = GetWindow<NekoGraphMigrationWindow>("NekoGraph 迁移工具");
            window.minSize = new Vector2(500, 400);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("NekoGraph Pack 扩展名迁移工具", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("将符合 NekoGraph 格式的 .json 文件转换为 .nekograph", EditorStyles.miniLabel);
            EditorGUILayout.Space(10);

            // 源文件夹选择
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("源文件夹:", GUILayout.Width(80));
            _sourceFolder = EditorGUILayout.TextField(_sourceFolder);
            if (GUILayout.Button("浏览", GUILayout.Width(60)))
            {
                string selected = EditorUtility.OpenFolderPanel("选择源文件夹", _sourceFolder, "");
                if (!string.IsNullOrEmpty(selected))
                {
                    // 转换为相对路径
                    if (selected.StartsWith(Application.dataPath))
                    {
                        _sourceFolder = "Assets" + selected.Substring(Application.dataPath.Length);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            // 选项
            _recursive = EditorGUILayout.Toggle("递归子文件夹", _recursive);

            EditorGUILayout.Space(10);

            // 操作按钮
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.7f, 0.9f, 1f);
            if (GUILayout.Button("预览", GUILayout.Height(30)))
            {
                GeneratePreview();
            }
            GUI.backgroundColor = Color.white;

            GUI.backgroundColor = _dryRun ? Color.yellow : new Color(0.3f, 1f, 0.3f);
            if (GUILayout.Button(_dryRun ? "试运行模式 (点击切换)" : "执行迁移", GUILayout.Height(30)))
            {
                if (_dryRun)
                {
                    _dryRun = false;
                }
                else
                {
                    ExecuteMigration();
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // 预览列表
            if (_showPreview && _previewItems.Count > 0)
            {
                EditorGUILayout.LabelField($"预览 ({_previewItems.Count(i => i.WillMigrate)} 个文件将被迁移):", EditorStyles.boldLabel);

                _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(250));

                foreach (var item in _previewItems)
                {
                    EditorGUILayout.BeginHorizontal();

                    // 是否有效图标
                    if (item.IsValidNekoGraph)
                    {
                        GUI.color = Color.green;
                        EditorGUILayout.LabelField("✓", GUILayout.Width(20));
                    }
                    else
                    {
                        GUI.color = Color.gray;
                        EditorGUILayout.LabelField("○", GUILayout.Width(20));
                    }
                    GUI.color = Color.white;

                    // 路径
                    EditorGUILayout.LabelField(Path.GetFileName(item.SourcePath), GUILayout.Width(200));

                    // 状态
                    EditorGUILayout.LabelField(item.Status, EditorStyles.miniLabel);

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();
            }
            else if (_showPreview)
            {
                EditorGUILayout.HelpBox("未找到符合条件的 .json 文件", MessageType.Info);
            }

            EditorGUILayout.Space(10);

            // 说明
            EditorGUILayout.LabelField("说明:", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "1. 预览: 扫描文件夹并显示哪些文件会被转换\n" +
                "2. 试运行: 默认开启，不会实际执行任何操作\n" +
                "3. 执行迁移: 关闭试运行后，点击执行迁移\n" +
                "4. 只会转换符合 NekoGraph Pack 格式的 JSON 文件",
                MessageType.Info);
        }

        private void GeneratePreview()
        {
            _previewItems.Clear();
            _showPreview = true;

            if (!Directory.Exists(_sourceFolder))
            {
                EditorUtility.DisplayDialog("错误", $"文件夹不存在: {_sourceFolder}", "确定");
                return;
            }

            SearchOption searchOption = _recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string[] jsonFiles = Directory.GetFiles(_sourceFolder, "*.json", searchOption);

            foreach (string jsonPath in jsonFiles)
            {
                var item = new MigrationItem
                {
                    SourcePath = jsonPath.Replace('\\', '/'),
                    TargetPath = Path.ChangeExtension(jsonPath, ".nekograph").Replace('\\', '/'),
                    IsValidNekoGraph = IsNekoGraphPack(jsonPath),
                    Status = "检查中...",
                    WillMigrate = false
                };

                if (item.IsValidNekoGraph)
                {
                    item.Status = "将迁移为 .nekograph";
                    item.WillMigrate = true;
                }
                else
                {
                    item.Status = "非 NekoGraph Pack，跳过";
                    item.WillMigrate = false;
                }

                _previewItems.Add(item);
            }

            Debug.Log($"[NekoGraphMigration] 预览完成: 找到 {jsonFiles.Length} 个 JSON 文件，" +
                      $"其中 {_previewItems.Count(i => i.WillMigrate)} 个是 NekoGraph Pack");
        }

        private bool IsNekoGraphPack(string path)
        {
            try
            {
                // 快速读取前 1000 字符检查特征
                using var reader = new StreamReader(path);
                char[] buffer = new char[1000];
                int read = reader.Read(buffer, 0, 1000);
                string header = new string(buffer, 0, read);

                // NekoGraph Pack 的特征
                return header.Contains("\"PackID\"") &&
                       header.Contains("\"Nodes\"") &&
                       header.Contains("\"NodeID\"");
            }
            catch
            {
                return false;
            }
        }

        private void ExecuteMigration()
        {
            if (_previewItems.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先点击预览", "确定");
                return;
            }

            var itemsToMigrate = _previewItems.Where(i => i.WillMigrate).ToList();
            if (itemsToMigrate.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "没有需要迁移的文件", "确定");
                return;
            }

            // 确认对话框
            if (!EditorUtility.DisplayDialog("确认迁移",
                    $"确定要将 {itemsToMigrate.Count} 个文件从 .json 重命名为 .nekograph 吗？\n\n" +
                    "此操作会: \n" +
                    "1. 重命名文件\n" +
                    "2. 更新相关引用\n" +
                    "3. 刷新 AssetDatabase\n\n" +
                    "建议先备份项目！",
                    "确认迁移", "取消"))
            {
                return;
            }

            int successCount = 0;
            int failCount = 0;

            AssetDatabase.StartAssetEditing();

            try
            {
                for (int i = 0; i < itemsToMigrate.Count; i++)
                {
                    var item = itemsToMigrate[i];
                    string relativePath = item.SourcePath;

                    // 转换为相对路径（如果是绝对路径）
                    if (Path.IsPathRooted(relativePath))
                    {
                        relativePath = "Assets" + relativePath.Substring(Application.dataPath.Length);
                    }

                    // 移动/重命名（改变扩展名需要用 MoveAsset）
                    string newRelativePath = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') + "/" + Path.GetFileName(item.TargetPath);
                    if (newRelativePath.StartsWith("/")) newRelativePath = newRelativePath.Substring(1);

                    string error = AssetDatabase.MoveAsset(relativePath, newRelativePath);

                    if (string.IsNullOrEmpty(error))
                    {
                        successCount++;
                        Debug.Log($"[NekoGraphMigration] 成功: {relativePath} -> {Path.GetFileName(item.TargetPath)}");
                    }
                    else
                    {
                        failCount++;
                        Debug.LogError($"[NekoGraphMigration] 失败: {relativePath} - {error}");
                    }

                    // 进度条
                    if (i % 10 == 0)
                    {
                        EditorUtility.DisplayProgressBar("迁移中...", $"处理 {i + 1}/{itemsToMigrate.Count}", (float)i / itemsToMigrate.Count);
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }

            EditorUtility.DisplayDialog("迁移完成",
                $"成功: {successCount}\n失败: {failCount}",
                "确定");

            Debug.Log($"[NekoGraphMigration] 迁移完成: 成功 {successCount}, 失败 {failCount}");

            // 重置
            _dryRun = true;
            _previewItems.Clear();
            _showPreview = false;
        }
    }
}
#endif
