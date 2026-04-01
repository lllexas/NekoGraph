using System.Collections.Generic;

/// <summary>
/// NekoGraph 需要的用户数据接口喵~
/// 宿主程序集的 UserModel 实现此接口，GraphHub 通过此接口读取存档数据。
/// </summary>
public interface IUserPackData
{
    Dictionary<string, BasePackData> GetPlayerPackDict();
    Dictionary<string, BasePackData> GetEntityPackDict(GraphInstanceSlot slot, bool createIfMissing);
}
