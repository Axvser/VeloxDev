# WorkflowSystem — 编译执行引擎（CompilerEx）的并发模型与契约

> 代码：`Src/Core/VeloxDev.Core/WorkflowSystem/CompilerEx/`（`Compile/` 16 文件 + `Runtime/` 20 文件）。
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
| `Data`、`RedirectRequested`、`PendingRedirectTarget` | `Uid`、`Sequence`、`Status`、`IsRunning`、`Attempt`、`CurrentEntry`/`NodeIndex`/`BranchKey`/`CurrentOrder`、`Target`/`TargetReached`、`EndedWithError`、`ActiveRedirectTarget`、`Set`/`TryGet`、**`Logs`**、`RegisterOutput`/`ResetOutputs`/`CollectGroupedInputs` |

节点拿到的永远是**本分支**的门面（以 `IRuntimeContext` 出现），所以载荷与旗标天然归属正确；而身份/进度/产物表/黑板仍只有一个 → UI 仍只绑一个会话、汇合点仍聚合一个产物表。

**链式图不受影响**：不在扇出里的节点仍拿到宿主那一个会话。`EntrySemanticsTests.OneRunSession_IsInjectedAsTheSameInstance_ToEveryNode` 用的是 a→b→c 链，它继续成立 —— 既有 25 条引擎测试在这轮改动里**一条未改**。

## 三、确定性政策一律「按分支序」

- **日志**：**严格按真实时序**，分支直写会话、不按分支归块（2026-09-27 改，推翻了此前「按分支成块合并」的做法）。所以序号在 `Logs` 里单调递增，而文件 sink（`ILogWriter`）与 `Logs` 逐行一致 —— 这是拿日志文件对时序的前提，不要为「读起来整齐」再把缓冲加回来。
- **重定向**：多个分支同时请求时**分支序最先者胜**，其余写一行日志忽略。注意「最先」实现为**按编译顺序**而非按墙钟先到，为的是让一轮运行可复现。
- **`Data`**：跑完留下**最后一支**的载荷，与串行时留下的残留值逐字一致（Agent 工具在 `RunAsync` 返回后读它，见 `WorkflowAgentToolkit` 的结果 JSON）。
- **语义变化**：`Task.WhenAll` 下一个分支抛异常不再立即中止兄弟，而是整组跑完再抛。节点异常在它自己的驱动里就被消化成「报告 + 记 null」（§六），本来也到不了 `Task.WhenAll`，所以实际影响面小 —— 已写进方法注释。
- 汇合点仍在整组之后，且分支是**单线程交错**（不是线程并行），所以产物表 `_outputs` **没有加锁**、也不需要 —— 这一点此前记错过（写成「加锁就够」），以代码为准。前提是宿主把运行钉在一个 `SynchronizationContext` 上；节点内部若自己 `Task.Run` 就会破坏这个前提。

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

## 六、两档报告：`Warn` 只是提醒，`Error`（与未捕获的异常）主动停止（2026-09-27 定）

| 节点做了什么 | 有 `IRedirectable` | 没有 |
|---|---|---|
| 干净返回 | — | 值传下去 |
| `Warn()`（正常返回） | **不改变走向**（连问都不问） | 值照常传下去，运行继续 |
| `Error()`，或抛异常（重试耗尽后） | 问它的决定 → 重定向或继续 | **整轮结束**：`CurrentOrder = -1`、`EndedWithError = true`、Status `"Stopped"`、Outcome `Failed` |

- 判据是 `RuntimeContext.ReportedLevel`（`ExecutionReportLevel?`：null / Warning / Error），由 `Error()`/`Warn()` 写；**抛异常那支由 `DriveAsync` 的 catch 调 `context.Error(...)` 写同一档** —— 所以异常没有单独的通道，它就是 Error 档。`IRuntimeContext.RedirectRequested` 现在是从 `ReportedLevel` **派生**的（契约上的 bool 没动，赋 `true` 按 Error 算）。
- **警告不吃掉结果**：`DriveAsync` 只在 Error 档把这次驱动记成「返回 null」+ `RegisterOutput(node, null)`；Warn 档节点返回什么，下游就收到什么（汇合点里读到的是那个值，不是 null）。
- **级别是分支私有的**：门面自带一份（与 `Data`/`CurrentNode` 同理）—— 写在会话上，交错的两个分支会互相覆盖。引擎读它走 `RuntimeEngine.ReportedLevel(context, session)`。
- 与 2026-09-27 之前那条「一句 `Warn` 终止整轮」比，**只有 Warn 这一格变了**；Error 与抛异常的行为逐字回到旧规则。
- 分档的理由：demo 的 [`PythonHelper.cs:27`](../../../Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/PythonHelper.cs) 与 [:54](../../../Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/PythonHelper.cs) 正好是两种意图 —— 脚本为空是「这一支没东西可跑」（30 个分支里有一个没写脚本不该拖垮整轮），python 进程失败是「没有任何下游能绕过它」。而全仓**没有一个生产 `IRedirectable` 实现者**（只有测试探针），所以 Error 档的默认后果就是终止。
- **同步那对只写日志，异步那对才记录到宿主**：`ErrorAsync`/`WarnAsync` = 同步那对 + 把 `ExecutionError` 交给 `IExecutionErrorSink`（带 `ExecutionReportLevel`，见 §十），**档位语义完全继承**（`ErrorAsync` 一样停）。同步的 `Error`/`Warn` 必须保持 `void`（`ILogWriter.Write` 是同步的，节点帧里不能阻塞），所以它不碰 sink。
- 节点自己报的记录里带 `CurrentNode`：**扇出里存在分支门面上**，与级别同一个理由。

测试：`NodeReportTests`（4 条：Warn 让值流过、Error 无处理者时结束整轮、Error 有 `IRedirectable` 时由契约安排、警告后的值与汇合点）；抛异常那两条是 `RuntimeEngineRunTests.ANodeThatThrows_WithoutIRedirectable_EndsTheFlowWithStatusMinusOne` 与 `EngineHostContractFailureTests.AnAttachThatThrows_EndsTheRun_InsteadOfSilentlySkippingTheNode`。

## 七、测试在哪、什么没测

- 引擎子集：`Src/Core/VeloxDev.Core.Test/WorkflowSystem/CompilerEx/`，**16 文件 / 77 条**（2026-09-27 实测；同日从 44 条经「五个可选能力契约」涨到 66、「报告不打断运行」到 72、「检查点与恢复」到 77）。**全部用手写探针**（`ProbeNode` 实现了全部三个契约）驱动，**从不针对真实的 `NodeDefaultViewModel`/`TreeDefaultViewModel`** ⇒ 它证明的是「**契约被实现时**是对的」，不是「没实现时会怎样」—— 第四节那类静默降级正好落在覆盖之外。
- 并发契约由 `ParallelExecutionTests` 钉住（5 条）：时间窗相交、上限为 1 时串行、分支只看得到扇出源载荷、**日志按真实时序**（因果交错：A 先记一行、等 B 记完再记第二行 → 断言 `A1 < B1 < A2`，成块合并必然读成 `A1, A2, B1`）、重定向取分支序最先。**做法是先写测试**：其中两条在串行引擎下必然失败（时间窗不相交 / `s0.Calls == 2`），改完才绿 —— 这类「先让测试证明它能判别」的次序值得沿用。
- 日志 sink 与上限另由 `CompilerLogWriterTests`（`Core.Test`）钉住：writer 与 `Logs` 逐行同序、上限只裁内存（`0` = 只落 writer）、**writer 抛异常不改变运行**（吞掉并报 `LogWriteFailed`）、分支的 `Warn` 不置会话的 `RedirectRequested`；Agent 路径那条在 `Core.Extension.Test` 的 `WorkflowLifecycleFidelityTests.WithLogWriter_RoutesACompiledRunsLinesToTheHostsSink`。
- **没测**（2026-09-27 更新：**取消已补测**，见第十节）：`ControllerViewModel` 整个（`Examples/` 没有测试工程）；Agent 侧 `CompileWorkflow`/`GetCompileStatus`/`GetExecutionLog` 三个工具；`ChainIndex`/`Offset`/`Segment.Id`/`Depth` 的值；重定向上限（50 次）那条路只有代码审查，没有测试跑进去过。

## 八、编译图作为可序列化文档（2026-09-27 起）

`CompiledGraph` 本就是标准 VM（段 + `ObservableCollection`，自身不含 slot/link），所以**列表视图可以直接绑**（`Entries` → `BranchSegment.Options` → `ParallelSegment.Branches` 的嵌套模板；仓库里此前零处绑定）。序列化走 Core.Extension 的专用扩展 `CompiledGraphEx`：`SerializeCompiledGraph(graph, includeTree = false, options)` / `DeserializeCompiledGraph(json)`。

**第一次往返验证就暴露两件事 —— 都是先实测、再设计：**

1. **一个节点就能把整棵树拖进 payload**：节点的 `Parent` 是可写的 `IWorkflowTreeViewModel`，以图为根加载还会得到**第二棵孤儿树**作为节点的 Parent；slot 的 `Targets`/`Sources` 同理会把**整个连通分量**拖进来。⇒ 快照模式按**声明类型**排除这两类（`typeof(IWorkflowTreeViewModel)`、`typeof(ObservableCollection<IWorkflowSlotViewModel>)`），经 `SerializationOptions.WithExcludedPropertyTypes` 落到既有的 `WritablePropertiesOnlyResolver` 上（默认空 ⇒ 树的往返逐字不变）。**按名排除会误伤**：`Parent` 在 slot 上指的是它的**节点**，那是前向需要的。
2. **枚举键会退化成 `long`**：实测钉在 `ComponentModelExTests.AnEnumInAnObjectMember_ComesBackAsItsNumber`，且 `TypeNameHandling.All` **也救不回来**（同文件另一条测试把这条死路钉住）。静态分支靠两边都退化侥幸相等；**动态分支在运行期重新解析出真枚举、与还原出的 `long` 永不相等** ⇒ 那条分支会像「没有下游」一样结束。⇒ `BranchSegment`/`BranchOption` 各带一个「键的类型名」侧信道（**只在键是枚举时**记录），在 `[OnDeserialized]` 里用 `CompileKeyNormalizer` 归一化。用 `[OnDeserialized]` 而不是生成的 setter：后者在编译器赋值时就会触发，加载时也可能早于类型名成员被读到。越界数字保持为未定义枚举值、类型名解析不到就原样保留，都不抛。

**两种模式的代价（已写进 `CompiledGraphEx` 的文档）**：默认**快照** = 段结构 + 每个节点自身的状态，**不可回灌**（还原节点无 `Parent` ⇒ 几何不再按缩放坍缩、移动不再标脏）；`includeTree: true` = 保留外向引用、可回灌，但 payload ≈ 整棵树 + 连通分量。两种模式下**还原节点的 `RuntimeId` 都是新的、编译身份都不在**（两者都不可写 ⇒ 被 resolver 丢掉），即第四节那条静默降级。

**列表视图有两条路**：嵌套模板直接绑 `Entries` / `Options` / `Branches`；想一次看到整张结构就用 `CompiledOutline.Of(graph)`（Core 的纯函数 → `Depth`/`Kind`/`Label`/`Nodes`，段类型词 `Execute`/`Branch`/`Parallel` 与 Agent 侧那份投影一致）。图在编译后冻结，所以拍平只需算一次。demo 的 Avalonia 侧栏已按后者做了一个面板：控制器编译后**推**给树（`TreeViewModel.RefreshCompiledStructure`，与 `Run` 推 `BeginWorkflowRun` 同一约定 —— 树不订阅控制器的属性变化），**其余六家只差同一段 XAML**。

**验证强度（照实记）**：数据通路有单测（编译经控制器命令 → 填充树上的列表、重编译替换而非追加）；XAML 绑定由 Avalonia 的编译绑定在**构建期**校验；**像素层未验** —— demo 的交互无法用合成输入驱动（实测两次点击、连 native 签名都修对了，仍到不了 Avalonia 的输入管线，`Run` 始终置灰），所以面板显示出来的样子仍需人眼。

**端到端「还原后的图能跑」已验**（2026-09-27 补）：`CompiledGraphSerializationTests.ARestoredGraph_DynamicallyRoutesOnItsEnumKey` —— 一张**动态 + 枚举键**的分支图，序列化→反序列化→`RuntimeEngine.RunAsync` 仍路由到正确那一支。这条**故意做成能判别**：把 `BranchOption` 的 `[OnDeserialized]` 临时去掉后它必然失败，日志给出正是那个失败模式 —— `Branch 'Low' has no downstream node; the run ends.`（运行期解析出真枚举、还原的选项键还是 `long`，一个都不匹配）。

## 九、日志 sink 与上限（`ILogWriter`）

- `ILogWriter.Write(string)`（**同步** —— `Log/Error/Warn` 是 `void`，改签名是破坏性）+ `DelegateLogWriter` + `TextWriterLogWriter`（`For(path)`：append、UTF8 无 BOM、每行 flush；**谁开的文件谁 Dispose**，借来的 `TextWriter` 不动）。
- 配置点：`RuntimeContext.LogWriter` / `MaxRetainedLogs`（**具体类成员，不进 `IRuntimeContext`** —— 加成员会破坏外部实现）；Agent 侧由 `WorkflowAgentScope.WithLogWriter` 承载（那个会话是工具内的局部变量，不经 scope 根本配不到）。
- **writer 抛异常不改变运行**：`AppendLog` 吞掉并报 `LogWriteFailed`。不吞的话异常会逃出节点帧、被引擎当成重定向请求 —— 诊断不得改变控制流（同 `AgentPipeline` 隔离 stage 的立场）。
- 上限默认 `null` = 不限：唯一读者是 Agent 工具（整份进结果 JSON），默认裁剪会静默改变模型看到的内容；`0` + writer 即「只落文件」。

## 十、五个可选能力契约：暂停 / 观察 / 重试 / 结构化错误 / 补偿（2026-09-27 起）

一次改动把「引擎没有的那几条轴」做成五个**彼此独立**的契约，一家一个接口。契约全在 `Runtime/Contracts/`，默认实现在 `Runtime/Model/`（一族的委托适配器一个文件，照 `LogWriters.cs` 的先例）；配置点一律是 `RuntimeContext` 的**具体类成员**，不进 `IRuntimeContext`（加成员会破坏每个外部实现，`MaxParallelBranches`/`LogWriter` 已是先例）。**不配置 = 从前行为逐字不变**：没有日志行、没有多一次 `await`、每个节点派发次数照旧。

| 契约 | 轴 | 挂点 | 配了会多出什么 |
|---|---|---|---|
| `IExecutionGate` | 暂停 | `DriveAsync` 顶部 | `Status` 出现第四个值 `"Paused"` |
| `IExecutionObserver` | 可观测 | run 起止 / 每节点 / 每分支 | 一行不写日志的观察流（`ExecutionObservation`） |
| `INodeRetryPolicy` | 异常→重试 | `DriveAsync` 的 catch 内 | `[Retry n]` 日志行 |
| `IExecutionErrorSink` | 结构化错误 | 引擎每次记错误处 + 节点自己调 `ErrorAsync`/`WarnAsync` 时 | `ExecutionError` 记录（取消也送一条，带 `ExecutionReportLevel`，见 §六） |
| `IExecutionCompensation` | 补偿 | `RunAsync` 的 finally，逆序 | 失败/取消收尾时的逐节点回调 |

**解析点只有一个**：`RuntimeEngine.Session(IRuntimeContext)`。扇出里节点拿到的是 `BranchRuntimeContext` 门面，能力必须透过它的 `Session` 去取 —— 直接 `context as RuntimeContext` 会在**最花时间的地方**静默失效。这条做过判别实验：把解析改回裸转型，`ExecutionGateTests.AClosedGate_AlsoHoldsTheBranchesOfAFanOut` 当场失败，症状正是「门关着，两条分支都跑了」。

其余几条容易记错的：

- **重试不是新一轮**。`Attempt` 数的是过图的趟数，同时是产物表的戳（`RuntimeContext.cs` 的 `_outputs` 与 `CollectGroupedInputs`），重试绝不碰它；重试只对**抛出的异常**生效，节点自己 `Error()`/`Warn()` 是刻意的重定向请求、不重试（`RuntimeEngine.NextRetryAsync` 先看 `RedirectRequested`）。
- **`ExponentialBackoffRetry` 的 `maxAttempts` 是总尝试次数（含首次）**。实现原先 `RetryNumber + 1 >= maxAttempts` 少给一次，与它自己的文档矛盾；本轮按文档改成 `RetryNumber >= maxAttempts`，两条重试测试正是被它咬出来的。
- **补偿看的是整轮、不是单趟**。列表是 `RuntimeContext.CompletedThisRun`（按驱动序、每节点一条、重跑移到末尾），只有 `ResetOutputs` 清它 —— 重定向跳过的前缀只被驱动过一趟，若按趟清就永远补偿不到它。`ExecutionCompensationTests.AfterARedirect_...` 钉住这条（顺序 `r, s1, x, s0`）。
- **取消只进 sink，不进日志**：宿主自己停的运行不是失败，写一行 `[Error]` 会让以后读 `Logs` 的人以为出过错。`RunOutcome` 才是把 `"Stopped"` 拆成 Failed / Cancelled 的那个成员（`RunOutcome.Unknown` = 取消之外没跑完，例如异常穿出 `RunAsync`）。
- **观察者与 sink 抛异常都不改运行**（各留一行日志）；补偿器抛异常不掩盖原始失败、也不中断其余节点；重试策略抛异常当作「不再试」。

**同笔修掉的两处宿主契约缺陷（行为变更）**：`ResolveRouteKey` 与 `ResolveRedirectAsync` 原先无守卫，宿主实现一抛异常就穿出 `RunAsync`、`Status` 停在 `"Running"`（会话谎称还在跑）；`IRuntimeAware.AttachRuntimeContext` 在 `try` 之外调用，异常落进空 catch ⇒ 节点被**无声跳过**。现在三者都走与节点体同一套失败纪律（记 Error → 重定向或结束），`EngineHostContractFailureTests` 三条分别钉住。重定向上限那条路也顺手补了 `Status = "Stopped"`（原先同样停在 `"Running"`），但它**只有代码审查、没有测试**跑进去过。

**注释风格别照抄**：`CompilerEx` 的 `internal`/`private` 成员上还是规范生效前写的英语 `///`（`RuntimeEngine` 里那几个老私有方法、`BranchRuntimeContext` 整份）。本轮新写的行按手册 §二 用中文 `//`，所以文件里两种并存 —— **以手册为准，不要拿旁边的老注释当标准**。

## 十一、检查点与恢复（2026-09-27 起）

一个契约 + 一个 DTO + 一个默认实现，全在 `CompilerEx/Runtime/`；落盘实现（JSON 与文件）在 `Core.Extension` 的 `CheckpointEx.cs`（与 `CompiledGraphEx` 同一种处境：命名空间属 `VeloxDev.MVVM.Serialization`，物理在 Extension 项目）。

| 件 | 位置 | 说明 |
|---|---|---|
| `IExecutionCheckpointStore` | `Runtime/Contracts/` | `SaveAsync` / `LoadAsync`。一个 store 一个运行，只存「最后一次」；`LoadAsync` 是宿主自己调的，引擎不会去读 |
| `ExecutionCheckpoint` | `Runtime/Model/ExecutionCheckpoints.cs` | `Attempt` / `ActiveRedirectTarget` / `Data` / `Outputs`（节点键 → 产物）/ `Shape`（指纹） |
| `InMemoryCheckpointStore` | 同上 | 默认实现，保留对象图本身 |
| `CheckpointEx` + `FileCheckpointStore` | `Extensions/CheckpointEx.cs` | JSON 往返 + 文件 store（写入串行化） |

- 配置点是 `RuntimeContext.CheckpointStore`（具体类成员，同其它能力）；恢复入口是 `RuntimeEngine.RunAsync(graph, context, ct, resumeFrom)` 的**第四个参数**（可选，老调用点不受影响）。
- **保存**在 `DriveAsync` 里每次「成功驱动」之后（`Snapshot()`）。失败那次不写 —— 于是恢复会**重新驱动那个节点**，宿主多半正是修好了它才恢复的。
- **跳过按节点、不按 Order**：`RunGraphAsync` 多带一个「已完成节点集合」。用 Order 阈值在扇出里是错的 —— 几个分支的 Order 交错，一个阈值会连带跳过没跑过的兄弟（这与重定向的按 Order 跳过不同：那是「契约保留前缀」，一刀切本来就是它的语义）。该集合只作用于恢复的那一轮，一旦发生重定向就交回按 Order 的旧规则。
- **恢复的那一轮原样沿用检查点里的 `Attempt`，不是加一**：产物表按 `Attempt` 盖戳，加一等于把铺回去的产物降级成陈旧产物，汇合点立刻读不到它们 —— 这条是实测撞出来的（`Resuming_SkipsWhatTheCheckpointRecords_...` 当场红）。此后的重定向照旧一轮加一。
- **形状不符直接拒**，而且在动会话之前抛 `InvalidOperationException`（`Status` 仍停在 `Idle`，不是谎称在跑）—— `RequireSameShape`。
- **节点键**是 `RuntimeId`（节点实现 `IWorkflowIdentifiable` 时），否则 `类型名#序号`。后者让测试探针也能用。**序列化往返过的图**（还原节点的 `RuntimeId` 全是新的）默认被拒 —— 那些确实是不同的节点对象，把旧产物喂给它们就是猜；**要接上就显式迁移**：`ExecutionCheckpoint.Rekey(place, graph)` 按**位置**重新归档（先核结构：节点数与**节点类型**的驱动序都要一致，`Types` 是为此新加的成员），返回一份新的检查点，引擎的形状核对随之通过。这就是崩溃恢复的形状（存盘 → 重载图 → 接着跑），`Core.Extension.Test/Serialization/ExecutionCheckpointMigrationTests.cs` 钉住：原样传被拒、re-key 后通过且已完成节点不再驱动。
- **`IGroupData` 归一化**：`Snapshot()` 把载荷与产物里的 `IGroupData` 换成**以节点键为键的普通字典**。节点引用写不进文件，而且序列化会顺着它把整棵树拖进去（与 `CompiledGraphEx` 排除 `Parent` 同一个坑）。运行中的对象不动，只换快照里那一份。
- **数字不保类型（实测）**：载荷是 `object`，JSON 只有一种整数 ⇒ `int` 回来是 `long`、`float` 是 `double`，`TypeNameHandling.All` **也救不回来**（试过了）。引擎自己的字段精确；`InMemoryCheckpointStore` 没有这个缺口。三个 TFM 里 `KeyValuePair` 也没有 `Deconstruct`，遍历产物表要显式取 `Key`/`Value`。
- DTO 是**纯数据**，所以不走 `ComponentModelEx` 的公开序列化面（那一面被 `INotifyPropertyChanged` 约束住了，它是为 VM 写的），而是用同程序集 `internal` 的 `CreateJsonSerializer()` —— 于是它继承库里的全部默认设置（保留引用、循环忽略、字典键转换器）。

测试：`Core.Test/…/ExecutionCheckpointTests.cs`（5 条）+ `Core.Extension.Test/Serialization/ExecutionCheckpointSerializationTests.cs`（5 条）。判别性最强的一条是「半途停 → 恢复」：断言既要求没跑过的分支被驱动，也要求**被跳过的那个节点在汇合点里仍读得到它当初的产物** —— 只测「跳过了」的话，产物铺没铺回去是看不出来的。

**demo 侧已接（2026-09-27）**：门与检查点是六个能力里唯一「得有人按一下」的两件（其余四件在运行里自己生效），所以它们在 demo 里有可点的东西 —— `WorkflowDemoSession.Gate` 交给每一轮运行、`HasCheckpoint` 给按钮判可用；`ControllerViewModel` 因此有了 `ResumeCommand`（`Run`/`Resume` 共用 `DriveAsync`，`CheckpointSource` 由会话注册且是 `internal`，所以不进序列化）。**七家 demo 现在都有**一块「运行控制」（Pause / Resume / 从检查点继续）：Avalonia / WPF / WinUI / MAUI / Blazor / Jalium 放在各自宿主外壳的侧栏，WinForms 放在 `Form1` 的工具栏（与「停止工作流 / 重置示例」同一排）。**放在外壳而不是节点卡上**是有理由的：门与检查点是**会话级**的，而节点卡（如 WinForms 的 `Controls/WorkflowNodeCard.cs:654-658`，那四个 Compile/Run/Stop/Close 按钮所在处）上下文只有节点 VM，要够到会话得顺着 `Parent` 往上爬。两点值得记：`ConfigureRun` 每次都 `Gate.Resume()`（一轮运行不带着上一轮的暂停开始）⇒ **「运行前先暂停」不成立**，暂停只能在运行中途按（也正是 UI 的用法）；以及会话的暂存目录可指定（`Create(scratchDirectory)`）—— 并行测试各自一个目录，否则日志与检查点这两条**固定路径**会互相踩。

## 十二、重定向与分支的三处边界（2026-09-27 实测，做 demo 那张展示图时撞出来的）

1. ~~**目标落进（嵌套）分支内部时，整条分支会被跳过。**~~ **已修（2026-09-27，同一笔）。** 原先 `RunBranchAsync` 写的是 `if (redirectTarget is int t && routerOrder < t) return false;` —— 注释说「目标在分支之前则整条跳过」，条件表达的却是「路由器在目标之前」⇒ 目标落在分支内部时整条被跳过，这一趟**一个节点都不会重跑**（实测：日志有 `Redirecting to compile state #3 …`，第二趟零驱动，运行照样 `Completed`）。**修法是删掉这条整分支跳过**：分支一律进，让「**目标之前不驱动**」这条统一规则去跳节点；顺带把路由器的驱动条件改成 `target < routerOrder` —— 目标在路由器之后（含落在分支内部）时，路由器属于保留前缀，**不再驱动**（原先会驱动，违反同一条规则）。`RuntimeRedirectTests.RedirectIntoABranch_EntersIt_AndDrivesFromTheTargetInside` 是判别测试：修复前该分支里那个目标只被驱动 1 次，修复后 2 次。demo 那张图原本为此把回退目标从 `Generate Dataset` 绕成 `Ticker`，现在两种写法都对。
2. **一条分支的所有选项都通向的节点，只会被编进其中一个选项。** demo 里 `Publish` 原本挂在三个报告节点之后 ⇒ 编译器把它编进遍历时先遇到的那个选项（实测它的 order 11 只属于 `Zero` 选项）⇒ 路由到 `Low` 的那一轮它根本不跑。想「分支之后再收拢」的步骤，得放到分支**之前**。
3. **报错的那一趟给下游留 null，而重定向不会中断当趟。** 报错的驱动记 `null`（§六）＋ `RunExecuteAsync` 记下回退目标后继续走完这条链 ⇒ 被拒绝的那一趟，**尾巴拿到的全是 null**。demo 的尾巴脚本因此按「空载荷就记一行 warning 返回」写 —— 否则一次拒绝会换来一屏堆栈。

## 十三、未做（别当成遗漏）

| 未做 | 说明 |
|---|---|
| 节点缺契约时给一次提示 | 第四节那些降级目前完全静默；编译期警告或日志都还没做 |
| 只编译的两个工具纳入闸门 | Agent 侧的 `CompileWorkflow`/`CompileNodeResult` 会写节点编译身份却不受 `WithAllowNodeExecution` 约束 —— 属 `VeloxDev.Core.Extension` 模块 |
| 编译执行时补 `Sender`/`Receiver` | 第五节的不对称仍未消 |
| `ExecuteCommandOnNode` 的完成语义 | Agent 侧它同步返回、不等完成，而同族的 `ExecuteNode` 会等 `Exited` —— 属 Extension 模块 |
| 七家 demo 的运行控制**在像素层仍未验** | 七家的控件与处理器都已接上、构建 0 错误，但**点下去的样子**没人看过：除 Avalonia 外合成输入进不了输入管线（已实测），而且这七处是七种 UI 栈（两家还是命令式搭界面）⇒ 只能人眼验。**依据订正（2026-09-27）**：WinForms 的四个控制器按钮**不在** `Form1.cs:336-343`（那里是 `UpdateControllerState`），而在节点卡 `Controls/WorkflowNodeCard.cs:654-658` —— 那条旧依据写错了文件与行号 |
