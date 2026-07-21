# Notification Property

`[VeloxProperty]` marks a field to generate a public property with full `INotifyPropertyChanged` support.

## Basic Usage

```csharp
using VeloxDev.MVVM;

public partial class ViewModel
{
	[VeloxProperty] private string _name = "VeloxDev";
	[VeloxProperty] private int _count;
}
```

The generator produces:
- Public properties `Name`, `Count`
- `OnPropertyChanging` / `OnPropertyChanged` calls
- `partial void OnNameChanged(string old, string new)` hook

## XAML Binding

```xml
<TextBlock Text="{Binding Name}" />
<TextBlock Text="{Binding Count}" />
```

Supports both field and `partial property` declaration styles.
