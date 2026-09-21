# VeloxDev.Core.Extension — 子代理子系统

> 代码：`Src/Core/VeloxDev.Core.Extension/Agent/SubAgents/`（6 个 .cs，命名空间 `VeloxDev.AI.SubAgents`）。
> 账本：`Agent/Workflow/Functions/ToolCallLedger.cs`（1 个 `internal` 类型，跨子代理与 Workflow 两侧）。
> 测试：`Src/Core/VeloxDev.Core.Extension.Test/Agent/SubAgents/`（9 个 .cs，8 个 `[TestClass]`）。
> 不依赖 MAF 的 `BackgroundAgentsProvider`：那个类型构造时吃死的 `IEnumerable<AIAgent>`，**没有「按次配置子能力」这个概念**，且整体标着 `[Experimental("MAAI001")]`。

本文只写「读完这些文件才知道的东西」。

---

## 一、执行模型是**派发 + 轮询**，而这是结构决定的，不是风格

`SpawnSubAgent` 的工体返回 `Task.FromResult(...)`（`SubAgentAgentToolkit.cs:84`）—— 它**立刻返回**，孩子的运行由 `Task.Run(() => RunAsync(...))` 承接（`SubAgentScope.cs:573`）。所以「异步」这个词指的是**孩子**在后台跑，不是工体在等什么。五个工具全是 `Task<string>`（含 `SpawnSubAgent`），这个形状是**给模型看的契约**：schema 里读出来的是「派发后轮询」，不是「调用即阻塞」。

**为什么不能是同步的**：一次 spawn 发生在一次工具调用内部，而工具被 `TrackedAIFunction` 编组到**宿主的 UI 线程**上（`Agent/TrackedAIFunction.cs` 的 post + await）。同步等孩子 = 按住宿主 UI 线程走完孩子整段对话。推论：工体里**不许** `.Result` / `.Wait()`，也不许写 `ConfigureAwait(false)`。

**`WaitSubAgents` 是 WhenAll-or-timeout，不是「任一完成即返回」**（`SubAgentScope.cs:878-891`）。超时判定靠**引用比较** `!ReferenceEquals(finished, all)`（`:886`）：`Task.WhenAll` 与 `Task.Delay` 都成功完成，只有引用能区分谁赢了。点名的 id 一个都不存在时返回空名册且 `timedOut: false`（`:893`），不是报错 —— 理由见 §五的隔离。

---

## 二、一口锅：为什么「任意深度」是**终止**的

任意深度把两个无界性放进来了：深度无界、分身数无界。子代理若各拿一份**独立**预算，「父是超集」就只在 spawn 那一刻成立，整棵树的实际开销完全无界。所以预算是一棵**树一口锅**（`ToolCallLedger`），而额度沿路径**严格递减**：

```
有效上限 = MaxToolCalls ?? SpawnBudget          // 恒为有限值
子额度   = min(请求值 ?? 剩余, 剩余 − 1)        // SubAgentScope.cs:394
剩余     = min(有效上限 − 本层已用, 根上限 − 根已用)   //  :680-689
子额度 < 1 ⇒ 拒绝这次 spawn（不是给 0）
```

- `SpawnBudget`（默认 64，`:210`/`:213`）**顶替**未设 `WithMaxToolCalls` 的父。没有它，「父剩余」对不设上限的宿主根本没有定义，递减就不成立 —— 这一行是整个终止性质的落点。
- `− 1` 不是重复计数：`SpawnSubAgent` 被归为**只读**（§六），但仍会被 `AccountAsync` 记一次，`− 1` 保证「即便那次记账没发生，界依然成立」。
- `RemainingAllowance` 还额外夹一次**根锅**（`:686-687`）：否则授权会许诺一个孩子超出整棵树能交付的量，而真正的拒绝发生在调用期，离这次撒谎的 spawn 很远。
- 推论：**深度 ≤ 根额度**，不是魔法常数，是算术。`TheRootsCap_BitesThroughASibling_NotThroughTheChildsOwnShare` 把这条写成算术断言。
- **终止 ≠ 可用**：根设 200 且没花过，就允许一条 199 层深的链。`WithSubAgentDepth` 才是让它有用的东西，宿主该两个都设 —— 这句在 `SubAgentScope` 的类注释与 `WithSubAgentDepth` 的注释里各写了一遍。

**账本的行为**（`ToolCallLedger.cs`）：`Spend` **向上**走（自增本层，再 `Outer?.Spend`），所以本层 `Usage` 是「本层 + 整个子树」；`Root` 就是 `Outer?.Root ?? this`；**根 scope 的账本 `Outer == null`，此时每个成员逐字退化成它替换掉的那三个计数器** —— 这条由 `WithoutAnySubAgents_TheAccountingIsTheScopesOwn` 钉住，是既有预算测试仍然有意义的前提。

**`ResetChain` 只向上，所以重置有刻意的不对称**：用户同意重开会话后，**已经发出的授权不会被改写**。一个把自己那份花完的孩子仍然被拒（它被拒的墙是它自己那一层，父的重置清不到），正确的回答是重新 spawn 一个 —— `TheParentsReset_ReopensTheTree_WithoutRewritingAGrantAlreadyMade` 与 `ASpentChild_ReportsUpward_RatherThanWaitingToBeUnstuck` 一起把这条钉成设计而不是遗漏。

---

## 三、窄化的三条轴：工具用**名单**，技能与 MCP 用**视图**

### 3.1 工具：授权与关停走两份名单，而且**必须**是两份

`TrySpawn`（`:370-574`）里最容易写错的一处。

| | 集合 | 出处 | 为什么 |
|---|---|---|---|
| 可被**授权**的 | `available` = 父**当前开启**的工具 − `NoChildMayHold` | `:420-423` | 名单是对模型说的，说它拿得到什么就必须真拿得到 |
| 必须被**关掉**的 | `everyName` = 父的**全部潜力**（`CreateAllTools()` + 技能工具名，不过滤开关） | `:411-418`、`:485-487` | 父自己关掉的一个工具若被孩子继承，孩子就持有了一项**父没有的能力** —— 这正是本子系统存在要防止的那一件事 |

`everyName` 里那句 `AddRange(SkillAgentToolkit.ToolNames)`（`:417`）是必需的而不是补充：技能工具**不在 `CreateAllTools()` 里**（它们由技能子系统自己的 provider 贡献），不加进来，每一次授权都会把 `load_skill` 当成「本代理没有这个工具」丢掉。

省略 `allowedTools` = 继承父的**只读**面（`:440`）；传 `[]` = **一个工具都不给**，与省略不是一回事（`SubAgentRequest.AllowedTools` 的注释与 `SubAgentNarrowingTests` 里那条断言）。要改图的子代理必须自己点名。

**`dropped` 必定回报**，而且理由文案按成因分叉，**每一条都指出是哪一堵墙**：超出父能力 / 父没开这个开关 / 缺 UI 上下文（`:432-434`、`:446-454`）/ 父的读或写档位不够（`ApplyCap` `:700-715`）/ 父不在某个 `allowedGenericCommands` 的白名单里（`RequestableCommands` `:750`）。**`NoChildMayHold` 的三项在回报之前就被减掉**（`:421-423` 的注释就是这条）：授权清单里出现一个孩子拿不到的名字，等于让模型围绕一个不存在的能力做计划。

**无 UI 上下文 ⇒ 变更类工具全被 drop**（理由 `needs a UI synchronization context`，`:446-454`）。仓库立场是「安全在代码里执行，不在提示词散文里」，所以这是结构性闸门：父与子的工体之间**唯一的串行化来源**就是 `TrackedAIFunction` 的编组，没有 UI 上下文就没有串行化，后台孩子改图是真的在和父竞态。

### 3.2 技能与 MCP：名字表达不了，所以给的是**窄化视图**

技能工具与 MCP 工具**都不在 `WorkflowAgentToolkit.CreateAllTools()` 里** —— 各自由自己的 `AIContextProvider` 贡献，数据层也各自独立。所以「把某个技能关掉」对一个工具名清单是**无意义**的：清单能说的只有「这个工具在不在」，而 `load_skill` 在不在与**它能读到哪几个技能**是两件事。

于是这两条轴走的不是名单，是**视图**：父把自己的源**窄化**成一份新的子源交给孩子（`:499-513`）。

| | 接口 | 实现 | 关键性质 |
|---|---|---|---|
| 技能 | `SkillScope.CreateNarrowed(allowed)` `SkillScope.cs:370` | 新 `SkillScope`，`_narrow` 在 `Apply` **最顶部**过滤 | 过滤在入口，所以**后续 `Refresh()` 仍保持窄化**，不会某次刷新后自己长回来 |
| MCP | `McpScope.CreateGrantedView(parent, granted)` `McpScope.cs:454` | 新 `McpScope`，`IsGrantedView = true`（`:422`） | `_loadedClients` / `_loadedConfigs` **故意留空**，所以销毁视图不可能拆掉父的连接 |

**默认值刻意不对称，三条各有理由**：

| 轴 | 省略参数时 | 理由 |
|---|---|---|
| 工具 | 父的**只读**面 | 默认权限必须是最小的那一份 |
| 技能 | 父**已开启**的全部 | 技能是**知识**不是权力；四个技能工具全是只读的，所以「只读的一半」就是全部 |
| MCP | **一个都不给** | 框架**无法**把 MCP 工具归为只读，所以这一源的「只读的一半」是**空集** —— 省略就必须等于不给，要点名索取 |

**视图的边界是可执行的，不是被声明的**：孩子的 `ListSkills` 只会列出被授予的那几个（`SubAgentCapabilityGrantTests` 断言的是这个输出，不是视图自己的账），`load_skill` 点一个没被授予的名字会在**孩子内部**返回错误。

**授予 MCP 服务器 ≠ 给它开关**：`McpAgentToolkit.CreateTools()`（`:69-86`）对 `IsGrantedView` 分叉，视图只剩 `ListMcpServers` + `DescribeMcpServer`；`LoadMcpServers` / `UnloadMcpServer` / `AddMcpServer` 结构性不存在。孩子的 MCP 面因此 = 「那两个 + 被授予服务器自己已开启的工具」。

**一条容易漏的收尾**：一个被授予**零个**技能的孩子，必须连技能工具一起丢掉（`:461-469`），理由与 `dropped` 里那句话一致 —— 留着 `load_skill` 等于告诉模型它握着一个**必然失败**的工具（技能工具是只读的，所以它会从上面的「继承只读面」分支**回来**）。这就是为什么这段判定必须排在 `available` 计算**之后**。

### 3.3 自定义工具：名字够用，但**分组**才可以继承

自定义工具本来是清单能表达的，却一度根本无法继承：`WithTools(prompt, tools)` 除了注册工具，还把那段 `promptContext` 追加进一个私有的 `StringBuilder`（`WorkflowAgentScope.cs:213`），而**只有**整份骨架读它（`AppendCustomToolsSection`）。孩子不读骨架，所以工具到了、用法说明没到。

修法是让**分组**成为继承单位：`_customToolGroups`（`:68`）记下每一组的提示与工具，`GrantCustomToolsTo`（`:250`）按被授予的工具**筛组建新组**给孩子 —— 于是孩子拿到的提示恰好覆盖它持有的工具。一个被完全拒绝的组，连提示一起消失（`TheGuidanceGoesWithTheTools_NotWithTheGroupTheParentRegistered` 两边都断言）。

---

## 四、三条无条件约束，与「孩子不能给自己扩额度」

不随请求走的三条，因为它们是「背景中的孩子」这个身份本身带来的（`:446-454`、`:496-497`）：

1. `child.WithInteractionSafety(0)` —— 背景孩子弹一个 `RequestConfirmation`，要在一个**父的回合正悬在半空**的时候去问用户。
2. `child.WithToolEnabled("ResetToolCallLimit", false)` —— 一条的兜底。
3. 无 UI 上下文时只给查询工具。

`NoChildMayHold`（`:602-605`）= `{ ResetToolCallLimit, RequestConfirmation, RequestSelection }`。

**「孩子不能给自己扩额度」是本次最重要的安全性质，而且是两道互相独立的闸**：`InteractionSafety(0)` 让重置**无从提问**（`WorkflowAgentToolkit.cs` 里 `IsInteractionAllowed == false` 时直接 deny），禁用让工具**根本不被列出、且调用期仍被拒**（禁用判定排在逃生舱之前，所以禁用赢）。测试分开钉：`AChild_CannotReachTheResetTool`（开关那次）与 `AChildWithInteractionOff_CannotAskEvenIfTheSwitchWereOn`（级别那次，**并把工具放回去**以证明两半各自独立）。

**拒绝文案按「这个 scope 是不是根」分叉**（`BudgetRefusal`，`WorkflowAgentToolkit.cs:475`；两条文案在 `BudgetRefusal` `:475` / `LimitRefusal` `:485`）：根被指去 `ResetToolCallLimit`，孩子被告知**它自己扩不了、向上报告** —— 把一个孩子指去一个它不持有的工具，只会教会它重试。`AChildThatRunsOut_IsToldToReportUpward_NotToAskTheUser` 连「文案里不出现 `ResetToolCallLimit` 这个词」一起断言。

---

## 五、名册：隔离、快照与树的边

- **句柄按 scope 隔离**：`_entries` 是**每个 scope 私有**的（`:116`）。`Select` 里一个查不到的 id 被**跳过而不是拒绝**（`:842-852`），所以一个模型点名了兄弟的孩子会得到空名册，而不是窥进另一条分支。`ListSubAgents` 的描述把这条写给了模型。
- **`Snapshot` 才是跨线程读的那一份**（`:180`）。`Children` 是绑到宿主 UI 上的 `ObservableCollection`，而一次 agent 调用会在框架自选的线程上渲染提示词 —— 在那里枚举 `Children` 就是在和 UI 竞态。`RefreshCallCounts()`（`:353`）在名册渲染前与等待返回前各刷一次，所以面板/模型读到的调用数是孩子**实际花掉**的，不是它上次改状态时的。
- **行不能反查父**：名册只有自己那一层，所以父名是 spawn 时写进子 scope 的 `SelfId` 的（`:265`、`:538`），每个孩子再拿它当自己的 `ParentId`（`:555`）。树因此可以**只凭行**建起来。
- **每个孩子都无条件挂一个 `SubAgentScope`，即便已经到深度上限**（`:553`）。拒绝来自 `CanSpawn`（`:268`、`:382`），不来自「没挂」—— 所以简报能诚实地对孩子说「你不能派发」，而不是对孩子存在一个它看不见的空洞。
- **`ChildBriefing` 存在的理由**（`:70-85`）：孩子的事实（深度、额度、被丢弃的请求、还能不能派发、**被授予的技能与服务器**）**不能**写进孩子的 instructions，因为 instructions 是**宿主的** —— `ForClient` 写一份固定前言，宿主自定义一份就丢掉这些。所以改由 provider 每轮在孩子自己的名册旁边补。

---

## 六、与 Workflow 侧的接线（改动落在哪）

| 落点 | 位置 |
|---|---|
| `WorkflowAgentScope.SubAgents`（公开只读）/ `WithSubAgents` | `WorkflowAgentScope.cs:1419` / `:1624` |
| `_subAgentProvider` 进 `CreateContextProviders()` | `:1493`、`:1775`（在 MCP `:1773` 与 Todo `:1777` 之间） |
| `ParentLedger`（`internal`）→ 根/子改走哪个 toolkit 构造器 | `:1352`、`CreateToolkit()` `:1364` |
| `WithSynchronizationContext` 往下传给子系统 | `:447-449` |
| 账本字段 / 公开读法 | `WorkflowAgentToolkit.cs:31`、`Ledger` `:61` |
| 预算判定顺序 | `CheckBudget` `:277-316` |
| 记账 / 重置 / 用量 | `AccountAsync` `:359`、`ResetToolCallLimit`（`_ledger.ResetChain()` 在 `:423`）、`CallUsage` `:443` |
| 五个名字并入只读集合（与技能工具同一段） | `BuildQueryToolNames` `:332-353` |
| 自定义工具分组（可继承的前提） | `_customToolGroups` `:68`、`AppendCustomToolPrompt` `:213`、`GrantCustomToolsTo` `:250` |

**`CheckBudget` 的顺序是有讲究的**：禁用 → 逃生舱（`ResetToolCallLimit` 无条件放行）→ **根锅** → 本层额度 → 读/写分档。根锅排在**本层之前**，因为它更硬、且是模型唯一绕不过去的墙；报错时必须指名对的那堵墙，否则模型会围绕错误的限制做推理。根 scope 上这条**跳过**（`:294-296`），不然同一个计数器会对同一个上限报两次。禁用判定排在最前，所以「被关掉的工具」永远赢 —— 这正是孩子拿不到 `ResetToolCallLimit` 的第二道闸。

**五个工具全部计入只读**（`:349` 把 `SubAgentAgentToolkit.ToolNames` 并进 `QueryToolNames`）：spawn/wait/cancel 既不改图也不该标脏。`ASpawnIsAQuery_AndSoIsNotChargedToTheMutationBudget` 连「读预算花光时 spawn 会被拒」一起钉住。

---

## 七、故意不继承的东西

**每个孩子拿到**：自己的 `WorkflowAgentScope`（`parent.Tree.AsAgentScope()`）、自己的 `AgentTranscript`（`WithTranscript` 每个 scope 只能设一次）、窄化后的全部能力、父的 UI 上下文、父的账本。

**按窄化视图继承**：技能与 MCP（§三之二）。两条都走**新建一个源**而不是把父的源交出去，理由不是洁癖而是具体故障：`WithMcps` 会**无条件顶掉**宿主在共享 `McpScope` 上设的确认处理器（`WorkflowAgentScope.WithMcps:1592` 的注释明说），N 个孩子各挂一次就各覆盖一次 —— 交给孩子的是 `CreateGrantedView` 造的新 `McpScope`，父的那个从头到尾没被 `WithMcps` 碰过。

**一律不继承**：`WithTodoTracking` / `WithAgentModes` / `WithAutoDiscovery` —— 这些是宿主在工厂里决定的**静态形状**，不由模型每次 spawn 决定，也不属于「能力」。

**`StateKeys` 用每个实例一个 `Guid`**（`:195`），不用 `StateDiscriminator`。后者派生自 `tree.RuntimeId`，而父与子**在同一棵树上** —— 那个派生值会在唯一一对绝对不能撞的 provider 上撞。

---

## 八、树面板（`SubAgentTreeViewModel`）

| 事实 | 为什么 |
|---|---|
| 节点身份跨重建保留（`Fill` 里的 `existing` 字典） | 否则每个孩子一改状态就塌掉整棵树、丢掉用户选中项 —— 而这个图除了变化什么都没有 |
| `_watched` 在**走的时候**登记，不是事先 | 还不存在的孩子无法预先订阅；`Dispose` 要退订的是「访问过的每一个」，否则每多一层孙代理就漏一个 handler |
| 重建**合并**（`_rebuildQueued`）**并串行化**（`_rebuildGate`）并编组到构造时的 `SynchronizationContext` | 同一瞬间「孩子启动 + 孩子完成 + 孙代理出现」只花一趟；而 `_ui == null` 时（每个测试、任何无头宿主）`QueueRebuild` **就在抛事件的线程上同步重建**，扇出两个孩子 = 两个线程同时进 `Fill` 改同一个 `ObservableCollection` |
| `Dispose` 也走 `_rebuildGate` | 它是唯一会同时改 `Roots` / `_flat` / `_watched` 的第三处；不锁它就和正在跑的重建撞 |
| `Dispose` **不取消任何孩子** | 关一个面板不是对面板所展示的工作的决定；取消是 `SubAgentScope.Cancel` 或 scope 自己 `DisposeAsync` 的事 |
| **计数随节点一起走** | 这一轮修掉的真缺陷：只清 `Roots` 会让 `TotalCount` 永远停在旧值，而 `Rebuild` 在 disposed 之后早返回，**永远修不回来**。`AfterDispose_NothingRebuilds` 现在连「计数与节点一致」一起断言 |

**为什么 `_rebuildGate` 是必要的而不是保险**：没有 `_ui` 上下文时那条「一切都在绑定的线程上」的假设**静默退化成了「完全没有串行化」**。更糟的是那处损坏**不是在这里被观察到的** —— `Fill` 在别的线程上抛的异常来自 `Finish` 内部的 `PropertyChanged` 处理器，而 `Finish` 活在 `RunAsync` 的 `try` 里，于是它被**当成那个孩子自己的失败原因**记账（测试表现为 `CompletedCount == 0` 而 `FailedCount == 1`，一个孩子无辜变红）。

**`Finish` 里先写载荷再写状态**（`SubAgentScope.cs:804-826`）：`State` 是 `[VeloxProperty]`，赋值会触发 `PropertyChanged` → `Republish()` → `Changed`，而消费者（正是一个面板）就在这个通知上读 —— 状态先动的一瞬间，一个已完成的孩子**没有 `Result`**。三个分支（取消 / 失败 / 完成）统一按「先载荷后状态」写，`RunAsync` 里的 `StartedAt` 同理排在 `State = Running` 之前。

**一条测试写法上的硬约束**：断言若放在 `ui.Send(...)` 的 lambda 里，**里面不能阻塞**（泵是单线程的）。先在测试线程 `WaitFor`，再 `Send` 进去只做读。

### 把这块面板挂上屏（Avalonia 是第一家，`Examples/Workflow/Avalonia/Demo/Views/Workflow/WorkflowView`）

| 事实 | 为什么 |
|---|---|
| 接线点在 `SubscribeAutoScroll` / `UnsubscribeAutoScroll`（`.axaml.cs:226`、`:243`），**不是**构造器 | 这正是它与 MCP 面板（`InitializeMcp`，只跑一次）不同的地方，两条理由各自都够：① `AgentHelper.Mcp` 是属性初始化器，视图构造时就在；`AgentHelper.SubAgents` 是 `ProvideAgent` 在**读到 key、解析出 client 之后**才建的（`AgentHelper.cs:305`），视图构造时它**可能为 null**，所以只能挂载时去查、不能假定；② 换工作流会换 helper，而这一对方法是唯一跟着换的地方 —— 挂在构造器上的话，第二棵树的子代理永远进不了面板 |
| `SubAgentPanel.IsVisible` 由代码置位（`.axaml.cs:84,92,102`），XAML 里初值是 `False` | `DataContext == null` 这件事绑不出来（没有 `IsNull` 转换器），而无 key 的宿主 `SubAgents` 永远为 null |
| 换树时 `Dispose()` 树 VM 而**不**动 scope（`.axaml.cs:103`，理由见上面「`Dispose` 不取消任何孩子」那行） | 换一棵树时旧 helper 是直接丢掉的（`InitializeNetworkDemo` 不 `Uninstall`），旧 scope 的孩子仍在跑。不 Dispose 树 VM 的话，它会一直订阅一个没人看的 scope、每变一次就重建一次 |
| 节点的默认展开靠 `<Style Selector="TreeViewItem">` 上的 `{ReflectionBinding IsExpanded, Mode=TwoWay}`（`.axaml` `:25-29`） | `SubAgentTreeNodeViewModel.IsExpanded` 默认 `true`，而 `TreeViewItem.IsExpanded` 默认 `false` —— 不接上的话面板一打开全是收起的。**必须是 `ReflectionBinding`**：`Style` 里没有 `x:DataType` 作用域，编译绑定无从下手 |

**一条可复用的验证杠杆**：Avalonia demo 的 `Demo.csproj:8` 是 `AvaloniaUseCompiledBindingsByDefault=true`，于是**绑错的路径是编译错误而不是运行时静默失效** —— 实测把一个绑定名改错，报的是

```
WorkflowView.axaml(253,22): Avalonia error AVLN2000: Unable to resolve property or method of name
'RunningCountTypo' on type 'VeloxDev.AI.SubAgents.SubAgentTreeViewModel'
```

所以「Avalonia demo 构建绿了」对绑定路径的**存在性**是真证据，可以拿着当地基用。但它证明不了**渲染**：`TreeViewItem.IsExpanded` 那条样式绑定、`TreeView` + `TreeDataTemplate` 的实际排版、以及内层 `TreeView`（`MaxHeight="320"`）与外层侧栏 `ScrollViewer` 的嵌套滚动，三件都**只经过编译校验，没有视觉复核**（跑它需要 key + 真窗口）。失效时的退路是优雅的：节点渲染成收起状态，用户逐个点开，而不是崩。

---

## 九、扩展点与捷径

**加第六个管理工具**：`SubAgentAgentToolkit` 加方法 + `CreateAllTools` 里 `AIFunctionFactory.Create(Xxx, ToolNames[n])` + 把名字加进 `ToolNames`（`BuildQueryToolNames` 会自动跟上，只读分类与不标脏都靠这一条）+ 在 `BuildPromptContext` 里按需说一句（它按 `CreateTools()` 的实际结果分叉，被宿主关掉的工具不会被广告）。

**加第四条能力轴**：先问「它是不是一个工具名能表达的」。是 ⇒ 并入 `available` / `everyName` 那两份名单就够了。否 ⇒ 照 §三之二：给父的那个源加一个 `CreateNarrowed` / `CreateGrantedView`，并把「本轴省略参数时的默认值」单独定一次 —— 三条轴的默认值**刻意不同**（只读面 / 全部 / 空集），照抄任何一条都是错的。

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
| 用技能/MCP 的**工具名**去表达技能/MCP 的窄化 | 两个子系统都不在 `CreateAllTools()` 里，名字清单只能表达「工具在不在」，表达不了「它能读到哪几个」。要么给孩子一个窄化后的源，要么根本没窄化 |
| 把父的 `SkillScope` / `McpScope` 直接交给孩子 | 技能会连父的授权开关一起给出去；MCP 更硬 —— `WithMcps` 会无条件顶掉宿主在共享 scope 上设的确认处理器，孩子挂一次就覆盖一次 |
| 在 `Apply` 之后过滤技能列表（而不是最顶部） | 下一次 `Refresh()` 就把窄化长回来了。视图的过滤必须在 `Apply` 的**入口** |
| 让 `McpScope.CreateGrantedView` 沿用父的 `_loadedClients` | 销毁视图会拆掉**父的**连接。视图故意一个 client 都不持有 |
| 只在 `TrySpawn` 里按名关掉技能工具、不给窄化视图 | 关掉 `load_skill` 与「它能读到哪个技能」是两件事；而授予了零个技能时又**必须**把关掉做掉，否则模型握着一个必然失败的工具 |
| 让自定义工具继承工具名却继承父的 `_customToolPrompt` | 那是父所有分组的提示拼在一起；孩子于是被教了它没有的工具。分组才是单位 |
| 把 `SubAgentTreeViewModel` 的 `_rebuildGate` 当成优化去掉 | 无 UI 上下文时它**就是**唯一的串行化；而损坏会以「某个孩子无辜失败」的形式出现在别处 |
| 在 `Finish` 里先赋 `State` 再赋 `Result` | `State` 触发 `Republish` → `Changed`，消费者在通知上读到的是「已完成但没有结论」 |

---

## 十、实测才能回答的三件事（门控测试）

`SubAgentLiveTests.cs` 读 `API_KEY_DEEPSEEK`，缺失则 `Assert.Inconclusive`（MSTest 4.0.2 下报成**已跳过**，不是失败 —— 已实测）。三条问的是离线替身**证明不了**的事：

1. `ARealModel_DispatchesAChildAtAll` —— **工具描述够不够清楚，模型会不会真的用 `SpawnSubAgent`**。离线套件已经证明「工具被调用时是对的」，所以这条红了只可能是描述的问题。
2. `ARealChild_DoesTheWork_AndReportsItBack` —— 被夹紧的孩子会不会**真的去调工具**再汇报。断言里带着 `callCount > 0`，因为「从零编一个答案」能通过任何「回复非空」的断言。
3. `ARealModel_PassesTheNarrowingOnRatherThanIgnoringIt` —— 模型会不会**真的去填 `allowedSkills` / `allowedMcpServers`**。这是新增两条轴唯一买不到离线答案的地方：描述在人看来通顺、模型却省略参数，两条轴的默认值（技能=全部、MCP=空）就会静默生效，而离线测试全绿。**已实测通过** —— 被明确要求「只让它读这一个技能」时，模型确实传了。

**仍然没被证明的一件事**：宿主 UI 线程在一棵树跑着的时候到底自不自由。整个轮询模型倚赖这一个假设（`TrackedAIFunction.RunOnContextAsync` 是 post + await TCS，理论上会让出），但离线替身**证明不了** —— `SingleThreadContext` 按构造是阻塞式 `Send` 的假货。要拿真 `DispatcherSynchronizationContext` 加真消息泵去验，本仓库目前没有这个环境。

**门控跳过这件事本身有一个坑，已实测**：`SubAgentLiveTests` 单跑或只跑 `Agent.SubAgents` 时，缺 key = 全绿；但**缺 key 跑全量**时整轮会中止 —— 那**不是**这几条测试的错，是 `Examples/` 的 `AgentHelper.Install` 是 `async void`、缺 key 时抛出的异常崩掉测试宿主，本模块变长之后才把它暴露出来。复现与隔离实验写在 `memory/modules/VeloxDev.Core.Extension.Test/architecture.md` §四。
