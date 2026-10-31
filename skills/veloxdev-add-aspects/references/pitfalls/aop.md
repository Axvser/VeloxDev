# Pitfalls — aspects

Failures the repository has actually hit. Each entry names the symptom, the rule it violates, and the fix. The correct rules are in [marking-and-proxies.md](../practices/marking-and-proxies.md) and [hooks.md](../practices/hooks.md).

## The attribute is applied but `Aop()` does not exist

**Symptom:** `data.Aop()` does not compile.

**Cause:** the class is not `partial`, or the generator/analyzer is missing.

**Fix:** mark the class `partial`; reference `VeloxDev.Core.Generator` as an analyzer when consuming Core by `ProjectReference` (a NuGet reference to `VeloxDev.Core` brings it in).

## The aspect is installed but nothing fires

**Symptom:** a hook is set, but the member runs as usual.

**Cause:** the call bypasses the proxy. `data.Name = …` and any call the class makes on itself go straight to the real member.

**Fix:** call through `data.Aop().Name`. For an internal call, route it through the proxy (the `AOP_…` marker-method pattern).

## A property hook installs but never runs

**Symptom:** the getter hook fires, the setter hook does not (or the reverse).

**Cause:** a property needs its getter and setter hooked separately (`ProxyMembers.Getter` / `ProxyMembers.Setter`). A field-backed property is exposed as the generated property.

**Fix:** install both stages you need. Confirm the field carries both the MVVM attribute and `[AspectOriented]`.

## Installing a hook throws `ArgumentOutOfRangeException`

**Symptom:** `SetProxy` throws for a member that looks marked.

**Cause:** the member was skipped during generation — a non-public method/property, a field without the MVVM attribute, or a name that matches nothing. The generated `SetHooks` switch has no slot for it.

**Fix:** make the member public and mark it as the generator expects; check the name is the plain member name. A name that matches no member throws the same exception.

## The generated code fails to compile

**Symptom:** errors point into generated code after a member is marked.

**Cause:** an unsupported member shape — an overloaded method (`CS0102`), a generic method, `ref` / `out` parameters, an expression-bodied property (`CS0548`), or a public static method (`CS0176`).

**Fix:** remove the attribute from the unsupported member or change its shape. The generator's error names the collision but points into generated code; check the member you just marked.

## `end` cannot change what the caller receives

**Symptom:** the `end` hook returns a value, but the caller still gets the member's result.

**Cause:** `end`'s return is discarded by design. Only `coverage` replaces the member.

**Fix:** use a `coverage` hook; use `end` only for side effects.

## A `coverage` hook returns null and the call crashes

**Symptom:** `NullReferenceException` or `InvalidCastException`.

**Cause:** the member returns a value type and `coverage` returned `null`, or it returned the wrong type.

**Fix:** return the member's declared type (boxed for a value type), or return a valid default.

## Two marked members collide

**Symptom:** `CS0102` in the generated proxy.

**Cause:** a member's hook slot is named after the member alone; two methods sharing a name (overloads) collide.

**Fix:** mark at most one overload, or wrap the overloads behind a single marked method.

## A hook is installed on the wrong instance

**Symptom:** the aspect fires on one object and not another.

**Cause:** the proxy is per-instance; a hook installed on `a.Aop()` does not affect `b.Aop()`.

**Fix:** configure each instance after construction. Call `Aop()` where you need it rather than caching a proxy elsewhere.
