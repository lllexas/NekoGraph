#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NekoGraph.Editor
{
    internal static class MetaLibSoRegistry
    {
        private const string MenuPath = "NekoGraph/MetaLib/Rebuild BBBNexus SO Entries";

        [MenuItem(MenuPath)]
        private static void RebuildBbbNexusSoEntries()
        {
            MetaLib.Reload();

            var allMetas = MetaLib.GetAllMetas().ToList();
            var preserved = new Dictionary<string, MetaLib.MetaEntry>(StringComparer.Ordinal);

            foreach (var entry in allMetas)
            {
                if (entry == null)
                    continue;

                if (entry.Kind != MetaLib.EntryKind.ResourceObject)
                {
                    preserved[entry.EffectiveID] = entry;
                }
            }

            var soEntries = BuildBbbNexusSoEntries();

            var merged = new Dictionary<string, MetaLib.MetaEntry>(StringComparer.Ordinal);
            foreach (var pair in preserved)
            {
                merged[pair.Key] = pair.Value;
            }

            foreach (var pair in soEntries)
            {
                if (merged.ContainsKey(pair.Key))
                {
                    throw new InvalidOperationException($"MetaLib ID conflict: '{pair.Key}' is already registered by a non-SO entry.");
                }

                merged[pair.Key] = pair.Value;
            }

            MetaLib.Clear();
            foreach (var pair in merged)
            {
                MetaLib.Register(pair.Key, pair.Value);
            }

            MetaLib.Save();
            Debug.Log($"[MetaLib] Rebuilt BBBNexus SO entries: {soEntries.Count}");
        }

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
                    throw new InvalidOperationException(
                        $"Duplicate BBBNexus SO ID '{id}'. Conflicting assets: '{results[id].ResourcePath}' and '{ToResourcesPath(assetPath)}'.");
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
            {
                relative = relative[..^".asset".Length];
            }

            return relative;
        }
    }
}
#endif
