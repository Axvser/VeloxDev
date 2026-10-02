---
name: veloxdev-write-viewmodels
description: Write ViewModels with VeloxDev's source generators — observable properties with [VeloxProperty], async commands with [VeloxCommand], collection change hooks, and interoperating with CommunityToolkit/Prism/ReactiveUI/Caliburn
---
## Responsibility

Write a class that the compile-time generators turn into a full ViewModel — properties that notify, commands with cancellation and a semaphore, collection hooks — without hand-writing `INotifyPropertyChanged` plumbing or a `RelayCommand`.

Package: **`VeloxDev.Core`**. No GUI adapter is needed and the behaviour is identical on every platform.

⚙ **Read this first: the class must be `partial`.** Every generator in the repository matches on the `partial` modifier, and a non-partial **class** still produces no file, no warning and no error — the member you expected simply is not there. If `[VeloxProperty]` "does nothing", this is why.

⚙ **A non-partial `[VeloxProperty]` property is the same trap, and it now warns.** The generator only ever *adds* code, so it cannot complete accessors you already wrote — mark the property `partial` or drop the attribute. This one reports `VELOX_MVVM_PROP003` rather than passing silently; the rest of the `VELOX_MVVM_PROP*` family is documented in the `VeloxDev.Core.Generator` module memory.

⚙ **The generator only sees the class if the file it is in compiles as syntax** — the same `partial` check runs before anything else, so a class in an excluded file or behind a bad preprocessor branch is skipped too.

## `[VeloxProperty]`

Two forms, both valid, and which one you use is a convention rather than a rule:

```csharp
[VeloxProperty] private string _name = string.Empty;          // field form
```

```csharp
[VeloxProperty] public partial SlotViewModel InputSlot { get; set; }   // property form
```

The field form generates the property; the property form generates the backing field. The example above is the workflow idiom: **fields for plain state, `partial` properties for anything with a lifecycle** (see [the workflow skill](../veloxdev-create-workflow/references/model.md)).

Generated from `[VeloxProperty] private int _index = 0;`:

```csharp
public int Index { get => this._index; set { … } }
partial void OnIndexChanging(int oldValue, int newValue);
partial void OnIndexChanged(int oldValue, int newValue);
public event PropertyChangingEventHandler? PropertyChanging;
public event PropertyChangedEventHandler? PropertyChanged;
public virtual void OnPropertyChanging(string propertyName);
public virtual void OnPropertyChanged(string propertyName);
```

⚙ **The callbacks take `(oldValue, newValue)` in one method.** This is *not* CommunityToolkit's `OnNameChanging(value)`; if you have used that generator, this is the signature to unlearn.

⚙ **Naming**: one leading `_` is stripped and the next character upper-cased; otherwise just the first character. `_index` → `Index`, `title` → `Title`, `intervalMilliseconds` → `IntervalMilliseconds`, `m_value` → `M_value`. There is no camel-case splitting, and the rest is preserved verbatim — so `[VeloxProperty] private string user_name;` gives you a `User_name` property, not `UserName`: first character upper-cased, the underscore and everything after it left untouched.

⚙ The property form synthesises `_camelCase` (`InputSlot` → `_inputSlot`). Generated init code elsewhere depends on that exact name, so a hand-written field of a different name breaks it silently.

⚙ The setter short-circuits on `Object.Equals(field, value)`, then runs, in order: capture old → slot uninstall → `OnPropertyChanging(name)` → `OnXChanging(old, value)` → collection unsubscribe → assign → slot install → collection subscribe → `OnXChanged(old, value)` → `OnPropertyChanged(name)`. If a lifecycle step looks like it fires at the wrong time, that order is the answer.

## Collections

A property whose type implements `INotifyCollectionChanged` gets extra members:

```csharp
partial void OnItemAddedToItems(IEnumerable<Item> items);
partial void OnItemRemovedFromItems(IEnumerable<Item> items);
partial void OnItemMovedInItems(IEnumerable<Item> items);
partial void OnItemsResetInItems();
```

and a `protected virtual void OnCollectionChanged<T>(string propertyName, NotifyCollectionChangedEventArgs e, IEnumerable<T>? oldItems, IEnumerable<T>? newItems)`.

⚙ **The four `OnItem…` hooks are the ergonomic path**; reach for the generic `OnCollectionChanged<T>` only when you need the raw event args.

⚙ **`OnCollectionChanged<T>` is generated only if no base class already declares that exact four-parameter signature** — that signature *is* how the generator detects an existing implementation. Changing it in a base class makes the generator emit a duplicate.

⚙ The getter installs a subscription through `ObservableCollectionTracker`, so a collection that is replaced is unsubscribed automatically; you never manage the subscription yourself.

## `[VeloxCommand]`

```csharp
[VeloxCommand] private void Save() { … }                       // → SaveCommand
[VeloxCommand("Reset")] private Task ResetAsync(CancellationToken ct) { … }   // → ResetCommand
```

Generates a lazily-cached `SaveCommand` / `ResetCommand` exposed as `IVeloxCommand`.

Accepted signatures — `Task` or `void` return, and one of:

```csharp
Task M(object? parameter, CancellationToken ct);
Task M(object? parameter);
Task M(CancellationToken ct);
Task M();
void M(object? parameter);
void M();
```

**A concrete parameter type makes the command strongly typed.** `Task M(MoveArgs a)` produces `IVeloxCommand<MoveArgs>` instead of `IVeloxCommand`, and the `canValidate` hook takes `MoveArgs` rather than `object?`. If the type argument comes from the *containing class* (`partial class Vm<T>` with `Task M(T value)`), the property carries it too.

```csharp
[VeloxCommand] private Task MoveAsync(MoveArgs a, CancellationToken ct);  // → IVeloxCommand<MoveArgs> MoveCommand
```

⚙ **A generic method works only when its type parameter appears in the parameter type.** `Task M<T>(T value)` produces a *method* `IVeloxCommand<T> GetMCommand<T>()` — not a property, so it cannot be bound, and each closed `T` gets its own command with its own queue, lock and concurrency cap. `Task M<T>(object? p)` is refused with `VELOX_MVVM_CMD001`: a command instance fixes its type argument when it is built.

⚠ **Strong typing is type information, not a compile-time guarantee.** `IVeloxCommand<T>` derives from `IVeloxCommand`, so the `object?` overloads stay reachable — `vm.MoveCommand.Execute(42)` still compiles and still fails at runtime with `InvalidCastException`. Value types still box. Read it as documentation the consumer can see, not as a check.

⚙ **An interface declaring the property as plain `IVeloxCommand` forces the untyped form back.** That is why the workflow view-model interfaces keep working: the generator detects the contract and emits `IVeloxCommand` for the property while everything else stays typed.

⚙ **`name: "Auto"` (the default) derives the command name as `methodName.Replace("Async", "")`** — and that replaces *every* occurrence, not just the suffix. Two methods whose names collapse to the same stem produce duplicate members, which is a compile error.

**A body that returns `Task<T>` or `ValueTask<T>` hands its value back.** `Task<MoveArgs> Build()` produces `IVeloxCommand<object?, MoveArgs>`, and `await cmd.ExecuteAsync(p, cancellationToken)` yields the `T`:

```csharp
[VeloxCommand] private Task<int> MeasureAsync(NotePayload note, CancellationToken ct);  // → IVeloxCommand<NotePayload, int>
int weight = await vm.MeasureCommand.ExecuteAsync(note, CancellationToken.None);
```

⚠ **That await throws when the execution did not complete.** `Failed` rethrows the body's own exception, a cancelled execution throws `OperationCanceledException`, and a call refused by `Lock()` throws `InvalidOperationException`. If you would rather read the outcome than catch, use `ExecuteAndWaitAsync`, which never throws and reports a `CommandCompletion` whose `Result` holds the value.

⚙ **A command with no return value still has the result channel, and always yields `null`** — `TR` is `object?`. So a `null` result cannot be told apart from "this command has no value".

⚙ **The parameter type must be at least as visible as the generated property, which is always `public`.** A `[VeloxCommand]` method taking an `internal` type now fails to compile with CS0053, because the value-returning property names that type in its signature.

⚙ **`Execute(parameter, out result)`** is the blocking form. It waits for a free slot as well as for the body, so it occupies the calling thread — and calling it from inside the body of the same command deadlocks on the command's own lock.

⚙ **`canValidate: true` requires `private partial bool CanExecute{Name}Command(<parameter type> <source parameter name>)`** — the type follows the command's parameter (`object?` for an untyped command, the concrete type otherwise), and **the name must match the method's own parameter**. `MoveAsync(MoveArgs args)` needs `CanExecuteMoveCommand(MoveArgs args)`; any other name is CS8826. A method with no leading parameter has no source name to mirror, so there it stays `parameter`. You must implement it, and the build fails loudly until you do.

⚠ **The attribute's own documentation says the required member is called `CanXxx`.** It is not; the generator requires `CanExecuteXxxCommand(object?)`. Trust the generator, not the XML comment.

⚙ **`semaphore`** is the command's concurrency capacity (default 1, serialised). It is the same knob as `workSemaphore: 1` on a workflow node's `[WorkflowBuilder.Node<T>]`.

⚙ `IVeloxCommand` adds lifecycle over `ICommand`: `ExecuteAsync`, `Notify()`, `Lock`/`UnLock`, `Interrupt`/`Continue`, `Clear`, `ChangeSemaphore`, and `Created` / `Enqueued` / `Dequeued` / `Started` / `Completed` / `Failed` / `Canceled` / `Exited` events. Cancellation comes from the signature — there is no separate cancellable-command attribute.

## Interoperating with another MVVM framework

⚙ **The generator detects the host framework and adapts the setter.** If the class derives from Prism's `BindableBase` (or any base with `SetProperty(ref T, T, string)`), implements ReactiveUI's `IReactiveObject`, or derives from Caliburn.Micro's `PropertyChangedBase`, the setter routes through that framework's raise method. CommunityToolkit.Mvvm is detected by its `[ObservableObject]` **attribute** — a class that only *derives* from `ObservableObject` without the attribute still lands on the same path, because the generic `SetProperty(ref T, T, string)` probe matches it. Caliburn is the one that does not delegate the assignment: it still writes the field itself, then calls `NotifyOfPropertyChange`.

⚙ **A member already carrying `[ObservableProperty]`, `[Reactive]` or another competing attribute is skipped by design** — the two generators would fight over the same property.

⚙ **Whether the generated setter also calls `OnPropertyChanged` depends on the framework.** On the `SetProperty` path (CommunityToolkit, Prism) it does **not** — that framework's own method raises the event, and calling it again would double-fire. On the ReactiveUI and Caliburn paths it **does**. Your `OnXChanged` partial runs either way, so the VeloxDev callback contract holds regardless.

## Silent failures

Since there are no diagnostics, these are the failure modes to know by name:

⚙ **The class is not `partial`** — nothing is generated for any attribute.

⚙ **The derived property name already exists** on the class or a base — the member is skipped, silently.

⚙ **`[VeloxProperty]` on a property written without the `partial` keyword** — `public string X { get; set; }` looks right and produces nothing. This is the easiest mistake to make.

⚙ **A competing MVVM attribute** is present — skipped by design.

⚙ **Two workflow classes with the same name in different namespaces collide**, because that one generator names its file `{ClassName}.g.cs` without a namespace. The MVVM, command and tick generators include the namespace and are unaffected.

## Reference

⚙ `Examples/MVVM/WPF/Demo` and `Examples/MVVM/Avalonia/Demo` — the richest MVVM sample: observable properties, collections with every hook, seven commands, and the lock / interrupt / clear lifecycle.

⚙ `Src/Generators/VeloxDev.Core.Generator/Writers/` — `MVVMWriter.cs`, `CommandWriter.cs`. The generated shape is exactly what these emit, and reading the setter body is faster than guessing.

⚙ `Src/Core/VeloxDev.Core/MVVM/` — `VeloxPropertyAttribute`, `VeloxCommandAttribute`, `VeloxCommand`, `ObservableCollectionTracker`.

⚙ The workflow node idiom on top of this layer — which members are generated, which you write, and the slot lifecycle behind a `partial` slot property — is in [the workflow skill](../veloxdev-create-workflow/references/model.md).
