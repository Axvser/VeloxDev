# AOP

Apply aspect-oriented programming via `DispatchProxy` — intercept method calls at runtime with before/after hooks, no compile-time weaving.

---

## Step 1 — Install

```shell
dotnet add package VeloxDev.Core
```

## Step 2 — Define an Interface and Implementation

Paste into `IMyService.cs`:

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

## Step 3 — Create Proxy and Attach Handlers

Paste into `Program.cs`:

```csharp
using VeloxDev.AspectOriented;

var proxy = new MyService().CreateProxy();

// before handler
ProxyHandler logStart = (args, _) =>
{
    Console.WriteLine($">> Add({string.Join(", ", args ?? [])})");
    return null;
};

// after handler
ProxyHandler logEnd = (args, result) =>
{
    Console.WriteLine($"<< Add = {result}");
    return result;
};

proxy.SetProxy(ProxyMembers.Method, nameof(IMyService.Add),
    start: logStart,   // called before
    coverage: null,     // null = use original method
    end: logEnd);       // called after

var result = proxy.Add(3, 4);
// Output:
//   >> Add(3, 4)
//   << Add = 7
Console.WriteLine($"Result: {result}");
```

## How it Works

| Piece | Role |
|-------|------|
| `IAspectOriented` | Marker interface required for proxy creation |
| `[AspectOriented]` | Flags a method/property for interception |
| `CreateProxy<T>()` | Wraps instance in a `DispatchProxy` |
| `ProxyHandler` | Delegate: `object? (object?[]? parameters, object? previous)` |
| `SetProxy(...)` | Attaches `start` (before), `coverage` (replace), `end` (after) handlers |

No reflection per-call overhead — `DispatchProxy` routes via `Invoke`.
