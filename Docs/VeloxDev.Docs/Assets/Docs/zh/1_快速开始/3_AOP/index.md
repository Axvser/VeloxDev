# AOP

通过 `DispatchProxy` 在运行时拦截方法调用，前置/后置钩子，无需编译时织入。

---

## 第一步 — 安装

```shell
dotnet add package VeloxDev.Core
```

## 第二步 — 定义接口和实现

粘贴到 `IMyService.cs`：

```csharp
using VeloxDev.AspectOriented;

public interface IMyService : IAspectOriented
{
    int Add(int x, int y);
}

public class MyService : IMyService
{
    [AspectOriented]
    public int Add(int x, int y) => x + y;
}
```

## 第三步 — 创建代理并附加处理器

粘贴到 `Program.cs`：

```csharp
using VeloxDev.AspectOriented;

var proxy = new MyService().CreateProxy();

// 前置处理器
ProxyHandler logStart = (args, _) =>
{
    Console.WriteLine($">> Add({string.Join(", ", args ?? [])})");
    return null;
};

// 后置处理器
ProxyHandler logEnd = (args, result) =>
{
    Console.WriteLine($"<< Add = {result}");
    return result;
};

proxy.SetProxy(ProxyMembers.Method, nameof(IMyService.Add),
    start: logStart,   // 调用前执行
    coverage: null,     // null = 执行原方法
    end: logEnd);       // 调用后执行

var result = proxy.Add(3, 4);
// 输出：
//   >> Add(3, 4)
//   << Add = 7
Console.WriteLine($"结果: {result}");
```

## 组件说明

| 组件 | 作用 |
|------|------|
| `IAspectOriented` | 标记接口，代理创建的必要条件 |
| `[AspectOriented]` | 标记需要拦截的方法/属性 |
| `CreateProxy<T>()` | 将实例包装为 `DispatchProxy` |
| `ProxyHandler` | 委托：`object? (object?[]? parameters, object? previous)` |
| `SetProxy(...)` | 附加 `start`（前置）、`coverage`（替代）、`end`（后置）处理器 |

每次调用无反射开销 — `DispatchProxy` 通过 `Invoke` 路由。
