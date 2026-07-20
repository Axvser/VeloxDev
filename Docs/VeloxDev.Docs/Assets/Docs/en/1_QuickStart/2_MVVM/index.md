# MVVM

Eliminate boilerplate: `[VeloxProperty]` generates `INotifyPropertyChanged`, `[VeloxCommand]` generates `ICommand`.

---

## Step 1 — Install

```shell
dotnet add package VeloxDev.Core
```

## Step 2 — Create the ViewModel (paste into `MainViewModel.cs`)

```csharp
using VeloxDev.MVVM;

public partial class MainViewModel
{
	[VeloxProperty] private string _name = "World";
	[VeloxProperty] private int _count;

	[VeloxCommand]
	private void Increment() => Count++;

	[VeloxCommand(canValidate: true)]
	private async Task SaveAsync(object? parameter)
	{
		await Task.Delay(100);
		Console.WriteLine($"Saved: {Name} with Count={Count}");
	}

	// Companion method for SaveAsync's CanExecute
	private bool CanSave() => !string.IsNullOrWhiteSpace(Name);
}
```

## Step 3 — Bind in XAML (WPF/Avalonia example)

```xml
<StackPanel>
	<TextBox Text="{Binding Name}" />
	<TextBlock Text="{Binding Count}" />
	<Button Command="{Binding IncrementCommand}" Content="+" />
	<Button Command="{Binding SaveCommand}" Content="Save" />
</StackPanel>
```

## What the Generator Produces

For `[VeloxProperty] private string _name` the generator emits:

- `public string Name { get; set; }` with full `INotifyPropertyChanged`
- `partial void OnNameChanged(string oldValue, string newValue)` hook

For `[VeloxCommand] private void Increment()` the generator emits:

- `public IVeloxCommand IncrementCommand { get; }` — an `ICommand` wrapper

## Without Any Third-Party Dependency

The generator ships inside `VeloxDev.Core` — no ReactiveUI, no CommunityToolkit, no Fody needed.
