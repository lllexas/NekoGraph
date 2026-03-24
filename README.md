# NekoGraph

A signal-driven visual scripting runtime for Unity, designed for mission/story/social graph execution with a Unix-style Virtual File System (VFS) interface.

## Architecture

NekoGraph uses an **OS-level metaphor**:

| OS Concept | NekoGraph Equivalent |
|---|---|
| Process Scheduler | `GraphHub` |
| Process (PCB) | `EntityGraphContext` |
| CPU | `GraphRunner` |
| MMU | `GraphAnalyser` |
| UID | `subjectLevel` |
| Virtual Address Space | `PackDataDict` |
| Code Segment | `NodeStrategy` |

## Permission System

Each Pack has two thresholds:

- `ReadableFrom` — minimum subjectLevel to read
- `WritableFrom` — minimum subjectLevel to write

| Level | Value | Who |
|---|---|---|
| Player | 0 | Player-facing terminals |
| AIMin | 100 | AI agents |
| EntitySystem | 200 | ECS systems |
| SystemMin | 1000 | Developer / system ops |

## Key Components

- **`GraphHub`** — global scheduler, manages all execution contexts
- **`GraphRunner`** — drives signal flow tick by tick
- **`GraphAnalyser`** — permission-checked VFS and graph operations
- **`BasePackData`** — serializable graph data container (Newtonsoft.Json)
- **`NodeStrategy`** — stateless strategy base for node execution logic
- **`VFSLoader`** — load/save PackData from Resources, StreamingAssets, or file

## VFS Interface

```csharp
analyser.WriteFile(packID, "/path/file.txt", content, subjectLevel);
analyser.GetNode(packID, "/path/file.txt", subjectLevel);
analyser.GetChildren(packID, "/path/", subjectLevel);
analyser.Delete(packID, "/path/file.txt", subjectLevel);
```

## Adding a New Node Type

1. Create `MyNodeData : BaseNodeData` in `Runtime/`
2. Create `MyNodeStrategy : NodeStrategy`
3. Register in `NodeStrategyFactory` static constructor
4. No changes to `GraphRunner` or `GraphAnalyser` needed

## Assembly Definitions

| Assembly | Purpose |
|---|---|
| `NekoGraph.Runtime` | Core runtime, auto-referenced |
| `NekoGraph.Editor` | Unity editor tools (Editor only) |

## Dependencies

- Unity 2022.3+
- `com.unity.nuget.newtonsoft-json` 3.2.1+
