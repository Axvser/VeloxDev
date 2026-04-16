# VeloxDev.Core.Extension

VeloxDev.Core.Extension 为 `VeloxDev.Core` 提供以下扩展能力：

- **ViewModel JSON 序列化**：基于 `Newtonsoft.Json` 的序列化/反序列化，保留运行时类型、处理对象引用并支持复杂字典。
- **Workflow Agent 语义上下文**：面向 Agent 的 Workflow 结构化文档（枚举、接口、组件）。
- **Workflow Agent 运行时接管（MAF Functions）**：基于 Microsoft Agent Framework 的完整工具集（30 个 AITool），让 Agent 在单个 Tree 内自由查询、修改、连接、执行、克隆工作流组件；包含类型自省、JSON Patch、状态快照、批量操作等。

---

## 目录

- [1) ViewModel JSON 序列化](#1-viewmodel-json-序列化)
- [2) Workflow Agent 架构与原理](#2-workflow-agent-架构与原理)
- [3) 快速上手](#3-快速上手)
- [4) Scope 配置 API](#4-scope-配置-api)
- [5) Prompt 系统](#5-prompt-系统)
- [6) 完整 AITool 列表](#6-完整-aitool-列表)
- [7) 安全机制](#7-安全机制)
- [8) 协议对象](#8-协议对象)

---

## 1) ViewModel JSON 序列化

```csharp
// 同步
var json = workflow.Serialize();
if (json.TryDeserialize<MyWorkflowTree>(out var tree)) { /* use */ }

// 异步
var jsonAsync = await workflow.SerializeAsync();
var (ok, tree2) = await jsonAsync.TryDeserializeAsync<MyWorkflowTree>();

// 流式
await using var ws = File.Create("wf.json");
await workflow.SerializeToStreamAsync(ws);
```

---

## 2) Workflow Agent 架构与原理

### 总体架构

```
IWorkflowTreeViewModel (业务层工作流)
        │
        ▼
 WorkflowAgentScope          ← 域：绑定一棵 Tree + 注册自定义类型
        │
        ├─► ProvideAllContexts()          → 完整语义 Prompt（框架+用户组件文档）
        ├─► ProvideProgressiveContextPrompt()  → 渐进式 Prompt（节省 Token）
        │
        └─► ProvideTools() ──► WorkflowAgentToolkit
                                     │
                                     └─► 30 个 AITool（MAF Function Calling）
                                              │
                                              ▼
                                     ChatClientAgent / IChatClient
```

### 设计原则

1. **Scope 隔离**：每个 `WorkflowAgentScope` 绑定一棵 `IWorkflowTreeViewModel`，Agent 只能操作该 Tree 内的组件。
2. **命令优先（Command-First）**：所有对工作流的变更必须通过 `IVeloxCommand` 管道执行，不可直接修改属性。命令负责 UI 线程调度、撤销/重做追踪与视图同步。
3. **渐进式上下文**：支持两种 Prompt 策略——完整上下文（`ProvideAllContexts`）适合短任务，渐进上下文（`ProvideProgressiveContextPrompt`）仅提供概览 + 发现流程，Agent 按需调用 `GetComponentContext` 获取细节，大幅节省 Token。
4. **Token 压缩**：所有 JSON 输出使用 `Formatting.None`，键名缩写（`i`/`t`/`x`/`y`/`w`/`h`），列表与详情分离。
5. **ID 稳定性**：通过 `IWorkflowIdentifiable.RuntimeId` 提供稳定标识，不因节点增删导致索引漂移。支持按索引和按 ID 两种寻址方式。
6. **三层安全保护**：`ComponentPatcher` 拒绝修改框架管理属性、命令支撑属性和 Source Generator 管理的 Slot 属性。

### 核心组件

| 组件 | 职责 |
|------|------|
| `WorkflowAgentScope` | 域配置：绑定 Tree、注册类型、生成 Prompt 与工具集 |
| `WorkflowAgentToolkit` | 基于 `AIFunctionFactory.Create` 生成 30 个 `AITool`，覆盖全部操作 |
| `AgentContextCollector` | 利用反射 + `[AgentContext]` 特性收集枚举/接口/类的结构化文档 |
| `TypeIntrospector` | 按 FullTypeName 解析类型、生成 JSON Schema |
| `CommandInvoker` | 发现并调用组件上的命令，支持 JSON 参数反序列化 |
| `ComponentPatcher` | 安全的 JSON Patch 引擎，三层属性保护 |
| `WorkflowStateTracker` | 状态快照 + 增量 Diff |

---

## 3) 快速上手

```csharp
// 1. 创建 Scope：以一棵 Tree 作为 Agent 的操作域
var scope = tree.AsAgentScope()
    .WithComponents(AgentLanguages.English,
        typeof(NodeViewModel),
        typeof(ControllerViewModel),
        typeof(SlotViewModel),
        typeof(LinkViewModel),
        typeof(TreeViewModel))
    .WithEnums(AgentLanguages.English)
    .WithInterfaces(AgentLanguages.English);

// 2. 生成 Prompt（框架 + 自定义组件的结构化上下文）
var contextPrompt = scope.ProvideAllContexts(AgentLanguages.English);

// 3. 生成 MAF 工具集
var tools = scope.ProvideTools();

// 4. 传入 Agent
var agent = chatClient.AsAIAgent(
    instructions: contextPrompt,
    tools: tools);

var session = await agent.CreateSessionAsync();
```

> 完整示例参见 [`Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/AgentHelper.cs`](../../Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/AgentHelper.cs)

---

## 4) Scope 配置 API

| 方法 | 说明 |
|------|------|
| `tree.AsAgentScope()` | 创建绑定到指定 Tree 的 Scope |
| `.WithEnums(lang, types)` | 注册自定义枚举类型，用于生成上下文文档 |
| `.WithInterfaces(lang, types)` | 注册自定义接口类型 |
| `.WithComponents(lang, types)` | 注册自定义组件类型（Node/Slot/Link/Tree） |
| `.WithMaxToolCalls(n)` | 限制单次会话最大工具调用次数，超出后工具返回错误 |
| `.WithToolCallCallback(cb)` | 注册每次工具调用后的回调 `(toolName, result, callCount)` |
| `.ProvideAllContexts(lang)` | 生成完整语义 Prompt（框架文档 + 用户组件文档 + 安全规则） |
| `.ProvideProgressiveContextPrompt(lang)` | 生成渐进式 Prompt（概览 + 发现流程，Agent 按需查询） |
| `.ProvideTools()` | 返回 `IList<AITool>`，包含全部 30 个 MAF Functions |
| `.CreateToolkit()` | 返回 `WorkflowAgentToolkit` 实例（高级用法） |

---

## 5) Prompt 系统

### ProvideAllContexts（完整上下文）

生成完整的系统 Prompt，包含：
- ⚠️ 命令优先规则
- 🚫 禁止操作列表（框架管理属性 + Source Generator 管理的 Slot 属性）
- 框架类型文档（枚举、接口、基类的属性/方法/命令说明）
- 用户自定义组件文档

适用于：短任务、简单对话、Token 预算充足的场景。

### ProvideProgressiveContextPrompt（渐进式上下文）

生成精简的系统 Prompt，仅包含：
- 命令优先规则 + 禁止操作
- Token 节省技巧（BatchExecute、快照 Diff、列表/详情分离）
- 框架行为说明（删除级联、节点尺寸由视图决定）
- 发现流程：`GetWorkflowSummary` → `ListNodes` → `GetNodeDetail` → `ListComponentCommands` → `GetComponentContext`
- 已注册的类型名列表（Agent 按需调用 `GetComponentContext` 查询详情）

适用于：长对话、复杂任务、Token 预算紧张的场景。

---

## 6) 完整 AITool 列表

### 📖 查询（Query）

| 工具 | 说明 |
|------|------|
| `ListNodes` | 列出所有节点，返回紧凑 JSON `[{i,id,t,x,y,l,w,h,slots,...props}]` |
| `GetNodeDetail` | 按索引获取节点详情（属性、Slot 列表、连接关系） |
| `GetNodeDetailById` | 按 RuntimeId 获取节点详情（跨增删稳定） |
| `ListConnections` | 列出所有可见连线 `[{id,sid,rid}]` |
| `GetTypeSchema` | 按完整类型名获取 .NET 类型的 JSON Schema |

### 🔍 渐进式上下文（Progressive Context）

| 工具 | 说明 |
|------|------|
| `GetWorkflowSummary` | 高层概览：节点/连线数量、类型分布、Tree ID |
| `GetComponentContext` | 按完整类型名获取 Agent 文档（属性/命令/说明），支持中英文 |
| `ListComponentCommands` | 列出节点上所有可执行命令及参数类型 |

### 📸 状态追踪（State Tracking / Diff）

| 工具 | 说明 |
|------|------|
| `TakeSnapshot` | 记录当前状态快照，返回版本号和摘要计数 |
| `GetChangesSinceSnapshot` | 返回自上次快照以来的增量变化（新增/删除/修改） |

### ✏️ 变更（Mutation）

| 工具 | 说明 |
|------|------|
| `MoveNode` | 按相对偏移量移动节点，支持撤销 |
| `SetNodePosition` | 设置节点绝对坐标和层级 |
| `ResizeNode` | 调整节点尺寸 |
| `DeleteNode` | 删除节点（级联删除所有子 Slot 及连线） |
| `DeleteSlot` | 删除指定 Slot 及其连线 |
| `ConnectSlots` | 按节点/Slot 索引建立连接 |
| `ConnectSlotsById` | 按 RuntimeId 建立连接（跨增删稳定） |
| `DisconnectSlots` | 按节点/Slot 索引断开连接 |
| `ExecuteWork` | 执行节点的 WorkCommand |
| `BroadcastNode` | 执行节点的 BroadcastCommand（沿连线转发数据） |
| `Undo` | 撤销上一步操作 |
| `Redo` | 重做上一步被撤销的操作 |
| `PatchNodeProperties` | 通过 JSON Patch 修改节点自定义属性（受安全机制保护） |
| `PatchComponentById` | 通过 RuntimeId + JSON Patch 修改任意组件的自定义属性 |

### ⚡ 通用命令执行（Generic Command）

| 工具 | 说明 |
|------|------|
| `ExecuteCommandOnNode` | 按索引 + 命令名执行节点上的任意命令，支持 JSON 参数 |
| `ExecuteCommandById` | 按 RuntimeId + 命令名执行任意组件上的命令 |
| `CreateNode` | 按完整类型名创建节点并加入 Tree |
| `CreateSlotOnNode` | 在节点上动态创建 Slot（仅用于无 Source Generator 管理的场景） |

### 📦 批量操作（Batch）

| 工具 | 说明 |
|------|------|
| `BatchExecute` | 在一次调用中执行多个操作，减少往返 `[{"tool":"...", "args":{...}}]` |

### 🔀 克隆（Clone）

| 工具 | 说明 |
|------|------|
| `CloneNodes` | 克隆一组节点（含内部连线）到新位置，返回旧→新 ID 映射 |

---

## 7) 安全机制

`ComponentPatcher` 实现三层防护，确保 Agent 不会破坏框架内部状态：

### 第一层：框架管理属性黑名单

以下属性被无条件拒绝修改：

| 属性 | 原因 |
|------|------|
| `Parent` | 由框架在节点/Slot 加入 Tree/Node 时自动设置 |
| `Nodes` / `Links` / `LinksMap` | Tree 集合，由框架命令管理 |
| `Slots` | Node Slot 集合，由框架命令管理 |
| `Targets` / `Sources` | Slot 连接集合，由框架命令管理 |
| `State` | Slot 状态，由连接生命周期管理 |
| `VirtualLink` / `Helper` | 框架内部实现细节 |
| `RuntimeId` | 不可变标识 |
| `Anchor`（在 Slot 上） | 由视图布局计算 |

### 第二层：命令支撑属性检测

如果某个属性存在同名的 `IVeloxCommand`（例如属性 `Anchor` 对应 `SetAnchorCommand`），则 Patch 被拒绝，Agent 必须使用对应的工具（如 `SetNodePosition`）。

### 第三层：Source Generator Slot 属性拒绝

如果属性类型实现了 `IWorkflowSlotViewModel`（例如 `InputSlot`、`OutputSlot`），则 Patch 被拒绝。这些 Slot 由 Source Generator 通过懒初始化 + `CreateSlotCommand` 自动创建和管理。

---

## 8) 协议对象

每个操作都有对应的可观察协议对象（`INotifyPropertyChanged`），可用于 UI 绑定或序列化：

```csharp
using VeloxDev.AI.Workflow.Protocols;

var protocol = new MoveNodeProtocol { NodeIndex = 0, OffsetX = 100, OffsetY = -50 };
var json = protocol.Serialize(); // 使用 ComponentModelEx 序列化
```

协议类型包括：`MoveNodeProtocol`、`SetNodePositionProtocol`、`ResizeNodeProtocol`、`DeleteNodeProtocol`、`ConnectSlotsProtocol`、`DisconnectSlotsProtocol`、`ExecuteWorkProtocol`、`PatchPropertiesProtocol`、`GetTypeSchemaProtocol`。