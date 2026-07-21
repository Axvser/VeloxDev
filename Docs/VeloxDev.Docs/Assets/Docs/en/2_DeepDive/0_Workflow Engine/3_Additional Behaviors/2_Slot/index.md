# WorkflowSlotConnectionBehavior

Slot view behavior — click to initiate or accept a connection.

---

## Attached Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IsEnabled` | `bool` | `false` | Enable connection interaction |

## Behavior

| Event | Action |
|-------|--------|
| `PreviewMouseLeftButtonDown` | Calls `SendConnectionCommand.Execute(null)` |
| `PreviewMouseLeftButtonUp` | Calls `ReceiveConnectionCommand.Execute(null)` |

## Complete Connection Flow

1. Press on output slot → `SendConnectionCommand` sets VirtualLink.Sender
2. Drag → `SetPointerCommand(Anchor)` updates ghost line (handled by SurfaceBehavior)
3. Pointer enters input slot → `ReceiveConnectionCommand` sets VirtualLink.Receiver
4. Release → `SubmitCommand(WorkflowActionPair)` creates and adds Link
5. ESC cancel → `ResetVirtualLinkCommand` clears ghost line

## SlotChannel Direction Constraints (Flags)

| Value | Flag Value | Meaning |
|-------|:----------:|---------|
| `None` | `0` | No connections allowed |
| `OneTarget` | `1` | At most 1 outgoing connection |
| `OneSource` | `2` | At most 1 incoming connection |
| `OneBoth` | `3` | At most 1 out + 1 in |
| `MultipleTargets` | `4` | Multiple outgoing connections |
| `MultipleSources` | `8` | Multiple incoming connections |
| `MultipleBoth` | `12` | Multiple out + multiple in (default) |
