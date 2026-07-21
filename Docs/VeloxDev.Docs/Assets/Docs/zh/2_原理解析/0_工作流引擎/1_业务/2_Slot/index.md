# Slot Helper

`IWorkflowSlotViewModelHelper` — Slot 组件的行为委托接口，默认实现为 `SlotHelper`。

---

## Helper 接口：`IWorkflowSlotViewModelHelper`

| 方法 | 调用时机 | 说明 |
|------|----------|------|
| `Install(component)` | 构造函数 | 绑定命令 |
| `Uninstall(component)` | Helper 切换 | 解绑 |
| `SetChannel(channel)` | `SetChannelCommand` | 设置通道类型 |
| `SendConnection()` | `SendConnectionCommand` | 发起连接 |
| `ReceiveConnection()` | `ReceiveConnectionCommand` | 接受连接 |
| `Delete()` | `DeleteCommand` | 删除 Slot |
| `CloseAsync()` | 工作流停止 | 清理资源 |

## 默认实现：`SlotHelper`

```csharp
public class SlotHelper : IWorkflowSlotViewModelHelper
{
    protected IWorkflowSlotViewModel? Component { get; private set; }
    public virtual void Install(IWorkflowSlotViewModel component) { Component = component; }
    public virtual void SetChannel(SlotChannel channel) { /* 设置 Channel */ }
    public virtual void SendConnection() { /* 通过 Tree Helper 发起 */ }
    public virtual void ReceiveConnection() { /* 通过 Tree Helper 接受 */ }
    public virtual void Delete() { /* 移除 */ }
    public virtual async Task CloseAsync() { /* 清理 */ }
}
```

## 自定义示例

```csharp
public class CustomSlotHelper : SlotHelper<CustomSlot>
{
    public override void SetChannel(SlotChannel channel)
    {
        base.SetChannel(channel);
        if (Component is not null)
            Component.OnPropertyChanged(nameof(Component.Channel));
    }
}

[WorkflowBuilder.Slot<CustomSlotHelper>]
public partial class CustomSlot
{
    public CustomSlot() => InitializeWorkflow();
}
```

`SlotHelper` 无泛型约束版本适用于简单场景；有泛型版本 `SlotHelper<TSlot>` 提供类型化的 `Component` 属性。