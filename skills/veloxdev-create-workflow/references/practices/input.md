# Input — one route, one handle, one argument per action

Adapters translate their platform's pointer and keys into Core's standard arguments; `WorkflowInput` decides who hears them. It owns one piece of state — which component the pointer is on — and performs no action of its own. Selecting, dragging, deleting and opening a menu belong to the adapter's gesture logic or to the host.

## The interfaces

| Type | Meaning |
|---|---|
| `IInputEvents` | A helper's input surface: `InputRelay Input { get; }`. `TreeHelper`, `NodeHelper`, `SlotHelper` and `LinkHelper` implement it, so a component's own events are reached through `((IInputEvents)component.GetHelper()).Input`. |
| `InputRelay` | The per-component event hub. It exposes the pointer and key events for that one component; subscribing here hears the events the route delivers to this component and no others. |
| `WorkflowInput` | The per-tree route and drag state: `For(tree)`, `Route(...)`, `PointerTarget`, the connection-preview fields, `Scroller` / `Navigator`, `HitRadius`, `IsSuspended`. |
| `WorkflowEventHandle` | The per-action decision pair: `PreventDefault` and `StopPropagation`. One handle is shared by every subscriber of the same routed action. |

## The route

```csharp
var input = WorkflowInput.For(tree);   // one instance per tree, same lifetime
input.Route(args);                     // the adapter calls this once per action
```

⚙ **One instance per tree.** `WorkflowInput.For(tree)` creates it on first use and keeps it as long as the tree lives, so the adapter and the host meet at the same call.

⚙ **One argument instance, one handle, the whole way.** The adapter creates exactly one args object per action; every subscriber on the route sees that same object and its `Handle`. A decision made nearer the target is readable by an ancestor.

⚙ **The chain is target → ancestors → tree, and the tree is always last.** A link hears it, then the tree. A slot hears it, then its owning node, then the tree. A node, then the tree. Empty canvas (`Target == null`) is the tree alone. The tree is the one the adapter passed to `For`, never inferred from `Parent`.

⚙ **Handlers run before the framework's own reaction.** `PreventDefault` suppresses that reaction; `StopPropagation` cuts the rest of the chain. Setting neither changes nothing.

⚙ **Hover is diffed for you.** When the target changes, the route raises `PointerExited` on the one left and `PointerEntered` on the one arrived at — each as its own action with its own handle — before the event itself. A component subscribes its own helper alone and hears its whole hover.

⚙ **The route's only default action is remembering the pointer target.** `PointerTarget` is updated after the route unless `IsSuspended`; nothing is deleted, highlighted or opened. **Keys bubble without a default action** — `Route(KeyEventArgs)` only bubbles. A host that deletes on `Delete` subscribes `KeyDown` and runs `link.DeleteCommand` itself.

## The handle

`WorkflowEventHandle` carries two independent flags. The same pair lands on three kinds of action:

| Action | `PreventDefault` | `StopPropagation` |
|---|---|---|
| Routed input (`Pointer*` / `Key*`) | the framework's own reaction does not run — for a wheel, the surface does not scroll this notch | no ancestor helper hears it |
| A model action about to happen (`…ing`: `Moving`, `Resizing`, `Deleting`, `ChannelChanging`, `Connecting`) | the action is refused; it does not happen | — |
| The report that it happened (`…ed`) | no effect — there is nothing left to prevent | later subscribers do not hear this one report |

⚙ **On routed input the adapter owns the reaction.** `PreventDefault` on a wheel means "the surface does not scroll"; the platform's own handling of a press or a key is separate.

## The argument

Every `PointerEventArgs` carries:

| Member | Meaning |
|---|---|
| `Position` | the pointer, in the surface's coordinates; its `Layer` is the source view's layer, not the pointer's |
| `Modifiers` | `InputModifiers`: `Alt`, `Control`, `Shift`, `Meta`, combinable |
| `Source` | the view that caught the input — the per-link control, or the surface when one view draws everything |
| `Target` | the component the adapter resolved the pointer onto, or `null` on empty canvas |
| `Handle` | this action's one handle, shared with the whole route |

| Args type | Adds |
|---|---|
| `PointerEnteredEventArgs` / `PointerExitedEventArgs` / `PointerMovedEventArgs` | nothing |
| `PointerPressedEventArgs` / `PointerReleasedEventArgs` | `Button` (`MouseButton`), `ClickCount` |
| `PointerWheelEventArgs` | `DeltaX`, `DeltaY` — wheel deltas, not scroll offsets; only the sign is contractual (`DeltaY` positive away from the user, `DeltaX` positive to the right) |
| `KeyDownEventArgs` / `KeyUpEventArgs` (base `KeyEventArgs`) | `Key` (`InputKey`, `Unknown` when unmapped), `RawKeyCode` (platform-specific), `IsRepeat` (best effort), `Source` (focused view), `Target` (what the key applies to) |

⚙ **A key has no position; `Target` is whatever the adapter decided it applies to.** For `Delete` that is the component the pointer is on — which is why `PointerTarget` is updated on every pointer action, not only on `PointerEntered` / `PointerMoved`.

⚙ **A down and an up are two actions, not two phases.** Each gets its own argument and its own handle.

## `WorkflowInput`'s state

| Member | What it is |
|---|---|
| `PointerTarget` / `HoveredLink` | the component / link the pointer is on; `null` on empty canvas |
| `ConnectionSender` | the port a connection drag started from; `null` when no drag is in progress |
| `PreviewReceiver` | the candidate port under the pointer; `null` when none |
| `PreviewState` | `None` / `Started` / `ValidTarget` / `InvalidTarget` |
| `PreviewCheck` | `ConnectionCheck` for the candidate — `IsValid`, `Reason`, `Message`; an invalid candidate still carries one |
| `PreviewChanged` | a snapshot whenever the sender, receiver or state changes |
| `PreviewValidating` | once per new framework-valid candidate, before it is marked valid; a subscriber may refuse it for this preview only |
| `Scroller` | the surface's `IWorkflowSurfaceScroller`, or `null` until an adapter registers one |
| `Navigator` | the surface's `IWorkflowSurfaceNavigator`, or `null` until an adapter registers one; `Locate()` asks it to center a world point |
| `IsSuspended` | while set, pointer movement stops updating `PointerTarget` — the route still delivers events |
| `HitRadius` | the reach passed to link hit-testing; `LinkHitTestEx.DefaultHitRadius` (6) by default |

⚙ **`IsSuspended` serves popups.** Set it while a popup is open; events still route, and only the hover bookkeeping pauses, so moving onto the popup does not clear the link the popup is about.

⚙ **The preview tightens the framework's verdict.** `PreviewValidating` fires once per candidate — call `StandardRefreshConnectionPreview` after an async check to ask again. The release-time `IWorkflowTreeEvents.Connecting` and `ValidateConnection` remain the verdict.

## What each side owns

| Side | Owns |
|---|---|
| Adapter | translating platform input; resolving `Target`; applying the default scroll; pan, drag, connect and zoom gestures; registering `Scroller` and `Navigator` |
| Core | routing and bubbling; the hover diff; `PointerTarget`; connection-preview state |
| Host / view | subscribing `IInputEvents.Input`; deciding Delete, highlight and menu actions |

## Examples

Hear a link's own hover:

```csharp
if (link.GetHelper() is IInputEvents events)
{
    events.Input.PointerEntered += (_, _) => { _lit = true;  InvalidateVisual(); };
    events.Input.PointerExited  += (_, _) => { _lit = false; InvalidateVisual(); };
}
```

Take a plain wheel over and scroll sideways instead:

```csharp
var input = WorkflowInput.For(tree);
var relay = ((IInputEvents)tree.GetHelper()).Input;
relay.PointerWheelChanged += (_, e) =>
{
    if ((e.Modifiers & InputModifiers.Shift) == 0) return;
    e.Handle.PreventDefault = true;           // the surface does not scroll this notch
    input.Scroller?.ScrollBy(-e.DeltaY, 0d);  // …scroll horizontally instead
};
```

⚙ **The two wheel axes do not share a positive direction.** `DeltaY` positive means away from the user; `ScrollBy`'s X positive means towards the content on the right. Feeding a vertical notch into the horizontal axis takes a negation — what the demos write as `ScrollBy(-e.DeltaY, 0d)`. A real tilt wheel (`DeltaX`) needs no negation.

Refuse a gesture before it starts — here, a Shift-press on empty canvas:

```csharp
var relay = ((IInputEvents)tree.GetHelper()).Input;
relay.PointerPressed += (_, e) =>
{
    if (e.Target is null && (e.Modifiers & InputModifiers.Shift) != 0)
        e.Handle.PreventDefault = true;   // no pan, node drag, connection or zoom starts
};
```

Refuse a connection candidate for this preview only:

```csharp
WorkflowInput.For(tree).PreviewValidating += (_, e) =>
{
    if (RejectForNow(e.Receiver)) e.Reject("busy");
};
```

⚙ **The refusal is of the press, not of a named gesture.** Core does not tell you which gesture would have started; whatever reads the handle stands down.

### Undo/redo on a modifier chord

The tree is the last element of every route, so a subscription on its helper hears every key — whether the pointer is over a node, a link, or empty canvas.

```csharp
private IWorkflowTreeViewModel? _tree;
private IInputEvents? _input;

// Call whenever the surface is handed a tree: DataContextChanged on WPF/Avalonia,
// BindingContextChanged on MAUI, OnParametersSet on Blazor, SetTree on WinForms/Jalium.
private void BindTree(IWorkflowTreeViewModel? tree)
{
    if (_input is not null) _input.Input.KeyDown -= OnKeyDown;

    _tree = null;
    _input = null;
    if (tree is null) return;
    if (tree.GetHelper() is not IInputEvents input) return;

    _tree = tree;
    _input = input;
    input.Input.KeyDown += OnKeyDown;
}

private void OnKeyDown(object? sender, KeyDownEventArgs e)
{
    if (_tree is not { } tree) return;
    if (!e.Modifiers.HasFlag(InputModifiers.Control)) return;

    var shift = e.Modifiers.HasFlag(InputModifiers.Shift);
    if (e.Key == InputKey.Z && !shift) tree.UndoCommand.Execute(null);
    else if (e.Key == InputKey.Y || (e.Key == InputKey.Z && shift)) tree.RedoCommand.Execute(null);
    else return;

    e.Handle.PreventDefault = true;
}
```

⚙ **Subscribe on the tree helper for a canvas-wide chord.** The route always ends at the tree, so one subscription hears the chord wherever the pointer is. A component's helper hears a key only while that component is the target — right for `Delete` on a link, wrong for a canvas-wide chord.

⚙ **The modifiers arrive with the key.** `e.Modifiers` is filled by the adapter from the platform's keyboard state, not tracked with `KeyDown` / `KeyUp` — a modifier pressed before the surface had focus is still correct. The key event itself still has to reach the adapter: WPF's surface takes focus on press (outside a `TextBoxBase`) for exactly that reason.

⚙ **`Ctrl+Shift+Z` and `Ctrl+Y` are the same action.** Test `InputKey.Z` with the Shift modifier and `InputKey.Y` without; both run `RedoCommand`.

⚙ **Set `PreventDefault` on the branch that acted.** It suppresses the framework's own reaction for that key; setting it for every Ctrl chord also swallows the chords you did not handle.

⚙ **Held keys auto-repeat.** `IsRepeat` is best effort; return on `e.IsRepeat` when one press should be one undo.

⚙ **Text editors inside a node keep their own Ctrl+Z.** The adapter routes what reaches the surface; WPF's surface does not steal focus from a `TextBoxBase` on press, and a key the editor already handled never reaches the surface handler. For a locally owned chord, set `StopPropagation` in a subscriber nearer the target.

⚙ **Dragging is runtime state.** `Move` / `SetAnchor` / `SetSize` are high-frequency geometry and do not push. `Ctrl+Z` undoes the last structural change (create, connect, delete, `SetSelector`); to make a drag undoable, submit your own `WorkflowActionPair` through `tree.GetHelper().Submit`.

⚙ **Unwire when the surface is handed another tree.** The subscription belongs to the tree instance, not to the view.

## Reference

⚙ `Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Events/WorkflowInput.cs` — the route, the state and the preview events.

⚙ `Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Events/Input/` — the argument family: `IInputEvents`, `InputRelay`, the pointer event args, `KeyEventArgs` / `KeyDownEventArgs` / `KeyUpEventArgs`, `InputKey`, `InputModifiers`, `MouseButton`.

⚙ `Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Events/WorkflowEventHandle.cs` — the two flags and the three landings.

When a click near a link misses or a wheel scrolls the wrong surface, read [references/pitfalls/view-and-input.md](../pitfalls/view-and-input.md).
