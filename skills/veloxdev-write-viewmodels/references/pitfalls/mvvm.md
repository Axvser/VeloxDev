# Pitfalls — MVVM generators

Failures the repository has actually hit. Each entry names the symptom, the rule it violates, and the fix. The correct rules are in [properties.md](../practices/properties.md) and [commands.md](../practices/commands.md).

## `[VeloxProperty]` / `[VeloxCommand]` does nothing

**Symptom:** no generated member, no warning, no error.

**Cause:** the class is not `partial`; it is a `partial record` / `partial struct` (not a target); the file is excluded or behind a false preprocessor branch; or the analyzer is missing.

**Fix:** make it a `partial class`; include the file in the compilation; add `VeloxDev.Core.Generator` as an analyzer when consuming Core by `ProjectReference`.

## A property with an initializer never notifies

**Symptom:** the field is set, but no `PropertyChanged`.

**Cause:** the property form was expected to complete accessors that already exist, or the property is not `partial`.

**Fix:** write `[VeloxProperty] public partial T X { get; set; }`; the generator adds the accessors. A non-partial property reports `VELOX_MVVM_PROP003`.

## A property is skipped with `VELOX_MVVM_PROP001`

**Symptom:** the generated property is missing or the build reports a conflict.

**Cause:** two members map to one property name; a same-named non-field member exists; or an existing field is const/static/read-only or of a different type and cannot back the property.

**Fix:** rename one member, remove the competing member, or give the field a compatible writable declaration.

## A property is skipped with `VELOX_MVVM_PROP002`

**Symptom:** a field name produces no usable identifier.

**Cause:** the name is `_`, starts with a non-letter (e.g. `_1x`), or otherwise cannot form a property.

**Fix:** rename the field.

## A collection hook is missing, or the class fails with `CS0103`

**Symptom:** `OnItemAddedTo…` is absent; a get-only collection property does not compile.

**Cause:** the collection hooks and their private handler are emitted only when the property has a setter, but the getter still installs the subscription.

**Fix:** give the collection property a setter. A get-only collection is not supported.

## A command cannot be bound

**Symptom:** the generated member is `GetMCommand<T>()`, not a property.

**Cause:** the method is generic. A generic method cannot carry `T` on a command instance, so it produces a method.

**Fix:** bind a non-generic command, or call `GetMCommand<T>()` in code.

## The validator does not compile

**Symptom:** `CS8826`, or a missing implementation.

**Cause:** the `private partial bool CanExecute{Name}Command(...)` signature does not match the generated declaration, or `canValidate: true` was set without implementing it.

**Fix:** copy the generated signature exactly — parameter types and names follow the source method; a method with no leading parameter gets `object? parameter`. A generic method's validator repeats its type parameters and constraints.

## A validator throws on a null parameter

**Symptom:** a null-reference exception during layout or `CanExecute`.

**Cause:** WPF asks `CanExecute(null)` for a button without a `CommandParameter`; the pipeline passes null to a reference-typed validator.

**Fix:** null-check the parameter in the validator. A value-typed parameter never reaches the predicate.

## A command parameter type is `internal`

**Symptom:** `CS0053` on the generated property.

**Cause:** a typed command names its parameter type in a public signature.

**Fix:** make the parameter type public, or force the property back to plain `IVeloxCommand` through an interface/base declaration.

## Two commands collapse to one name

**Symptom:** `CS0102` in the generated file.

**Cause:** `Auto` removes every `Async`, so `Save` and `SaveAsync` both become `SaveCommand`.

**Fix:** give one method an explicit name (`[VeloxCommand("SaveNow")]`).

## The generated setter raises `PropertyChanged` twice

**Symptom:** UI work runs twice per change on CommunityToolkit or Prism.

**Cause:** the setter is on the `SetProperty` path (the framework raises) and `OnPropertyChanged` is also called.

**Fix:** let the framework's `SetProperty` raise; the generator already omits the extra call on that path. Do not call `OnPropertyChanged` manually for the same property.

## A `[VeloxProperty]` member is ignored in favour of another generator

**Symptom:** the property is not generated.

**Cause:** the member carries `[ObservableProperty]` or `[Reactive]` from another MVVM framework.

**Fix:** remove one of the two attributes. The two generators are not designed to share a property.

## Two workflow classes with the same name collide

**Symptom:** a generated-file collision for workflow components.

**Cause:** the workflow generator names its file `{ClassName}.g.cs` without a namespace segment; the MVVM/command/tick generators include one.

**Fix:** give the workflow classes distinct names, or split them into separate compilations.

## A low `LangVersion` reports `VELOX_LANGVERSION001`

**Symptom:** the generated code does not compile, or the warning appears.

**Cause:** generated code needs C# 12; the `partial`-property form needs C# 13.

**Fix:** set `LangVersion` to `12` / `latest` for C# 12 code, and to a C# 13-capable SDK for `partial` properties.
