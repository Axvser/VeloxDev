# Tree

The `TreeViewModelBase` is the root container for a single workflow.

```csharp
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

public partial class TreeViewModelBase : IWorkflowTreeViewModel
{
    [VeloxProperty] private CanvasLayout layout = new();
    [VeloxProperty] private ObservableCollection<IWorkflowNodeViewModel> nodes = [];
    [VeloxProperty] private ObservableCollection<IWorkflowLinkViewModel> links = [];
    [VeloxProperty] private Dictionary<IWorkflowSlotViewModel, Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>> linksMap = [];
    [VeloxProperty] private IWorkflowLinkViewModel virtualLink = new LinkViewModelBase();
}
```

All nodes, slots, and links within a workflow are scoped to one Tree instance. The Tree also manages:

- **Undo/Redo** via `SubmitCommand` / `UndoCommand` / `RedoCommand`
- **Connection workflow** via `SendConnectionCommand` / `ReceiveConnectionCommand`
- **Virtual link** for visual feedback during drag-connect
