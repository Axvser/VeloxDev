## 💡 Token 节省技巧

> **决策规则**：不再存在复合工具——对同一节点或同一组节点的**变异操作**必须逐步、一次一个命令地调用。若你即将对同一目标进行多次顺序调用，请按上表先查阅是否有单次调用工具；**读操作**仍可复用缓存拓扑或使用 `GetFullTopology` 减少往返。

> **状态复用规则**：允许为了节省 token 而复用缓存拓扑，而不是每次操作前都重新读取实时状态。但只要发生结构变更，缓存就应视为失效。

**可用单次调用替代多步序列的工具（仅限仍存在的工具）：**

| 工具 | 等效替代 | 节省 |
|---|---|---|
| **ConnectByProperty** | ResolveSlotId×2 + ConnectSlots | 3 次 → 1 次；无需预先解析插槽 ID |
| **GetFullTopology** | ListNodes + GetNodeDetail×N + ListConnections | N+2 次 → 1 次；适合复杂多节点操作 |

- **TakeSnapshot** 只返回版本号+统计数量；用 **GetChangesSinceSnapshot** 获取差异，而非重新读取全部内容。
- **FindNodes**：按类型名或属性值过滤节点，避免读取全部再手动过滤。
- **SearchForward / SearchReverse / SearchAllRelative**：通过图遍历发现相连节点，避免手动逐连接查找。
- **IsConnected**：检查可达性，无需列出所有路径。
- **FindPath**：查找两节点间的最短路径。
- **ExecuteWorkOnNodes**：一次触发多个节点的工作。
- **ValidateWorkflow**：在询问用户前先检查是否存在问题。
- **ListCreatableTypes**：发现可用的节点/插槽类型。
- **ResolveSlotId**：通过属性名直接获取插槽运行时 ID，避免为解析 ID 而调用 GetNodeDetail。
- 多步操作中优先使用 **RuntimeId**（而非索引），在增删操作后依然稳定。
- 已有插槽 ID 时使用 **ConnectSlotsById**；知道属性名时使用 **ConnectByProperty**。

### 查询工具——按需选一个，别连环调用

| 需要… | 使用… |
|---|---|
| 快速概览（数量 + 去重类型） | **GetWorkflowSummary** |
| 紧凑节点列表 | **ListNodes** |
| 完整拓扑（节点 + 插槽 + 连接，一次调用） | **GetFullTopology** |
| 单个节点的完整详情 | **GetNodeDetail** / **GetNodeDetailById** |
| 仅连接列表 | **ListConnections**（GetFullTopology 已包含连接） |
| 类型结构 + 运行时默认值 | **GetTypeSchema** |
| 类型的开发者文档 | **GetComponentContext** |

### 缓存失效检查表

如果刚调用过以下任一操作，在下一次依赖拓扑的步骤前应先刷新：

- `CreateNode`、`DeleteNode`、`DeleteSlot`
- `CreateSlotOnNode`、`AddSlotToCollection`、`RemoveSlotFromCollection`
- `SetEnumSlotCollection`

这些操作之后：

- 旧 **node index** 可能已指向别的节点
- 旧 **slot index** 可能已指向别的插槽
- 旧 **枚举 slot runtime ID** 可能已经不存在

若未发生上述失效操作，则允许按速度优先策略复用缓存 ID 与拓扑。
