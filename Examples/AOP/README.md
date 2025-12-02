# VeloxDev AOP Framework

零侵入、基于代理的运行时 AOP，支持属性读写拦截、方法覆写与扩展。  
**无需修改 ViewModel 源码，自动织入横切逻辑。**

---

## ✍️ 1. 代码怎么写？

### 步骤 1：标记可代理成员
在 ViewModel 中用 `[AspectOriented]` 标记需拦截的 **属性或方法**：
```csharp
public partial class TeamViewModel
{
    [VeloxProperty][AspectOriented] 
    private string _name = string.Empty; // Name 属性可被拦截

    [AspectOriented]
    public void Reset() { ... } // 方法可被拦截
}
```

> 💡 `[VeloxProperty]` 是 MVVM 自动生成属性用的，`[AspectOriented]` 声明该成员支持 AOP。

---

### 步骤 2：配置代理钩子
在 View 或启动代码中，通过 `Proxy.SetProxy()` 注册逻辑：
```csharp
var teamData = new TeamViewModel();
var teamProxy = ConfigureAOP(teamData); // 返回代理对象

// 使用 teamProxy 而非 teamData！
teamProxy.Name = "New Team"; // 触发后置钩子
```

#### 钩子类型（按需注册）
```csharp
// 1. 属性读取前（Getter 前置）
data.Proxy.SetProxy(ProxyMembers.Getter, nameof(TeamViewModel.Name), 
    (args, _) => { /* 前置逻辑 */ return null; }, 
    null, null);

// 2. 属性写入后（Setter 后置）
data.Proxy.SetProxy(ProxyMembers.Setter, nameof(TeamViewModel.Name), 
    null, null, 
    (args, _) => { /* 后置逻辑，args[0] 是新值 */ });

// 3. 覆写方法（完全替换原逻辑）
data.Proxy.SetProxy(ProxyMembers.Method, nameof(TeamViewModel.Reset), 
    null, 
    (args, _) => { /* 新逻辑 */ return null; }, 
    null);

// 4. 扩展方法（原逻辑后追加）
data.Proxy.SetProxy(ProxyMembers.Method, nameof(TeamViewModel.AOP_OnMemberAdded), 
    null, null, 
    (args, _) => { /* 扩展逻辑 */ });
```

> 📌 **SetProxy 三钩子顺序**：`(前置, 覆写, 后置)`  
> - **前置**：执行前拦截  
> - **覆写**：跳过原方法，直接返回  
> - **后置**：原方法执行后回调  

---

## 📚 2. 核心 API 列表

### `ProxyEx.SetProxy<T>`
```csharp
void SetProxy(
    T target,
    ProxyMembers memberType,   // Getter / Setter / Method
    string memberName,         // 成员名（属性去 get_/set_ 前缀）
    ProxyHandler? start,       // 前置钩子
    ProxyHandler? coverage,    // 覆写钩子
    ProxyHandler? end          // 后置钩子
)
```

### `ProxyHandler` 签名
```csharp
delegate object? ProxyHandler(
    object?[]? parameters,     // 方法/属性调用的参数数组
    object? previous           // 上一钩子返回值（覆写时可忽略）
);
```
- **Setter 参数**：`parameters[0]` = 新值  
- **Getter 返回**：可通过覆写钩子返回自定义值  
- **Method 参数**：按声明顺序传入

### 枚举：`ProxyMembers`
| 值 | 用途 |
|----|------|
| `Getter` | 拦截属性读取（如 `team.Name`） |
| `Setter` | 拦截属性赋值（如 `team.Name = "x"`） |
| `Method` | 拦截方法调用（如 `team.Reset()`） |

### 关键约定
- **代理对象类型**：原始类型 `T` → 代理类型 `T_ProxyNamespace_Aop`（由 `CreateProxy` 生成）
- **集合变更扩展**：通过 `AOP_OnMemberAdded` / `AOP_OnMemberRemoved` 拦截 `ObservableCollection` 变化
- **必须使用代理对象**：原始对象无 AOP 效果！

---

> 💡 **一行原则**：标记 `[AspectOriented]` → 配置 `SetProxy` 钩子 → 使用代理对象，即可实现日志、验证、通知等横切关注点。