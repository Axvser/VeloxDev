# Command

`[VeloxCommand]` marks a method to generate an `ICommand` wrapper property (`MethodName` → `{Name}Command`).

## Basic Usage

```csharp
using VeloxDev.MVVM;

public partial class ViewModel
{
	[VeloxCommand]
	private void Increment() => Count++;

	// Async command
	[VeloxCommand]
	private async Task LoadDataAsync() =>
		await Task.Delay(100);

	// Parameterized command + CanExecute validation
	[VeloxCommand(name: "Auto", canValidate: true)]
	private async Task SaveAsync(object? parameter) =>
		await Task.Delay(50);

	// Compiler-generated partial method for CanExecute
	private partial bool CanExecuteSaveCommand(object? parameter) => IsDirty;
}
```

## Supported Method Signatures

| Signature | Description |
|-----------|-------------|
| `void Method()` | Synchronous, no parameter |
| `void Method(object?)` | Synchronous, parameterized |
| `Task Method()` | Async, no parameter |
| `Task Method(CancellationToken)` | Async, cancelable |
| `Task Method(object?, CancellationToken)` | Async, parameterized & cancelable |

When `canValidate: true` is set, the compiler emits a `partial bool CanExecute{Name}Command(object? parameter)` declaration that you must implement.
