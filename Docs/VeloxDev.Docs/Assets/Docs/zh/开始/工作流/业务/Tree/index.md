# Tree

> **构建**

```csharp
public class YourTreeHelper() : TreeHelper<TreeViewModel>(200) // 200表示一个典型节点的尺寸，用于激活内置的虚拟化支持
{

}
```

> **数据**

| 成员                                                                                   | 类型                                            | 说明             |
| -------------------------------------------------------------------------------------- | ----------------------------------------------- | ---------------- |
| `VisibleItems`                                                                       | `ObservableCollection<IWorkflowViewModel>`    | 当前可见项集合   |
| `Viewport`                                                                           | `Viewport`                                    | 当前视口         |

> **方法**

| 成员                                                                                   | 类型                                            | 说明             |
| -------------------------------------------------------------------------------------- | ----------------------------------------------- | ---------------- |
| `Install(IWorkflowTreeViewModel tree)`                                               | `void`                                        | 安装到 Tree      |
| `Uninstall(IWorkflowTreeViewModel tree)`                                             | `void`                                        | 从 Tree 卸载     |
| `CreateNode(IWorkflowNodeViewModel node)`                                            | `void`                                        | 创建节点         |
| `CreateLink(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)`         | `IWorkflowLinkViewModel`                      | 创建连接         |
| `SetPointer(Anchor anchor)`                                                          | `void`                                        | 设置指针位置     |
| `ValidateConnection(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)` | `bool`                                        | 校验连接是否合法 |
| `SendConnection(IWorkflowSlotViewModel slot)`                                        | `void`                                        | 发起连接         |
| `ReceiveConnection(IWorkflowSlotViewModel slot)`                                     | `void`                                        | 接收连接         |
| `ResetVirtualLink()`                                                                 | `void`                                        | 重置虚拟连接     |
| `Virtualize(Viewport viewport)`                                                      | `void`                                        | 触发视口虚拟化   |
| `Submit(IWorkflowActionPair actionPair)`                                             | `void`                                        | 提交操作         |
| `Redo()`                                                                             | `void`                                        | 重做             |
| `Undo()`                                                                             | `void`                                        | 撤销             |
| `ClearHistory()`                                                                     | `void`                                        | 清空历史记录     |
| `MarkDirty()`                                                                        | `void`                                        | 标记脏状态       |

> **事件**

| 成员                                                                                   | 类型                                            | 说明             |
| -------------------------------------------------------------------------------------- | ----------------------------------------------- | ---------------- |
| `NodeAdded`                                                                          | `event EventHandler<IWorkflowNodeViewModel>?` | 节点新增事件     |
| `NodeRemoved`                                                                        | `event EventHandler<IWorkflowNodeViewModel>?` | 节点移除事件     |
| `LinkAdded`                                                                          | `event EventHandler<IWorkflowLinkViewModel>?` | 连接新增事件     |
| `LinkRemoved`                                                                        | `event EventHandler<IWorkflowLinkViewModel>?` | 连接移除事件     |
| `VisibleItemAdded`                                                                   | `event EventHandler<IWorkflowViewModel>?`     | 可见项新增事件   |
| `VisibleItemRemoved`                                                                 | `event EventHandler<IWorkflowViewModel>?`     | 可见项移除事件   |