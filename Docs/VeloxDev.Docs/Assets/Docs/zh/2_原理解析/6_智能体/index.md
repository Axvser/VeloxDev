# 智能体架构

基于 **MAF（Model-Aware Function）框架**，将工作流组件元数据转换为 LLM 可理解的工具定义，实现自然语言驱动的图操作。

---

## 工具调用流程

```mermaid
sequenceDiagram
    participant User as 用户
    participant Agent as AIAgent
    participant LLM as ChatClient (LLM)
    participant Scope as WorkflowAgentScope
    participant Tree as WorkflowTree

    User->>Agent: "添加一个节点并连接到控制器"
    Agent->>LLM: instructions + 工具定义
    LLM-->>Agent: 工具调用: CreateNode(type=MyNode)
    Agent->>Scope: 执行工具
    Scope->>Tree: tree.Nodes.Add(newNode)
    Tree-->>Scope: 成功
    Scope-->>Agent: 结果: 节点已创建
    Agent->>LLM: 工具结果
    LLM-->>Agent: 工具调用: ConnectSlots(ctrl.Slots[0], newNode.Slots[0])
    Agent->>Scope: 执行工具
    Scope->>Tree: new LinkViewModel { Sender, Receiver }
    Tree-->>Scope: 成功
    Scope-->>Agent: 结果: 已连接
    Agent-->>User: 响应: 完成！已创建节点 X 并连接
```

## MAF 工具生成流水线

```mermaid
flowchart LR
    subgraph 代码库
        A[[VeloxProperty]]
        B[[VeloxCommand]]
        C[[AgentContext]]
        D[[AgentCommandParameter]]
        E[[SlotSelectors]]
    end
    subgraph Scope[WorkflowAgentScope]
        F[反射扫描类型]
        G[构建工具 Schema]
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

## 安全等级

```mermaid
flowchart TB
    L0[Level 0: 自动
    允许所有变更]
    L1[Level 1: 谨慎
    破坏性操作需确认]
    L2[Level 2: 确认
    所有变更需确认]
    L3[Level 3: 严格
    默认只读
    每次变更需显式批准]

    L0 --> L1 --> L2 --> L3
```

## 上下文感知

| 特性 | 机制 |
|------|------|
| **渐进式上下文** | `ProvideProgressiveContextPrompt()` 生成当前图拓扑的文本描述 |
| **自动发现** | `WithAutoDiscovery(assembly)` 扫描 `[AgentContext]`、`[AgentCommandParameter]`、`[SlotSelectors]` |
| **自定义注册** | `WithComponents(types)`、`WithData(types)`、`WithEnums(types)` 用于领域特定类型 |
| **MCP 支持** | `McpServerConfiguration` 用于连接外部数据源 |
