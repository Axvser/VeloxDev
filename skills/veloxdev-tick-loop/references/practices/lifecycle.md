# The lifecycle and the five hooks

## `[Tickable]`

```csharp
[Tickable("physics", TargetFPS = 60)]
public partial class PhysicsComponent { … }
```

⚙ **`[Tickable]` is the real attribute.** The generator matches the full name `VeloxDev.TimeLine.TickableAttribute`; an attribute of your own with the same short name is ignored without a word. It applies to classes only, is not inherited, and allows one instance per class.

⚙ **`TickableAttribute.Channel` is get-only.** Pass the channel positionally; a named `Channel = ...` is `CS0617`. `TargetFPS` is settable, so `[Tickable("physics", TargetFPS = 60)]` is the form. The generator reads named arguments after positional ones, and a named value wins.

⚙ **The class is `partial`.** The hook name and signature match exactly, or the build fails: the generator emits the defining `partial void` declarations, so a hook with a body but no matching declaration is `CS0759`, and a non-partial method with the exact signature collides with the generated one (`CS0111`).

⚙ **A missing analyzer is not `CS0535`.** Without the generator your class never declares `ITickable`; every implemented `partial void` hook reports `CS0759`, and the remaining failures are `CS0103` on `InitializeTickable` (or `CS1061` on a generated member). To inspect the output, build with `-p:EmitCompilerGeneratedFiles=true`; it lands under `obj/<Configuration>/<tfm>/generated/VeloxDev.Core.Generator/VeloxDev.Generators.Tickable/`.

## The five hooks

| Hook | When it runs |
|---|---|
| `Awake` | Once per applied registration, on the update thread in the registration drain, before the first frame. |
| `Start` | Immediately after `Awake` in the same drain, before the first `Update`. |
| `Update(FrameEventArgs e)` | Once per frame on the update thread, before `LateUpdate`. `DeltaTime` is measured and uncompensated. |
| `LateUpdate(FrameEventArgs e)` | Once per frame, after every behaviour's `Update`; shares that frame's arguments and `Handled` flag. |
| `FixedUpdate(FrameEventArgs e)` | On the fixed-update thread, concurrently with `Update` / `LateUpdate`, once per owed constant step; a stall is repaid as a batch. |

`FrameEventArgs` carries `DeltaTime`, `TotalTime`, `CurrentFPS` and `TargetFPS`; the two times and `Handled` live on the base `TimeLineEventArgs`. All setters except `Handled` are `internal`, so a hook reads the measurements and writes only `Handled`.

⚙ **`e.Handled = true` stops the rest of the `Update` sweep and the whole `LateUpdate` sweep for that frame.** The behaviour that set it is not skipped; everything after it is. `FixedUpdate` steps build their own `FrameEventArgs`, so they are unaffected.

⚙ **The arguments object is pooled and reused for every hook of a frame.** A hook reads `e` during the call and does not store it.

⚙ **Fixed-step delivery is independent of the frame rate.** With time scale 1, `FixedUpdate` is called about `(1000/h)·N` times over `N` seconds for a step of `h` ms — about 62.5/s at the default 16 ms — regardless of the target FPS. One wake-up may deliver several owed steps; the catch-up burst is capped at 8 steps per advance, and debt above 64 steps is forgiven rather than repaid.

## Registration and removal

⚙ **`InitializeTickable()` registers the behaviour on its channel.** It is generated: it applies the attribute's `TargetFPS` when one was declared, then calls `RegisterBehaviour`.

⚙ **`CloseTickable()` is generated and enqueues an unregistration.** It pairs with `InitializeTickable()`; removal is silent (there is no close hook). Registering the same instance again runs `Awake` / `Start` again on a fresh wrapper.

⚙ **Separate the pair by at least one pump drain.** One drain applies additions before removals, so `CloseTickable(); InitializeTickable();` registers and then immediately unregisters — `Awake` / `Start` run, but the behaviour ends up off the channel. The demo splits them across two button clicks.

⚙ **`CloseTickable()` is called on uninstall, not from the constructor.** The generated forwarders are the only members you implement by hand if you replace the generator.
