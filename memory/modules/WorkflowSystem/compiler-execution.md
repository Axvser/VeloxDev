# WorkflowSystem — 编译执行引擎（CompilerEx）的并发模型与契约

> 代码：`Src/Core/VeloxDev.Core/WorkflowSystem/CompilerEx/`（`Compile/` 8 文件 + `Runtime/` 6 文件）。
> 提交标签用 `[Compiler]`（提交规范把它列为独立模块名），但记忆按模块粒度落在 `WorkflowSystem/` 下。
> 编译/运行的**流程与段类型**见 [architecture.md](architecture.md) §3.3；本文只写流程之外、读代码才知道的东西。

---

## 一、扇出是并发的（2026-09-27 起），但那不是线程并行

- `RunParallelAsync`（`Runtime/RuntimeEngine.cs:220`）用 `Task.WhenAll` 让分支**交错执行**，**不用 `Task.Run`**：分支在调用方上下文上启动、靠 `await` 让位 —— 所以 I/O 型分支重叠，而整组**不离开宿主的 `SynchronizationContext`**（「组件是 UI 绑定的」这条契约不破）。
- 推论：**CPU 型分支仍轮流占线程**。把分支体丢到线程池能拿到真并行，但那会破坏上面那条契约（`TrackedAIFunction` 的存在理由）——不要为了核数改这一层。
- 上限：`RuntimeContext.MaxParallelBranches`（`Runtime/Model/RuntimeContext.cs:83`），默认 `null` = 不限、按组生效。**刻意不进 `IRuntimeContext`**：给那个契约加成员会破坏每个外部实现，而这是引擎策略不是会话状态（自定义会话拿到的就是不限并发）。
- 收益形状：一个中枢 → N 个处理器 → 一个汇聚，每个处理器 `await` 一次进程调用（demo 的 python 节点就是），墙钟从 Σ 变 ≈ max。

## 二、每分支一个会话门面（`BranchRuntimeContext`）

**为什么不能用 `AsyncLocal` 做流隔离（走过一次死路，别再走）**：`AsyncLocal` 的写入**不跨帧回流给调用方**。而这个引擎的交接恰恰跨帧 ——

- `DriveAsync` 在**自己的延续**里写 `context.Data`，由**调用它的** `RunExecuteAsync` 读走（链式传递）；
- 节点里调 `Error()` 在**节点帧**里置旗标，由 `DriveAsync` 的延续读走（重定向判定）。

按 AsyncLocal 改的结果是「扇出载荷为空」「重定向没发生」，两条既有测试当场抓住（`FanoutParallel_RestoresSourcePayload_BeforeEachBranch`、`RedirectToPredecessor_SkipsPrefixOnRerun_CountsNodesPerPass`）。而旗标又**必须**跨帧（节点只拿得到 `context` 一个对象，没法把归属传给别的容器），所以唯一成立的形态是「每分支一个对象」：

| 分支私有（否则兄弟之间会互相覆盖） | 转发给宿主那一个会话 |
|---|---|
| `Data`、`RedirectRequested`、`PendingRedirectTarget`、`Logs`（缓冲） | `Uid`、`Sequence`、`Status`、`IsRunning`、`Attempt`、`CurrentEntry`/`NodeIndex`/`BranchKey`/`CurrentOrder`、`Target`/`TargetReached`、`EndedWithError`、`ActiveRedirectTarget`、`Set`/`TryGet`、`RegisterOutput`/`ResetOutputs`/`CollectGroupedInputs` |

节点拿到的永远是**本分支**的门面（以 `IRuntimeContext` 出现），所以载荷与旗标天然归属正确；而身份/进度/产物表/黑板仍只有一个 → UI 仍只绑一个会话、汇合点仍聚合一个产物表。

**链式图不受影响**：不在扇出里的节点仍拿到宿主那一个会话。`EntrySemanticsTests.OneRunSession_IsInjectedAsTheSameInstance_ToEveryNode` 用的是 a→b→c 链，它继续成立 —— 既有 25 条引擎测试在这轮改动里**一条未改**。

## 三、确定性政策一律「按分支序」

- **日志**：每分支一块，组跑完后按分支序合并（`RuntimeEngine.cs:249-252`）。分支内的序号仍取自会话的共享计数器，所以**序号不再等于显示顺序** —— 这是选定取舍，不是疏漏。
- **重定向**：多个分支同时请求时**分支序最先者胜**，其余写一行日志忽略。注意「最先」实现为**按编译顺序**而非按墙钟先到，为的是让一轮运行可复现。
- **`Data`**：跑完留下**最后一支**的载荷，与串行时留下的残留值逐字一致（Agent 工具在 `RunAsync` 返回后读它，见 `WorkflowAgentToolkit` 的结果 JSON）。
- **语义变化**：`Task.WhenAll` 下一个分支抛异常不再立即中止兄弟，而是整组跑完再抛。节点异常本就被 `DriveAsync` 转成重定向请求，实际影响面小 —— 已写进方法注释。
- 汇合点仍在整组之后，所以 `_outputs` 加锁就够（写 `RuntimeContext.cs:126`、读 `:142`）。

## 四、三个契约是**可选**实现的，而 Core 自带的节点一个都没实现

`ICompileTimeAware` / `IRuntimeAware` / `IRedirectable` **都不在** `IWorkflowNodeViewModel` 的继承链上（`Interfaces/WorkflowSystem/IWorkflowNodeViewModel.cs:9`）—— Core 的 `NodeDefaultViewModel` 只实现 `IWorkflowNodeViewModel, IWorkflowIdentifiable`（生成器也不补）。实现它们的是 **demo 的节点**：`Examples/Workflow/Common/Lib/…` 的 `PythonScriptNodeViewModel`、`TimerNodeViewModel`、`EnumSelectorNodeViewModel`、`ControllerViewModel`。

自研节点不实现时**静默降级**，没有任何提示：

- `AttachCompileTimeContext` 只在 `node is ICompileTimeAware` 时注入（`CompilerViewModel.cs:249`）⇒ 取不到 `CompileContext` ⇒ `Order` 恒 `-1`、`InputNodes` 恒 `null`；
- 于是多输入汇合点**拿不到 `GroupData`**，只看到上一个节点写进 `Data` 的输出（`RuntimeEngine.cs:327` 的注入条件不成立）；
- 重定向按 `order < target` 跳过节点，而 `-1` 永远小于目标 ⇒ 这类节点在**重跑里不会被驱动**。

## 五、两条与「广播路径」的不对称

| | 广播路径 | 编译执行 |
|---|---|---|
| `context.Sender` / `Receiver` | 由 `Templates/Helpers/TreeHelper.cs:155-156` 填上真实上下游 slot | **恒为 `null`** —— `DriveAsync` 从不给 `RuntimeContext` 的 `_sender`/`_receiver` 赋值（代码级核对；未做运行时验证） |
| 多输入节点的输入 | 按 slot 广播 | 只有 `InputNodes.Count > 1` 才聚合成 `GroupData`，否则是单值 `Data` |

## 六、`Warn` / `Error` 会**结束流程**，不是日志

`Warn` 与 `Error` 一样置 `RedirectRequested`（`RuntimeContext.cs:102-113`）；节点若没实现 `IRedirectable`，`RunExecuteAsync` 就 `CurrentOrder = -1`、`EndedWithError = true` 并**结束整个流程**（`RuntimeEngine.cs:138-144`）。而**全仓生产代码没有一个 `IRedirectable` 实现者**（只有测试探针 `Core.Test/WorkflowSystem/CompilerEx/ProbeNodes.cs:265`）。

⇒ **一句 `Warn` 足以终止一次编译运行。** demo 的 `PythonHelper.ReceiveAsync` 在脚本为空时正是写 `rc.Warn(...)` —— 30 个分支里有一个没写脚本，整轮就结束。

## 七、测试在哪、什么没测

- 引擎子集：`Src/Core/VeloxDev.Core.Test/WorkflowSystem/CompilerEx/`，8 文件 / **31 条**（2026-09-27 实测）。**全部用手写探针**（`ProbeNode` 实现了全部三个契约）驱动，**从不针对真实的 `NodeDefaultViewModel`/`TreeDefaultViewModel`** ⇒ 它证明的是「**契约被实现时**是对的」，不是「没实现时会怎样」—— 第四节那类静默降级正好落在覆盖之外。
- 并发契约由 `ParallelExecutionTests` 钉住（5 条）：时间窗相交、上限为 1 时串行、分支只看得到扇出源载荷、日志按分支成块、重定向取分支序最先。**做法是先写测试**：其中两条在串行引擎下必然失败（时间窗不相交 / `s0.Calls == 2`），改完才绿 —— 这类「先让测试证明它能判别」的次序值得沿用。
- **没测**：编译运行的**取消**（非默认 `CancellationToken` 传进 `RunAsync`）；`ControllerViewModel` 整个（`Examples/` 没有测试工程）；Agent 侧 `CompileWorkflow`/`GetCompileStatus`/`GetExecutionLog` 三个工具；`ChainIndex`/`Offset`/`Segment.Id`/`Depth` 的值。

## 八、未做（别当成遗漏）

| 未做 | 说明 |
|---|---|
| 节点缺契约时给一次提示 | 第四节那些降级目前完全静默；编译期警告或日志都还没做 |
| 只编译的两个工具纳入闸门 | Agent 侧的 `CompileWorkflow`/`CompileNodeResult` 会写节点编译身份却不受 `WithAllowNodeExecution` 约束 —— 属 `VeloxDev.Core.Extension` 模块 |
| 编译执行时补 `Sender`/`Receiver` | 第五节的不对称仍未消 |
| `ExecuteCommandOnNode` 的完成语义 | Agent 侧它同步返回、不等完成，而同族的 `ExecuteNode` 会等 `Exited` —— 属 Extension 模块 |
