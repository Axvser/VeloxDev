# Tree

`TreeDefaultViewModel` 是单个工作流的根容器，一个 Tree 实例管理其作用域内的所有节点、插槽和连接。

```csharp
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

public partial class TreeDefaultViewModel : IWorkflowTreeViewModel
{
    [VeloxProperty] private CanvasLayout layout = new();
    [VeloxProperty] private ObservableCollection<IWorkflowNodeViewModel> nodes = [];
    [VeloxProperty] private ObservableCollection<IWorkflowLinkViewModel> links = [];
    [VeloxProperty] private IWorkflowLinkViewModel virtualLink = new LinkDefaultViewModel();
}
```

Tree 还提供了以下能力：

- **撤销/重做**：`SubmitCommand` / `UndoCommand` / `RedoCommand`
- **连接管理**：`SendConnectionCommand` / `ReceiveConnectionCommand`
- **虚拟连线**：拖拽连接时的视觉反馈
