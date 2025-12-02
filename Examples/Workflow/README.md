# VeloxDev Workflow System  

**声明式节点工作流框架 · 编译时生成 · 命令驱动 · 完整 Undo/Redo**

---

## ✍️ 1. 代码怎么写？

### 步骤 1：定义组件 ViewModel（仅需标记特性）
```csharp
// 根容器
[WorkflowBuilder.ViewModel.Tree<TreeHelper>]
public partial class MyTree { }

// 可执行节点（支持并发限制）
[WorkflowBuilder.ViewModel.Node<NodeHelper>(workSemaphore: 3)]
public partial class MyNode { }

// 控制器节点（无任务逻辑）
[WorkflowBuilder.ViewModel.Node<CtrlHelper>]
public partial class MyController { }

// 插槽与连接线
[WorkflowBuilder.ViewModel.Slot<SlotHelper>]
public partial class MySlot { }

[WorkflowBuilder.ViewModel.Link<LinkHelper>]
public partial class MyLink { }
```
> ✅ 所有属性（Anchor/Size/Nodes...）、命令（Move/Create/Delete...）由 Source Generator 自动生成。  
> ✅ 构造函数中必须调用 `InitializeWorkflow()`。

---

### 步骤 2：实现 Helper（注入业务逻辑）
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

### 步骤 3：在 View 中绑定交互
```csharp
// 拖拽
node.MoveCommand.Execute(new Offset(dx, dy));

// 连线
slot.ApplyConnectionCommand.Execute(null);   // 开始
slot.ReceiveConnectionCommand.Execute(null); // 结束

// 保存/加载（使用独立序列化扩展）
string json = tree.Mutualize();                          // 序列化
bool ok = json.TryDeMutualize(out MyTree? tree);         // 反序列化
```

---

## 📚 2. 核心 API 列表

### 自动生成的命令
| 命令 | 参数 | 作用 |
|------|------|------|
| `MoveCommand` | `Offset` | 移动节点 |
| `CreateSlotCommand` | `SlotViewModel` | 添加插槽 |
| `DeleteCommand` | `null` | 删除自身 |
| `ApplyConnectionCommand` | `null` | 设为连接发起端 |
| `ReceiveConnectionCommand` | `null` | 设为连接接收端 |
| `UndoCommand` / `RedoCommand` | `null` | 撤销/重做 |

### 序列化扩展（来自 `WorkflowEx`）
| 方法 | 说明 |
|------|------|
| `T.Mutualize()` | 同步序列化为 JSON |
| `json.TryDeMutualize<T>(out T?)` | 安全反序列化 |
| `stream.TryDeMutualizeFromStreamAsync<T>()` | 异步从流加载 |

### 关键数据模型
| 类型 | 字段 |
|------|------|
| `Anchor` | `X`, `Y`, `ZIndex` |
| `Offset` | `DX`, `DY` |
| `Size` | `Width`, `Height` |

---

> 💡 **一句话使用**：  
> **标记 `[Tree/Node/Slot/Link]` → 实现 `Helper` → 用 `MoveCommand`/`ApplyConnectionCommand` 驱动交互 → 通过 `Mutualize()`/`TryDeMutualize()` 保存加载。**