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
    /// 统一的 MetaLib 重建：一次扫描同时登记 EntityPack (JSON) 和 BBBNexus SO 两类条目。
    /// 替代原先分离的 EntityPackMetaLibRegistry 和 MetaLibSoRegistry。
    /// </summary>
    internal static class MetaLibFullRegistry
    {
        private const string MenuPath = "NekoGraph/MetaLib/Rebuild All Entries";

        private const string AssetsRootDir = "Assets";
        private const string ResourcesMarker = "/Resources/";
        private const string StreamingAssetsMarker = "/StreamingAssets/";

        [MenuItem(MenuPath)]
        public static void RebuildAllEntries()
        {
            MetaLib.Reload();

            // 保留既不是 EntityPack 也不是 SO 的条目（如果未来有第三种来源）
            var preserved = MetaLib.GetAllMetas()
                .Where(e => e != null && !IsPackEntry(e) && e.Kind != MetaLib.EntryKind.ResourceObject)
                .ToDictionary(e => e.EffectiveID, e => e, StringComparer.Ordinal);

            var entityEntries = BuildPackEntries();
            var soEntries = BuildBbbNexusSoEntries();

            // 冲突检测
            foreach (var key in soEntries.Keys)
            {
                if (entityEntries.ContainsKey(key))
                {
                    Debug.LogError($"[MetaLib] ID conflict between EntityPack and SO: '{key}'. SO entry will be skipped.");
                    soEntries.Remove(key);
                }
            }

            MetaLib.Clear();

            foreach (var pair in preserved)
                MetaLib.Register(pair.Key, pair.Value);
            foreach (var pair in entityEntries)
                MetaLib.Register(pair.Key, pair.Value);
            foreach (var pair in soEntries)
                MetaLib.Register(pair.Key, pair.Value);

            MetaLib.Save();
            MetaLib.Reload();

            Debug.Log($"[MetaLib] Rebuilt all entries — EntityPack: {entityEntries.Count}, SO: {soEntries.Count}, Preserved: {preserved.Count}");
        }

        #region EntityPack (JSON)

        private static Dictionary<string, MetaLib.MetaEntry> BuildPackEntries()
        {
            var results = new Dictionary<string, MetaLib.MetaEntry>(StringComparer.Ordinal);
            if (!Directory.Exists(AssetsRootDir))
                return results;

            // 递归扫描 Assets/ 下所有 JSON，只要路径含 /Resources/ 或 /StreamingAssets/ 就尝试登记
            foreach (var file in Directory.GetFiles(AssetsRootDir, "*.json", SearchOption.AllDirectories))
            {
                string normalized = file.Replace('\\', '/');
                if (!TryClassifyPath(normalized, out var storage, out var relativePath))
                    continue;

                var pack = TryLoadPack(file);
                if (pack == null || string.IsNullOrWhiteSpace(pack.PackID))
                    continue;

                results[pack.PackID] = new MetaLib.MetaEntry
                {
                    ID = pack.PackID,
                    PackID = pack.PackID,
                    Kind = MetaLib.EntryKind.Pack,
                    Storage = storage,
                    ResourcePath = relativePath,
                    ObjectType = "BasePackData",
                    GraphType = pack.GetType().Name.Replace("PackData", string.Empty),
                    DisplayName = string.IsNullOrWhiteSpace(pack.DisplayName) ? pack.PackID : pack.DisplayName,
                    Author = string.IsNullOrWhiteSpace(pack.Author) ? "NekoTeam" : pack.Author,
                    Version = string.IsNullOrWhiteSpace(pack.Version) ? "1.0.0" : pack.Version,
                    Description = pack.Description,
                    CustomFields = new Dictionary<string, string>
                    {
                        ["AssetPath"] = normalized
                    }
                };
            }

            return results;
        }

        private static BasePackData TryLoadPack(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                return BasePackData.FromJson(json);
            }
            catch
            {
                // 非 Pack JSON（如 MetaLib.json 自身）静默跳过
                return null;
            }
        }

        /// <summary>
        /// 判断路径属于 Resources 还是 StreamingAssets，返回对应的 StorageType 和去掉扩展名的相对路径。
        /// 不属于任一则返回 false。
        /// </summary>
        private static bool TryClassifyPath(string normalizedPath, out MetaLib.StorageType storage, out string relativePath)
        {
            int idx = normalizedPath.IndexOf(ResourcesMarker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                storage = MetaLib.StorageType.Resources;
                relativePath = StripExtension(normalizedPath[(idx + ResourcesMarker.Length)..]);
                return true;
            }

            idx = normalizedPath.IndexOf(StreamingAssetsMarker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                storage = MetaLib.StorageType.StreamingAssets;
                relativePath = StripExtension(normalizedPath[(idx + StreamingAssetsMarker.Length)..]);
                return true;
            }

            storage = default;
            relativePath = null;
            return false;
        }

        private static string StripExtension(string path)
        {
            int dot = path.LastIndexOf('.');
            return dot >= 0 ? path[..dot] : path;
        }

        private static bool IsPackEntry(MetaLib.MetaEntry entry)
        {
            return entry.Kind == MetaLib.EntryKind.Pack;
        }

        #endregion

        #region BBBNexus SO

        private static Dictionary<string, MetaLib.MetaEntry> BuildBbbNexusSoEntries()
        {
            var results = new Dictionary<string, MetaLib.MetaEntry>(StringComparer.Ordinal);
            var guids = AssetDatabase.FindAssets("t:ScriptableObject");

            foreach (var guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!assetPath.Contains("/Resources/", StringComparison.OrdinalIgnoreCase) &&
                    !assetPath.Contains("\\Resources\\", StringComparison.OrdinalIgnoreCase))
                    continue;

                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
                if (so == null)
                    continue;

                var type = so.GetType();
                if (type.Namespace == null || !type.Namespace.StartsWith("BBBNexus", StringComparison.Ordinal))
                    continue;

                string id = so.name;
                if (results.ContainsKey(id))
                {
                    Debug.LogError(
                        $"[MetaLib] Duplicate BBBNexus SO ID '{id}'. Conflicting: '{results[id].ResourcePath}' vs '{ToResourcesPath(assetPath)}'.");
                    continue;
                }

                results[id] = new MetaLib.MetaEntry
                {
                    ID = id,
                    PackID = id,
                    Kind = MetaLib.EntryKind.ResourceObject,
                    Storage = MetaLib.StorageType.Resources,
                    ResourcePath = ToResourcesPath(assetPath),
                    ObjectType = type.FullName,
                    DisplayName = so.name,
                    Author = "NekoTeam",
                    Version = "1.0.0",
                    Description = string.Empty,
                    CustomFields = new Dictionary<string, string>
                    {
                        ["AssetPath"] = assetPath,
                    },
                };
            }

            return results;
        }

        private static string ToResourcesPath(string assetPath)
        {
            string normalized = assetPath.Replace('\\', '/');
            const string marker = "/Resources/";
            int idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                throw new InvalidOperationException($"Asset is not under a Resources folder: {assetPath}");

            string relative = normalized[(idx + marker.Length)..];
            if (relative.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                relative = relative[..^".asset".Length];

            return relative;
        }

        #endregion
    }
}
#endif
