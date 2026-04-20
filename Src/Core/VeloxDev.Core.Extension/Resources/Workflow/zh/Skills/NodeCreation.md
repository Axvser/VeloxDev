## Skill: 节点创建协议

当用户要求创建节点时，按以下步骤**依序**执行：

1. **选择最合适的节点类型**：从上方已预加载的客户组件类型中选取。**禁止调用 `ListCreatableTypes`**——所有可用类型已在上方列出。若只有一种类型，直接使用；若有多种，选择最符合用户意图的，或请用户澄清。
2. **读取该类型的预加载 `[AgentContext]` 描述**（已在上方上下文中）。提取默认尺寸及其他默认值。**禁止调用 `GetComponentContext`**——描述已在上下文中。
3. **应用 `[AgentContext]` 中的默认值**：若描述中写有"默认大小为 200×100"，则在 CreateNode 中使用 `width=200, height=100`。若用户明确指定了不同的值，以用户的值为准。
4. **选择无重叠的位置**：调用 CreateNode 前，检查现有节点位置（通过 ListNodes 或已缓存的知识），选取与已有节点不重叠的位置（至少 30px 间距）。工具会自动偏移重叠，但主动选择良好位置可获得更优布局。
5. **调用 CreateNode 或 CreateAndConfigureNode**，传入解析出的类型、位置和尺寸。检查响应——若 `repositioned=true`，说明节点已被自动移动以避免重叠，以响应中实际的 `x`/`y` 为准。
6. **通过 PatchNodeProperties 设置 `[AgentContext]` 描述的属性**（若用户需求与默认值不同）。

### 📌 默认值解析（关键）

当用户提及"默认"值（如"默认大小"、"重置为默认"）时，按以下**严格优先级顺序**解析：

1. **`[AgentContext]` 开发者说明**（上方预加载或来自 GetComponentContext）。例如"默认大小为 200×100"意味着 width=200，height=100。**始终权威**。
2. **GetComponentContext 完整属性表**——若预加载描述未涵盖具体属性，调用 GetComponentContext 获取含各属性默认值的完整表格。
3. **禁止**将其他节点的运行时值、GetTypeSchema 的 `defaultJson` 或猜测作为"默认值"使用——这些是运行时状态，不是预期默认值。

> ⚠️ **GetTypeSchema 的 `defaultJson` 显示的是运行时零初始化值（如 Size={0,0}）**，不是预期默认值。`[AgentContext]` 描述中的开发者说明始终优先于 `defaultJson`。

**关键原则**：`[AgentContext]` 默认值是基准。用户指令可覆盖它们。永远不要忽略已文档化的默认值，也不要向用户询问 `[AgentContext]` 已提供的信息。
