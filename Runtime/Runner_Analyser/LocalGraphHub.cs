using System.Collections.Generic;

/// <summary>
/// 每个宿主对象独立持有的轻量图上下文。
/// 不参与全局 GraphHub 调度，适合临时单位的本地 pack / 被动 / 光环后端。
/// </summary>
public sealed class LocalGraphHub
{
    public EntityGraphContext Context { get; }
    public GraphAnalyser Analyser => Context.Analyser;
    public GraphRunner Runner => Context.Runner;
    public Dictionary<string, BasePackData> PackDataDict => Context.PackDataDict;

    public LocalGraphHub(GraphInstanceSlot slot = GraphInstanceSlot.System)
    {
        Context = new EntityGraphContext(slot);
    }

    public void SetPackDataDict(Dictionary<string, BasePackData> packDataDict)
    {
        Context.SetPackDataDict(packDataDict);
        Context.Analyser.RebuildIndex();
    }

    public void Tick()
    {
        Context.Runner.Tick();
    }
}
