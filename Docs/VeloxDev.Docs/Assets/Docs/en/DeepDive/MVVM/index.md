# MVVM Architecture

VeloxDev's MVVM layer is built on source generators, not reflection.

## VeloxProperty Generator

Marks a field to generate a full notification property. The generator emits:

- A public CLR property
- `INotifyPropertyChanging` / `INotifyPropertyChanged` implementations
- A `partial void On{Name}Changed(T old, T new)` hook

## VeloxCommand Generator

Marks a method to generate an `ICommand` wrapper. Supports:

- `CanExecute` via a `bool Can{Name}Execute()` companion method
- Async commands via `Task`-returning methods
- Parameterized commands via `object? parameter`
