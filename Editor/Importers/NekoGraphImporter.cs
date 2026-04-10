#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace NekoGraph.Editor
{
    /// <summary>
    /// NekoGraph Pack 文件的自定义导入器
    /// 将 .nekograph 文件导入为 Unity 可识别的资源类型
    /// </summary>
    [ScriptedImporter(1, "nekograph")]
    public class NekoGraphImporter : ScriptedImporter
    {
        [Tooltip("Pack 数据预览")]
        public string PreviewJson;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            // 读取文件内容
            string jsonContent = File.ReadAllText(ctx.assetPath);
            PreviewJson = jsonContent.Length > 1000
                ? jsonContent.Substring(0, 1000) + "\n... (truncated)"
                : jsonContent;

            // 创建 TextAsset 作为导入对象（方便查看）
            var textAsset = new TextAsset(jsonContent);
            textAsset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);

            // 添加到资源上下文
            ctx.AddObjectToAsset("main", textAsset);
            ctx.SetMainObject(textAsset);

            // 验证 JSON 有效性（可选）
            ValidatePackData(ctx.assetPath, jsonContent);
        }

        private void ValidatePackData(string path, string json)
        {
            try
            {
                var pack = BasePackData.FromJson(json);
                if (pack == null)
                {
                    Debug.LogWarning($"[NekoGraphImporter] {path} 解析为 Pack 失败");
                }
                else
                {
                    Debug.Log($"[NekoGraphImporter] 已导入: {pack.PackID} ({pack.Nodes.Count} 个节点)");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[NekoGraphImporter] {path} JSON 解析错误: {e.Message}");
            }
        }
    }
}
#endif
