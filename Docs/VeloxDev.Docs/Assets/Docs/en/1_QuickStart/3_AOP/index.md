# AOP

Intercept method/property access at runtime via `DispatchProxy` — three handler slots (start/coverage/end), no compile-time weaving required.

---

## Demo

```csharp
// No need to modify ViewModel source code
var proxy = data.Aop();  // Get or create a cached DispatchProxy proxy

// Intercept property getter with a before-handler
proxy.SetProxy(ProxyMembers.Getter, nameof(TeamViewModel.Name),
    (_, _) => { Console.WriteLine("Name was read"); return null; },
    null, null);
```

## Steps

### 1. Install

```shell
dotnet add package VeloxDev.Core
```

### 2. Define a Class with `[AspectOriented]`

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

### 3. Get the Proxy and Attach Handlers

```csharp
var data = new TeamViewModel();
var proxy = data.Aop();  // ① First call creates and caches the DispatchProxy

// ② Attach handlers for Getter/Setter/Method
proxy.SetProxy(ProxyMembers.Getter, nameof(TeamViewModel.Name),
    (_, _) => { /* before: runs before getter */ return null; },
    null, null);

proxy.SetProxy(ProxyMembers.Setter, nameof(TeamViewModel.Name),
    null, null,
    (p, _) => { /* after: runs after setter */ Console.WriteLine($"New value={p?[0]}"); return null; });

// coverage returns non-null to bypass the original method
proxy.SetProxy(ProxyMembers.Method, nameof(TeamViewModel.Reset),
    null,
    (_, _) => { Console.WriteLine("Reset was cancelled"); return null; },
    null);

// All calls through the proxy are intercepted
var name = proxy.Name;  // Getter → start fires
proxy.Name = "New Team"; // Setter → end fires
proxy.Reset();           // Method → coverage replaces
```

### 4. Run

```shell
dotnet run
```

## Handler Reference

| Handler | Position | Return Value Meaning |
|---------|----------|---------------------|
| **start** (before) | Before original member | Passed to coverage; ignored |
| **coverage** (replace) | Decides whether to execute | `null` = run original; non-null = replacement result |
| **end** (after) | After original (or coverage) | Returned to caller |

Full example: [Examples/AOP/WPF](https://github.com/Axvser/VeloxDev/tree/master/Examples/AOP/WPF/Demo)
