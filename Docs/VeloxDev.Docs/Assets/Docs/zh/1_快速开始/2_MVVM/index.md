# MVVM

用 `[VeloxProperty]` 消除 `INotifyPropertyChanged` 样板代码，用 `[VeloxCommand]` 自动生成 `ICommand`。

---

## 第一步 — 安装

```shell
dotnet add package VeloxDev.Core
```

## 第二步 — 创建 ViewModel（粘贴到 `MainViewModel.cs`）

```csharp
using VeloxDev.MVVM;

public partial class MainViewModel
{
    [VeloxProperty] private string _name = "世界";
    [VeloxProperty] private int _count;

    [VeloxCommand]
    private void Increment() => Count++;

    [VeloxCommand(canValidate: true)]
    private async Task SaveAsync(object? parameter)
    {
        await Task.Delay(100);
        Console.WriteLine($"已保存：{Name}，Count={Count}");
    }

    // SaveAsync 的 CanExecute 伴侣方法
    private bool CanSave() => !string.IsNullOrWhiteSpace(Name);
}
```

## 第三步 — XAML 绑定

```xml
<StackPanel>
    <TextBox Text="{Binding Name}" />
    <TextBlock Text="{Binding Count}" />
    <Button Command="{Binding IncrementCommand}" Content="+" />
    <Button Command="{Binding SaveCommand}" Content="保存" />
</StackPanel>
```

## 生成器产出

`[VeloxProperty] private string _name` 生成：
- `public string Name { get; set; }` + 完整 `INotifyPropertyChanged`
- `partial void OnNameChanged(string oldValue, string newValue)` 钩子

`[VeloxCommand] private void Increment()` 生成：
- `public IVeloxCommand IncrementCommand { get; }` — `ICommand` 包装

## 零第三方依赖

生成器内置于 `VeloxDev.Core` 包中，无需 ReactiveUI、CommunityToolkit 或 Fody。
