# Flow — the travelling highlight on a link

A settled link can carry a travelling highlight, so its direction is read from motion rather than from a small arrowhead. The animation belongs to the **tree**: every link travels the same three phases, so the motion is owned once and the views only sample it.

## The shared clock

`WorkflowFlowClock` (in `Examples/Workflow/Common/Lib`, compiled into the shared demo library) is one clock per tree:

```csharp
// The platform's link view installs its driver factory once.
WorkflowFlowClock.RegisterDriver(static clock => new FlowDriver(clock));

// A link view subscribes while it is attached and unsubscribes when released.
var clock = WorkflowFlowClock.ForLink(link);
clock?.Subscribe(OnClockTick);      // reference-counted: first subscriber starts, last stops
clock?.Unsubscribe(OnClockTick);
```

```csharp
private sealed class FlowDriver : IWorkflowFlowDriver
{
    private readonly Transition<WorkflowFlowClock> _flow;

    public FlowDriver(WorkflowFlowClock clock)
    {
        _flow = Transition<WorkflowFlowClock>.Create()
            .Property(c => c.BandHead, BandFormed)
            .Property(c => c.BandIntensity, 1d)
            .Effect(Phase(450))
            .Then()
            .Property(c => c.BandHead, BandLeaving)
            .Effect(Phase(700))
            .Then()
            .Property(c => c.BandHead, 1d)
            .Property(c => c.BandIntensity, 0d)
            .Effect(Phase(450))
            .Repeat(int.MaxValue);
    }
}
```

⚙ **One `Transition<WorkflowFlowClock>` per tree.** The clock is cached in a `ConditionalWeakTable` keyed by the tree; `Subscribe` is reference-counted, so many links on one tree run exactly one timer. A tree with no attached links runs none.

⚙ **`BandHead` and `BandIntensity` are written together, then the subscribers are notified.** `LateUpdate` fans one frame out to every subscriber, so a view never sees half a frame.

⚙ **The clock resolves a link's tree through its slot and node parents.** A link detached from any tree answers `null`; the view skips subscription.

## Per platform

| Platform | Who owns the motion | What the view does |
|---|---|---|
| WPF / Avalonia / WinUI | the tree's `WorkflowFlowClock` | subscribes in `Loaded` / `DataContextChanged`, copies `BandHead` / `BandIntensity`, repaints; unsubscribes in `Unloaded` / on rebind |
| WinForms | the surface (`WorkflowCanvas`) — one `Transition<WorkflowCanvas>` | the canvas writes `BandHead` / `BandIntensity` and `Invalidate()`s; link renderers read the pair |
| Jalium | the surface (`NodeEditorSurface`) — one `Transition<NodeEditorSurface>` | the surface writes a single `FlowPhase`; every link derives its arc-length position and intensity from it |
| Blazor | `flow.js` — one `requestAnimationFrame` loop for every link | the component registers/updates geometry only when it changes; the JS owns per-frame geometry and alpha |

⚙ **The Blazor link has no `Phase` property.** Its per-frame work is in `flow.js` (30fps throttle, `IntersectionObserver` pause for off-screen links). The `Phase` scalar in the Blazor demo belongs to `TemplateSlotView`, where it drives the port ripple. Keep the two separate.

⚙ **The WinForms and Jalium flows are surface-level.** One transition per surface, not per link; the surface redraws its links from the scalar.

## The shape of the phase

⚙ **Three phases, then `Repeat(int.MaxValue)`:** leave the sender and light up, travel fully lit, arrive and go dark. Both ends are the "no light" state, so the loop seam is invisible.

⚙ **The band comes up from the resting colour over the first third of its travel, travels fully lit and unchanged for the middle third, and settles back over the last third.** The last phase is also what makes the loop seam invisible.

⚙ **The line rests dim and the band is the same colour at full strength.** Dim the resting line by alpha (about three fifths) and keep the hue. Pushing the link colour towards white changes one channel of three on a 2px line and is invisible.

⚙ **A virtual link (the rubber band under the pointer) and a selected link keep a flat pen.** One is not a settled connection and the other is already highlighted.

## Design rules

⚙ **Animate a scalar the view repaints from, and derive geometry and colour from it in the draw pass.** The scalar is part of the path, so a phase is a handful of writes whose endpoints a reader can check against the constants, and there is no arithmetic in between to get wrong.

⚙ **Give the scalar a change notification that triggers a repaint.** A dependency/styled/bindable property with `AffectsRender`, or a `LateUpdate` that calls `InvalidateVisual` / `StateHasChanged`, is the platform's mechanism.

⚙ **Drive a scalar and derive the drawing from it.** An indexed write into a `GradientStops` collection changes the value, but a retained-mode framework does not repaint until told; Jalium measured gradient-stop writes, whole-collection swaps and axis moves as producing an identical frame. Use an indexed brush path only in a one-off sampler probe, not in a moving light.

⚙ **The flow cycle owns the phase; a link move only re-aims the gradient and re-colours the shoulders.** Endpoint changes happen every frame of a zoom or a node drag; the phase keeps its captured endpoints. A second writer fights the animation for a frame, which reads as a band that stutters while the canvas moves.

⚙ **Subscribe on attach and unsubscribe on detach.** Views are pooled; a released view handed a different link must stop sampling the previous flow. The shared clock starts on its first subscriber and stops on its last.

⚙ **The flow is decoration.** The Trimmed demo suites leave it out; the full demos include it. A minimal editor can omit it entirely.

## Writing your own flow

1. Decide whether the motion is per tree (shared) or per surface. Per tree is the default for a link flow; per surface is right for an immediate-mode canvas that draws every link anyway.
2. Declare the three phases over a scalar on the owner (`BandHead` / `BandIntensity`, or a single `FlowPhase`).
3. Expose the scalar to the link views, or let them subscribe to the owner.
4. Compute geometry and colour from the scalar in the draw pass.
5. Make the loop's start and end states identical.
6. Unsubscribe or stop on detach.
