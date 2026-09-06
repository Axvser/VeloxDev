## 🧭 技能：编译器与编译期路由

WorkflowSystem 内置编译器：把一张子图分解为编译计划——`ExecuteEntry` 线性段、`BranchEntry` 分支点、`ParallelEntry` 扇出组——并给每个编译感知节点分配固定的**编译身份**。编译工具能给你编译器 UI 看到的同一视图。

每次编译调用都指定一个节点及其角色：
- **Root** —— 节点作为启动/入口（如控制器）；沿下游编译它可达的子图。
- **Terminal** —— 节点是你想要的结果；只编译它的**祖先锥**（沿 `Sources` 反向追溯到的所有上游生产者），并自动从锥自身的入口前沿开始 —— 不需要控制器/启动节点。

### ⚠ 三个执行入口——不要混淆

| 入口 | 工具 | 驱动对象 | 语义 |
|---|---|---|---|
| **节点级** | `ExecuteNode` / `ExecuteNodes` | 单个节点（其自身 `ReceiveAsync`，经 `ReceiveCommand`） | `EXEC`/`RECV`；若节点 `AutoBroadcast` 开启，可能级联下游。 |
| **链级（Root）** | `RunCompiledWorkflow(nodeIndex)` | 从启动/入口节点开始的整条编译链 | 注入 `IRuntimeContext` 会话、选分支、处理回退；下游派发由引擎接管（不自动广播）。 |
| **结果级（Terminal）** | `GetNodeResult(nodeIndex)` | 目标节点的祖先锥（从其入口前沿开始） | 编译该锥并运行，使被查询节点自身的输出成为结果。router 的分支选择保持真实（见下）。 |

只读的计划等价工具：`CompileWorkflow(nodeIndex)` 编 **Root** 计划；`CompileNodeResult(nodeIndex)` 编 **Terminal** 计划。

任务语义是"运行工作流 / 执行整条链"时用 `RunCompiledWorkflow`；只需要**某个节点的值**、不想从控制器跑整条链时用 `GetNodeResult`；只有确实想单独触发某个节点的逻辑时才用 `ExecuteNode`。

> **编号**：`#N` 徽标是编译机器的身份（`CompileContext.Order + 1`）。直接 `ExecuteNode`（非编译器任务）只会把活动记入执行日志（`GetExecutionLog`），**不会**重新给节点编号——不要期待它改变徽标，也不要自己去"分配"顺序号。

### Terminal（结果）语义——router 不会被绕过

Terminal 编译与**正向语义完全一致**：目标锥上的 router 保持真实的 `BranchEntry` 行为，且只编译通往锥的那一支（兄弟支直接不存在）。

- 若 router 的实际决策选中通往目标的那一支 → 目标被驱动并返回其输出（`targetReached: true`）。
- 若 router 运行期选到**兄弟支** → 目标**不可达**。`GetNodeResult` 返回 `status: error`，其 `message` 指名目标（… `was NOT reached` … `No result was produced.`）。**绝不要把别的支的最终载荷当成该节点的结果**——不会编造值。

要拿到这种节点的值：
1. 先让 router 指向通往它的那一支——用 `PatchNodeProperties` 或 `SetEnumSlotCollection` 设置 `CompileMode`/选中键——再重试 `GetNodeResult`。
2. 或者改问位于 router 实际选中分支上的节点。

其他 Terminal 要点：
- 若**同一个 router 有多条 route key 都通往被查询节点**，`CompileNodeResult` 会返回错误——单次正向运行只能走一条分支，该节点没有良定义的结果。
- 编译驱动时引擎会给实现 `IRuntimeAware` 的节点注入 `IRuntimeContext` 会话（UID / 日志 / 共享变量 / 执行位置）。

### 编译身份（ICompileContext）

每个实现 `ICompileTimeAware` 的节点在编译完成后都会拿到一个 `ICompileContext`：

| 成员 | 含义 |
|---|---|
| `Order` | 固定执行序号。**`-1` = 绝对停止**——该节点处于被剪除的静态分支，不得运行。 |
| `ChainIndex` | 线性段内索引。 |
| `Offset` | 子图入口偏移。 |

用 `GetCompileStatus` 查询（逐节点返回 `{i, id, t, order, chainIndex, offset, isStopped}`）。

### 路由模式（RouterCompileMode）

| 模式 | 编译期行为 | 运行期行为 |
|---|---|---|
| **Static** | 分支 key 锁定为选择器当前值。未选中分支被剪除，其下游节点 `Order = -1`（停止）。 | 只执行锁定的分支。 |
| **Dynamic** | 无法决策——`ResolveRouteKey(null)` 返回 null；**所有**分支存活（`isDynamic = true`）。 | 每次运行按数据负载重新解析 key。 |

### 读取节点编译状态

- `ICompileContext` —— 只读；节点的 `Order` / `ChainIndex` / `Offset`。
- `IsCompileStopped` —— `Order == -1` 时为 `true`（被剪除的静态分支）。
- `CompileMode` —— 路由节点的编译模式（`Static` / `Dynamic`），**可写**：通过 `PatchNodeProperties` 设置。

### 操作方式

1. **先编译** —— 从控制器/入口节点调用 `CompileWorkflow(nodeIndex)`。读取返回的 `entries`（Execute/Branch/Parallel，含 options、`isTerminal`）与 `nodeOrders` 以理解计划。想要单个节点的结果计划时用 `CompileNodeResult(nodeIndex)`。
2. **切换路由模式** —— 用 `PatchNodeProperties` 设置路由节点的 `CompileMode`（如 `{"CompileMode":"Static"}`），再重新调用编译工具。Static 模式下应看到被剪除的分支（`Order = -1`）。
3. **尊重停止节点** —— `Order = -1` 的节点已被编译出活跃路径，不要把它当作活跃链来驱动。
4. **运行整条链** —— 调用 `RunCompiledWorkflow(nodeIndex)` 端到端执行编译链（即 Demo Run 按钮的路径）。读取返回的 `runStatus`（`Completed`/`Stopped`）、`logs`（执行轨迹）、`data`（最终载荷）与 `endedWithError`。
5. **取单个节点结果** —— 调用 `GetNodeResult(nodeIndex)` 从其祖先锥算该节点。若返回错误说目标 `was NOT reached`，说明 router 选了别的分支（见上）——不要编造，按恢复步骤处理。
6. **运行时会话** —— 编译驱动时引擎会给实现 `IRuntimeAware` 的节点注入 `IRuntimeContext` 会话（UID / 日志 / 共享变量 / 执行位置）。共享变量用 `Set(key, value)` 写入、`TryGet(key, ...)` 读取。

> 只需当前编译身份时，优先用 `GetCompileStatus`（廉价、不重新编译），而不是重复编译工具。要执行时：整条链用 `RunCompiledWorkflow`、单个节点值用 `GetNodeResult`——除非确实只想单独触发某个节点，否则不要用 `ExecuteNode`。
