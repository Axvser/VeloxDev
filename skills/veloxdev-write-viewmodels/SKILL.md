---
name: veloxdev-write-viewmodels
description: Write ViewModels with VeloxDev's source generators — observable properties with [VeloxProperty], async commands with [VeloxCommand], collection change hooks, interoperating with CommunityToolkit/Prism/ReactiveUI/Caliburn, and the [MonoBehaviour] frame loop
---

## Responsibility

Write a class that the compile-time generators turn into a full ViewModel — properties that notify, commands with cancellation and a semaphore, collection hooks — without hand-writing `INotifyPropertyChanged` plumbing or a `RelayCommand`.

Package: **`VeloxDev.Core`**. No GUI adapter is needed and the behaviour is identical on every platform.

⚙ **Read this first: the class must be `partial`.** Every generator in the repository matches on the `partial` modifier, and **the generator project contains no diagnostics at all** — a missing `partial` produces no file, no warning and no error. The member you expected simply is not there. If `[VeloxProperty]` "does nothing", this is why.

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

⚙ **Naming**: one leading `_` is stripped and the next character upper-cased; otherwise just the first character. `_index` → `Index`, `title` → `Title`, `intervalMilliseconds` → `IntervalMilliseconds`, `m_value` → `M_value`. There is no camel-case splitting, and the rest is preserved verbatim — so `[VeloxProperty] private string user_name;` gives you a `user_name` property, not `UserName`.

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

⚙ **`name: "Auto"` (the default) derives the command name as `methodName.Replace("Async", "")`** — and that replaces *every* occurrence, not just the suffix. Two methods whose names collapse to the same stem produce duplicate members, which is a compile error.

⚙ **`canValidate: true` requires `private partial bool CanExecute{Name}Command(object? parameter)`** — you must implement it, and the build fails loudly until you do.

⚠ **The attribute's own documentation says the required member is called `CanXxx`.** It is not; the generator requires `CanExecuteXxxCommand(object?)`. Trust the generator, not the XML comment.

⚙ **`semaphore`** is the command's concurrency capacity (default 1, serialised). It is the same knob as `workSemaphore: 1` on a workflow node's `[WorkflowBuilder.Node<T>]`.

⚙ `IVeloxCommand` adds lifecycle over `ICommand`: `ExecuteAsync`, `Notify()`, `Lock`/`UnLock`, `Interrupt`/`Continue`, `Clear`, `ChangeSemaphore`, and `Created` / `Enqueued` / `Dequeued` / `Started` / `Completed` / `Failed` / `Canceled` / `Exited` events. Cancellation comes from the signature — there is no separate cancellable-command attribute.

## Interoperating with another MVVM framework

⚙ **The generator detects the host framework and adapts the setter.** If the class derives from Prism's `BindableBase` (or any base with `SetProperty(ref T, T, string)`), implements ReactiveUI's `IReactiveObject`, or derives from Caliburn.Micro's `PropertyChangedBase`, the setter delegates to that framework's raise method instead of assigning directly. CommunityToolkit.Mvvm is detected by attribute.

⚙ **A member already carrying `[ObservableProperty]`, `[Reactive]` or another competing attribute is skipped by design** — the two generators would fight over the same property.

⚙ With a framework setter, `OnPropertyChanged` is *not* called by the generated setter (the framework's own method raises it), but your `OnXChanged` partial still runs. The VeloxDev callback contract holds either way.

## Silent failures

Since there are no diagnostics, these are the failure modes to know by name:

⚙ **The class is not `partial`** — nothing is generated for any attribute.

⚙ **The derived property name already exists** on the class or a base — the member is skipped, silently.

⚙ **`[VeloxProperty]` on a property written without the `partial` keyword** — `public string X { get; set; }` looks right and produces nothing. This is the easiest mistake to make.

⚙ **A competing MVVM attribute** is present — skipped by design.

⚙ **`[MonoBehaviour]` must be the real attribute.** The generator matches the full name `VeloxDev.TimeLine.MonoBehaviourAttribute`; a user-defined attribute with the same short name is ignored without a word.

⚙ **Two workflow classes with the same name in different namespaces collide**, because that one generator names its file `{ClassName}.g.cs` without a namespace. The MVVM, command and mono generators include the namespace and are unaffected.

## `[MonoBehaviour]` — a frame loop on any class

```csharp
[MonoBehaviour(channel: "physics", fps: 60)]
public partial class PhysicsComponent
{
    public PhysicsComponent() => InitializeMonoBehaviour();

    partial void Awake() { }
    partial void Update(FrameEventArgs e) { … }
    partial void FixedUpdate(FrameEventArgs e) { … }
}

MonoBehaviourManager.Start("physics");
```

Gives a POCO a Unity-style lifecycle — `Awake` / `Start` / `Update` / `LateUpdate` / `FixedUpdate` — on its own background thread, with no UI framework involved. `FrameEventArgs` carries `DeltaTime`, `TotalTime`, `CurrentFPS`, `TargetFPS` and `Handled`.

⚙ **The attribute alone does nothing.** You must call `InitializeMonoBehaviour()` (conventionally from the constructor) **and** start the channel with `MonoBehaviourManager.Start(channel)`. A behaviour that never ticks is almost always a missing `Start`.

⚙ **The `partial void` name and parameter type must match exactly**, or the hook is never invoked — silently. `Update(FrameEventArgs e)` is the signature.

⚙ The channel defaults to `"default"`; `fps <= 0` means "use whatever the channel is already set to" (the manager's default is 60).

⚙ **Exceptions inside a behaviour are swallowed** to `Debug.WriteLine`. A `Update` that throws contributes nothing and reports nothing.

⚙ Setting `e.Handled = true` stops the remaining behaviours for that frame.

`MonoBehaviourManager` also offers `StopAsync`, `Pause`/`Resume`/`TogglePause`, `RestartAsync`, `SetTargetFPS`, `SetFixedUpdateInterval`, `SetTimeScale`, `ExecuteOnMainThread`, and the queries `IsRunning`, `IsPaused`, `ActiveBehaviorCount`, `SystemStatus`.

⚙ **`IMonoBehaviour` and `InitializeMonoBehaviour` contain a zero-width space (U+200B) in their names.** C# ignores formatting characters when comparing identifiers, so typing them normally binds correctly — but the names do not survive a copy-paste, a rename tool or a highlight-search. If you implement the interface by hand, retype it rather than pasting it.

⚙ **The one real consumer in the library is `TreeHelper<T>`**, which runs the workflow canvas's virtualization at 10 fps on a `TreeHelper` channel. That is the pattern to copy when a graph needs a periodic view concern: a helper deriving from a generated `partial` class, its `Update` doing the work, and the host owning start and stop.

## Reference

⚙ `Examples/MVVM/WPF/Demo` and `Examples/MVVM/Avalonia/Demo` — the richest MVVM sample: observable properties, collections with every hook, six commands, and the lock / interrupt / clear lifecycle.

⚙ `Examples/MonoBehaviour/WPF/Demo` — one window with three nested `[MonoBehaviour]` components, start on load and `await StopAsync` on close.

⚙ `Src/Generators/VeloxDev.Core.Generator/Writers/` — `MVVMWriter.cs`, `CommandWriter.cs`, `MonoWriter.cs`. The generated shape is exactly what these emit, and reading the setter body is faster than guessing.

⚙ `Src/Core/VeloxDev.Core/MVVM/` — `VeloxPropertyAttribute`, `VeloxCommandAttribute`, `VeloxCommand`, `ObservableCollectionTracker`.

⚙ The workflow node idiom on top of this layer — which members are generated, which you write, and the slot lifecycle behind a `partial` slot property — is in [the workflow skill](../veloxdev-create-workflow/references/model.md).
