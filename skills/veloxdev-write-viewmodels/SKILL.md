---
name: veloxdev-write-viewmodels
description: Write ViewModels with VeloxDev's source generators — observable properties with [VeloxProperty], typed and async commands with [VeloxCommand], result channels, collection change hooks, and interoperating with CommunityToolkit/Prism/ReactiveUI/Caliburn. Use when writing a ViewModel, observable properties or commands with the generators.
---

## Responsibility

Write a class that the compile-time generators turn into a full ViewModel — properties that notify, commands with cancellation, a result channel and a semaphore, collection hooks — without hand-writing `INotifyPropertyChanged` plumbing or a `RelayCommand`.

Package: **`VeloxDev.Core`** (runtime, namespace `VeloxDev.MVVM`) plus the analyzer-only package **`VeloxDev.Core.Generator`**. A NuGet reference to `VeloxDev.Core` brings the generator in as a dependency, so installing Core is enough. When you consume Core by `ProjectReference` instead, add the generator explicitly as an analyzer (`OutputItemType="Analyzer"`) — analyzers do not flow across project references. No GUI adapter is needed.

⚙ **The class is `partial`.** Every generator matches on the `partial` modifier. A non-partial class produces no file, no warning and no error. A `partial record` or `partial struct` is not a target at all: the MVVM/command pipeline requires a `ClassDeclarationSyntax`, and the theme generator has its own `ClassDeclarationSyntax`-only predicate.

⚙ **A non-partial `[VeloxProperty]` property cannot be completed.** The generator only adds code; it cannot complete accessors you already wrote. Mark the property `partial` or drop the attribute; this reports `VELOX_MVVM_PROP003`.

⚙ **The generator sees a class only while its file compiles as syntax.** A file excluded from the compilation, or a class behind a false preprocessor branch, is skipped.

⚙ **Language level.** Generated code needs C# 12 (collection expressions); a lower `LangVersion` reports `VELOX_LANGVERSION001`. The `partial`-property form needs C# 13 on top of that.

## The two property forms

```csharp
[VeloxProperty] private string _name = string.Empty;          // field form: generates the property
```

```csharp
[VeloxProperty] public partial SlotViewModel InputSlot { get; set; }   // property form: generates the backing field
```

The workflow idiom is **fields for plain state, `partial` properties for anything with a lifecycle** — see [the workflow model reference](../veloxdev-create-workflow/references/practices/model.md).

## The command forms

```csharp
[VeloxCommand] private void Save() { … }                                    // → SaveCommand (IVeloxCommand)
[VeloxCommand("Reset")] private Task ResetAsync(CancellationToken ct) { … } // → ResetCommand
[VeloxCommand] private Task<int> AddAsync(int left, int right) { … }        // → IVeloxCommand<int, int, int>
```

## Where to read next

| You are about to… | Read |
|---|---|
| Understand the generated property, naming, setter order and collection hooks | [references/practices/properties.md](references/practices/properties.md) |
| Write a typed, async, cancellable or validating command | [references/practices/commands.md](references/practices/commands.md) |
| Read the result channel, lifecycle, diagnostics or framework interop | [references/practices/interop-and-diagnostics.md](references/practices/interop-and-diagnostics.md) |

## Pitfalls

| Symptom | Read |
|---|---|
| `[VeloxProperty]` / `[VeloxCommand]` does nothing | [references/pitfalls/mvvm.md](references/pitfalls/mvvm.md) |
| A property never notifies, or a callback fires at the wrong time | [references/pitfalls/mvvm.md](references/pitfalls/mvvm.md) |
| A command cannot be bound, or a validator never runs | [references/pitfalls/mvvm.md](references/pitfalls/mvvm.md) |
| A collection hook is missing or a get-only collection fails to compile | [references/pitfalls/mvvm.md](references/pitfalls/mvvm.md) |
| The generated file collides with another class | [references/pitfalls/mvvm.md](references/pitfalls/mvvm.md) |

## Reference

⚙ `Examples/MVVM/Common/Lib/CommandGalleryViewModel.cs` — one method per accepted `[VeloxCommand]` signature: typed, single-parameter (including a tuple), generic-method and `ValueTask` forms. `Examples/MVVM/WPF/Demo/MainWindowViewModel.cs` and `Examples/MVVM/Avalonia/Demo/ViewModels/MainWindowViewModel.cs` add properties, collection hooks, validators and the lock / interrupt / clear lifecycle.

⚙ `Src/Generators/VeloxDev.Core.Generator/Writers/` — `MVVMWriter.cs`, `CommandWriter.cs`.

⚙ `Src/Generators/VeloxDev.Core.Generator/Base/` — `Analizer.cs` (the property/setter factory) and `AIContextNaming.cs`; `Diagnostics.cs` sits in the generator root.

⚙ `Src/Core/VeloxDev.Core/MVVM/` and `Src/Core/VeloxDev.Core/Interfaces/MVVM/` — the runtime types, including `CommandArities.cs` and `ObservableCollectionTracker.cs`.

⚙ `Src/Core/VeloxDev.Core.Test/MVVM/` — the behavioural contract, including `CommandSignatureTests`, `CommandArityTests`, `TypedCommandEventTests`, `VeloxCommandLifecycleTests`, `VeloxCommandCompletionTests`, `CommandSignatureDiagnosticsTests` and `CommandAllocationTests`.
