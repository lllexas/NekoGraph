using UnityEngine;

/// <summary>
/// 【示例】特性驱动的事件定义喵~
/// 演示如何使用 [TriggerEventInfo] 在任意静态类中定义新事件喵~
/// 
/// 使用方式：
/// 1. 在任意静态类上添加 [TriggerEventInfo] 特性
/// 2. 可选：提供 Handle 方法作为默认处理器
/// 3. 发送事件：PostOffice.Send("PlayerLevelUp", 5)
/// </summary>
[TriggerEventInfo(
    "PlayerLevelUp", 
    EventProtocol.Numeric, 
    "⬆️ 玩家升级", 
    "玩家",
    Tooltip = "当玩家等级提升时触发，携带新等级数值喵~"
)]
public static class PlayerEventHandlers
{
    /// <summary>
    /// 默认事件处理器喵~
    /// 当 PostOffice.Send("PlayerLevelUp", ...) 被调用时会自动执行这个方法喵~
    /// </summary>
    public static void Handle(object payload)
    {
        if (payload is int level)
        {
            Debug.Log($"[PlayerEventHandlers] 玩家升级到等级 {level} 喵~！");
        }
    }
}

/// <summary>
/// 【示例 2】带自定义 Handler 方法名的事件定义喵~
/// </summary>
[TriggerEventInfo(
    "AchievementUnlocked", 
    EventProtocol.String, 
    "🏆 成就解锁", 
    "成就",
    Tooltip = "当玩家解锁成就时触发，携带成就 ID 喵~",
    HandlerMethodName = "OnAchievementUnlocked"
)]
public static class AchievementEvents
{
    /// <summary>
    /// 自定义名称的事件处理器喵~
    /// </summary>
    public static void OnAchievementUnlocked(object payload)
    {
        if (payload is string achievementId)
        {
            Debug.Log($"[AchievementEvents] 成就解锁：{achievementId} 喵~！");
        }
    }
}

/// <summary>
/// 【示例 3】实体协议的事件定义喵~
/// </summary>
[TriggerEventInfo(
    "EnemyDefeated", 
    EventProtocol.Entity, 
    "⚔️ 敌人被击败", 
    "战斗",
    Tooltip = "当敌人被击败时触发，携带敌人实体句柄喵~"
)]
public static class CombatEvents
{
    public static void Handle(object payload)
    {
        // 这里可以处理敌人被击败的逻辑喵~
        Debug.Log($"[CombatEvents] 敌人被击败喵~！");
    }
}
