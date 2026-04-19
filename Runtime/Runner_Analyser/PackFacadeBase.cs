using System;
using UnityEngine;

namespace NekoGraph
{

/// <summary>
/// Pack 领域门面的实例基类。
/// 由 GraphHub 持有，并在启动阶段绑定到具体 PackID。
/// </summary>
[Serializable]
public abstract class PackFacadeBase
{
    [SerializeField]
    private string _boundPackID;

    public string BoundPackID => _boundPackID;

    public string ResolvedPackID => string.IsNullOrWhiteSpace(_boundPackID)
        ? GetDefaultPackID()
        : _boundPackID;

    public void BindPack(string packID)
    {
        _boundPackID = packID;
    }

    public void ClearPackBinding()
    {
        _boundPackID = null;
    }

    protected abstract string GetDefaultPackID();
}

}
