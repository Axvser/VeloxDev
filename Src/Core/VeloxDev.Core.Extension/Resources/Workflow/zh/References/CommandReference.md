## ⚠️ 命令优先原则

**所有对工作流的变更必须通过命令（IVeloxCommand）执行。** 禁止直接调用 Helper 方法或通过反射设置命令驱动的属性。命令负责 UI 线程调度、撤销/重做追踪与视图同步，绕过命令会导致静默失效。

| 操作 | 工具 | 对应命令 |
|---|---|---|
| 移动节点 | MoveNode | MoveCommand |
| 定位节点 | SetNodePosition | SetAnchorCommand |
| 调整节点大小 | ResizeNode | SetSizeCommand |
| 创建节点 | CreateNode | Tree.CreateNodeCommand |
| 创建插槽 | CreateSlotOnNode | Node.CreateSlotCommand |
| 连接 | ConnectSlots / ConnectSlotsById | Tree.Send/ReceiveConnectionCommand |
| 断开连接 | DisconnectSlots | Link.DeleteCommand |
| 删除节点 | DeleteNode | Node.DeleteCommand |
| 删除插槽 | DeleteSlot | Slot.DeleteCommand |
| 广播 | BroadcastNode | Node.BroadcastCommand |
| 其他操作 | ExecuteCommandOnNode / ExecuteCommandById | 按名称解析 |
| 修改自定义属性 | PatchNodeProperties / PatchComponentById | 直接设置（仅非命令属性） |
| 向集合添加插槽 | AddSlotToCollection | 集合生命周期（OnWorkflowSlotAdded） |
| 从集合移除插槽 | RemoveSlotFromCollection | 集合生命周期（OnWorkflowSlotRemoved） |
| 设置枚举插槽集合 | SetEnumSlotCollection | 清除并重建枚举驱动集合 |
| 按条件查找节点 | FindNodes | 仅只读查询 |
| 通过属性解析插槽 ID | ResolveSlotId | 仅只读查询 |
| **按属性名连接** | **ConnectByProperty** | Tree.Send/ReceiveConnectionCommand |
| **创建并配置节点** | **CreateAndConfigureNode** | CreateNode + Patch + SetEnum 合并为一次调用 |
| **批量删除节点** | **DeleteNodes** | Node.DeleteCommand × N |
| **批量定位节点** | **ArrangeNodes** | SetAnchorCommand × N |
| **完整图快照** | **GetFullTopology** | 一次调用获取所有节点+插槽+连接 |
| **反向广播** | **ReverseBroadcastNode** | Node.ReverseBroadcastCommand |
| **向下搜索** | **SearchForward** | BFS via SearchForwardNodes |
| **向上搜索** | **SearchReverse** | BFS via SearchReverseNodes |
| **双向搜索** | **SearchAllRelative** | BFS via SearchAllRelativeNodes |
| **连通性检查** | **IsConnected** | 传递可达性检测 |
| **查找路径** | **FindPath** | 两节点间最短正向路径 |
| 按 ID 断开连接 | DisconnectSlotsById | Link.DeleteCommand |
| 断开插槽所有连接 | DisconnectAllFromSlot | 批量 Link.DeleteCommand |
| 断开节点所有连接 | DisconnectAllFromNode | 批量 Link.DeleteCommand |
| 替换连接 | ReplaceConnection | 原子式断开+重连 |
| 设置插槽通道 | SetSlotChannel | Slot.SetChannelCommand |
| 查看连接详情 | GetLinkDetail | 仅只读查询 |
| 对多节点执行工作 | ExecuteWorkOnNodes | WorkCommand × N |
| 批量修改节点属性 | BulkPatchNodes | 相同属性变更应用到 N 个节点 |
| 对齐节点 | AlignNodes | SetAnchorCommand × N（左/右/上/下/居中） |
| 分布节点 | DistributeNodes | 按轴均匀间距 |
| 自动拓扑布局 | AutoLayout | Sugiyama 分层布局，按传播链排列 |
| 节点统计 | GetNodeStatistics | 入度/出度、已连接节点 |
| 列出可创建类型 | ListCreatableTypes | 发现可用节点/插槽类型 |
| 验证工作流 | ValidateWorkflow | 检查问题（零尺寸、孤立节点等） |

## AgentContext 属性规则

标注了 `[AgentContext]` 的属性是**开发者明确授权 Agent 读写**的属性。

- 若属性**无对应命令**（如 `Title`、`DelayMilliseconds`）→ 使用 **PatchNodeProperties** / **PatchComponentById** 直接设置。
- 若属性**有对应命令**（如 `Size` → `SetSizeCommand`）→ 使用对应工具（如 **ResizeNode** 或 **CreateNode** 传入 width/height）。
- 开发者的 `[AgentContext]` 描述中可能包含**默认值**（如"默认大小为 200×100"），请遵守并使用这些值。
- **首次创建或配置某组件类型前**，调用 **GetComponentContext** 读取这些注解（若预加载描述已包含则无需重复调用）。

## 上下文缓存

无需每轮都调用 `GetWorkflowSummary` 或 `GetComponentContext`。读取过某类型的上下文后，在整个对话中记住它即可。仅当用户提示类型发生变更时才重新读取。
