using System;
using System.Collections.Generic;

namespace NekoGraph
{

/// <summary>
/// 每个宿主对象独立持有的轻量图上下文。
/// 不参与全局 GraphHub 调度，适合临时单位的本地 pack / 被动 / 光环后端。
/// </summary>
public class LocalGraphHub
{
    public EntityGraphContext Context { get; }
    public GraphAnalyser Analyser => Context.Analyser;
    public GraphRunner Runner => Context.Runner;
    public Dictionary<string, BasePackData> PackTable => Context.PackTable;

    [Obsolete("PackDataDict 语义已统一为 PackTable。新代码请使用 PackTable。", false)]
    public Dictionary<string, BasePackData> PackDataDict => Context.PackTable;

    public LocalGraphHub(GraphInstanceSlot slot = GraphInstanceSlot.System)
    {
        Context = new EntityGraphContext(slot);
    }

    public void SetPackTable(Dictionary<string, BasePackData> packTable)
    {
        Context.SetPackTable(packTable);
        Context.Analyser.RebuildIndex();
    }

    [Obsolete("SetPackDataDict 已更名为 SetPackTable。", false)]
    public void SetPackDataDict(Dictionary<string, BasePackData> packDataDict)
    {
        SetPackTable(packDataDict);
    }

    public void Tick()
    {
        Context.Runner.Tick();
    }
}

}
