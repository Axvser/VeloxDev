# Slot

> **构建**

```csharp
public class YourSlotHelper() : SlotHelper<SlotViewModel>
{

}
```

> **方法**

| 成员                                       | 类型                                            | 说明               |
| ------------------------------------------ | ----------------------------------------------- | ------------------ |
| `Install(IWorkflowSlotViewModel slot)`   | `void`                                        | 安装到 Slot        |
| `Uninstall(IWorkflowSlotViewModel slot)` | `void`                                        | 从 Slot 卸载       |
| `SetChannel(SlotChannel channel)`        | `void`                                        | 设置通道           |
| `UpdateState()`                          | `void`                                        | 更新状态           |
| `SendConnection()`                       | `void`                                        | 发起连接           |
| `ReceiveConnection()`                    | `void`                                        | 接收连接           |
| `Delete()`                               | `void`                                        | 删除 Slot          |

> **事件**

| 成员                                       | 类型                                            | 说明               |
| ------------------------------------------ | ----------------------------------------------- | ------------------ |
| `TargetAdded`                            | `event EventHandler<IWorkflowSlotViewModel>?` | 目标 Slot 新增事件 |
| `TargetRemoved`                          | `event EventHandler<IWorkflowSlotViewModel>?` | 目标 Slot 移除事件 |
| `SourceAdded`                            | `event EventHandler<IWorkflowSlotViewModel>?` | 来源 Slot 新增事件 |
| `SourceRemoved`                          | `event EventHandler<IWorkflowSlotViewModel>?` | 来源 Slot 移除事件 |