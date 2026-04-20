using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Global object registry keyed by unique IDs.
/// Supports loading C# objects from Resources or StreamingAssets.
/// </summary>
public static class MetaLib
{
    public enum StorageType
    {
        Resources,
        StreamingAssets,
    }

    public enum EntryKind
    {
        Unknown = 0,
        Pack = 1,
        ResourceObject = 2,
        Text = 3,
    }

    private static Dictionary<string, MetaEntry> _metadata;
    private static bool _isInitialized;
    private const string METALIB_PATH = "NekoGraph/MetaLib";

    public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        TypeNameHandling = TypeNameHandling.Objects,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.Indented,
        SerializationBinder = NekoGraphSerializationBinder.Instance,
    };

    [Serializable]
    public class MetaEntry
    {
        public string ID;

        // Legacy field kept for backwards compatibility with existing MetaLib.json and old callers.
        public string PackID;

        public EntryKind Kind = EntryKind.Unknown;
        public StorageType Storage;
        public string ResourcePath;
        public string ObjectType;
        public string GraphType;
        public string DisplayName;
        public string Author;
        public string Version = "1.0.0";
        public string Description;
        public Dictionary<string, string> CustomFields = new Dictionary<string, string>();

        public string EffectiveID
        {
            get => !string.IsNullOrEmpty(ID) ? ID : PackID;
            set
            {
                ID = value;
                PackID = value;
            }
        }
    }

    [RuntimeInitializeOnLoadMethod]
    private static void Initialize()
    {
        if (_isInitialized) return;
        LoadFromResources();
        _isInitialized = true;
    }

    private static void LoadFromResources()
    {
        var jsonAsset = Resources.Load<TextAsset>(METALIB_PATH);
        if (jsonAsset == null)
        {
            _metadata = new Dictionary<string, MetaEntry>(StringComparer.Ordinal);
            return;
        }

        try
        {
            _metadata = JsonConvert.DeserializeObject<Dictionary<string, MetaEntry>>(jsonAsset.text)
                        ?? new Dictionary<string, MetaEntry>(StringComparer.Ordinal);

            NormalizeEntries();
        }
        catch (Exception e)
        {
            Debug.LogError($"[MetaLib] Failed to deserialize metadata: {e.Message}");
            _metadata = new Dictionary<string, MetaEntry>(StringComparer.Ordinal);
        }
    }

    private static void NormalizeEntries()
    {
        if (_metadata == null)
        {
            _metadata = new Dictionary<string, MetaEntry>(StringComparer.Ordinal);
            return;
        }

        var normalized = new Dictionary<string, MetaEntry>(StringComparer.Ordinal);
        foreach (var kvp in _metadata)
        {
            var entry = kvp.Value ?? new MetaEntry();
            var id = !string.IsNullOrEmpty(entry.EffectiveID) ? entry.EffectiveID : kvp.Key;
            entry.EffectiveID = id;

            if (entry.Kind == EntryKind.Unknown)
            {
                entry.Kind = InferEntryKind(entry);
            }

            normalized[id] = entry;
        }

        _metadata = normalized;
    }

    private static EntryKind InferEntryKind(MetaEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.GraphType))
        {
            return EntryKind.Pack;
        }

        if (!string.IsNullOrEmpty(entry.ObjectType))
        {
            return EntryKind.ResourceObject;
        }

        return entry.Storage == StorageType.StreamingAssets
            ? EntryKind.Text
            : EntryKind.Pack;
    }

    public static void Reload()
    {
        _isInitialized = false;
        Initialize();
    }

    public static T GetPack<T>(string id) where T : BasePackData
    {
        var entry = RequireEntry(id);
        if (entry == null) return null;

        if (entry.Kind != EntryKind.Pack && entry.Kind != EntryKind.Unknown)
        {
            Debug.LogError($"[MetaLib] Entry '{id}' is not registered as a Pack.");
            return null;
        }

        var jsonContent = GetRaw(id);
        if (string.IsNullOrEmpty(jsonContent)) return null;

        try
        {
            return JsonConvert.DeserializeObject<T>(jsonContent, JsonSettings);
        }
        catch (Exception e)
        {
            Debug.LogError($"[MetaLib] Failed to deserialize pack '{id}': {e.Message}");
            return null;
        }
    }

    public static T GetObject<T>(string id)
    {
        object obj = GetObject(id, typeof(T));
        return obj is T typed ? typed : default;
    }

    public static object GetObject(string id)
    {
        var entry = RequireEntry(id);
        if (entry == null) return null;

        var resolvedType = ResolveObjectType(entry.ObjectType);
        return GetObject(id, resolvedType);
    }

    public static string GetRaw(string id)
    {
        var entry = RequireEntry(id);
        if (entry == null) return null;

        switch (entry.Storage)
        {
            case StorageType.Resources:
            {
                var textAsset = Resources.Load<TextAsset>(entry.ResourcePath);
                if (textAsset == null)
                {
                    Debug.LogError($"[MetaLib] Resource not found: {entry.ResourcePath}");
                    return null;
                }

                return textAsset.text;
            }
            case StorageType.StreamingAssets:
            {
                try
                {
                    string fullPath = Path.Combine(Application.streamingAssetsPath, entry.ResourcePath);
                    return File.ReadAllText(fullPath);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[MetaLib] Failed to read StreamingAssets file '{entry.ResourcePath}': {e.Message}");
                    return null;
                }
            }
            default:
                return null;
        }
    }

    public static string GetMetaString(string id) => GetRaw(id);

    public static MetaEntry GetMeta(string id)
    {
        if (!_isInitialized) Initialize();
        _metadata.TryGetValue(id, out var entry);
        return entry;
    }

    public static bool TryGetResourcePath(string id, out string path)
    {
        path = null;
        var entry = GetMeta(id);
        if (entry == null) return false;
        path = entry.ResourcePath;
        return !string.IsNullOrWhiteSpace(path);
    }

    public static MetaEntry GetMetaByPath(string resourcePath)
    {
        if (!_isInitialized) Initialize();
        foreach (var entry in _metadata.Values)
        {
            if (entry.ResourcePath == resourcePath)
            {
                return entry;
            }
        }

        return null;
    }

    public static IEnumerable<MetaEntry> GetAllMetas()
    {
        if (!_isInitialized) Initialize();
        return _metadata.Values;
    }

    public static bool HasMeta(string id)
    {
        if (!_isInitialized) Initialize();
        return _metadata.ContainsKey(id);
    }

    public static bool HasEntry(string id) => HasMeta(id);

    public static void Register(string id, MetaEntry entry)
    {
        if (!_isInitialized) Initialize();
        if (string.IsNullOrEmpty(id) || entry == null) return;

        entry.EffectiveID = id;
        if (entry.Kind == EntryKind.Unknown)
        {
            entry.Kind = InferEntryKind(entry);
        }

        _metadata[id] = entry;
    }

    public static void Unregister(string id)
    {
        if (!_isInitialized) Initialize();
        _metadata.Remove(id);
    }

    public static void Clear()
    {
        if (!_isInitialized) Initialize();
        _metadata.Clear();
    }

    private static MetaEntry RequireEntry(string id)
    {
        if (!_isInitialized) Initialize();

        if (!_metadata.TryGetValue(id, out var entry))
        {
            Debug.LogError($"[MetaLib] No metadata found for ID '{id}'.");
            return null;
        }

        return entry;
    }

    private static object GetObject(string id, Type requestedType)
    {
        var entry = RequireEntry(id);
        if (entry == null) return null;

        if (entry.Storage == StorageType.Resources)
        {
            return LoadResourceObject(id, entry, requestedType);
        }

        string raw = GetRaw(id);
        if (string.IsNullOrEmpty(raw)) return null;

        if (requestedType == null || requestedType == typeof(string))
        {
            return raw;
        }

        try
        {
            return JsonConvert.DeserializeObject(raw, requestedType, JsonSettings);
        }
        catch (Exception e)
        {
            Debug.LogError($"[MetaLib] Failed to deserialize '{id}' as {requestedType.FullName}: {e.Message}");
            return null;
        }
    }

    private static object LoadResourceObject(string id, MetaEntry entry, Type requestedType)
    {
        Type targetType = requestedType ?? ResolveObjectType(entry.ObjectType) ?? typeof(UnityEngine.Object);

        if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
        {
            var asset = Resources.Load(entry.ResourcePath, targetType);
            if (asset == null)
            {
                Debug.LogError($"[MetaLib] Resource object not found: {entry.ResourcePath} ({targetType.FullName})");
            }

            return asset;
        }

        var textAsset = Resources.Load<TextAsset>(entry.ResourcePath);
        if (textAsset == null)
        {
            Debug.LogError($"[MetaLib] Text resource not found for non-Unity object '{id}': {entry.ResourcePath}");
            return null;
        }

        if (targetType == typeof(string))
        {
            return textAsset.text;
        }

        try
        {
            return JsonConvert.DeserializeObject(textAsset.text, targetType, JsonSettings);
        }
        catch (Exception e)
        {
            Debug.LogError($"[MetaLib] Failed to deserialize resource '{id}' as {targetType.FullName}: {e.Message}");
            return null;
        }
    }

    private static Type ResolveObjectType(string objectTypeName)
    {
        if (string.IsNullOrEmpty(objectTypeName))
        {
            return null;
        }

        var type = Type.GetType(objectTypeName, false);
        if (type != null)
        {
            return type;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(objectTypeName, false);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

#if UNITY_EDITOR
    public static void Save()
    {
        var json = JsonConvert.SerializeObject(_metadata, Formatting.Indented);
        string directory = Path.GetDirectoryName(Application.dataPath + "/Resources/" + METALIB_PATH + ".json");
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string fullPath = Application.dataPath + "/Resources/" + METALIB_PATH + ".json";
        File.WriteAllText(fullPath, json);
        UnityEditor.AssetDatabase.Refresh();
    }
#else
    public static void Save() { }
#endif

    public static string GetDebugInfo()
    {
        if (!_isInitialized) Initialize();
        var info = new System.Text.StringBuilder();
        info.AppendLine($"[MetaLib] Entry count: {_metadata.Count}");
        foreach (var kvp in _metadata)
        {
            info.AppendLine($"  - {kvp.Key}: {kvp.Value?.Kind} ({kvp.Value?.ResourcePath})");
        }

        return info.ToString();
    }
}
