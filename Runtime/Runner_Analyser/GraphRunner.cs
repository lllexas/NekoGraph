using System;
using System.Collections.Generic;
using UnityEngine;

namespace NekoGraph
{

// ═══════════════════════════════════════════════════════════════
// GraphRunner - 图执行器喵~ 💓
// ═══════════════════════════════════════════════════════════════
//
// 【操作系统类比】CPU + 进程执行环境 🖥️
// ┌─────────────────────────────────────────────────────────────┐
// │  概念              │  操作系统          │  GraphRunner      │
// ├─────────────────────────────────────────────────────────────┤
// │  执行器            │  CPU 核心          │  GraphRunner      │
// │  进程 UID          │  UID/EUID         │  _subjectLevel    │
// │  指令集            │  机器指令          │  NodeStrategy     │
// │  指令执行          │  取指 - 译码 - 执行  │  ProcessSignal()  │
// │  信号/中断         │  Interrupt/Signal │  SignalContext    │
// │  地址空间          │  页表基址寄存器    │  PackTable        │
// │  时间片调度        │  Tick/Scheduler   │  Tick()           │
// └─────────────────────────────────────────────────────────────┘
//
// 【执行模型】
// 1. 每个 GraphRunner 实例是一个独立的"CPU"，有自己的"UID"(_subjectLevel)
// 2. CPU 执行"指令"(NodeStrategy) 时，硬件自动携带当前进程的 UID
// 3. "指令"本身是无状态的（类似机器码），执行时才有权限概念
// 4. 当"指令"需要访问内存时，会触发"内存异常"(Analyser API)，由 MMU 检查权限
//
// 【与 GraphAnalyser 的关系】
// ┌─────────────────────────────────────────────────────────────┐
// │  GraphRunner (CPU)          │  GraphAnalyser (MMU)          │
// │  • 驱动信号流动              │  • 存储空间结构               │
// │  • Tick() 驱动执行           │  • BFS 路径查询               │
// │  • 携带 _subjectLevel        │  • 检查 _subjectLevel         │
// │  • 调用 NodeStrategy         │  • Resolve() 权限网关         │
// └─────────────────────────────────────────────────────────────┘
//
// 【权限传递链】
// EntityGraphContext (PCB)
//   ├── Runner._subjectLevel = 100 (AI_1 的 UID)
//   │     ↓ 执行时携带
//   │   NodeStrategy.OnSignalEnter()
//   │     ↓ 需要访问数据
//   │   Analyser.GetNode(packID, path, subjectLevel: 100)
//   │     ↓ 权限检查
//   │   Resolve() → subjectLevel(100) >= ReadableFrom(0)? OK!
//   └── Analyser._subjectLevel = 100 (AI_1 的 MMU)
//
// 【设计原则】
// 1. CPU 和 MMU 分离：Runner 专注执行，Analyser 专注访问控制
// 2. 权限与执行绑定：_subjectLevel 跟随 Runner，不是 NodeStrategy
// 3. 最小权限原则：代码 (NodeStrategy) 本身无权限，执行时才有
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// ═══════════════════════════════════════════════════════════════
/// GraphRunner - 图运行器（CPU）喵~ 🖥️
/// ═══════════════════════════════════════════════════════════════
///
/// 职责：
/// 1. 驱动信号在图中的流动（类似 CPU 执行指令）
/// 2. 管理所有已加载的 Pack 实例（类似进程的地址空间）
/// 3. 持有执行主体的等级（类似进程的 UID）
///
/// 生命周期：
/// - 每个 GraphInstanceSlot 对应一个 GraphRunner 实例
/// - Player、AI_1、AI_2 等各有独立的 Runner
/// - Runner 和 Analyser 一对一绑定，共享同一个 subjectLevel
/// ═══════════════════════════════════════════════════════════════
/// </summary>
public class GraphRunner
{
    /// <summary>
    /// Legacy singleton compatibility shim.
    /// New code should use GraphHub.Instance.DefaultRunner.
    /// </summary>
    [Obsolete("GraphRunner.Instance 已迁移。新代码请使用 GraphHub.Instance.DefaultRunner。", false)]
    public static GraphRunner Instance => GraphHub.Instance?.DefaultRunner;

    /// <summary>
    /// 主体等级（类比操作系统的进程 UID）喵~ 🔑
    /// 每个 GraphRunner 实例对应一个执行主体（Player/AI/System）
    /// 
    /// 【操作系统类比】
    /// - 类似 Linux 的 UID (User ID)
    /// - CPU 执行指令时，硬件自动携带当前进程的 UID
    /// - 系统调用时，内核用 UID 检查权限
    /// 
    /// 【使用场景】
    /// - NodeStrategy 执行时，通过当前 runner.GetSubjectLevel() 获取
    /// - 调用 Analyser API 时传入，用于权限校验
    /// - 默认值来自 EntityGraphContext 的 GraphInstanceSlot
    /// </summary>
    private readonly int _subjectLevel;

    /// <summary>
    /// 共享 Pack 表喵~
    /// Key: PackID, Value: BasePackData 本体
    /// 通常指向 UserModel.PackDataDict（GraphRunner 只是引用，不拥有所有权）
    /// 生命周期跟随 UserModel，GraphRunner 只是个"打工人"喵~
    /// </summary>
    private Dictionary<string, BasePackData> _packTable;

    public Dictionary<string, BasePackData> PackTable => _packTable;

    [Obsolete("PersistentGuidToInstancedPackDict 语义已统一为 PackTable。新代码请使用 PackTable。", false)]
    public Dictionary<string, BasePackData> PersistentGuidToInstancedPackDict => _packTable;

    /// <summary>
    /// PackID 缓存列表 - 用于安全遍历，防止"回手掏"导致字典修改异常喵~
    /// </summary>
    [NonSerialized]
    private List<string> _packIdCache = new List<string>();

    /// <summary>
    /// 最大信号传播深度（防止死循环）喵~
    /// </summary>
    public int MaxSignalDepth = 100;

    /// <summary>
    /// 是否启用调试日志喵~
    /// </summary>
    public bool EnableDebugLog = false;

    /// <summary>
    /// 构造函数喵~ 🔧
    /// </summary>
    /// <param name="packTable">Pack 数据字典（类似进程的地址空间）</param>
    /// <param name="subjectLevel">
    /// 主体等级（类比进程 UID）喵~
    /// - Player = 0 (类似 root)
    /// - AI = 100+ (类似普通用户)
    /// - System = 1000+ (类似系统进程)
    /// </param>
    public GraphRunner(Dictionary<string, BasePackData> packTable = null, int subjectLevel = PackAccessSubjects.Player)
    {
        _subjectLevel = subjectLevel;
        SetPackTable(packTable);
    }

    /// <summary>
    /// 获取当前 Runner 的主体等级喵~ 🔑
    /// 【操作系统类比】类似 getuid() 系统调用
    /// 
    /// 【使用示例】
    /// var subjectLevel = runner.GetSubjectLevel();
    /// var node = analyser.GetNode(packID, path, subjectLevel);
    /// </summary>
    public int GetSubjectLevel() => _subjectLevel;

    private void StartCompatibility()
    {
        // 注册到 PostSystem 接收全局事件
        PostSystem.Instance.Register(this);
    }

    public void Tick()
    {
        // 驱动所有 Pack 中的信号步进喵~
        TickAllPacks();
    }

    public void Dispose()
    {
        PackTable?.Clear();
    }

    // =========================================================
    // 核心 API - Pack 数据管理
    // =========================================================

    /// <summary>
    /// 设置 Pack 数据字典引用喵~
    /// 通常指向 UserModel.PackDataDict
    /// </summary>
    public void SetPackTable(Dictionary<string, BasePackData> packTable)
    {
        _packTable = packTable ?? new Dictionary<string, BasePackData>();
    }

    [Obsolete("SetPackDataDict 已更名为 SetPackTable。", false)]
    public void SetPackDataDict(Dictionary<string, BasePackData> dict)
    {
        SetPackTable(dict);
    }

    public void OnPackTableLoaded(Dictionary<string, BasePackData> packTable)
    {
        SetPackTable(packTable);

        foreach (var pack in PackTable.Values)
        {
            if (pack != null && !pack.HasStarted && !string.IsNullOrEmpty(pack.RootNodeId))
            {
                pack.ActiveSignals.Enqueue(new SignalContext(pack.RootNodeId, null));
                pack.HasStarted = true;
            }
        }
    }

    /// <summary>
    /// 读档/新档后由 SaveManager 直接调用，完成引用挂接 + 启动未启动的 Pack 喵~
    /// </summary>
    [Obsolete("OnPackDataDictLoaded 已更名为 OnPackTableLoaded。", false)]
    public void OnPackDataDictLoaded(Dictionary<string, BasePackData> dict)
    {
        OnPackTableLoaded(dict);
    }

    public void OnUserLoaded(Dictionary<string, BasePackData> packTable)
    {
        // 1. 直接引用 PackTable，零副本喵~
        _packTable = packTable;

        // 2. 扫描所有 Pack，保留挂起信号并启动未启动的
        foreach (var pack in packTable.Values)
        {
            if (pack.SuspendedSignals != null && pack.SuspendedSignals.Count > 0)
            {
                if (EnableDebugLog)
                    Debug.Log($"[GraphRunner] Pack '{pack.PackID}' 保留了 {pack.SuspendedSignals.Count} 个挂起信号喵~");
            }

            // 2.1 启动未启动的 Pack
            if (!pack.HasStarted && !string.IsNullOrEmpty(pack.RootNodeId))
            {
                pack.ActiveSignals.Enqueue(new SignalContext(pack.RootNodeId, null));
                pack.HasStarted = true;
            }
        }
    }

    /// <summary>
    /// 加载 Pack，返回 PackID 作为句柄喵~
    /// </summary>
    /// <param name="pack">Pack 数据</param>
    /// <returns>PackID，用作统一句柄</returns>
    public string LoadPack(BasePackData pack)
    {
        if (pack == null)
        {
            Debug.LogError("[GraphRunner] 尝试加载空的 Pack 喵~");
            return null;
        }

        if (string.IsNullOrEmpty(pack.PackID))
        {
            Debug.LogError("[GraphRunner] Pack 缺少 PackID，无法加载喵~");
            return null;
        }

        // 统一以 PackID 作为共享表主键
        PackTable[pack.PackID] = pack;

        if (EnableDebugLog)
        {
            Debug.Log($"[GraphRunner] Pack 已加载：{pack.PackID}");
        }

        return pack.PackID;
    }

    /// <summary>
    /// 卸载指定 PackID 的 Pack 喵~
    /// </summary>
    public void UnloadPack(string packID)
    {
        if (string.IsNullOrEmpty(packID)) return;

        // 清理该 Pack 的信号（如果有）
        if (PackTable.TryGetValue(packID, out var pack))
        {
            pack.ActiveSignals.Clear();
        }

        PackTable.Remove(packID);

        if (EnableDebugLog)
        {
            Debug.Log($"[GraphRunner] Pack 已卸载：{packID}");
        }
    }

    /// <summary>
    /// 卸载指定 PackID 喵~
    /// </summary>
    public void UnloadPacks(string packID)
    {
        UnloadPack(packID);
    }

    // =========================================================
    // 核心 API - 信号驱动
    // =========================================================

    /// <summary>
    /// 向指定 Pack 注入信号喵~
    /// </summary>
    public void InjectSignal(string packID, SignalContext signal)
    {
        if (PackTable == null) return;
        if (!PackTable.TryGetValue(packID, out var pack)) return;

        pack.ActiveSignals.Enqueue(signal);
    }

    /// <summary>
    /// 从 Root 节点注入信号喵~（便捷方法）
    /// </summary>
    public void InjectSignalFromRoot(string packID, object args = null)
    {
        if (PackTable == null) return;
        if (!PackTable.TryGetValue(packID, out var pack)) return;
        if (string.IsNullOrEmpty(pack.RootNodeId))
        {
            Debug.LogWarning($"[GraphRunner] Pack 没有 RootNodeId，无法注入信号喵~");
            return;
        }

        var signal = new SignalContext(pack.RootNodeId, args);
        pack.ActiveSignals.Enqueue(signal);
    }

    public bool ResumeSuspendedSignalToTarget(string packID, string signalId, string sourceNodeId, string targetNodeId)
    {
        if (PackTable == null || string.IsNullOrWhiteSpace(packID))
            return false;

        if (!PackTable.TryGetValue(packID, out var pack) || pack == null)
        {
            Debug.LogWarning($"[GraphRunner] ResumeSuspendedSignalToTarget 失败：Pack '{packID}' 不存在");
            return false;
        }

        if (string.IsNullOrWhiteSpace(signalId) || !pack.SuspendedSignals.TryGetValue(signalId, out var signal) || signal == null)
        {
            Debug.LogWarning($"[GraphRunner] ResumeSuspendedSignalToTarget 失败：Signal '{signalId}' 不存在于挂起字典");
            return false;
        }

        if (string.IsNullOrWhiteSpace(sourceNodeId) || signal.CurrentNodeId != sourceNodeId)
        {
            Debug.LogWarning($"[GraphRunner] ResumeSuspendedSignalToTarget 失败：Signal '{signalId}' 当前节点是 '{signal.CurrentNodeId}'，不是预期的 '{sourceNodeId}'");
            return false;
        }

        if (string.IsNullOrWhiteSpace(targetNodeId) || !pack.Nodes.ContainsKey(targetNodeId))
        {
            Debug.LogWarning($"[GraphRunner] ResumeSuspendedSignalToTarget 失败：目标节点 '{targetNodeId}' 不存在");
            return false;
        }

        if (pack.Nodes.TryGetValue(sourceNodeId, out var sourceNode))
            sourceNode.IsChecked = true;

        signal.RecordConnection(new ConnectionData(sourceNodeId, -1, targetNodeId, -1));
        signal.CurrentNodeId = targetNodeId;
        pack.SuspendedSignals.Remove(signalId);
        pack.ActiveSignals.Enqueue(signal);

        Debug.LogFormat(
            LogType.Log,
            LogOption.NoStacktrace,
            null,
            "[graph_runner] resume-suspended-signal pack={0} signal={1} sourceNode={2} targetNode={3}",
            packID,
            signalId,
            sourceNodeId,
            targetNodeId);
        return true;
    }

    /// <summary>
    /// 向所有 Pack 广播信号喵~
    /// </summary>
    public void BroadcastSignal(SignalContext signal)
    {
        if (PackTable == null) return;

        foreach (var pack in PackTable.Values)
        {
            pack.ActiveSignals.Enqueue(signal.Clone());
        }
    }

    /// <summary>
    /// 驱动所有 Pack 的信号步进喵~
    /// 使用缓存列表防止"回手掏"导致字典修改异常喵~
    /// </summary>
    private void TickAllPacks()
    {
        if (PackTable == null) return;

        // 使用缓存列表安全遍历，防止信号处理过程中卸载 Pack 导致字典修改喵~
        _packIdCache.Clear();
        foreach (var key in PackTable.Keys)
        {
            _packIdCache.Add(key);
        }

        foreach (var packID in _packIdCache)
        {
            if (PackTable.TryGetValue(packID, out var pack))
            {
                if (pack.ActiveSignals.Count > 0)
                {
                    TickPack(pack, packID);
                }
            }
        }
    }

    /// <summary>
    /// 驱动单个 Pack 的信号步进喵~
    /// 限制每帧处理的信号数量，防止卡顿
    /// </summary>
    private void TickPack(BasePackData pack, string packID)
    {
        int signalsToProcess = Math.Min(pack.ActiveSignals.Count, 50);

        for (int i = 0; i < signalsToProcess; i++)
        {
            if (pack.ActiveSignals.Count == 0) break;

            var signal = pack.ActiveSignals.Dequeue();

            ProcessSignal(signal, pack, packID);
        }
    }

    /// <summary>
    /// 处理单个信号的传播喵~
    /// 包含深度检查，防止死循环喵~
    /// </summary>
    private void ProcessSignal(SignalContext signal, BasePackData pack, string packID)
    {
        // 深度检查：防止死循环喵~
        if (signal.Depth > MaxSignalDepth)
        {
            if (EnableDebugLog)
            {
                Debug.LogWarning($"[GraphRunner] 信号深度超过上限 ({MaxSignalDepth})，已强制丢弃喵~ Signal: {signal.CurrentNodeId}");
            }
            return;
        }

        // 如果 CurrentNodeId 为空，直接丢弃信号喵~
        if (string.IsNullOrEmpty(signal.CurrentNodeId))
        {
            if (EnableDebugLog)
            {
                Debug.LogWarning($"[GraphRunner] 信号没有 CurrentNodeId，已丢弃喵~");
            }
            return;
        }

        // 直接从 pack.Nodes 字典查找节点喵~
        if (pack.Nodes.TryGetValue(signal.CurrentNodeId, out var currentNode))
        {
            var strategy = GetStrategy(currentNode);
            if (strategy != null)
            {
                strategy.OnSignalEnter(currentNode, signal, pack, this, packID);
            }
        }
        else
        {
            if (EnableDebugLog)
            {
                Debug.LogWarning($"[GraphRunner] 节点不存在：{signal.CurrentNodeId}");
            }
        }
    }

    // =========================================================
    // 辅助方法
    // =========================================================

    /// <summary>
    /// 获取节点的策略处理器喵~
    /// 直接从 NodeStrategyFactory 获取（工厂已按类型缓存）
    /// </summary>
    private NodeStrategy GetStrategy(BaseNodeData data)
    {
        if (data == null) return null;
        return NodeStrategyFactory.GetStrategy(data);
    }

    /// <summary>
    /// 清理指定 instance 的所有活跃监听器（TriggerNode 的响应式监听）喵~
    /// </summary>
    private void CleanupInstanceListeners(string packID)
    {
        // 通过 TriggerNodeStrategy 单例调用清理方法
        TriggerNodeStrategy.Instance?.ForceDeactivate(packID);
    }

    /// <summary>
    /// 获取调试信息喵~
    /// 包含信号积压警告喵~
    /// </summary>
    public string GetDebugInfo()
    {
        var info = new System.Text.StringBuilder();
        info.AppendLine($"[GraphRunner] Pack 数据：{PackTable?.Count ?? 0}");

        if (PackTable != null)
        {
            foreach (var kvp in PackTable)
            {
                var pack = kvp.Value;
                int signalCount = pack.ActiveSignals.Count;
                
                // 信号积压警告喵~
                if (signalCount > 20)
                {
                    info.AppendLine($"  ⚠️ [警告] 信号积压！PackID: {kvp.Key}, Count: {signalCount}");
                }
                else
                {
                    info.AppendLine($"  - {kvp.Key}: Nodes={pack.Nodes.Count}, Signals={signalCount}");
                }
            }
        }

        return info.ToString();
    }
}

}
