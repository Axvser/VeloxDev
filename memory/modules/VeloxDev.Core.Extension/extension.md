# VeloxDev.Core.Extension — 扩展

> 配套：`architecture.md`（分层与控制流）、`pipelines.md`、`skills.md`、`mcp.md`、`dashboard.md`。

---

## 一、扩展点地图

| 想加的东西 | 扩展点 | 落在哪个文件 | 是否代码强制 |
|---|---|---|---|
| 一个工作流工具 | `WorkflowAgentToolkit` 里加 `private async Task<string> Xxx(...)` + `[Description]`，在 `CreateAllTools` 里 `Add(类别, T(Xxx, 名字))` | `Agent/Workflow/Functions/WorkflowAgentToolkit.cs`（`CreateAllTools` 起于 `:54`） | 是（闸门 / 预算自动生效） |
| 一个工具类别 | `WorkflowToolCategory` 加 `[Flags]` 位 | `Agent/Workflow/Functions/WorkflowToolCategory.cs` | — |
| 一个技能（内置语料） | 放 `Resources/Workflow/{en,zh}/Skills/<Name>.md` | 不需要改 C# | — |
| 一个技能来源（外部） | 实现 `ISkillSource` | `Agent/Skills/ISkillSource.cs:17` | — |
| 一个管线阶段 | 实现 `IAgentPipelineStage` / `Use(handler)` | `Agent/Pipelines/AgentPipeline.cs:16`、`:62` | — |
| 一个上下文提供者 | `WorkflowAgentScope.WithContextProvider(Func<scope, AIContextProvider>)` | `Agent/Workflow/WorkflowAgentScope.cs:1499` | 顺序固定在自带的三个之后 |
| 一个 MCP 管理工具 | `McpAgentToolkit` 加方法 + 在 `CreateTools()` 注册 | `Agent/MCP/McpAgentToolkit.cs:52` | 是 |
| 一个 MCP 运行模式 | 枚举 + `GetRuntimeDir` + 装载分支 + 选项白名单 + 提示文案 | `Agent/MCP/McpScope.cs`（见 `mcp.md` §六） | 是 |
| 一个宿主可开关的成员 | 继承 `AgentMemberViewModel` + `ApplyToScope` | `Agent/Dashboard/AgentMemberViewModel.cs:18`、`:68` | 是（必须穿透到 scope） |
| 宿主提供只读工具 | `WithQueryTools(promptContext, tools…)` | `WorkflowAgentScope.cs:194` | 是（计入读预算、不标脏） |
| 宿主提供任意工具 | `WithTools(promptContext, tools…)` | `WorkflowAgentScope.cs:179` | 是（计入写预算、可标脏） |
| 宿主接对话记录 | `WithTranscript(AgentTranscript)` | `WorkflowAgentScope.cs:1437` | **只能设一次**，重复抛 `InvalidOperationException` |

**六个能力闸门，逐个确认是「代码挡」还是「提示说」。** 本模块的立场是**代码挡**（`WorkflowAgentScope.cs:227` 的小节标题就是这句话）：

| 闸门 | 默认 | 代码在哪拒 |
|---|---|---|
| `WithAllowNodeExecution(bool)` | **`false`（拒绝）** | `WorkflowAgentScope.cs:235`、内部标志 `:242`。理由（`:229-234`）：这些工具跑**节点的任意业务代码** |
| `WithAllowedGenericCommands(params string[])` | **从不调用 = 完全禁用** | `:252`、判定 `IsGenericCommandAllowed` `:263`。名字的 `"Command"` 后缀可省（`:256`） |
| `WithInteractionSafety(int level)` | `0` = 不注册 `RequestSelection`/`RequestConfirmation` | `:462`、`IsInteractionAllowed` `:568`、注册处 `WorkflowAgentToolkit.cs:161-167`。**两级条件**：级别 > 0 **且** 对应 handler 非空 |
| `McpScope.WithSelfService(level)` | `Closed` = `AddMcpServer` 不注册 | `McpScope.cs:75`、注册处 `McpAgentToolkit.cs:64` |
| `WithMaxToolCalls` / `WithMaxReadToolCalls` / `WithMaxWriteToolCalls` | 无上限 | `WorkflowAgentToolkit.CheckBudget` `:249` |
| `WithToolEnabled` / `SetToolEnabled` | 全开 | `CreateTools` 的 `.Where`（`:47`）**加** `CheckBudget` 的第一条（`:255`） |

**「被关掉」是两层，不是一层**（`WorkflowAgentToolkit.cs:251-254`）：过滤只到得了工作流内置工具；`CheckBudget` 的钩子被所有切片共享，所以一个开关能到达 MCP 与技能的工具。**只做过滤会让 `WithToolEnabled("ListSkills")` 静默无效。**

---

## 二、官方做法 vs 看着能编译、但错的捷径

| 想做的事 | 官方做法 | 捷径（能编译，但是错的） |
|---|---|---|
| 让模型用上某套工具 | 挂 provider：`scope.CreateContextProviders()` → `AIAgent` 的 `AIContextProviders` | 把工具塞进 `ChatOptions.Tools`。框架把两者**并集且不按名去重** ⇒ 模型收到两遍（`WorkflowAgentContextProvider.cs:15-19`） |
| 给模型加指令 | `ChatOptions.Instructions = scope.ProvideProgressiveContextPrompt()`（宿主样例 `AgentHelper.cs` 就是这么做的） | 指望 provider 注入。`BuildDynamicInstructions()` **恒返回 `null`**（`WorkflowAgentScope.cs:1545`），那是个预留坑位 |
| 让技能、MCP 的工具受预算管 | 什么都不做：`CreateContextProviders()` 把**同一个** `ToolPipeline` 递给每个子系统 | 自己 `new ToolPipeline(...)` —— 会得到**另一本账本**，预算形同虚设 |
| 关掉一个工具 | `SetToolEnabled(name, false)` | 只在宿主侧过滤 `ProvideTools()` 的结果。模型那一侧走的是 provider 渲染的集合，过滤不掉，而且不推进 `Version` ⇒ 缓存不失效 |
| 关掉一个技能 | `SkillScope.SetEnabled(name, false)` | 直写 `SkillStatusViewModel.IsEnabled` —— 绕过 `Version` 自增，缓存的提示继续送旧文本（`AgentMemberViewModel.cs:11-15`） |
| 加一个「只读」工具 | 把名字加进 `BuildQueryToolNames`（`WorkflowAgentToolkit.cs:290`） | 不登记 —— 它会计入 `MaxWriteToolCalls`，并在 `AutoMarkDirty` 打开时**把图标脏** |
| 加一个技能工具 | 加进 `SkillAgentToolkit.ToolNames` | 另起一个名字或另写一个常量集合 —— 只读分类只认 `ToolNames` 里那几个（自动并入点在 `WorkflowAgentToolkit.cs:304`） |
| 写一个 pipeline 阶段 | 实现 `IAgentPipelineStage`，自己 `try/catch` | 靠抛异常中断 run。工具路径上它已经被 `TrackedAIFunction` 的 `catch` 变成**工具错误**，模型会以为工具失败（`AgentPipeline.cs:70-83`） |
| 在工具体里 await | 什么都不写（不要加 `ConfigureAwait(false)`） | 加上它 —— 会把 await 之后的工作挪到线程池，而连接校验、`ExecuteNodes` 的第二个节点、执行引擎都在那里跑（`TrackedAIFunction.cs` 的注释） |
| 从后台线程改绑定的集合 | 走 scope 的 `Set*`（内部编组） | 直改 —— `SkillsViewModel.Skills`、`McpStatusViewModel.Servers` 是绑到宿主 UI 的 `ObservableCollection`（`McpScope.cs:726-731`） |
| 让面板开关生效 | `ApplyToScope` 穿透 | 直接改行的 `IsEnabled` 字段不触发 `OnIsEnabledChanged`（生成器只挂在属性 setter 上） |
| 加一个复合工具 | 不加 —— 每个操作都是单个组件命令步骤 | 加一个「批量做 N 件事」的工具：会**绕过或重复提交** Core 的 undo/redo 栈（`WorkflowAgentToolkit.cs:156-158`） |
| 给 MCP 自服务配审批 | 在 **scope** 上调 `WithConfirmationHandler`（`WorkflowAgentScope.cs:521`），工作流工具与 MCP 共用它 | 在 `McpScope` 上调 —— `WithMcps` 会**无条件顶掉**它（`WorkflowAgentScope.cs:1478`，注释明说「直接设在 MCP scope 上的处理器会被它替换」） |

---

## 三、步骤清单

### A. 加一个工作流工具

1. `Agent/Workflow/Functions/WorkflowAgentToolkit.cs`：加一个 `private async Task<string> Xxx(...)`，参数上用 `[Description]` 写清每个参数；返回 `Ok(...)` / `Error(...)` 信封。
2. 同一个文件，在 `CreateAllTools`（`:54`）里选一个 `WorkflowToolCategory` 并 `Add(类别, T(Xxx, nameof(Xxx)))`。**类别只影响 `ProvideTools(类别)` 的切片**；`Layout` 与 `Composite` 是保留位，别用。
3. **若它是只读的**：加进 `BuildQueryToolNames`（`:290`）的字面量集合。这决定它计入读还是写预算，以及 `AutoMarkDirty` 打开时会不会标脏。
4. **若它会在提示里被点名**：`WorkflowAgentScope.ProvideProgressiveContextPrompt`（`:1053`）与 `ProvideAllContexts`（`:998`）；失败协议里的工具名清单在 `:618`。
5. **若它属于某个能力闸门**：闸门判定在 `WorkflowAgentScope.cs`（`AllowNodeExecution` / `IsGenericCommandAllowed` / `IsInteractionAllowed`），注册的条件也在这里判断，不是「注册了再拒」——见 `:161-167` 的写法。
6. 文档同步：`skills/veloxdev-drive-workflow-with-ai/references/tools.md` 与 `Src/Core/VeloxDev.Core.Extension/README.md`（README 里写着工具数量，`Description` 属性里也有）。
7. 测试：`VeloxDev.Core.Extension.Test` 里按 `ProvideTools().Single(t => t.Name == "…")` 拿工具（`Agent/Workflow/Functions/ToolThreadAffinityTests.cs` 是范式）。**别绕过 `ProvideTools()`** —— 它是「公开注册路径」，`WorkflowLifecycleFidelityTests.cs:23` 的注释就是为这条写的。

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

见 `mcp.md` §六。要点：枚举、`GetRuntimeDir`、装载分支、`EnsureKnownKeys` 白名单、`BuildPromptContext` 文案、`AddServer` 的 `[Description]`，六处。

### E. 宿主接线（照着做的那一份）

`Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/AgentHelper.cs` 是唯一一份完整的官方接线，顺序不能换：

1. 建 scope：`tree.AsAgentScope().With…(…)`，**先**把语言、预算、闸门、子系统都配好。
2. `scope.WithTranscript(transcript)` —— **必须在 agent 存在之前**，因为 scope 是从它组合出 stage 链的（`AgentHelper.cs:215`）。
3. `scope.ProvideProgressiveContextPrompt()` 取静态骨架文本，塞进 `ChatOptions.Instructions`。
4. `chatClient.AsAIAgent(new ChatClientAgentOptions { ChatOptions = …, AIContextProviders = scope.CreateContextProviders() })` —— **不传 tools**。
5. `agent.WithPipeline(scope.Pipeline)`。
6. **不要**再手动注册技能/MCP 的管理工具。`AgentHelper.cs:205-210` 的注释把这条写死了：provider 每轮贡献自己那套，再注册一遍就是每个工具两份。

---

## 四、联动清单（加一个工具时必须同时看的地方）

| 位置 | 为什么 |
|---|---|
| `WorkflowAgentToolkit.CreateAllTools` 的 `Add(类别, …)` | 不登记 ⇒ 工具不存在 |
| `WorkflowToolCategory` | 类别只影响切片；新增类别要确认 `All` 覆盖到它 |
| `WorkflowAgentToolkit.BuildQueryToolNames`（`:290`） | 决定读/写预算与标脏 |
| `SkillAgentToolkit.ToolNames`（若属于技能） | 会自动并入只读集合 |
| `WorkflowAgentToolkit.CreateTools` 的 `.Where(IsToolEnabled)` | 自动生效，**但前提是名字能对上**（`OrdinalIgnoreCase`） |
| `WorkflowAgentScope` 的提示文本 | 模型不知道存在就不会用 |
| `Resources/Workflow/{en,zh}/Safety/*.md` | 若新工具与安全挡位有关 |
| `skills/veloxdev-drive-workflow-with-ai/references/tools.md`、同目录 `mcp.md`、`SKILL.md` | 公开文档里的工具清单 |
| `Src/Core/VeloxDev.Core.Extension/README.md` 与 csproj 的 `Description` | 里面有「60+ function-calling tools」这类会过期的数字 |
| `VeloxDev.Core.Extension.Test/Agent/**` | `ProvideTools()` 按名取工具；改名会让测试静默找不到（`Single` 会抛，`FirstOrDefault` 不会） |

**最容易漏的一条**：`WorkflowAgentToolkit.CreateTools` 与 `CreateAllTools` 的差别。宿主 UI 要用 `CreateAllTools`（要看得到被关掉的工具），模型要用 `CreateTools`（过滤后）。**两个都存在，选错会在两个方向各出一次事故。**

---

## 五、死扩展点与已失效的钩子

| 面 | 位置 | 实际状态 |
|---|---|---|
| `WorkflowAgentScope.BuildDynamicInstructions()` | `WorkflowAgentScope.cs:1545` | `internal`、**恒 `null`**。`WorkflowAgentContextProvider.cs:73` 唯一调用它。**这是预留位，不是坏掉的钩子** —— 想让它生效要让宿主与 provider 二选一 |
| `WorkflowAgentScope.ProvideAllContexts` | `WorkflowAgentScope.cs:996`、`:998` | **仓库内无外部调用者**；是「另一档提示模式」，宿主样例走的是渐进模式 |
| `AgentEmbeddedResources.ReadScript` / `ListScripts` / `ReadAllScripts` | `AgentEmbeddedResources.cs:135`、`:141`、`:147` | **零调用者，且要读的 `Resources/{system}/Scripts/` 目录不存在** —— 加目录不会自动生效 |
| `AgentEmbeddedResources.ReadSafetyFiles` | `AgentEmbeddedResources.cs:114` | **零调用者**。逐级读的是单个 `ReadSafety`（`WorkflowAgentScope.cs:582`、`:589`） |
| `AgentEmbeddedResources.ReadReference` / `ListReferences` | `AgentEmbeddedResources.cs:86`、`:92` | **零调用者**。活的是「全读」的 `ReadAllReferences` |
| `McpScope.WithMcpRoot` | `McpScope.cs:58` | 仓库内无调用者；`.evn/mcp` 是唯一用到的根 |
| `McpScope.WithSelfService` | `McpScope.cs:75` | **非测试调用者为零** —— 三个 demo 都停在 `Closed`，`AddMcpServer` 的完整路径只有测试在跑 |
| `AgentDashboardViewModel` | `Agent/Dashboard/AgentDashboardViewModel.cs` | 有测试、**无 UI 消费者**；三个 demo 绑的是 `McpScope.Status` |
| `WorkflowToolCategory.Layout` / `Composite` | `Agent/Workflow/Functions/WorkflowToolCategory.cs` | 保留位，无工具注册 |
| `WithInteractionSafetyPrompt(level, body)` 的覆盖段 | `WorkflowAgentScope.cs:593-602` | **只在 1–3 挡生效**（`{0}` 走的是 `:573` 的提前返回），接在嵌入的 `Level{n}.md` 之后、优先级更高 |
