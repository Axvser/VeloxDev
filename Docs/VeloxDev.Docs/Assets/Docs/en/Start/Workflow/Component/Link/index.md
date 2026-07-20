# Link

> **build**

```csharp
[WorkflowBuilder.Link<LinkHelper>]
public partial class LinkViewModel
{
    // Executes auto-generated methods during the construction phase
    public LinkViewModel() => InitializeWorkflow();

    // …… Freely extend your workflow connection line view model
}
```

> **data**

| 成员                                               | 类型                             | 说明            |
| -------------------------------------------------- | -------------------------------- | --------------- |
| `Sender`                                         | `IWorkflowSlotViewModel`       | Connection initiator      |
| `Receiver`                                       | `IWorkflowSlotViewModel`       | Connection receiver      |
| `IsVisible`                                      | `bool`                         | Whether the connection is visible    |

> **Command**

| Member          | Type            | Description                  |
| --------------- | --------------- | ---------------------------- |
| `DeleteCommand` | `IVeloxCommand` | Delete connection command    |