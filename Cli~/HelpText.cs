namespace NekoGraph.Cli;

internal static class HelpText
{
    public static string Value =>
        """
        NekoGraph CLI  —  剧情脚本语言编辑工具

        ═══════════════════════════════════════════════════════════════
        概念速览（必读）
        ═══════════════════════════════════════════════════════════════

        Pack
          一个剧情脚本 JSON 文件，包含一张节点图，描述一段剧情或任务逻辑
          的完整执行流程。用 <packid> 指定，即文件名去掉 .json 后缀。
          Pack 从根节点开始执行，信号沿节点之间的边逐步传播。

        信号 (Signal)
          图中流动的"执行令牌"，从根节点出发沿边传播，驱动每个节点执行
          其逻辑。每帧最多处理 50 个信号，深度超过 100 时自动丢弃（防死循环）。

        具名节点 (Named Node)  ←  骨架节点，剧情的"章节标题"
          图的结构锚点，可用语义引用访问，也接受原始 NodeID：
            spine:<processId>      进程主干节点（流程入口 / 出口）
            leaf-a:<processId>     叶子节点 A（进程分支入口）
            leaf-b:<processId>     叶子节点 B（进程分支出口）
            mission-a:<missionId>  任务激活节点
            mission-s:<missionId>  任务成功节点
            mission-f:<missionId>  任务失败节点
            mission-r:<missionId>  任务重置节点

        匿名节点 (Unnamed Node)  ←  逻辑节点，剧情的"具体行为"
          存在于两个具名节点之间的桥接路径上，有三种类型：
            trigger   等待外部事件触发后继续传播信号（被动等待）
            comparer  条件判断：满足条件走 Pass 输出，否则走 Fail 输出
            command   执行命令：调用游戏系统（播放动画、更改状态等）

        桥接 (Bridge)
          从一个具名节点到另一个具名节点的完整路径，中间穿插匿名节点：
            具名节点  →  [匿名节点₀  →  匿名节点₁  →  ...]  →  具名节点
          匿名节点用从 0 开始的 unnamed-node-index 定位（0 = 第一个）。

        depth-edge-index
          insert-unnamed-at / remove-unnamed-at 使用的插入位置。
          0 = 在起始具名节点之后的第一条边处操作。

        ★ 重要技巧：node-ref 可以是任意节点的 NodeID
          所有接受 <node-ref> 的命令（--show --node、--query --bridge、
          --edit --create-bridge 等）都同时接受：
            • 语义引用      spine:main、leaf-a:act1、mission-s:q1 …
            • 原始 NodeID   abc123、0f9e…  （任意节点，包括匿名节点）

          这意味着匿名节点也可以作为桥接的起点或终点。
          最常见的场景是 comparer 节点的分支：comparer 有 Pass / Fail
          两路输出，它本身是匿名节点，没有语义引用，必须用 NodeID 来
          创建从它出发的新桥接。

          操作方式：
            1. --query --bridge 查询包含 comparer 的桥接，获取其 NodeID
            2. 用该 NodeID 作为 from-node-ref 执行 --edit --create-bridge，
               并用 --from-port 1 指定从 Fail 端口出发（见下方端口说明）

        ★ 端口参数：--from-port 和 --to-port
          部分命令支持在末尾追加可选的端口参数，用于指定连接走哪个端口：
            --from-port <N>   起点节点的输出端口编号（默认 0）
            --to-port <N>     终点节点的输入端口编号（默认 0）

          端口编号由 Runtime 源码中的 [OutPort(N)] / [InPort(N)] 属性决定。
          CLI 在启动时自动扫描源码完成映射，无需手动维护。

          comparer 节点的端口定义：
            输出端口 0  →  PassOutputs（条件满足时）
            输出端口 1  →  FailOutputs（条件不满足时）

          支持端口参数的命令：
            --query --bridge、--query --fields
            --edit --create-bridge、--edit --destroy-bridge、--edit --field

          若省略端口参数，CLI 默认使用端口 0。
          若节点不存在请求的端口，CLI 报错并列出可用端口编号。

          示例：从 comparer 的 Fail 端口接出新的分支
            .\nekograph.cmd --edit --create-bridge pack1 <comparerNodeId> leaf-b:fail trigger --from-port 1

        ═══════════════════════════════════════════════════════════════
        节点类型说明（--help --nodes 查看详细说明）
        ═══════════════════════════════════════════════════════════════

        【流程控制节点】— 构建剧情/任务的骨架结构

        Root (根节点)
          语义：流程的起始锚点，所有信号从这里注入
          行为：收到信号 → 立即向输出节点传播
          用途：每个 Pack 必须有一个 Root 节点，作为剧情入口

        Spine (主干节点)
          语义：流程的逻辑骨架，像"脊椎"一样支撑整个剧情结构
          行为：收到信号 → 1) 激活关联的 Leaf-A 节点，2) 向下一个 Spine 传播
          字段：ProcessID（进程 ID，用于关联 Leaf 节点）
          用途：将长流程分割成多个"进程段"，便于管理

        Leaf-A (叶子节点 A)
          语义：进程分支的入口，被 Spine 激活
          行为：收到信号 → 向输出节点传播（通常是 Mission-A 或 Trigger）
          字段：ProcessID（必须与某个 Spine 共享）
          用途：启动具体的任务线或剧情分支

        Leaf-B (叶子节点 B)
          语义：进程分支的出口，任务完成后信号返回这里
          行为：收到信号 → 向下一个 Spine 或其他节点传播
          字段：ProcessID（必须与某个 Spine 共享）
          用途：将完成信号返回到主干流程

        【任务节点】— 管理任务的状态流转

        Mission-A (任务激活)
          语义：任务的起点，激活任务状态
          行为：收到信号 → IsActive=true，向输出节点传播
          字段：MissionID（任务唯一标识），Priority（优先级）
          用途：标记一个任务开始，通常后接 Trigger 等待条件

        Mission-S (任务成功)
          语义：任务成功终点
          行为：收到信号 → IsCompleted=true，查找 Mission-A 标记完成
          字段：MissionID（必须与某个 Mission-A 匹配）
          用途：标记任务成功完成，通常后接奖励 Command 或 Leaf-B

        Mission-F (任务失败)
          语义：任务失败终点
          行为：收到信号 → IsFailed=true，查找 Mission-A 标记失败
          字段：MissionID（必须与某个 Mission-A 匹配）
          用途：标记任务失败，通常后接重试逻辑或剧情分支

        Mission-R (任务重置)
          语义：任务重置点
          行为：收到信号 → 重置进度，可选重新激活
          字段：MissionID, ResetProgress（是否重置进度）, Reactivate（是否重新激活）
          用途：重置任务状态，用于可重复任务或失败后重试

        【逻辑节点】— 实现具体的游戏逻辑

        Trigger (触发器)
          语义：事件监听器，被动等待特定事件发生
          行为：挂载事件监听 → 事件触发 → 向输出节点传播信号
          字段：Event（监听的事件名，见下方事件列表）
          用途：等待游戏事件（如"单位死亡"、"建筑完成"）

        Comparer (比较器)
          语义：条件判断器，根据条件走不同分支
          行为：检查条件 → 满足走 Pass(端口 0)，不满足走 Fail(端口 1)
          字段：ComparerName（比较器类型），Parameters（比较参数）
          用途：条件分支（如"击杀数>=5"、"资源>=1000"）

        Command (命令)
          语义：执行具体的游戏操作
          行为：调用游戏系统 → 执行命令 → 向输出节点传播
          字段：CommandName（命令名），Parameter（单参数），Parameters（多参数）
          用途：执行操作（如"生成单位"、"播放动画"、"发放奖励"）

        ═══════════════════════════════════════════════════════════════
        可用触发事件 (trigger 节点的 Event 字段)
        ═══════════════════════════════════════════════════════════════

          GameStarted          游戏开始
          GameTickUpdated      游戏 Tick 更新
          UnitSpawned          单位生成
          UnitKilled           单位死亡
          UnitDamaged          单位受伤
          MoneyChanged         金钱变化
          ResourceChanged      资源变化
          BuildingConstructed  建筑建造完成
          MissionCompleted     任务完成
          ResearchCompleted    研究完成
          GroundClicked        点击地面
          UnitSelected         选中单位
          SocialOption1        社交选项 1
          SocialOption2        社交选项 2
          SocialOption3        社交选项 3
          SocialOption4        社交选项 4
          BaseUnderAttack      基地被攻击

        ═══════════════════════════════════════════════════════════════
        可编辑字段  (--edit --field 允许修改的字段)
        ═══════════════════════════════════════════════════════════════

          trigger 节点：
            Event                    触发事件名（见上方事件列表）

          comparer 节点：
            ComparerName             比较器名称
            Parameters               比较参数（JSON 字符串）

          command 节点：
            Command.CommandName      命令名称
            Command.Parameter        单个参数（字符串）
            Command.Parameters       多参数（JSON 字符串）

          注意：NodeID、OutputConnections 等结构字段不可通过此命令修改，
                需使用 --edit --create-bridge / --insert-unnamed 等结构命令。

        ═══════════════════════════════════════════════════════════════
        命令参考
        ═══════════════════════════════════════════════════════════════

        --run --full <packid>
            输出完整运行报告：信号路径、所有阻塞点、结构异常。
            改动任何 Pack 前请先执行此命令，了解当前执行语义。

        --show --node <packid> <node-ref>
            显示单个节点的详细信息（类型、字段、出边）。
            node-ref 可以是语义引用（spine:main）或原始 NodeID。

        --show --process <packid> <processid>
            显示一个进程的所有节点（spine + leaf-a + leaf-b）。

        --show --mission <packid> <missionid>
            显示一个任务的所有节点（mission-a / s / f / r）。

        --query --bridge <packid> <from-node-ref> <to-node-ref>
            显示两个具名节点之间的完整桥接路径及每个匿名节点信息。
            执行结构编辑之前必须先 query，确认路径后再操作。

        --query --fields <packid> <from-node-ref> <to-node-ref> <unnamed-node-index>
            显示桥接中指定匿名节点的所有业务字段当前值。
            在 --edit --field 之前执行，确认字段名和当前值。

        --edit --create-bridge <packid> <from-node-ref> <to-node-ref> <node-kind-list|none>
            在两个具名节点之间创建新桥接。
            node-kind-list：逗号分隔的节点类型，如 trigger,command
            none：创建无匿名节点的直连边。
            示例：.\nekograph.cmd --edit --create-bridge pack1 spine:main leaf-a:act1 trigger,command

        --edit --destroy-bridge <packid> <from-node-ref> <to-node-ref>
            删除两个具名节点之间的整条桥接（含所有中间匿名节点）。
            警告：此操作不可撤销，请先 --query --bridge 确认范围。

        --edit --insert-unnamed <packid> <from-node-ref> <to-node-ref> <trigger|comparer|command>
            在已有桥接末尾（紧接目标节点之前）追加一个匿名节点。

        --edit --insert-unnamed-at <packid> <from-node-ref> <to-node-ref> <depth-edge-index> <trigger|comparer|command>
            在桥接的指定边位置插入匿名节点。
            depth-edge-index=0 表示插入到起始节点之后第一条边处。

        --edit --remove-unnamed <packid> <from-node-ref> <to-node-ref>
            删除桥接中最后一个匿名节点（紧接目标节点之前的那个）。

        --edit --remove-unnamed-at <packid> <from-node-ref> <to-node-ref> <depth-edge-index>
            删除桥接中指定位置的匿名节点。

        --edit --field <packid> <from-node-ref> <to-node-ref> <unnamed-node-index> <field-name> <value>
            修改桥接中指定匿名节点的业务字段。
            示例：.\nekograph.cmd --edit --field pack1 spine:main leaf-a:act1 0 Event MissionCompleted

        ═══════════════════════════════════════════════════════════════
        典型工作流
        ═══════════════════════════════════════════════════════════════

        【查看 Pack 的完整剧情执行流程】
          .\nekograph.cmd --run --full <packid>

        【在两个节点间添加"等待任务完成后执行命令"逻辑】
          # 1. 查询当前桥接，确认是否已存在
          .\nekograph.cmd --query --bridge <packid> spine:main leaf-a:act1
          # 2. 创建 trigger → command 结构的桥接
          .\nekograph.cmd --edit --create-bridge <packid> spine:main leaf-a:act1 trigger,command
          # 3. 设置 trigger 监听的事件（索引 0）
          .\nekograph.cmd --edit --field <packid> spine:main leaf-a:act1 0 Event MissionCompleted
          # 4. 设置 command 执行的命令名（索引 1）
          .\nekograph.cmd --edit --field <packid> spine:main leaf-a:act1 1 Command.CommandName PlayCutscene
          # 5. 验证结果
          .\nekograph.cmd --query --bridge <packid> spine:main leaf-a:act1

        【从 comparer 的 Pass / Fail 端口分别接出两条分支】
          # 1. 查询桥接，从输出中获取 comparer 的原始 NodeID
          .\nekograph.cmd --query --bridge <packid> spine:main leaf-a:act1
          # 2. Pass 端口（端口 0，默认）接成功分支 — --from-port 0 可省略
          .\nekograph.cmd --edit --create-bridge <packid> <comparerNodeId> leaf-b:pass command --from-port 0
          .\nekograph.cmd --edit --field <packid> <comparerNodeId> leaf-b:pass 0 Command.CommandName OnSuccess --from-port 0
          # 3. Fail 端口（端口 1）接失败分支 — 必须显式写 --from-port 1
          .\nekograph.cmd --edit --create-bridge <packid> <comparerNodeId> leaf-b:fail command --from-port 1
          .\nekograph.cmd --edit --field <packid> <comparerNodeId> leaf-b:fail 0 Command.CommandName OnFail --from-port 1

        ═══════════════════════════════════════════════════════════════
        Usage
        ═══════════════════════════════════════════════════════════════

          nekograph-cli --version
          nekograph-cli --help
          nekograph-cli --help --nodes       ← 查看节点类型详细说明
          nekograph-cli --run --full <packid>
          nekograph-cli --show --node <packid> <node-ref>
          nekograph-cli --show --process <packid> <processid>
          nekograph-cli --show --mission <packid> <missionid>
          nekograph-cli --query --bridge <packid> <from-node-ref> <to-node-ref> [--from-port N] [--to-port N]
          nekograph-cli --query --fields <packid> <from-node-ref> <to-node-ref> <unnamed-node-index> [--from-port N] [--to-port N]
          nekograph-cli --edit --create-bridge <packid> <from-node-ref> <to-node-ref> <node-kind-list|none> [--from-port N] [--to-port N]
          nekograph-cli --edit --destroy-bridge <packid> <from-node-ref> <to-node-ref> [--from-port N] [--to-port N]
          nekograph-cli --edit --field <packid> <from-node-ref> <to-node-ref> <unnamed-node-index> <field-name> <value> [--from-port N] [--to-port N]
          nekograph-cli --edit --insert-unnamed <packid> <from-node-ref> <to-node-ref> <trigger|comparer|command> [--from-port N] [--to-port N]
          nekograph-cli --edit --insert-unnamed-at <packid> <from-node-ref> <to-node-ref> <depth-edge-index> <trigger|comparer|command> [--from-port N] [--to-port N]
          nekograph-cli --edit --remove-unnamed <packid> <from-node-ref> <to-node-ref>
          nekograph-cli --edit --remove-unnamed-at <packid> <from-node-ref> <to-node-ref> <depth-edge-index>
        """;
}
