# Node

> **💡V5.7.0已发生API变更，可前往【版本】章节查看**

> **构建**

```csharp
public class YourNodeHelper() : NodeHelper<NodeViewModel>
{

}
```

> **方法**

| 成员                                       | 类型                                            | 说明               |
| ------------------------------------------ | ----------------------------------------------- | ------------------ |
| `Install(IWorkflowNodeViewModel node)`                                                                                            | `void`                                        | 安装到 Node      |
| `Uninstall(IWorkflowNodeViewModel node)`                                                                                          | `void`                                        | 从 Node 卸载     |
| `CreateSlot(IWorkflowSlotViewModel slot)`                                                                                         | `void`                                        | 创建 Slot        |
| `Move(Offset offset)`                                                                                                             | `void`                                        | 移动节点         |
| `SetAnchor(Anchor newValue)`                                                                                                      | `void`                                        | 设置锚点         |
| `SetSize(Size newValue)`                                                                                                          | `void`                                        | 设置尺寸         |
| `WorkAsync(object? parameter, CancellationToken ct)`                                                                              | `Task`                                        | 执行节点工作     |
| `BroadcastAsync(object? parameter, CancellationToken ct)`                                                                         | `Task`                                        | 正向广播         |
| `ReverseBroadcastAsync(object? parameter, CancellationToken ct)`                                                                  | `Task`                                        | 反向广播         |
| `ValidateBroadcastAsync(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver, object? parameter, CancellationToken ct)` | `Task<bool>`                                  | 校验广播是否合法 |
| `Delete()`                                                                                                                        | `void`                                        | 删除节点         |

> **事件**

| 成员                                       | 类型                                            | 说明               |
| ------------------------------------------ | ----------------------------------------------- | ------------------ |
| `SlotAdded`                                                                                                                       | `event EventHandler<IWorkflowSlotViewModel>?` | Slot 新增事件    |
| `SlotRemoved`                                                                                                                     | `event EventHandler<IWorkflowSlotViewModel>?` | Slot 移除事件    |