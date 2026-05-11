## 💡 Token 节省技巧

> **决策规则**：在进行任何工具调用之前，先问自己：*“我是否即将进行 2 次或更多次釁目标相同节点或同一组节点的顺序调用？”*。若是，将下表先查阅——使用组合工具或 BatchExecute 几乎总是更优的选择。

> **状态复用规则**：允许为了节省 token 而复用缓存拓扑，而不是每次操作前都重新读取实时状态。但只要发生结构变更，缓存就应视为失效。

**优先使用组合工具，而非多步序列：**

| 组合工具 | 等效替代 | 节省 |
|---|---|---|
| **CreateAndConfigureNode** | CreateNode + PatchNodeProperties + SetEnumSlotCollection | 3 次 → 1 次；传入 `enumSlotProperty`（如 "OutputSlots"）+ `enumTypeName` 配置 SlotEnumerator；返回完整节点详情含插槽 ID |
| **ConnectByProperty** | ResolveSlotId×2 + ConnectSlots | 3 次 → 1 次；无需预先解析插槽 ID |
| **DeleteNodes** | DeleteNode×N | N 次 → 1 次批量删除 |
| **ArrangeNodes** | SetNodePosition×N | N 次 → 1 次批量定位 |
| **GetFullTopology** | ListNodes + GetNodeDetail×N + ListConnections | N+2 次 → 1 次；适合复杂多节点操作 |

- **BatchExecute**：上述组合工具未覆盖的其他操作组合，使用 BatchExecute。
- **TakeSnapshot** 只返回版本号+统计数量；用 **GetChangesSinceSnapshot** 获取差异，而非重新读取全部内容。
- **FindNodes**：按类型名或属性值过滤节点，避免读取全部再手动过滤。
- **SearchForward / SearchReverse / SearchAllRelative**：通过图遍历发现相连节点，避免手动逐连接查找。
- **IsConnected**：检查可达性，无需列出所有路径。
- **FindPath**：查找两节点间的最短路径。
- **DisconnectAllFromNode**：一次调用清除所有连接，无需逐插槽操作。
- **AlignNodes / DistributeNodes / AutoLayout**：布局操作一次完成，而非 N 次 SetNodePosition。AutoLayout 使用拓扑感知分层排列，沿传播链布局。
- **ExecuteWorkOnNodes**：一次触发多个节点的工作。
- **BulkPatchNodes**：同一属性变更应用到多个节点。
- **ValidateWorkflow**：在询问用户前先检查是否存在问题。
- **ListCreatableTypes**：发现可用的节点/插槽类型。
- **ResolveSlotId**：通过属性名直接获取插槽运行时 ID，避免为解析 ID 而调用 GetNodeDetail。
- 多步操作中优先使用 **RuntimeId**（而非索引），在增删操作后依然稳定。
- 已有插槽 ID 时使用 **ConnectSlotsById**；知道属性名时使用 **ConnectByProperty**。

### 缓存失效检查表

如果刚调用过以下任一操作，在下一次依赖拓扑的步骤前应先刷新：

- `CreateNode`、`DeleteNode`、`DeleteSlot`、`CloneNodes`
- `CreateSlotOnNode`、`AddSlotToCollection`、`RemoveSlotFromCollection`
- `SetEnumSlotCollection`

这些操作之后：

- 旧 **node index** 可能已指向别的节点
- 旧 **slot index** 可能已指向别的插槽
- 旧 **枚举 slot runtime ID** 可能已经不存在

若未发生上述失效操作，则允许按速度优先策略复用缓存 ID 与拓扑。
