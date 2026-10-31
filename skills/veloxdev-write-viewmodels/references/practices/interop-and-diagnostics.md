# Interop, diagnostics and the command lifecycle

## Interoperating with another MVVM framework

⚙ **The generator detects the host framework and adapts the setter.** CommunityToolkit.Mvvm is detected by its `[ObservableObject]` attribute (a class that derives from `ObservableObject` also matches, via the generic `SetProperty(ref T, T, string)` probe); Prism by a base with such a `SetProperty`; ReactiveUI by implementing `IReactiveObject`; Caliburn.Micro by a base with `NotifyOfPropertyChange(string)`. The setter delegates to that framework's raise method.

⚙ **Caliburn is the exception to the assignment rule:** it still writes the field itself, then calls `NotifyOfPropertyChange`.

⚙ **A member already carrying `[ObservableProperty]` or `[Reactive]` from CommunityToolkit, ReactiveUI, Prism or Caliburn is skipped by design** — the two generators would fight over the same property.

⚙ **Whether the generated setter also calls `OnPropertyChanged` depends on the framework.** On the `SetProperty` path (CommunityToolkit, Prism) it does **not** — that framework's own method raises the event, and calling it again would double-fire. On the ReactiveUI and Caliburn paths it **does**. Your `OnXChanged` partial runs either way, so the VeloxDev callback contract holds regardless.

## Diagnostics

| ID | Severity | Condition |
|---|---|---|
| `VELOX_MVVM_CMD001` | Error | A `[VeloxCommand]` method cannot be turned into a command; that method is skipped. |
| `VELOX_MVVM_PROP001` | Error | Conflicting property declaration: two members map to one property name; a same-named non-field member already exists on the type; or an existing field cannot back the property (const/static/read-only, or a different type). |
| `VELOX_MVVM_PROP002` | Warning | A `[VeloxProperty]` field name yields no usable identifier (for example `_` or `_1x`); the property is skipped. |
| `VELOX_MVVM_PROP003` | Warning | A `[VeloxProperty]` property is not `partial`, so its accessors cannot be completed; also reported for an indexer. |
| `VELOX_LANGVERSION001` | Warning | The project's `LangVersion` is below C# 12, which the generated code needs. |

⚙ The command diagnostic is `VELOX_MVVM_CMD001`; a `#pragma warning disable VELOXCMD001` does not suppress it.

## Generator output and shared state

⚙ **The MVVM, command and tick generators include the namespace segment in the generated file name**, so two classes with the same name in different namespaces do not collide. The workflow generator names its file `{ClassName}.g.cs` without a namespace; two workflow classes sharing a name collide.

⚙ **The generator adds code to the partial class; it never modifies your source.** Read the generated file when a member looks wrong: `obj/<Configuration>/<tfm>/generated/VeloxDev.Core.Generator/`.

⚙ **A class in an excluded file or behind a false preprocessor branch is skipped**, because the generator sees the syntax tree only while it compiles.

When a property, command or collection hook does not appear or does not behave, read [references/pitfalls/mvvm.md](../pitfalls/mvvm.md).
