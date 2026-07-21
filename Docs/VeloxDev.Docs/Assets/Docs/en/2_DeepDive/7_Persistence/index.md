# Persistence Architecture

Workflow state serialization using `Newtonsoft.Json` via extension methods in `VeloxDev.Core.Extension`. The serializer understands the full component graph — Tree, Nodes, Slots, Links, and their `[VeloxProperty]` values.

---

## Serialization Graph

```mermaid
flowchart TB
    subgraph JSON[Serialized JSON]\n
        Tree[Tree\nLayout, VirtualLink]
        Nodes["Nodes[]\nAnchor, Size"]
        Links["Links[]\nSender, Receiver"]
        Maps["LinksMap\nSlot→Slot→Link"]
    end

    subgraph Memory[Runtime Objects]
        T[TreeDefaultViewModel]\n
        N[NodeDefaultViewModel]\nSlots[], Parent\n
        L[LinkDefaultViewModel]\nSender, Receiver\n
        S[SlotDefaultViewModel]\nTargets, Sources\n
    end

    T --> Tree\n
    N --> Nodes\n
    L --> Links\n
    S -.->|via Parent| N
    Tree --> Links --> L
    Nodes --> N
    Tree --> Nodes
```

## Extension Method API Surface

```mermaid
flowchart LR
    subgraph Sync\n
        T[tree.Serialize&#40;&#41;] --> Str[string JSON]\n
        S2[tree.Serialize&#40;options&#41;] --> Str2[string with options]\n
        Str3["json.Deserialize&lt;T&gt;&#40;&#41;"] --> Obj[T]\n
        B["json.TryDeserialize&lt;T&gt;&#40;out var&#41;"] --> Bool[bool]\n
    end

    subgraph Async\n
        T2["await tree.SerializeAsync&#40;&#41;"] --> StrA[string]\n
        T3["await json.DeserializeAsync&lt;T&gt;&#40;&#41;"] --> ObjA[T]\n
    end

    subgraph Advanced\n
        S3["tree.SerializeToUtf8Bytes&#40;&#41;"] --> Bytes[byte&#91;&#93;]\n
        S4["stream.SerializeToStreamAsync&#40;&#41;"] --> Stream[Stream]\n
        S5["reader.DeserializeFromTextReaderAsync&#40;&#41;"] --> ObjR[T]\n
    end
```

## SerializationOptions Fluent API

```csharp
var json = tree.Serialize(
    SerializationOptions.Create()
        .WithIndented()                    // Human-readable
        .WithTypeNameHandling(TypeNameHandling.Auto)  // Polymorphic types
        .WithNullValueHandling(NullValueHandling.Ignore)
        .WithDefaultValueHandling(DefaultValueHandling.Ignore)
);
```

## What Gets Serialized

| Component | Properties |
|-----------|-----------|
| **Tree** | Layout (canvas size, viewport offset), VirtualLink |
| **Node** | Anchor (x, y, layer), Size, all `[VeloxProperty]` fields |
| **Slot** | Anchor, Channel, State, Targets (as slot IDs), Sources (as slot IDs) |
| **Link** | Sender (slot ID), Receiver (slot ID), IsVisible |
| **Custom data** | All `[VeloxProperty]` values, including business-specific data |

## Cross-Platform

The same serialized JSON can be loaded on **Desktop**, **Browser**, or **Mobile** — enabling cloud-backed workflow persistence. The serialization is agnostic of the GUI framework; only runtime object structure matters.
