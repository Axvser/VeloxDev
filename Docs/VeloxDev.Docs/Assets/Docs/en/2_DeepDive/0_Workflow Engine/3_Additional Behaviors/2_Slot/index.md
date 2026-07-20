# Slot Connection Flow

Slot connection is a multi-step process managed by the Tree:

1. **Pointer down** on an output slot → `SendConnectionCommand`
2. **Drag** → `SetPointerCommand(Anchor)` updates the virtual link endpoint
3. **Pointer enter** an input slot → `ReceiveConnectionCommand` highlights it
4. **Pointer up** on the input slot → `SubmitCommand(WorkflowActionPair)` creates the link
5. **Cancel** (ESC) → `ResetVirtualLinkCommand` clears the ghost line

The `SlotChannel` property controls directionality: `Input`, `Output`, `OneBoth`, or `None`.
