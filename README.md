# NekoGraph

NekoGraph 是一套运行在 Unity 内的“客户端伪后端”图运行时。

它的核心不是单纯做可视化脚本，而是把下面几件事收进同一套内核里：

- 用 `Pack` 表达一份可持久化的业务图或静态资源盘
- 用 `GraphHub / GraphRunner / GraphAnalyser` 组织运行时、权限和路径访问
- 用 Unix 风格 `VFS` 表达静态资源树
- 用后缀协议把业务资源挂进图里，例如 `.dialog`、`.choice`、`.msg`、`.entity`
- 用 `Wait -> ResumeToTarget` 让前端交互和后台图流重新闭环

这套东西已经先后在 `Mi-Demo-Spring`、`GAL01`、`MineRTS` 里承担过任务图、社交消息、对话、实体仓库、研究条目等系统。

## 当前定位

最近约 60 到 90 天的演进之后，NekoGraph 更适合这样理解：

- 它是 Unity 内的一层业务运行时内核
- 图负责“什么时候发生什么”
- VFS 负责“资源以什么路径和权限暴露出来”
- 后缀资源负责“这个文件节点在运行时意味着什么”
- Console / TUI / UI 只是消费这套协议的前端

换句话说，NekoGraph 现在更像一个轻量后端，而不只是一个编辑器画图工具。

## 心智模型

NekoGraph 整体沿用一套操作系统类比：

| OS 概念 | NekoGraph 对应物 |
|---|---|
| Process Scheduler | `GraphHub` |
| Process / PCB | `EntityGraphContext` |
| CPU | `GraphRunner` |
| MMU / 文件系统访问层 | `GraphAnalyser` |
| UID | `subjectLevel` |
| Virtual Address Space | `PackDataDict` |
| Code Segment | `NodeStrategy` |

最重要的基础对象有这些：

- `BasePackData`：一份可序列化的图与 VFS 数据
- `BaseNodeData`：节点基类
- `GraphRunner`：驱动 signal 在图中传播
- `GraphAnalyser`：做权限检查、路径查询、VFS 读写
- `VFSNodeData`：把节点当成目录或文件来组织
- `PackFacadeBase`：把具体业务包访问收成领域门面

## 最近这轮重构真正带来了什么

### 1. `PackID` 被重新扶正为主键

当前方向是让 pack 身份尽量围绕 `PackID` 组织，而不是长期混用 `guid / instanceID / runtime key`。

这让：

- 运行时查包更稳定
- facade 绑定更清楚
- VFS 副本回指后台节点时更容易持久化
- CLI、编辑器、运行时三边的口径更一致

### 2. VFS 后缀协议从“能跑”变成“可扩展”

旧思路主要是：

- `VFSNodeStrategy -> ExeRegistry -> [EXEHandler]`

现在的新口径是：

- `VFSResource` 声明资源身份
- `VFSExecute` 负责运行时执行
- `VFSQuery` 负责查询 / 预览 / 前台呈现入口

兼容层仍然保留：

- `[EXEHandler]`
- `ExeRegistry`

但更推荐的新写法是资源驱动模式。

示例：

```csharp
[VFSResource(".msg", typeof(VFSMsgSO))]
public static class VFSMsgResource
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
        return HandleResult.Wait;
    }

    [VFSQuery]
    public static VFSQueryResult Query(
        VFSResolvedContent content,
        VFSQueryContext context)
    {
        return VFSQueryResult.Create(
            presentationType: "msg",
            title: "Hello",
            summary: "preview",
            payload: null,
            isInteractive: true);
    }
}
```

### 3. `Execute` 和 `Query` 被正式拆开

这是近阶段最重要的设计变化之一：

- `Execute` 负责后台真实运行，允许副作用
- `Query` 负责前台读取和展示入口

这样一来，`.msg`、`.entity`、`.labentry` 这类资源就不需要再把“后端协议 + 前端显示 + 输入接管”揉成一个神秘大类。

### 4. `Wait -> ResumeToTarget` 成为正式异步机制

等待型资源不再依赖临时闭包或 UI 直推流程。

当前更稳定的模型是：

- handler 返回 `HandleResult.Wait`
- 当前 signal 以 `SignalId` 挂起
- 前端交互完成后，显式调用 `ResumeSuspendedSignalToTarget(...)`

这让下列场景都能共用同一套恢复语义：

- `.dialog`
- `.choice`
- `.msg`
- 未来其他需要等待用户输入的资源

### 5. Console / TUI 被重新解释成 `session`

`SpaceTUI` 这条线已经不再推荐继续用“input handler / slot”这种模糊说法。

现在更清楚的结构是：

- `ConsoleManager`：session 宿主
- `ConsoleClientRuntime`：本地呈现仲裁器
- `IConsoleSession`：会话协议
- `TUISelectSlot`：可复用的选择交互内核

这样以后做新的命令行前端资源时，可以优先走：

- `Query -> VFSQueryResult -> ConsoleClientRuntime -> Session`

而不是每次重新手搓一套输入系统。

## Pack 既是图，也是静态盘

NekoGraph 的 pack 不只是“能执行的图”，也可以被当成一个静态 VFS 盘符。

推荐语义：

- `Pack` 本身就是盘
- `RootNode` 对应 `/`
- 根节点的直接子节点就是第一层路径
- 路径只表达 pack 内部结构，不重复表达外部归属

推荐：

```text
PackID = equipment
/
├── inventory/
├── slots/
└── hotbar/
```

不推荐：

```text
/player/equipment/inventory
```

因为：

- `player` 往往已经由外部拥有者表达
- `equipment` 已经由 `PackID` 表达

静态文件内容也应尽量保持原子：

```json
{
  "Id": "ZombieFistsSO"
}
```

这类设计通常更利于：

- 迁移
- 存档
- CLI 编辑
- 运行时副本和静态模板分层

## VFS 内容模型

`VFSNodeData` 现在不只支持内嵌 JSON 文本。

运行时已经支持：

- `VFSContentKind.Json`
- `VFSContentKind.Csv`
- `VFSContentKind.UnityObject`

以及：

- `VFSContentSource.Inline`
- `VFSContentSource.Reference`

这意味着一个 VFS 文件节点既可以：

- 直接内嵌文本
- 引用 `Resources` / `StreamingAssets` 文本
- 引用 `ScriptableObject` 等 Unity 资源

这正是 `.dialog`、`.msg`、`.entity` 这些资源协议能稳定落地的基础。

## 权限模型

每个 pack 至少有两条阈值：

- `ReadableFrom`
- `WritableFrom`

默认等级约定：

| Level | Value | 用途 |
|---|---|---|
| `Player` | `0` | 玩家前端 |
| `AIMin` | `100` | AI 或代理逻辑 |
| `EntitySystem` | `200` | ECS / 实体系统 |
| `SystemMin` | `1000` | 系统级逻辑与工具 |

所有 VFS 读写都建议通过 `GraphAnalyser` 走权限网关：

```csharp
analyser.WriteFile(packID, "/path/file.txt", content, subjectLevel);
analyser.GetNode(packID, "/path/file.txt", subjectLevel);
analyser.GetChildren(packID, "/path/", subjectLevel);
analyser.Delete(packID, "/path/file.txt", subjectLevel);
```

## 项目中的三个样板来源

### Mi-Demo-Spring

这里保留了一份更偏“基础设施期”的 NekoGraph 快照，重点包括：

- `Pack + VFS` 的静态盘语义
- 图桥接编辑 CLI
- 进程骨架 `Spine + LeafA + LeafB`
- 把物品、弹药、初始化状态等数据写进 pack

它比较像这套框架开始进入“客户端伪后端”之前的基础底座。

### GAL01

这里验证了“资源后缀 + Wait/Resume”这条线：

- `.dialog`
- `.choice`
- `DialogPlayer`
- `ChoicePlayer`

也就是：后端 handler 负责理解协议，前端 player 负责自治播放和回调恢复。

### MineRTS

这里把这套东西进一步推成了完整的前后端链：

- `.msg` 的 Execute / Query 分口
- `SocialBoxFacade`
- `EntityWarehouseFacade`
- `LabFacade`
- `ConsoleClientRuntime`
- `VFSMsgSession : TUISelectSlot`

这一版可以视为当前 README 主要参考的“新口径样板”。

## CLI

仓库现在自带一套非常实用的本地 CLI，用于补足“图运行时有了，但手改 JSON 太痛苦”的问题。

典型能力包括：

- 创建 pack
- 注册 pack 到 `MetaLib`
- 创建最小 process 套组
- 查看完整运行报告
- 查询 bridge 路径
- 编辑匿名节点业务字段
- 直接做 VFS 的 `ls / mkdir / write / delete`

典型命令：

```bash
nekograph-cli --create --pack equipment
nekograph-cli --create --process chapter1 intro --attach-root
nekograph-cli --run --full chapter1
nekograph-cli --query --bridge chapter1 spine:intro leaf-a:intro
nekograph-cli --vfs --write equipment /slots/1.item "{ \"Id\": \"Keycard_Lv1\" }"
```

如果你现在主要通过 Agent 或终端改 pack，而不是手开 Unity 编辑器，这套 CLI 会非常有用。

## 目录结构

```text
NekoGraph/
├── Runtime/    # 运行时核心：Hub / Runner / Analyser / Node / VFS
├── Editor/     # 图编辑器、导入器、自动保存、迁移工具
├── Cli~/       # 本地 pack / bridge / VFS 维护工具
├── GraphVSF/   # VFS 相关编辑器 UI
└── SpaceTUI/   # Console / TUI / session 交互骨架
```

## 适合用它做什么

NekoGraph 比较适合这些需求：

- 任务或剧情流程图
- 社交消息、终端、邮件、对话资源
- 客户端内的伪后端数据盘
- 需要“路径 + 权限 + 图流 + 前端交互”统一组织的系统
- 希望让 AI / CLI / 编辑器都能改同一份结构化业务图

如果你只需要一个非常轻的线性状态机，或者只做纯前端 UI 流程，它通常不是最省事的选择。

## 版本口径说明

这个仓库目前在多个项目里各自演进过一段时间。

可以粗略理解为：

- `Mi-Demo-Spring` 内嵌版本更偏基础设施期
- `GAL01` 与 `MineRTS` 内嵌版本已经进入 `VFSResource / Query / Session / Facade` 这条新口径

所以这份 README 更接近“当前这条主线的总说明”，而不是只描述某一个老快照。

## 依赖

- Unity 2022.3+
- `com.unity.nuget.newtonsoft-json` 3.2.1+
