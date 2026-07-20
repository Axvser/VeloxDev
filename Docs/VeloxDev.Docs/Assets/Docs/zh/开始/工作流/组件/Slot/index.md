# Slot

> **构建**

```csharp
[WorkflowBuilder.Slot<SlotHelper>]
public partial class SlotViewModel
{
    // 构造阶段执行自动生成的方法
    public SlotViewModel() => InitializeWorkflow();

    // …… 自由扩展您的工作流连接器视图模型
}
```

> **数据**

| 成员                                               | 类型                                             | 说明                       |
| -------------------------------------------------- | ------------------------------------------------ | -------------------------- |
| `Targets`                                        | `ObservableCollection<IWorkflowSlotViewModel>` | 当前 Slot 指向的目标集合   |
| `Sources`                                        | `ObservableCollection<IWorkflowSlotViewModel>` | 当前 Slot 接收到的来源集合 |
| `Parent`                                         | `IWorkflowNodeViewModel?`                      | 所属 Node                  |
| `Channel`                                        | `SlotChannel`                                  | Slot 通道类型              |
| `State`                                          | `SlotState`                                    | Slot 连接状态              |
| `Anchor`                                         | `Anchor`                                       | Slot 锚点位置              |

> **命令**

| 成员                                               | 类型                                             | 说明                       |
| -------------------------------------------------- | ------------------------------------------------ | -------------------------- |
| `SetChannelCommand`                              | `IVeloxCommand`                                | 设置通道命令               |
| `SendConnectionCommand`                          | `IVeloxCommand`                                | 发起连接命令               |
| `ReceiveConnectionCommand`                       | `IVeloxCommand`                                | 接收连接命令               |
| `DeleteCommand`                                  | `IVeloxCommand`                                | 删除 Slot 命令             |