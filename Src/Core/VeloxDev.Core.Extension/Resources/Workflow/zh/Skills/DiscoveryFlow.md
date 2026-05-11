## Skill: 发现流程

> **跳过条件**：组件类型及其 `[AgentContext]` 描述已**预加载在上下文中**。若你已知道类型名称及其默认值，可直接从最符合当前信息缺口的步骤开始。仅当需要当前树的实时节点/连接数量、需要确认当前实际存在的类型，或怀疑缓存拓扑已过期时，才调用 `GetWorkflowSummary`。

在变更前先选择状态刷新策略：

### 策略 A — 安全优先

- 在大多数变更前先刷新实时状态。
- 适合能力较弱的模型、较长工具链，或存在其他操作者可能同时修改树的场景。
- 常见顺序：
  1. **`GetWorkflowSummary`**
  2. **`ListNodes`** 或 **`FindNodes`**
  3. **`GetNodeDetail(ById)`** / **`ListSlotProperties`**
  4. 再执行变更

### 策略 B — 速度优先

- 激进复用缓存状态。
- 适合 Agent 确信没有其他人修改树，且下一步只依赖自己已掌握的数据。
- 仅在前一步操作使旧句柄失效，或工具响应提示拒绝 / 不匹配时再刷新。

### 推荐发现工具

1. **`GetWorkflowSummary`** → 当前树的数量统计与类型概览。
2. **`ListNodes`** → 含 ID 的紧凑列表；或 **`FindNodes`** → 按类型/属性过滤。
3. **`GetNodeDetail(ById)`** → 特定节点的插槽详情（含 `prop` 字段，显示插槽对应的属性名）。
4. **`ResolveSlotId`** → 通过属性名直接获取插槽 ID，无需调用完整的 `GetNodeDetail`。
5. **`ListSlotProperties`** → 区分单个插槽属性与插槽集合属性。
6. **`ListComponentCommands`** → 执行前先发现可用命令。
7. **`GetComponentContext`** → 仅在需要完整属性表或预加载内容未覆盖的命令参数详情时调用。

### 强制刷新点

即使采用速度优先策略，遇到以下操作后也必须刷新实时状态，因为旧索引或旧 slot ID 可能已失效：

- `CreateNode`、`DeleteNode`、`DeleteSlot`、`CloneNodes`
- `CreateSlotOnNode`、`AddSlotToCollection`、`RemoveSlotFromCollection`
- `SetEnumSlotCollection`（旧枚举插槽 ID 与旧连接都会过期）
- 当前工具链之外的用户操作或外部流程可能已修改树

> 你**不需要**每次都走完所有步骤。允许 Agent 自由权衡安全与速度，但在结构变更后绝不能继续复用陈旧的索引或陈旧的 slot ID。
