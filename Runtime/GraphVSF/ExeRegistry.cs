using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// EXE 注册表 - VFS 后缀名到执行处理器的映射喵~
///
/// 镜像 CommandRegistry 的设计模式：
/// - 全域扫描所有程序集中带 [EXEHandler] 属性的公开静态方法并自动注册
/// - 方法可定义在任意程序集的任意类中，无需显式注册
/// - 同时提供 Register() 手动注册 API 供程序化使用
///
/// 处理器方法签名规范：
///   public static void MyHandler(string dataJson, SignalContext context, BasePackData pack, GraphRunner runner, string packInstanceID)
///
/// 与 CommandRegistry 的对称设计：
///   CommandHandlerWithOutput: (IConsoleController, int, string[], object) → CommandOutput
///   EXEHandlerDelegate:       (string dataJson, SignalContext, BasePackData, GraphRunner, string) → void
/// </summary>
public static class ExeRegistry
{
    // ─────────────────────────────────────────────────────────
    // 委托类型
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// EXE 处理器委托喵~
    /// dataJson: VFSNodeData.DataJson 原始字符串，处理器自行反序列化
    /// </summary>
    public delegate void EXEHandlerDelegate(
        string dataJson,
        SignalContext context,
        BasePackData pack,
        GraphRunner runner,
        string packInstanceID
    );

    // ─────────────────────────────────────────────────────────
    // 内部状态
    // ─────────────────────────────────────────────────────────

    private static Dictionary<string, EXEHandlerDelegate> _handlers;
    private static Dictionary<string, Type> _handlerTypes;
    private static bool _isInitialized = false;

    // ─────────────────────────────────────────────────────────
    // 初始化：全域扫描喵~
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 初始化 ExeRegistry，全域扫描带 [EXEHandler] 属性的静态方法并注册喵~
    /// Unity Runtime 自动调用，Editor/CLI 可手动调用。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Initialize()
    {
        if (_isInitialized) return;

        _handlers = new Dictionary<string, EXEHandlerDelegate>(StringComparer.OrdinalIgnoreCase);
        _handlerTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        // 全域扫描：所有已加载程序集中带 [EXEHandler] 的公开静态 void 方法喵~
        var allMethods = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(SafeGetTypes)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static));

        foreach (var method in allMethods)
        {
            var attr = method.GetCustomAttribute<EXEHandlerAttribute>();
            if (attr == null) continue;

            var parameters = method.GetParameters();
            if (parameters.Length == 5 &&
                parameters[0].ParameterType == typeof(string) &&
                parameters[1].ParameterType == typeof(SignalContext) &&
                parameters[2].ParameterType == typeof(BasePackData) &&
                parameters[3].ParameterType == typeof(GraphRunner) &&
                parameters[4].ParameterType == typeof(string) &&
                method.ReturnType == typeof(void))
            {
                var suffix = NormalizeSuffix(attr.Suffix);
                var handler = (EXEHandlerDelegate)Delegate.CreateDelegate(typeof(EXEHandlerDelegate), method);
                _handlers[suffix] = handler;
                if (attr.DataType != null)
                    _handlerTypes[suffix] = attr.DataType;
            }
            else
            {
                Debug.LogWarning($"[ExeRegistry] 方法 {method.DeclaringType?.Name}.{method.Name} 签名不符合规范，已跳过喵~\n" +
                    "要求: (string dataJson, SignalContext, BasePackData, GraphRunner, string) → void");
            }
        }

        _isInitialized = true;
        Debug.Log($"[ExeRegistry] 初始化完成，共注册 {_handlers.Count} 个后缀处理器喵~");
    }

    private static void EnsureInitialized()
    {
        if (!_isInitialized) Initialize();
    }

    // ─────────────────────────────────────────────────────────
    // 手动注册 API
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 手动注册后缀处理器喵~（程序化注册，优先级高于属性扫描）
    /// </summary>
    public static void Register(string suffix, EXEHandlerDelegate handler, Type dataType = null)
    {
        EnsureInitialized();
        var key = NormalizeSuffix(suffix);
        _handlers[key] = handler;
        if (dataType != null) _handlerTypes[key] = dataType;
    }

    // ─────────────────────────────────────────────────────────
    // 查询 API
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 尝试获取后缀对应的处理器喵~
    /// </summary>
    public static bool TryGetHandler(string suffix, out EXEHandlerDelegate handler)
    {
        EnsureInitialized();
        return _handlers.TryGetValue(NormalizeSuffix(suffix), out handler);
    }

    /// <summary>
    /// 获取后缀对应的 DataJson 数据类型（供编辑器字段提示用）喵~
    /// 若未注册或未声明 DataType 则返回 null。
    /// </summary>
    public static Type GetDataType(string suffix)
    {
        EnsureInitialized();
        _handlerTypes.TryGetValue(NormalizeSuffix(suffix), out var type);
        return type;
    }

    /// <summary>
    /// 获取所有已注册的后缀名喵~
    /// </summary>
    public static List<string> GetAllSuffixes()
    {
        EnsureInitialized();
        return new List<string>(_handlers.Keys);
    }

    // ─────────────────────────────────────────────────────────
    // 工具方法
    // ─────────────────────────────────────────────────────────

    private static string NormalizeSuffix(string suffix)
    {
        if (string.IsNullOrEmpty(suffix)) return suffix;
        if (!suffix.StartsWith(".")) suffix = "." + suffix;
        return suffix.ToLowerInvariant();
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch { return Array.Empty<Type>(); }
    }
}
