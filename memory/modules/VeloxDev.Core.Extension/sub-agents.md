# VeloxDev.Core.Extension — 子代理子系统

> 代码：`Src/Core/VeloxDev.Core.Extension/Agent/SubAgents/`（6 个 .cs，命名空间 `VeloxDev.AI.SubAgents`）。
> 账本：`Agent/Workflow/Functions/ToolCallLedger.cs`（1 个 `internal` 类型，跨子代理与 Workflow 两侧）。
> 测试：`Src/Core/VeloxDev.Core.Extension.Test/Agent/SubAgents/`（8 个 .cs，7 个 `[TestClass]`）。
> 不依赖 MAF 的 `BackgroundAgentsProvider`：那个类型构造时吃死的 `IEnumerable<AIAgent>`，**没有「按次配置子能力」这个概念**，且整体标着 `[Experimental("MAAI001")]`。

本文只写「读完这些文件才知道的东西」。

---

## 一、执行模型是**派发 + 轮询**，而这是结构决定的，不是风格

`SpawnSubAgent` 的工体返回 `Task.FromResult(...)`（`SubAgentAgentToolkit.cs:95`）—— 它**立刻返回**，孩子的运行由 `Task.Run(() => RunAsync(...))` 承接（`SubAgentScope.cs:505`）。所以「异步」这个词指的是**孩子**在后台跑，不是工体在等什么。五个工具全是 `Task<string>`（含 `SpawnSubAgent`），这个形状是**给模型看的契约**：schema 里读出来的是「派发后轮询」，不是「调用即阻塞」。

**为什么不能是同步的**：一次 spawn 发生在一次工具调用内部，而工具被 `TrackedAIFunction` 编组到**宿主的 UI 线程**上（`Agent/TrackedAIFunction.cs` 的 post + await）。同步等孩子 = 按住宿主 UI 线程走完孩子整段对话。推论：工体里**不许** `.Result` / `.Wait()`，也不许写 `ConfigureAwait(false)`。

**`WaitSubAgents` 是 WhenAll-or-timeout，不是「任一完成即返回」**（`SubAgentScope.cs:736-741`）。超时判定靠**引用比较** `!ReferenceEquals(finished, all)`：`Task.WhenAll` 与 `Task.Delay` 都成功完成，只有引用能区分谁赢了。点名的 id 一个都不存在时返回空名册且 `timedOut: false`（`:745`），不是报错 —— 理由见 §五的隔离。

---

## 二、一口锅：为什么「任意深度」是**终止**的

任意深度把两个无界性放进来了：深度无界、分身数无界。子代理若各拿一份**独立**预算，「父是超集」就只在 spawn 那一刻成立，整棵树的实际开销完全无界。所以预算是一棵**树一口锅**（`ToolCallLedger`），而额度沿路径**严格递减**：

```
有效上限 = MaxToolCalls ?? SpawnBudget          // 恒为有限值
子额度   = min(请求值 ?? 剩余, 剩余 − 1)        // SubAgentScope.cs:370
剩余     = min(有效上限 − 本层已用, 根上限 − 根已用)   //  :548-560
子额度 < 1 ⇒ 拒绝这次 spawn（不是给 0）
```

- `SpawnBudget`（默认 64，`:178`）**顶替**未设 `WithMaxToolCalls` 的父。没有它，「父剩余」对不设上限的宿主根本没有定义，递减就不成立 —— 这一行是整个终止性质的落点。
- `− 1` 不是重复计数：`SpawnSubAgent` 被归为**只读**（§六），但仍会被 `AccountAsync` 记一次，`− 1` 保证「即便那次记账没发生，界依然成立」。
- `RemainingAllowance` 还额外夹一次**根锅**（`:556-557`）：否则授权会许诺一个孩子超出整棵树能交付的量，而真正的拒绝发生在调用期，离这次撒谎的 spawn 很远。
- 推论：**深度 ≤ 根额度**，不是魔法常数，是算术。`TheRootsCap_BitesThroughASibling_NotThroughTheChildsOwnShare` 把这条写成算术断言。
- **终止 ≠ 可用**：根设 200 且没花过，就允许一条 199 层深的链。`WithSubAgentDepth` 才是让它有用的东西，宿主该两个都设 —— 这句在 `SubAgentScope` 的类注释与 `WithSubAgentDepth` 的注释里各写了一遍。

**账本的行为**（`ToolCallLedger.cs`）：`Spend` **向上**走（自增本层，再 `Outer?.Spend`），所以本层 `Usage` 是「本层 + 整个子树」；`Root` 就是 `Outer?.Root ?? this`；**根 scope 的账本 `Outer == null`，此时每个成员逐字退化成它替换掉的那三个计数器** —— 这条由 `WithoutAnySubAgents_TheAccountingIsTheScopesOwn` 钉住，是既有预算测试仍然有意义的前提。

**`ResetChain` 只向上，所以重置有刻意的不对称**：用户同意重开会话后，**已经发出的授权不会被改写**。一个把自己那份花完的孩子仍然被拒（它被拒的墙是它自己那一层，父的重置清不到），正确的回答是重新 spawn 一个 —— `TheParentsReset_ReopensTheTree_WithoutRewritingAGrantAlreadyMade` 与 `ASpentChild_ReportsUpward_RatherThanWaitingToBeUnstuck` 一起把这条钉成设计而不是遗漏。

---

## 三、窄化的两条规则，用的是**两个不同的集合**

`TrySpawn`（`:346-507`）里最容易写错的一处：授权与关停走的是两份名单，而且**必须**是两份。

| | 集合 | 出处 | 为什么 |
|---|---|---|---|
| 可被**授权**的 | `available` = 父**当前开启**的工具 − `NoChildMayHold` | `:392-395` | 名单是对模型说的，说它拿得到什么就必须真拿得到 |
| 必须被**关掉**的 | `everyName` = 父的**全部潜力**（`CreateAllTools()`，不过滤开关） | `:387-390`、`441-443` | 父自己关掉的一个工具若被孩子继承，孩子就持有了一项**父没有的能力** —— 这正是本子系统存在要防止的那一件事 |

省略 `allowedTools` = 继承父的**只读**面（`:410`）；传 `[]` = **一个工具都不给**，与省略不是一回事（`SubAgentRequest.AllowedTools` 的注释与 `SubAgentNarrowingTests` 里那条断言）。要改图的子代理必须自己点名。

**`dropped` 必定回报**，而且三类理由文案分开：超出父能力 / 父没开这个开关 / 父不允许某个 `allowedGenericCommands`（`:403-404`、`:602`、`:618`）。**`NoChildMayHold` 的三项在回报之前就被减掉**（`:534-537` 的注释就是这条）：授权清单里出现一个孩子拿不到的名字，等于让模型围绕一个不存在的能力做计划。

**无 UI 上下文 ⇒ 变更类工具全被 drop**（理由 `needs a UI synchronization context`，`:417-425`）。仓库立场是「安全在代码里执行，不在提示词散文里」（`:240` 附近的注释），所以这是结构性闸门：父与子的工体之间**唯一的串行化来源**就是 `TrackedAIFunction` 的编组，没有 UI 上下文就没有串行化，后台孩子改图是真的在和父竞态。

---

## 四、三条无条件约束，与「孩子不能给自己扩额度」

不随请求走的三条，因为它们是「背景中的孩子」这个身份本身带来的（`:413-425`、`:450-453`）：

1. `child.WithInteractionSafety(0)` —— 背景孩子弹一个 `RequestConfirmation`，要在一个**父的回合正悬在半空**的时候去问用户。
2. `child.WithToolEnabled("ResetToolCallLimit", false)` —— 一条的兜底。
3. 无 UI 上下文时只给查询工具。

`NoChildMayHold`（`:534-537`）= `{ ResetToolCallLimit, RequestConfirmation, RequestSelection }`。

**「孩子不能给自己扩额度」是本次最重要的安全性质，而且是两道互相独立的闸**：`InteractionSafety(0)` 让重置**无从提问**（`WorkflowAgentToolkit.cs` 里 `IsInteractionAllowed == false` 时直接 deny），禁用让工具**根本不被列出、且调用期仍被拒**（禁用判定排在逃生舱之前，所以禁用赢）。测试分开钉：`AChild_CannotReachTheResetTool`（开关那次）与 `AChildWithInteractionOff_CannotAskEvenIfTheSwitchWereOn`（级别那次，**并把工具放回去**以证明两半各自独立）。

**拒绝文案按「这个 scope 是不是根」分叉**（`DescribeExhaustedLimit`，`WorkflowAgentToolkit.cs:449`；两条文案在 `BudgetRefusal` `:475` / `LimitRefusal` `:485`）：根被指去 `ResetToolCallLimit`，孩子被告知**它自己扩不了、向上报告** —— 把一个孩子指去一个它不持有的工具，只会教会它重试。`AChildThatRunsOut_IsToldToReportUpward_NotToAskTheUser` 连「文案里不出现 `ResetToolCallLimit` 这个词」一起断言。

---

## 五、名册：隔离、快照与树的边

- **句柄按 scope 隔离**：`_entries` 是**每个 scope 私有**的（`:94`）。`Select` 里一个查不到的 id 被**跳过而不是拒绝**（`:697-707`），所以一个模型点名了兄弟的孩子会得到空名册，而不是窥进另一条分支。`ListSubAgents` 的描述把这条写给了模型。
- **`Snapshot` 才是跨线程读的那一份**（`:150-158`）。`Children` 是绑到宿主 UI 上的 `ObservableCollection`，而已一次 agent 调用会在框架自选的线程上渲染提示词 —— 在那里枚举 `Children` 就是在和 UI 竞态。`RefreshCallCounts()`（`:329`）在名册渲染前与等待返回前各刷一次，所以面板/模型读到的调用数是孩子**实际花掉**的，不是它上次改状态时的。
- **行不能反查父**：名册只有自己那一层，所以父名是 spawn 时写进子 scope 的 `SelfId` 的（`:243`、`:474-475`），每个孩子再拿它当自己的 `ParentId`。树因此可以**只凭行**建起来。
- **每个孩子都无条件挂一个 `SubAgentScope`，即便已经到深度上限**（`:487`）。拒绝来自 `CanSpawn`（`:246`、`:358`），不来自「没挂」—— 所以简报能诚实地对孩子说「你不能派发」，而不是对孩子存在一个它看不见的空洞。
- **`ChildBriefing` 存在的理由**（`:45-63`）：孩子的事实（深度、额度、被丢弃的请求、还能不能派发）**不能**写进孩子的 instructions，因为 instructions 是**宿主的** —— `ForClient` 写一份固定前言，宿主自定义一份就丢掉这些。所以改由 provider 每轮在孩子自己的名册旁边补。

---

## 六、与 Workflow 侧的接线（改动落在哪）

| 落点 | 位置 |
|---|---|
| `WorkflowAgentScope.SubAgents`（公开只读）/ `WithSubAgents` | `WorkflowAgentScope.cs:1372` / `:1577` |
| `_subAgentProvider` 进 `CreateContextProviders()` | `:1446`、`:1728`（在 MCP 与 Todo 之间） |
| `ParentLedger`（`internal`）→ 根/子改走哪个 toolkit 构造器 | `:1305`、`CreateToolkit()` `:1317` |
| `WithSynchronizationContext` 往下传给子系统 | `:402` |
| 账本字段 / 公开读法 | `WorkflowAgentToolkit.cs:31`、`Ledger` `:61` |
| 预算判定顺序 | `CheckBudget` `:277-314` |
| 记账 / 重置 / 用量 | `AccountAsync` `:359`、`ResetToolCallLimit` `:390`（`_ledger.ResetChain()` 在 `:423`）、`CallUsage` `:443` |
| 五个名字并入只读集合 | `BuildQueryToolNames` `:349` |

**`CheckBudget` 的顺序是有讲究的**：禁用 → 逃生舱（`ResetToolCallLimit` 无条件放行）→ **根锅** → 本层额度 → 读/写分档。根锅排在**本层之前**，因为它更硬、且是模型唯一绕不过去的墙；报错时必须指名对的那堵墙，否则模型会围绕错误的限制做推理。根 scope 上这条**跳过**（`:297`），不然同一个计数器会对同一个上限报两次。

**五个工具全部计入只读**（`:349` 把 `SubAgentAgentToolkit.ToolNames` 并进 `QueryToolNames`）：spawn/wait/cancel 既不改图也不该标脏。`ASpawnIsAQuery_AndSoIsNotChargedToTheMutationBudget` 连「读预算花光时 spawn 会被拒」一起钉住。

---

## 七、故意不继承的东西

**每个孩子拿到**：自己的 `WorkflowAgentScope`（`parent.Tree.AsAgentScope()`）、自己的 `AgentTranscript`（`WithTranscript` 每个 scope 只能设一次）、窄化后的全部能力、父的 UI 上下文、父的账本。

**一律不继承**：`WithSkills` / `WithMcps` / `WithTodoTracking` / `WithAgentModes` / `WithAutoDiscovery` —— 这些是宿主在工厂里决定的**静态形状**，不由模型每次 spawn 决定。`WithMcps` 的理由最硬：它会**无条件顶掉**宿主在共享 `McpScope` 上设的确认处理器（`WorkflowAgentScope.WithMcps` 的注释明说），N 个孩子各挂一次就各覆盖一次。自定义工具同理不可继承，另有一个具体障碍：让它们可用的提示文本是私有字段、没有读者。

**`StateKeys` 用每个实例一个 `Guid`**（`:173`），不用 `StateDiscriminator`。后者派生自 `tree.RuntimeId`，而父与子**在同一棵树上** —— 那个派生值会在唯一一对绝对不能撞的 provider 上撞。

---

## 八、树面板（`SubAgentTreeViewModel`）

| 事实 | 为什么 |
|---|---|
| 节点身份跨重建保留（`Fill` 里的 `existing` 字典） | 否则每个孩子一改状态就塌掉整棵树、丢掉用户选中项 —— 而这个图除了变化什么都没有 |
| `_watched` 在**走的时候**登记，不是事先 | 还不存在的孩子无法预先订阅；`Dispose` 要退订的是「访问过的每一个」，否则每多一层孙代理就漏一个 handler |
| 重建**合并**（`_rebuildQueued`）并编组到构造时的 `SynchronizationContext` | 同一瞬间「孩子启动 + 孩子完成 + 孙代理出现」只花一趟 |
| `Dispose` **不取消任何孩子** | 关一个面板不是对面板所展示的工作的决定；取消是 `SubAgentScope.Cancel` 或 scope 自己 `DisposeAsync` 的事 |
| **计数随节点一起走** | 这一轮修掉的真缺陷：只清 `Roots` 会让 `TotalCount` 永远停在旧值，而 `Rebuild` 在 disposed 之后早返回，**永远修不回来**。`AfterDispose_NothingRebuilds` 现在连「计数与节点一致」一起断言 |

**一条测试写法上的硬约束**：断言若放在 `ui.Send(...)` 的 lambda 里，**里面不能阻塞**（泵是单线程的）。先在测试线程 `WaitFor`，再 `Send` 进去只做读。

---

## 九、扩展点与捷径

**加第六个管理工具**：`SubAgentAgentToolkit` 加方法 + `CreateAllTools` 里 `AIFunctionFactory.Create(Xxx, ToolNames[n])` + 把名字加进 `ToolNames`（`BuildQueryToolNames` 会自动跟上，只读分类与不标脏都靠这一条）+ 在 `BuildPromptContext` 里按需说一句（它按 `CreateTools()` 的实际结果分叉，被宿主关掉的工具不会被广告）。

| 捷径（能编译，但是错的） | 为什么错 |
|---|---|
| 让 `SpawnSubAgent` 同步等孩子 | 工体跑在宿主的 UI 线程上，等于按住整个 UI 走完孩子的对话 |
| 在工体里 `.Result` / `.Wait()` / `ConfigureAwait(false)` | 前两个死锁在编组块里；第三个把 await 之后的工作挪出宿主线程 |
| 给每个孩子一份独立预算（`new ToolCallLedger(childScope)`） | 「父是超集」只在 spawn 那一刻成立，整棵树的实际开销无界 |
| 只把 `available` 里没授权的工具关掉 | 父**自己关掉**的工具于是漏给孩子 —— 孩子持有父没有的能力 |
| 靠事后关停来兑现授权清单 | 报告已经发出去了。`NoChildMayHold` 的三项必须在**回报之前**从可授权集合里减掉 |
| 把深度上限做成「到顶就不挂 `SubAgentScope`」 | 孩子于是一个字都说不出自己为什么不能派发；拒绝该来自 `CanSpawn` |
| 用 `StateDiscriminator` 当 `StateKeys` | 父与子在同一棵树上，派生值相同 ⇒ 框架在构造 agent 时抛 |
| 从别的线程枚举 `Children` | 那是绑到 UI 的集合；跨线程读 `Snapshot` |
| 在 `Dispose` 里再 `Rebuild()` 一次 | `Rebuild` 在 `_disposed` 后早返回；清单要走 `NotifyCounts()` |
| 指望 `ResetToolCallLimit` 把孩子已经花掉的份额还回去 | `ResetChain` 只向上走；重开的是会话，已发出的授权是既成事实 |

---

## 十、实测才能回答的两件事（门控测试）

`SubAgentLiveTests.cs` 读 `API_KEY_DEEPSEEK`，缺失则 `Assert.Inconclusive`（MSTest 4.0.2 下报成**已跳过**，不是失败 —— 已实测）。两条问的是离线替身**证明不了**的事：

1. `ARealModel_DispatchesAChildAtAll` —— **工具描述够不够清楚，模型会不会真的用 `SpawnSubAgent`**。离线套件已经证明「工具被调用时是对的」，所以这条红了只可能是描述的问题。
2. `ARealChild_DoesTheWork_AndReportsItBack` —— 被夹紧的孩子会不会**真的去调工具**再汇报。断言里带着 `callCount > 0`，因为「从零编一个答案」能通过任何「回复非空」的断言。

**仍然没被证明的一件事**：宿主 UI 线程在一棵树跑着的时候到底自不自由。整个轮询模型倚赖这一个假设（`TrackedAIFunction.RunOnContextAsync` 是 post + await TCS，理论上会让出），但离线替身**证明不了** —— `SingleThreadContext` 按构造是阻塞式 `Send` 的假货。要拿真 `DispatcherSynchronizationContext` 加真消息泵去验，本仓库目前没有这个环境。

**门控跳过这件事本身有一个坑，已实测**：`SubAgentLiveTests` 单跑或只跑 `Agent.SubAgents` 时，缺 key = 69 通过 + 2 跳过、全绿；但**缺 key 跑全量**时整轮会中止 —— 那**不是**这几条测试的错，是 `Examples/` 的 `AgentHelper.Install` 是 `async void`、缺 key 时抛出的异常崩掉测试宿主，本模块变长之后才把它暴露出来。复现与隔离实验写在 `memory/modules/VeloxDev.Core.Extension.Test/architecture.md` §四。
