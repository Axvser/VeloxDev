# 业务

通过自定义 Helper 子类注入业务逻辑，使用 `[WorkflowBuilder]` 特性在编译时构建 ViewModel。

## 自定义 Node Helper

继承 `NodeHelper<T>` 并重写虚方法：

```csharp
using VeloxDev.WorkflowSystem;

public class MyNodeHelper : NodeHelper<MyNodeViewModel>
{
    // 当节点在执行链中收到数据时调用
    public override Task WorkAsync(object? parameter, CancellationToken ct)
    {
        Console.WriteLine($"处理: {parameter}");
        return Task.CompletedTask;
    }
}

[WorkflowBuilder.Node<MyNodeHelper>]
public partial class MyNodeViewModel
{
    public MyNodeViewModel() => InitializeWorkflow();

    [VeloxProperty] private string inputValue = "";
}
```

## 可用的 `[WorkflowBuilder]` 特性

| 特性 | 目标 | Helper 接口 |
|------|------|-------------|
| `[WorkflowBuilder.Tree<THelper>]` | Tree | `IWorkflowTreeViewModelHelper` |
| `[WorkflowBuilder.Node<THelper>]` | Node | `IWorkflowNodeViewModelHelper` |
| `[WorkflowBuilder.Slot<THelper>]` | Slot | `IWorkflowSlotViewModelHelper` |
| `[WorkflowBuilder.Link<THelper>]` | Link | `IWorkflowLinkViewModelHelper` |

源码生成器读取 Helper 类型后自动生成命令、事件绑定等基础设施。
