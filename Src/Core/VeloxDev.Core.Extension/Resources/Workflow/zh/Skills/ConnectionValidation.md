---
name: connection-validation
description: 在被拒绝的 ConnectSlots、ConnectByProperty 或 ConnectEnumSlot 上恢复而非重试——读 status rejected 的 reasons 数组区分通道不兼容（SetSlotChannel）、同节点端点与开发者 ValidateConnection 规则，并知道同向去重是替换连接而不是拒绝
---

## ⚠️ 连接验证

框架可能拒绝连接尝试。所有 Connect 工具都会验证并报告结果；收到 `status: "rejected"` 时，**禁止重试相同的连接**——拒绝是确定性的。

### 拒绝原因

1. **通道不兼容**：发送方插槽的通道不支持发送，或接收方插槽的通道不支持接收。检查 `GetNodeDetail` / `ListSlotProperties` 中的 `ch` 字段。
2. **同节点连接**：发送方和接收方属于同一节点——始终被拒绝。
3. **开发者 `ValidateConnection` 规则**：Tree 的 helper 中的自定义验证拒绝了它。把它当作不透明规则：告知用户，不要重试。
4. **兜底原因**——回复只写 `"developer ValidateConnection rule or channel capacity limit"` 时，就是上一条的开发者规则。每个方向只允许一条连接的通道会自动挤掉旧连接，容量不可能是原因。

### 不是拒绝：同向去重

当连接两个已有同向连接的节点时，框架会静默删除旧连接——结果是替换而非并行连接。对同一节点对，无需先调用 `DisconnectSlots` 再调用 `ConnectSlots`。

### 恢复

1. 读 `reasons` 数组。
2. **通道问题** → 检查 `ch`，若通道与预期角色不符，用 `SetSlotChannel` 更改。
3. **`ValidateConnection`** → 告知用户；不要重试。
4. `ConnectSlots` 成功后，其响应会给出 `senderProperty` / `receiverProperty`——下次改用 `ConnectByProperty`。

### 连接工具的刷新策略

- 若前几步**没有**改变拓扑，可复用缓存的 slot ID。
- 若改动了插槽集合、删除了组件、克隆了节点，或调用了 `SetEnumSlotCollection`，先刷新插槽身份。
- 在未改变前置条件（channel、端点、selector 类型、现有连接）之前，不要对同一 `Connect*` 做循环重试。
