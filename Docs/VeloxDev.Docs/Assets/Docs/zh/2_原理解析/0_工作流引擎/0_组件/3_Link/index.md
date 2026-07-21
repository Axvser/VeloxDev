# Link

`LinkDefaultViewModel` 表示两个 Slot 之间的可视化连接线。

```csharp
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

public sealed partial class LinkDefaultViewModel : IWorkflowLinkViewModel
{
    [VeloxProperty] private IWorkflowSlotViewModel sender = new SlotDefaultViewModel();
    [VeloxProperty] private IWorkflowSlotViewModel receiver = new SlotDefaultViewModel();
    [VeloxProperty] private bool isVisible = false;
}
```

创建连接只需设置 `Sender` 和 `Receiver`，然后添加到 Tree 的 `Links` 集合中。各平台适配层负责将其渲染为贝塞尔曲线或折线。
