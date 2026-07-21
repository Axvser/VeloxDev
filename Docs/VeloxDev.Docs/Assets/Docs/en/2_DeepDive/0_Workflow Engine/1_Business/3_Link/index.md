# Business Link

Extend `LinkDefaultViewModel` with `[WorkflowBuilder.Link<THelper>]` for custom connection behavior.

```csharp
using VeloxDev.WorkflowSystem;

public class CustomLinkHelper : LinkHelper<CustomLink>
{
    public override void Delete()
    {
        // Clean up resources before deleting
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
