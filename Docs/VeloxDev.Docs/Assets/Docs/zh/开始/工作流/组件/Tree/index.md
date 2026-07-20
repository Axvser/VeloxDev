# Tree

> **构建**

```csharp
[WorkflowBuilder.Tree<TreeHelper>]
public partial class TreeViewModel
{
    // 构造阶段执行自动生成的方法
    public TreeViewModel() => InitializeWorkflow();

    // …… 自由扩展您的工作流树视图模型
}
```

> **数据**

| 成员                                               | 类型                                                                                               | 说明                     |
| -------------------------------------------------- | -------------------------------------------------------------------------------------------------- | ------------------------ |
| `VirtualLink`                                    | `IWorkflowLinkViewModel`                                                                         | 建立连接过程中的虚拟连接 |
| `Nodes`                                          | `ObservableCollection<IWorkflowNodeViewModel>`                                                   | 所有 Node 组件           |
| `Links`                                          | `ObservableCollection<IWorkflowLinkViewModel>`                                                   | 所有 Link 组件           |
|`Layout`                                          | 'CanvasLayout'                                                                                                            | 画布布局                 |               

> **命令**

| 成员                                               | 类型                                                                                               | 说明                     |
| -------------------------------------------------- | -------------------------------------------------------------------------------------------------- | ------------------------ |
| `CreateNodeCommand`                              | `IVeloxCommand`                                                                                  | 创建节点命令             |
| `SetPointerCommand`                              | `IVeloxCommand`                                                                                  | 更新指针位置命令         |
| `ResetVirtualLinkCommand`                        | `IVeloxCommand`                                                                                  | 重置虚拟连接命令         |
| `RedoCommand`                                    | `IVeloxCommand`                                                                                  | 重做命令                 |
| `UndoCommand`                                    | `IVeloxCommand`                                                                                  | 撤销命令                 |