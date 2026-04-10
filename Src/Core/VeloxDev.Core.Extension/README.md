# VeloxDev.Core.Extension

> `VeloxDev.Core.Extension` 是 `VeloxDev.Core` 的可选扩展包，用于提供基于第三方库实现的高级能力。当前主要包含两部分：
>
> - 基于 `Newtonsoft.Json` 的 ViewModel JSON 序列化能力
> - 面向 Agent 的 Workflow JSON 接管能力

---

## 1. ViewModel JSON 序列化

相对于默认序列化行为，此扩展为 MVVM / Workflow 模型提供了更适合运行时读写的 JSON 能力。

### 1.1 特性

- 保留运行时类型信息，适用于接口、抽象类型与多态对象
- 支持对象引用处理
- 支持复杂字典 Key 与嵌套字典
- 仅处理实现了 `INotifyPropertyChanged` 的对象
- 仅处理同时具备 `public getter` 与 `public setter` 的属性

### 1.2 同步 API

```csharp
using VeloxDev.Core.Extension;

var json = workflow.Serialize();

if (json.TryDeserialize<MyWorkflowTree>(out var tree))
{
    // 使用 tree
}
```

### 1.3 异步 API

```csharp
using VeloxDev.Core.Extension;

var json = await workflow.SerializeAsync();

var (success, tree) = await json.TryDeserializeAsync<MyWorkflowTree>();

var requiredTree = await json.DeserializeAsync<MyWorkflowTree>();
```

### 1.4 流式 API

```csharp
using VeloxDev.Core.Extension;

await using var writeStream = File.Create("workflow.json");
await workflow.SerializeToStreamAsync(writeStream);

await using var readStream = File.OpenRead("workflow.json");
var tree = await readStream.DeserializeFromStreamAsync<MyWorkflowTree>();
```

---

## 2. Workflow Agent 接管能力

`VeloxDev.Core` 的 `Tree / Node / Slot / Link` 已经具备脱离 GUI 运行的核心结构与命令能力。`VeloxDev.Core.Extension` 在此基础上补充了一套面向 Agent 的 JSON 工具层，使大模型可以基于上下文与 JSON 快照接管 Workflow 操作。

### 2.1 设计目标

核心目标如下：

1. 先向 Agent 暴露组件上下文，而不是只暴露方法名
2. Agent 基于组件上下文理解每种组件的 JSON 结构
3. 所有工作流操作都以 JSON 请求 / JSON 响应进行
4. Agent 可以在“读取当前状态 → 推理下一步 → 提交新请求”的闭环中持续接管工作流
5. 对需要运行时状态的能力（如 `Undo / Redo`）提供会话模式支持

### 2.2 注册组件上下文

在把 Workflow 交给 Agent 前，先注册希望暴露给 Agent 的组件类型：

```csharp
using VeloxDev.Core.Extension.Agent.Workflow;

typeof(TreeViewModel).AsWorkflowAgent();
typeof(NodeViewModel).AsWorkflowAgent();
typeof(SlotViewModel).AsWorkflowAgent();
typeof(LinkViewModel).AsWorkflowAgent();
```

注册后，Agent 可以读取：

- 类型描述
- 属性描述
- 组件 JSON 示例

### 2.3 获取 Agent 指南

```csharp
var helper = WorkflowContextProvider.GetWorkflowHelper();
var nodeContext = WorkflowContextProvider.GetComponentContext("NodeViewModel");
```

其中：

- `GetWorkflowHelper()` 返回 Agent 使用工作流工具所需的英文手册
- `GetComponentContext(string)` 返回单个组件的英文上下文描述

> 约定：扩展库中所有直接提供给 Agent 的描述均为英文；`README` 维持中文简体。

### 2.4 获取全部 Workflow Agent Tools

```csharp
using VeloxDev.Core.Extension;

IEnumerable<Delegate> tools = AgentToolEx.ProvideWorkflowAgentTools();
```

当前已开放的能力包括：

#### 会话类工具

- `CreateWorkflowSession`
- `GetWorkflowSessionState`
- `ReleaseWorkflowSession`

#### Tree 类工具

- `NormalizeWorkflowTreeJson`
- `CloseWorkflowTreeAsync`
- `SetWorkflowPointer`
- `ResetWorkflowVirtualLink`
- `UndoWorkflowTree`
- `RedoWorkflowTree`
- `ClearWorkflowTreeHistory`

#### Node 类工具

- `GetWorkflowNodeJson`
- `CreateWorkflowNode`
- `DeleteWorkflowNode`
- `MoveWorkflowNode`
- `SetWorkflowNodeAnchor`
- `SetWorkflowNodeSize`
- `SetWorkflowNodeBroadcastMode`
- `SetWorkflowNodeReverseBroadcastMode`
- `InvokeWorkflowNodeWorkAsync`
- `InvokeWorkflowNodeBroadcastAsync`
- `InvokeWorkflowNodeReverseBroadcastAsync`

#### Slot 类工具

- `GetWorkflowSlotJson`
- `CreateWorkflowSlot`
- `DeleteWorkflowSlot`
- `SetWorkflowSlotSize`
- `SetWorkflowSlotChannel`
- `ValidateWorkflowConnection`
- `ConnectWorkflowSlots`

#### Link 类工具

- `GetWorkflowLinkJson`
- `DeleteWorkflowLink`
- `SetWorkflowLinkVisibility`

### 2.5 请求模式

所有 Workflow Agent Tool 都接收一个 `string requestJson`，其内容本身是 JSON 对象。

常见字段如下：

- `sessionId`：可选，绑定运行时工作流会话
- `tree`：可选，工作流树 JSON；无会话模式时通常必须提供
- `nodeIndex`：节点索引，对应 `tree.Nodes`
- `slotIndex`：插槽索引，对应 `node.Slots`
- `linkIndex`：连接索引，对应 `tree.Links`
- `node` / `slot`：用于创建组件的组件 JSON
- `anchor` / `size` / `offset`：几何对象 JSON
- `parameter`：传给 `Work / Broadcast / ReverseBroadcast` 的任意 JSON 参数
- `broadcastMode` / `reverseBroadcastMode` / `channel` / `isVisible`：标量配置项

示例：创建节点

```json
{
  "tree": {
    "$type": "Demo.ViewModels.TreeViewModel, Demo",
    "Nodes": [],
    "Links": []
  },
  "node": {
    "$type": "Demo.ViewModels.NodeViewModel, Demo",
    "Title": "HTTP Request"
  }
}
```

示例：连接两个 Slot

```json
{
  "sessionId": "workflow-session-id",
  "senderNodeIndex": 0,
  "senderSlotIndex": 0,
  "receiverNodeIndex": 1,
  "receiverSlotIndex": 0
}
```

### 2.6 两种工作模式

#### 无状态模式

每次调用都传入完整的 `tree` JSON。

优点：

- 简单直接
- 结果稳定
- 易于和大模型上下文对齐

适合：

- 创建、删除、调整属性
- 广播前后的快照分析
- 单次确定性操作

#### 会话模式

先调用 `CreateWorkflowSession`，后续只传 `sessionId` 或者同时传 `sessionId + tree`。

优点：

- 能保留运行时对象
- 能保留 `Undo / Redo` 历史
- 更适合连续交互式编排

适合：

- 长链路工作流编辑
- 需要多次修改后再撤销 / 重做
- 需要依赖运行时状态的 Agent 接管流程

---

## 3. 推荐接入顺序

推荐按如下顺序接入：

1. 注册 `Tree / Node / Slot / Link` 组件类型到 Agent 上下文
2. 让 Agent 先调用 `GetWorkflowHelper()` 读取英文手册
3. 让 Agent 根据需要调用 `GetComponentContext(...)` 读取组件 JSON 契约
4. 使用 `ProvideWorkflowAgentTools()` 提供完整工具集
5. 让 Agent 基于最新 JSON 快照持续推进工作流操作

---

## 4. 适用场景

该扩展尤其适用于：

- AI 编排器接管工作流编辑器
- 通过大模型动态创建节点、插槽与连线
- 用 JSON 快照驱动工作流自动编排
- 将现有 Workflow 系统扩展为 Agent 可操作的运行时内核
