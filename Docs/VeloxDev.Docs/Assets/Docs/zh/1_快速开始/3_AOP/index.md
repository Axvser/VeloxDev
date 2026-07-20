# AOP

通过源码生成器在编译时织入横切关注点，无运行时开销。

```csharp
[Aspect]
public partial class LoggingAspect
{
    [Before] void OnEnter(string methodName) => Console.WriteLine($">> {methodName}");
    [After]  void OnExit(string methodName)  => Console.WriteLine($"<< {methodName}");
}
```
