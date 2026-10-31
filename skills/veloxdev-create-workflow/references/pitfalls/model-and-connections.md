# Pitfalls — model and connections

These are failures the repository has actually hit. Each entry names the symptom, the rule it violates, and the fix. Use this file when the graph is structurally wrong; the correct rules are in [model.md](../practices/model.md).

## A connection returns success but no link appears

**Symptom:** the model accepts the call; `Links` is unchanged or the link is absent from `LinksMap`.

**Cause:** the receiver was never attached to a tree, the sender/receiver directions disagree with the channel, or a `Connecting` subscriber vetoed the release. `StandardCheckConnection` can pass and the release-time veto still refuse; see [the connection preview](../practices/model.md#the-connection-preview).

**Fix:** confirm both slots have a parent node in a tree; check `sender.Channel` / `receiver.Channel`; subscribe `IWorkflowTreeEvents.Connecting` and log `Handle.PreventDefault`; check `tree.StandardCheckConnection(sender, receiver).Reason`.

## A same-direction connection silently replaces the previous link

**Symptom:** one link disappears and a new one appears; no error.

**Cause:** the channel is `One*`, and cleanup removes the conflicting link before creating the new one. This is the channel's contract, not a failure.

**Fix:** choose `Multiple*` when several connections must coexist. If the replacement was unexpected, check the channel default of the slot class the node declares.

## Undo does not restore a change

**Symptom:** the change is visible in the canvas, but `UndoCommand` skips it.

**Cause:** the mutation was a direct collection edit (`tree.Nodes.Add(...)`, `slot.Targets.Add(...)`) or a direct property write. Only `Submit` records history. Position, size, `SetAnchor`, `SetPointer`, `ResetVirtualLink` and `SetChannel` are runtime state and intentionally have no undo entry.

**Fix:** perform structural changes through the generated commands or `tree.GetHelper().Submit(new WorkflowActionPair(redo, undo))`. Undo a slot-channel change by writing the previous channel yourself.

## Ghost undo entries after assigning a slot

**Symptom:** assigning a new slot to a generated slot property produces an extra undo entry, or the old slot refuses to disappear cleanly.

**Cause:** the generated property's setter owns the slot lifecycle; a manually constructed slot was installed beside it.

**Fix:** change the channel with `slot.SetChannelCommand.Execute(channel)`. When a different slot type is needed, declare it as the property type and let `InitializeWorkflow()` create it.

## Editing a graph is a no-op in Release but throws in Debug

**Symptom:** an operation on a detached component does nothing in Release; a Debug build throws `InvalidOperationException` from `WorkflowGuard.Fail`.

**Cause:** `WorkflowGuard.Fail` is `[Conditional("DEBUG")]`; the whole call site is erased in Release. Most helpers treat a detached component as a silent no-op.

**Fix:** attach the component to a tree first. In a Release-only investigation, check the attachment before the operation; the guard will not tell you.

## A slot's `Equals` override breaks registration

**Symptom:** a slot is in `Slots` twice, or a connection does not find its endpoint.

**Cause:** `StandardCreateSlot` uses `ReferenceEquals`; the generated `InitializeWorkflowCore` uses `Slots.Contains`. A slot ViewModel that overrides `Equals` makes the two paths disagree.

**Fix:** keep component identity reference-based. Override `Equals` only on value objects that never participate in the component graph.

## Serialization throws `MissingWriter`

**Symptom:** `Serialize()` throws for a type that is in the object graph.

**Cause:** the world is closed. A type takes part when the generator emitted a writer for it: a workflow component, a `[VeloxProperty]` / `[VeloxCommand]` / `[Archivable]` type, or a type reachable along a member's *declared* type.

**Fix:** annotate the type with `[Archivable]`, or declare it from a type that is already in the document. `[Archivable(typeof(A), typeof(B))]` pulls in further types.

## A property is missing from the saved document

**Symptom:** a public property is not in the JSON.

**Cause:** it is get-only, `{ get; private set; }`, or a delegate. Only writable members are written; `[Archive(ArchiveOptions.KeepProperty)]` writes a computed property but it is never read back.

**Fix:** give the member a writable setter, or use `[Archive(ArchiveOptions.KeepField)]` for a field that no property corresponds to.

## An enum comes back as a number

**Symptom:** a serialized enum member round-trips as its underlying integer, and a dynamically routed branch no longer matches.

**Cause:** numbers are the default for enum members. A compiled graph's dynamic branch key resolves to a real enum at runtime and will not equal a restored `long`.

**Fix:** mark the member `[Archive(ArchiveOptions.EnumName)]` when the name is part of the contract. For a compiled graph, use `CompiledGraphEx`, which carries a key-type side channel for enum keys.

## A selector-driven node has no ports after creation

**Symptom:** `ListSlotProperties` reports `count: 0` on a `SlotEnumerator`, or a connection cannot find the port.

**Cause:** a compiler-created selector node starts with no ports; the slots exist only after a selector is set.

**Fix:** call `SetEnumSlotCollection` / `SetSelector` before connecting. For a non-enum provider, annotate the provider type with `[Archivable]`; for an enum selector, annotate the enum with `[AgentContext]`.

## `PatchNodeProperties` refuses a `[SlotSelectors]` property

**Symptom:** the Agent tool returns a refusal that names `SetEnumSlotCollection`.

**Cause:** selector properties are rebuilt as a set, not patched member by member.

**Fix:** use `SetEnumSlotCollection` (or `SetSelector` on the model). The refusal is the intended route.
