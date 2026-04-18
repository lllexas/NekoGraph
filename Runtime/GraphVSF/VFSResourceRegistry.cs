using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace NekoGraph
{
    /// <summary>
    /// VFS 资源注册表：扫描并聚合资源声明和能力方法。
    /// 真正的注册结果是 VFSResourceDriver，ExeRegistry / VFSQueryRegistry 只是 facade。
    /// </summary>
    public static class VFSResourceRegistry
    {
        private static Dictionary<string, VFSResourceDriver> _drivers;
        private static bool _isInitialized;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void EditorInitialize()
        {
            Initialize(force: true);
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            Initialize(force: false);
        }

        public static void Initialize(bool force)
        {
            if (_isInitialized && !force) return;

            _drivers = new Dictionary<string, VFSResourceDriver>(StringComparer.OrdinalIgnoreCase);

            var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(SafeGetTypes)
                .ToArray();

            ScanNewResourceModel(allTypes);
            ScanLegacyMethodAttributes(allTypes);

            _isInitialized = true;
            Debug.Log($"[VFSResourceRegistry] 初始化完成，共注册 {_drivers.Count} 个资源驱动喵~");
        }

        private static void EnsureInitialized()
        {
            if (!_isInitialized) Initialize();
        }

        private static void ScanNewResourceModel(IEnumerable<Type> allTypes)
        {
            foreach (var type in allTypes)
            {
                var resourceAttr = type.GetCustomAttribute<VFSResourceAttribute>();
                if (resourceAttr == null) continue;

                string suffix = NormalizeSuffix(resourceAttr.Suffix);
                var driver = GetOrCreateDriver(suffix, resourceAttr.DataType, $"type {type.FullName}");

                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
                foreach (var method in methods)
                {
                    if (method.GetCustomAttribute<VFSExecuteAttribute>() != null)
                    {
                        driver.Execute = BuildExecuteDelegate(method);
                    }

                    if (method.GetCustomAttribute<VFSQueryAttribute>() != null)
                    {
                        driver.Query = BuildQueryDelegate(method);
                    }
                }
            }
        }

        private static void ScanLegacyMethodAttributes(IEnumerable<Type> allTypes)
        {
            var allMethods = allTypes.SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static));
            foreach (var method in allMethods)
            {
                var exeAttr = method.GetCustomAttribute<EXEHandlerAttribute>();
                if (exeAttr != null)
                {
                    string suffix = NormalizeSuffix(exeAttr.Suffix);
                    var driver = GetOrCreateDriver(suffix, exeAttr.DataType, $"legacy execute {method.DeclaringType?.FullName}.{method.Name}");
                    driver.Execute = BuildExecuteDelegate(method);
                }

                var queryAttr = method.GetCustomAttribute<VFSQueryHandlerAttribute>();
                if (queryAttr != null)
                {
                    string suffix = NormalizeSuffix(queryAttr.Suffix);
                    var driver = GetOrCreateDriver(suffix, queryAttr.DataType, $"legacy query {method.DeclaringType?.FullName}.{method.Name}");
                    driver.Query = BuildQueryDelegate(method);
                }
            }
        }

        private static VFSResourceDriver GetOrCreateDriver(string suffix, Type dataType, string source)
        {
            if (!_drivers.TryGetValue(suffix, out var driver))
            {
                driver = new VFSResourceDriver
                {
                    Suffix = suffix,
                    DataType = dataType
                };
                _drivers[suffix] = driver;
                return driver;
            }

            if (driver.DataType == null && dataType != null)
            {
                driver.DataType = dataType;
            }
            else if (driver.DataType != null && dataType != null && driver.DataType != dataType)
            {
                Debug.LogWarning($"[VFSResourceRegistry] 后缀 {suffix} 的 DataType 声明不一致：已有 {driver.DataType.Name}，来源 {source} 试图注册 {dataType.Name}。保留已有声明喵~");
            }

            return driver;
        }

        private static VFSExecuteDelegate BuildExecuteDelegate(MethodInfo method)
        {
            var parameters = method.GetParameters();
            bool isSixParamSignature =
                parameters.Length == 6 &&
                parameters[1].ParameterType == typeof(SignalContext) &&
                parameters[2].ParameterType == typeof(BasePackData) &&
                parameters[3].ParameterType == typeof(GraphRunner) &&
                parameters[4].ParameterType == typeof(string) &&
                parameters[5].ParameterType == typeof(Action) &&
                (method.ReturnType == typeof(HandleResult) || method.ReturnType == typeof(void));

            bool isLegacyFiveParamSignature =
                parameters.Length == 5 &&
                parameters[1].ParameterType == typeof(SignalContext) &&
                parameters[2].ParameterType == typeof(BasePackData) &&
                parameters[3].ParameterType == typeof(GraphRunner) &&
                parameters[4].ParameterType == typeof(string) &&
                (method.ReturnType == typeof(HandleResult) || method.ReturnType == typeof(void));

            if (!isSixParamSignature && !isLegacyFiveParamSignature)
            {
                Debug.LogWarning($"[VFSResourceRegistry] Execute 方法 {method.DeclaringType?.Name}.{method.Name} 签名不符合规范，已跳过喵~");
                return null;
            }

            if (parameters[0].ParameterType == typeof(VFSResolvedContent))
            {
                if (isLegacyFiveParamSignature && method.ReturnType == typeof(HandleResult))
                {
                    var handler = (Func<VFSResolvedContent, SignalContext, BasePackData, GraphRunner, string, HandleResult>)
                        Delegate.CreateDelegate(typeof(Func<VFSResolvedContent, SignalContext, BasePackData, GraphRunner, string, HandleResult>), method);
                    return (content, context, pack, runner, packIDKey, continueAction) =>
                        handler(content, context, pack, runner, packIDKey);
                }

                if (isLegacyFiveParamSignature)
                {
                    var legacyVoidHandler = (Action<VFSResolvedContent, SignalContext, BasePackData, GraphRunner, string>)
                        Delegate.CreateDelegate(typeof(Action<VFSResolvedContent, SignalContext, BasePackData, GraphRunner, string>), method);
                    return (content, context, pack, runner, packIDKey, continueAction) =>
                    {
                        legacyVoidHandler(content, context, pack, runner, packIDKey);
                        return HandleResult.Push;
                    };
                }

                if (method.ReturnType == typeof(HandleResult))
                {
                    return (VFSExecuteDelegate)Delegate.CreateDelegate(typeof(VFSExecuteDelegate), method);
                }

                var legacyVoidHandlerWithContinue = (Action<VFSResolvedContent, SignalContext, BasePackData, GraphRunner, string, Action>)
                    Delegate.CreateDelegate(typeof(Action<VFSResolvedContent, SignalContext, BasePackData, GraphRunner, string, Action>), method);
                return (content, context, pack, runner, packIDKey, continueAction) =>
                {
                    legacyVoidHandlerWithContinue(content, context, pack, runner, packIDKey, continueAction);
                    return HandleResult.Push;
                };
            }

            if (parameters[0].ParameterType == typeof(string))
            {
                if (isLegacyFiveParamSignature && method.ReturnType == typeof(HandleResult))
                {
                    var handler = (Func<string, SignalContext, BasePackData, GraphRunner, string, HandleResult>)
                        Delegate.CreateDelegate(typeof(Func<string, SignalContext, BasePackData, GraphRunner, string, HandleResult>), method);
                    return (content, context, pack, runner, packIDKey, continueAction) =>
                        handler(content?.RawText ?? string.Empty, context, pack, runner, packIDKey);
                }

                if (isLegacyFiveParamSignature)
                {
                    var legacyVoidHandler = (Action<string, SignalContext, BasePackData, GraphRunner, string>)
                        Delegate.CreateDelegate(typeof(Action<string, SignalContext, BasePackData, GraphRunner, string>), method);
                    return (content, context, pack, runner, packIDKey, continueAction) =>
                    {
                        legacyVoidHandler(content?.RawText ?? string.Empty, context, pack, runner, packIDKey);
                        return HandleResult.Push;
                    };
                }

                var legacyTextHandler = (Action<string, SignalContext, BasePackData, GraphRunner, string, Action>)
                    Delegate.CreateDelegate(typeof(Action<string, SignalContext, BasePackData, GraphRunner, string, Action>), method);
                return (content, context, pack, runner, packIDKey, continueAction) =>
                {
                    legacyTextHandler(content?.RawText ?? string.Empty, context, pack, runner, packIDKey, continueAction);
                    return HandleResult.Push;
                };
            }

            Debug.LogWarning($"[VFSResourceRegistry] Execute 方法 {method.DeclaringType?.Name}.{method.Name} 第一个参数必须是 VFSResolvedContent 或 string，已跳过喵~");
            return null;
        }

        private static VFSQueryDelegate BuildQueryDelegate(MethodInfo method)
        {
            var parameters = method.GetParameters();
            bool valid =
                parameters.Length == 2 &&
                parameters[0].ParameterType == typeof(VFSResolvedContent) &&
                parameters[1].ParameterType == typeof(VFSQueryContext) &&
                method.ReturnType == typeof(VFSQueryResult);

            if (!valid)
            {
                Debug.LogWarning($"[VFSResourceRegistry] Query 方法 {method.DeclaringType?.Name}.{method.Name} 签名不符合规范，已跳过喵~");
                return null;
            }

            return (VFSQueryDelegate)Delegate.CreateDelegate(typeof(VFSQueryDelegate), method);
        }

        public static void RegisterExecute(string suffix, VFSExecuteDelegate handler, Type dataType = null)
        {
            EnsureInitialized();
            var key = NormalizeSuffix(suffix);
            var driver = GetOrCreateDriver(key, dataType, "manual execute register");
            driver.Execute = handler;
        }

        public static void RegisterQuery(string suffix, VFSQueryDelegate handler, Type dataType = null)
        {
            EnsureInitialized();
            var key = NormalizeSuffix(suffix);
            var driver = GetOrCreateDriver(key, dataType, "manual query register");
            driver.Query = handler;
        }

        public static bool TryGetDriver(string suffix, out VFSResourceDriver driver)
        {
            EnsureInitialized();
            return _drivers.TryGetValue(NormalizeSuffix(suffix), out driver);
        }

        public static Type GetDataType(string suffix)
        {
            EnsureInitialized();
            return TryGetDriver(suffix, out var driver) ? driver.DataType : null;
        }

        public static List<string> GetAllSuffixes()
        {
            EnsureInitialized();
            return new List<string>(_drivers.Keys);
        }

        public static List<string> GetQueryableSuffixes()
        {
            EnsureInitialized();
            return _drivers.Values.Where(d => d.HasQuery).Select(d => d.Suffix).ToList();
        }

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
}
