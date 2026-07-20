# Link

> **Build**

```csharp
public class YourLinkHelper() : LinkHelper<LinkViewModel>
{

}
```

> **method**

| Member                                       | Type     | Description         |
| ------------------------------------------ | -------- | ------------------- |
| `Install(IWorkflowLinkViewModel link)`   | `void` | Install to Link     |
| `Uninstall(IWorkflowLinkViewModel link)` | `void` | Uninstall from Link |
| `Delete()`                               | `void` | Delete connection   |