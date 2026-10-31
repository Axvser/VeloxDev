# Marking members and reaching the proxy

## What gets generated

For a `partial` class with at least one marked member, two generators (`AopSurface`, and `AopProxy` via `AopWriter`) emit three artifacts into your assembly. For `Demo.TeamViewModel`:

| Artifact | Shape |
|---|---|
| Mirror interface | `public interface TeamViewModel_Demo_Aop : IAspectOriented, IAopHookTarget` in namespace `VeloxDev.AopInterfaces` |
| Proxy | `internal sealed class TeamViewModel_Demo_AopProxy : TeamViewModel_Demo_Aop` |
| Entry point | `public static TeamViewModel_Demo_Aop Aop(this Demo.TeamViewModel instance)` in `VeloxDev.AspectOriented` |

⚙ The name segment is the containing namespace with `.` replaced by `_`; a class in the global namespace uses `Global`.

⚙ The proxy is `internal sealed`, holds the target, and is the only implementation of the interface. `Aop()` constructs it once per instance and caches it. **Nothing is injected into your class** — it does not implement the interface.

⚙ A member with no hooks installed calls the target directly: no argument array, no boxing, no delegate invocation.

⚙ `IAopHookTarget` is the proxy's installation surface: `void SetHooks(string memberKey, AspectHooks? hooks)`. `ProxyEx.SetProxy` calls it; a host does not.

## Marking members

Three kinds of member can be marked.

**A field-backed property** — mark the field with the MVVM generator's attribute **and** `[AspectOriented]`:

```csharp
[VeloxProperty][AspectOriented] private string _name = string.Empty;
```

⚙ The proxy exposes the **generated property** (`Name`), not the field. A field marked `[AspectOriented]` without an MVVM attribute produces no proxy member.

**A property** — mark it, and it is **public**, with a public accessor for whichever direction you intercept. A write-only property (a setter with no getter) makes the generated proxy read the target and fail with `CS0154`; an `init`-only property gets no setter slot, so `ProxyMembers.Setter` on it throws `ArgumentOutOfRangeException`.

**A method** — mark it, and it is **public**:

```csharp
[AspectOriented]
public void Reset()
{
    Name = string.Empty;
    Members.Clear();
}
```

⚙ Write `[AspectOriented]` unqualified. The generator matches the attribute text `AspectOriented` when collecting members, so a qualified or aliased form is not recognised.

⚙ The body you write is the member's **default** behaviour. An aspect can replace it at runtime; you never change it to make that possible.

## Reaching the proxy

```csharp
var p = data.Aop();
```

⚙ `Aop()` is **generated for every class that has at least one marked member**; call it. It returns the generated interface, whose type name you never need to spell. The extension lives in `VeloxDev.AspectOriented`.

⚙ **The class is `partial`.** Discovery only accepts partial class declarations; on a class without it, `Aop()` is not generated and the call fails to compile.

⚙ **One proxy per instance, cached.** The cache is a weak table keyed by the target, and the reverse lookup is another weak table, so the proxy goes away once your code drops the instance. Caching means you call `Aop()` wherever you need it rather than storing it.

⚙ `Aop.GetTarget<TTarget>(proxy)` goes the other way, from a proxy back to the instance it wraps. `TTarget` is `where TTarget : class`, and the lookup returns `null` when the proxy is not in the reverse map.

## Calling through the proxy

**This is the rule that decides whether an aspect fires at all:**

```csharp
data.Aop().Name = "New Team Name";   // the aspect runs
data.Name = "New Team Name";         // the proxy is bypassed
```

⚙ **Calls made through the proxy are intercepted.** Every call the caller wants intercepted goes through `Aop()`.

⚙ **A call the class makes on itself goes straight to the real member.** Route it through the proxy to make it interceptable — the shipped demo does exactly this:

```csharp
private void OnMemberAdded(object? sender, NotifyCollectionChangedEventArgs e)
{
    this.Aop().AOP_OnMemberAdded(sender, e);   // through the proxy, so the aspect can see it
}

[AspectOriented]
public void AOP_OnMemberAdded(object? sender, NotifyCollectionChangedEventArgs e) { … }
```

⚙ The marker-method pattern (`AOP_…`) turns an internal call into a proxy call without changing what the class does.

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

⚙ `SetProxy` on the same member again **replaces** the previous triplet.
