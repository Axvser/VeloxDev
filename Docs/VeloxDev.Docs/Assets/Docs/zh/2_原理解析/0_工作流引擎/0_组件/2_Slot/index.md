# Slot

`SlotViewModelBase` 是节点上的类型化连接点，维护节点间的连接关系。

```csharp
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

public sealed partial class SlotViewModelBase : IWorkflowSlotViewModel
{
    [VeloxProperty] private ObservableCollection<IWorkflowSlotViewModel> targets = [];
    [VeloxProperty] private ObservableCollection<IWorkflowSlotViewModel> sources = [];
    [VeloxProperty] private SlotChannel channel = SlotChannel.OneBoth;
    [VeloxProperty] private SlotState state = SlotState.StandBy;
    [VeloxProperty] private Anchor anchor = new();
    [VeloxProperty] private IWorkflowNodeViewModel? parent = null;
}
```

| 属性 | 类型 | 描述 |
|------|------|------|
| `Targets` | `ObservableCollection<IWorkflowSlotViewModel>` | 当前 Slot 连接到的目标 Slot |
| `Sources` | `ObservableCollection<IWorkflowSlotViewModel>` | 连接到当前 Slot 的源 Slot |
| `Channel` | `SlotChannel` | 方向约束（`Input` / `Output` / `OneBoth` / `None`）|
| `State` | `SlotState` | 连接状态（`StandBy` / `Connected` / `Linking` / `Error`）|
| `Anchor` | `Anchor` | 画布上的空间位置 |
| `Parent` | `IWorkflowNodeViewModel?` | 所属节点 |
