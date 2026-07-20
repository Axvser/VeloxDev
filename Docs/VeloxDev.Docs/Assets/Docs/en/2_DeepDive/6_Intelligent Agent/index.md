# Intelligent Agent Architecture

The agent system is built on the **MAF (Model-Aware Function) framework** — it translates workflow component metadata into LLM-understandable tool definitions, enabling natural-language-driven graph manipulation.

---

## Tool Call Flow

```mermaid
sequenceDiagram
    participant User
    participant Agent as AIAgent
    participant LLM as ChatClient (LLM)
    participant Scope as WorkflowAgentScope
    participant Tree as WorkflowTree

    User->>Agent: "Add a node and connect to controller"
    Agent->>LLM: instructions + tool definitions
    LLM-->>Agent: Tool call: CreateNode(type=MyNode)
    Agent->>Scope: Execute tool
    Scope->>Tree: tree.Nodes.Add(newNode)
    Tree-->>Scope: OK
    Scope-->>Agent: Result: node created
    Agent->>LLM: tool result
    LLM-->>Agent: Tool call: ConnectSlots(ctrl.Slots[0], newNode.Slots[0])
    Agent->>Scope: Execute tool
    Scope->>Tree: new LinkViewModel { Sender, Receiver }
    Tree-->>Scope: OK
    Scope-->>Agent: Result: connected
    Agent-->>User: Response: Done! Created node X and connected it.
```

## MAF Tool Generation Pipeline

```mermaid
flowchart LR
    subgraph Codebase
        A[[VeloxProperty]]
        B[[VeloxCommand]]
        C[[AgentContext]]
        D[[AgentCommandParameter]]
        E[[SlotSelectors]]
    end
    subgraph Scope[WorkflowAgentScope]
        F[Reflect over types]
        G[Build tool schemas]
    end
    subgraph LLM
        H[Function Calling]
    end

    A --> F
    B --> F
    C --> F
    D --> F
    E --> F
    F --> G --> H
```

## Safety Levels

```mermaid
flowchart TB
    L0[Level 0: Auto
    All mutations allowed]
    L1[Level 1: Caution
    Destructive ops need confirmation]
    L2[Level 2: Confirm
    All mutations need confirmation]
    L3[Level 3: Strict
    Read-only by default
    Each mutation requires explicit approval]

    L0 --> L1 --> L2 --> L3
```

## Context Awareness

| Feature | Mechanism |
|---------|-----------|
| **Progressive Context** | `ProvideProgressiveContextPrompt()` generates a text description of the current graph topology |
| **Auto-discovery** | `WithAutoDiscovery(assembly)` scans for `[AgentContext]`, `[AgentCommandParameter]`, `[SlotSelectors]` |
| **Custom registration** | `WithComponents(types)`, `WithData(types)`, `WithEnums(types)` for domain-specific types |
| **MCP support** | `McpServerConfiguration` for connecting to external data sources |
