# VeloxDev.Core.Extension — 子代理子系统

> 代码：`Src/Core/VeloxDev.Core.Extension/Agent/SubAgents/`（6 个 .cs，命名空间 `VeloxDev.AI.SubAgents`）。
> 账本：`Agent/Workflow/Functions/ToolCallLedger.cs`（1 个 `internal` 类型，跨子代理与 Workflow 两侧）。
> 测试：`Src/Core/VeloxDev.Core.Extension.Test/Agent/SubAgents/`（10 个 .cs，9 个 `[TestClass]`）。
> 不依赖 MAF 的 `BackgroundAgentsProvider`：那个类型构造时吃死的 `IEnumerable<AIAgent>`，**没有「按次配置子能力」这个概念**，且整体标着 `[Experimental("MAAI001")]`。

本文只写「读完这些文件才知道的东西」。

---

## 一、执行模型是**派发 + 轮询**，而这是结构决定的，不是风格

`SpawnSubAgent` 的工体返回 `Task.FromResult(...)`（`SubAgentAgentToolkit.cs:98`）—— 它**立刻返回**，孩子的运行由 `Task.Run(() => RunAsync(...))` 承接（`SubAgentScope.cs:599`）。所以「异步」这个词指的是**孩子**在后台跑，不是工体在等什么。五个工具全是 `Task<string>`（含 `SpawnSubAgent`），这个形状是**给模型看的契约**：schema 里读出来的是「派发后轮询」，不是「调用即阻塞」。

**为什么不能是同步的**：一次 spawn 发生在一次工具调用内部，而工具被 `TrackedAIFunction` 编组到**宿主的 UI 线程**上（`Agent/TrackedAIFunction.cs` 的 post + await）。同步等孩子 = 按住宿主 UI 线程走完孩子整段对话。推论：工体里**不许** `.Result` / `.Wait()`，也不许写 `ConfigureAwait(false)`。

**`WaitSubAgents` 是 WhenAll-or-timeout，不是「任一完成即返回」**（`SubAgentScope.WaitAsync`，`SubAgentScope.cs:877-892`）。超时判定靠**引用比较** `!ReferenceEquals(finished, all)`（`:887`）：`Task.WhenAll` 与 `Task.Delay` 都成功完成，只有引用能区分谁赢了。点名的 id 一个都不存在时返回空名册且 `timedOut: false`（`:891`），不是报错 —— 理由见 §五的隔离。

---

## 二、一口锅：为什么「任意深度」是**终止**的

任意深度把两个无界性放进来了：深度无界、分身数无界。子代理若各拿一份**独立**预算，「父是超集」就只在 spawn 那一刻成立，整棵树的实际开销完全无界。所以预算是一棵**树一口锅**（`ToolCallLedger`），而额度沿路径**严格递减**：

```
有效上限 = MaxToolCalls ?? SpawnBudget          // 恒为有限值
子额度   = min(请求值 ?? 剩余, 剩余 − 1)        // SubAgentScope.cs:406
剩余     = min(有效上限 − 本层已用, 根上限 − 根已用)   //  :681-693
子额度 < 1 ⇒ 拒绝这次 spawn（不是给 0）
```

- `SpawnBudget`（默认 64，`:212`/`:222`）**顶替**未设 `WithMaxToolCalls` 的父。没有它，「父剩余」对不设上限的宿主根本没有定义，递减就不成立 —— 这一行是整个终止性质的落点。
- `− 1` 不是重复计数：`SpawnSubAgent` 被归为**只读**（§六），但仍会被 `AccountAsync` 记一次，`− 1` 保证「即便那次记账没发生，界依然成立」。
- `RemainingAllowance` 还额外夹一次**根锅**（`:688-690`）：否则授权会许诺一个孩子超出整棵树能交付的量，而真正的拒绝发生在调用期，离这次撒谎的 spawn 很远。
- 推论：**深度 ≤ 根额度**，不是魔法常数，是算术。`TheRootsCap_BitesThroughASibling_NotThroughTheChildsOwnShare` 把这条写成算术断言。
- **终止 ≠ 可用**：根设 200 且没花过，就允许一条 199 层深的链。`WithSubAgentDepth` 才是让它有用的东西，宿主该两个都设 —— 这句在 `SubAgentScope` 的类注释与 `WithSubAgentDepth` 的注释里各写了一遍。
- **这一行的 `− 1` 就是深度终止的全部来源，与那口锅的余额无关**：`ResetChain` 能把计数清零，却动不了 spawn 时已经定下的 `granted`。所以「孩子拿了重置工具」不会让树变得可以无限深（代价见 §四）。

**账本的行为**（`ToolCallLedger.cs`）：`Spend` **向上**走（自增本层，再 `Outer?.Spend`），所以本层 `Usage` 是「本层 + 整个子树」；`Root` 就是 `Outer?.Root ?? this`；**根 scope 的账本 `Outer == null`，此时每个成员逐字退化成它替换掉的那三个计数器** —— 这条由 `WithoutAnySubAgents_TheAccountingIsTheScopesOwn` 钉住，是既有预算测试仍然有意义的前提。

**`ResetChain` 只向上，所以重置有刻意的不对称**（`ToolCallLedger.cs:77-83`）：用户同意重开会话后，**已经发出的授权不会被改写**。一个把自己那份花完的孩子仍然被拒 —— 它被拒的墙是它**自己那一层**的计数，而父的重置从根出发只清得掉根及以上。`TheParentsReset_ReopensTheTree_WithoutRewritingAGrantAlreadyMade` 与 `ASpentChild_ReportsUpward_RatherThanWaitingToBeUnstuck` 一起把这条钉成设计而不是遗漏。

**但这个不对称现在有了第二条出路：孩子自己握着 `ResetToolCallLimit`**（§四）。2026-09-22 之前它两侧都被钉死，所以「重新 spawn 一个」是唯一答案；现在孩子可以自己发起重置，而 `ResetChain` 从**它**那一层往上清 —— 等于把孩子自己和它全部祖先一起清零。`AChildsReset_ReachesTheUser_AndReopensTheWholeTree` 钉的是这条（孩子调一次，整棵树 `Spent == 0`；用户对话框由一个后台子代理弹出）。**重置仍然是用户闸**（要 `IsInteractionAllowed` 且宿主的处理器在场且用户同意），所以这不是一个免费的续命，是「孩子能把问题递到用户面前」。

---

## 三、能力的三条轴：工具用**名单**，技能与 MCP 用**视图**，三条的默认都是**继承**

### 3.1 工具：授权与关停走两份名单，而且**必须**是两份

`TrySpawn`（`:382-605`）里最容易写错的一处。

| | 集合 | 出处 | 为什么 |
|---|---|---|---|
| 可被**授权**的 | `available` = 父**当前开启**的工具，**全部** | `:423-452` | 名单是对模型说的，说它拿得到什么就必须真拿得到 |
| 必须被**关掉**的 | `everyName` = 孩子**够得着的全部**（工体 + 技能工具名 + 五个子代理工具名 + MCP 工具名，不过滤开关） | `:423-450`、`:501-503` | 父自己关掉的一个工具若被孩子继承，孩子就持有了一项**父没有的能力** —— 这正是本子系统存在要防止的那一件事 |

**`everyName` 收的是「孩子够得着的」，不是「父当前贡献的」，所以它的四个来源各自带着不同的守卫**：

| 来源 | 守卫 | 为什么守卫不同 |
|---|---|---|
| `CreateAllTools()` | 无 | 父自己的工体（`:423`） |
| `SkillAgentToolkit.ToolNames` | `parent.Skills is not null`（`:429`） | 技能工具由**父**的技能源贡献，父没技能就不存在这条轴 |
| `SubAgentAgentToolkit.ToolNames` | **无**（`:439`） | 与上面恰好相反：`child.WithSubAgents(grand)`（`:579`）是**无条件**的，所以每个孩子都够得着这五个，**哪怕父自己没挂子系统**。守卫照抄技能那条是错的 |
| `parent.Mcp.LoadedTools` 的**工具名** | 无（父没挂 MCP 时取空集，`:446-449`） | 这一源的守卫又不同：MCP 工具由**孩子自己的** MCP provider 贡献（视图），与父挂没挂无关，而 `LoadedTools` 自身已经过滤掉被关掉的服务器与被关掉的单个工具 |

**这五个名字漏在 `everyName` 之外时，整条轴是反的**（2026-09-22 修，`SubAgentScope.cs:439`）：
① 省略 `allowedTools` ⇒ 关停循环看不见它们 ⇒ 全部留开，孩子**能**派发；
② 点名 `allowedTools` ⇒ 当时那段专门补的 `foreach (SubAgentAgentToolkit.ToolNames)` 把它们全关掉，孩子**不能**派发；
③ 模型点名 `SpawnSubAgent` ⇒ 不在 `available` 里 ⇒ 被当成「本代理没有这个工具」丢进 `dropped`。
合起来就是：**模型只有在「没想过自己授予了什么」时才派得动，而它一旦认真考虑并点名，就派不动了**，而且它问也问不出所以然 —— 这是「agent 不积极创建子代理 / 子代理不再创建子代理」的结构性成因，不是提示词写坏了。修法是把这五个并进 `everyName`（`:439`），于是 `:501-503` 那个循环成为**唯一**的关停点，原先那段专用循环随之删掉（它变成冗余）。五个工具在 `BuildQueryToolNames` 里已归只读，因此走继承分支 ⇒ **孩子的默认面就含这五个**，孙代理默认成立。

**MCP 名字漏在 `everyName` 之外时，症状就是「MCP 子工具被判定为失败」**（2026-09-22 修，`SubAgentScope.cs:446-449`）。原子句是「MCP 服务器默认不继承 ⇒ 孩子手上一个 MCP 工具都没有」，所以模型点名一个 MCP 工具时，它不在 `available` 里，回报的是 `{name}: not available to this agent, or switched off by the host`（`:461`）—— **而它明明是父开着、孩子也够得着的**。这条与上面那五个名字是同一个病的两个分支：**`everyName` 少收一个来源，那个来源就只能在「省略时留着、点名时被判不存在」之间二选一**。修完的两条断言是 `NamingAnMcpTool_IsNotARefusal`（点名不进 `dropped`、确实在孩子的 `McpSurfaceOf` 里、`IsToolEnabled` 为真）与 `AWhitelistThatOmitsAnMcpTool_TakesItOffTheChildsSurface`。

`everyName` 里那句 `AddRange(SkillAgentToolkit.ToolNames)`（`:429`）是必需的而不是补充：技能工具**不在 `CreateAllTools()` 里**（它们由技能子系统自己的 provider 贡献），不加进来，每一次授权都会把 `load_skill` 当成「本代理没有这个工具」丢掉。

**一条推论，`grantedTools` 会因此报出 provider 贡献的名字。** `available` 是 `everyName` 里滤掉父自己关掉的那些之后剩下的，于是继承分支的 `grantedTools` 含技能工具与五个管理工具 —— 而 `SubAgentFixture.SurfaceOf` 只读 `ProvideTools()`（工体那半边），**报的集合于是大于它**。这不是回归：技能轴早就是这样（父挂了技能时 `grantedTools` 也含四个技能工具名），只是彼时没有测试的父挂了技能。`SubAgentFixture.FullSurfaceOf`（`SubAgentDoubles.cs:395`）是那个完整的「孩子被展示的全部」，断言「报告 == 实际 hold」应当用它。

省略 `allowedTools` = 继承父**当前启用的全部**（`:463-466`），**含变更类**：`CreateNode` / `DeleteNode` / `ExecuteNode` / `ExecuteCommandOnNode` 都在里面。传 `[]` = **一个工具都不给**，与省略不是一回事（`SubAgentRequest.AllowedTools` 的注释 `SubAgentScope.cs:36` 与 `SubAgentNarrowingTests` 里那条断言）。

**2026-09-22 之前，省略的默认是「父的只读半面」**（`IsQueryOnlyTool` 过滤 + `NoChildMayHold` 减法 + 无 UI 上下文闸门，三样现在都没了）。口径改成「省略即与父同权」是用户定的（「Mcp、Skill、Tools 原样提供给子代理」，且明确选择了连结构闸一起取消）：**白名单从此是主动收窄的唯一手段**。`IsQueryOnlyTool`（`WorkflowAgentToolkit.cs`）随之删除 —— 底层 `IsQueryTool` 仍被 `BuildQueryToolNames` 用，保留。

**这一口径把「名单」变成了一份承诺，而承诺必须与孩子真的能做什么一致**：孩子拿到的每一个名字，都必须在**它自己的** scope 上存在且可调用。两条推论：
① 五个管理工具、技能工具、MCP 工具的名字都要进 `everyName`（上表），否则关停循环与授权清单对不上；
② **交互工具（`RequestSelection` / `RequestConfirmation`）需要宿主在孩子的 scope 上也有处理器才存在** —— 见 §4.1，这是本次最容易漏的一步。

**`dropped` 必定回报**，而且理由文案按成因分叉，**每一条都指出是哪一堵墙**：超出父能力 / 父没开这个开关（`:460-461`）/ 父的读或写档位不够（`ApplyCap` `:700-716`）/ 父不允许跑节点业务代码（`RequestableNodeExecution` `:730-736`）/ 父不在某个 `allowedGenericCommands` 的白名单里（`RequestableCommands` `:743-754`）/ 授予了零个技能因而连技能工具一起收走（`:475-480`）。**能给出的集合与报告出去的名字必须是同一个集合**：授权清单里出现一个孩子拿不到的名字，等于让模型围绕一个不存在的能力做计划 —— 这正是 MCP 那条缺陷（§3.1）的另一面。

**「无 UI 上下文 ⇒ 变更类工具全被 drop」这道闸门已经取消**（原 `:446-454`）。它当时有真实理由，写在这里以免下一个人以为它是被误删的：父与子的工体之间**唯一的串行化来源**是 `TrackedAIFunction` 的编组，没有 UI 上下文就没有串行化，后台孩子改图是真的在和父竞态。现在的答案是**把宿主自己的 UI 上下文转交给孩子**（`child.WithSynchronizationContext(parent.UIContext)`，`:546`）而不是收回权限 —— 于是孩子仍然串行化到同一个泵上，只是这个事实由继承表达，不再由拒绝表达。**代价**：一个没有 UI 上下文的无头宿主，其孩子现在也能改图，而它没有任何东西替它们串行化；`WithNoUIContext_TheSurfaceIsStillTheParentsOwn` 与 `TheUIContext_ChangesNothingAboutWhatIsGranted` 一起钉的就是「有没有上下文，授出面逐字相同」。

### 3.2 技能与 MCP：名字表达不了，所以给的是**视图**

技能工具与 MCP 工具**都不在 `WorkflowAgentToolkit.CreateAllTools()` 里** —— 各自由自己的 `AIContextProvider` 贡献，数据层也各自独立。所以「把某个技能关掉」对一个工具名清单是**无意义**的：清单能说的只有「这个工具在不在」，而 `load_skill` 在不在与**它能读到哪几个技能**是两件事。

于是这两条轴走的不是名单，是**视图**：父把自己的源**照原样**（或按请求收窄）包成一份新的子源交给孩子（`SubAgentScope.cs:516-536`）。

| | 接口 | 实现 | 关键性质 |
|---|---|---|---|
| 技能 | `SkillScope.CreateNarrowed(allowed)` `SkillScope.cs:370` | 新 `SkillScope`，`_narrow` 在 `Apply` **最顶部**过滤 | 过滤在入口，所以**后续 `Refresh()` 仍保持窄化**，不会某次刷新后自己长回来 |
| MCP | `McpScope.CreateGrantedView(parent, granted, grantedTools)` `McpScope.cs:468` | 新 `McpScope`，`IsGrantedView = true`（`:475`） | `_loadedClients` / `_loadedConfigs` **故意留空**，所以销毁视图不可能拆掉父的连接 |

**三条轴的默认值现在是同一个：省略参数 = 父当前启用的全部**。工具、技能、MCP 都不例外，白名单是这三条轴上唯一的收窄手段。

**2026-09-22 之前三条刻意不同（只读面 / 全部 / 空集），各自有一条当时的理由**，写在这里以免下一次有人照抄旧结论：
- 工具当时默认「父的只读半面」——理由是「默认权限必须是最小的那一份」。
- 技能当时默认「父已开启的全部」——理由是技能是**知识**不是权力。这条至今仍对，只是它不再是例外。
- MCP 当时默认「**一个都不给**」——理由是框架**无法**把 MCP 工具归为只读（`McpScope` 里那句原话），所以这一源的「只读的一半」是空集，省略就必须等于不给。**这条理由是这次要修的对象**：用户要的是原样，所以「框架分不出 MCP 工具的读/写」这件事在新口径下**不再需要被解决** —— 新口径不要求按读写分类，它只要求把父的开着的东西原样交下去。

**视图的边界是可执行的，不是被声明的**：孩子的 `ListSkills` 只会列出被授予的那几个（`SubAgentCapabilityGrantTests` 断言的是这个输出，不是视图自己的账），`load_skill` 点一个没被授予的名字会在**孩子内部**返回错误。

**授予 MCP 服务器 ≠ 给它开关**：`McpAgentToolkit.CreateTools()`（`:69-92`）对 `IsGrantedView` 分叉，视图只剩 `ListMcpServers` + `DescribeMcpServer`；`LoadMcpServers` / `UnloadMcpServer` / `AddMcpServer` 结构性不存在（`:83`、`:113`）。孩子的 MCP 面因此 = 「那两个 + 被授予服务器自己已开启的工具」。

**MCP 这一轴的「取走」发生在视图里，不在关停循环里**（`SubAgentScope.cs:497-503`）。这是「一个工具只有一个取走点」那条原则的必然结果：MCP 工具的开关键是 `server/tool`、落在 `McpScope` 上，把它的名字交给 `WorkflowAgentScope.WithToolEnabled(name, false)` 会**看起来像删除而实际什么都没删**。所以关停循环**跳过 MCP 名字**（只跳过它们），改由 `CreateGrantedView` 的第三个参数 `grantedTools` 在此源的键所在处过滤（`McpScope.cs:493-494`）。省略参数 ⇒ `grantedTools` 为 null ⇒ 不过滤（继承）；点名 ⇒ 取走没被点名的那些。`AWhitelistThatOmitsAnMcpTool_TakesItOffTheChildsSurface` 钉的是这条。

**一条容易漏的收尾**：一个被授予**零个**技能的孩子，必须连技能工具一起丢掉（`:475-480`），理由与 `dropped` 里那句话一致 —— 留着 `load_skill` 等于告诉模型它握着一个**必然失败**的工具（技能工具默认就在父的面上，所以它会从继承分支**回来**）。这就是为什么这段判定必须排在 `available` 计算**之后**。

### 3.3 自定义工具：名字够用，但**分组**才可以继承

自定义工具本来是清单能表达的，却一度根本无法继承：`WithTools(prompt, tools)`（`WorkflowAgentScope.cs:185`）除了注册工具，还把那段 `promptContext` 追加进一个私有的 `StringBuilder`（`:216`），而**只有**整份骨架读它（`AppendCustomToolsSection` `:1300`）。孩子不读骨架，所以工具到了、用法说明没到。

修法是让**分组**成为继承单位：`_customToolGroups`（`:68`）记下每一组的提示与工具，`GrantCustomToolsTo`（`:251`）按被授予的工具**筛组建新组**给孩子 —— 于是孩子拿到的提示恰好覆盖它持有的工具。一个被完全拒绝的组，连提示一起消失（`TheGuidanceGoesWithTheTools_NotWithTheGroupTheParentRegistered` 两边都断言）。

---

## 四、孩子拿到的东西与父**同权**，以及为此付掉的代价

**2026-09-22 反转。** 这里原先写的是「三条无条件约束」：`child.WithInteractionSafety(0)`、`child.WithToolEnabled("ResetToolCallLimit", false)`、无 UI 上下文时只给查询工具，以及 `NoChildMayHold`（`{ ResetToolCallLimit, RequestConfirmation, RequestSelection }`）那份减法。**这些现在都不存在了** —— 用户的口径是「Mcp、Skill、Tools 原样提供给子代理」，并明确选择连结构闸一起取消。新的不变式是：**省略 `allowedTools` ⇒ 孩子与父同权**，白名单是唯一收窄手段。

反转后**孩子确实持有 `ResetToolCallLimit`，并且调得动**（`AChild_HoldsTheResetTool` 断言开关为真、在 `SurfaceOf` 里、点名它不是一次拒绝）。

### 4.1 交互配置**必须**随授权一起转交，否则授权清单会列出不存在的工具

**这是本次唯一一个「只做减法就会造出要求明令禁止的东西」的地方，也是最容易被下一个人踩掉的坑。**

`RequestSelection` / `RequestConfirmation` **只在 `_scope.IsInteractionAllowed` 且处理器非 null 时才会被提供**（`WorkflowAgentToolkit.cs:188-195`；`IsInteractionAllowed` = `_interactionSafety > 0`，默认 1）。而孩子是**新 scope**（`parent.Tree.AsAgentScope()`，`:482`），一个全新的对象 —— 交互等级回到默认、两个处理器是 null。

于是「把这两样原样交给孩子」若只取消减法，结果是：**授权清单里出现 `RequestConfirmation`，而孩子的 scope 上这把工具不存在也永远调不通** —— 正是 `SubAgentHierarchyTests` 里那句 `A tool the model can see but never use is a trap`，也正是要求 3 要消灭的症状（把它从 MCP 换成交互工具，是同一个病）。

**所以 `TrySpawn` 里必须有一句 `parent.GrantInteractionTo(child)`**（`SubAgentScope.cs:514`；实现 `WorkflowAgentScope.cs:283-296`），它转交三样：`_interactionSafety` 等级、`_safetyPromptOverrides` 覆盖表、以及 `SelectionHandler` / `ConfirmationHandler` 两个委托（后两个是**直接赋值**，不走公开的 `With…` 重载 —— 那些重载吃的是宿主面向的事件参数，再包一层只是给孩子的调用和宿主的对话框之间加一层转换）。等级必须跟着走，因为**等级 0 正是让重置工具拒绝去问的东西**：一个在等级 0 下握着重置工具的孩子，握的是「一把只会失败的工具」。

宿主的处理器就是宿主视图的对话框（Avalonia demo 在 `AgentHelper.cs:231-240` 注册，落到 `WorkflowView.axaml.cs`）。**接受这个后果**：一个后台孩子现在可以弹一个模态问题。父的回合不是阻塞的（派发 + 轮询），所以 UI 线程会响应；但那个弹窗在 UI 上**归属于无人的上下文**（它来自一个后台子代理）。

三条钉子：`AChild_InheritsTheHostsInteractionConfiguration`（等级与处理器都真的到了；等级 0 时 `RequestConfirmation` 不在孩子面上）、`AChildsReset_ReachesTheUser_AndReopensTheWholeTree`（孩子的重置走宿主的对话框，同意后整条链清零）、`NamingAnMcpTool_IsNotARefusal`（同一形状的另一条轴）。

**同一个坑还有文字那一半，而且它躲得更久。** 标准文本里一直有一句 `It cannot ask you questions`（`SubAgentAgentToolkit.cs:86` 的 `task` 参数描述、`:265` 的骨架句）—— 在 `GrantInteractionTo` 落地之前它是**真的**，落地之后它就成了 §4.1 那个病的散文版：工具已经挂在孩子面上，而提示词还在说它没有。这类句子的危险在于**没有测试会红**（`TheStandingText_MakesDelegationARule_NotAPermission` 钉的是强制句与例外句，不是这一句），而模型会据此写出一条更短的指令。改后的说法保留了唯一仍然成立、且对模型有行动意义的那一半：**它问不到「你」**（除了最终回复，它什么都不送到父那里），所以指令必须自足；顺带说明它**可以**直接问用户。⇒ **往标准文本里加一句关于孩子能力的话之前，先看 `GrantInteractionTo` 转交了哪些东西。**

### 4.2 孩子握着重置工具的代价：重置是**用户的**闸，不是孩子的免费续命

`ResetToolCallLimit` 的语义没变（`WorkflowAgentToolkit.cs:277-319` 的 `CheckBudget` 里，禁用 → 逃生舱 → 根锅 → 本层额度 → 读/写分档）：没有耗尽时回 `ok` + 一句「当前没有触顶的限制」；等级 0 或处理器缺失时 `denied`；用户同意时 `ok` + `ResetChain()`。

代价是两条，**都不是「开销无界」**：

1. **孩子可以把问题递到用户面前**。`ResetChain` 从**它**那一层往上清（`ToolCallLedger.cs:77-83`），所以孩子的一次重置 = 孩子自己 + 它全部祖先一起归零（`AChildsReset_ReachesTheUser_AndReopensTheWholeTree` 断言整棵树 `Spent == 0`）。但它**仍然要用户同意**，所以这不是免费的续命，是「孩子有了一个通向用户的出口」—— 与根自己走的是同一个出口，只是发起人不同。
2. **一个后台孩子能把一个模态弹窗放到用户面前，而那个弹窗在 UI 上没有归属的上下文**（见 §4.1 末）。这是「原样」的直接代价，不是缺陷。

另外两条**没有**被这次反转改掉，容易误读成改了：

- **深度终止完全不受影响**。终止来自 spawn 时的 `Math.Min(requested ?? remaining, remaining - 1)`（`:406`），是「沿路径严格递减」，与那口锅的余额无关；`ResetChain` 清的是计数，动不了已经发出的 `granted`（`TheParentsReset_ReopensTheTree_WithoutRewritingAGrantAlreadyMade` 里那条「重置靠下次 spawn 重新授予、不回头改写既成授权」的设计仍然成立）。
- **父子两侧的重置方向仍然不对称**：父的重置清不到孩子那一层（`ASpentChild_ReportsUpward_RatherThanWaitingToBeUnstuck` 仍然成立），而孩子自己的重置清得掉包括父在内的全部祖先。两个方向合起来才是完整的图景。

**拒绝文案**（`BudgetRefusal`，`WorkflowAgentToolkit.cs:474-481`，正文由 `LimitRefusal` `:488` 给出）：两类 scope **现在都被指去 `ResetToolCallLimit`**，因为两类都持有它，孩子的重置与父的一样能到用户面前。区别在于追加的那句职责：孩子有一个悬在它结果上的派发者，所以它被同时要求**向上报告**。`AChildThatRunsOut_IsSentToTheEscapeHatch_AndToldToReportUpward` 连「文案里出现 `ResetToolCallLimit` 这个词」一起断言 —— 老文案把一个孩子指去一个它不持有的工具，只会教会它重试，那条断言现在是它的反面。

---

## 五、名册：隔离、快照与树的边

- **句柄按 scope 隔离**：`_entries` 是**每个 scope 私有**的（`:127`）。`Select` 里一个查不到的 id 被**跳过而不是拒绝**（`:843-856`），所以一个模型点名了兄弟的孩子会得到空名册，而不是窥进另一条分支。`ListSubAgents` 的描述把这条写给了模型。
- **`Snapshot` 才是跨线程读的那一份**（`:192`）。`Children` 是绑到宿主 UI 上的 `ObservableCollection`，而一次 agent 调用会在框架自选的线程上渲染提示词 —— 在那里枚举 `Children` 就是在和 UI 竞态。`RefreshCallCounts()`（`:365`）在名册渲染前与等待返回前各刷一次，所以面板/模型读到的调用数是孩子**实际花掉**的，不是它上次改状态时的。
- **行不能反查父**：名册只有自己那一层，所以父名是 spawn 时写进子 scope 的 `SelfId` 的（`:277`、`:557`），每个孩子再拿它当自己的 `ParentId`（`:340`、`:583`）。树因此可以**只凭行**建起来。
- **每个孩子都无条件挂一个 `SubAgentScope`，即便已经到深度上限**（`:579`）。拒绝来自 `CanSpawn`（`:280`、`:394`），不来自「没挂」—— 所以简报能诚实地对孩子说「你不能派发」，而不是对孩子存在一个它看不见的空洞。
- **`ChildBriefing` 存在的理由**（`:70`）：孩子的事实（深度、额度、被丢弃的请求、还能不能派发、**被授予的技能与服务器**）**不能**写进孩子的 instructions，因为 instructions 是**宿主的** —— `ForClient` 写一份固定前言，宿主自定义一份就丢掉这些。所以改由 provider 每轮在孩子自己的名册旁边补。

### 提示词有两半，各自要回答的是不同的问题

代码：`SubAgentAgentToolkit.BuildPromptContext()`（`:238`，宿主与孩子都读）与 `AppendBriefing`（`:323`，只给孩子）。

**「你可能派发」是个坏句子，因为它回答的是模型没在问的问题。** 它只说明了没有东西禁止派发 —— 而模型本来就这么假设。它没给的是**什么时候派发更好**，缺了这个，自己做永远是最优解：多花一轮、从不出错、也不需要被推理。

**2026-09-22 改成强制式，而且这次改的是**判据**不是语气**（`:262`）。要求是「耗时长但结论短的任务（如 web 搜索）**必须**发起子代理」。第一版把判据写成「难度」——「只有一次你已经知道怎么发的调用才自己做」—— **而那个例外把规则整个吃掉了**：每一次读都是模型已经知道怎么发的调用。实测（`deepseek-v4-flash`、六章语料）的结果是它在**自己的上下文里发了六次调用、一个孩子都没派**（`SubAgentLiveTests.ARealModel_DelegatesAReadHeavyTask_WithoutBeingToldTo` 就是为此而写）。

改后的判据是**工作的目的**：`Work whose purpose is to gather material rather than to act — a web search, reading a document, surveying several files, working through a library — … must be dispatched to a sub-agent rather than done by you`，例外收窄成 `only when the entire answer is one value read off a single call and quoted as it stands`。**改完之后那条实测通过。** 所以这段话的措辞是有实测支撑的，不要凭语感把它改回去 —— `TheStandingText_MakesDelegationARule_NotAPermission` 离线钉住「must be dispatched」「web search」两处锚点，并**反向**断言 `one call you already know how to make` 不再出现。

（同一段还留着「派发之后不要自己再做一遍」，抄的是 Claude Code 自己的 `Agent` 工具描述：那里写着 "Once you've delegated a search, don't also run it yourself"。）

**`name` 这个参数的读者是**人**，这类参数得单独对待。** 2026-09-22 起它的语义是**任务标题**（不再是标识符），因为面板那一行只印它（§八）。它的特殊性在于**反馈回路断了**：模型观察到的任何东西都不会告诉它「`node-counter` 读起来不对」或「这是个句子」，而一个可省略参数被跳过是**静默**的、不是报错 —— 与上一段 `allowedMcpServers` 被静默省略是同一类失败。所以它按两处写：参数描述（`SubAgentAgentToolkit.cs:87`）+ 标准文本里的一句（`BuildPromptContext`，`Title each one with \`name\`: it is what the user reads on the panel…`）。**离线能证的只有两半**：回退是「按父编号」（`AChildWithNoTitle_FallsBackToANumberUnderItsOwnParent`）与那句话在场（`TheStandingText_AsksForATitle_BecauseThePanelShowsOne`）；「模型读了会不会真填」只有实测能答 —— `ARealModel_TitlesTheTaskItDelegates`（`:150-177`）**已通过**（`deepseek-v4-flash`，断言 `Name` 不以 `子代理 ` 开头且长度 ≤ 60）。⇒ **加载荷给一个人看的字符串，别指望模型自己学会它的语域；但没有实测就别声称它学会了。**

**孩子那一半缺的是肯定句。** `AppendBriefing` 只写了否定的一半（到深度上限时说「你不能派发，自己做」，`:348`），从没有一句说「你可以派发」。于是模型**从没被告知它可以**，而工具在不在与模型会不会去够它是两件事。现在补成对称的两句（`:347-356`）：上限 ⇒ 不能；否则若真握着 `SpawnSubAgent` ⇒ 可以，并同时说明「你派出去的从你上面那句额度里扣」。第二句的守卫是 `MayDispatch`（`:364`）= `_host.IsToolEnabled(ToolNames[0])`，与 `CreateTools()` 的过滤同一个开关，所以「不能派发的两种原因」（到顶 / 白名单没给它）不会读成同一件事 —— `AChildThatCannotDispatch_IsNotToldItCan` 与 `AChildDispatchedWithoutAWhitelist_IsToldItMayDispatchToo` 各钉一半。

**「孩子会不会在没被点名的情况下真的派发」现在是**已证**的一件事。** 离线替身仍然证明不了（`SubAgentHierarchyTests.AChildMayDispatchAChildOfItsOwn` 是被测试**直接调**孩子作用域上的 `SpawnSubAgent`，不是孩子自己想起来的），用 `DispatchInstruction` 的那几条门控实测也确实全是**被明确要求**才派的 —— `DispatchInstruction`（`SubAgentLiveTests.cs:40-43`）里写着 "by dispatching a background sub-agent to do the counting — do not count them yourself"，所以那几条证明的是「叫它派它就派」（新一轮的 `ARealModel_TitlesTheTaskItDelegates` 也走这条指令，它顺带证明的是同一趟派发里 `name` 被填了，不是「它想起来要派」）。补上的是 `ARealModel_DelegatesAReadHeavyTask_WithoutBeingToldTo`（`:179-237`）**通篇没有「派发」两个字**，只给了六章「答案埋在中间」的合成语料和一个「只要六个词」的请求，断言模型**自发**派了孩子。语料是自造的而不是借技能库的，因为**技能列表本身就带每个文档的描述**，「总结这个库」因此是一次调用就能答的题，测不出任何东西 —— 这个坑先前踩过一次。

---

## 六、与 Workflow 侧的接线（改动落在哪）

| 落点 | 位置 |
|---|---|
| `WorkflowAgentScope.SubAgents`（公开只读）/ `WithSubAgents` | `WorkflowAgentScope.cs:1452` / `:1659` |
| `_subAgentProvider` 进 `CreateContextProviders()` | `:1526`、`:1810`（在 MCP `:1807` 与 Todo `:1812` 之间） |
| `ParentLedger`（`internal`）→ 根/子改走哪个 toolkit 构造器 | `:1385`、`CreateToolkit()` `:1397` |
| `WithSynchronizationContext` 往下传给子系统 | `:475-483` |
| 交互配置转交（等级 + 覆盖表 + 两个处理器） | `GrantInteractionTo` `:283-296` |
| 账本字段 / 公开读法 | `WorkflowAgentToolkit.cs:31`、`Ledger` `:61` |
| 预算判定顺序 | `CheckBudget` `:277-319` |
| 记账 / 重置 / 用量 | `AccountAsync` `:359`、`ResetToolCallLimit`（`_ledger.ResetChain()` 在 `:423`）、`CallUsage` `:443` |
| 五个名字并入只读集合（与技能工具同一段） | `BuildQueryToolNames` `:332-352`（`:349-350`） |
| 自定义工具分组（可继承的前提） | `_customToolGroups` `:68`、`AppendCustomToolPrompt` `:213`、`GrantCustomToolsTo` `:251` |

**`CheckBudget` 的顺序是有讲究的**：禁用 → 逃生舱（`ResetToolCallLimit` 无条件放行）→ **根锅** → 本层额度 → 读/写分档。根锅排在**本层之前**，因为它更硬、且是模型唯一绕不过去的墙；报错时必须指名对的那堵墙，否则模型会围绕错误的限制做推理。根 scope 上这条**跳过**（`:296-297`），不然同一个计数器会对同一个上限报两次。禁用判定排在最前，所以「被关掉的工具」永远赢 —— 这是宿主能对孩子单独关掉一把工具的唯一入口（`SubAgentScope.cs:501-503` 那个循环），而**它不再被用来对任何一类工具做无条件关停**（`NoChildMayHold` 已删，§四）。

**五个工具全部计入只读**（`:349` 把 `SubAgentAgentToolkit.ToolNames` 并进 `QueryToolNames`）：spawn/wait/cancel 既不改图也不该标脏。`ASpawnIsAQuery_AndSoIsNotChargedToTheMutationBudget` 连「读预算花光时 spawn 会被拒」一起钉住。

---

## 七、继承什么，以及故意不继承什么

**每个孩子拿到**：自己的 `WorkflowAgentScope`（`parent.Tree.AsAgentScope()`，`SubAgentScope.cs:482`）、自己的 `AgentTranscript`（`WithTranscript` 每个 scope 只能设一次）、**父当前启用的全部能力**（除非请求点名收窄）、父的 UI 上下文（`:546`）、父的账本（`:540`）、**父的交互配置**（`GrantInteractionTo`，`:514`）、以及无条件挂上的自己的 `SubAgentScope`（`:579`）。

**按视图继承**：技能与 MCP（§3.2）。两条都走**新建一个源**而不是把父的源交出去，理由不是洁癖而是具体故障：`WithMcps` 会**无条件顶掉**宿主在共享 `McpScope` 上设的确认处理器（`WorkflowAgentScope.WithMcps:1625` 的注释明说），N 个孩子各挂一次就各覆盖一次 —— 交给孩子的是 `CreateGrantedView` 造的新 `McpScope`，父的那个从头到尾没被 `WithMcps` 碰过。**视图 ≠ 收窄**：省略参数时视图承载的就是父的全部，收窄只是它按名字取走一部分之后的样子。

**一律不继承**：`WithTodoTracking` / `WithAgentModes` / `WithAutoDiscovery` —— 这些是宿主在工厂里决定的**静态形状**，不由模型每次 spawn 决定，也不属于「能力」。

**`StateKeys` 用每个实例一个 `Guid`**（`SubAgentAgentContextProvider.cs:57` 的 `_stateKeys`），不用 `StateDiscriminator`。后者派生自 `tree.RuntimeId`，而父与子**在同一棵树上** —— 那个派生值会在唯一一对绝对不能撞的 provider 上撞。

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

**`Finish` 里先写载荷再写状态**（`SubAgentScope.cs:805`）：`State` 是 `[VeloxProperty]`，赋值会触发 `PropertyChanged` → `Republish()` → `Changed`，而消费者（正是一个面板）就在这个通知上读 —— 状态先动的一瞬间，一个已完成的孩子**没有 `Result`**。三个分支（取消 / 失败 / 完成）统一按「先载荷后状态」写，`RunAsync` 里的 `StartedAt` 同理排在 `State = Running` 之前。

**一条测试写法上的硬约束**：断言若放在 `ui.Send(...)` 的 lambda 里，**里面不能阻塞**（泵是单线程的）。先在测试线程 `WaitFor`，再 `Send` 进去只做读。

### 度量：时长、token、作用域根节点（2026-09-25 加）

| 事实 | 为什么 |
|---|---|
| **token 的接缝在 `SubAgentScope.cs:790`**：`response` 是 `AgentResponse`，`response.Usage` 是 `Microsoft.Extensions.AI.UsageDetails?`，`Finish` 收下它并写三个字段 | 那里是**唯一**拿得到用量的地方 —— 五个管理工具、`AgentTranscript`、`AgentPipeline` 的事件全都没有 token 概念（`CallUsage` 是**调用次数**，别混）。走这条路**不必动 `AgentEvent` 的公共构造器**，改动面因此只在 SubAgents 子系统内。token 只在**成功分支**写：被取消或抛异常的孩子其 response 已经无从取得，留 null 让面板显示「未计量」，而不是一个没测过的 0 |
| **MAF 1.22.0 确实把 `ChatResponse.Usage` 聚合进 `AgentResponse.Usage`**（`SubAgentMetricsTests.TokenUsage_FromTheProvider_ReachesTheRowAndTheSummary` 钉住） | 这条查文档查不到、只能实测：框架里有 `UsageAggregator` / `UsageAggregationExtensions.ApplyAggregatedUsage`，但「这条路径上到底调没调」只有一条喂了 usage 的假 client 能回答。**换 MAF 版本时先跑这条**，它红了就是面板开始静默显示空白的日子 |
| `Republish()` 原先**不投影** `StartedAt` / `FinishedAt`，所以 `Snapshot` 上根本没有时长 | 面板走 `Children` 才看得见，而 `Snapshot` 是跨线程那一份、也是五个工具那一份。加 token 时必须一并把它们补进去，否则「面板有、模型没有」 |
| 时长是**算出来的**（`FinishedAt ?? Now − StartedAt`），不是存的；`NotifyElapsed()` / `TickElapsed()` 才是通知 | 运行中的孩子没有「已完成」那一刻可言。库**故意不持有计时器**：面板会跳、进程会活，两者寿命不同，计时器属于宿主。demo 用 1 秒的 `DispatcherTimer`（`WorkflowView.axaml.cs` 的 `StartSubAgentTick`），只跟面板的挂载/卸载走 —— **不是**「有孩子在跑才走」，因为下一个孩子可能是某个正在跑的孩子派出来的，「此刻空闲」不是一个能可靠观察到并唤醒的状态 |
| **行存自身消耗，节点算子树合计**（`TokensUsed` vs `SubtreeTokens`） | 二者不能相加：父只报自身会藏起它底下的工作，父报合计则整列无法求和。面板同时印两者，`ShowSubtreeTokens` 只在**自身有值且子树更大**时为真。自下而上的顺序是构造保证的 —— `Fill` 先递归孩子、再 `RecomputeAggregates()` |
| 顶节点代表作用域（`Row == null`，`Id` 是固定的 `__scope__`），`Roots` 就是它的 `Children` | 照 agent map 的形状：最上面那个是「会话本身」，不是某个子代理。`ScopeTokens` / `ScopeTitle` 由宿主填 —— **库测不出主代理的用量**（那是宿主的对话），所以留 null 时顶节点退回去显示子树合计，而不是替宿主猜一个数。`ScopeTokens` 可以在树建好之后再设，所以它的 `partial` 钩子里**必须重算**而不只是发通知 |
| `Fill` **就地重整**（先移除离开的、再按名册顺序 `Insert` / `Move`），不再 `Clear()` | 名册在**每一行的每一次属性写入**上都重发一次，所以清空重建会把面板的容器每孩子拆装好几遍，展开状态与选中项也一并丢掉。`SubAgentMetricsTests.ARebuild_ReconcilesTheLevelInPlace` 连「重置次数为 0、新增只有一次、节点实例还是原来那个」一起断言 |
| 树 VM 里那个 `[VeloxProperty]` 字段（`tree`）**不能删** | 生成器按「类里有没有 `VeloxProperty` 成员」决定要不要注入 `OnPropertyChanged`。把 `Roots` 从字段改成 `ScopeRoot.Children` 的别名时顺手删掉它，整个类的计数通知就都编译不过 —— 而计数全是派生属性 |
| 计数面板那一行多了子树 token 合计（`SubtreeTokensText`） | 侧栏一屏只看得见几个节点，总量必须有一个不属于任何单个节点的地方 |

### 把这块面板挂上屏（Avalonia 是第一家，`Examples/Workflow/Avalonia/Demo/Views/Workflow/WorkflowView`）

| 事实 | 为什么 |
|---|---|
| 接线点在 `SubscribeAutoScroll` / `UnsubscribeAutoScroll`（`.axaml.cs:226`、`:243`，即那对方法里 `AttachSubAgents` / `DetachSubAgents` 的**调用点**），**不是**构造器 | 这正是它与 MCP 面板（`InitializeMcp`，只跑一次）不同的地方，两条理由各自都够：① `AgentHelper.Mcp` 是属性初始化器，视图构造时就在；`AgentHelper.SubAgents` 是 `ProvideAgent` 在**读到 key、解析出 client 之后**才建的（`AgentHelper.cs:306`），视图构造时它**可能为 null**，所以只能挂载时去查、不能假定；② 换工作流会换 helper，而这一对方法是唯一跟着换的地方 —— 挂在构造器上的话，第二棵树的子代理永远进不了面板 |
| `SubAgentPanel.IsVisible` 由代码置位（`.axaml.cs:84,92,102`），XAML 里初值是 `False` | `DataContext == null` 这件事绑不出来（没有 `IsNull` 转换器），而无 key 的宿主 `SubAgents` 永远为 null |
| 换树时 `Dispose()` 树 VM 而**不**动 scope（`.axaml.cs:103`，理由见上面「`Dispose` 不取消任何孩子」那行） | 换一棵树时旧 helper 是直接丢掉的（`InitializeNetworkDemo` 不 `Uninstall`），旧 scope 的孩子仍在跑。不 Dispose 树 VM 的话，它会一直订阅一个没人看的 scope、每变一次就重建一次 |
| 节点的默认展开靠 `<Style Selector="TreeViewItem">` 上的 `{ReflectionBinding IsExpanded, Mode=TwoWay}`（`.axaml` `:26-28`） | `SubAgentTreeNodeViewModel.IsExpanded` 默认 `true`，而 `TreeViewItem.IsExpanded` 默认 `false` —— 不接上的话面板一打开全是收起的。**必须是 `ReflectionBinding`**：`Style` 里没有 `x:DataType` 作用域，编译绑定无从下手 |
| 状态灯是**库外**的一个控件：`SubAgentStatusLight : Ellipse`（`Demo/Views/Workflow/SubAgentStatusLight.cs`），在节点模板里绑 `Row="{Binding Row}"`（`.axaml` `:276`） | 面板 VM 活在库侧而灯是纯视图的事，所以它属于 demo。`Row` 是 `StyledProperty` 而不是普通字段：模板带 `x:DataType`，这样 `{Binding Row}` 走编译绑定，路径错了是**编译错误**。**`Row` 现在可为 null**（顶节点是作用域），灯对此已经有正确行为：`Paint()` 取 `_watched?.State ?? Completed` ⇒ 灰、不呼吸 —— 恰好是一个「没有自己的运行可言」的节点该有的样子 |
| 树绑的是 `{Binding Tree}`（单个节点），`MaxHeight="340"` 在外层 `ScrollViewer` 上而不是 `TreeView` 上（`.axaml` `:264-265`） | `Tree` 只有一个元素 —— 作用域节点，`Roots` 是它的 `Children`。高度上限移到外层，是因为 `TreeView` 自己也有滚动条，两个嵌套的滚动区域会在同一处滚轮事件上打架 |
| 时长要有人推：`StartSubAgentTick` / `OnSubAgentTick` / `StopSubAgentTick` 挂在 `AttachSubAgents` / `DetachSubAgents` 上（`.axaml.cs`） | 与面板同生共死 —— 卸载后还留着一个 `DispatcherTimer` 就是往一个没人看的树上写属性。**库不提供计时器**（见上一节），demo 用 1 秒的 `DispatcherTimer` |
| 灯只在 `Running/Queued` 时呼吸，`Failed` 红、其余灰（含 `Cancelled`） | 呼吸动画由 `Transition<T>.Create().Property(l => l.Opacity, 1d).Effect(new TransitionEffect { Duration = 800ms, IsAutoReverse = true, LoopTime = int.MaxValue, Ease = Eases.Sine.InOut })` 一个静态声明驱动，`LoopTime = int.MaxValue` 是这套系统唯一的「永久」。**必须在 `OnDetachedFromVisualTree` 里 `Transition.Exit`**（照 `PolylineCurveView.axaml.cs:244-249` 那条先例）：永久循环不会因为控件离开可视树而停，滚走的节点会留着一条对无人可见的控件写 `Opacity` 的采样循环。`Duration` 不能为 0 —— 零长的一趟不消耗时间，永久循环会变成空转且 `Exit` 再也打断不了它 |
| 节点是**两行**：「灯 + 标题」/「时长 · tokens (· 子树 N) (+ 被拒数) (状态)」。作用域根节点的第二行整块不显示（`IsVisible="{Binding !IsScopeRoot}"`），因为它的合计已经印在表头上了 | **面板是状态视图，不是内容视图。** 上一轮它印的是四行：`Task` / `Result` / `Error` / 被拒数。而一个孩子的任务常常是一段、结果更长，于是扇出的那一刻整棵树变成每行四个折行块 —— 恰好在它开始有意思的时候不可读。2026-09-25 把第二行给了**度量**（时长、token、子树合计），因为它回答的仍是「这一行现在怎么样」而不是「它说了什么」。用者 2026-09-22 的口径是「代理树中的任何成员都不应该显示具体内容」，**失败节点也不例外**：红灯 + 「失败」就这一行，原因不显示。**`Task` / `Result` / `Error` 仍留在 `SubAgentSummary` 上**，它们从五个工具出去，要看的人去那里看 —— 从面板上拿掉的只是那个界面，不是那些数据 |
| 2026-09-25 又砍掉两样，用者的口径是「更精简美观」：**调用数**不再上屏，**状态文字只在灯说不清时才印**（`ShowStateText` = 非 `Completed`） | 调用数与 token 争同一个「代价」位置，而 token 是更好的那个；它仍在 `SubAgentSummary` / 五个工具上。状态文字同理：灰灯已经说了「完成了」，而那是最常见的状态 —— 每行再印一遍「已完成」等于把同一件事说 N 次。灯分不清的是其余几档：排队与运行都在呼吸、取消与失败都是灰的，那才是文字该出现的地方。表头的「共 N」也一并改成「N 个」，因为它和「N 次调用」用了同一个数字形状，读起来会串 |
| 选中与悬停的**强调色块要在两处同时盖**：`UserControl.Resources` 里四个 `TreeViewItemBackground*` 画刷设为 `Transparent`，再加 `Style Selector="TreeViewItem:selected"` / `:pointerover` / `:selected:pointerover` 的 `Background` | 这块面板只读，选中什么都不改变，而 Fluent 默认会画一个蓝色块 —— 深色侧栏里非常刺眼。**`SelectionMode` 没有 `None` 这一档**（只有 `Single` / `Multiple` / `Toggle` / `AlwaysSelected`），关不掉。两处都写是因为 Fluent 12 把主题编译进了二进制资源，既取不到键名也取不到模板部件名；键名不存在时只是一个没人用的资源，无害。**这两处是本模块唯一「防不住也不会报错」的改动** —— 见 §八之末的复核记录 |
| 模板只绑**节点自己的成员**（`Title` / `DurationText` / `TokensText` / `SubtreeTokensText` / `CallCount` / `StateText` / `HasDroppedRequests`），一个 `Row.*` 路径都没有 | 顶节点代表作用域、**没有行**（`Row` 是 `SubAgentStatusViewModel?`），所以任何以 `Row` 起头的路径恰好会在面板围着建的那个节点上指向空。节点把这些成员全部转发一遍（`NotifyRow`），就是为了让模板有一个不依赖 `Row` 的面。**编译绑定抓不出这种错** —— `{Binding Row.Name}` 在类型上仍然合法，只是永远取不到值 |
| 标题那一格是节点的 `Title`（= `Row?.Name ?? ScopeTitle`），而 `Name` 现在是**任务标题**不是标识符（`SubAgentScope.Describe` `:612`） | 原先印的是 id 前八位。位置已经由树本身说清楚了，所以标识符在这里花掉了一格标题的宽度却什么也没告诉看的人。省略 `name` 时回退成 `子代理 N`，**N 按父各自编号**（`Describe` 收的是 `Children.Count + 1`），不是全树的序号 —— 孙代理是它自己父的第一个孩子。标题**裁剪而不折行**：它是模型填的，行必须扛得住一个填成了句子的标题 |

**一条可复用的验证杠杆**：Avalonia demo 的 `Demo.csproj:8` 是 `AvaloniaUseCompiledBindingsByDefault=true`，于是**绑错的路径是编译错误而不是运行时静默失效** —— 实测把一个绑定名改错，报的是

```
WorkflowView.axaml(253,22): Avalonia error AVLN2000: Unable to resolve property or method of name
'RunningCountTypo' on type 'VeloxDev.AI.SubAgents.SubAgentTreeViewModel'
```

所以「Avalonia demo 构建绿了」对绑定路径的**存在性**是真证据，可以拿着当地基用。**但它只覆盖路径存在性这一件事，别的什么都不证明** —— 2026-09-22 实测：「构建 0 错误」与「产出的程序集压根没有编译后的 XAML、启动即抛」可以同时成立（见 [`VeloxDev.Avalonia/architecture.md`](../VeloxDev.Avalonia/architecture.md) §八.5）。改完 XAML 必须真启动一次，构建绿不是复核。

它也证明不了**渲染**：`TreeViewItem.IsExpanded` 那条样式绑定、`TreeView` + `TreeDataTemplate` 的实际排版、呼吸灯的观感（9px 的 `Ellipse`、`#6BFFB8` 上 `Opacity` 0.3↔1.0 的 800ms 正弦往返）、以及内层 `TreeView`（`MaxHeight="320"`）与外层侧栏 `ScrollViewer` 的嵌套滚动，都**只经过编译校验，没有视觉复核**（跑它需要 key + 真窗口）。失效时的退路是优雅的：节点渲染成收起状态、灯不动，而不是崩。
**2026-09-22 实测到的那一步**：改完这份 XAML 后 `-t:Rebuild` 0 错误，并**真的启动过一次** demo（随后连它自己的 `msedgewebview2.exe` 子进程一起收掉，见 [`VeloxDev.Avalonia/architecture.md`](../VeloxDev.Avalonia/architecture.md) §八.5、§八.6）。这一层证明的是「程序集可用」，不是「画面对」。
**同日第二次改动这份 XAML，同一步又跑了一遍**：`Demo.csproj` 先是无论增量还是 `-t:Rebuild` 都报 `AVLN9999`（文件被占），而**查不到任何 `avalonia.buildservices` 进程** —— 占着它的是上一步整解并行构建留下的 MSBuild node-reuse 节点，`dotnet build-server shutdown` 一条即解；随后 `-t:Rebuild` **20 s、0 错误**。**所以「Rebuild 绿 + 真启动」这个复核动作本身要连着「先关构建服务」一起做**，否则会误把一次 `AVLN9999` 当成「改坏了」。
**但紧接着这一步就被使用者当场证伪了，这是这一节最该记住的一笔。** 上面两次我都拿「**跑满 N 秒、日志为空**」当通过 —— 那**不是**证据：它区分不了「窗口在」和「进程刚好还没崩」，而这一轮我正是拿它把一版**启动即抛 `No precompiled XAML found for Demo.App`** 的 demo 报成「可用」，使用者一句「demo 起不来」才把它翻出来。判据换成**窗口句柄**：`Get-Process Demo | Select MainWindowHandle` 非 0 且 `Responding=True`（`MainWindowTitle` 应当读到 `Demo`；读早了会拿到 0，隔几秒再读）。⇒ **凡是「起来了吗」这类断言，判据必须是那个东西本身，不能是它的一个代理量 —— 代理量恰好也能被「坏得安静」满足。**
**同日第三次改这份 XAML，改后的复核第一次就用了新判据并一次通过**：`dotnet build-server shutdown`（这一步仍不能省，见上一段）→ `Demo.csproj -c Debug -t:Rebuild -nodeReuse:false` **40 s、0 错误、1 个既有警告**（`WorkflowAgentToolkit.cs:2050` 的 CS8602）→ 启动后读到 `MainWindowHandle=6819388` / `Responding=True` / `MainWindowTitle=Demo`，进程持续存活且日志为空。**注意这一轮没有任何一处验证是「构建绿」** —— 单行节点到底长什么样、`CharacterEllipsis` 截在哪个字上、9px 的灯在深色底上够不够醒目，仍然**只经过编译校验**。

**2026-09-25 第四次改（度量那一批，节点从一行变两行、树顶多了一个根节点），构建与启动复核一次通过**：`dotnet build-server shutdown` → `Demo.csproj -c Debug -t:Rebuild -nodeReuse:false` **0 错误、1 个既有警告** → 启动 14 s 后读到 `MainWindowHandle=1246990` / `Responding=True` / `MainWindowTitle=Demo`。

**2026-09-26 起上面那条「1 个既有警告」的基线作废**：`WorkflowAgentToolkit.cs:2050` 的 CS8602 已随 `TryGetNode` 加 `[NotNullWhen(true)]` 而消失（`netstandard2.0` 没有这个特性，靠本程序集内 `Compat/NotNullWhenAttribute.cs` 的 internal 补丁提供），同批退掉的还有 26 处 `node!` 与 2 处 `slot!`。现在 `VeloxDev.Core.Extension` 与其测试项目都是 **0 警告 0 错误** —— 所以此后读到本文任何「N 个既有警告」都是**历史观测**，不是当下基线。

**同日发现：截图是可以读回来的 —— 「视觉复核做不了」这条从前的结论作废。** 用 PowerShell 的 `System.Drawing` 抓窗口（`GetWindowRect` + `Graphics.CopyFromScreen`）存 PNG，再用 `Read` 读它，**能看清内容**（`PrintWindow` 不行：对这块 GPU 合成的窗口会返回缺元素的残帧，实测两次得到的画面都是不完整的，别用它）。这条能力的**边界**同样实测过：

| 能做 | 不能做 |
|---|---|
| 看到面板的真实排版、配色、缩进、文字截断 | **滚不动侧栏**：滚轮要落在纯 Avalonia 区域上，落在 `MarkdownView`（WebView）上会被它吃掉；实测能把侧栏滚到 MCP 面板那一屏，再往下就不动了 |
| 验证某一个样式到底有没有生效（这是它最大的价值） | **输入进不去**：`SendKeys` 与剪贴板粘贴都到不了 demo 的 `TextBox`（窗口确实是前台，`GetForegroundWindow` 已核对），所以**没法从空状态驱动出一次真实的派发** |
| 窗口可以 `SetWindowPos` 拉高，但**超过 1067（屏幕高）会被系统钳住** | 因此「面板里有子代理」那种状态只能由**人**跑出来，agent 自己复现不了 |

⇒ 结论：**样式类改动可以自己验，交互类改动不能。** 「那个蓝色选中块到底盖掉了没有」属于前者，本可以验；这一轮改完没来得及做（同一天晚些时候补做）。

**2026-09-25 第五次改（精简那一批：砍掉调用数与冗余的状态文字、把选中/悬停的强调色块盖掉），验证到什么程度**：`Demo.csproj` 构建 0 错误、`Extension.Test` **391/391 通过**（含新增的那条实时测试，真跑了 `deepseek-v4-flash`）。**没有做视觉复核**，所以下面这些**没有**被证明：那个蓝块是否真的被 `TreeViewItem` 的 `Background` 样式 / 主题资源键盖掉了（Fluent 12 的主题编译在二进制资源里，取不到键名也取不到模板部件名，**两处都写是在赌其中一个命中**，而且不命中时**不会报错**，只是继续蓝）、两行节点在侧栏宽度下会不会把第二行挤到换行、表头三个数字并排会不会溢出。⇒ 这一节里凡是形容观感的句子（「读起来像」「够不够醒目」）都是**未经复核的判断**，不是记录下来的事实。

### 一个已知缺口：新启动时面板不显示，且之后没有东西会重新挂它

**2026-09-25 实测发现**：冷启动 demo（key 已设）后把侧栏滚到底，**面板不在侧栏里** —— 不是被滚过头了，是它压根没渲染。

原因是时序，不是滚动：

- `AttachSubAgents` 的调用点只有三个，**全部在 `SubscribeAutoScroll` 里**（`.axaml.cs:257`），而 `SubscribeAutoScroll` 的三个调用点是构造器路径（经 `InitializeNetworkDemo`，`:53`/`:224`）与「从文件载入」（`:184`）。
- `InitializeNetworkDemo`（`:219-227`）会**新建一棵树**（`:222`），于是 helper 是新的、`helper.SubAgents` 在那一刻是 null —— `AgentHelper.Install` 是 `async void`，它要等读 key、建 client、`ProvideAgent` 返回之后才把 `SubAgents` 立起来。
- 所以那一次 `AttachSubAgents` 走的是 `SubAgentPanel.IsVisible = false` 那条分支（`:84`），**而此后没有任何东西会再调它一次** —— `ToolCalled` / `VisualRefreshRequested` 都不重挂面板。

⇒ **用户要再点一次「Load Workflow Demo」（或从文件载入工作流）面板才会出现**，尽管那时子代理子系统早就准备好了。使用者那张截图里的面板是这么来的。

**这是 demo 的接线缺口，不是库的缺陷** —— 但修它需要 `AgentHelper` 在 install 完成时给出一个信号（新增事件，或在 `VisualRefreshRequested` 之外补一个），而 `AgentHelper` 活在 `Examples/Workflow/Common/Lib`，是**七个平台 demo 共享**的，所以那是一次跨平台面的改动，还没做。

**对复核的含义**：agent 自己**无法**在空状态下把这个面板弄出来（面板不显示 → 滚不到 → 也没法靠输入驱动，见上一节的表）。所以「面板长什么样」这类复核，目前只能由**人**跑一次来提供。

---

## 九、扩展点与捷径

**加第六个管理工具**：`SubAgentAgentToolkit` 加方法 + `CreateAllTools` 里 `AIFunctionFactory.Create(Xxx, ToolNames[n])` + 把名字加进 `ToolNames`（`BuildQueryToolNames` 会自动跟上，只读分类与不标脏都靠这一条）+ 在 `BuildPromptContext` 里按需说一句（它按 `CreateTools()` 的实际结果分叉，被宿主关掉的工具不会被广告）。

**加第四条能力轴**：先问「它是不是一个工具名能表达的」。是 ⇒ 并入 `available` / `everyName` 那两份名单就够了 —— **但四个来源一个都不能漏**（§3.1 的表：漏一个，这条轴就在「省略时留着、点名时被判不存在」之间二选一）。否 ⇒ 照 §3.2：给父的那个源加一个 `CreateNarrowed` / `CreateGrantedView`，并把「本轴省略参数时的默认值」定成**继承父的全量** —— 三条轴现在共用这一个默认（§3.2），2026-09-22 之前那份「刻意不同」的表已经作废，别照抄。

| 捷径（能编译，但是错的） | 为什么错 |
|---|---|
| 让 `SpawnSubAgent` 同步等孩子 | 工体跑在宿主的 UI 线程上，等于按住整个 UI 走完孩子的对话 |
| 在工体里 `.Result` / `.Wait()` / `ConfigureAwait(false)` | 前两个死锁在编组块里；第三个把 await 之后的工作挪出宿主线程 |
| 给每个孩子一份独立预算（`new ToolCallLedger(childScope)`） | 「父是超集」只在 spawn 那一刻成立，整棵树的实际开销无界 |
| 只把 `available` 里没授权的工具关掉 | 父**自己关掉**的工具于是漏给孩子 —— 孩子持有父没有的能力 |
| 让授权清单与孩子**实际持有**的能力不一致（不管差在哪一边） | 报告已经发出去了，模型于是围绕一个不存在的能力做计划。旧口径下的两个实例是 `NoChildMayHold` 那三项与 MCP 名字；新口径下剩下的两个是**交互工具**（父有开关但孩子没有处理器，§4.1）与**零技能时残留的 `load_skill`**（`SubAgentScope.cs:475-480`）。共同点：清单是在 spawn 时一次性写死的，而「孩子能不能调」还取决于别人给的东西 |
| 把深度上限做成「到顶就不挂 `SubAgentScope`」 | 孩子于是一个字都说不出自己为什么不能派发；拒绝该来自 `CanSpawn` |
| 用 `StateDiscriminator` 当 `StateKeys` | 父与子在同一棵树上，派生值相同 ⇒ 框架在构造 agent 时抛 |
| 从别的线程枚举 `Children` | 那是绑到 UI 的集合；跨线程读 `Snapshot` |
| 在 `Dispose` 里再 `Rebuild()` 一次 | `Rebuild` 在 `_disposed` 后早返回；清单要走 `NotifyCounts()` |
| 把交互工具的名字放进授权清单，却不把宿主的交互配置转交过去 | `RequestSelection` / `RequestConfirmation` 只在**等级的** scope 上**有处理器**时才存在（`WorkflowAgentToolkit.cs:188-195`）。孩子是新 scope，所以清单里会多出两把**永远调不通**的工具 —— 要求 3 要消灭的正是这个症状。必须在 `TrySpawn` 里调 `parent.GrantInteractionTo(child)`（`SubAgentScope.cs:514`），把等级、覆盖表、两个处理器一起交下去（§4.1） |
| 指望 `ResetToolCallLimit` 把**父**已经花掉的份额还给孩子 | `ResetChain` 只向上走，父的重置清不到孩子那一层（`ToolCallLedger.cs:77-83`）。重开的是会话，已发出的授权是既成事实，答案是新 spawn 一个 |
| 以为孩子握着 `ResetToolCallLimit` 就等于「开销无界」 | 重置**仍然是用户闸**（等级 > 0 + 处理器在场 + 用户同意），走的和根是同一条出口。它给孩子的是一条通向用户的通道，不是一张免费续命券（§4.2） |
| 把强制委派的判据写成「这次调用难不难」 | 那个例外会被规则吞掉：**每一次读都是模型已经知道怎么发的调用**。实测（六章语料）结果是模型在自己上下文里发了六次、一个孩子都没派，而离线套件全绿。判据必须是**工作的目的**（gather material vs. act），例外收窄到「整个答案是一次调用读出的一个值」（`SubAgentAgentToolkit.cs:262`，§五之末） |
| 用技能/MCP 的**工具名**去表达技能/MCP 的窄化 | 两个子系统都不在 `CreateAllTools()` 里，名字清单只能表达「工具在不在」，表达不了「它能读到哪几个」。要么给孩子一个窄化后的源，要么根本没窄化 |
| 把父的 `SkillScope` / `McpScope` 直接交给孩子 | 技能会连父的授权开关一起给出去；MCP 更硬 —— `WithMcps` 会无条件顶掉宿主在共享 scope 上设的确认处理器，孩子挂一次就覆盖一次 |
| 在 `Apply` 之后过滤技能列表（而不是最顶部） | 下一次 `Refresh()` 就把窄化长回来了。视图的过滤必须在 `Apply` 的**入口** |
| 让 `McpScope.CreateGrantedView` 沿用父的 `_loadedClients` | 销毁视图会拆掉**父的**连接。视图故意一个 client 都不持有 |
| 只在 `TrySpawn` 里按名关掉技能工具、不给窄化视图 | 关掉 `load_skill` 与「它能读到哪个技能」是两件事；而授予了零个技能时又**必须**把关掉做掉，否则模型握着一个必然失败的工具 |
| 让自定义工具继承工具名却继承父的 `_customToolPrompt` | 那是父所有分组的提示拼在一起；孩子于是被教了它没有的工具。分组才是单位 |
| 把 `SubAgentTreeViewModel` 的 `_rebuildGate` 当成优化去掉 | 无 UI 上下文时它**就是**唯一的串行化；而损坏会以「某个孩子无辜失败」的形式出现在别处 |
| 在 `Finish` 里先赋 `State` 再赋 `Result` | `State` 触发 `Republish` → `Changed`，消费者在通知上读到的是「已完成但没有结论」 |
| 把孩子的**内容**（`Task` / `Result` / `Error`）印在树节点上 | 一个孩子的任务常常是一段、结果更长，扇出时每行退化成四个折行块 —— 恰好在它开始有意思的时候不可读。这些数据本来就从五个工具出去（§八）。面板是**状态视图**，失败也只留一行红灯 |
| 拿 id 前缀当面板那一格的字 | 位置已经由树本身说清楚了，标识符在这里花掉一格宽度却什么也没告诉看的人；那一格该是 `name`（任务标题），省略时回退成按父编号的 `子代理 N` |
| 只把 `CreateAllTools()` 的名字当 `everyName`，另外给 provider 贡献的工具名补一段「专用关停循环」 | 关停循环只关得掉它**看得见**的名字。看不见时那一段专用循环与它并存，两条路径对同一个名字给出不同答案，而这种不一致不会报错 —— 它表现为整条轴反过来：省略参数就留着、点名就全关掉（`SubAgentScope.cs:439` 的注释记了完整症状）。**MCP 那一源是同一个病的另一个分支**，只是症状不同：它被漏掉时不进 `dropped` 的是「点名即失败」（`:461`）—— 用户报的「MCP 子工具被判定为失败」就是它 |
| 照抄技能那行的 `if (parent.Skills is not null)` 去守卫子代理工具名 | 两条轴的**所有关系**刚好相反：技能工具由**父的**技能源贡献（父没有就没有），子代理工具由 `child.WithSubAgents(grand)` 无条件地挂到**孩子**身上（`:579`）。守卫照抄 ⇒ 父没挂子系统的那些孩子的名字又掉出 `everyName`，缺陷原样回来 |
| 把孩子那半提示词只写成否定的（「到深度上限就不能派发」） | 模型**从没被告知它可以**派发，而「工具在不在」与「模型会不会去够它」是两件事。同一段里「不能派发的两种原因」（到顶 / 白名单没给）也必须分开写，否则孩子分不清那是限制还是自己的 bug |
| 拿 `CallUsage` / `ToolCallLedger.Usage` 当 token 计量 | 那是**调用次数**（`(ToolCalls, ReadCalls, WriteCalls)`），与 token 没有关系。子代理子系统里原先一处 token 都没有，`grep -i "token"` 是空的 |
| 去 `AgentEvent` / `AgentPipeline` / `AgentTranscript` 上接 token，或给 `AgentTurnCompleted` 的公共构造器加参数 | 这三处都没有 token，接上去要动一个公共构造器。**接缝早就有了**：`SubAgentScope.cs:790` 那句 `var response = await agent.RunAsync(...)` 手上的就是 `AgentResponse`，`response.Usage` 直接可读 —— 改动因此完全关在 SubAgents 子系统里 |
| 给被取消 / 抛异常的孩子补一个 `TokensUsed = 0` | 那两条路径上 response 已经不存在了，0 是编的。留 null，面板据此不显示 —— `HasTokens` 存在的全部意义就是让「没测过」与「花了 0」在界面上不是一件事 |
| 在树节点模板里写 `{Binding Row.Name}` 这类路径 | 顶节点代表作用域、没有行，所以这些路径**恰好会在面板围着建的那个节点上**取不到值。而且编译绑定**不会报错**（路径在类型上合法）。节点的 `Title` / `StateText` / `CallCount` / `DurationText` / `TokensText` 就是为此转发的一层 |
| 把树 VM 里那个 `[VeloxProperty]` 字段删掉（比如把 `Roots` 改成 `ScopeRoot.Children` 的别名时顺手删） | 生成器按「类里有没有 `VeloxProperty` 成员」决定注入不注入 `OnPropertyChanged`。删掉它，`NotifyCounts()` 里的九行全部编译不过 —— 而这个类的计数**全是**派生属性，没有一条是自己会通知的 |
| 让 `SubAgentTreeViewModel` 自己起一个计时器来推动时长 | 库的寿命与面板的寿命不是一回事，这是本仓库「帧源由适配器/宿主提供」那条分工的另一处体现。而且计时器一旦起了就得管：一个没被停在 `DetachSubAgents` 的 `DispatcherTimer` 会一直往一个没人看的树上写属性 |
| 在 `Fill` 里 `level.Clear()` 之后再重建 | 名册在每一行的**每一次属性写入**上都重发，所以这是每孩子好几趟的容器拆装，连带丢掉展开状态与选中项。就地重整（先移除离开的、再按序 `Insert` / `Move`）才有 `ARebuild_ReconcilesTheLevelInPlace` 断言的那三条 |

---

## 十、实测才能回答的六件事（门控测试）

`SubAgentLiveTests.cs` 读 `API_KEY_DEEPSEEK`，缺失则 `Assert.Inconclusive`（MSTest 4.0.2 下报成**已跳过**，不是失败 —— 已实测）。六条问的是离线替身**证明不了**的事：

1. `ARealModel_DispatchesAChildAtAll`（`:47-69`）—— **工具描述够不够清楚，模型会不会真的用 `SpawnSubAgent`**。离线套件已经证明「工具被调用时是对的」，所以这条红了只可能是描述的问题。
   ⚠ **但它问的是「叫它派它就派吗」** —— 它喂的 `DispatchInstruction`（`SubAgentLiveTests.cs:40-43`）里写着 "by dispatching a background sub-agent to do the counting — do not count them yourself"。**自发派发由第 5 条回答**，见下。
2. `ARealChild_DoesTheWork_AndReportsItBack`（`:71-100`）—— 被夹紧的孩子会不会**真的去调工具**再汇报。断言里带着 `callCount > 0`，因为「从零编一个答案」能通过任何「回复非空」的断言。
3. `ARealModel_PassesTheNarrowingOnRatherThanIgnoringIt`（`:102-148`）—— 模型会不会**真的去填 `allowedSkills` / `allowedMcpServers`**。这是这两条轴唯一买不到离线答案的地方：描述在人看来通顺、模型却省略参数，而省略现在意味着**拿到父的全量**（旧口径下 MCP 那半是「拿到空集」），两种错法都静默且离线全绿。**已实测通过** —— 被明确要求「只让它读这一个技能」时，模型确实传了。
4. `ARealModel_TitlesTheTaskItDelegates`（`:150-177`）—— 模型会不会**真的去填 `name`**。与上一条同一类静默失败，但它的读者是**人**（§八的面板那一行），所以连「填得对不对」都没有反馈回路可依。断言是 `Name` 不以 `子代理 ` 开头（即回退没被触发）且长度 ≤ 60。**已实测通过**（`deepseek-v4-flash`）。
5. `ARealModel_DelegatesAReadHeavyTask_WithoutBeingToldTo`（`:179-237`）—— **要求 1 的唯一判据**：给一个「读一大堆、只要六个词」的任务，**通篇不提「派发」**，模型会不会自发地把材料读进一个孩子的上下文而不是自己的。语料是自造的六章（每章 300 行填充、中间埋一个核心词），因为技能库的列表自带描述、用它造不出这种任务。**这条是先失败后通过的**：第一版判据写成「难度」时它连跑三次都是六次调用 + 零孩子；把判据换成「工作的目的」后才过（§五之末，`SubAgentAgentToolkit.cs:262`）。失败时它会把「花了多少次调用、其中多少次是读、以及模型答了什么」一起打进消息里，这样下一个人不必重跑一遍才知道是哪种失败。
6. `ARealChild_ReportsWhatItSpent`（2026-09-25 加）—— **token 计量的另一半**。`UsageChatClient` 证明的是本仓库自己那半：response 上的 `UsageDetails` 会变成行上的数字。**它证明不了另外半**：真实 provider 到底报不报用量、MAF 到底聚不聚合到 `AgentResponse.Usage`。这条红了就说明面板的 token 那一格会**静默变空**，而换 MAF 版本正是最可能的成因。**已实测通过**（`deepseek-v4-flash`，4 秒），断言 `TokensUsed > 0` / `InputTokens > 0` / `Duration > 0`，并顺带断言同一组数字走到了树上（`node.TokensUsed == row.TokensUsed`、单孩子时 `tree.SubtreeTokens` 等于它、`ShowSubtreeTokens` 为假）。

**仍然没被证明的一件事**：宿主 UI 线程在一棵树跑着的时候到底自不自由。整个轮询模型倚赖这一个假设（`TrackedAIFunction.RunOnContextAsync` 是 post + await TCS，理论上会让出），但离线替身**证明不了** —— `SingleThreadContext` 按构造是阻塞式 `Send` 的假货。要拿真 `DispatcherSynchronizationContext` 加真消息泵去验，本仓库目前没有这个环境。

**同样没被证明的还有第 5 条的「泛化」**：它证明的是**一个**读重任务上模型自发派了，不证明它在这类任务上稳定如此。判据是三句话、语料是合成的一个形状，换一个模型或换一种「重」都会重新变成未知。这条测试的价值在于它是**唯一**能证伪那段措辞的东西，不在于它给出了措辞正确的一般证明。

**门控跳过这件事本身有一个坑，已实测**：`SubAgentLiveTests` 单跑或只跑 `Agent.SubAgents` 时，缺 key = 全绿；但**缺 key 跑全量**时整轮会中止 —— 那**不是**这几条测试的错，是 `Examples/` 的 `AgentHelper.Install` 是 `async void`、缺 key 时抛出的异常崩掉测试宿主，本模块变长之后才把它暴露出来。复现与隔离实验写在 `memory/modules/VeloxDev.Core.Extension.Test/architecture.md` §四。
