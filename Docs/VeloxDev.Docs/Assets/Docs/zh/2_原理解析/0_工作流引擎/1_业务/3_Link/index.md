# Link

通过 `[WorkflowBuilder.Link<THelper>]` 扩展 `LinkDefaultViewModel`。

```csharp
using VeloxDev.WorkflowSystem;

public class CustomLinkHelper : LinkHelper<CustomLink>
{
    public override void Delete()
    {
        // 清理资源后删除
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
