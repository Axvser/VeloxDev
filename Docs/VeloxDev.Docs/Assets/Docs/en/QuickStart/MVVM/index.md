# MVVM

VeloxDev provides source generators that eliminate boilerplate for the MVVM pattern.

## Quick Example

```csharp
public partial class MainViewModel
{
	[VeloxProperty] private string _name = "World";
	[VeloxProperty] private int _count;

	[VeloxCommand]
	private void Increment() => Count++;
}
```

`[VeloxProperty]` generates a public property with full `INotifyPropertyChanged` support and a `partial void On{Name}Changed(T old, T new)` hook.

`[VeloxCommand]` generates a reactive `ICommand` (`IncrementCommand`).

## Binding in XAML

```xml
<TextBlock Text="{Binding Name}" />
<Button Command="{Binding IncrementCommand}" Content="+" />
<TextBlock Text="{Binding Count}" />
```

## Dependency-Free

`VeloxDev.Core` has **zero** third-party dependencies — the generators ship inside the package.
