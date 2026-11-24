## 💡 Token 节省技巧

> **读操作可以合并，变更操作不能。** 没有复合工具——每次变更都是一次一个命令（见 OperationOrdering）。节省来自**读侧**批量（如用 `GetFullTopology` 一次取整图）和**稳定句柄复用**（`RuntimeId`），从不来自把多次编辑打包。

优先读侧批量与稳定句柄，避免不必要的重复读取：

| 替代… | 使用… | 节省 |
|---|---|---|
| ResolveSlotId × 2 + ConnectSlots | **ConnectByProperty** | 2 次调用 |
| ListNodes + GetNodeDetail × N + ListConnections | **GetFullTopology** | N+1 次调用 |

> **大结果会被封顶，而不是整段送来。** `GetFullTopology` 有 `maxNodes`，`GetExecutionLog` 有 `maxEntries`，`ListNodes`/`FindNodes` 有 `maxNodes`/`maxMatches`；被截断的回复会带 `truncated` 与总数。任何工具结果超过宿主的字符上限时也会被替换成截断信封。回复说被截断时，收窄查询，不要原样重试。

**其他提效工具：**

- **TakeSnapshot** / **GetChangesSinceSnapshot** — 只取差异，避免重读全部。
- **FindNodes** — 按类型名或属性值过滤节点，避免读出全部再手动过滤。
- **SearchForward / SearchReverse / SearchAllRelative** — 通过图遍历找相连节点，避免手动逐连接查找。
- **IsConnected** — 一次调用检查可达性。
- **FindPath** — 查找两节点间的最短路径。
- **ExecuteNodes** — 一次触发多个节点的接收路径。
- **ValidateWorkflow** — 在询问用户前先检查是否存在问题。
- **ListCreatableTypes** — 发现可用的节点/插槽类型。
- **ResolveSlotId** — 通过属性名直接获取插槽 ID，无需完整的 GetNodeDetail。
- 多步操作中优先使用 **RuntimeId**（而非索引），在增删后依然稳定。
- 已有插槽 ID 时用 **ConnectSlotsById**；知道属性名时用 **ConnectByProperty**。

### 查询工具——按需选一个，别连环调用

| 需要… | 使用… |
|---|---|
| 快速概览（数量 + 去重类型） | **GetWorkflowSummary** |
| 紧凑节点列表 | **ListNodes** |
| 完整拓扑（节点 + 插槽 + 连接，一次调用） | **GetFullTopology** |
| 单个节点的完整详情 | **GetNodeDetail** / **GetNodeDetailById** |
| 仅连接列表 | **ListConnections**（GetFullTopology 已包含连接） |
| 类型结构 + 仅运行期值 | **GetTypeSchema** |
| 类型的开发者文档 | **GetComponentContext** |

若读缓存之后发生过结构变更，先刷新（清单见 Discovery Flow 技能）；否则可以复用缓存的 ID 与拓扑。
