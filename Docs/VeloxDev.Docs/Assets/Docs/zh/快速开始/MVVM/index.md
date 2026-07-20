# MVVM

使用 `[VeloxProperty]` 和 `[VeloxCommand]` 消除 MVVM 样板代码。

```csharp
public partial class MainViewModel
{
    [VeloxProperty] private string _name = "世界";
    [VeloxCommand] private void SayHello() => Console.WriteLine($"Hello {Name}");
}
```

生成器自动创建 `INotifyPropertyChanged` 属性和 `ICommand` 包装，零运行时依赖。
