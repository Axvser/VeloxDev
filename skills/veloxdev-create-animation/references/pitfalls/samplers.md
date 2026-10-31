# Pitfalls — custom samplers and registrations

Failures the repository has actually hit. Each entry names the symptom, the rule it violates, and the fix. The correct rules are in [control-and-samplers.md](../practices/control-and-samplers.md).

## A custom type never animates

**Symptom:** the path is accepted, but the property does not change; `Warn` reports `Unsampled`.

**Cause:** no sampler is registered for the declared property type, or the sampler was registered after the declaration was prepared. A reference-type path with no sampler is dropped; a value-type path is dropped at frame time.

**Fix:** register the sampler before building or executing the declaration. Register the type the property is *declared* as, not only its concrete runtime type.

## A sampler registered for a base type is never reached

**Symptom:** a property declared as `Effect` (or another abstract base) does not animate, even though a sampler exists for a concrete subclass.

**Cause:** the lookup walks exact type, then base classes, then interfaces — upward only. A concrete registration does not serve a base-declared property.

**Fix:** register the abstract type and handle the whole family in the sampler; or change the property's declared type. WPF registers `typeof(Effect)` for this reason.

## The first frame throws `InvalidCastException`

**Symptom:** the animation is cancelled on its first frame; the sampler's cast fails.

**Cause:** the registration key and the type the sampler body unboxes are different types that share a simple name in different assemblies (for example `RectF` versus `RectangleF`).

**Fix:** make the key's type identical to the cast. Spell the sampler's using list out when the framework ships same-named geometry.

## A readonly struct is never animated

**Symptom:** an `ISampleable` struct is skipped; no error.

**Cause:** `TransitionProperty.Members<T>` keeps only readable **and writable** members. On a readonly struct it returns an empty list, the assembler returns null, and the value is skipped.

**Fix:** declare the members with `TransitionProperty.ReadableMembers<T>` and reconstruct through the struct's constructor in `CreateFrameValue`.

## Animated values are shared or corrupted between runs

**Symptom:** one animation's scratch leaks into another, or the declared endpoint changes.

**Cause:** the sampler mutated `start` / `end` (they are shared with the snapshot), or kept mutable state on the registered singleton.

**Fix:** keep samplers stateless and treat the endpoints as read-only. Scratch state belongs in `working`, which is per segment.

## A custom `Prepare` override breaks endpoint resolution

**Symptom:** frozen indices stop binding, or the start value is read on the wrong thread.

**Cause:** the override replaced the base chain instead of extending it.

**Fix:** call the base implementation, then adjust. `Prepare` is the settlement point for reading paths on the owning thread and binding `Frozen` arguments.

## A `decimal` property silently does nothing

**Symptom:** a `decimal` path is accepted and never changes.

**Cause:** Core registers no decimal sampler. The six adapters that declare a `decimal` overload accept the call and skip the path at run time; Jalium reaches the same skip through its generic `Property<TValue>`.

**Fix:** use `double` / `float` for animation, or register a decimal sampler explicitly.

## A registration is missing from the acceptance suite

**Symptom:** the coverage test fails after a sampler is added.

**Cause:** every published sampler needs a row in `Examples/Transition/AUTO TEST/Samplers/` (or the unreachable list). The suite reflects over the product assemblies and fails when one is unaccounted for.

**Fix:** add the row with its declared type and closed form; add it to the unreachable list only when its endpoint cannot be constructed in a headless process.
