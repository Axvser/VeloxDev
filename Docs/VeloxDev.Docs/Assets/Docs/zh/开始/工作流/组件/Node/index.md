# Node

> **构建**

```csharp
[WorkflowBuilder.Node<NodeHelper>]
public partial class NodeViewModel
{
    // 构造阶段执行自动生成的方法
    public NodeViewModel() => InitializeWorkflow();

    // …… 自由扩展您的工作流节点视图模型
}
```

> **特殊成员**

节点视图模型在定义阶段，有两种特殊成员：
    - IWorkflowSlotViewModel 派生类，必须标记 [VeloxProperty]
    - SlotEnumerator<T> ，可以将 bool 或者 enum 作为条件分支，集合内部按照条件生成多个Slot，也必须标记 [VeloxProperty]

如此一来，稍后您在视图中就可以直接从 InputSlot 属性 + OutputSlots 集合渲染多个连接器，需要注意的是，此时UI负责跟踪Slot的坐标（Anchor）并传回视图模型，若UI缺失了这种坐标跟踪，那么您的Link组件的起始、终止位置就无法正确计算

```csharp
[VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

[VeloxProperty] public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }

public NodeViewModel()
{
    InitializeWorkflow();

    OutputSlots.SetSelector(typeof(bool)); // 为SlotEnumerator设定条件分支，以bool为例，会内部生成名为 True、名为False的两个Slot组件
}
```

SlotEnumerator<T> 实现 IEnumerable 接口，子元素定义如下：Name是枚举成员的名称，Value是枚举值，Slot是自动构造的Slot组件

```csharp
public partial class ConditionalSlot<TSlot>
    where TSlot : IWorkflowSlotViewModel, new()
{
    [VeloxProperty] private string _name = string.Empty;
    [VeloxProperty] private object? _value;
    [VeloxProperty] private TSlot _slot = new();
}
```

> **数据**

| 成员                                               | 类型                                             | 说明                 |
| -------------------------------------------------- | ------------------------------------------------ | -------------------- |
| `Parent`                                         | `IWorkflowTreeViewModel?`                      | 所属 Tree            |
| `Anchor`                                         | `Anchor`                                       | 节点锚点坐标         |
| `Size`                                           | `Size`                                         | 节点尺寸             |
| `Slots`                                          | `ObservableCollection<IWorkflowSlotViewModel>` | 节点拥有的 Slot 集合 |

> **命令**

| 成员                                               | 类型                                             | 说明                 |
| -------------------------------------------------- | ------------------------------------------------ | -------------------- |
| `MoveCommand`                                    | `IVeloxCommand`                                | 移动节点命令         |
| `SetAnchorCommand`                               | `IVeloxCommand`                                | 设置锚点命令         |
| `SetSizeCommand`                                 | `IVeloxCommand`                                | 设置尺寸命令         |
| `CreateSlotCommand`                              | `IVeloxCommand`                                | 创建 Slot 命令       |
| `DeleteCommand`                                  | `IVeloxCommand`                                | 删除节点命令         |
| `WorkCommand`                                    | `IVeloxCommand`                                | 执行工作命令         |
| `BroadcastCommand`                               | `IVeloxCommand`                                | 正向广播命令         |
| `ReverseBroadcastCommand`                        | `IVeloxCommand`                                | 反向广播命令         |