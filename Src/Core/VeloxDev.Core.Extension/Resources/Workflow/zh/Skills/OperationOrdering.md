---
name: operation-ordering
description: 遵守变更生命周期 CreateNode、PatchNodeProperties/SetEnumSlotCollection、CreateSlotOnNode/AddSlotToCollection、ConnectSlots/ConnectByProperty、ExecuteNode/BroadcastNode——倒序为何破坏插槽；未挂载组件上命令路径静默无操作，而补丁器会拒绝
---

## Skill: 操作顺序协议（关键）

必须遵守以下生命周期顺序——与人类开发者的操作顺序一致。违反此顺序会导致数据静默丢失、插槽未注册或连接断裂。

### 强制执行序列

```
1. CreateNode          — 节点必须先存在于树中，才能进行后续任何操作
2. PatchNodeProperties  — 配置标量属性（Title、DelayMs 等）
   SetEnumSlotCollection — 为 SlotEnumerator 属性设置选择器类型
3. CreateSlotOnNode /   — 创建或配置插槽（必须在节点已加入树之后）
   AddSlotToCollection
4. ConnectSlots /       — 连接插槽（两端端点必须都已存在）
   ConnectByProperty
5. ExecuteNode /        — 经接收路径执行节点逻辑（仅在拓扑完整后）
   BroadcastNode
```

**强制序列**：没有"一步到位"的复合工具——每一步都必须作为独立命令逐步执行，就像人类开发者按上述顺序在 GUI 中逐步操作一样。

### 顺序重要性说明

| 错误顺序 | 导致的问题 |
|---|---|
| PatchNodeProperties 在 CreateNode 之前 | 补丁被拒绝：目标没有父链，未挂载到树中 |
| ConnectSlots 在插槽存在之前 | 插槽 ID 查找失败，或连接到错误插槽 |
| SetEnumSlotCollection 在 CreateNode 之前 | 选择器状态以脱离树的方式应用——不注册到树，也不产生撤销记录 |
| ExecuteNode 在连接建立之前 | 接收无下游效果 |

### ⚠ 静默跳过警告

在 release 构建中，下列命令路径的扩展方法会**静默返回，不执行任何操作，也不报告任何错误**——工具仍会返回 `status: "ok"`，但什么也没发生。`PatchNodeProperties` 是例外：它会对未挂载目标返回 `status: "error"` 拒绝写入。

| 违规操作 | 实际后果 |
|---|---|
| 在**未添加到 Tree** 的节点上操作（`Parent == null`） | `DeleteNode` / `SetSlotChannel` → **静默无操作**。`CreateSlotOnNode` → 添加插槽但**无撤销注册** |
| 在 `Parent == null` 的插槽上操作 | `DeleteSlot` → **静默无操作** |
| 在 `Sender?.Parent?.Parent == null` 的连接上操作 | `DeleteCommand` → **静默无操作** |

**始终**通过 `ListNodes` / `CreateNode` 获取有效的 `nodeIndex` 或 `runtimeId` 后再操作节点内部。除非你刚刚创建或查询了节点，否则不要假设节点存在。
