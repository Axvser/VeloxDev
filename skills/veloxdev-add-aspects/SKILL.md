---
name: veloxdev-add-aspects
description: Add aspects to VeloxDev classes with compile-time proxies — mark members with [AspectOriented], reach them through Aop(), and hook behaviour before, instead of, and after the member runs, without editing the member's own body
---

## Responsibility

Wrap behaviour around an existing member — log it, veto it, replace it, react to it — **without editing that member's code**. The mechanism is a compile-time proxy: source generators write a mirror interface, a proxy class and an `Aop()` extension while the project builds, and `Aop()` hands you one cached proxy per instance.

## What it is, and what it is not

⚙ **Compile-time, and reflection-free.** The generators emit ordinary C# into your own assembly. There is no assembly scanning, runtime subclassing, runtime code generation or `dynamic` — so a trimmed or NativeAOT-published app can use this, and an intercepted call costs no reflection lookup.

⚙ **Only the members you mark exist on the proxy.** Everything else on the class is not reachable through it.

⚙ **Only calls made through the proxy are intercepted.** This is the single thing to get right — see [Calling through the proxy](#calling-through-the-proxy).

⚙ It is not general interception: constructors, static members and events are not interceptable. A field is only a way to mark the property the MVVM generator creates from it — the property is what gets intercepted.

## Which package

`VeloxDev.Core` — no GUI adapter is involved, and the feature is identical on every GUI.

⚙ The AOP runtime is wrapped in `#if NET`. The package ships it in its `net5.0` and `net8.0` assets, so a `net5.0`-or-later project resolves a compatible asset. A `netcoreapp3.0`, `netstandard2.0` or `netframework4.6.1` target does not define `NET` and gets neither the attribute nor the runtime types.

⚙ `AspectOrientedAttribute` and the hook types live in `VeloxDev.AspectOriented`; `IAspectOriented` is the empty marker every generated interface inherits. The NuGet package brings `VeloxDev.Core.Generator` as a dependency, so referencing `VeloxDev.Core` is enough — an in-repo `ProjectReference` does not carry analyzers, which is why the demos add the generator separately.

## What gets generated

For a `partial` class with at least one marked member, two generators (`AopSurface`, and `AopProxy` via `AopWriter`) emit three artifacts into your assembly. For `Demo.TeamViewModel`:

| Artifact | Shape |
|---|---|
| Mirror interface | `public interface TeamViewModel_Demo_Aop : IAspectOriented, IAopHookTarget` in namespace `VeloxDev.AopInterfaces` |
| Proxy | `internal sealed class TeamViewModel_Demo_AopProxy : TeamViewModel_Demo_Aop` |
| Entry point | `public static TeamViewModel_Demo_Aop Aop(this Demo.TeamViewModel instance)` in `VeloxDev.AspectOriented` |

⚙ The name segment is the containing namespace with `.` replaced by `_`; a class in the global namespace uses `Global`.

⚙ The proxy is `internal sealed`, holds the target, and is the only implementation of the interface. `Aop()` constructs it once per instance and caches it. **Nothing is injected into your class** — do not expect it to implement the interface.

⚙ A member with no hooks installed calls the target directly: no argument array, no boxing, no delegate invocation.

⚙ `IAopHookTarget` is the proxy's installation surface: `void SetHooks(string memberKey, AspectHooks? hooks)`. You never call it; `ProxyEx.SetProxy` does.

## Marking members

Three kinds of member can be marked, and each has a rule about what actually lands on the proxy.

**A field-backed property** — mark the field with the MVVM generator's attribute **and** `[AspectOriented]`:

```csharp
[VeloxProperty][AspectOriented] private string _name = string.Empty;
```

⚙ The proxy exposes the **generated property** (`Name`), not the field. A field marked `[AspectOriented]` without an MVVM attribute produces no proxy member.

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

⚙ Write `[AspectOriented]` unqualified. The generator matches the attribute text `AspectOriented` when collecting members, so a qualified or aliased form is not recognised.

⚙ The body you write is the member's **default** behaviour. An aspect can replace it at runtime; you never have to change it to make that possible.

## Reaching the proxy

```csharp
var p = data.Aop();
```

⚙ `Aop()` is **generated for every class that has at least one marked member** — do not write it. It returns the generated interface, whose type name you never need to spell. The extension lives in `VeloxDev.AspectOriented`.

⚙ **The class has to be `partial`.** Discovery only accepts partial class declarations; on a class without it, `Aop()` is simply not generated and the call fails to compile.

⚙ **One proxy per instance, cached.** The cache is a weak table keyed by the target, and the reverse lookup is another weak table, so the proxy goes away once your code drops the instance — nothing is held for the life of the process. Caching means you can call `Aop()` wherever you need it rather than storing it.

⚙ `Aop.GetTarget<T>(proxy)` goes the other way, from a proxy back to the instance it wraps.

## The three hooks

```csharp
var p = data.Aop();

p.SetProxy(ProxyMembers.Getter, nameof(TeamViewModel.Name), start, coverage, end);
p.SetProxy(ProxyMembers.Setter, nameof(TeamViewModel.Name), start, coverage, end);
p.SetProxy(ProxyMembers.Method, nameof(TeamViewModel.Reset), start, coverage, end);
```

⚙ A handler is the `ProxyHandler` delegate: `object? ProxyHandler(object?[]? parameters, object? previous)`. `AspectHooks` bundles the three stages, and `ProxyEx.SetProxy` is the installation method.

⚙ **`ProxyMembers.Getter` / `Setter` / `Method` pick the kind, and the name is the plain member name** — `nameof(TeamViewModel.Name)`, not `get_Name`. A property needs its getter and setter hooked separately.

⚙ **`parameters` are the call's arguments.** `parameters[0]` is the first one — a setter's new value, a method's first parameter; a getter has none. Cast before you read (`parameters?[1] is NotifyCollectionChangedEventArgs e`).

⚙ The three stages run in order, and they are the whole model:

| stage | when | its return value |
|---|---|---|
| `start` | before the member | passed on as the next stage's `previous` |
| `coverage` | **non-null replaces the member** — the body does not run at all | **becomes the member's return value** |
| `end` | after the member | **discarded** |

⚙ Pass `null` for any stage you do not need. A hook that only logs is `end` alone; a hook that vetoes is `coverage` alone.

⚙ **`end`'s return is thrown away** — a handler that "returns the new result" changes nothing. To change what the caller receives, use `coverage`.

⚙ **A getter's `previous` on `end` is the value that was read**, because a getter returns it and so it flows through. **A setter's is `null`** — a setter returns `void`, so there is nothing to pass on, and the written value reaches you only through `parameters?[0]`. Reacting to a write is therefore `(parameters, previous) => { var written = parameters?[0]; … }` on the `end` stage, never `previous`.

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

⚙ `SetProxy` on the same member again **replaces** the previous triplet — it does not stack.

## Pitfalls

⚙ **A `coverage` hook makes the member's body dead.** If the member had side effects the rest of the class depends on, they stop happening. That is the point of `coverage`, but it is easy to reach for it when `end` was what you wanted.

⚙ **Aspect on the wrong kind.** A property whose *field* is marked needs the MVVM attribute on that field, otherwise the generator has nothing to put on the proxy. A non-public property or method is skipped without a diagnostic, so the aspect silently never fires.

⚙ **Expecting `end` to shape the result.** It cannot — see the table above.

⚙ **Do not mark an overloaded method.** A member's hook slot is named after the member alone, so two methods sharing a name collide — the generated proxy then fails to compile with `CS0102`, and the error points into generated code rather than at your method.

⚙ **Do not mark a generic method.** The generated interface drops the type-parameter list, so any signature that uses its type parameter references an undefined type and fails to compile.

⚙ **Do not mark a method with `ref` or `out` parameters.** The generated proxy calls the target with plain argument names, omitting the modifier, so it fails to compile. `in` parameters are not preserved either: the emitted call omits `in` and passes the value by copy.

⚙ **Do not mark an expression-bodied property** (`public string X => …`). Only accessor lists are recognised, so a property written that way reaches the proxy with neither accessor and fails to compile with `CS0548`. Give it an accessor list — `{ get; }` is enough — or mark something else.

⚙ **Installing aspects on the object itself no longer compiles.** `SetProxy` requires the generated interface, which only the generated proxy implements, so `data.SetProxy(…)` is a compile error. A hand-written `IAspectOriented` implementation does compile, but `SetProxy` refuses it at runtime with `InvalidOperationException`. The hooks belong on what `Aop()` returned.

⚙ **A name that matches no member throws `ArgumentOutOfRangeException`** rather than being ignored.

⚙ **A `coverage` hook that returns `null` for a value-type member fails** with `NullReferenceException` at the generated unbox cast; returning the wrong type fails with `InvalidCastException`.

## Reference

⚙ `Examples/AOP/WPF/Demo` and `Examples/AOP/Avalonia/Demo` — two runnable demos of everything above: fields and methods marked for interception, a getter hook, a setter hook, a method that is vetoed, and handlers extended into a collection's add and remove.

⚙ `Src/Core/VeloxDev.Core/AspectOriented/` — the runtime: `ProxyMembers`, `ProxyHandler`, `AspectHooks`, `IAopHookTarget` (in `AspectHooks.cs`), `ProxyEx.SetProxy`, `Aop` (`Map` / `GetTarget`, the weak reverse map), and `AopCache` (the per-instance weak cache). The proxy class itself is generated code, not runtime infrastructure. `IAspectOriented` is in `Src/Core/VeloxDev.Core/Interfaces/AspectOriented/IAspectOriented.cs`.

⚙ `Src/Generators/VeloxDev.Core.Generator/` — `AopSurface.cs` (interface + proxy), `AopProxy.cs` and `Writers/AopWriter.cs` (the `Aop()` extension), `Base/AopNames.cs` (the one naming algorithm).

⚙ `Src/Core/VeloxDev.Core.Test/AspectOriented/` — `AopFixture.cs` and `AopProxyTests.cs` (13 tests) pin stage order, replacement, caching/collectability and the error paths.
