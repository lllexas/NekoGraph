using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// VFS 载荷内容类型喵~
/// </summary>
public enum VFSContentKind
{
    Json,
    Csv,
    UnityObject
}

/// <summary>
/// VFS 载荷来源类型喵~
/// </summary>
public enum VFSContentSource
{
    Inline,
    Reference
}

/// <summary>
/// VFS 解析后的统一载荷喵~
/// 让后缀处理器不再被 DataJson 这个历史命名束缚。
/// </summary>
public sealed class VFSResolvedContent
{
    public string Extension { get; set; }
    public VFSContentKind Kind { get; set; }
    public VFSContentSource Source { get; set; }
    public string RawText { get; set; }
    public string ReferencePath { get; set; }
    public string AssetGuid { get; set; }
    public string AssetPath { get; set; }
    public string UnityObjectTypeName { get; set; }
    public UnityEngine.Object UnityObject { get; set; }

    public bool HasText => !string.IsNullOrWhiteSpace(RawText);
    public bool HasReference => !string.IsNullOrWhiteSpace(ReferencePath) || !string.IsNullOrWhiteSpace(AssetPath);
    public bool HasUnityObject => UnityObject != null;

    public string GetTextOrEmpty() => RawText ?? string.Empty;

    public T ParseJson<T>()
    {
        if (string.IsNullOrWhiteSpace(RawText))
            return default;

        return JsonConvert.DeserializeObject<T>(RawText);
    }

    public T GetUnityObject<T>() where T : UnityEngine.Object
    {
        return UnityObject as T;
    }
}

/// <summary>
/// VFS 载荷解析器喵~
/// 负责把节点上的配置解析成运行时统一载荷。
/// </summary>
public static class VFSContentResolver
{
    public static VFSResolvedContent Resolve(VFSNodeData node)
    {
        if (node == null)
            return null;

        var effectiveKind = node.GetEffectiveContentKind();
        var effectiveSource = node.GetEffectiveContentSource();
        string rawText = ResolveText(node, effectiveKind, effectiveSource);
        UnityEngine.Object unityObject = ResolveUnityObject(node, effectiveKind, effectiveSource);

        return new VFSResolvedContent
        {
            Extension = node.Extension,
            Kind = effectiveKind,
            Source = effectiveSource,
            RawText = rawText,
            ReferencePath = node.ReferencePath,
            AssetGuid = node.AssetGuid,
            AssetPath = node.AssetPath,
            UnityObjectTypeName = node.UnityObjectTypeName,
            UnityObject = unityObject
        };
    }

    private static string ResolveText(VFSNodeData node, VFSContentKind kind, VFSContentSource source)
    {
        if (kind == VFSContentKind.UnityObject)
            return string.Empty;

        if (source == VFSContentSource.Inline)
            return node.GetInlineText();

        if (TryLoadReferencedText(node.ReferencePath, node.AssetPath, out var text))
            return text;

#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(node.AssetPath) && File.Exists(node.AssetPath))
        {
            return File.ReadAllText(node.AssetPath);
        }
#endif

        return node.GetInlineText();
    }

    private static UnityEngine.Object ResolveUnityObject(VFSNodeData node, VFSContentKind kind, VFSContentSource source)
    {
        if (kind != VFSContentKind.UnityObject || source != VFSContentSource.Reference)
            return null;

        if (!string.IsNullOrWhiteSpace(node.ReferencePath))
        {
            if (!string.IsNullOrWhiteSpace(node.UnityObjectTypeName))
            {
                var objectType = Type.GetType(node.UnityObjectTypeName);
                if (objectType != null && typeof(UnityEngine.Object).IsAssignableFrom(objectType))
                {
                    var typedObject = Resources.Load(node.ReferencePath, objectType);
                    if (typedObject != null)
                        return typedObject;
                }
            }

            var anyObject = Resources.Load(node.ReferencePath);
            if (anyObject != null)
                return anyObject;
        }

#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(node.AssetPath))
        {
            var editorObject = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(node.AssetPath);
            if (editorObject != null)
                return editorObject;
        }
#endif

        return null;
    }

    public static bool TryLoadReferencedText(string referencePath, string assetPath, out string text)
    {
        text = string.Empty;

        if (!string.IsNullOrWhiteSpace(referencePath))
        {
            var textAsset = Resources.Load<TextAsset>(referencePath);
            if (textAsset != null)
            {
                text = textAsset.text;
                return true;
            }

            foreach (var candidate in BuildStreamingAssetsCandidates(referencePath))
            {
                if (File.Exists(candidate))
                {
                    text = File.ReadAllText(candidate);
                    return true;
                }
            }
        }

#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(assetPath) && File.Exists(assetPath))
        {
            text = File.ReadAllText(assetPath);
            return true;
        }
#endif

        return false;
    }

    public static bool TryResolveReferenceFilePath(string referencePath, string assetPath, out string filePath)
    {
        filePath = string.Empty;

#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(assetPath) && File.Exists(assetPath))
        {
            filePath = assetPath;
            return true;
        }
#endif

        if (string.IsNullOrWhiteSpace(referencePath))
            return false;

        foreach (var candidate in BuildStreamingAssetsCandidates(referencePath))
        {
            if (File.Exists(candidate))
            {
                filePath = candidate;
                return true;
            }
        }

        return false;
    }

    private static string[] BuildStreamingAssetsCandidates(string referencePath)
    {
        string normalized = referencePath.Replace('\\', '/').TrimStart('/');
        return new[]
        {
            Path.Combine(Application.streamingAssetsPath, normalized),
            Path.Combine(Application.streamingAssetsPath, normalized + ".json"),
            Path.Combine(Application.streamingAssetsPath, normalized + ".csv"),
            Path.Combine(Application.streamingAssetsPath, normalized + ".txt")
        };
    }
}
