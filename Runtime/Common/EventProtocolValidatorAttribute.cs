using System;

/// <summary>
/// 事件协议校验器特性喵~
/// 用于在任意公开静态方法上注册某个 EventProtocol 的 payload 校验逻辑。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class EventProtocolValidatorAttribute : Attribute
{
    public EventProtocol Protocol { get; }

    public EventProtocolValidatorAttribute(EventProtocol protocol)
    {
        Protocol = protocol;
    }
}
