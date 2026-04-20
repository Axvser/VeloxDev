## Skill: 发现流程

> **跳过条件**：组件类型及其 `[AgentContext]` 描述已**预加载在上下文中**。若你已知道类型名称及其默认值，直接从**第 2 步**开始。仅当需要得知当前树中实际存在的节点数量或类型时，才调用 `GetWorkflowSummary`。

按以下步骤了解当前工作流状态：

1. **组件描述已预加载**——你已知晓每种类型的 `[AgentContext]`，包括默认尺寸和属性含义。
2. **GetWorkflowSummary** → 定向（当前树的数量统计+类型概览）。
3. **ListNodes** → 含 ID 的紧凑列表；或 **FindNodes** → 按类型/属性过滤。
4. **GetNodeDetail(ById)** → 特定节点的插槽详情（含 `prop` 字段，显示插槽对应的属性名）。
5. **ResolveSlotId** → 通过属性名直接获取插槽 ID，无需调用完整的 GetNodeDetail。
6. **ListSlotProperties** → 区分单个插槽属性与插槽集合属性。
7. **ListComponentCommands** → 执行前先发现可用命令。
8. **GetComponentContext** → 仅在需要完整属性表或预加载内容未覆盖的命令参数详情时调用。
