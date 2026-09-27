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
| `Data`、`RedirectRequested`、`PendingRedirectTarget` | `Uid`、`Sequence`、`Status`、`IsRunning`、`Attempt`、`CurrentEntry`/`NodeIndex`/`BranchKey`/`CurrentOrder`、`Target`/`TargetReached`、`EndedWithError`、`ActiveRedirectTarget`、`Set`/`TryGet`、**`Logs`**、`RegisterOutput`/`ResetOutputs`/`CollectGroupedInputs` |

节点拿到的永远是**本分支**的门面（以 `IRuntimeContext` 出现），所以载荷与旗标天然归属正确；而身份/进度/产物表/黑板仍只有一个 → UI 仍只绑一个会话、汇合点仍聚合一个产物表。

**链式图不受影响**：不在扇出里的节点仍拿到宿主那一个会话。`EntrySemanticsTests.OneRunSession_IsInjectedAsTheSameInstance_ToEveryNode` 用的是 a→b→c 链，它继续成立 —— 既有 25 条引擎测试在这轮改动里**一条未改**。

## 三、确定性政策一律「按分支序」

- **日志**：**严格按真实时序**，分支直写会话、不按分支归块（2026-09-27 改，推翻了此前「按分支成块合并」的做法）。所以序号在 `Logs` 里单调递增，而文件 sink（`ILogWriter`）与 `Logs` 逐行一致 —— 这是拿日志文件对时序的前提，不要为「读起来整齐」再把缓冲加回来。
- **重定向**：多个分支同时请求时**分支序最先者胜**，其余写一行日志忽略。注意「最先」实现为**按编译顺序**而非按墙钟先到，为的是让一轮运行可复现。
- **`Data`**：跑完留下**最后一支**的载荷，与串行时留下的残留值逐字一致（Agent 工具在 `RunAsync` 返回后读它，见 `WorkflowAgentToolkit` 的结果 JSON）。
- **语义变化**：`Task.WhenAll` 下一个分支抛异常不再立即中止兄弟，而是整组跑完再抛。节点异常本就被 `DriveAsync` 转成重定向请求，实际影响面小 —— 已写进方法注释。
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

## 六、`Warn` / `Error` 会**结束流程**，不是日志

`Warn` 与 `Error` 一样置 `RedirectRequested`（`RuntimeContext.cs:102-113`）；节点若没实现 `IRedirectable`，`RunExecuteAsync` 就 `CurrentOrder = -1`、`EndedWithError = true` 并**结束整个流程**（`RuntimeEngine.cs:138-144`）。而**全仓生产代码没有一个 `IRedirectable` 实现者**（只有测试探针 `Core.Test/WorkflowSystem/CompilerEx/ProbeNodes.cs:265`）。

⇒ **一句 `Warn` 足以终止一次编译运行。** demo 的 `PythonHelper.ReceiveAsync` 在脚本为空时正是写 `rc.Warn(...)` —— 30 个分支里有一个没写脚本，整轮就结束。

## 七、测试在哪、什么没测

- 引擎子集：`Src/Core/VeloxDev.Core.Test/WorkflowSystem/CompilerEx/`，8 文件 / **31 条**（2026-09-27 实测）。**全部用手写探针**（`ProbeNode` 实现了全部三个契约）驱动，**从不针对真实的 `NodeDefaultViewModel`/`TreeDefaultViewModel`** ⇒ 它证明的是「**契约被实现时**是对的」，不是「没实现时会怎样」—— 第四节那类静默降级正好落在覆盖之外。
- 并发契约由 `ParallelExecutionTests` 钉住（5 条）：时间窗相交、上限为 1 时串行、分支只看得到扇出源载荷、**日志按真实时序**（因果交错：A 先记一行、等 B 记完再记第二行 → 断言 `A1 < B1 < A2`，成块合并必然读成 `A1, A2, B1`）、重定向取分支序最先。**做法是先写测试**：其中两条在串行引擎下必然失败（时间窗不相交 / `s0.Calls == 2`），改完才绿 —— 这类「先让测试证明它能判别」的次序值得沿用。
- 日志 sink 与上限另由 `CompilerLogWriterTests`（`Core.Test`）钉住：writer 与 `Logs` 逐行同序、上限只裁内存（`0` = 只落 writer）、**writer 抛异常不改变运行**（吞掉并报 `LogWriteFailed`）、分支的 `Warn` 不置会话的 `RedirectRequested`；Agent 路径那条在 `Core.Extension.Test` 的 `WorkflowLifecycleFidelityTests.WithLogWriter_RoutesACompiledRunsLinesToTheHostsSink`。
- **没测**：编译运行的**取消**（非默认 `CancellationToken` 传进 `RunAsync`）；`ControllerViewModel` 整个（`Examples/` 没有测试工程）；Agent 侧 `CompileWorkflow`/`GetCompileStatus`/`GetExecutionLog` 三个工具；`ChainIndex`/`Offset`/`Segment.Id`/`Depth` 的值。

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

## 十、未做（别当成遗漏）

| 未做 | 说明 |
|---|---|
| 节点缺契约时给一次提示 | 第四节那些降级目前完全静默；编译期警告或日志都还没做 |
| 只编译的两个工具纳入闸门 | Agent 侧的 `CompileWorkflow`/`CompileNodeResult` 会写节点编译身份却不受 `WithAllowNodeExecution` 约束 —— 属 `VeloxDev.Core.Extension` 模块 |
| 编译执行时补 `Sender`/`Receiver` | 第五节的不对称仍未消 |
| `ExecuteCommandOnNode` 的完成语义 | Agent 侧它同步返回、不等完成，而同族的 `ExecuteNode` 会等 `Exited` —— 属 Extension 模块 |
