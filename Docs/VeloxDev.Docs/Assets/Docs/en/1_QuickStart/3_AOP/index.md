# AOP

Apply aspect-oriented programming without runtime overhead.

## Quick Example

```csharp
[Aspect]
public partial class LoggingAspect
{
    [Before]
    void OnEnter(string methodName, object[] args) =>
        Console.WriteLine($">> {methodName}");

    [After]
    void OnExit(string methodName, object result) =>
        Console.WriteLine($"<< {methodName} = {result}");
}

[LoggingAspect]
public partial class MyService
{
    public Task<int> CalculateAsync(int x, int y) => Task.FromResult(x + y);
}
```

The source generator weaves `Before`/`After` calls into every public method at compile time — no runtime proxies, no reflection.
