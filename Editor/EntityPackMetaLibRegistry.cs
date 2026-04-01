#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

internal static class EntityPackMetaLibRegistry
{
    private const string MenuPath = "NekoGraph/MetaLib/Rebuild EntityPack Entries";
    private const string EntityPackAssetDir = "Assets/Resources/EntityPack";
    private const string EntityPackResourcePrefix = "EntityPack/";

    [MenuItem(MenuPath)]
    public static void RebuildEntityPackEntries()
    {
        MetaLib.Reload();

        var preserved = MetaLib.GetAllMetas()
            .Where(entry => entry != null && !IsEntityPackEntry(entry))
            .ToDictionary(entry => entry.EffectiveID, entry => entry, StringComparer.Ordinal);

        var entityEntries = BuildEntityPackEntries();

        MetaLib.Clear();

        foreach (var pair in preserved)
        {
            MetaLib.Register(pair.Key, pair.Value);
        }

        foreach (var pair in entityEntries)
        {
            MetaLib.Register(pair.Key, pair.Value);
        }

        MetaLib.Save();
        MetaLib.Reload();

        Debug.Log($"[MetaLib] Rebuilt EntityPack entries: {entityEntries.Count}");
    }

    private static Dictionary<string, MetaLib.MetaEntry> BuildEntityPackEntries()
    {
        var results = new Dictionary<string, MetaLib.MetaEntry>(StringComparer.Ordinal);
        if (!Directory.Exists(EntityPackAssetDir))
        {
            return results;
        }

        foreach (var file in Directory.GetFiles(EntityPackAssetDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            var pack = TryLoadPack(file);
            if (pack == null || string.IsNullOrWhiteSpace(pack.PackID))
            {
                continue;
            }

            var resourcePath = $"{EntityPackResourcePrefix}{Path.GetFileNameWithoutExtension(file)}";
            results[pack.PackID] = new MetaLib.MetaEntry
            {
                ID = pack.PackID,
                PackID = pack.PackID,
                Kind = MetaLib.EntryKind.Pack,
                Storage = MetaLib.StorageType.Resources,
                ResourcePath = resourcePath,
                ObjectType = "BasePackData",
                GraphType = pack.GetType().Name.Replace("PackData", string.Empty),
                DisplayName = string.IsNullOrWhiteSpace(pack.DisplayName) ? pack.PackID : pack.DisplayName,
                Author = string.IsNullOrWhiteSpace(pack.Author) ? "NekoTeam" : pack.Author,
                Version = string.IsNullOrWhiteSpace(pack.Version) ? "1.0.0" : pack.Version,
                Description = pack.Description,
                CustomFields = new Dictionary<string, string>
                {
                    ["AssetPath"] = file.Replace('\\', '/')
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
        catch (Exception ex)
        {
            Debug.LogWarning($"[MetaLib] Failed to read EntityPack '{filePath}': {ex.Message}");
            return null;
        }
    }

    private static bool IsEntityPackEntry(MetaLib.MetaEntry entry)
    {
        return entry.Kind == MetaLib.EntryKind.Pack &&
               !string.IsNullOrWhiteSpace(entry.ResourcePath) &&
               entry.ResourcePath.StartsWith(EntityPackResourcePrefix, StringComparison.Ordinal);
    }
}
#endif
