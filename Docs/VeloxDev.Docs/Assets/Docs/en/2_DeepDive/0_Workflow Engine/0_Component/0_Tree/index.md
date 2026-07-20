# Tree

The `WorkflowTreeViewModel` is the root container for a single workflow.

```csharp
public partial class WorkflowTreeViewModel
{
    public ControllerViewModel Controller { get; set; }
    public ObservableCollection<IWorkflowNodeViewModel> Nodes { get; set; }
    // ...
}
```

All nodes, slots, and links within a workflow are scoped to one Tree instance.
