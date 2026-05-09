using System;
using System.Collections.Generic;
using UnityEngine;

namespace NekoGraph
{

// ═══════════════════════════════════════════════════════════════
// 权限系统 - 操作系统原理喵~ 🔐
// ═══════════════════════════════════════════════════════════════
//
// 【操作系统类比】
// ┌─────────────────────────────────────────────────────────────┐
// │  概念              │  操作系统          │  本系统           │
// ├─────────────────────────────────────────────────────────────┤
// │  执行主体          │  进程 (Process)    │  GraphInstanceSlot│
// │  主体标识          │  UID (用户 ID)     │  subjectLevel     │
// │  执行上下文        │  PCB (进程控制块)  │  EntityGraphContext│
// │  执行器            │  CPU              │  GraphRunner      │
// │  内存管理器        │  MMU (内存管理单元) │  GraphAnalyser   │
// │  地址空间          │  虚拟地址空间      │  PackTable        │
// │  代码段            │  Text Segment      │  NodeStrategy     │
// │  权限检查          │  内核权限校验      │  Resolve()        │
// │  访问级别          │  rwx (读写执行)    │  PackAccessLevel  │
// └─────────────────────────────────────────────────────────────┘
//
// 【权限模型】
// subjectLevel < ReadableFrom  →  Hidden (无权限，类似 chmod 000)
// subjectLevel < WritableFrom  →  ReadOnly (只读，类似 chmod 444)
// subjectLevel >= WritableFrom →  ReadWrite (读写，类似 chmod 644)
//
// 【设计原则】
// 1. 每个执行主体 (Player/AI/System) 有独立的"地址空间"(PackTable)
// 2. CPU(GraphRunner) 执行时携带 UID(_subjectLevel)
// 3. 所有内存访问 (Analyser API) 都要经过 MMU(Resolve) 的权限检查
// 4. 代码段 (NodeStrategy) 本身无状态，执行时携带 CPU 的 UID
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// 访问权限级别喵~ 📋
/// 【操作系统类比】类似 Unix 的 rwx 权限位
/// </summary>
public enum PackAccessLevel
{
    ReadWrite,  // 可读写 (类似 chmod 644)
    ReadOnly,   // 只读 (类似 chmod 444)
    Hidden      // 隐藏/无权限 (类似 chmod 000)
}

/// <summary>
/// 权限主体等级常量喵~ 🔑
/// 【操作系统类比】类似 UID 的分级
/// </summary>
public static class PackAccessSubjects
{
    public const int Player = 0;      // 玩家等级 (类似 root/UID 0)
    public const int EntitySystem = 200;  // ECS 系统等级 (单位创建/管理喵~)
    public const int AIMin = 100;     // AI 起始等级 (类似普通用户/UID 1000+)
    public const int SystemMin = 1000; // 系统起始等级 (类似系统进程/UID 10000+)
}

/// <summary>
/// ═══════════════════════════════════════════════════════════════
/// GraphHub - 图系统的全局调度中心喵~ 🎯
/// ═══════════════════════════════════════════════════════════════
///
/// 【操作系统类比】进程调度器 (Process Scheduler) + PCB 管理器
///
/// 职责：
/// 1. 管理所有执行主体的上下文 (EntityGraphContext)
/// 2. 每个上下文包含独立的"CPU"(Runner) 和"内存管理器"(Analyser)
/// 3. 驱动所有"CPU"的 Tick() 执行（类似时间片轮转调度）
///
/// 【架构设计】
/// ┌─────────────────────────────────────────────────────────────┐
/// │  GraphHub (调度器)                                          │
/// │    └── Dictionary<GraphInstanceSlot, EntityGraphContext>    │
/// │         ├── EntityGraphContext[Player] ← subjectLevel=0     │
/// │         │   ├── Runner (Player 的 CPU)                      │
/// │         │   └── Analyser (Player 的 MMU)                    │
/// │         ├── EntityGraphContext[AI_1] ← subjectLevel=100     │
/// │         │   ├── Runner (AI_1 的 CPU)                        │
/// │         │   └── Analyser (AI_1 的 MMU)                      │
/// │         └── ...                                             │
/// └─────────────────────────────────────────────────────────────┘
///
/// 【权限隔离】
/// - 每个主体的"地址空间"(PackTable) 相互隔离
/// - AI 无法直接访问 Player 的数据（除非 Pack 的 ReadableFrom 允许）
/// - 权限检查在"内存访问"(Analyser API) 时进行
/// ═══════════════════════════════════════════════════════════════
/// </summary>
public class GraphHub : SingletonMono<GraphHub>
{
    private readonly Dictionary<GraphInstanceSlot, EntityGraphContext> _contexts =
        new Dictionary<GraphInstanceSlot, EntityGraphContext>();
    private readonly Dictionary<Type, PackFacadeBase> _facades =
        new Dictionary<Type, PackFacadeBase>();

    public GraphAnalyser DefaultAnalyser => GetContext(GraphInstanceSlot.Player)?.Analyser;
    public GraphRunner DefaultRunner => GetContext(GraphInstanceSlot.Player)?.Runner;

    protected override void Awake()
    {
        base.Awake();
        InitializeAllContexts();
    }

    private void Update()
    {
        foreach (var context in _contexts.Values)
        {
            context.Runner.Tick();
        }
    }

    public EntityGraphContext GetContext(GraphInstanceSlot slot)
    {
        if (!_contexts.TryGetValue(slot, out var context))
        {
            context = new EntityGraphContext(slot);
            _contexts[slot] = context;
        }

        return context;
    }

    public void ClearFacadeBindings()
    {
        Debug.LogFormat(
            LogType.Log,
            LogOption.NoStacktrace,
            null,
            "[graph_hub] clear-facades count={0}",
            _facades.Count);

        foreach (var facade in _facades.Values)
        {
            facade?.ClearPackBinding();
        }

        _facades.Clear();
    }

    public void RegisterFacade(PackFacadeBase facade)
    {
        if (facade == null)
            return;

        _facades[facade.GetType()] = facade;
        Debug.LogFormat(
            LogType.Log,
            LogOption.NoStacktrace,
            null,
            "[graph_hub] register-facade type={0} pack={1}",
            facade.GetType().Name,
            facade.ResolvedPackID);
    }

    public T GetFacade<T>() where T : PackFacadeBase
    {
        return _facades.TryGetValue(typeof(T), out var facade) ? facade as T : null;
    }

    public Dictionary<string, BasePackData> GetPackTable(GraphInstanceSlot slot)
    {
        return GetContext(slot).PackTable;
    }

    [Obsolete("GetPackDataDict 已更名为 GetPackTable。", false)]
    public Dictionary<string, BasePackData> GetPackDataDict(GraphInstanceSlot slot)
    {
        return GetPackTable(slot);
    }

    public int GetSubjectLevel(GraphInstanceSlot slot)
    {
        return (int)slot;
    }

    public PackAccessLevel GetPackAccessLevel(GraphInstanceSlot slot, BasePackData pack)
    {
        return GetPackAccessLevel(GetSubjectLevel(slot), pack);
    }

    public PackAccessLevel GetPackAccessLevel(int subjectLevel, BasePackData pack)
    {
        if (pack == null)
            return PackAccessLevel.Hidden;

        if (subjectLevel < pack.ReadableFrom)
            return PackAccessLevel.Hidden;

        if (subjectLevel < pack.WritableFrom)
            return PackAccessLevel.ReadOnly;

        return PackAccessLevel.ReadWrite;
    }

    public void ApplyUser(IUserPackData user)
    {
        InitializeAllContexts();

        if (user == null)
        {
            foreach (var context in _contexts.Values)
            {
                context.SetPackTable(new Dictionary<string, BasePackData>());
                context.Analyser.RebuildIndex();
            }
            return;
        }

        foreach (GraphInstanceSlot slot in Enum.GetValues(typeof(GraphInstanceSlot)))
        {
            var context = GetContext(slot);
            Dictionary<string, BasePackData> packTable = slot == GraphInstanceSlot.Player
                ? user.GetPlayerPackDict()
                : user.GetEntityPackDict(slot, createIfMissing: true);

            context.SetPackTable(packTable);
            context.Analyser.RebuildIndex();
            context.Runner.OnPackTableLoaded(packTable);
        }
    }

    private void InitializeAllContexts()
    {
        foreach (GraphInstanceSlot slot in Enum.GetValues(typeof(GraphInstanceSlot)))
        {
            GetContext(slot);
        }
    }
}

}
