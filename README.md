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

## Static Pack Semantics

NekoGraph packs are not only runnable graphs. A pack can also be treated as a static Unix-style virtual disk.

- A `Pack` is the drive / volume itself.
- Ownership or higher-level grouping belongs outside the path, typically in the caller's `PackDataDict` or `UserModel`.
- `RootNode` is the VFS root `/`.
- Direct children of `RootNode` are the first path segments.

That means a path should describe only the structure inside the pack itself.

Good:

```text
PackID = equipment
/
├── inventory/
├── slots/
└── hotbar/
```

Avoid duplicating outer semantics inside the path:

```text
/player/equipment/inventory
```

if `player` is already implied by the owner of the pack and `equipment` is already the `PackID`.

For reference-style files, prefer tiny, atomic file contents. Example:

```json
{
  "Id": "ZombieFistsSO"
}
```

In that design:

- path expresses structure
- file name expresses object identity or slot position
- file content expresses the smallest useful reference
- runtime behavior stays in external systems, not in the static pack layout

## VFS Runtime Blueprint (ExeRegistry)

VFS file nodes can execute external logic based on their suffix. This allows external projects to "plant" custom runtime behavior into graphs without modifying NekoGraph core.

### How it works

1. `VFSNodeStrategy` handles `VFSNodeData` — when the node is a file with a registered suffix, it dispatches to `ExeRegistry`
2. `ExeRegistry` scans all assemblies at startup for `[EXEHandler]`-annotated static methods and builds the suffix → handler map
3. Handlers are defined in external projects; NekoGraph has no knowledge of them

### Registering a handler (external project)

```csharp
[EXEHandler(".mysuffix", typeof(MyData))]
public static void Handle(
    string dataJson,
    SignalContext context,
    BasePackData pack,
    GraphRunner runner,
    string packInstanceID)
{
    var data = JsonConvert.DeserializeObject<MyData>(dataJson);
    // ... do work ...
    context.Args = result; // pass to downstream nodes
}
```

Handler method signature must match exactly:
`(string, SignalContext, BasePackData, GraphRunner, string) → void`

### DataType registration

The optional second argument to `[EXEHandler]` registers a data type for editor tooling:
- VFSNode editor shows field names as a hint
- "Open in External Editor" button generates a JSON template from default field values

### ExeRegistry API

```csharp
ExeRegistry.TryGetHandler(".prefab", out var handler);  // query
ExeRegistry.GetDataType(".prefab");                      // editor hint
ExeRegistry.Register(".custom", myDelegate, typeof(T)); // manual register
ExeRegistry.GetAllSuffixes();                            // list all
```

`ExeRegistry` lazy-initializes via `EnsureInitialized()` — safe to call from Editor and CLI tools (no `[RuntimeInitializeOnLoadMethod]` dependency).

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
