# 命令

`[VeloxCommand]` 标记方法，生成 `ICommand` 包装属性（`方法名 + Command`）。

## 基本用法

```csharp
using VeloxDev.MVVM;

public partial class ViewModel
{
    [VeloxCommand]
    private void Increment() => Count++;

    // 异步命令
    [VeloxCommand]
    private async Task LoadDataAsync() =>
        await Task.Delay(100);

    // 参数化命令 + CanExecute 验证
    [VeloxCommand(name: "Auto", canValidate: true)]
    private async Task SaveAsync(object? parameter) =>
        await Task.Delay(50);

    private partial bool CanExecuteSaveCommand(object? parameter) => IsDirty;
}
```

## 支持的方法签名

| 签名 | 说明 |
|------|------|
| `void Method()` | 同步无参 |
| `void Method(object?)` | 同步参数化 |
| `Task Method()` | 异步无参 |
| `Task Method(CancellationToken)` | 异步可取消 |
| `Task Method(object?, CancellationToken)` | 异步参数化可取消 |

`canValidate: true` 时编译器生成 `partial bool CanExecute{Name}Command(object? parameter)` 声明，需用户实现。
