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

`VeloxDev.Core` 负责提供工作流框架层的语义上下文；`VeloxDev.Core.Extension` 在此基础上提供低 Token 的运行时协议层，使 Agent 可以基于稳定 id、增量变更和批量 Patch 接管 Workflow 操作。

### 2.1 设计目标

核心目标如下：

1. 先向 Agent 暴露框架语义上下文，而不是只暴露方法名
2. 运行时协议优先返回摘要、增量和受影响对象，而不是默认返回整棵树
3. 使用稳定 id（`nodeId` / `slotId` / `linkId`）而不是长期依赖索引
4. 使用批量 Patch 和增量同步减少多轮往返和上下文膨胀
5. 对需要运行时状态的能力（如 `Undo / Redo`）提供会话模式支持

### 2.2 注册组件上下文

在把 Workflow 交给 Agent 前，先注册希望暴露给 Agent 的组件类型：

```csharp
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow;

typeof(TreeViewModel).AsWorkflowAgentContextProvider();
typeof(NodeViewModel).AsWorkflowAgentContextProvider();
typeof(SlotViewModel).AsWorkflowAgentContextProvider();
typeof(LinkViewModel).AsWorkflowAgentContextProvider();
```

注册后，Agent 可以读取：

- 框架接口与模板组件的上下文文档
- 枚举和值类型语义表
- 用户组件按 `Tree / Node / Slot / Link` 分区的上下文表
- 单个类型的详细上下文

### 2.3 获取 Agent 指南

```csharp
var helper = WorkflowContextProvider.GetWorkflowHelper();
var bootstrap = WorkflowProtocolTools.GetWorkflowBootstrap();
var framework = WorkflowProtocolTools.GetWorkflowContextSection("{\"section\":\"framework\"}");
var nodeContext = WorkflowContextProvider.GetComponentContext("NodeViewModel");
```

其中：

- `GetWorkflowHelper()` 返回推荐工具循环与兼容说明
- `GetWorkflowBootstrap()` 返回低 Token 协议层的启动文档
- `GetWorkflowContextSection(...)` 按需返回上下文分区
- `GetComponentContext(string)` 返回单个类型上下文

> 约定：扩展库中所有直接提供给 Agent 的描述均为英文；`README` 维持中文简体。

### 2.4 获取全部 Workflow Agent Tools

```csharp
using VeloxDev.Core.Extension;

IEnumerable<Delegate> tools = AgentToolEx.ProvideWorkflowAgentTools();
```

推荐优先使用的新协议层能力包括：

#### Bootstrap / Context Tools

- `GetWorkflowBootstrap`
- `GetWorkflowBootstrapInLanguage`
- `GetWorkflowContextSection`

#### Compact Runtime Protocol Tools

- `OpenWorkflowSession`
- `QueryWorkflowGraph`
- `GetWorkflowTargetCapabilities`
- `GetWorkflowPropertyValue`
- `ValidateWorkflowPatch`
- `ApplyWorkflowPatch`
- `InvokeWorkflowActionAsync`
- `InvokeWorkflowCommandAsync`
- `InvokeWorkflowMethodAsync`
- `GetWorkflowChanges`
- `GetWorkflowDiagnostics`
- `ReleaseWorkflowProtocolSession`

其中：

- `OpenWorkflowSession` 返回 `sessionId + revision + summary + context hashes`
- `QueryWorkflowGraph` 支持 `summary / tree / node / slot / link` 五种查询模式
- `QueryWorkflowGraph` 可通过 `includeJson: true` 返回单个对象的实时 JSON 参考
- `GetWorkflowTargetCapabilities` 返回当前目标上可被 Agent 完整接管的属性、命令、方法目录
- `GetWorkflowPropertyValue` 精确读取一个已授权属性路径的当前值
- `ValidateWorkflowPatch` 以 detached clone dry-run 方式验证 patch，不修改 live session
- `ApplyWorkflowPatch` 支持批量图编辑与通用属性编辑，默认返回 `delta`
- `InvokeWorkflowCommandAsync` 反射调用已授权的命令属性
- `InvokeWorkflowMethodAsync` 反射调用已授权的方法
- `GetWorkflowChanges` 用于按 revision 拉取增量变化

### 2.5 新协议请求模式

新协议层所有 Tool 都接收一个 `string requestJson`，其内容本身是 JSON 对象。

常见字段如下：

- `sessionId`：运行时协议会话 id
- `tree`：可选，首次建会话或刷新会话时使用的完整树 JSON
- `revision` / `expectedRevision`：增量同步与乐观并发控制
- `queryMode`：`summary / tree / node / slot / link`
- `includeJson`：是否返回目标对象的实时 JSON 参考
- `id` / `nodeId` / `slotId` / `linkId`：稳定组件 id
- `targetId` / `targetTree`：反射接管目标选择器
- `operations`：批量 Patch 操作数组
- `returnMode`：`delta / affected / snapshot`
- `action`：运行时动作，如 `work / broadcast / close / undo / redo / clearHistory`
- `commandName` / `methodName`：已授权命令或方法名称
- `arguments`：方法调用参数数组

Patch 额外支持：

- `setProperty`：更新单个可写公共属性，支持 `propertyPath`
- `setProperties`：一次更新多个可写公共属性
- `replaceObject`：将完整对象快照合并回现有运行时对象

反射接管约束：

- 只有能够从运行时成员、接口声明、基类声明或字段映射中解析到 `AgentContext` 的属性 / 命令 / 方法才允许被 Agent 调用
- 命令属性支持 `ICommand` 与 `IVeloxCommand`
- 方法支持同步返回值、`Task` 和 `Task<T>`，并自动绑定 JSON 参数

白名单策略：

- 可通过 `WorkflowAgentMemberWhitelist` 对属性、命令、方法分别配置 host-side 白名单
- 若某一类成员未配置白名单，则该类中所有已标注 `AgentContext` 的成员默认可控
- 一旦配置白名单，则只有白名单中的已标注成员可被 Agent 调用

示例：打开协议会话

```json
{
  "tree": {
    "$type": "Demo.ViewModels.Workflow.TreeViewModel, Demo",
    "Nodes": [],
    "Links": []
  },
  "languageCode": "zh"
}
```

示例：dry-run 校验 patch

```json
{
  "sessionId": "workflow-session-id",
  "expectedRevision": 4,
  "operations": [
    {
      "op": "setProperty",
      "nodeId": "node-1",
      "propertyPath": "Anchor.Horizontal",
      "value": 320
    }
  ]
}
```

示例：读取单个属性值

```json
{
  "sessionId": "workflow-session-id",
  "nodeId": "node-1",
  "propertyPath": "Anchor.Horizontal"
}
```

示例：调用命令属性

```json
{
  "sessionId": "workflow-session-id",
  "nodeId": "node-1",
  "commandName": "WorkCommand",
  "parameter": {
    "message": "run"
  }
}
```

示例：调用方法

```json
{
  "sessionId": "workflow-session-id",
  "targetTree": true,
  "methodName": "InitializeWorkflow",
  "arguments": []
}
```

示例：更新单个对象属性

```json
{
  "sessionId": "workflow-session-id",
  "expectedRevision": 4,
  "returnMode": "affected",
  "operations": [
    {
      "op": "setProperty",
      "nodeId": "node-1",
      "propertyPath": "Anchor.Horizontal",
      "value": 320
    },
    {
      "op": "setProperties",
      "targetId": "node-1",
      "properties": {
        "Size.Width": 260,
        "Size.Height": 120
      }
    }
  ]
}
```

示例：查询摘要

```json
{
  "sessionId": "workflow-session-id",
  "queryMode": "summary"
}
```

示例：提交 Patch

```json
{
  "sessionId": "workflow-session-id",
  "expectedRevision": 3,
  "returnMode": "delta",
  "operations": [
    {
      "op": "createNode",
      "node": {
        "$type": "Demo.ViewModels.Workflow.NodeViewModel, Demo"
      }
    }
  ]
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

### 2.7 绑定单个 Tree 的 Agent Scope

如果你的实际场景是：宿主程序先自行创建一个 `Tree` 实例，再把这个现有实例交给 Agent 接管，那么更推荐使用 **绑定作用域**，而不是让 Agent 直接接触全局会话工具。

这种模式的特点是：

- Agent 只能操作当前绑定的 `Tree`
- Agent 无法自行注入别的 `tree` JSON
- Agent 无法自行切换到别的 `sessionId`
- `Undo / Redo` 仍然可用，因为作用域内部会维护该 `Tree` 的运行时会话
- 作用域只暴露新协议层工具，并会自动注入协议会话 id

示例：

```csharp
using VeloxDev.Core.Extension;
using VeloxDev.AI.Workflow;

var tree = new TreeViewModel();

typeof(TreeViewModel).AsWorkflowAgentContextProvider();
typeof(NodeViewModel).AsWorkflowAgentContextProvider();
typeof(SlotViewModel).AsWorkflowAgentContextProvider();
typeof(LinkViewModel).AsWorkflowAgentContextProvider();

using var scope = tree.CreateWorkflowAgentScope();
IEnumerable<Delegate> tools = scope.ProvideWorkflowAgentTools();
```

此时提供给 Agent 的工具仍然包含 `Tree / Node / Slot / Link` 相关操作，但这些操作已经被限制在当前 `tree` 上。

在这种模式下：

- `GetWorkflowTreeState()` 直接读取当前绑定树的快照
- 类似 `CreateWorkflowNode(...)`、`MoveWorkflowNode(...)` 等方法只需要传递局部请求 JSON
- 请求中**不允许**再传入 `sessionId` 或 `tree`

例如，绑定作用域下移动节点只需要：

```json
{
  "nodeIndex": 0,
  "offset": {
    "Left": 120,
    "Top": 40
  }
}
```

这类作用域包装更适合：

- 宿主已经持有运行中的工作流对象
- 只想让 Agent 接管当前画布 / 当前流程
- 不希望 Agent 具备跨工作流切换能力

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
