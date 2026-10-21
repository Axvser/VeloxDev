---
name: veloxdev-write-viewmodels
description: Write ViewModels with VeloxDev's source generators — observable properties with [VeloxProperty], typed and async commands with [VeloxCommand], result channels, collection change hooks, and interoperating with CommunityToolkit/Prism/ReactiveUI/Caliburn. Use when writing a ViewModel, observable properties or commands with the generators.
---

## Responsibility

Write a class that the compile-time generators turn into a full ViewModel — properties that notify, commands with cancellation, a result channel and a semaphore, collection hooks — without hand-writing `INotifyPropertyChanged` plumbing or a `RelayCommand`.

Package: **`VeloxDev.Core`** (runtime, namespace `VeloxDev.MVVM`) plus the analyzer-only package **`VeloxDev.Core.Generator`**. A NuGet reference to `VeloxDev.Core` brings the generator in as a dependency (`Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:40-43`), so installing Core is enough. When you consume Core by `ProjectReference` instead, add the generator explicitly as an analyzer (`OutputItemType="Analyzer"`) — analyzers do not flow across project references. No GUI adapter is needed and the behaviour is identical on every platform. The generator targets `netstandard2.0` and references `Microsoft.CodeAnalysis.CSharp` **4.8.0**. Its analyzer needs Roslyn 4.0+ / .NET SDK 6+ (`VeloxDev.Core.Generator.targets`), but the emitted code is C# 12 (`VELOX_LANGVERSION001`), so use a .NET 8 SDK or newer (or VS 2022 17.8+). `VeloxDev.Core` targets `netstandard2.0;netframework4.6.1;net5.0;netcoreapp3.0;net8.0`.

⚙ **Read this first: the class must be `partial`.** Every generator this skill uses matches on the `partial` modifier, and a non-partial **class** still produces no file, no warning and no error — the member you expected simply is not there. If `[VeloxProperty]` or `[VeloxCommand]` "does nothing", this is why. A `partial record` or `partial struct` is not a target at all: the MVVM/command pipeline requires a `ClassDeclarationSyntax` (`Base/Analizer.cs:191-195`) and matches no other declaration kind. (The theme generator has its own `ClassDeclarationSyntax`-only predicate, `Theme.cs:33-56`.)

⚙ **A non-partial `[VeloxProperty]` property cannot be completed either, and it now warns.** The generator only ever *adds* code, so it cannot complete accessors you already wrote — mark the property `partial` or drop the attribute. This reports `VELOX_MVVM_PROP003`; the rest of the diagnostics are listed under [Diagnostics](#diagnostics).

⚙ **The generator only sees the class if the file it is in compiles as syntax** — the `partial` check runs before anything else, so a class in an excluded file or behind a bad preprocessor branch is skipped too.

⚙ **Language level.** Generated code needs C# 12 (collection expressions); a lower `LangVersion` reports `VELOX_LANGVERSION001`. The `partial`-property form needs C# 13 on top of that, which the guard cannot detect for you.

## `[VeloxProperty]`

Two forms, both valid, and which one you use is a convention rather than a rule:

```csharp
[VeloxProperty] private string _name = string.Empty;          // field form: generates the property
```

```csharp
[VeloxProperty] public partial SlotViewModel InputSlot { get; set; }   // property form: generates the backing field
```

The example above is the workflow idiom: **fields for plain state, `partial` properties for anything with a lifecycle** (see [the workflow skill](../veloxdev-create-workflow/references/model.md)).

Generated from `[VeloxProperty] private int _index = 0;`:

```csharp
public int Index
{
    get => this._index;
    set
    {
        if (global::System.Object.Equals(this._index, value)) return;
        var old = this._index;
        OnPropertyChanging(nameof(Index));
        OnIndexChanging(old, value);
        this._index = value;
        OnIndexChanged(old, value);
        OnPropertyChanged(nameof(Index));
    }
}
partial void OnIndexChanging(int oldValue, int newValue);
partial void OnIndexChanged(int oldValue, int newValue);
```

Generated output renders types fully qualified — `System.Int32`, `System.String`, and so on; the samples here shorten them for readability.

When nothing in the hierarchy already supplies the notification plumbing, the class also receives `INotifyPropertyChanging` / `INotifyPropertyChanged`, the `PropertyChanging` / `PropertyChanged` events, and `public virtual void OnPropertyChanging(string)` / `OnPropertyChanged(string)` (non-virtual when the class is sealed). A base that already supplies them is reused rather than duplicated.

⚙ **The callbacks take `(oldValue, newValue)` in one method.** This is *not* CommunityToolkit's `OnNameChanging(value)`; if you have used that generator, this is the signature to unlearn.

⚙ **Naming**: one leading `_` is dropped and the next character upper-cased; otherwise just the first character. `_index` → `Index`, `title` → `Title`, `intervalMilliseconds` → `IntervalMilliseconds`, `m_value` → `M_value`. There is no camel-case splitting and the rest is preserved verbatim — `[VeloxProperty] private string user_name;` gives `User_name`, not `UserName`.

⚙ The field form always emits a `public` property; the property form preserves the accessibility and accessor visibility you declared. The property form synthesises a field by lower-casing only the first character: `InputSlot` → `_inputSlot`, `URL` → `_uRL`. For a workflow slot property, generated code depends on that exact name (`InitializeWorkflowCore` references it — see [the workflow skill](../veloxdev-create-workflow/references/model.md)), so a hand-written field of a different name breaks it silently. Elsewhere `MVVMWriter.ResolveBackingStorage` (`Writers/MVVMWriter.cs:206-257`) simply emits its own field when the conventional `_camelCase` field is absent. An accessible field of that conventional name is reused; otherwise one is emitted as `private T _name = <default>;` (`default(T)`, or `null` for a nullable type). `GetDefaultValue` compares keyword spellings against the rendered `System.*` names, so even `bool` / `int` / `string` come out as `default(System.Boolean)` / `default(System.Int32)` / `default(System.String)`, not `false` / `0` / `string.Empty`.

⚙ **Equality guard (the default setter).** The setter short-circuits on `Object.Equals(field, value)` — except for `float` / `double` (including their nullable forms), which compare bit patterns so `-0.0` and the various `NaN` values are not collapsed.

⚙ **Setter order.** Capture old → slot uninstall → `OnPropertyChanging(name)` → `OnXChanging(old, value)` → collection unsubscribe (and `OnItemRemovedFromX`) → assign → slot install → collection subscribe (and `OnItemAddedToX`) → `OnXChanged(old, value)` → `OnPropertyChanged(name)`. If a lifecycle step looks like it fires at the wrong time, that order is the answer. That is `SetterMode.Default`; on the `FrameworkSetProperty` path (CommunityToolkit/Prism) there is no `Object.Equals` guard — `SetProperty(ref field, value, nameof(X))` does the comparison and assignment — and the slot-install, collection-subscribe and `OnXChanged` steps run inside its `if` (`Base/Analizer.cs:567-585`). See [Interoperating](#interoperating-with-another-mvvm-framework).

## Collections

A property whose type implements `INotifyCollectionChanged` gets extra members:

```csharp
partial void OnItemAddedToItems(IEnumerable<Item> items);
partial void OnItemRemovedFromItems(IEnumerable<Item> items);
partial void OnItemMovedInItems(IEnumerable<Item> items);
partial void OnItemsResetInItems();
```

and, unless a base already declares a matching method, a `protected virtual void OnCollectionChanged<T>(string propertyName, NotifyCollectionChangedEventArgs e, IEnumerable<T>? oldItems, IEnumerable<T>? newItems)`.

⚙ **The four `OnItem…` hooks are the ergonomic path**; reach for `OnCollectionChanged<T>` only when you need the raw event args. `Replace` calls the removed hook then the added hook; `Reset` calls `OnItemsResetInItems`.

⚙ **Base-class detection is loose.** The generator treats a base as already supplying `OnCollectionChanged<T>` when it finds a non-static generic method with one type parameter, four parameters, and a `string` first parameter. It does not compare the other three parameter types, so a same-named four-parameter method with different types also suppresses generation.

⚙ The getter installs a subscription through `ObservableCollectionTracker.EnsureSubscribed`, so a collection initialised with `= []` is still observed even though the field initializer bypasses the setter. Replacing the collection unsubscribes the old instance and subscribes the new one automatically. Deduplication is by `(Method, Target)`, and entries live in a `ConditionalWeakTable`.

⚙ **A collection hook needs a settable property.** The hooks and the private `On{Name}CollectionChanged` handler are emitted only when the property has a setter (`Base/Analizer.cs:830-834`), but the getter still calls `EnsureSubscribed(..., On{Name}CollectionChanged)` (`:701-719`). A get-only collection property therefore fails to compile with `CS0103`.

## `[VeloxCommand]`

```csharp
[VeloxCommand] private void Save() { … }                                    // → SaveCommand (IVeloxCommand)
[VeloxCommand("Reset")] private Task ResetAsync(CancellationToken ct) { … } // → ResetCommand
```

Names: `name: "Auto"` (the default) uses `methodName.Replace("Async", "")` and appends `Command`; any other value is used verbatim and still gets `Command` appended. The replacement removes **every** `Async`, not just the suffix. Two methods that collapse to the same stem emit duplicate members — a `CS0102` in the generated file, not a Velox diagnostic.

The generated property (or accessor) is lazily cached on first read.

### Accepted signatures

The return type must be `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>` or `void`. Leading parameters are optional, at most **15**, and may be followed by one trailing `CancellationToken`.

| Body shape | Generated member |
|---|---|
| no leading parameter; `Task`, `ValueTask`, `void` | untyped `IVeloxCommand` |
| no leading parameter; `Task<T>` / `ValueTask<T>` | untyped `IVeloxCommand`, with the value in the boxed result channel |
| one `object` / `object?` parameter (any return) | untyped `IVeloxCommand`; the argument and any value stay boxed |
| one parameter of a concrete type `T`; no value | `IVeloxCommand<T, object?>` |
| one parameter of a concrete type `T`; `Task<TR>` / `ValueTask<TR>` | `IVeloxCommand<T, TR>` |
| 2–15 leading parameters | `IVeloxCommand<T1, …, Tn, TR>` from the arity family in `CommandArities.cs` |
| generic method `M<T>(T value)` | cached accessor `IVeloxCommand<T, TR> GetMCommand<T>()` |

`TR` is the body's `T` for `Task<T>` / `ValueTask<T>`, and `object?` (always `null`) otherwise. The two-parameter-plus arity classes pack the arguments into a `ValueTuple` internally; the public surface is tuple-free and a `ValueTuple` allocates nothing.

```csharp
[VeloxCommand] private Task<int> AddAsync(int left, int right) { … }   // → IVeloxCommand<int, int, int>
int sum = await vm.AddCommand.ExecuteAsync(2, 3, CancellationToken.None);
```

**Rejected shapes** report `VELOX_MVVM_CMD001` on the method's own line and are skipped; the rest of the class still generates:

- `void M(CancellationToken ct)` — a synchronous body cannot observe the token.
- A generic method whose type parameters do not appear in its parameter types — `Task M<T>(object? p)` cannot carry `T` on a command instance.
- More than 15 leading parameters — the generated `Func<T1..Tn, CancellationToken, Task<TResult>>` would exceed `Func`'s limit of 17 type arguments.
- A return type outside the accepted set.
- A member named `{Name}Command` already exists on the type.

⚙ **A generic method produces a method, not a property.** `Task M<T>(T value)` generates `IVeloxCommand<T, TR> GetMCommand<T>()`, so it cannot be bound. The cache is keyed by **all** method type parameters; each closed `T` gets its own command, queue, lock and concurrency cap.

⚙ **`name`, `canValidate` and `semaphore`.** `canValidate: true` makes the generator declare `private partial bool CanExecute{Name}Command(...)` — you must implement it, and the build fails loudly until you do. The validator's parameter list copies **all** leading parameters' types and names from the source method; a method with no leading parameter gets `object? parameter`. A generic method's validator is a generic partial method with the same type parameters and constraints. Any name mismatch is `CS8826`. Call `{Name}Command.Notify()` (or `Get{Name}Command<T>().Notify()` for a generic accessor) when state that affects the predicate changes.

⚙ **A validator must accept `null` when `TParam` is a reference type.** `CommandPipeline<TParam, TResult>.CanExecute(object?)` passes a null argument straight to `CanExecuteCore(default!)` (`CommandPipeline{TParam,TResult}.cs:403-406`), and WPF does exactly that when a button has no `CommandParameter`. Null-check the parameter, as the gallery's own validator does (`Examples/MVVM/Common/Lib/CommandGalleryViewModel.cs:235-237`). A value-typed parameter never reaches the predicate: the pipeline rejects the null before calling it.

⚙ The attribute's XML documentation and the generator agree: the member is `CanExecute{Name}Command`.

⚙ **`semaphore`** is the command's concurrency capacity (default 1, serialised). Generated commands clamp it with `Math.Max(1, semaphore)`; the `VeloxCommand` constructor and `ChangeSemaphore` throw `ArgumentOutOfRangeException` for a value below 1.

⚙ **Parameter type visibility.** A generated untyped property is always `public`; when the property is typed, its parameter type is named in that public signature, so an `internal` parameter type fails with `CS0053`. If an interface or base forces the property back to plain `IVeloxCommand`, that particular failure disappears.

⚙ **A declared command property fixes the property's static type.** If an interface or base declares `IVeloxCommand MoveCommand`, the generated property is emitted as `IVeloxCommand` (the buffer and pipeline stay typed). A declaration of the exact matching `IVeloxCommand<TParam, TResult>` is kept. A declaration that cannot accept the generated command is rejected with `VELOX_MVVM_CMD001`.

## Typed commands and the result channel

`IVeloxCommand<TParam, TResult>` derives from `IVeloxCommand`, so the `object?` overloads stay reachable and **strong typing is not a compile-time guarantee**: `vm.MoveCommand.Execute(42)` still compiles and ends as a `Failed` execution carrying `InvalidCastException`. What the typed interface buys is a readable signature, a strongly typed validator, and an unboxed path through the pipeline.

```csharp
Task<TResult> ExecuteAsync(TParam parameter, CancellationToken cancellationToken);  // waits for this execution
void Execute(TParam parameter, out TResult result);                                 // blocking twin
```

The one-argument `ExecuteAsync(TParam)` still returns as soon as the call is accepted. The two-argument overload exists so it *overloads* that member rather than hiding it.

**A body returning `Task<T>` or `ValueTask<T>` fills the result channel.** When the command is typed, the value comes back unboxed. A command with **no** leading parameter stays untyped (`IVeloxCommand`) even when its body returns a value, so there the value is available only through the boxed interfaces below.

```csharp
[VeloxCommand] private Task<int> MeasureAsync(NotePayload note, CancellationToken ct) { … } // → IVeloxCommand<NotePayload, int>

// Typed: unboxed, throws when the execution did not complete.
int weight = await vm.MeasureCommand.ExecuteAsync(note, CancellationToken.None);

// Untyped: boxed through IVeloxCommandResult.
object? value = await ((IVeloxCommandResult)vm.MeasureCommand).ExecuteAsync(note, CancellationToken.None);

// Outcome without throwing.
CommandCompletion completion = await vm.MeasureCommand.ExecuteAndWaitAsync(note);
if (completion.Succeeded)
    weight = completion.GetResultOrThrow<int>();   // GetResultOrThrow() returns boxed object?
```

- `IVeloxCommandResult` declares `Task<object?> ExecuteAsync(object?, CancellationToken)` and `void Execute(object?, out object?)`. The token overload has no default, so a one-argument call cannot become ambiguous.
- `ExecuteAndWaitAsync` lives on `IVeloxCommandCompletion`; reach it from the interface type of a generated property through the `VeloxCommandExtensions` extension. It never throws for a body failure or a cancellation — it reports `CommandOutcome.Completed` / `Failed` / `Canceled` / `Refused`. Only the caller's own `CancellationToken` aborts the wait, and that does throw.
- `CommandCompletion` is a `readonly struct` with `Outcome`, `Exception`, `Result` (boxed), `Succeeded`, `GetResultOrThrow()` and `GetResultOrThrow<TR>()`. The awaitable typed `ExecuteAsync` rethrows the body's own exception, throws `OperationCanceledException` when cancelled, and `InvalidOperationException` when refused by `Lock()`.
- `Refused` has no `CommandEventType` counterpart: a refused call raises `Canceled` and never reaches `Exited`.
- A command with no return value still has the result channel and always yields `null`, so `null` cannot be told apart from "this command has no value".

⚙ **Typed events without boxing.** `IVeloxCommandEvents<TParam, TResult>` exposes the same eight stages with `CommandEventArgs<TParam, TResult>`, whose `TypedParameter` and `TypedResult` are in their own types. The class-level `Created` / `Failed` / … stay the `object?`-shaped ones that binding needs; subscribe through a reference typed as `IVeloxCommandEvents<,>` to get the typed payload.

## Runtime types

| Type | Role |
|---|---|
| `IVeloxCommand : ICommand` | Eight lifecycle events and `ExecuteAsync`, `Notify`, `Lock` / `Unlock`, `Interrupt` / `Continue`, `Clear`, `ChangeSemaphore` (each with an `…Async` twin where the synchronous form would block). |
| `IVeloxCommand<TParam, TResult>` | Typed facade: `CanExecute(TParam)`, `Execute(TParam)`, `ExecuteAsync(TParam)`, `ExecuteAsync(TParam, ct)`, `Execute(TParam, out TResult)`. |
| `IVeloxCommandEvents<TParam, TResult>` | Typed version of the eight events, carrying `CommandEventArgs<TParam, TResult>`. |
| `IVeloxCommandResult` | Boxed value channel: `ExecuteAsync(object?, CancellationToken)` and `Execute(object?, out object?)`. |
| `IVeloxCommandCompletion` | `ExecuteAndWaitAsync(object?, CancellationToken)` returning `Task<CommandCompletion>`. |
| `IVeloxCommandStatus` | `IsBusy`, `ActiveCount`, `PendingCount`; reached from `IVeloxCommand` through `VeloxCommandExtensions`. |
| `CommandPipeline<TParam, TResult>` | Public abstract engine implementing `IVeloxCommand`, `IVeloxCommandCompletion`, `IVeloxCommandStatus`, `IVeloxCommandResult`, `IVeloxCommandEvents<TParam, TResult>` and `IDisposable`. Adds `EventContext`, the static `HandlerException` hook, and protected typed helpers (`CanExecuteCore`, `ExecuteAsyncCore`, `ExecuteAndWaitTypedAsync`, `ExecuteAndWaitTyped`). |
| `VeloxCommand` | **Not sealed**; derives from `CommandPipeline<object?, object?>`. This is the untyped/fallback shape that binding reaches. |
| `VeloxCommand<TParam, TResult>` | Sealed one-parameter typed shape. The arity family (`CommandArities.cs`) declares sealed `VeloxCommand<T1, …, Tn, TResult>` for 2–15 parameters; each derives from `CommandPipeline<(T1, …, Tn), TResult>`. |
| `CommandEventArgs` | **Not sealed**; public event payload with `Parameter`, `EventType`, `Exception`, and `With(...)`. The per-execution token source is internal. |
| `CommandEventArgs<TParam, TResult>` | Sealed subclass adding `TypedParameter` and `TypedResult`; one instance can be delivered to both the typed and the `object?` face. |
| `CommandCompletion` | `readonly struct` returned by `ExecuteAndWaitAsync`. |
| `CommandEventType` | `None`, `Created`, `Enqueued`, `Dequeued`, `Started`, `Completed`, `Failed`, `Canceled`, `Exited`. |
| `CommandOutcome` | `Completed`, `Failed`, `Canceled`, `Refused`. |

**Lifecycle.** Immediate: `Created → Started → Completed → Exited`. Queued: `Created → Enqueued`, then `Dequeued → Started → …` when a slot frees. Refused by `Lock`: `Created → Canceled` only. Cancelled while running: `Created → Started → Canceled → Exited`. An execution reports at most one `Canceled`; a throwing subscriber never disturbs the pipeline and is reported through the static `HandlerException` event. Events are raised inline by default; set `EventContext` to a UI `SynchronizationContext` to post them there.

**Cancellation.** Only a `CancellationToken` parameter lets the command actually stop the body. Every other shape builds a command whose body never receives a token: an interrupted execution reports `Canceled`, but the body runs to completion.

## Interoperating with another MVVM framework

⚙ **The generator detects the host framework and adapts the setter.** CommunityToolkit.Mvvm is detected by its `[ObservableObject]` attribute (a class that only *derives* from `ObservableObject` also matches, via the generic `SetProperty(ref T, T, string)` probe); Prism by a base with such a `SetProperty`; ReactiveUI by implementing `IReactiveObject`; Caliburn.Micro by a base with `NotifyOfPropertyChange(string)`. The setter delegates to that framework's raise method.

⚙ **Caliburn is the exception to the assignment rule**: it still writes the field itself, then calls `NotifyOfPropertyChange`.

⚙ **A member already carrying `[ObservableProperty]` or `[Reactive]` from CommunityToolkit, ReactiveUI, Prism or Caliburn is skipped by design** — the two generators would fight over the same property.

⚙ **Whether the generated setter also calls `OnPropertyChanged` depends on the framework.** On the `SetProperty` path (CommunityToolkit, Prism) it does **not** — that framework's own method raises the event, and calling it again would double-fire. On the ReactiveUI and Caliburn paths it **does**. Your `OnXChanged` partial runs either way, so the VeloxDev callback contract holds regardless.

## Diagnostics

| ID | Severity | Condition |
|---|---|---|
| `VELOX_MVVM_CMD001` | Error | A `[VeloxCommand]` method cannot be turned into a command; that method is skipped. |
| `VELOX_MVVM_PROP001` | Error | Conflicting property declaration: two members map to one property name; a same-named non-field member already exists on the type (`MVVMWriter.cs:214-221`); or an existing field cannot back the property (const/static/read-only, or a different type). |
| `VELOX_MVVM_PROP002` | Warning | A `[VeloxProperty]` field name yields no usable identifier (for example `_` or `_1x`); the property is skipped. |
| `VELOX_MVVM_PROP003` | Warning | A `[VeloxProperty]` property is not `partial`, so its accessors cannot be completed; also reported for an indexer (`MVVMWriter.cs:176-178`). |
| `VELOX_LANGVERSION001` | Warning | The project's `LangVersion` is below C# 12, which the generated code needs. |

⚙ The command diagnostic was renamed from `VELOXCMD001` to `VELOX_MVVM_CMD001`. A `#pragma warning disable VELOXCMD001` no longer suppresses it.

## Silent failures

⚙ **The class is not `partial`** — or it is a `partial record` / `partial struct`, which is not a generator target — so nothing is generated for any attribute.

⚙ **The derived property name already exists** on the class or a base — the field form is skipped silently. The property form reports `VELOX_MVVM_PROP001` for the conflicts it can see.

⚙ **A competing MVVM attribute** is present — skipped by design.

⚙ **Two `[VeloxCommand]` methods collapse to the same command name** — the generated file declares the member twice and fails with `CS0102`.

⚙ **Two workflow classes with the same name in different namespaces collide**, because that generator names its file `{ClassName}.g.cs` without a namespace. The MVVM, command and tick generators include the namespace segment and are unaffected.

## Reference

⚙ `Examples/MVVM/Common/Lib/CommandGalleryViewModel.cs` — one method per accepted `[VeloxCommand]` signature: typed, single-parameter (including a tuple), generic-method and `ValueTask` forms. `Examples/MVVM/WPF/Demo/MainWindowViewModel.cs` and `Examples/MVVM/Avalonia/Demo/ViewModels/MainWindowViewModel.cs` add properties, collection hooks, validators and the lock / interrupt / clear lifecycle.

⚙ `Src/Generators/VeloxDev.Core.Generator/Writers/` — `MVVMWriter.cs`, `CommandWriter.cs`.

⚙ `Src/Generators/VeloxDev.Core.Generator/Base/` — `Analizer.cs` (the property/setter factory) and `AIContextNaming.cs`; `Diagnostics.cs` sits in the generator root. The generated shape is exactly what these emit; reading the setter body is faster than guessing.

⚙ `Src/Core/VeloxDev.Core/MVVM/` and `Src/Core/VeloxDev.Core/Interfaces/MVVM/` — the runtime types listed above, including `CommandArities.cs` and `ObservableCollectionTracker.cs`.

⚙ `Src/Core/VeloxDev.Core.Test/MVVM/` — the behavioural contract, including `CommandSignatureTests`, `CommandArityTests`, `TypedCommandEventTests`, `VeloxCommandLifecycleTests`, `VeloxCommandCompletionTests`, `CommandSignatureDiagnosticsTests` and `CommandAllocationTests`.

⚙ The workflow node idiom on top of this layer — which members are generated, which you write, and the slot lifecycle behind a `partial` slot property — is in [the workflow skill](../veloxdev-create-workflow/references/model.md).
