# NekoGraph

**NekoGraph 是 Unity 里的客户端业务运行时内核，核心能力是"玩家游戏进度管理"。**

它用**图**来表达玩家进度的推进路径：逻辑节点控制流程，业务节点处理资源，连线是分支条件，`SignalContext`
在图中传播，遇到节点触发对应的逻辑。需要等待玩家输入时，信号挂起；完成后恢复，继续推进。

**关键前提：NekoGraph 不内置任何业务类型。**
框架只提供"图 + 信号 + 挂起恢复"这套运行时内核——你的项目自己定义节点上绑什么资源、触发什么逻辑。

---

## 核心抽象：图

NekoGraph 的一切围绕**图**展开。

### 节点

节点分两类：

**逻辑节点**：直接执行逻辑，不绑定资源。框架预置了一些基础类型（Trigger 事件监听、Comparer 数值/字符串比较、Command 指令执行），但项目可以自定义扩展更多事件类型、比较方式、命令参数，框架提供类型安全检测。

- `Trigger`：监听外部事件。框架预置基础事件，项目可自定义事件类型（`MissionCompleted`、`ItemAcquired`）、事件对象
- `Comparer`：判断条件。框架预置数值/字符串比较，项目可自定义比较对象、比较方式（对象属性、集合包含等）
- `Command`：执行指令。框架预置基础命令，项目可自定义命令，命令可要求输入特定类型的对象参数

**业务节点（VFSNode）**：绑定资源，走后缀协议分发到 Handler。

- 绑定 `ScriptableObject`、`JSON`、`CSV` 等资源
- 被信号流触发时，框架根据后缀找到对应的 Execute/Query Handler

**为什么需要两种节点？**

逻辑节点是图灵完备的，可以表达任何控制流。但游戏中有大量具体业务场景——播放一段对话、生成一个实体、发送一条消息、解锁一个科技——它们的共性是：跨越多个系统、需要等待玩家输入、相对独立可复用。

这些场景纯粹用逻辑节点表达，就好像削足适履：如果把"播放对话"的每一行字、每个角色表情、每个音效都拆成逻辑节点，图会爆炸。如果把它们全部硬编码在 Command 节点里，又失去了复用性和可配置性。

VFSNode 是**"代码化业务"的容器**——图里只放一个节点表达"这里要触发一个业务事件"，具体逻辑写在 Handler 里。这是**图形化（图管流程）和代码化（Handler 写业务）之间的平衡点**。

### 连线

连线表达节点之间的推进关系：

- 单向推进：A 完成后到 B
- 分支：A 完成后根据条件到 B 或 C
- 汇合：B 和 C 都完成后到 D

### 信号

`SignalContext` 是图中流动的"执行令牌"，同时也是节点之间的**对象管道**。

作为执行令牌：它标识当前执行位置，从根节点出发，沿着连线传播，遇到节点时触发对应的逻辑。

作为对象管道：它的 `Args` 属性可以在 Handler 之间传递数据对象。上游节点写入数据，下游节点读取消费：

```csharp
// 上游 .command 节点写入 SpawnRequestArgs
context.Args = new SpawnRequestArgs { GridPosition = pos, Faction = faction };

// 下游 .entity 节点读取
var args = context.Args as SpawnRequestArgs;
EntitySystem.Instance.CreateEntityFromSO(bp, args.GridPosition, args.Faction);
```

### 挂起与恢复

当节点需要等待玩家输入时，信号不继续传播，而是**挂起**到 `SuspendedSignals`
字典。玩家完成交互后，信号**恢复**，从挂起点继续传播。

**存档就是保存当前信号在图中的位置**——包括活跃信号和挂起信号。读档后精确恢复，不会丢状态。

---

## 你遇到过这些问题吗？

**玩家进度写在 C# 里，改进度要改代码**

```csharp
// 传统做法：进度逻辑硬编码
if (player.HasItem("Keycard")) {
    if (player.Affection > 50) {
        ShowGoodEnding();
    } else {
        ShowBadEnding();
    }
} else {
    ShowLocked();
}
```

**进度和资源混在一起，改进度要改资源，改资源要改进度**

```csharp
// 实体生成逻辑和实体数据耦合在同一个 Spawner 里
public class Spawner : MonoBehaviour
{
    [SerializeField] private List<BlueprintSO> blueprints;
    public void Spawn(string id) { /* 逻辑和数据焊死 */ }
}
```

**每种进度事件都要写一套管理器**

```csharp
// 对话有 DialogManager，实体有 EntityManager，科技有 TechManager...
// 每个管理器都有自己的加载、执行、存档逻辑
// 新增一种进度事件 = 从零写一个管理器 + 一套 UI + 一套存档格式
```

**UI 想展示进度数据，但数据被锁在业务系统里**

```csharp
// 仓库 UI 想显示"有哪些实体"，只能从 EntityManager 里硬拆接口
// 邮箱 UI 想显示"有哪些消息"，只能从 MessageSystem 里硬拆接口
```

**异步进度推进嵌套成回调地狱**

```csharp
// 触发事件 → 播放内容 → 等玩家选择 → 根据选择发消息 → 继续推进
coroutine = StartCoroutine(PlayEvent(() => {
    ShowChoice(new[] {"接受", "拒绝"}, index => {
        if (index == 0) {
            SendMessage(() => { Continue(); });
        } else {
            Abort();
        }
    });
}));
```

**存档不知道存什么**

```csharp
// 存了 currentMissionIndex，但恢复后进度状态对不上
// 存了 unlockedTechs 列表，但不知道科技是怎么解锁的
// 存了 entityPositions，但实体和蓝图的关联丢了
```

---

## NekoGraph 怎么做？

### 1. 用"图"表达玩家进度，不改代码就能改流程

在编辑器里画一张图：节点是进度事件，连线是分支条件。运行时信号在图中传播。

```
[Trigger:MissionComplete] --→ [Comparer:str_match] --→ [VFS:发放奖励]
                                                            ↓ 条件A
                                                  [VFS:解锁科技] --→ [Leaf]
                                                            ↓ 条件B
                                                  [VFS:生成实体] --→ [Leaf]
```

改进度 = 改图，策划可以自己操作，不需要程序员改代码。

### 2. VFS 业务节点绑定资源，运行时自动分发

逻辑节点（Trigger、Comparer、Command）直接执行内置逻辑。业务节点（VFSNode）绑定资源，被信号流触发时框架根据后缀自动找到对应的 Handler 处理：

```
节点 A → 绑定资源: ItemSO
节点 B → 绑定资源: {"title":"战报", "body":"..."}
节点 C → 绑定资源: TechSO
```

**新增一种进度事件 = 写一个 Handler，无需改引擎。**

### 3. 运行时执行和前端查询分离

同一个资源类型有两个入口：

- **执行入口**：信号流到达节点时触发，有副作用（生成实体、投递消息、解锁科技）
- **查询入口**：前端需要展示时调用，无副作用（返回属性面板、列表项）

这样资源既可以被图执行（战场上生成实体），又可以被独立查询（仓库里浏览属性），两者互不干扰。

### 4. 挂起与恢复统一所有异步等待

需要等待玩家输入的节点，信号挂起。玩家完成后恢复，继续传播。

**存档时挂起的信号被完整序列化**，读档后自动恢复，不会丢状态。

---

## 代码对比：Before vs After

### Before：传统进度系统

```csharp
public class GameManager : MonoBehaviour
{
    [SerializeField] private List<EventSO> events;
    private int currentIndex;
    private System.Action onComplete;

    public void Play(EventSO evt, System.Action onDone)
    {
        onComplete = onDone;
        currentIndex = 0;
        ShowNext();
    }

    void ShowNext()
    {
        if (currentIndex >= events.Count) {
            onComplete?.Invoke();
            return;
        }
        ui.Show(events[currentIndex], () => {
            currentIndex++;
            ShowNext();
        });
    }
}
```

问题：

- 改进度要改代码
- 进度和资源耦合
- UI 和业务系统焊死
- 存档只能存 `currentIndex`，恢复后状态对不上

### After：NekoGraph

**定义资源协议（一次，后续复用）：**

```csharp
// 假设你的项目定义了 .myevent 后缀
[VFSResource(".myevent", typeof(MyEventSO))]
public static class MyEventResource
{
    [VFSExecute]
    public static HandleResult Execute(
        VFSResolvedContent content,
        SignalContext context,
        BasePackData pack,
        GraphRunner runner,
        string packIDKey,
        System.Action continueAction)
    {
        var evt = content.GetPayload<MyEventSO>();
        MyEventPlayer.Instance.TryPlay(evt, continueAction);
        return HandleResult.Wait;  // 挂起，等播放完成自动恢复
    }
}
```

**前端自治播放（不引用任何 NekoGraph 类型）：**

```csharp
public class EventPanel : MonoBehaviour
{
    void Start()
    {
        PostSystem.Instance.Subscribe("播放事件", OnPlayEvent);
    }

    void OnPlayEvent(object data)
    {
        // 只管怎么显示，不管进度逻辑
        var evt = (EventLineData)data;
        textMesh.text = evt.text;
    }
}
```

**进度数据 = 一张图，编辑器里配置：**

```
节点: intro
绑定资源: IntroSequence.asset

节点: choice_branch
绑定资源: {"options": ["接受", "拒绝"]}
连线: 接受 → reward_node
连线: 拒绝 → abort_node
```

好处：

- 改进度 = 改图，策划可以自己操作
- 前端不引用 NekoGraph，可以独立测试
- 存档自动保存信号状态（包括挂起的选择）
- 新增进度事件类型 = 写一个 Handler，零代码改动

---

## 架构一览

```
┌─────────────────────────────────────────────┐
│              NekoGraph 后端                  │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  │
│  │GraphHub  │  │GraphRunner│  │GraphAnalyser│ │
│  │ 进程调度  │  │ 信号传播  │  │ 权限/VFS  │  │
│  └────┬─────┘  └────┬─────┘  └──────────┘  │
│       └─────────────┘                       │
│              ↓                              │
│         节点（绑定资源）                     │
│              ↓                              │
│         Handler（执行 / 查询）               │
│         return Push / Wait / Error          │
└──────────────┬──────────────────────────────┘
               │ 异步回调 (continueAction)
               ↓
┌─────────────────────────────────────────────┐
│              前端表现层（任意 UI 框架）         │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  │
│  │EventSystem│ │DialogPlayer│ │SocialBox │  │
│  │ 事件管理  │  │ 对话播放器  │  │ 消息管理  │  │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  │
│       ↓ PostSystem 事件总线                 │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  │
│  │EventPanel │ │DialogueUI│  │MailboxUI │  │
│  │ 事件面板  │  │ 对话面板  │  │ 邮箱面板  │  │
│  └──────────┘  └──────────┘  └──────────┘  │
└─────────────────────────────────────────────┘
```

**关键分层原则：**

- **后端不引用前端**：`GraphRunner` 不知道 `EventPanel` 存在
- **前端不引用后端**：`EventPanel` 不知道 `GraphRunner` 存在
- **通信靠事件总线**：`PostSystem.Send("播放事件", data)`
- **资源靠路径**：VFS 路径取代直接 SO 引用

---

## 核心概念速查

### Pack = 图 + 静态盘

一个 Pack 有两个身份，取决于你怎么看它。

**身份一：运行图**

当你把 Pack 加载进 `GraphRunner`，它就是一张有向图。`SignalContext` 从根节点出发，沿着 `OutputConnections`
传播，遇到节点就触发对应的 Handler。

```
[Trigger:MissionComplete] --→ [Comparer:str_match] --→ [VFS:发放奖励]
                                                            ↓ 条件A
                                                  [VFS:解锁科技] --→ [Leaf]
                                                            ↓ 条件B
                                                  [VFS:生成实体] --→ [Leaf]
```

**身份二：静态盘**

从设计意图上，Pack 也应该能作为一棵 VFS 资源树被独立查询、读写、持久化——不依赖 `GraphRunner` 是否在执行它。

```
player_data/
├── inventory/
│   ├── sword_01.item
│   └── shield_02.item
└── archives/
    └── mission_log.archive
```

**两种"图"的语义差异**

虽然都叫"图"，但 VFS 和 GraphRunner 面对的图在结构语义上完全不同：

|              | VFS（静态盘）                       | Graph（运行时）                   |
| ------------ | ----------------------------------- | --------------------------------- |
| **结构**     | 树形，偏扁平                        | 网状，偏深                        |
| **方向**     | 单向（父→子）                       | 可能有交叉、合并、循环            |
| **语义**     | "资源在哪里"                        | "流程怎么走"                      |
| **遍历**     | 路径访问 `/inventory/sword_01.item` | 信号流沿 `OutputConnections` 传播 |
| **生命周期** | 持久化存储                          | 运行时临时状态                    |

同一个节点在 **VFS 语义** 上是树中的一个文件，在 **Graph 语义** 上是有向图中的一个执行点。

> **当前实现的局限**：Pack 的数据结构和 `GraphRunner` 的运行时状态（`SuspendedSignals`、`ActiveSignals`
> 等）目前是直接混在同一个对象里的，并没有拆分为"纯数据 Pack"和"运行时 Pack 实例"。这意味着当前你无法在没有
> `GraphRunner` 的情况下独立查询一个 Pack 的 VFS 树。这个分离是未来的完善方向。

### 节点与资源绑定

逻辑节点（Trigger、Comparer、Command）直接执行内置逻辑，不绑定资源。

业务节点（VFSNode）本身不决定行为，行为由绑定的资源类型决定。运行时通过**后缀协议**将资源分发到对应的 Handler：

```
节点 → 后缀: .mytype → 绑定资源: MySO
         ↓
    VFSResourceRegistry 查找 .mytype 对应的 Handler
         ↓
    调用 Execute（信号流触发）或 Query（前端查询）
```

### Wait / Resume

节点需要等待玩家输入时：

1. Handler 返回 `HandleResult.Wait`
2. 当前信号以 `SignalId` 挂起到 `SuspendedSignals` 字典
3. 玩家完成后调用 `runner.ResumeSuspendedSignalToTarget(packID, signalId, sourceNode, targetNode)`
4. 信号从字典移除，重新进入传播队列

**存档时 `SuspendedSignals` 被完整 JSON 序列化**，读档后自动恢复。

### Execute vs Query —— 为什么有且只有两个

|                | Execute                                        | Query                    |
| -------------- | ---------------------------------------------- | ------------------------ |
| **问题**       | "让它跑起来"                                   | "看看它是什么"           |
| **副作用**     | 有（生成实体、投递消息、解锁条目）             | 无（只读描述）           |
| **参与图调度** | 是（返回 Push/Wait/Error）                     | 否                       |
| **调用方**     | `GraphRunner`，信号流入节点时                  | 前端门面（CLI、UI 面板） |
| **参数**       | `SignalContext`、`BasePackData`、`GraphRunner` | `VFSQueryContext`        |

**为什么只有两个？**

任何资源在运行时只面临这两种需求，不存在第三种：

- 要么需要被执行（有副作用，改变游戏状态）
- 要么需要被查询（无副作用，获取信息展示）

两者正交：

- **只有 Execute**：纯触发器，信号流到达时触发一次即可
- **只有 Query**：纯档案，只供浏览不执行
- **两者都有**：既参与图流程，又作为可查询资源

"预览并执行"不是第三种 Handler，而是 Query + Execute 的组合。拆开比揉在一起更清晰。

### 权限模型

每个 Pack 有两条阈值：`ReadableFrom` / `WritableFrom`。

```csharp
analyser.ReadFile(packID, "/path/file.txt", subjectLevel);
analyser.WriteFile(packID, "/path/file.txt", content, subjectLevel);
```

| Level        | Value | 用途           |
| ------------ | ----- | -------------- |
| Player       | 0     | 玩家前端       |
| AIMin        | 100   | AI / 代理逻辑  |
| EntitySystem | 200   | ECS / 实体系统 |
| SystemMin    | 1000  | 系统级逻辑     |

---

## 具体项目验证

以下项目自定义了不同的后缀，验证同一条协议可以支撑完全不同的业务：

| 项目        | 自定义后缀                     | 用途                                     |
| ----------- | ------------------------------ | ---------------------------------------- |
| **GAL01**   | `.dialog`、`.choice`           | 叙事进度：玩家看到什么对话、做了什么选择 |
| **MineRTS** | `.entity`、`.msg`、`.labentry` | 实体进度、消息进度、科技进度             |

**这些后缀不是框架内置的。** 它们只是上述抽象内核在不同项目中的具体应用。

### GAL01：用图管理叙事进度

GAL01 自定义了 `.dialog` 和 `.choice`，把叙事进度表达为一张图：

```
[Trigger:SceneStart] --→ [.dialog:开场白] --→ [.choice:分支]
                                    ↓ 选项A            ↓ 选项B
                              [.dialog:剧情A]      [.dialog:剧情B]
```

- `.dialog` 节点被信号流触发 → Execute 播放对话内容 → 返回 `Wait` 挂起 → 玩家点击继续后 Resume
- `.choice` 节点被触发 → Execute 展示选项 → 返回 `Wait` 挂起 → 玩家选择后 Resume 到对应分支

### MineRTS：用图管理实体、消息、科技

MineRTS 自定义了 `.entity`、`.msg`、`.labentry`，把硬核系统进度也表达为图：

```
[Trigger:BattleStart] --→ [.entity:spawn_enemy_01] --→ [.entity:spawn_enemy_02]
                                    ↓
                            [Trigger:WaveComplete]
```

- `.entity` 作为图执行：信号流触发 → Execute 在战场生成单位
- `.entity` 作为静态盘查询：仓库 UI 遍历 VFS 路径 → Query 返回单位属性面板

```
[Trigger:MissionComplete] --→ [.msg:战报] --→ [.choice:是否接受奖励]
                                    ↓ 接受                ↓ 拒绝
                              [.msg:发送奖励通知]      [.dialog:拒绝]
                                    ↓
                              [.labentry:解锁新兵种]
```

- `.msg` 作为图执行：信号流触发 → Execute 投递消息副本到邮箱 → 返回 `Wait`
- `.msg` 作为静态盘查询：邮箱 UI 遍历 `/messages/` → Query 返回标题、发件人、已读状态
- `.labentry` 作为图执行：信号流触发 → Execute 将科技条目写入 Lab pack

---

## 快速开始

### 1. 创建 Pack

```
NekoGraph → Create Pack → 输入 PackID: chapter1
```

### 2. 创建节点并绑定资源

```
右键 → Create Node → 输入节点名: intro
节点属性 → Suffix: .myevent  ← 你的项目自定义的后缀
节点属性 → Content → 引用你的 SO 资产
```

### 3. 写 Handler（一次定义，复用）

```csharp
[VFSResource(".myevent", typeof(MyEventSO))]
public static class MyEventResource
{
    [VFSExecute]
    public static HandleResult Execute(
        VFSResolvedContent content,
        SignalContext context,
        BasePackData pack,
        GraphRunner runner,
        string packIDKey,
        System.Action continueAction)
    {
        var evt = content.GetPayload<MyEventSO>();
        MyEventPlayer.Instance.TryPlay(evt, continueAction);
        return HandleResult.Wait;
    }

    [VFSQuery]
    public static VFSQueryResult Query(
        VFSResolvedContent content,
        VFSQueryContext context)
    {
        var evt = content.GetPayload<MyEventSO>();
        return VFSQueryResult.Create(
            presentationType: "myevent",
            title: evt.Title,
            summary: evt.Description,
            isInteractive: false);
    }
}
```

### 4. 运行（Execute）

```csharp
GraphHub.Instance.LoadPack("chapter1");
GraphHub.Instance.RunPack("chapter1", startNodeId: "intro");
```

信号流自动遍历节点，遇到 `.myevent` 节点时触发 Execute Handler，播放完成后自动继续。

### 5. 查询（Query）

前端需要展示资源列表时，直接查询 Pack 的 VFS 树：

```csharp
var children = analyser.GetChildren("chapter1", "/events/", subjectLevel: 0);
foreach (var node in children)
{
    var result = VFSQueryRegistry.Query(node, queryContext);
    // result.Title, result.Summary → 渲染列表项
}
```

### 6. 推荐实现：每个 Pack 一个 Facade

MineRTS 中的实践：为每个业务 Pack 写一个 Facade，封装该 Pack 的读写操作。

```csharp
public static class SocialBoxFacade
{
    public static bool TryDeliverMessageCopy(VFSMsgSO msg, BasePackData sourcePack)
    {
        // 将消息副本投递到 social_tree_default pack 的 /messages/ 目录
        var targetPack = GraphHub.Instance.GetPack("social_tree_default");
        // ... 写 VFS、创建节点、同步数据
    }

    public static List<VFSQueryResult> QueryMessages(int subjectLevel)
    {
        // 遍历 /messages/ 目录，对所有 .msg 调用 Query
        var analyser = GraphHub.Instance.GetAnalyser();
        var children = analyser.GetChildren("social_tree_default", "/messages/", subjectLevel);
        // ... 返回查询结果列表
    }
}
```

好处：
- 封装 Pack 的内部路径结构，前端不直接拼路径
- 集中处理该 Pack 的业务规则（去重、排序、权限）
- 多个前端（UI、CLI、AI）共用同一套门面

---

## CLI 工具

仓库自带 CLI，用于不打开 Unity 时维护 Pack：

```bash
nekograph-cli --create --pack chapter1
nekograph-cli --create --process chapter1 intro --attach-root
nekograph-cli --vfs --write chapter1 /intro/script.json "{\"text\":\"Hello\"}"
nekograph-cli --vfs --ls chapter1 /
```

---

## 目录结构

```
NekoGraph/
├── Runtime/          # 核心：Hub / Runner / Analyser / VFS / 后缀协议
│   ├── Runner_Analyser/
│   └── GraphVSF/
├── Editor/           # 图编辑器、.nekograph 导入器、自动保存
├── SpaceTUI/         # Console / TUI / 事件总线骨架
└── Cli~/             # 本地 CLI 工具
```

---

## 依赖

- Unity 2022.3+
- `com.unity.nuget.newtonsoft-json` 3.2.1+

---

## 许可证

MIT
