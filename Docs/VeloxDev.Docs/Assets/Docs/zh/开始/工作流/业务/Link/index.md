# Link

> **构建**

```csharp
public class YourLinkHelper() : LinkHelper<LinkViewModel>
{

}
```

> **方法**

| 成员                                       | 类型     | 说明         |
| ------------------------------------------ | -------- | ------------ |
| `Install(IWorkflowLinkViewModel link)`   | `void` | 安装到 Link  |
| `Uninstall(IWorkflowLinkViewModel link)` | `void` | 从 Link 卸载 |
| `Delete()`                               | `void` | 删除连接     |