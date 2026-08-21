## 🧭 技能：编译器与编译期路由

WorkflowSystem 内置编译器：把控制器可达的子图分解为编译计划——`ExecuteEntry` 线性段、`BranchEntry` 分支点、`ParallelEntry` 扇出组——并给每个编译感知节点分配固定的**编译身份**。`CompileWorkflow` 与 `GetCompileStatus` 工具能给你编译器 UI 看到的同一视图。

### ⚠ 两个执行入口——不要混淆

| 入口 | 工具 | 驱动对象 | 语义 |
|---|---|---|---|
| **节点级** | `ExecuteNode` / `ExecuteNodes` | 单个节点（其自身 `ReceiveAsync`，经 `ReceiveCommand`） | `EXEC`/`RECV`；若节点 `AutoBroadcast` 开启，可能级联下游。 |
| **链级** | `RunCompiledWorkflow(startNodeIndex)` | 整条编译链（执行引擎驱动） | 注入 `IRuntimeContext` 会话、选分支、处理回退；下游派发由引擎接管（不自动广播）。 |

任务语义是"运行工作流 / 执行整条链"时用 `RunCompiledWorkflow`；只有确实想单独触发某个节点的逻辑时才用 `ExecuteNode`。

> **编号**：`#N` 徽标是编译机器的身份（`CompileContext.Order + 1`）。直接 `ExecuteNode`（非编译器任务）只会把活动记入执行日志（`GetExecutionLog`），**不会**重新给节点编号——不要期待它改变徽标，也不要自己去"分配"顺序号。

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

1. **先编译** —— 从控制器/入口节点调用 `CompileWorkflow(startNodeIndex)`。读取返回的 `entries`（Execute/Branch/Parallel，含 options、`isTerminal`）与 `nodeOrders` 以理解计划。
2. **切换路由模式** —— 用 `PatchNodeProperties` 设置路由节点的 `CompileMode`（如 `{"CompileMode":"Static"}`），再重新 `CompileWorkflow`。Static 模式下应看到被剪除的分支（`Order = -1`）。
3. **尊重停止节点** —— `Order = -1` 的节点已被编译出活跃路径，不要把它当作活跃链来驱动。
4. **运行整条链** —— 调用 `RunCompiledWorkflow(startNodeIndex)` 端到端执行编译链（即 Demo Run 按钮的路径）。读取返回的 `runStatus`（`Completed`/`Stopped`）、`logs`（执行轨迹）、`data`（最终载荷）与 `endedWithError`。
5. **运行时会话** —— 编译驱动时引擎会给实现 `IRuntimeAware` 的节点注入 `IRuntimeContext` 会话（UID / 日志 / 共享变量 / 执行位置）。共享变量用 `Set(key, value)` 写入、`TryGet(key, ...)` 读取。

> 只需当前编译身份时，优先用 `GetCompileStatus`（廉价、不重新编译），而不是重复 `CompileWorkflow`。要执行时**一律用 `RunCompiledWorkflow`**——除非确实只想单独触发某个节点，否则不要用 `ExecuteNode`。
