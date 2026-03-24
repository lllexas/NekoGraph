using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 比较结果枚举喵~
/// </summary>
public enum ComparerResult
{
    Pass,
    Fail,
    TypeMismatch
}

/// <summary>
/// 比较器元数据特性喵~
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ComparerInfoAttribute : Attribute
{
    public EventProtocol Protocol { get; }
    public string Name { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public string[] ParamNames { get; }
    public string Tooltip { get; set; }

    public ComparerInfoAttribute(EventProtocol protocol, string name, string displayName, string category, string[] paramNames = null)
    {
        Protocol = protocol;
        Name = name;
        DisplayName = displayName;
        Category = category;
        ParamNames = paramNames ?? Array.Empty<string>();
    }
}

/// <summary>
/// 比较器注册表 - 全域扫描版喵~
/// 所有程序集中带 [ComparerInfo] 的公开静态方法自动注册。
///
/// 比较器砖块签名规范：
///   public static ComparerResult MyComparer(object payload, string[] args)
/// </summary>
public static class ComparerRegistry
{
    public class ComparerMeta
    {
        public ComparerInfoAttribute Info;
        public MethodInfo Method;
    }

    private static readonly Dictionary<string, ComparerMeta> _comparers = new Dictionary<string, ComparerMeta>();
    private static bool _isInitialized = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Initialize()
    {
        _comparers.Clear();

        // 全域扫描：所有已加载程序集中带 [ComparerInfo] 的公开静态方法喵~
        var allMethods = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(SafeGetTypes)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static));

        foreach (var method in allMethods)
        {
            var attr = method.GetCustomAttribute<ComparerInfoAttribute>();
            if (attr == null) continue;

            var parameters = method.GetParameters();
            if (parameters.Length == 2 &&
                parameters[0].ParameterType == typeof(object) &&
                parameters[1].ParameterType == typeof(string[]) &&
                method.ReturnType == typeof(ComparerResult))
            {
                _comparers[attr.Name] = new ComparerMeta { Info = attr, Method = method };
            }
            else
            {
                Debug.LogWarning($"[ComparerRegistry] 比较器方法 {method.DeclaringType?.Name}.{method.Name} 签名不符合规范，已跳过喵~\n" +
                                 $"要求: (object payload, string[] args) → ComparerResult");
            }
        }

        _isInitialized = true;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch { return Array.Empty<Type>(); }
    }

    private static void EnsureInitialized()
    {
        if (!_isInitialized || _comparers.Count == 0)
            Initialize();
    }

    public static ComparerResult Execute(string name, object payload, string[] args)
    {
        EnsureInitialized();
        if (!_comparers.TryGetValue(name, out var meta)) return ComparerResult.Fail;

        try
        {
            return (ComparerResult)meta.Method.Invoke(null, new object[] { payload, args });
        }
        catch (Exception e)
        {
            Debug.LogError($"[ComparerRegistry] 执行 {name} 出错: {e.Message} 喵~");
            return ComparerResult.Fail;
        }
    }

    public static IEnumerable<ComparerMeta> GetAllComparers()
    {
        EnsureInitialized();
        return _comparers.Values;
    }

    public static ComparerMeta GetMeta(string name)
    {
        EnsureInitialized();
        return _comparers.TryGetValue(name, out var meta) ? meta : null;
    }

    // =========================================================
    // 内置砖块：数值系列 (无游戏依赖)
    // =========================================================

    private static ComparerResult FastCompare(double val, string op, double target)
    {
        return op switch
        {
            ">"  => val > target  ? ComparerResult.Pass : ComparerResult.Fail,
            "<"  => val < target  ? ComparerResult.Pass : ComparerResult.Fail,
            ">=" => val >= target ? ComparerResult.Pass : ComparerResult.Fail,
            "<=" => val <= target ? ComparerResult.Pass : ComparerResult.Fail,
            "==" => Mathf.Approximately((float)val, (float)target) ? ComparerResult.Pass : ComparerResult.Fail,
            "!=" => !Mathf.Approximately((float)val, (float)target) ? ComparerResult.Pass : ComparerResult.Fail,
            _    => ComparerResult.Fail
        };
    }

    [ComparerInfo(EventProtocol.Numeric, "val_compare", "🔢 数值: 常规比较", "数值", new[] { "运算符", "比较值" }, Tooltip = "支持 > < >= <= == != 喵~")]
    public static ComparerResult NumericCompare(object payload, string[] args)
    {
        if (payload == null) return ComparerResult.Fail;

        double val;
        if (payload is int i) val = i;
        else if (payload is float f) val = f;
        else if (payload is double d) val = d;
        else if (!double.TryParse(payload.ToString(), out val)) return ComparerResult.TypeMismatch;

        return FastCompare(val, args[0], double.Parse(args[1]));
    }

    // =========================================================
    // 内置砖块：字符串系列
    // =========================================================

    [ComparerInfo(EventProtocol.String, "str_match", "🆔 字符: 完全匹配", "字符", new[] { "预期文本" }, Tooltip = "检查字符串是否完全相等喵~")]
    public static ComparerResult StringMatch(object payload, string[] args)
    {
        if (payload is not string str) return ComparerResult.TypeMismatch;
        return str == args[0] ? ComparerResult.Pass : ComparerResult.Fail;
    }

    [ComparerInfo(EventProtocol.String, "str_contains", "🔍 字符: 包含关键词", "字符", new[] { "关键词" }, Tooltip = "检查字符串是否包含指定关键词喵~")]
    public static ComparerResult StringContains(object payload, string[] args)
    {
        if (payload is not string str) return ComparerResult.TypeMismatch;
        return str.Contains(args[0]) ? ComparerResult.Pass : ComparerResult.Fail;
    }

    [ComparerInfo(EventProtocol.String, "str_not_empty", "❗ 字符: 非空检查", "字符", null, Tooltip = "检查字符串是否不为空且不全为空格喵~")]
    public static ComparerResult StringNotEmpty(object payload, string[] args)
    {
        if (payload is not string str) return ComparerResult.TypeMismatch;
        return !string.IsNullOrWhiteSpace(str) ? ComparerResult.Pass : ComparerResult.Fail;
    }
}
