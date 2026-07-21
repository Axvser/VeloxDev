# 业务（Helper 模式）

工作流四组件将行为委托给 **Helper** 对象。Helper 由源码生成器自动注入，实现**策略模式**——用户通过继承默认 Helper 并重写方法注入业务逻辑，无需修改组件核心。

---

## 机制

```csharp
// 用户代码
[WorkflowBuilder.Node<HttpHelper<NodeViewModel>>(workSemaphore: 5)]
public partial class NodeViewModel { ... }

// 生成器输出（.g.cs）
public partial class NodeViewModel : IWorkflowNodeViewModel
{
    private IWorkflowNodeViewModelHelper helper = new HttpHelper<NodeViewModel>();
    public void InitializeWorkflow() { helper.Install(this); }
}
```

## [WorkflowBuilder] 特性一览

| 特性 | 目标 | Helper 接口 | 默认 Helper |
|------|------|-------------|-------------|
| `[WorkflowBuilder.Tree<THelper>]` | Tree | `IWorkflowTreeViewModelHelper` | `TreeHelper<TTree>` |
| `[WorkflowBuilder.Node<THelper>]` | Node | `IWorkflowNodeViewModelHelper` | `NodeHelper<TNode>` |
| `[WorkflowBuilder.Slot<THelper>]` | Slot | `IWorkflowSlotViewModelHelper` | `SlotHelper` |
| `[WorkflowBuilder.Link<THelper>]` | Link | `IWorkflowLinkViewModelHelper` | `LinkHelper` |

## 标准扩展点

`VeloxDev.WorkflowSystem.StandardEx` 提供一组标准扩展方法，可在 Helper 中调用：

| 扩展方法 | 用途 |
|----------|------|
| `GetStandardCommands()` | 获取组件全部标准命令 |
| `StandardCreateSlot(slot)` | 带撤销支持的 Slot 创建 |
| `StandardClosing/StandardClosingAsync()` | 锁定所有命令 |
| `StandardClose/StandardCloseAsync()` | 清除所有命令 |

## 自定义 Helper 步骤

```csharp
// 1. 继承默认 Helper
public class HttpHelper<TNode> : NodeHelper<TNode> where TNode : IWorkflowNodeViewModel
{
    public override async Task WorkAsync(object? parameter, CancellationToken ct)
    {
        var url = GetUrlFromProperty();
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync(url, ct);
    }
}

// 2. 通过 [WorkflowBuilder.*] 指定
[WorkflowBuilder.Node<HttpHelper<NodeViewModel>>(workSemaphore: 5)]
public partial class NodeViewModel { ... }
```

> 如果不指定 `[WorkflowBuilder.*]`，class 不会获得工作流接口和任何命令。必须始终使用该特性。

各组件 Helper 的可重写方法详见以下子页面。
