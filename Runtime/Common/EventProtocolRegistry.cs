using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 事件协议校验注册表喵~
/// 职责：扫描所有程序集中的 [EventProtocolValidator] 方法，为项目层提供外部 payload 校验扩展。
/// </summary>
public static class EventProtocolRegistry
{
    public class ValidatorMeta
    {
        public EventProtocolValidatorAttribute Info;
        public MethodInfo Method;
    }

    private static readonly Dictionary<EventProtocol, ValidatorMeta> _validators = new Dictionary<EventProtocol, ValidatorMeta>();
    private static bool _isInitialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Initialize()
    {
        _validators.Clear();

        var allMethods = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(SafeGetTypes)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static));

        foreach (var method in allMethods)
        {
            var attr = method.GetCustomAttribute<EventProtocolValidatorAttribute>();
            if (attr == null) continue;

            var parameters = method.GetParameters();
            if (parameters.Length == 1 &&
                parameters[0].ParameterType == typeof(object) &&
                method.ReturnType == typeof(bool))
            {
                _validators[attr.Protocol] = new ValidatorMeta
                {
                    Info = attr,
                    Method = method
                };
            }
            else
            {
                Debug.LogWarning($"[EventProtocolRegistry] 协议校验方法 {method.DeclaringType?.Name}.{method.Name} 签名不符合规范，已跳过喵~\n" +
                                 "要求: (object payload) → bool");
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
        if (!_isInitialized)
            Initialize();
    }

    public static bool Validate(EventProtocol protocol, object payload)
    {
        EnsureInitialized();

        if (_validators.TryGetValue(protocol, out var meta))
        {
            try
            {
                return (bool)meta.Method.Invoke(null, new[] { payload });
            }
            catch (Exception e)
            {
                Debug.LogError($"[EventProtocolRegistry] 执行 {protocol} 校验器失败：{e}");
                return false;
            }
        }

        return payload != null;
    }
}
