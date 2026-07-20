# Link

> **构建**

```csharp
[WorkflowBuilder.Link<LinkHelper>]
public partial class LinkViewModel
{
    // 构造阶段执行自动生成的方法
    public LinkViewModel() => InitializeWorkflow();

    // …… 自由扩展您的工作流连接线视图模型
}
```

> **数据**

| 成员                                               | 类型                             | 说明            |
| -------------------------------------------------- | -------------------------------- | --------------- |
| `Sender`                                         | `IWorkflowSlotViewModel`       | 连接发起方      |
| `Receiver`                                       | `IWorkflowSlotViewModel`       | 连接接收方      |
| `IsVisible`                                      | `bool`                         | 连接是否可见    |

> **命令**

| 成员                                               | 类型                             | 说明            |
| -------------------------------------------------- | -------------------------------- | --------------- |
| `DeleteCommand`                                  | `IVeloxCommand`                | 删除连接命令    |