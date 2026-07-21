# Progressive Context

`WorkflowAgentScope` provides progressive context prompts to help the LLM understand the current workflow topology. Context is collected by `AgentContextCollector` across multiple dimensions.

---

## Context Composition

### 1. Graph Topology State

`ProvideProgressiveContextPrompt()` produces a text description containing:
- **Node list**: type, `[VeloxProperty]` values, Anchor position, Slot count
- **Connection map**: Sender → Receiver for each Link
- **Available tools**: current AI tool list

### 2. Auto-Discovery

```csharp
scope.WithAutoDiscovery("VeloxDev.Core");
```

The `AgentContextCollector` scans for types annotated with:
- `[AgentContext]` — description text
- `[AgentCommandParameter]` — command parameter type
- `[SlotSelectors]` — conditional slot enums
- `[WorkflowBuilder.Tree/Node/Slot/Link]` — workflow components

### 3. Complete AI Annotation Reference

All AI attributes are defined in `VeloxDev.Core.AI` — no Extension dependency.

| Attribute | Target | AllowMultiple | Description |
|-----------|--------|:---:|-------------|
| `[AgentContext(AgentLanguages, string)]` | Class/property/method/field/etc (`AttributeTargets.All`) | ✅ | Multi-language descriptions for the LLM |
| `[AgentCommandParameter]` | Property/method/field | ❌ | Declares command takes no parameter |
| `[AgentCommandParameter(typeof(T))]` | Property/method/field | ❌ | Declares command parameter type T |
| `[SlotSelectors(typeof(Enum)[])]` | SlotEnumerator property | ❌ | Declares allowed enum selector types |

**`[AgentContext]` supported languages** (33 languages via `AgentLanguages`):

`English`, `ChineseSimplified` / `Chinese`, `ChineseTraditional`, `Japanese`, `Korean`, `French`, `German`, `Spanish`, `Portuguese`, `Russian`, `Arabic`, `Hindi`, `Bengali`, `Urdu`, `Indonesian`, `Malay`, `Vietnamese`, `Thai`, `Turkish`, `Italian`, `Dutch`, `Polish`, `Czech`, `Swedish`, `Danish`, `Norwegian`, `Finnish`, `Greek`, `Hebrew`, `Romanian`, `Hungarian`, `Ukrainian`, `Persian`

### 4. Built-in Framework Types

| Category | Types |
|----------|-------|
| **Components** | `TreeDefaultViewModel`, `NodeDefaultViewModel`, `SlotDefaultViewModel`, `LinkDefaultViewModel` |
| **Data** | `Anchor`, `Offset`, `Size` |
| **Enums** | `SlotChannel`, `SlotState` |
| **Interfaces** | `IWorkflowTreeViewModel`, `IWorkflowNodeViewModel`, `IWorkflowSlotViewModel`, `IWorkflowLinkViewModel` |

### 5. Custom Type Registration

```csharp
scope
	.WithComponents(typeof(MyNode))
	.WithInterfaces(typeof(IMyService))
	.WithData(typeof(MyDto))
	.WithEnums(typeof(MyEnum));
```

### 6. Embedded Resources

`AgentEmbeddedResources` provides built-in prompt templates automatically referenced by `ProvideProgressiveContextPrompt()`.

### 7. Sample Context Output

```
Current workflow topology:
- Node A (Controller, Pos: 100,200)
  - Output Slot: Slot[0] (connected -> Node B)
- Node B (Processor, Pos: 400,200)
  - Input Slot: Slot[0] (from Node A)
  - Properties: delay = 1200ms, title = "processing"

Available tools:
- CreateAndConfigureNode
- ConnectSlots
- PatchNodeProperties
- ...
```
