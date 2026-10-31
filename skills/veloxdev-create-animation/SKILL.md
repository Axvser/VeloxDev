---
name: veloxdev-create-animation
description: Write VeloxDev interpolation animations — pick the adapter package for the project's GUI, declare transitions in the layout the shipped demos use, choose static-reuse or create-and-discard, express indices (constant, live, frozen), build chains and flow animations, and build an adapter for a GUI that has no official one. Use when adding, fixing or porting an animation or transition, or when animating a custom view on any supported GUI.
---

## Responsibility

Produce animation code that is **readable as prose** and **correct on first run**, for whatever GUI the project already uses. Every rule below matches the layout the shipped demos use — follow that layout.

## Package selection

Identify the project's GUI, then reference **one** package. Versions are deliberately not stated: take the latest published.

| GUI | Package |
|---|---|
| WPF | `VeloxDev.WPF` |
| Avalonia | `VeloxDev.Avalonia` |
| WinUI 3 / Windows App SDK | `VeloxDev.WinUI` |
| .NET MAUI | `VeloxDev.MAUI` |
| Windows Forms | `VeloxDev.WinForms` |
| Blazor / Razor | `VeloxDev.Razor` |
| Jalium | `VeloxDev.Jalium` |

The adapter package brings `VeloxDev.Core` in transitively, so that one reference is enough. Add `VeloxDev.Core` explicitly when you also want its non-UI parts.

If the project's GUI is not in that table, read [references/practices/adapter.md](references/practices/adapter.md) before writing animation code.

`using VeloxDev.TransitionSystem;` covers the everyday API — `Transition<T>`, `TransitionEffect`, `Eases`, `TransitionEffects`, `PathIndex`, the event args and the path exceptions. The support types live one namespace down: `using VeloxDev.TransitionSystem.Abstractions;` is needed when you name `TransitionProperty` (`ReadableMembers` / `Members`), `InterpolatorCore`, or a scheduler / interpreter / host base. Every adapter exposes the same `VeloxDev.TransitionSystem` surface. Six of the seven also declare the generic `Property<TValue>` overload; **Razor offers typed overloads only** (`int`, `string?`, `double`, `float`, `decimal`, the `System.Drawing` structs and the `System.Numerics` types), so generic `Property` code does not compile there unchanged.

Typed `Property` overloads are per adapter otherwise. Jalium's are generic `TValue`, `Point`, `Rect`, `Thickness`, `CornerRadius`, `Size`, `Color`, `Brush`, `Transform`, `Transform3D`, `int`, `double` and `float`. **`decimal` has no sampler:** Core registers none, so the six adapters that declare a `decimal` overload accept the call and skip the path at run time, and Jalium reaches the same skip through its generic `Property<TValue>`.

## The canonical layout

```csharp
private static readonly Transition<Rectangle> Shift =
    Transition<Rectangle>.Create()
        .Property(r => r.Opacity, 0d)
        .Property(r => ((TranslateTransform)r.RenderTransform).X, 40d)
        .Effect(new TransitionEffect()
        {
            Duration = TimeSpan.FromMilliseconds(2000),
            Ease = Eases.Sine.InOut,
        });
```

⚙ `Transition<T>` — `T` is **the object the animation writes into**, and it is a reference type (`where T : class`). It is not necessarily the control: when the index or the value has to be read from somewhere, a small model object holding both the control's data and that somewhere is the normal shape.

⚙ `Transition<T>.Create()` sits on its own line, indented one level from the field.

⚙ **One `.Property(...)` per line**, indented one more level.

⚙ **The opening brace follows the initializer's shape.** For a multi-member initializer write `new TransitionEffect()` and put `{` on the **next** line, members one per line, closing with `});`. For a one- or two-member initializer written on a single line, keep the brace on that line: `.Effect(new TransitionEffect { Duration = …, Ease = … })`.

⚙ The declaration ends at `Create()...Effect(...)`. `Execute` is the terminal call: a separate statement for a reused declaration, and chained at the end of a one-shot declaration. It is never part of the cached declaration.

## Which form to use

**Same shape and same endpoint every time → `static readonly`:**

```csharp
private static readonly Transition<Rectangle> Fade =
    Transition<Rectangle>.Create()
        .Property(r => r.Opacity, 0d)
        .Effect(new TransitionEffect()
        {
            Duration = TimeSpan.FromSeconds(1),
            Ease = Eases.Sine.InOut,
        });

// …elsewhere
Fade.Execute(rect);
```

**Endpoint or path computed per call → build at the call site and let it go:**

```csharp
Transition<Border>.Create()
    .Property(b => b.Width, requestedWidth)
    .Effect(new TransitionEffect()
    {
        Duration = TimeSpan.FromMilliseconds(240),
        Ease = Eases.Cubic.Out,
    })
    .Execute(border);
```

⚙ **A declaration is immutable and reusable.** Holding it in a `static readonly` field and calling `Execute` many times — including on different targets at once — is the intended use.

⚙ **A declaration that reads a local is built per caller.** An endpoint local is frozen when the declaration is built; an index local is captured by the closure and read live. Either way one cached declaration would share the first caller's data with every later execution.

## Where to read next

| You are about to… | Read |
|---|---|
| Express an index (constant, live, frozen) | [references/practices/declarations.md](references/practices/declarations.md#indices) |
| Chain segments, repeat, wait, add a frame hook | [references/practices/segments-and-effects.md](references/practices/segments-and-effects.md) |
| Pause, resume, seek, set a rate, stop | [references/practices/control-and-samplers.md](references/practices/control-and-samplers.md#controlling-a-running-animation) |
| Interpolate a custom type | [references/practices/control-and-samplers.md](references/practices/control-and-samplers.md#custom-samplers) |
| Animate a link's travelling highlight / a flow | [references/practices/flow.md](references/practices/flow.md) |
| Build an adapter for another GUI | [references/practices/adapter.md](references/practices/adapter.md) |

## Pitfalls

Read the symptom first, then the pitfall file. Each entry is a failure the repository has actually hit.

| Symptom | Read |
|---|---|
| A cached declaration animates the first caller's value | [references/pitfalls/declarations.md](references/pitfalls/declarations.md) |
| An index write does nothing, or lands on the wrong slot | [references/pitfalls/declarations.md](references/pitfalls/declarations.md) |
| An animation stops early, loops forever, or cannot be stopped | [references/pitfalls/segments.md](references/pitfalls/segments.md) |
| A custom type never animates, or a sampler is never reached | [references/pitfalls/samplers.md](references/pitfalls/samplers.md) |
| A flow freezes, stutters, or runs one timer per link | [references/pitfalls/flow.md](references/pitfalls/flow.md) |
| An adapter drops frames, hangs, or animates at two frames | [references/pitfalls/adapter.md](references/pitfalls/adapter.md) |

## Reference

⚙ `Examples/Transition/<GUI>/Demo` — one runnable demo per supported GUI, all exercising the same API. (Blazor's is nested one level deeper: `Examples/Transition/Blazor/Demo/Demo`.) `SamplerSubject.cs` is the object the animations write into; `SamplerProbe.cs` is one row per sampler and the two indexed paths. Both are `internal` to the demo project — read them for the shape, do not reference them. Blazor has no `SamplerSubject.cs`; it animates a plain `INotifyPropertyChanged` model (`Demo/Demo/Models/BoxModel.cs`).

⚙ `Examples/Transition/AUTO TEST/AGENTS.md` — how the acceptance suite is run and extended.

⚙ The engine contract is pinned by `Src/Core/VeloxDev.Core.Test/TransitionSystem/` and `.../Timing/`; run them with `dotnet test Src/Core/VeloxDev.Core.Test/VeloxDev.Core.Test.csproj --filter "FullyQualifiedName~TransitionSystem|FullyQualifiedName~Timing"`.
