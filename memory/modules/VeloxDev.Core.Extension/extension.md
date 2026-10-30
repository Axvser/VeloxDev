# VeloxDev.Core.Extension — 扩展

> 配套：`architecture.md`（分层与控制流）、`pipelines.md`、`skills.md`、`mcp.md`、`sub-agents.md`、`dashboard.md`、`native-capabilities.md`（框架自带的三条原生能力）。

---

## 一、扩展点地图

| 想加的东西 | 扩展点 | 落在哪个文件 | 是否代码强制 |
|---|---|---|---|
| 一个工作流工具 | `WorkflowAgentToolkit` 里加 `private async Task<string> Xxx(...)` + `[Description]`，在 `CreateAllTools` 里 `Add(类别, T(Xxx, 名字))` | `Agent/Workflow/Functions/WorkflowAgentToolkit.cs`（`CreateAllTools`） | 是（闸门 / 预算自动生效） |
| 一个工具类别 | `WorkflowToolCategory` 加 `[Flags]` 位 | `Agent/Workflow/Functions/WorkflowToolCategory.cs` | — |
| 一个技能（内置语料） | 放 `Resources/Workflow/{en,zh}/Skills/<Name>.md` | 不需要改 C# | — |
| 一个技能来源（外部） | 实现 `ISkillSource` | `Agent/Skills/ISkillSource.cs` 的 `ISkillSource` | — |
| 一个管线阶段 | 实现 `IAgentPipelineStage` / `Use(handler)` | `Agent/Pipelines/AgentPipeline.cs` 的 `IAgentPipelineStage`、`Use(handler)` | — |
| 一个上下文提供者 | `WorkflowAgentScope.WithContextProvider(Func<scope, AIContextProvider>)` | `Agent/Workflow/WorkflowAgentScope.cs` 的 `WithContextProvider` | 顺序固定在自带的三片之后 |
| 待办清单 / 运行模式 / 上下文压缩 | `WithTodoTracking()` / `WithAgentModes(options)` / `WithContextCompaction(cw, out)` —— **都是挂框架自带的 provider，不自己写** | `Agent/Workflow/WorkflowAgentScope.cs` 的 `WithTodoTracking` / `WithAgentModes` / `WithContextCompaction` | 见 `native-capabilities.md`；压缩那条的 MAF 命名空间是 `[Experimental]` |
| 让模型能派子代理 | `WithSubAgents(SubAgentScope)`，子系统本身用 `SubAgentScope.ForClient(client)`（或自定义工厂）造 | `Agent/Workflow/WorkflowAgentScope.cs` 的 `WithSubAgents`、`Agent/SubAgents/` | 是（窄化 + 树共享账本，见 `sub-agents.md`） |
| 一个 MCP 管理工具 | `McpAgentToolkit` 加方法 + 在 `CreateTools()` 注册 | `Agent/MCP/McpAgentToolkit.cs` 的 `CreateTools` | 是 |
| 一个 MCP 运行模式 | 枚举 + `GetRuntimeDir` + 装载分支 + 选项白名单 + 提示文案 | `Agent/MCP/McpScope.cs`（见 `mcp.md` §六） | 是 |
| 一个宿主可开关的成员 | 继承 `AgentMemberViewModel` + `ApplyToScope` | `Agent/Dashboard/AgentMemberViewModel.cs` 的 `AgentMemberViewModel`、`ApplyToScope` | 是（必须穿透到 scope） |
| 宿主提供只读工具 | `WithQueryTools(promptContext, tools…)` | `WorkflowAgentScope.WithQueryTools` | 是（计入读预算、不标脏） |
| 宿主提供任意工具 | `WithTools(promptContext, tools…)` | `WorkflowAgentScope.WithTools` | 是（计入写预算、可标脏） |
| 宿主接对话记录 | `WithTranscript(AgentTranscript)` | `WorkflowAgentScope.WithTranscript` | **只能设一次**，重复抛 `InvalidOperationException` |
| 出 OpenTelemetry 埋点 | `UseAgentTelemetry(AIAgentBuilder, 源名?, 敏感数据?)` / `WithAgentTelemetry(AIAgent, …)` | `AgentTelemetryExtensions.cs` | 库自己**不产出任何埋点**，只是把框架的接线收成一次调用；宿主仍须自己 `AddSource(源名)` 与持有导出器。默认 `EnableSensitiveData = false` |
| 写工具要人工批准 | 由权限模式决定：`Manual` 把每个非读动作都问人，`Auto` / `AutoEdit` 只问它们各自仍管的那几类 | `ToolPipeline.Confirm` → `WorkflowAgentToolkit.ConfirmMutationAsync` → `AgentPermissionPolicy.Evaluate` | **没有 handler 也拒绝**（fail-closed）。以**工具名**为键，`AllowAlways` 对该工具整会话生效 |

**逐个能力闸门，确认是「代码挡」还是「提示说」。** 本模块的立场是**代码挡**（`WorkflowAgentScope` 的「Capability gates (enforced in code, not just prompt)」小节标题就是这句话）：

| 闸门 | 默认 | 代码在哪拒 |
|---|---|---|
| `WithAllowNodeExecution(bool)` | **`false`（拒绝）** | `WithAllowNodeExecution`、内部标志 `AllowNodeExecution`。理由（`WithAllowNodeExecution` 的注释）：这些工具跑**节点的任意业务代码**，「Security is enforced here, not in prompt prose」 |
| `WithAllowedGenericCommands(params string[])` | **从不调用 = 完全禁用** | `WithAllowedGenericCommands`、判定 `IsGenericCommandAllowed`。名字的 `"Command"` 后缀可省（`WithAllowedGenericCommands`） |
| `WithSelectionHandler` / `WithConfirmationHandler` | 未设对应处理器 = 不注册 `RequestSelection` / `RequestConfirmation` | 注册处 `WorkflowAgentToolkit.CreateAllTools` 按 `_scope.SelectionHandler` / `ConfirmationHandler` 非空分叉；处理器本身由 `WithSelectionHandler` / `WithConfirmationHandler` 设置 |
| `McpScope` 的权限接线 | 两个写工具永远注册，能否执行由权限模式与规则决定 | `WorkflowAgentScope.WithMcps` 把 `PermissionCheck` 接到 `McpScope`；`McpAgentToolkit.CreateTools` 注册全部工具 |
| `WithMaxToolCalls` / `WithMaxReadToolCalls` / `WithMaxWriteToolCalls` | 无上限 | `WorkflowAgentToolkit.CheckInvocation` |
| `WithToolEnabled` / `SetToolEnabled` | 全开 | `CreateTools` 的 `.Where` **加** `CheckInvocation` 的第一条 |
| **一个子代理的额度** | 从父那里取一份**份额**，不是另开一口锅 | 授权在 `SubAgentScope.TrySpawn` 就被夹紧，执行期由 `CheckInvocation` 的根账本分支兜底 |
| 权限模式的确认闸 | `Manual` 问、`Auto` 不问（除变更可达范围那一类） | `ToolPipeline.Confirm` → `WorkflowAgentToolkit.ConfirmMutationAsync`，判定 `AgentPermissionPolicy.Evaluate`；**确认钩子本身不产生提示文本**，所以只有模式变更才 `BumpVersion()` |

**「被关掉」是两层，不是一层**（`WorkflowAgentToolkit.CheckInvocation` 的注释）：过滤只到得了工作流内置工具；`CheckInvocation` 的钩子被所有切片共享，所以一个开关能到达 MCP 与技能的工具。**只做过滤会让 `WithToolEnabled("ListSkills")` 静默无效。**

---

## 二、官方做法 vs 看着能编译、但错的捷径

| 想做的事 | 官方做法 | 捷径（能编译，但是错的） |
|---|---|---|
| 让模型用上某套工具 | 挂 provider：`scope.CreateContextProviders()` → `AIAgent` 的 `AIContextProviders` | 把工具塞进 `ChatOptions.Tools`。框架把两者**并集且不按名去重** ⇒ 模型收到两遍（`WorkflowAgentContextProvider` 的类注释） |
| 给模型加指令 | `ChatOptions.Instructions = scope.ProvideProgressiveContextPrompt()`（宿主样例 `AgentHelper.cs` 就是这么做的）。**每轮变化的那部分不用你管**：`BuildDynamicInstructions()` 渲染能力包络，由 provider 自动追加在骨架之后 | 把会变的东西也写进 `ChatOptions.Instructions`：那是构造期冻住的一份，之后 `BumpVersion()` 的各调用点都到不了它 |
| 给 Agent 加待办清单 / 运行模式 | `WithTodoTracking()` / `WithAgentModes(options)` —— **挂框架自带的** | 自己写一套 todo 工具：MAF 已经给了 `todos_*` 与配套的提示文本，自己写的还得自己维护 |
| 给 Agent 加上下文压缩 | `WithContextCompaction(窗口, 输出上限)`，两个数必须是**宿主模型的真实值** | ① 猜一个窗口大小 —— 策略拿 `窗口 - 输出上限` 当输入预算，猜错就在错误的时刻压缩；② 让签名收 `CompactionStrategy` —— 那会把宿主拖进 `[Experimental]` 命名空间 |
| 读 Agent 当前的待办 / 模式 | `scope.Todo` / `scope.AgentMode`（框架 provider 的公开访问器） | 自己再 new 一个 provider —— 拿到的是**第二本账**，与模型实际看到的那份无关 |
| 让技能、MCP 的工具受预算管 | 什么都不做：`CreateContextProviders()` 把**同一个** `ToolPipeline` 递给每个子系统 | 自己 `new ToolPipeline(...)` —— 会得到**另一本账本**，预算形同虚设 |
| 关掉一个工具 | `SetToolEnabled(name, false)` | 只在宿主侧过滤 `ProvideTools()` 的结果。模型那一侧走的是 provider 渲染的集合，过滤不掉，而且不推进 `Version` ⇒ 缓存不失效 |
| 关掉一个技能 | `SkillScope.SetEnabled(name, false)` —— 它会推 `Version`。**内置技能关掉后，提示词里会出现一份「已停用」清单**（`SkillScope.BuildWithdrawnBlock`）：骨架若已把语料冻进去，撤不回，只能告诉模型忽略那几篇 | 直写 `SkillStatusViewModel.IsEnabled` —— 绕过 `Version` 自增，缓存的提示继续送旧文本（`AgentMemberViewModel` 的类注释） |
| 加一个「只读」工具 | 把名字加进 `BuildQueryToolNames`（`WorkflowAgentToolkit.BuildQueryToolNames`） | 不登记 —— 它会计入 `MaxWriteToolCalls`，并在 `AutoMarkDirty` 打开时**把图标脏** |
| 加一个技能工具 | 加进 `SkillAgentToolkit.ToolNames` | 另起一个名字或另写一个常量集合 —— 只读分类只认 `ToolNames` 里那几个（自动并入点在 `BuildQueryToolNames` 里并入技能工具名的那段） |
| 让子代理的工具也算只读 | 加进 `SubAgentAgentToolkit.ToolNames`（已并入 `BuildQueryToolNames`） | 在 `SubAgentAgentToolkit` 里另写一份名字清单 —— 迁移到别处时会漏掉只读分类 |
| 给模型派子代理的能力 | `scope.WithSubAgents(SubAgentScope.ForClient(client))`，窄化由子系统按父的实时能力算 | 自己 `new WorkflowAgentScope(tree)` 当孩子 —— 它**不**继承父的账本、UI 上下文与开关，等于给了模型一个能力不受限的第二棵工作流（见 `sub-agents.md` §二、§三） |
| 写一个 pipeline 阶段 | 实现 `IAgentPipelineStage`，自己 `try/catch` | 靠抛异常中断 run。工具路径上它已经被 `TrackedAIFunction` 的 `catch` 变成**工具错误**，模型会以为工具失败（`AgentPipeline.PublishAsync`） |
| 在工具体里 await | 什么都不写（不要加 `ConfigureAwait(false)`） | 加上它 —— 会把 await 之后的工作挪到线程池，而连接校验、`ExecuteNodes` 的第二个节点、执行引擎都在那里跑（`TrackedAIFunction.cs` 的注释） |
| 从后台线程改绑定的集合 | 走 scope 的 `Set*`（内部编组） | 直改 —— `SkillsViewModel.Skills`、`McpStatusViewModel.Servers` 是绑到宿主 UI 的 `ObservableCollection`（`SkillStatusViewModel.cs` 的 `SkillsViewModel.skills`、`McpStatusViewModel.cs` 的 `servers`） |
| 让面板开关生效 | `ApplyToScope` 穿透 | 直接改行的 `IsEnabled` 字段不触发 `OnIsEnabledChanged`（生成器只挂在属性 setter 上） |
| 加一个复合工具 | 不加 —— 每个操作都是单个组件命令步骤 | 加一个「批量做 N 件事」的工具：会**绕过或重复提交** Core 的 undo/redo 栈（`CreateAllTools` 的「No composite/bundled tools」注释） |
| 给 MCP 自服务配审批 | 在 **scope** 上调 `WithConfirmationHandler`（`WorkflowAgentScope.WithConfirmationHandler`），工作流工具与 MCP 共用它 | 在 `McpScope` 上调 —— `WithMcps` 会**无条件顶掉**它（`WorkflowAgentScope.WithMcps` 的注释明说「A handler set directly on the MCP scope is replaced by this」） |
| 让子代理继承技能 / MCP / todo / 运行模式 | 技能与 MCP **默认就继承**（省略参数 = 父的全量）；todo 与运行模式**不继承**，这是刻意的 —— 那些是宿主在工厂里定的静态形状，不由模型每次 spawn 决定。要给孩子加，就在传给 `SubAgentScope` 的工厂里显式加 | 让子 scope 共享宿主的 `McpScope` —— `WithMcps` 会顶掉宿主设的确认处理器，N 个孩子各挂一次就各覆盖一次；交给孩子的是 `CreateGrantedView` 造的新 `McpScope`（`sub-agents.md` §3.2、§七） |
| 给子代理一把它「看得见」的工具 | 名字从父的能力里来，**并且**孩子自己那一侧真的存在它 —— 交互工具要先 `GrantInteractionTo` 转交宿主的处理器，MCP 工具要能通过视图的键开关 | 只改授权清单。`RequestConfirmation` 在孩子 scope 上没有处理器就**不会被提供**，清单于是列出两把永远调不通的工具（`sub-agents.md` §4.1） |

---

## 三、步骤清单

### A. 加一个工作流工具

1. `Agent/Workflow/Functions/WorkflowAgentToolkit.cs`：加一个 `private async Task<string> Xxx(...)`，参数上用 `[Description]` 写清每个参数；返回 `Ok(...)` / `Error(...)` 信封。
2. 同一个文件，在 `CreateAllTools` 里选一个 `WorkflowToolCategory` 并 `Add(类别, T(Xxx, nameof(Xxx)))`。**类别只影响 `ProvideTools(类别)` 的切片**；`Composite` 是保留位。`Layout` 已注册 `GetLayoutInfo` / `PreviewLayout` / `ArrangeLayout`，它们走 `Tree.Arrange`，不要在 toolkit 里另写一套布局算法。
3. **若它是只读的**：加进 `BuildQueryToolNames` 的字面量集合。这决定它计入读还是写预算，以及 `AutoMarkDirty` 打开时会不会标脏。
4. **若它会在提示里被点名**：`WorkflowAgentScope.ProvideProgressiveContextPrompt` 与 `ProvideAllContexts`；失败协议里的工具名清单在 `BuildFailureHandlingProtocol`。**另加**：闸门 / 关闭开关 / 预算上限若属于这个工具的语义，包络（`RenderEnvelope`）也要跟着说。
5. **若它属于某个能力闸门**：闸门判定在 `WorkflowAgentScope.cs`（`AllowNodeExecution` / `IsGenericCommandAllowed`），注册的条件也在这里判断，不是「注册了再拒」——见 `WorkflowAgentToolkit.CreateAllTools` 里按 handler 非空注册的那段。
6. 文档同步：`skills/veloxdev-drive-workflow-with-ai/references/tools.md` 与 `Src/Core/VeloxDev.Core.Extension/README.md`（README 里写着工具数量，`Description` 属性里也有）。
7. 测试：`VeloxDev.Core.Extension.Test` 里按 `ProvideTools().Single(t => t.Name == "…")` 拿工具（`Agent/Workflow/Functions/ToolThreadAffinityTests.cs` 是范式）。**别绕过 `ProvideTools()`** —— 它是「公开注册路径」，`WorkflowLifecycleFidelityTests` 的类注释就是为这条写的。工具调用助手现有一份共用的 `WorkflowToolInvoker.Invoke`（同目录）。
8. **若它改几何**（位置 / 尺寸 / 层级）：相对移动用 `MoveCommand(new Offset(...))` 而不是自己算绝对锚点（`node.Anchor` 的 getter 是缩放坍缩值，读回来再写回去必错），改完补 `RefreshSlotAnchors(node)` —— 否则节点卡片动了、线还停在旧端点。**详见 [geometry-tools.md](geometry-tools.md)。**

### B. 加一个内置技能

1. `Resources/Workflow/en/Skills/<Name>.md` **和** `Resources/Workflow/zh/Skills/<Name>.md`。
2. 顶部 YAML frontmatter（`name` 必须是 kebab-case，≤ 64 字符；`description` ≤ 1024）。缺失时回退到从文件名/首个标题派生，但质量差。
3. 不需要改 C#，也不需要改 csproj（`EmbeddedResource Include="Resources\**\*"` 是通配的）。
4. 要加**带资源**的技能，用文件来源（`SKILL.md` + 同目录资源），因为嵌入来源的 `ReadResource` 恒为 `null`。

### C. 加一个 pipeline 阶段

1. 选位置：`TextPipeline` 之前（看原始事件）、`SharedTools` 之后（看已经被闸门处理过的）。组合在 `WorkflowAgentScope.Pipeline`。
2. 实现 `IAgentPipelineStage`，或 `pipeline.Use((e, next, ct) => …)`。
3. 要在 UI 线程上改绑定对象就 `await PipelineDispatch.RunAsync(SynchronizationContext.Current, …)`（`Agent/Pipelines/PipelineDispatch.cs`），**不要** fire-and-forget。
4. 想被宿主看见异常就订阅 `StageFailed`。

### D. 加一个 MCP 运行模式 / 打开自服务

见 `mcp.md` §六。要点：枚举、`GetRuntimeDir`、装载分支、`EnsureKnownKeys` 白名单、`BuildPromptContext` 文案、`AddServer` 的 `[Description]`，这几处。

### E. 宿主接线（照着做的那一份）

`Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/AgentHelper.cs` 是唯一一份完整的官方接线，顺序不能换：

1. 建 scope：`tree.AsAgentScope().With…(…)`，**先**把语言、预算、闸门、子系统都配好。
2. `scope.WithTranscript(transcript)` —— **必须在 agent 存在之前**，因为 scope 是从它组合出 stage 链的（`scope.WithTranscript(helper.Transcript)`）。
3. 解析模型（`OpenAIClient` → `AsIChatClient`），随即 `scope.WithSubAgents(SubAgentScope.ForClient(client))`（`SubAgentScope.ForClient(chatClient).WithSubAgentDepth(3)` 与 `scope.WithSubAgents(...)`）。
4. `scope.ProvideProgressiveContextPrompt()` 取静态骨架文本，塞进 `ChatOptions.Instructions`。
5. `chatClient.AsAIAgent(new ChatClientAgentOptions { ChatOptions = …, AIContextProviders = scope.CreateContextProviders() })` —— **不传 tools**。
6. `agent.WithPipeline(scope.Pipeline)`。
7. **不要**再手动注册技能/MCP 的管理工具。`AgentHelper.ProvideAgent` 里「The MCP and skill management tools are no longer registered here」那段注释把这条写死了：provider 每轮贡献自己那套，再注册一遍就是每个工具两份。

**第 3 步为什么排在骨架之前**：子系统是建在聊天客户端之上的，所以客户端必须先解析出来；而挂子代理又必须早于 `CreateContextProviders()`（见下），于是这三行只能落在骨架渲染之前。顺带的好处是模型那份**静态**指令里就写着它能派子代理 —— 晚于骨架挂也能跑，但那时它只是包络每轮补的「后来才注册的工具」。

**第 3 步的文本是骨架，不是全部。** 构造之后调 `WithPermissionMode` / `WithAllowNodeExecution` / `WithMax*ToolCalls` 等，模型是通过 provider 每轮追加的**能力包络**知道的，不需要重建 agent。唯一必须重建的是 `WithPromptLanguage`。

**`WithSubAgents` 必须排在第 5 步之前**（`WorkflowAgentScope.WithSubAgents`）：子代理 provider 是在 `CreateContextProviders()` 里拼进列表的，之后挂只会得到一份没有它的 provider 列表 —— 子系统本身没坏，但模型一个派发工具都拿不到。`WithSubAgents` 自己会做三件事，宿主不用管：把子系统 `Attach` 到这个 scope（窄化要读父的**实时**能力）、把 scope 的 UI 上下文传下去（`WithSynchronizationContext` 的传播）、用 `SharedTools` 与 `Pipeline` 造 provider —— **所以每个 spawn 都像内置工具一样被计数、被闸门拦、被上报**。子系统本身**不持有聊天客户端**：`SubAgentScope.ForClient(client)` 只是把「孩子跑哪个模型」这件事包成一个工厂，自定义工厂就换模型。

**宿主还要给的两件东西**（demo 这一份都给了）：`WithSubAgentDepth(3)` —— 预算只保证**终止**，`WithMaxToolCalls(200)` 允许一条 199 层深的链，深度上限才是让它有用的那一个（`sub-agents.md` §二）；以及 `Uninstall` 里的 `DisposeAsync()` —— 在跑的孩子被取消并**等待**，因为它们的工具调用编组在宿主的 dispatcher 上，拆了树再往上 post 就是往一个不存在的泵里投递（`AgentHelper.Uninstall`）。

**骨架与技能的语料只能有一个主人。** `ProvideProgressiveContextPrompt()` 在 `Skills == null` 时会把嵌入语料一并冻进骨架；`WithSkills(...)` 之后，技能子系统发现自己「接得晚了」就**不再重复语料**，改为每轮只报「自骨架生成以来被关掉的技能」（`SkillScope.BuildWithdrawnBlock`，`WorkflowAgentScope.AttachSkillProvider`）。两种顺序都能跑，但**先 `WithSkills` 再取骨架**才是语料归子系统管的那一种。`AgentHelper.ProvideAgent` 中 `ProvideProgressiveContextPrompt` 之前的注释就是把这条写死在宿主样例里的地方。

---

## 四、联动清单（加一个工具时必须同时看的地方）

| 位置 | 为什么 |
|---|---|
| `WorkflowAgentToolkit.CreateAllTools` 的 `Add(类别, …)` | 不登记 ⇒ 工具不存在 |
| `WorkflowToolCategory` | 类别只影响切片；新增类别要确认 `All` 覆盖到它 |
| `WorkflowAgentToolkit.BuildQueryToolNames` | 决定读/写预算与标脏 |
| `SkillAgentToolkit.ToolNames`（若属于技能） | 会自动并入只读集合 |
| `SubAgentAgentToolkit.ToolNames`（子代理的那组） | 同样自动并入（`BuildQueryToolNames` 里并入子代理工具名的循环）—— 改名字要改这一处，别另起一份清单 |
| `WorkflowAgentToolkit.CreateTools` 的 `.Where(IsToolEnabled)` | 自动生效，**但前提是名字能对上**（`OrdinalIgnoreCase`） |
| `WorkflowAgentScope` 的提示文本 | 模型不知道存在就不会用 |
| `Resources/Workflow/{en,zh}/Safety/*.md` | 若新工具与安全挡位有关 |
| `skills/veloxdev-drive-workflow-with-ai/references/tools.md`、同目录 `mcp.md`、`SKILL.md` | 公开文档里的工具清单 |
| `Src/Core/VeloxDev.Core.Extension/README.md` 与 csproj 的 `Description` | 里面有会过期的工具数量 |
| `VeloxDev.Core.Extension.Test/Agent/**` | `ProvideTools()` 按名取工具；改名会让测试静默找不到（`Single` 会抛，`FirstOrDefault` 不会） |

**最容易漏的一条**：`WorkflowAgentToolkit.CreateTools` 与 `CreateAllTools` 的差别。宿主 UI 要用 `CreateAllTools`（要看得到被关掉的工具），模型要用 `CreateTools`（过滤后）。**两个都存在，选错会在两个方向各出一次事故。**

---

## 五、死扩展点与已失效的钩子

| 面 | 位置 | 实际状态 |
|---|---|---|
| `WorkflowAgentScope.BuildDynamicInstructions()` | `BuildDynamicInstructions` | **活的钩子**：渲染**能力包络**。`WorkflowAgentContextProvider.BuildRender` 是唯一调用它。包络走 provider 通道，追加在骨架之后 |
| `WorkflowAgentScope.ProvideAllContexts` | `ProvideAllContexts` 的两个重载 | **仓库内无外部调用者**；是「另一档提示模式」，宿主样例走的是渐进模式。两档都会写骨架收据 |
| `WorkflowAgentScope.WithPromptLanguage` 的漂移 | `WithPromptLanguage` | **不修**：语言变了，结构上无法用「追加」纠正（不能说一句「忘掉上面那段旧语言」）。文档把它定死为构造期首个调用；包络的措辞跟随**骨架渲染时的**语言 |
| `AgentEmbeddedResources` 的 Scripts / 单文件读取 API | 已删除（2026-10-18） | 零调用者，且 `Resources/{system}/Scripts/` 目录不存在；活的是 `ReadAllReferences` / `ReadAllSkills` / `ReadSkill` / `ListSkills` 与 `ReadSafety` |
| `McpScope.WithMcpRoot` | `WithMcpRoot` | 仓库内无调用者；`.evn/mcp` 是唯一用到的根 |
| `McpScope.WithSelfService` | 已删除（2026-10-11） | **随阶梯一起删除** —— 两个写工具永远注册，能否执行由权限模式与宿主规则决定；`AddMcpServer` 的完整路径仍然只有测试在跑 |
| `AgentDashboardViewModel` | `Agent/Dashboard/AgentDashboardViewModel.cs` | 有测试、**无 UI 消费者**；三个 demo 绑的是 `McpScope.Status` |
| `WorkflowToolCategory.Layout` | `Agent/Workflow/Functions/WorkflowToolCategory.cs` | 已注册 `GetLayoutInfo` / `PreviewLayout` / `ArrangeLayout`，使用树 helper 的 solver |
| `WorkflowToolCategory.Composite` | 同上 | 保留位，无工具注册 |
| `WithInteractionSafetyPrompt(level, body)` 的覆盖段 | 已删除（与 `WithInteractionSafety` 一起） | 交互工具改为**按处理器是否存在注册**；模式的附加文本走 `WithModePrompt`，落在 `BuildInteractionSafetyPrompt` 里 |
| `SubAgentScope.WithSubAgentDepth` | `Agent/SubAgents/SubAgentScope.cs` 的 `WithSubAgentDepth` | **不是死代码，但没有它「任意深度」会很难看**：预算已经保证树终止，深度上限才是让它可用的东西。宿主该两个都设（类注释里写死了这句） |
