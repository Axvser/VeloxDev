# MVVM Generator Multi-Framework Adaptation **Related Files:**

> - `Src/Generators/VeloxDev.Core.Generator/Base/Analizer.cs`（+97 / -15 lines） - `Src/Generators/VeloxDev.Core.Generator/Writers/MVVMWriter.cs` (+65 / -6 lines)
>   **Changes:** +162 / -21 lines

---

## Background

Previously, the source code generator used a uniform setter pattern for all types:

```csharp
if(Object.Equals(_field, value)) return;
var old = _field;
OnPropertyChanging(...);
_field = value;
OnPropertyChanged(new PropertyChangedEventArgs(nameof(Prop)));
```

This works fine without an MVVM base class, but when the class inherits from framework base classes such as `ObservableObject`, `BindableBase`, or `ReactiveObject`, it will **repeatedly trigger property notifications** and cannot utilize built-in optimization mechanisms like `SetProperty`.

## Improved Design

### SetterMode Enum

Added in `Analizer.cs`:

```csharp
public enum SetterMode
{
    /// <summary>Direct field assignment + Object.Equals check (original behavior in v5.4.0)</summary>
    Default,

    /// <summary>Delegates to SetProperty<T>(ref T, T, string)</summary>
    FrameworkSetProperty,

    /// <summary>Delegates to this.RaiseAndSetIfChanged<T>(ref T, T, string) [ReactiveUI]</summary>
    FrameworkRaiseAndSet,

    /// <summary>Uses NotifyOfPropertyChange(string) [Caliburn.Micro]</summary>
    FrameworkNotifyOfPropertyChange,
}
```

### Auto Detection Process

```mermaid
flowchart TD
    A[DetectSetterMode Detection] --> B{[ObservableObject] Attribute?}
    B -->|Yes| C[FrameworkSetProperty<br>← CommunityToolkit.Mvvm]

    B -->|No| D{Base class BindableBase<br>or SetProperty(ref T,T,string)?}
    D -->|Yes| E[FrameworkSetProperty<br>← Prism]

    D -->|No| F{Implements IReactiveObject?}
    F -->|Yes| G[FrameworkRaiseAndSet<br>← ReactiveUI]

    F -->|No| H{Has NotifyOfPropertyChange?}
    H -->|Yes| I[FrameworkNotifyOfPropertyChange<br>← Caliburn.Micro]

    H -->|No| J[Default<br>← No MVVM base class]
```

### Comparison of Setter Generation by Each Framework

#### Default (no MVVM base class)

```csharp
set
{
    if (global::System.Object.Equals(_field, value)) return;
    var old = _field;
    OnPropertyChanging(nameof(Field));
    OnFieldChanging(old, value);
    _field = value;
    OnFieldChanged(old, value);
    OnPropertyChanged(new PropertyChangedEventArgs(nameof(Field)));
}
```

#### FrameworkSetProperty（CommunityToolkit.Mvvm / Prism）

```csharp
set
{
    var old = _field;
    OnPropertyChanging(nameof(Field));
    OnFieldChanging(old, value);
    if (SetProperty(ref _field, value, nameof(Field)))
    {
        OnFieldChanged(old, _field);
        // SetProperty has already triggered PropertyChanged internally, do not call OnPropertyChanged again
    }
}
```

#### FrameworkRaiseAndSet（ReactiveUI）

```csharp
set
{
    var old = _field;
    OnPropertyChanging(nameof(Field));
    this.RaiseAndSetIfChanged(ref _field, value, nameof(Field));
    OnFieldChanged(old, _field);
    OnPropertyChanged(...);
}
```

#### FrameworkNotifyOfPropertyChange（Caliburn.Micro）

```csharp
set
{
    if (global::System.Object.Equals(_field, value)) return;
    var old = _field;
    OnPropertyChanging(nameof(Field));
    OnFieldChanging(old, value);
    _field = value;
    NotifyOfPropertyChange(nameof(Field));  // Respect IsNotifying check
    OnFieldChanged(old, value);
    OnPropertyChanged(...);
}
```

---

## Notification Method Lookup Priority Adjustment

Change the priority of the `BuildNotifyMethodInvocation` method in `MVVMWriter.cs` to:

1. **Prefer to look for framework-specific forwarding method names** (`RaisePropertyChanged`, `NotifyOfPropertyChange`, etc.)
2. **Next is the `EventArgs` overload**（`OnPropertyChanged(PropertyChangedEventArgs)`）
3. **Finally, the ReactiveUI extension methods** **Fixed issue:** Caliburn.Micro previously prioritized matching `OnPropertyChanged(PropertyChangedEventArgs)` causing it to skip `NotifyOfPropertyChange` (which contains the `IsNotifying` check), now it prioritizes using `NotifyOfPropertyChange`.

---

## Known Member Overlaps across Frameworks

| Framework               | Overlapping Events                     | Overlapping Methods                                                                                                                       | Events/Methods Required to Generate                                                                      |
| ----------------------- | -------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| CommunityToolkit.Mvvm | `PropertyChanging`, `PropertyChanged` | `OnPropertyChanging/Changed(string)` (protected, non-virtual); `OnPropertyChanging/Changed(EventArgs)` (protected virtual) | None (all provided by base class)                                                                        |
| Prism BindableBase    | `PropertyChanged` only               | `OnPropertyChanged(EventArgs)` (protected virtual); `RaisePropertyChanged(string)` (protected, non-virtual)                | `PropertyChanging` event, `INotifyPropertyChanging`, `OnPropertyChanging(string)`                    |
| ReactiveUI            | `PropertyChanging`, `PropertyChanged` | None (explicit interface implementation + extension methods)                                                                               | `OnPropertyChanging(string)` → delegate to extension methods                                       |
| Caliburn.Micro        | `PropertyChanged` only               | `OnPropertyChanged(EventArgs)` (protected); `NotifyOfPropertyChange(string)` (public virtual)                                          | `PropertyChanging` event, `INotifyPropertyChanging`, `OnPropertyChanging(string)`                    |

---

## Code Quality Improvement

Extract the setter logic originally inlined in `GenerateViewModel()` into a `GetSetterBodyLines()` method, dispatching by `SetterMode`.
- Add `FrameworkSetterMode` property to `MVVMPropertyFactory`
- Exclude `SetteredBody` via the `FrameworkSetProperty` pattern, avoiding duplicate notifications.