using UnityEngine;
using System.Linq;

namespace NekoGraph
{

// =========================================================
// 任务节点策略 - MissionNode_A/S/F/R 处理器喵~
// =========================================================

public class MissionNodeAStrategy : NodeStrategy
{
    public override void OnSignalEnter(BaseNodeData data, SignalContext context, BasePackData pack, GraphRunner runner, string packInstanceID)
    {
        if (data is not MissionNode_A_Data missionNode) return;

        if (runner.EnableDebugLog)
        {
            Debug.Log($"[MissionNode A] 任务激活：{missionNode.Title} (ID: {missionNode.MissionID})");
        }

        missionNode.IsActive = true;
        missionNode.IsCompleted = false;
        missionNode.IsFailed = false;

        SendUIRefreshSignal();
        PropagateSignal(missionNode, context, pack);
    }

    public override void OnEvent(BaseNodeData data, string eventName, object eventData, BasePackData pack, GraphRunner runner, string packInstanceID)
    {
    }

    private void PropagateSignal(MissionNode_A_Data node, SignalContext context, BasePackData pack)
    {
        EnqueueSignals(pack, node.OutPutNodeIDs, context);
    }

    private void SendUIRefreshSignal()
    {
        PostSystem.Instance.Send("UI_MISSION_REFRESH", null);
    }
}

public class MissionNodeSStrategy : NodeStrategy
{
    public override void OnSignalEnter(BaseNodeData data, SignalContext context, BasePackData pack, GraphRunner runner, string packInstanceID)
    {
        if (data is not MissionNode_S_Data missionNode) return;

        if (runner.EnableDebugLog)
        {
            Debug.Log($"[MissionNode S] 成功节点触发：{missionNode.NodeID} (MissionID: {missionNode.MissionID})");
        }

        var missionA = pack.Nodes.Values
            .OfType<MissionNode_A_Data>()
            .FirstOrDefault(m => m.MissionID == missionNode.MissionID);

        if (missionA != null)
        {
            missionA.IsCompleted = true;
            missionA.IsActive = false;

            if (runner.EnableDebugLog)
            {
                Debug.Log($"[MissionNode S] 任务完成：{missionA.Title}");
            }

            SendMissionCompleteSignal(missionA);
        }

        PropagateSignal(missionNode, context, pack);
    }

    public override void OnEvent(BaseNodeData data, string eventName, object eventData, BasePackData pack, GraphRunner runner, string packInstanceID)
    {
    }

    private void PropagateSignal(MissionNode_S_Data node, SignalContext context, BasePackData pack)
    {
        EnqueueSignals(pack, node.OutPutNodeIDs, context);
    }

    private void SendMissionCompleteSignal(MissionNode_A_Data mission)
    {
        PostSystem.Instance.Send("UI_MISSION_COMPLETE", mission);
    }
}

public class MissionNodeFStrategy : NodeStrategy
{
    public override void OnSignalEnter(BaseNodeData data, SignalContext context, BasePackData pack, GraphRunner runner, string packInstanceID)
    {
        if (data is not MissionNode_F_Data missionNode) return;

        if (runner.EnableDebugLog)
        {
            Debug.Log($"[MissionNode F] 失败节点触发：{missionNode.NodeID} (MissionID: {missionNode.MissionID})");
        }

        var missionA = pack.Nodes.Values
            .OfType<MissionNode_A_Data>()
            .FirstOrDefault(m => m.MissionID == missionNode.MissionID);

        if (missionA != null)
        {
            missionA.IsFailed = true;
            missionA.IsActive = false;

            if (runner.EnableDebugLog)
            {
                Debug.Log($"[MissionNode F] 任务失败：{missionA.Title}");
            }

            SendMissionFailedSignal(missionA);
        }

        PropagateSignal(missionNode, context, pack);
    }

    public override void OnEvent(BaseNodeData data, string eventName, object eventData, BasePackData pack, GraphRunner runner, string packInstanceID)
    {
    }

    private void PropagateSignal(MissionNode_F_Data node, SignalContext context, BasePackData pack)
    {
        EnqueueSignals(pack, node.OutPutNodeIDs, context);
    }

    private void SendMissionFailedSignal(MissionNode_A_Data mission)
    {
        PostSystem.Instance.Send("UI_MISSION_FAILED", mission);
    }
}

public class MissionNodeRStrategy : NodeStrategy
{
    public override void OnSignalEnter(BaseNodeData data, SignalContext context, BasePackData pack, GraphRunner runner, string packInstanceID)
    {
        if (data is not MissionNode_R_Data missionNode) return;

        if (runner.EnableDebugLog)
        {
            Debug.Log($"[MissionNode R] 刷新节点触发：{missionNode.NodeID} (MissionID: {missionNode.MissionID})");
        }

        var missionA = pack.Nodes.Values
            .OfType<MissionNode_A_Data>()
            .FirstOrDefault(m => m.MissionID == missionNode.MissionID);

        if (missionA != null)
        {
            if (missionNode.ResetProgress)
            {
                Debug.LogWarning("[MissionNode R] ResetProgress 已废弃，请使用 CommandNode 重置进度喵~");
            }

            if (missionNode.Reactivate)
            {
                missionA.IsActive = true;
                missionA.IsCompleted = false;
                missionA.IsFailed = false;

                if (runner.EnableDebugLog)
                {
                    Debug.Log($"[MissionNode R] 任务已重新激活：{missionA.Title}");
                }
            }

            SendUIRefreshSignal();
        }

        PropagateSignal(missionNode, context, pack);
    }

    public override void OnEvent(BaseNodeData data, string eventName, object eventData, BasePackData pack, GraphRunner runner, string packInstanceID)
    {
    }

    private void PropagateSignal(MissionNode_R_Data node, SignalContext context, BasePackData pack)
    {
        EnqueueSignals(pack, node.OutPutNodeIDs, context);
    }

    private void SendUIRefreshSignal()
    {
        PostSystem.Instance.Send("UI_MISSION_REFRESH", null);
    }
}

}
