# 通知属性

`[VeloxProperty]` 标记字段，生成带完整 `INotifyPropertyChanged` 支持的公开属性。

## 基本用法

```csharp
using VeloxDev.MVVM;

public partial class ViewModel
{
    [VeloxProperty] private string _name = "VeloxDev";
    [VeloxProperty] private int _count;
}
```

生成器自动产生：
- 公开属性 `Name`、`Count`
- `OnPropertyChanging` / `OnPropertyChanged` 调用
- `partial void OnNameChanged(string old, string new)` 钩子

## XAML 绑定

```xml
<TextBlock Text="{Binding Name}" />
<TextBlock Text="{Binding Count}" />
```

支持字段和 `partial property` 两种声明方式。
