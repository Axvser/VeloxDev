---
name: veloxdev-add-aspects
description: Add aspects to VeloxDev classes with compile-time proxies — mark members with [AspectOriented], reach them through Aop(), and hook behaviour before, instead of, and after the member runs, without editing the member's own body
---

## Responsibility

Wrap behaviour around an existing member — log it, veto it, replace it, react to it — **without editing that member's code**. The mechanism is a compile-time proxy: a source generator writes an interface for the marked members, and `Aop()` hands you a `DispatchProxy` implementing it.

## What it is, and what it is not

⚙ **Compile-time.** The generator emits the interface and the `Aop()` entry point while the project builds. There is no assembly scanning, no runtime subclassing, and no reflection to set up.

⚙ **Only the members you mark exist on the proxy.** Everything else on the class is simply not reachable through it.

⚙ **Only calls made through the proxy are intercepted.** This is the single thing to get right — see [Calling through the proxy](#calling-through-the-proxy).

⚙ It is not general interception: constructors, fields, static members and events are not interceptable.

## Which package

`VeloxDev.Core` — no GUI adapter is involved, and the feature is identical on every GUI. (The types are compiled for .NET 5 and later, so a netstandard2.0 or .NET Framework target will not see them.)

## Marking members

Three kinds of member can be marked, and each has a rule about what actually lands on the proxy.

**A field-backed property** — mark the field with the MVVM generator's attribute **and** `[AspectOriented]`:

```csharp
[VeloxProperty][AspectOriented] private string _name = string.Empty;
```

⚙ The proxy exposes the **generated property** (`Name`), not the field. A field marked `[AspectOriented]` without an MVVM attribute produces nothing.

**A property** — mark it, and it has to be **public**, with a public accessor for whichever direction you want to intercept.

**A method** — mark it, and it has to be **public**:

```csharp
[AspectOriented]
public void Reset()
{
    Name = string.Empty;
    Members.Clear();
}
```

⚙ The body you write is the member's **default** behaviour. An aspect can replace it at runtime; you never have to change it to make that possible.

## Reaching the proxy

```csharp
var p = data.Aop();
```

⚙ `Aop()` is **generated for every class that has at least one marked member** — do not write it. It returns the generated interface, whose type name you never need to spell.

⚙ **One proxy per instance, cached**, and the cache is a `ConditionalWeakTable`, so the proxy dies with the instance it wraps. Call `Aop()` wherever you need it rather than storing it.

⚙ `Aop.GetTarget<T>(proxy)` goes the other way, from a proxy back to the instance it wraps.

## The three hooks

```csharp
var p = data.Aop();

p.SetProxy(ProxyMembers.Getter, nameof(TeamViewModel.Name), start, coverage, end);
p.SetProxy(ProxyMembers.Setter, nameof(TeamViewModel.Name), start, coverage, end);
p.SetProxy(ProxyMembers.Method, nameof(TeamViewModel.Reset), start, coverage, end);
```

⚙ **`ProxyMembers.Getter` / `Setter` / `Method` pick the kind, and the name is the plain member name** — `nameof(TeamViewModel.Name)`, not `get_Name`. A property needs its getter and setter hooked separately.

⚙ A handler is `object? (object?[]? parameters, object? previous)`.

⚙ **`parameters` are the call's arguments.** `parameters[0]` is the first one — a setter's new value, a method's first parameter; a getter has none. Cast before you read (`parameters?[1] is NotifyCollectionChangedEventArgs e`).

⚙ The three stages run in order, and they are the whole model:

| stage | when | its return value |
|---|---|---|
| `start` | before the member | passed on as the next stage's `previous` |
| `coverage` | **non-null replaces the member** — the body does not run at all | **becomes the member's return value** |
| `end` | after the member | **discarded** |

⚙ Pass `null` for any stage you do not need. A hook that only logs is `end` alone; a hook that vetoes is `coverage` alone.

⚙ **`end`'s return is thrown away** — a handler that "returns the new result" changes nothing. To change what the caller receives, use `coverage`.

⚙ A getter's or setter's `previous` on `end` is the value that was read or written, so reacting to a write is `(parameters, previous) => { var written = parameters?[0]; … }` on the `end` stage.

### Worked examples

Log every read of a property:

```csharp
p.SetProxy(ProxyMembers.Getter, nameof(TeamViewModel.Name),
    null,
    null,
    (_, _) => { log.Write($"read at {DateTime.Now}"); return null; });
```

Veto a method — its body never runs:

```csharp
p.SetProxy(ProxyMembers.Method, nameof(TeamViewModel.Reset),
    null,
    (_, _) => { log.Write("Reset() was cancelled"); return null; },
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
        foreach (MemberViewModel member in e.NewItems) log.Write($"{member.Name} added");
        return null;
    });
```

## Calling through the proxy

**This is the rule that decides whether an aspect fires at all:**

```csharp
data.Aop().Name = "New Team Name";   // the aspect runs
data.Name = "New Team Name";         // it does not — this never touches the proxy
```

⚙ The aspect is on the **proxy**, not on the class. Every call the caller wants intercepted has to go through `Aop()`.

⚙ **A member that the class calls on itself is not intercepted either.** When a collection raises an event and the class handles it, that internal call goes straight to the real method. Route it through the proxy to make it interceptable — the shipped demo does exactly this:

```csharp
private void OnMemberAdded(object? sender, NotifyCollectionChangedEventArgs e)
{
    this.Aop().AOP_OnMemberAdded(sender, e);   // through the proxy, so the aspect can see it
}

[AspectOriented]
public void AOP_OnMemberAdded(object? sender, NotifyCollectionChangedEventArgs e) { … }
```

⚙ This is why the pattern of naming a marker method (`AOP_…`) and forwarding to it exists: it turns an internal call into a proxy call without changing what the class does.

## Installing the hooks

Set them **once, right after the object is constructed**, in one place:

```csharp
public MainWindow()
{
    InitializeComponent();
    _teamData = new TeamViewModel();
    ConfigureAOP(_teamData);
}
```

⚙ The hooks live on the proxy instance and the proxy is per-instance and cached, so setting them once is enough for the object's whole life.

⚙ `SetProxy` on the same member again replaces the previous triplet — it does not stack.

## Pitfalls

⚙ **A `coverage` hook makes the member's body dead.** If the member had side effects the rest of the class depends on, they stop happening. That is the point of `coverage`, but it is easy to reach for it when `end` was what you wanted.

⚙ **Aspect on the wrong kind.** A property whose *field* is marked needs the MVVM attribute on that field, otherwise the generator has nothing to put on the proxy; and a non-public member is skipped by the generator without a diagnostic, so the aspect silently never fires.

⚙ **Expecting `end` to shape the result.** It cannot — see the table above.

⚙ **Do not mark an overloaded method.** The proxy resolves the target's member by name alone, so two methods sharing a name make the lookup ambiguous and the intercepted call fails instead of running.

⚙ The proxy holds a reference to the target and the registry of proxies is a plain static map, so proxies are not collected before the process ends even though the proxy-to-target lookup is weak. Registering aspects on a very large number of short-lived objects is the case to watch.

## Reference

⚙ `Examples/AOP/WPF/Demo` and `Examples/AOP/Avalonia/Demo` — two runnable demos of everything above: fields and methods marked for interception, a getter hook, a setter hook, a method that is vetoed, and handlers extended into a collection's add and remove.

⚙ `Src/Core/VeloxDev.Core/AspectOriented/` — the runtime: `ProxyInstance` (the `DispatchProxy` and the three stage tables), `ProxyEx` (creating a proxy and installing hooks), `AopCache` (the per-instance cache), `Aop` (proxy-to-target lookup).
