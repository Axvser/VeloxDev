# Link Helper

`IWorkflowLinkViewModelHelper` — Link 组件的行为委托接口，默认实现为 `LinkHelper`。

---

## Helper 接口：`IWorkflowLinkViewModelHelper`

| 方法 | 调用时机 | 说明 |
|------|----------|------|
| `Install(component)` | 构造函数 | 绑定命令 |
| `Uninstall(component)` | Helper 切换 | 解绑 |
| `Delete()` | `DeleteCommand` | 删除连接 |
| `CloseAsync()` | 工作流停止 | 清理资源 |

## 默认实现：`LinkHelper`

```csharp
public class LinkHelper : IWorkflowLinkViewModelHelper
{
    protected IWorkflowLinkViewModel? Component { get; private set; }
    public virtual void Install(IWorkflowLinkViewModel component) { Component = component; }
    public virtual void Uninstall(IWorkflowLinkViewModel component) { Component = default; }
    public virtual void Delete() { /* 从 Links 集合移除 */ }
    public virtual async Task CloseAsync() { /* 清理 */ }
}
```

## 自定义示例

```csharp
public class CustomLinkHelper : LinkHelper<CustomLink>
{
    public override void Delete()
    {
        // 自定义清理资源后删除
        base.Delete();
    }
}

[WorkflowBuilder.Link<CustomLinkHelper>]
public partial class CustomLink
{
    public CustomLink() => InitializeWorkflow();
    [VeloxProperty] private bool usePolyline = true;
}
```

泛型版本 `LinkHelper<TLink>` 提供类型化的 `Component` 属性。