## Skill: 操作顺序协议（关键）

必须遵守以下生命周期顺序——与人类开发者的操作顺序一致。违反此顺序会导致数据静默丢失、插槽未注册或连接断裂。

### 强制执行序列

```
1. CreateNode          — 节点必须先存在于树中，才能进行后续任何操作
   （或 CreateAndConfigureNode — 将步骤 1-2 合并为一次调用）
2. PatchNodeProperties  — 配置标量属性（Title、DelayMs 等）
   SetEnumSlotCollection — 为 SlotEnumerator 属性设置选择器类型
3. CreateSlotOnNode /   — 创建或配置插槽（必须在节点已加入树之后）
   AddSlotToCollection
4. ConnectSlots /       — 连接插槽（两端端点必须都已存在）
   ConnectByProperty
5. ExecuteWork /        — 执行工作流逻辑（仅在拓扑完整后）
   BroadcastNode
```

### 顺序重要性说明

| 错误顺序 | 导致的问题 |
|---|---|
| PatchNodeProperties 在 CreateNode 之前 | 节点无 Parent；插槽生命周期钩子不触发（节点不在树中） |
| ConnectSlots 在插槽存在之前 | 插槽 ID 查找失败，或连接到错误插槽 |
| SetEnumSlotCollection 在 CreateNode 之前 | OutputSlots 被创建但 OnWorkflowSlotAdded 无法在树中注册它们 |
| ExecuteWork 在连接建立之前 | 工作产生无下游效果 |

### BatchExecute 顺序规则

**BatchExecute** 调用中的操作按**数组顺序依次执行**。  
必须按正确的生命周期顺序排列：CreateNode → Patch → Slot → Connect → Execute。

### 常用 BatchExecute 模式

**模式 A — 一次调用中创建、配置并连接两个节点：**
```json
BatchExecute([
  { "tool": "CreateNode",          "type": "MyNodeType", "x": 100, "y": 100, "width": 200, "height": 100 },
  { "tool": "CreateNode",          "type": "MyNodeType", "x": 400, "y": 100, "width": 200, "height": 100 },
  { "tool": "PatchNodeProperties", "nodeId": "$0.id", "properties": { "Title": "源节点" } },
  { "tool": "PatchNodeProperties", "nodeId": "$1.id", "properties": { "Title": "目标节点" } },
  { "tool": "ConnectByProperty",   "sourceNodeId": "$0.id", "sourceProperty": "OutputSlot",
                                   "targetNodeId": "$1.id", "targetProperty": "InputSlot" }
])
```

**模式 B — 批量重新配置已有节点（无需创建）：**
```json
BatchExecute([
  { "tool": "PatchNodeProperties", "nodeId": "id-A", "properties": { "DelayMs": 500 } },
  { "tool": "PatchNodeProperties", "nodeId": "id-B", "properties": { "DelayMs": 500 } },
  { "tool": "ConnectByProperty",   "sourceNodeId": "id-A", "sourceProperty": "OutputSlot",
                                   "targetNodeId": "id-B", "targetProperty": "InputSlot" }
])
```

> 尽可能使用 `CreateAndConfigureNode` 替代 `CreateNode + PatchNodeProperties + SetEnumSlotCollection`——它是 3 合 1 的组合工具，可进一步缩短 BatchExecute 数组长度。
