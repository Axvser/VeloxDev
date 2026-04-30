# VeloxDev.Core.Extension

[![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core.Extension?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core.Extension/) · [← 返回主页](../../../README.md)

VeloxDev.Core.Extension 为 `VeloxDev.Core` 提供以下扩展能力：

- **通用 Agent 工具集（AgentObjectToolkit）**：将任意 .NET 对象包装为 10 个 MAF `AITool`（属性读写、命令执行、方法调用），不限于工作流——适用于所有 .NET 项目。
- **ViewModel JSON 序列化**：基于 `Newtonsoft.Json` 的序列化/反序列化，保留运行时类型、处理对象引用并支持复杂字典。
- **Workflow Agent 语义上下文**：面向 Agent 的 Workflow 结构化文档（枚举、接口、组件），含预加载上下文机制与坐标系说明。
- **Workflow Agent 运行时接管（MAF Functions）**：基于 Microsoft Agent Framework 的完整工具集（**59+ 个 AITool**），让 Agent 在单个 Tree 内自由查询、修改、连接、执行、克隆、布局、遍历、分析工作流组件；包含类型自省、JSON Patch、状态快照、批量操作、图遍历、拓扑排序布局、Slot 集合管理、连线管理、复合工具等。

> **与 Core 的关系**：Extension 中的反射操作（属性读写、命令发现执行、类型解析、上下文读取）委托给 `VeloxDev.Core.AI` 中的通用工具类。Extension 负责添加 JSON 序列化和 MAF `AITool` 封装。

---

## 目录

- [0) 通用 Agent 工具集（AgentObjectToolkit）](#0-通用-agent-工具集agentobjecttoolkit)
- [1) ViewModel JSON 序列化](#1-viewmodel-json-序列化)
- [2) Workflow Agent 架构与原理](#2-workflow-agent-架构与原理)
- [3) 坐标系](#3-坐标系)
- [4) 快速上手](#4-快速上手)
- [5) Scope 配置 API](#5-scope-配置-api)
- [6) Prompt 系统](#6-prompt-系统)
- [7) 完整 AITool 列表（59+ 工具）](#7-完整-aitool-列表59-工具)
- [8) 安全机制](#8-安全机制)
- [9) 协议对象](#9-协议对象)

---

## 0) 通用 Agent 工具集（AgentObjectToolkit）

`AgentObjectToolkit` 将 **任意 .NET 对象** 包装为 10 个 MAF `AITool`，使 Agent 可以通过 Function Calling 读写属性、执行命令、调用方法——不限于工作流组件。

### 快速使用

```csharp
// 一行代码：把任何对象变成 Agent 工具集
var tools = myViewModel.AsAgentTools(AgentLanguages.Chinese);

// 或者带属性黑名单
var rejected = new HashSet<string> { "Parent", "InternalState" };
var tools = myViewModel.AsAgentTools(AgentLanguages.Chinese, rejected);

// 传入 Agent
var agent = client.AsAIAgent(
    instructions: "你是一个操作组件的助手。",
    tools: tools);
```

### 生成的 10 个工具

| 工具 | 说明 |
|------|------|
| `GetComponentInfo` | 获取类型名和 `[AgentContext]` 描述 |
| `ListProperties` | 列出所有属性（类型、读写状态、当前值、描述） |
| `GetProperty` | 读取单个属性值 |
| `SetProperty` | 设置单个属性 |
| `PatchProperties` | JSON 批量修改属性（受黑名单保护） |
| `ListCommands` | 列出所有 `ICommand`（参数类型、CanExecute、描述） |
| `ExecuteCommand` | 执行命名命令 |
| `ListMethods` | 列出所有公共方法 |
| `InvokeMethod` | 调用命名方法（支持 JSON 参数数组） |
| `ResolveType` | 按全名解析 .NET 类型 |

### 与 WorkflowAgentToolkit 的区别

| | `AgentObjectToolkit` | `WorkflowAgentToolkit` |
|---|---|---|
| 适用范围 | 任意 .NET 对象 | 绑定到 `IWorkflowTreeViewModel` |
| 工具数量 | 10 个通用工具 | 59+ 个领域专属工具 |
| 安全机制 | 可选属性黑名单 | 三层属性保护 |
| 上下文系统 | 无（用户自定义 Prompt） | 内建完整 Prompt 系统 |
| 状态追踪 | 无 | 快照 + 增量 Diff |

---

## 1) ViewModel JSON 序列化

```csharp
// 同步
var json = workflow.Serialize();
if (json.TryDeserialize<MyWorkflowTree>(out var tree)) { /* use */ }

var treeSync = json.Deserialize<MyWorkflowTree>();

// 异步
var jsonAsync = await workflow.SerializeAsync();
var (ok, tree2) = await jsonAsync.TryDeserializeAsync<MyWorkflowTree>();

// UTF-8 字节（适合 Web / Wasm / 内存缓存 / 网络传输）
var utf8 = workflow.SerializeToUtf8Bytes();
var tree3 = utf8.DeserializeFromUtf8Bytes<MyWorkflowTree>();

// TextReader / TextWriter（适合 StringWriter、浏览器存储桥接、日志或自定义宿主）
using var sw = new StringWriter();
await workflow.SerializeToTextWriterAsync(sw);
using var sr = new StringReader(sw.ToString());
var tree4 = await sr.DeserializeFromTextReaderAsync<MyWorkflowTree>();

// 流式（适合 MemoryStream、网络流、文件流）
using var ms = new MemoryStream();
await workflow.SerializeToStreamAsync(ms);
ms.Position = 0;
var tree5 = await ms.DeserializeFromStreamAsync<MyWorkflowTree>();
```

### 设计说明

- 序列化核心基于 `Newtonsoft.Json`
- API 同时支持 `string`、`byte[]`、`Stream`、`TextReader`、`TextWriter`
- 异步 API 不依赖 `Task.Run` 包装同步序列化，因此更适合 `Avalonia.Browser` / Wasm 等受限运行时
- `TryDeserialize` / `TryDeserializeAsync` 在输入无效时返回 `false`，不抛出格式异常
- `Stream` API 负责序列化到流，不抽象具体持久化介质；浏览器环境建议配合内存流、JS Bridge、IndexedDB 或宿主侧存储接口使用

---

## 2) Workflow Agent 架构与原理

### 总体架构

```
VeloxDev.Core.AI (通用反射基础设施，零依赖)
  AgentPropertyAccessor / AgentMethodInvoker / AgentCommandDiscoverer
  AgentContextReader / AgentTypeResolver
  [AgentContext] / [AgentCommandParameter] 特性
        │
        ▼
IWorkflowTreeViewModel (业务层工作流)
        │
        ▼
 WorkflowAgentScope          ← 域：绑定一棵 Tree + 注册自定义类型
        │
        ├─► ProvideAllContexts()          → 完整语义 Prompt（框架+用户组件文档）
        ├─► ProvideProgressiveContextPrompt()  → 渐进式 Prompt（预加载组件描述 + 按需查询）
        │
        └─► ProvideTools() ──► WorkflowAgentToolkit
                                     │
                                     └─► 59+ 个 AITool（MAF Function Calling）
                                              │
                                              ▼
                                     ChatClientAgent / IChatClient
```

### 设计原则

1. **Scope 隔离**：每个 `WorkflowAgentScope` 绑定一棵 `IWorkflowTreeViewModel`，Agent 只能操作该 Tree 内的组件。
2. **命令优先（Command-First）**：所有对工作流的变更必须通过 `IVeloxCommand` 管道执行，不可直接修改属性。命令负责 UI 线程调度、撤销/重做追踪与视图同步。
3. **渐进式上下文与预加载**：支持两种 Prompt 策略——完整上下文（`ProvideAllContexts`）适合短任务，渐进上下文（`ProvideProgressiveContextPrompt`）将所有注册组件的 `[AgentContext]` 类级描述（含默认尺寸、属性语义等）直接嵌入 Prompt，Agent 从第一轮对话就拥有完整的组件知识，无需额外工具调用。对于详细属性表和命令参数，Agent 按需调用 `GetComponentContext`。
4. **Token 压缩**：所有 JSON 输出使用 `Formatting.None`，键名缩写（`i`/`t`/`x`/`y`/`w`/`h`），列表与详情分离。复合工具（`CreateAndConfigureNode`、`ConnectByProperty`、`ArrangeNodes` 等）可大幅减少工具调用轮次。
5. **ID 稳定性**：通过 `IWorkflowIdentifiable.RuntimeId` 提供稳定标识，不因节点增删导致索引漂移。支持按索引和按 ID 两种寻址方式。
6. **三层安全保护**：`ComponentPatcher` 拒绝修改框架管理属性、命令支撑属性和 Source Generator 管理的 Slot 属性。
7. **坐标系一致性**：所有坐标工具（`MoveNode`、`SetNodePosition`、`AutoLayout`、`ArrangeNodes`）统一使用屏幕坐标系——原点左上角，X+ 向右，Y+ 向下。

### 核心组件

| 组件 | 职责 |
|------|------|
| `WorkflowAgentScope` | 域配置：绑定 Tree、注册类型、生成 Prompt（含预加载上下文）与工具集 |
| `WorkflowAgentToolkit` | 基于 `AIFunctionFactory.Create` 生成 59+ 个 `AITool`，覆盖全部操作 |
| `AgentContextCollector` | 利用反射 + `[AgentContext]` 特性收集枚举/接口/类的结构化文档（基础读取委托至 Core `AgentContextReader`） |
| `TypeIntrospector` | 按 FullTypeName 解析类型（委托至 Core `AgentTypeResolver`）、生成 JSON Schema |
| `CommandInvoker` | 发现并调用组件上的命令，支持 JSON 参数反序列化 |
| `ComponentPatcher` | 安全的 JSON Patch 引擎，三层属性保护（命令检测委托至 Core `AgentCommandDiscoverer.FindBackingCommand`） |
| `WorkflowStateTracker` | 状态快照 + 增量 Diff |

### Core AI 基础设施（由 Extension 委托使用）

| Core 类 | Extension 中的调用方 | 说明 |
|---------|---------------------|------|
| `AgentContextReader` | `AgentContextCollector.GetAgentContext` | 基础的 `[AgentContext]` 特性读取 |
| `AgentTypeResolver` | `TypeIntrospector.ResolveType` | 跨程序集类型解析 |
| `AgentCommandDiscoverer` | `ComponentPatcher.FindBackingCommand` | 命令支撑属性检测 |
| `AgentPropertyAccessor` | — | 通用属性读写（可被自定义工具使用） |
| `AgentMethodInvoker` | — | 通用方法调用（可被自定义工具使用） |

---

## 3) 坐标系

工作流画布使用**屏幕坐标系**（与 WPF/MAUI Canvas 一致）：

```
原点 (0,0)
  ┌──────────────────► X+（向右）
  │
  │    ┌────────┐
  │    │  Node  │  (x=200, y=150)
  │    └────────┘
  │
  ▼
  Y+（向下）
```

| 自然语言 | 坐标操作 |
|---------|----------|
| 向右移动 | X 增大 |
| 向左移动 | X 减小 |
| 向下移动 | Y 增大 |
| 向上移动 | Y 减小 |

> ⚠️ 常见陷阱：Y 轴**向下为正**，与数学坐标系相反。"向上移动" = Y 值减小。

---

## 4) 快速上手

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

## 5) Scope 配置 API

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
| `.WithTools(tools, prompt)` | 注册自定义 `AITool` 并附带 Prompt 描述，嵌入系统 Prompt |
| `.ProvideTools()` | 返回 `IList<AITool>`，包含全部 59+ 个 MAF Functions（含自定义工具） |
| `.CreateToolkit()` | 返回 `WorkflowAgentToolkit` 实例（高级用法） |

---

## 6) Prompt 系统

### ProvideAllContexts（完整上下文）

生成完整的系统 Prompt，包含：
- ⚠️ 命令优先规则
- 🚫 禁止操作列表（框架管理属性 + Source Generator 管理的 Slot 属性）
- 框架类型文档（枚举、接口、基类的属性/方法/命令说明）
- 用户自定义组件文档

适用于：短任务、简单对话、Token 预算充足的场景。

### ProvideProgressiveContextPrompt（渐进式上下文）

生成精简但信息完备的系统 Prompt，包含：
- 命令优先规则 + 禁止操作
- Token 节省技巧（BatchExecute、快照 Diff、列表/详情分离）
- 框架行为说明（删除级联、节点尺寸由视图决定）
- **📌 默认值解析协议**：明确优先级——`[AgentContext]` 开发者指令 > `GetComponentContext` 完整属性表 > ❌ 永远不从运行时状态猜测
- 节点创建协议：从预加载的描述中提取默认尺寸，用户指令覆盖默认值
- **📋 预加载组件描述**：所有注册的 Customer 组件的类级 `[AgentContext]`（含默认尺寸、属性语义）和带 `[AgentContext]` 标注的属性列表直接嵌入 Prompt
- 已注册的类型名列表（Agent 按需调用 `GetComponentContext` 查询完整属性表和命令参数）

适用于：长对话、复杂任务、Token 预算紧张的场景。Agent 从第一轮对话就知道每个组件的默认值和属性含义。

---

## 7) 完整 AITool 列表（59+ 工具）

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

### 🎰 Slot 集合管理（EnumSlotCollection）

| 工具 | 说明 |
|------|------|
| `ListSlotProperties` | 列出节点上所有 Slot 属性及 `EnumSlotCollection` 详情 |
| `AddSlotToCollection` | 向 `EnumSlotCollection` 添加枚举值对应的 Slot |
| `RemoveSlotFromCollection` | 从 `EnumSlotCollection` 移除指定枚举值的 Slot |
| `SetEnumSlotCollection` | 一次性设置 `EnumSlotCollection` 的完整枚举值列表 |

### 🔎 搜索（Search）

| 工具 | 说明 |
|------|------|
| `FindNodes` | 按类型名、属性值等条件模糊搜索节点 |
| `ResolveSlotId` | 按节点 ID + Slot 属性名解析 Slot 的 RuntimeId |

### 🌐 图遍历（Graph Traversal）

| 工具 | 说明 |
|------|------|
| `SearchForward` | 从指定节点沿正向连接搜索可达节点（BFS，可限深度） |
| `SearchReverse` | 从指定节点沿反向连接搜索上游节点 |
| `SearchAllRelative` | 双向搜索，返回所有与指定节点相关的节点 |
| `IsConnected` | 判断两个节点之间是否存在路径 |
| `FindPath` | 查找两个节点之间的最短路径 |

### 📡 反向广播（Reverse Broadcast）

| 工具 | 说明 |
|------|------|
| `ReverseBroadcastNode` | 执行节点的 ReverseBroadcastCommand（沿反向连线回传数据） |

### 🔗 连线管理（Connection Management）

| 工具 | 说明 |
|------|------|
| `DisconnectSlotsById` | 按 RuntimeId 断开连接 |
| `DisconnectAllFromSlot` | 断开指定 Slot 的所有连线 |
| `DisconnectAllFromNode` | 断开指定节点的所有连线 |
| `ReplaceConnection` | 替换现有连线的一端 |

### 📢 Slot Channel

| 工具 | 说明 |
|------|------|
| `SetSlotChannel` | 设置 Slot 的通道（Channel）属性 |

### 🔍 连线详情（Link Inspection）

| 工具 | 说明 |
|------|------|
| `GetLinkDetail` | 获取连线详情（源 Slot、目标 Slot、Channel 等） |

### ⚡ 批量执行（Bulk Operations）

| 工具 | 说明 |
|------|------|
| `ExecuteWorkOnNodes` | 对多个节点批量执行 WorkCommand |
| `BulkPatchNodes` | 对多个节点批量应用 JSON Patch |

### 📐 布局（Layout）

| 工具 | 说明 |
|------|------|
| `AlignNodes` | 将一组节点按指定方向对齐（左/右/上/下/居中） |
| `DistributeNodes` | 将一组节点等间距分布 |
| `AutoLayout` | 基于 Sugiyama 分层算法的自动布局（拓扑排序 → 层次分配 → 交叉最小化 → 坐标计算），坐标系：原点左上，X+ 向右，Y+ 向下 |

### 📊 分析（Analytics）

| 工具 | 说明 |
|------|------|
| `GetNodeStatistics` | 获取工作流统计信息（节点数、连线数、类型分布、连接度等） |
| `ListCreatableTypes` | 列出所有已注册的可创建节点类型 |
| `ValidateWorkflow` | 验证工作流完整性（孤立节点、断开连线、循环检测等） |

### 🧩 复合工具（Composite — 减少工具调用轮次）

| 工具 | 说明 |
|------|------|
| `ConnectByProperty` | 按节点 ID + 属性名连接 Slot（无需先解析 Slot 索引） |
| `CreateAndConfigureNode` | 创建节点 + 设置位置 + 批量 Patch 属性，一步完成 |
| `DeleteNodes` | 批量删除多个节点 |
| `ArrangeNodes` | 批量移动/定位多个节点（坐标系：原点左上，X+ 向右，Y+ 向下） |
| `GetFullTopology` | 一次返回完整工作流拓扑（所有节点 + 所有连线 + 统计信息） |

---

## 8) 安全机制

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

## 9) 协议对象

每个操作都有对应的可观察协议对象（`INotifyPropertyChanged`），可用于 UI 绑定或序列化：

```csharp
using VeloxDev.AI.Workflow.Protocols;

var protocol = new MoveNodeProtocol { NodeIndex = 0, OffsetX = 100, OffsetY = -50 };
var json = protocol.Serialize(); // 使用 ComponentModelEx 序列化
```

协议类型包括：`MoveNodeProtocol`、`SetNodePositionProtocol`、`ResizeNodeProtocol`、`DeleteNodeProtocol`、`ConnectSlotsProtocol`、`DisconnectSlotsProtocol`、`ExecuteWorkProtocol`、`PatchPropertiesProtocol`、`GetTypeSchemaProtocol`、`ExecuteCommandProtocol`、`ExecuteCommandByIdProtocol`、`CreateNodeProtocol`、`CreateSlotProtocol`。