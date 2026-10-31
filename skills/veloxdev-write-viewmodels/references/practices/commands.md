# Commands

## Naming

```csharp
[VeloxCommand] private void Save() { … }                                    // → SaveCommand
[VeloxCommand("Reset")] private Task ResetAsync(CancellationToken ct) { … } // → ResetCommand
```

⚙ `name: "Auto"` (the default) uses `methodName.Replace("Async", "")` and appends `Command`; any other value is used verbatim and still gets `Command` appended. The replacement removes **every** `Async`, not just the suffix. Two methods that collapse to the same stem emit duplicate members — a `CS0102` in the generated file.

⚙ The generated property (or accessor) is lazily cached on first read.

## Accepted signatures

The return type is `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>` or `void`. Leading parameters are optional, at most **15**, and may be followed by one trailing `CancellationToken`.

| Body shape | Generated member |
|---|---|
| no leading parameter; `Task`, `ValueTask`, `void` | untyped `IVeloxCommand` |
| no leading parameter; `Task<T>` / `ValueTask<T>` | untyped `IVeloxCommand`, with the value in the boxed result channel |
| one `object` / `object?` parameter (any return) | untyped `IVeloxCommand`; the argument and any value stay boxed |
| one parameter of a concrete type `T`; no value | `IVeloxCommand<T, object?>` |
| one parameter of a concrete type `T`; `Task<TR>` / `ValueTask<TR>` | `IVeloxCommand<T, TR>` |
| 2–15 leading parameters | `IVeloxCommand<T1, …, Tn, TR>` from the arity family in `CommandArities.cs` |
| generic method `M<T>(T value)` | cached accessor `IVeloxCommand<T, TR> GetMCommand<T>()` |

`TR` is the body's `T` for `Task<T>` / `ValueTask<T>`, and `object?` (always `null`) otherwise. The two-parameter-plus arity classes pack the arguments into a `ValueTuple` internally; the public surface is tuple-free.

```csharp
[VeloxCommand] private Task<int> AddAsync(int left, int right) { … }   // → IVeloxCommand<int, int, int>
int sum = await vm.AddCommand.ExecuteAsync(2, 3, CancellationToken.None);
```

**Rejected shapes** report `VELOX_MVVM_CMD001` on the method's own line and are skipped; the rest of the class still generates:

- `void M(CancellationToken ct)` — a synchronous body cannot observe the token.
- A generic method whose type parameters do not appear in its parameter types.
- More than 15 leading parameters.
- A return type outside the accepted set.
- A member named `{Name}Command` already exists on the type.

## Generic methods

⚙ **A generic method produces a method, not a property.** `Task M<T>(T value)` generates `IVeloxCommand<T, TR> GetMCommand<T>()`, so it cannot be bound. The cache is keyed by all method type parameters; each closed `T` gets its own command, queue, lock and concurrency cap.

## Validation and concurrency

⚙ **`canValidate: true`** makes the generator declare `private partial bool CanExecute{Name}Command(...)` — you implement it, and the build fails loudly until you do. The validator's parameter list copies all leading parameters' types and names from the source method; a method with no leading parameter gets `object? parameter`. A generic method's validator is a generic partial method with the same type parameters and constraints. Any name mismatch is `CS8826`. Call `{Name}Command.Notify()` (or `Get{Name}Command<T>().Notify()`) when state that affects the predicate changes.

⚙ **A validator accepts `null` when `TParam` is a reference type.** `CommandPipeline<TParam, TResult>.CanExecute(object?)` passes a null argument to `CanExecuteCore(default!)`, and WPF does exactly that when a button has no `CommandParameter`. Null-check the parameter. A value-typed parameter never reaches the predicate: the pipeline rejects the null first.

⚙ **`semaphore` is the command's concurrency capacity** (default 1, serialised). Generated commands clamp it with `Math.Max(1, semaphore)`; the `VeloxCommand` constructor and `ChangeSemaphore` throw `ArgumentOutOfRangeException` for a value below 1.

⚙ **Parameter type visibility.** A generated untyped property is always `public`; when the property is typed, its parameter type is named in that public signature, so an `internal` parameter type fails with `CS0053`. An interface or base that forces the property back to plain `IVeloxCommand` removes that failure.

⚙ **A declared command property fixes the property's static type.** If an interface or base declares `IVeloxCommand MoveCommand`, the generated property is emitted as `IVeloxCommand` (the buffer and pipeline stay typed). A declaration of the exact matching `IVeloxCommand<TParam, TResult>` is kept. A declaration that cannot accept the generated command is rejected with `VELOX_MVVM_CMD001`.

## Typed commands and the result channel

`IVeloxCommand<TParam, TResult>` derives from `IVeloxCommand`, so the `object?` overloads stay reachable and **strong typing is not a compile-time guarantee**: `vm.MoveCommand.Execute(42)` still compiles and ends as a `Failed` execution carrying `InvalidCastException`. The typed interface buys a readable signature, a strongly typed validator, and an unboxed path through the pipeline.

```csharp
Task<TResult> ExecuteAsync(TParam parameter, CancellationToken cancellationToken);  // waits for this execution
void Execute(TParam parameter, out TResult result);                                 // blocking twin
```

⚙ The one-argument `ExecuteAsync(TParam)` returns as soon as the call is accepted. The two-argument overload exists so it *overloads* that member rather than hiding it.

⚙ **A body returning `Task<T>` or `ValueTask<T>` fills the result channel.** When the command is typed, the value comes back unboxed. A command with **no** leading parameter stays untyped (`IVeloxCommand`) even when its body returns a value, so there the value is available only through the boxed interfaces.

```csharp
[VeloxCommand] private Task<int> MeasureAsync(NotePayload note, CancellationToken ct) { … } // → IVeloxCommand<NotePayload, int>

// Typed: unboxed, throws when the execution did not complete.
int weight = await vm.MeasureCommand.ExecuteAsync(note, CancellationToken.None);

// Untyped: boxed through IVeloxCommandResult.
object? value = await ((IVeloxCommandResult)vm.MeasureCommand).ExecuteAsync(note, CancellationToken.None);

// Outcome without throwing.
CommandCompletion completion = await vm.MeasureCommand.ExecuteAndWaitAsync(note);
if (completion.Succeeded)
    weight = completion.GetResultOrThrow<int>();
```

- `IVeloxCommandResult` declares `Task<object?> ExecuteAsync(object?, CancellationToken)` and `void Execute(object?, out object?)`. The token overload has no default, so a one-argument call cannot become ambiguous.
- `ExecuteAndWaitAsync` lives on `IVeloxCommandCompletion`; reach it from a generated property through the `VeloxCommandExtensions` extension. It never throws for a body failure or a cancellation — it reports `CommandOutcome.Completed` / `Failed` / `Canceled` / `Refused`. Only the caller's own `CancellationToken` aborts the wait, and that does throw.
- `CommandCompletion` is a `readonly struct` with `Outcome`, `Exception`, `Result` (boxed), `Succeeded`, `GetResultOrThrow()` and `GetResultOrThrow<TR>()`. The awaitable typed `ExecuteAsync` rethrows the body's own exception, throws `OperationCanceledException` when cancelled, and `InvalidOperationException` when refused by `Lock()`.
- `Refused` has no `CommandEventType` counterpart: a refused call raises `Canceled` and never reaches `Exited`.
- A command with no return value still has the result channel and always yields `null`, so `null` cannot be told apart from "this command has no value".

⚙ **Typed events without boxing.** `IVeloxCommandEvents<TParam, TResult>` exposes the same eight stages with `CommandEventArgs<TParam, TResult>`, whose `TypedParameter` and `TypedResult` are in their own types. The class-level `Created` / `Failed` / … stay the `object?`-shaped ones that binding needs; subscribe through a reference typed as `IVeloxCommandEvents<,>` to get the typed payload.

## Runtime types

| Type | Role |
|---|---|
| `IVeloxCommand : ICommand` | eight lifecycle events and `ExecuteAsync`, `Notify`, `Lock` / `Unlock`, `Interrupt` / `Continue`, `Clear`, `ChangeSemaphore` (each with an `…Async` twin where the synchronous form would block) |
| `IVeloxCommand<TParam, TResult>` | typed facade: `CanExecute(TParam)`, `Execute(TParam)`, `ExecuteAsync(TParam)`, `ExecuteAsync(TParam, ct)`, `Execute(TParam, out TResult)` |
| `IVeloxCommandEvents<TParam, TResult>` | typed version of the eight events, carrying `CommandEventArgs<TParam, TResult>` |
| `IVeloxCommandResult` | boxed value channel: `ExecuteAsync(object?, CancellationToken)` and `Execute(object?, out object?)` |
| `IVeloxCommandCompletion` | `ExecuteAndWaitAsync(object?, CancellationToken)` returning `Task<CommandCompletion>` |
| `IVeloxCommandStatus` | `IsBusy`, `ActiveCount`, `PendingCount`; reached from `IVeloxCommand` through `VeloxCommandExtensions` |
| `CommandPipeline<TParam, TResult>` | public abstract engine implementing the interfaces above. Adds `EventContext`, the static `HandlerException` hook, and protected typed helpers (`CanExecuteCore`, `ExecuteAsyncCore`, `ExecuteAndWaitTypedAsync`, `ExecuteAndWaitTyped`) |
| `VeloxCommand` | **not sealed**; derives from `CommandPipeline<object?, object?>`. The untyped/fallback shape that binding reaches. |
| `VeloxCommand<TParam, TResult>` | sealed one-parameter typed shape. The arity family declares sealed `VeloxCommand<T1, …, Tn, TResult>` for 2–15 parameters. |
| `CommandEventArgs` | **not sealed**; public event payload with `Parameter`, `EventType`, `Exception`, and `With(...)` |
| `CommandEventArgs<TParam, TResult>` | sealed subclass adding `TypedParameter` and `TypedResult` |
| `CommandCompletion` | `readonly struct` returned by `ExecuteAndWaitAsync` |
| `CommandEventType` | `None`, `Created`, `Enqueued`, `Dequeued`, `Started`, `Completed`, `Failed`, `Canceled`, `Exited` |
| `CommandOutcome` | `Completed`, `Failed`, `Canceled`, `Refused` |

**Lifecycle.** Immediate: `Created → Started → Completed → Exited`. Queued: `Created → Enqueued`, then `Dequeued → Started → …` when a slot frees. Refused by `Lock`: `Created → Canceled` only. Cancelled while running: `Created → Started → Canceled → Exited`. An execution reports at most one `Canceled`; a throwing subscriber never disturbs the pipeline and is reported through the static `HandlerException` event. Events are raised inline by default; set `EventContext` to a UI `SynchronizationContext` to post them there.

**Cancellation.** A `CancellationToken` parameter is what lets the command stop the body. Every other shape builds a command whose body never receives a token: an interrupted execution reports `Canceled`, but the body runs to completion.
