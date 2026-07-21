# AOP

通过 `DispatchProxy` 在运行时拦截方法调用 —— start/coverage/end 三处理器，无需编译时织入。

---

## Demo 效果

```csharp
// 无需修改 ViewModel 源码
var proxy = data.Aop();  // 获取或创建 AOP 代理（自动缓存）

// 为 Name 属性的 Getter 添加前置拦截
proxy.SetProxy(ProxyMembers.Getter, nameof(TeamViewModel.Name),
    (_, _) => { Console.WriteLine("Name 被读取了"); return null; },
    null, null);
```

## 操作步骤

### 1. 安装

```shell
dotnet add package VeloxDev.Core
```

### 2. 定义接口和实现

接口必须继承 `IAspectOriented`，需要拦截的方法标记 `[AspectOriented]`：

```csharp
using VeloxDev.AspectOriented;
using VeloxDev.MVVM;

public partial class TeamViewModel
{
    [VeloxProperty][AspectOriented] private string _name = string.Empty;

    [AspectOriented]
    public void Reset()
    {
        Name = string.Empty;
    }
}
```

### 3. 获取代理并通过 SetProxy 附加处理器

```csharp
var data = new TeamViewModel();
var proxy = data.Aop();  // ① 获取/创建 DispatchProxy 代理（自动缓存）

// ② 为 Getter/Setter/Method 分别设置处理器
proxy.SetProxy(ProxyMembers.Getter,
    nameof(TeamViewModel.Name),           // 目标成员
    (_, _) => { /* 前置：读取前执行 */ return null; },  // start
    null,                                 // coverage：null=执行原方法
    null);                                // end

proxy.SetProxy(ProxyMembers.Setter,
    nameof(TeamViewModel.Name),
    null,
    null,
    (p, _) => { /* 后置：修改后执行 */ Console.WriteLine($"新值={p?[0]}"); return null; });

// coverage 返回非 null 时，替代原方法执行
proxy.SetProxy(ProxyMembers.Method,
    nameof(TeamViewModel.Reset),
    null,
    (_, _) => { Console.WriteLine("Reset 被取消了"); return null; }, // 替代原逻辑
    null);

// 所有通过 proxy 的调用均被拦截
var name = proxy.Name;  // Getter → start 触发
proxy.Name = "新团队";   // Setter → end 触发
proxy.Reset();           // Method → coverage 替代原方法
```

### 4. 运行

```shell
dotnet run
```

## 处理器类型

| 处理器 | 位置 | 返回值含义 |
|--------|------|-----------|
| **start**（前置） | 原成员执行前 | 传递给 coverage |
| **coverage**（替代） | 决定是否执行原成员 | `null` = 执行原方法；非 null = 替代结果 |
| **end**（后置） | 原成员执行后 | 返回给调用方 |

完整示例见 [Examples/AOP/WPF](https://github.com/Axvser/VeloxDev/tree/master/Examples/AOP/WPF/Demo)
