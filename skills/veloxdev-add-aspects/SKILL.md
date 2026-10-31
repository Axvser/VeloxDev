---
name: veloxdev-add-aspects
description: Add aspects to VeloxDev classes with compile-time proxies — mark members with [AspectOriented], reach them through Aop(), and hook behaviour before, instead of, and after the member runs, without editing the member's own body. Use when you need to log, veto, replace or react to an existing member's execution without changing that member.
---

## Responsibility

Wrap behaviour around an existing member — log it, veto it, replace it, react to it — **without editing that member's code**. The mechanism is a compile-time proxy: source generators write a mirror interface, a proxy class and an `Aop()` extension while the project builds, and `Aop()` hands you one cached proxy per instance.

## What it is

⚙ **Compile-time, and reflection-free.** The generators emit ordinary C# into your own assembly. There is no assembly scanning, runtime subclassing, runtime code generation or `dynamic` — a trimmed or NativeAOT-published app can use this, and an intercepted call costs no reflection lookup.

⚙ **Only the members you mark exist on the proxy.** Everything else on the class is not reachable through it.

⚙ **Only calls made through the proxy are intercepted.** The aspect is on the proxy, not on the class; every call the caller wants intercepted goes through `Aop()`.

⚙ Constructors and events cannot carry the attribute (`AttributeUsage` is `Method | Property | Field`). A public static method is collected but the generated proxy calls it through the target, which fails to compile. A field is a way to mark the property the MVVM generator creates from it — the property is what gets intercepted.

## Which package

`VeloxDev.Core` — no GUI adapter is involved, and the feature is identical on every GUI.

⚙ The AOP runtime is wrapped in `#if NET`. The package ships it in its `net5.0` and `net8.0` assets, so a `net5.0`-or-later project resolves a compatible asset. A `netcoreapp3.0`, `netstandard2.0` or `netframework4.6.1` target does not define `NET` and gets neither the attribute nor the runtime types.

⚙ `AspectOrientedAttribute` and the hook types live in `VeloxDev.AspectOriented`; `IAspectOriented` is the marker every generated interface inherits. The NuGet package brings `VeloxDev.Core.Generator` as a dependency, so referencing `VeloxDev.Core` is enough; an in-repo `ProjectReference` does not carry analyzers, which is why the demos add the generator separately.

⚙ The toolchain floor is Roslyn 4.0+ / VS 2022 17.0+ / .NET SDK 6+ (`VeloxDev.Core.Generator.targets`); on an older Roslyn the generator reports `VELOXCFG0001` and disables itself. Its `LanguageVersionCheck` warns `VELOX_LANGVERSION001` when `LangVersion` maps below C# 12. Set `LangVersion` to `12` or `latest`; that option needs Roslyn 4.8 or later, so the practical floor is a .NET 8 SDK (or VS 2022 17.8+).

## Where to read next

| You are about to… | Read |
|---|---|
| Mark a field, property or method, and reach the proxy | [references/practices/marking-and-proxies.md](references/practices/marking-and-proxies.md) |
| Install the three hooks and read their return values | [references/practices/hooks.md](references/practices/hooks.md) |
| Call through the proxy, including from inside the class | [references/practices/marking-and-proxies.md](references/practices/marking-and-proxies.md#calling-through-the-proxy) |

## Pitfalls

| Symptom | Read |
|---|---|
| The aspect is installed but nothing fires | [references/pitfalls/aop.md](references/pitfalls/aop.md) |
| A call bypasses the proxy | [references/pitfalls/aop.md](references/pitfalls/aop.md) |
| The generated code fails to compile after marking a member | [references/pitfalls/aop.md](references/pitfalls/aop.md) |
| Replacing a member's return value does not work | [references/pitfalls/aop.md](references/pitfalls/aop.md) |

## Reference

⚙ `Examples/AOP/WPF/Demo` and `Examples/AOP/Avalonia/Demo` — two runnable demos: fields and methods marked for interception, a getter hook, a setter hook, a method that is vetoed, and handlers extended into a collection's add and remove.

⚙ `Src/Core/VeloxDev.Core/AspectOriented/` — the runtime: `ProxyMembers`, `ProxyHandler`, `AspectHooks`, `IAopHookTarget`, `ProxyEx.SetProxy`, `Aop` (`Map` / `GetTarget`, the weak reverse map), and `AopCache`. The proxy class itself is generated code. `IAspectOriented` is in `Src/Core/VeloxDev.Core/Interfaces/AspectOriented/IAspectOriented.cs`.

⚙ `Src/Generators/VeloxDev.Core.Generator/` — `AopSurface.cs` (interface + proxy), `AopProxy.cs` and `Writers/AopWriter.cs` (the `Aop()` extension), `Base/AopNames.cs` (the one naming algorithm).

⚙ `Src/Core/VeloxDev.Core.Test/AspectOriented/` — `AopFixture.cs` and `AopProxyTests.cs` pin stage order, replacement, caching/collectability and the error paths.
