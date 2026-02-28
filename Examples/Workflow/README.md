# VeloxDev Workflow System  

**节点工作流框架 · 编译时生成 · 序列化支持 · 并发支持 · 命令驱动 · 完整 Undo/Redo**

---

## ✍️ 1. 代码怎么写？

### 步骤 1：定义组件 ViewModel

> 构造函数中必须调用 `InitializeWorkflow()`，否则一些机制不会生效

```csharp
// 根容器
[WorkflowBuilder.ViewModel.Tree<TreeHelper>]
public partial class MyTree { }

// 可执行节点（支持并发）
[WorkflowBuilder.ViewModel.Node<NodeHelper>(workSemaphore: 3)]
public partial class MyNode { }

// 连接器
[WorkflowBuilder.ViewModel.Slot<SlotHelper>]
public partial class MySlot { }

// 连线
[WorkflowBuilder.ViewModel.Link<LinkHelper>]
public partial class MyLink { }
```

---

### 步骤 2：实现 Helper（注入业务逻辑）

> 可以直接使用类库提供的基础Helper，也可以继承基础Helper并重写

```csharp
public partial class NodeHelper : WorkflowHelper.ViewModel.Node
{
    public override async Task WorkAsync(object? param, CancellationToken ct)
    {
        await YourTask(ct);
        await BroadcastAsync(param, ct); // 广播到下游
    }

    public override Task<bool> ValidateBroadcastAsync(...) => Task.FromResult(true);
}
```

---

### 步骤 3：在 View 中处理交互

> 原则上，需要为GUI写附加属性，只是条件受限，我们需要逐步推进

```csharp
// 拖拽
node.MoveCommand.Execute(new Offset(dx, dy));

// 连线
slot.SendConnectionCommand.Execute(null);   // 开始
slot.ReceiveConnectionCommand.Execute(null); // 结束

// 保存/加载（使用独立序列化扩展）
string json = tree.Serialize();                          // 序列化
bool ok = json.TryDeSerialize(out MyTree? tree);         // 反序列化
```

---

## 📚 2. API 概览

> 组件相关的API请以仓库中的 Xmind 图为准，此处仅列举较为核心的API

### 关键命令
| 命令 | 参数 | 作用 |
|------|------|------|
| `WorkCommand` | `object` | 执行工作 |
| `DeleteCommand` | `null` | 删除自身 |
| `SendConnectionCommand` | `null` | 设为连接发起端 |
| `ReceiveConnectionCommand` | `null` | 设为连接接收端 |
| `UndoCommand` / `RedoCommand` | `null` | 撤销/重做 |
| ... |||

### 序列化扩展（来自 `VeloxDev.Core.Extension`）
| 方法 | 说明 |
|------|------|
| `T.Serialize()` | 同步序列化为 JSON |
| `json.TryDeSerialize<T>(out T?)` | 安全反序列化 |
| `stream.TryDeSerializeFromStreamAsync<T>()` | 异步从流加载 |
| ... ||

### 视觉数据模型
| 类型 | 属性 | 方法 |
|------|------|------|
| `Anchor` | `X`, `Y`, `Layer` |
| `Offset` | `X`, `Y` |
| `Scale` | `X`, `Y` |
| `Size` | `Width`, `Height` |
| `VisualPoint` | `X`,`Y`,`Unit`,`Alignment` |
| `Viewport` | `Left`,`Top`,`Width`,`Height` | `IntersectsWith`,`Contains` |
| `SpatialHashMap` |  | `Insert`,`Remove`,`Query`,`Clear` |

---

> 💡 更多点子请参考Workflow的Avalonia例子，我们会优先维护此demo，使其具有一切已有、新增功能，并在恰当的时候并入核心库