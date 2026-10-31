# The three hooks

```csharp
using VeloxDev.AspectOriented;

var p = data.Aop();

ProxyHandler logStart = (_, previous) => previous;
ProxyHandler replace = (_, _) => null;   // coverage: its return becomes the member's result
ProxyHandler logEnd = (_, _) => { /* react */ return null; };

p.SetProxy(ProxyMembers.Getter, nameof(TeamViewModel.Name), logStart, null, logEnd);
p.SetProxy(ProxyMembers.Setter, nameof(TeamViewModel.Name), logStart, replace, logEnd);
p.SetProxy(ProxyMembers.Method, nameof(TeamViewModel.Reset), logStart, replace, logEnd);
```

⚙ A handler is the `ProxyHandler` delegate: `object? ProxyHandler(object?[]? parameters, object? previous)`. `AspectHooks` bundles the three stages, and `ProxyEx.SetProxy` installs them.

⚙ **`ProxyMembers.Getter` / `Setter` / `Method` pick the kind, and the name is the plain member name** — `nameof(TeamViewModel.Name)`, not `get_Name`. A property needs its getter and setter hooked separately.

⚙ **`parameters` are the call's arguments** — a snapshot, not a channel back into the member. `parameters[0]` is the first one: a setter's new value, a method's first parameter; a getter has none. Cast before reading (`parameters?[1] is NotifyCollectionChangedEventArgs e`).

## The stages

| Stage | When | Its return value |
|---|---|---|
| `start` | before the member | passed to `coverage` as its `previous`; ignored when there is no `coverage` |
| `coverage` | **non-null replaces the member** — the body does not run | **becomes the member's return value** |
| `end` | after the member | **discarded** |

⚙ Pass `null` for any stage you do not need. A hook that only logs is `end` alone; a hook that vetoes is `coverage` alone.

⚙ `start`'s return does not reach `end` directly. Without a `coverage` hook, `end` receives the member's own result — the value for a getter or a value-returning method, `null` for a setter or a `void` method. With a `coverage` hook, it receives `coverage`'s return. A setter's written value is never in `previous`; read it from `parameters?[0]`, so reacting to a write is `(parameters, previous) => { var written = parameters?[0]; … }` on the `end` stage.

## Worked examples

Log every read of a property:

```csharp
p.SetProxy(ProxyMembers.Getter, nameof(TeamViewModel.Name),
    null,
    null,
    (_, _) => { Console.WriteLine($"read at {DateTime.Now}"); return null; });
```

Veto a method — its body never runs:

```csharp
p.SetProxy(ProxyMembers.Method, nameof(TeamViewModel.Reset),
    null,
    (_, _) => { Console.WriteLine("Reset() was cancelled"); return null; },
    null);
```

Extend a handler the class already calls internally:

```csharp
p.SetProxy(ProxyMembers.Method, nameof(TeamViewModel.AOP_OnMemberAdded),
    null,
    null,
    (parameters, _) =>
    {
        if (parameters?[1] is not NotifyCollectionChangedEventArgs e || e.NewItems is null) return null;
        foreach (MemberViewModel member in e.NewItems) Console.WriteLine($"{member.Name} added");
        return null;
    });
```

⚙ **A `coverage` hook makes the member's body dead.** If the member had side effects the rest of the class depends on, they stop happening. That is the point of `coverage`; use `end` when you only want to react.

⚙ **`parameters` is a snapshot.** The generated proxy calls the target with its own original values/locals, so `parameters?[0] = …` changes nothing for the member; only later stages in the same call see the new value. To change behaviour, use `coverage`.

⚙ **A `coverage` hook that returns `null` for a value-type member fails** with `NullReferenceException` at the generated unbox cast; returning the wrong type fails with `InvalidCastException`.
