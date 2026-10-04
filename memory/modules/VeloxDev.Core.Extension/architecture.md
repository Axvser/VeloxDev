# VeloxDev.Core.Extension — 架构

> 代码：`Src/Core/VeloxDev.Core.Extension/`（59 个源 .cs，`Agent/` 6 + `Agent/MCP/` 8 + `Agent/Skills/` 11 + `Agent/SubAgents/` 6 + `Agent/Pipelines/` 7 + `Agent/Dashboard/` 5 + `Agent/Workflow/` 5 + `Agent/Workflow/Functions/` 6，外加根目录 `AgentEx.cs`、`CheckpointEx.cs`、`CompiledGraphEx.cs`、`ComponentModelEx.cs`、`Compat/NotNullWhenAttribute.cs`）。
> **依赖**：`Src/Core/VeloxDev.Core/AI/`（命名空间 `VeloxDev.AI`，20 个文件）。本模块**是它的调用方**，Core 对本科目零引用。
> **外部包**：MAF 固定在 `Microsoft.Agents.AI` **1.22.0**（`Microsoft.Extensions.AI` 必须 ≥ 10.10.0，`ModelContextProtocol` 2.2.0）。MAF 有 51 个类型标着 `[Experimental]`（MAAI001，此数不可复核），**整个 `Microsoft.Agents.AI.Compaction` 命名空间在内** —— 见 `native-capabilities.md`。
> 嵌入资源：`Resources/Workflow/{en,zh}/{References,Safety,Skills}/`，32 个 .md。
> 宿主样例：`Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/AgentHelper.cs`。
> 测试：`Src/Core/VeloxDev.Core.Extension.Test/`（60 个 .cs，473 条 `[TestMethod]`，54 个 `[TestClass]`）。

本文只写「读完这些文件才知道的东西」。类型清单、成员表、继承树请看 IDE。

---

## 一、这个模块是什么，不解决什么

**是什么。** 把一棵工作流树（`IWorkflowTreeViewModel`）包成一个 **MAF agent 可读可写、且被宿主节制的工具面**。三件事：

1. **统一的追踪包装** —— 任意来源的工具（内置的、`WithTools` 注册的、技能贡献的、MCP 服务器贡献的、子代理子系统贡献的）都会被同一个包装器接管，于是「编组到宿主线程 / 计预算 / 触发回调 / 标脏」只有一处实现（`Agent/Workflow/Functions/WorkflowAgentToolkit.cs:228`）。
2. **上下文渲染** —— 每轮把「框架有哪些类型 / 图现在长什么样 / 有哪些技能与 MCP 服务器 / 名册上有哪些子代理」渲染成提示，按 `Version` 缓存。
3. **四块可独立使用的子系统** —— Skills、MCP、SubAgents（让模型派出能力被夹紧的背景子代理）、Dashboard（宿主 UI 的只读镜像）。

**不解决什么：**

| 不在本模块内 | 实际归谁 |
|---|---|
| 命令发现、参数绑定、属性读写、按名解析类型 | 大多在 `Src/Core/VeloxDev.Core/AI/`（`AgentCommandDiscoverer` / `AgentMethodInvoker` / `AgentPropertyAccessor` / `AgentTypeResolver`）。**但「命令的发现/调用」与「属性写入」在本模块各有一份分叉实现**：`.../Agent/Workflow/Functions/CommandInvoker.cs`（自带的 `DiscoverCommands` 与自带 `CommandDescriptor`）、`.../Agent/Workflow/Functions/ComponentPatcher.cs`（自带的 `CopyScalarProperties`）。改 Core 的这两条**不会**影响这里实际跑的路径；详见 `memory/modules/AI/architecture.md` |
| undo/redo 栈 | Core。这是**没有复合工具**的理由 —— 见 `WorkflowAgentToolkit.cs:190-192`：「No composite/bundled tools: every operation is a single component-command step so the undo/redo stack (owned by Core) is never bypassed or double-submitted」 |
| 对话历史 / 会话状态 | `Microsoft.Agents.AI` 的 `AgentSession`。`AgentTranscript` 不是它，见 `pipelines.md` |
| 模型调用与 tool-calling 循环 | `Microsoft.Agents.AI` |
| 面板控件 | 模块只发出可绑定的 `[VeloxProperty] partial` 对象，控件在宿主（`Examples/Workflow/Jalium/Demo/MainWindow.cs:701`、`Examples/Workflow/Blazor/Demo/Demo/Components/Pages/Workflow.razor.cs:26`、`Examples/Workflow/Avalonia/Demo/Views/Workflow/WorkflowView.axaml:273`） |

**本模块自己加的，不是 AI 层给的**：工具预算（三档 + 逃生舱，账本是树共享的一口锅 —— 见 `sub-agents.md` §二）、把整段调用编组到 `SynchronizationContext`、事件管线、技能 / MCP / 子代理三个子系统、被禁用工具的闸门。

---

## 二、分层与依赖方向

```
宿主 / demo
  │  ① tree.AsAgentScope()            AgentEx.cs:9
  ▼
WorkflowAgentScope                      Agent/Workflow/WorkflowAgentScope.cs
  │  配置 facade：Version / 语言 / 预算 / 开关 / 子系统 / 工具注册
  │
  ├─ CreateToolkit()  ──► WorkflowAgentToolkit          (每个 scope 一个实例，:1324)
  │                          ├─ CreateTools()    受 IsToolEnabled 过滤，给模型看   :73
  │                          └─ CreateAllTools() 不过滤，给宿主 UI 列全             :81
  │                          └─ Tools(ToolPipeline)  每 scope 一个，Refuse=CheckBudget  :248
  │                          └─ Ledger(ToolCallLedger) 子 scope 的接父账本（见 sub-agents.md §二）
  │
  ├─ Pipeline ──► TextPipeline ► SharedTools ► CreateAccountingStage()   （ToolPipeline.cs / AgentPipeline.cs）
  │
  └─ CreateContextProviders() ──► [Compaction?, self, Skills, MCP, SubAgents, Todo?, AgentMode?, …WithContextProvider 工厂]
                                   │   （固定顺序，与宿主挂载的先后无关）
                                   │   Compaction 排最前：它重写消息历史，后面的切片要看到裁剪过的版本
                                   │   SubAgents 排在 MCP 之后、Todo 之前：它是上下文（现在有谁在跑），不是行为指令
                                   │   Todo / AgentMode 是框架自带的，排在上下文来源之后 —— 见 native-capabilities.md
                                   ▼ 每轮渲染一次（未变时直接返回同一实例，零分配）
                             AIContext{Instructions = 能力包络, Tools}
                                   │
                                   ▼
                            MAF agent ──► 模型 ──► 工具调用
                                   │
                                   ▼
              TrackedAIFunction ──► ToolPipeline.Refuse(CheckBudget)
                                 └─► 编组到 SynchronizationContext 上跑工具体
                                       └─► AgentToolCallCompleted ──► AccountingStage ──► AccountAsync
                                                                   └─► TextPipeline / ToolPipeline ──► AgentTranscript
```

**方向不可逆的理由（代码里写明的）：**

- `WorkflowAgentContextProvider.cs:15-19`：**provider 是工具的唯一来源**。Agent Framework 会把 provider 贡献的工具与 `ChatOptions.Tools` **并集**，且**不按名去重** —— 两条通道给同一个工具，模型就收到两份。所以 `Agent/AgentClientExtensions.cs:42` 的 `AsAIAgent(providers, instructions)` **刻意没有 tools 参数**。
- `WorkflowAgentToolkit.cs:233-241`：`Tools` 是**每 scope 一个实例**，并且**同一个引用**交给各子系统 provider —— 这是「MCP 或技能贡献的工具与内置工具受同一套预算约束」的实现方式。钩子读 scope 的**实时**值，所以子系统挂上之后再调 `With*` 也能到达它们的工具。
- `WorkflowAgentContextProvider.cs:92`：本 provider 只按 **`_scope.ContextKey`**（= `Version` + 预算用量档，见 `WorkflowAgentScope.cs:1841`）缓存。技能与 MCP 由各自的 provider 渲染，它们的版本变了不会影响这里，按它们开键只会重渲染一个没动过的切片。用量档进键是因为**它是唯一一个不 bump `Version` 也会变的事实**（每次工具调用都在动），而包络会陈述它。

---

## 三、一次工具调用的完整流向

1. 模型调用工具。`WorkflowAgentToolkit.WrapTool`（`:228`）保证**任何 `AIFunction` 型工具**（内置、`WithTools`、技能、MCP）都被 `Agent/TrackedAIFunction.cs:29` 包住。非 `AIFunction` 的工具（如 MCP 客户端原始工具）原样放行，**不受包装**。
2. `TrackedAIFunction` 先把**整段调用**编组到宿主的 `SynchronizationContext`（`TrackedAIFunction.cs:49` → `RunOnContextAsync` `:115`）。**工具体里刻意没有 `ConfigureAwait(false)`** —— 加了会把 await 之后的工作挪到线程池，而那正是「连接校验 / `ExecuteNodes` 的第二个节点 / 执行引擎驱动编译链」发生的地方。
3. 在编组块内、工具体之前，先跑 `ToolPipeline.CheckRefusal`（`TrackedAIFunction.cs:59`）。拒绝 ⇒ 不跑工具体，直接发 `AgentToolCallCompleted{Refused}` 并返回 `{"status":"error",…}`（`:63-64`、`:141`）。
4. 若工具没被预算拒绝，再跑 `ToolPipeline.CheckConfirmationAsync`（`TrackedAIFunction.cs:72`）—— 只对非只读工具、且宿主开了 `WithToolApproval` 时问人；拒绝同样按 `Refused` 上报。**这是 `CheckBudget` 之后的第二道闸**（见 `maf-conformance.md` §三）。
5. `WorkflowAgentToolkit.CheckBudget`（`:317`）按顺序判：**被宿主关掉的工具** → **预算工具本身放行** → **根账本上限（子 scope 才有）** → `MaxToolCalls` → 写/读分档上限。命中就返回 `BudgetRefusal`（`:515`）。**两类 scope 现在都被指去 `ResetToolCallLimit`**（`LimitRefusal` `:528`）—— 因为两类都持有它；区别只在追加的职责：子 scope 另被要求「向上报告」，因为它有一个悬在结果上的派发者。见 `sub-agents.md` §四。
6. 工具体跑完 ⇒ `ReportAsync`（`TrackedAIFunction.cs:101`）发 `AgentToolCallCompleted{Succeeded|Failed}`，带耗时。
7. `AccountingStage`（`WorkflowAgentToolkit.cs:296`）只对 **`Succeeded`** 记账（`:306`）。`AccountAsync`（`:399`）先跳过预算工具本身（否则重置会把自己刚清零的计数再加回去 = 「重置撤销了自己」），再 `_ledger.Spend(IsQueryTool(toolName))`（沿账本链一路上行）、发 `RaiseToolCalledAsync`、按需要 `MarkDirty()`（`:412-413`）。
8. 事件继续沿 `AgentPipeline` 走到 `TextPipeline` / `ToolPipeline`，写进 `AgentTranscript`。

**为什么 `CheckBudget` 同时管「被关掉」而不只是过滤：** 见 `:319-322` —— 过滤（`CreateTools` 的 `.Where`）只到得了工作流内置工具；这个钩子被所有切片共享，所以一个开关能到达 MCP 和技能的工具。只过滤的话，关掉 `ListSkills` 会**静默无效**。

**为什么 `ResetToolCallLimit` 不受类别过滤、也不被闸门拦**（`:207-208`、`:326-329`）：它是宿主设的预算的唯一出口，恰好在该用到它的时候消失就没意义了。

---

### 三之末、每一次工具调用，宿主都要付一次账（2026-09-27 实测）

`WorkflowAgentScope.RaiseToolCalledAsync`（`:785`）**每完成一次工具调用**就发一次 `ToolCalled`。宿主侧通常拿它刷新画布 ——
而**全量刷新**（重解析命名控件 + 跑布局 + 重算可见集）是幂等的重活：一轮 Agent 回合几十次调用 ⇒ 几十次全量刷新 ⇒
用户实测「对话中节点编辑器的显示响应**非常非常慢**」。修法不是在库里节流（库不知道宿主什么时候算"settle"），而是**宿主把
请求合并**：demo 侧的统一件是 `Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/CoalescedRefresh.cs`（首次请求排队、
其余落在同一趟里；旗标**在刷新执行之前**清，所以刷新期间来的请求会再排一次 —— 那一轮才代表刷新后的最新状态）。

**七家的现状是三种，不是一种**（2026-09-27 逐家核过）：Avalonia / WPF / WinUI / WinForms / Jalium **每调用一次就全量刷新**
（本次接上合并器）；**MAUI 早就自己做了同一件事** —— `ScheduleRefresh()`（`Examples/Workflow/MAUI/Demo/Controls/Workflow/WorkflowView.xaml.cs:378`）用一个
`bool _layoutRefreshPending` 门控 + `MainThread.BeginInvokeOnMainThread`，而且同样是**先清旗标再刷新**，与 `CoalescedRefresh`
的契约逐条一致（差别只在门是普通 `bool`、非原子；眼下都从主线程来，所以行为正确）⇒ **不要再叠第二套**；**Blazor 压根不
在这两个事件上刷新**（页面既不订阅 `ToolCalled` 也不订阅 `VisualRefreshRequested`，而是反应式重渲：`Nodes`/`Links` 的 `CollectionChanged`、`Controller.PropertyChanged`、`Layout.PropertyChanged`，外加 `MCP.Status.PropertyChanged` —— `Workflow.razor.cs:77-89` 的订阅、`:146-150` 的处理器），所以那条前提在它身上不成立。

**顺带一条零调用者**：`AgentHelper.VisualRefreshRequested`（demo 的 Lib，`Helper/AgentHelper.cs:189`）**声明了、七家都订阅了、
从来没有人 raise**。所以七家那份订阅一直是空的 —— 真正的触发只有 `ToolCalled`。（这类"声明了没人发"的面，本模块 §六 有专节。）

## 三点五、编译运行的控制面（2026-09-27 起）

`RunCompiledWorkflow` 是**跑到完才返回**的，所以模型说什么都到不了"还在跑的那一轮"。新增一条后台入口 + 一组控制工具后，六个编译执行能力里"需要有人按一下"的那两件（暂停门、检查点）才真的能被 Agent 用：

| 工具 | 作用 |
|---|---|
| `StartCompiledWorkflow` | 与 `RunCompiledWorkflow` 同一条编译+引擎路径，但**立刻**返回一个句柄 |
| `PauseCompiledRun` / `ResumeCompiledRun` | 门：停在下一个节点边界 / 放行 |
| `StopCompiledRun` | 取消（`outcome = Cancelled`），留下的位置仍在 store 里 |
| `GetCompiledRunStatus` | `isRunning` / `outcome` / `isPaused` / `failures` / 日志尾 / `logFile`；**看到结束时它会把这个句柄退休**，之后再问就是未知句柄 |
| `ContinueCompiledWorkflow` | 从 store 里最后一次检查点起新一轮（已完成的节点不再驱动） |

两条 `With*` 是宿主的口子：`WithCheckpointStore`（默认给每个 scope 一个内存 store，所以 Continue 开箱可用）与 `WithSessionConfiguration(Action<RuntimeContext>)`（重试策略 / 观察者 / sink / 补偿 / 门 / 检查点，一次配齐）。**填充顺序是契约**：scope 自己的设置 → 宿主钩子 → 工具需要的（门与 store 只在仍是 `null` 时补 ✗ 不覆盖宿主）✓（`WorkflowAgentToolkit.cs:2048-2049`）；失败记录与宿主的 sink **并存**（`RecordingErrorSink` 转发 ✓）。

**日志读取取决于宿主的配置，而结果会把答案带出来**：配了文件 `ILogWriter` ⇒ 结果里有 `logFile`（**绝对路径**）⇒ 模型用它自己的文件工具打开即可；默认的内存日志 ⇒ 结果里的 `logs` 就是记录，不需要任何文件工具。`TextWriterLogWriter.Path`（`:80`，`For(path)` 时填、包装外部 `TextWriter` 时为 null）就是为这一条加的。

## 四、四条承重的不变量

1. **工具只从 provider 出，不从 `ChatOptions.Tools` 出。** 理由见上（并集不去重）。
2. **每个 scope 一个 `WorkflowAgentToolkit`、一个 `ToolPipeline`、一个 `WorkflowAgentContextProvider`。** 工具计数与状态都在 toolkit 实例上，但**计数不再是三个裸字段**：每个 toolkit 持一个 `ToolCallLedger`（`WorkflowAgentToolkit.cs:30`），根 scope 的账本 `Outer == null`，被 spawn 出来的子 scope 经 `WorkflowAgentScope.ParentLedger`（`:1312`）接到父的账本上。换实例 = 换账本；**没有父子关系时账本的每个成员逐字退化成那三个计数器**，这是既有预算测试仍然有意义的前提。`WorkflowAgentScope.CreateToolkit()` 是 `_toolkit ??= new(this, ParentLedger)`（`:1324`），`CreateTools` 每次调用**新建工具列表**但复用同一个账本。这与 `ProvideTools()`（`:1334`，一次快照）的区别写在 `skills/veloxdev-drive-workflow-with-ai/SKILL.md:43`。详见 `sub-agents.md` §二。
3. **提示词按三个时钟切成三份，各有各的载体。** 混起来就会得到一个每轮重建 870 KB、或者永远过期的提示词。

| 内容 | 变化时钟 | 载体 | 缓存键 |
|---|---|---|---|
| 静态骨架（行为约束、参考文档、类型清单、安全策略） | 构造后从不 | 宿主 `ChatOptions.Instructions` | 构造期一次 |
| **能力包络**（闸门 / 关掉的工具 / 预算 / 挡位漂移 / 类型差量） | 配置变更 + 预算用量档 | provider 的 `AIContext.Instructions`（框架**追加**在骨架之后，逐字 `"骨架\n"` 前缀稳定） | `ContextKey` = `Version` + 用量档 |
| 预算消耗 | 每轮 | 包络内，**分档**：仅 ≥ 上限 80% 时出现，20% 一档 | 靠分档自然稳定 —— 整个预算生命周期最多变 2 次 |

**骨架收据**（`_skeletonReceipt`，`WorkflowAgentScope.cs:1766`）是「不说两遍」的机制：骨架渲染时记下它写了什么快照，包络只补漂移的那几段，并用「取代上文」措辞。骨架从没被渲染过时，包络全量输出。**代价**：它假定「渲染出来的骨架就是交给模型的那份」—— 拿去当预览/日志渲染会让包络以为已经说过。

**图状态刻意不进包络**：渐进披露，模型自己 `ListNodes`。

4. **一片提示文本只有一个主人，第二个到场的人让位。** 静态骨架与技能子系统都能渲染嵌入语料：`ProvideProgressiveContextPrompt()` 在 `Skills == null` 时把语料拼进骨架，而 `SkillAgentContextProvider` 本也读 `BuildEmbeddedBlock`。同一个 scope 两样都做 ⇒ 每篇技能文档进两次。解法是让**后接的一方**知情：`WorkflowAgentScope` 用 `_embeddedSkillsFrozenIntoPrompt`（sticky，一旦有骨架带着语料出门就置位）把事实传给 `SkillScope.CreateContextProvider(..., embeddedCorpusDelivered:)`，provider 于是改贡献 `SkillScope.BuildWithdrawnBlock`（**当前停用的嵌入技能名单**）而不是正文。**为什么不让它闭嘴**：骨架冻住的是当时的启用状态，`SetEnabled(name, false)` 若不发声就成了静默空操作。**正确的宿主顺序是先 `WithSkills` 再取骨架**（`AgentHelper.cs` 即是），语料归子系统，差量机制闲置。

  这条是 §三 表格的必然推论：骨架住在宿主的 `ChatOptions.Instructions` 里，构造后**撤不回**。同一推理也适用于「技能/MCP 清单不进包络」（§三 表末段）—— 那两片各有 provider，第三个说话的人就是重复。

---

## 五、入口：我要改 X，先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 工具清单 / 分类 / 某个工具的 JSON 形状 | `Agent/Workflow/Functions/WorkflowAgentToolkit.cs`（约 3150 行） |
| 哪些工具算「只读」、脏标记怎么落 | `WorkflowAgentToolkit.cs:372`（`BuildQueryToolNames`）+ `:412` |
| 工具预算（三档、拒绝文案、重置流程） | `WorkflowAgentToolkit.cs:317`（`CheckBudget`）、`:399`（`AccountAsync`）、`:430`（`ResetToolCallLimit`）、`:515`（`BudgetRefusal` 的根/子分叉）、`Agent/Workflow/Functions/ToolCallLedger.cs`（树共享的那口锅）。**上限本身**由 `WithMax{Tool,Read,Write}ToolCalls` 设，三个都 `BumpVersion()` —— 门（`CheckBudget`）读的是实时属性，包络读的是缓存文本，不 bump 就会让模型读到一个不被执行的上限 |
| 子代理（派发 / 窄化 / 任意深度为什么终止） | `Agent/SubAgents/`（见 `sub-agents.md`） |
| 提示词（行为约束、失败处理、渐进模式） | `Agent/Workflow/WorkflowAgentScope.cs:1065`（`ProvideProgressiveContextPrompt`）、`:850`（`BuildFailureHandlingProtocol`） |
| 各级安全挡位注入什么 | `WorkflowAgentScope.cs:802`（`BuildInteractionSafetyPrompt`）+ `Resources/Workflow/{lang}/Safety/*.md` |
| 每轮渲染的指令与工具 | `Agent/Workflow/WorkflowAgentContextProvider.cs:86`（`BuildContext`）；指令是**能力包络**，不是 `null` —— 见 §四.3 |
| 待办清单 / 运行模式 / 上下文压缩 | 三个都是**框架自带的 provider**，本模块只负责挂上去 —— 见 `native-capabilities.md` |
| 每轮都用哪些 provider、按什么顺序 | `WorkflowAgentScope.CreateContextProviders()`；工具只从这里出，不走 `ChatOptions.Tools` |
| 树快照与 diff | `Agent/Workflow/WorkflowStateTracker.cs` |
| 属性补丁的拒绝白名单 | `Agent/Workflow/Functions/ComponentPatcher.cs` |
| 事件与 transcript 的形状 | `Agent/Pipelines/`（见 `pipelines.md`） |
| 技能发现 / 文件布局 / frontmatter | `Agent/Skills/`（见 `skills.md`） |
| MCP 装载 / 自服务挡位 / 状态 | `Agent/MCP/`（见 `mcp.md`） |
| 宿主可绑定的面板对象 | `Agent/Dashboard/`（见 `dashboard.md`） |

---

## 六、代码与注释不一致 / 零调用者的面

**这是本模块最值得先知道的一条：`Agent/AgentEmbeddedResources.cs` 有一整段「Scripts」API 和一段死掉的 Safety 组合器，它们读的资源目录不存在。**

| 成员 | 位置 | 状态 |
|---|---|---|
| `ReadScript` / `ListScripts` / `ReadAllScripts` | `AgentEmbeddedResources.cs:135 / :141 / :147` | **零调用者**（三个只互相调用）。XML 注释宣称读 `Resources/{system}/Scripts/{name}`（`:132`），而 `Resources/Workflow/` 下**只有 `en/`、`zh/` 两个目录**，其下只有 `References/`、`Safety/`、`Skills/`。**没有 `Scripts/`** |
| `ReadSafetyFiles` | `AgentEmbeddedResources.cs:114` | **零调用者**。单个 `ReadSafety`（`:108`）是活的（`WorkflowAgentScope.cs:814`、`:821`） |
| `ReadReference`（单个）/ `ListReferences` | `AgentEmbeddedResources.cs:86 / :92` | **零调用者**（只有一个「全读」的 `ReadAllReferences` 是活的：`WorkflowAgentScope.cs:1019`、`:1143`） |
| `ProvideAllContexts` | `WorkflowAgentScope.cs:1005 / :1008` | **仓库内零外部调用者**（无参重载只转发给有参重载）。是给宿主的另一档提示模式，宿主样例走的是渐进模式（`AgentHelper.cs` 调 `ProvideProgressiveContextPrompt`）。**它和渐进模式一样会写骨架收据**，所以两档都算「骨架已交付」 |
| `BuildDynamicInstructions()` | `WorkflowAgentScope.cs:1872` | **2b 起不再是预留坑位**：渲染**能力包络**（闸门 / 被关掉的工具 / 调用预算的分档用量 / 相对骨架的漂移段）。按 `ContextKey` 缓存，空闲轮零分配；**绝不调用** `ProvideProgressiveContextPrompt`（骨架一次 870 KB、是框架每轮基线的 19 倍，见 `WorkflowAgentScope.cs:1752-1753`；旧文说的「1,100 倍」不可复核） |
| `WorkflowToolCategory.Layout`（`1<<5`）、`Composite`（`1<<8`） | `Agent/Workflow/Functions/WorkflowToolCategory.cs` | 保留位，**没有工具注册在这两个类别下**（与 `WorkflowAgentToolkit.cs:185-186` 的「不做复合工具」一致） |

**推论**：改 `Resources/` 时不要以为加一个 `Scripts/` 目录就会被自动加载 —— 加载器在（`ListScriptCategory`），调用者在（`ReadAllScripts`），但**没有第三方调用它**。同理，加 `Safety/Level4.md` 不会有任何效果，档位取值域是 `_interactionSafety > 0`（且 `WithInteractionSafety` 夹在 0–3）而文件名按 `$"Level{_interactionSafety}"` 拼（`WorkflowAgentScope.cs:821`），级别的语义定义在 `McpSelfServiceLevel.cs` 之外的那套交互挡位上，要新加挡位必须同时改宿主。

---

## 七、平台差异

**本模块没有平台差异轴。** `netstandard2.0;net8.0`（`VeloxDev.Core.Extension.csproj:4`），只依赖 `SynchronizationContext` 抽象，七家 GUI 一视同仁。

平台差异出现在**宿主接线**上：各家 demo 自己决定把 agent 面板、MCP 状态面板挂在哪、用什么对话框实现 `RequestSelection` / `RequestConfirmation`。要照抄接线方式看 `Examples/Workflow/Avalonia/Demo/Views/Workflow/WorkflowView.axaml.cs`、`Examples/Workflow/Blazor/Demo/Demo/Components/Pages/Workflow.razor.cs`、`Examples/Workflow/Jalium/Demo/MainWindow.cs` 三家。平台侧的通用陷阱见 `memory/modules/TransitionSystem/adapters/<平台>.md` 与 `memory/modules/WorkflowSystem/adapters/<平台>.md`，此处不重复。

---

## 八、附：序列化的入口 `ComponentModelEx.cs`（引擎在 Core）

`Src/Core/VeloxDev.Core.Extension/ComponentModelEx.cs` 的命名空间是 **`VeloxDev.MVVM.Serialization`**。
**2026-10-03 起它只是入口，引擎在 `VeloxDev.Core` 的 `VeloxDev.Serialization`（见 §八·一）** ——
本项目**已经没有 Newtonsoft 依赖**（包引用也删了），整个 Agent 面的 IL 裁剪警告从 160 降到 0（历史观测，不可复核）。

**公开面一行没变**：`Serialize<T>` / `TryDeserialize<T>` / `Deserialize<T>` 一族（同步 / 异步 / 流 / 字节 /
`TextWriter`），全部 `where T : INotifyPropertyChanged`，交给新的序列化器执行。它是所有 demo 存/读工作流
走的那条路（`TreeViewModel.cs` 的 `this.Serialize()`）。

**`SerializationOptions` 只剩两个开关**：`WithIndented` / `WithCompact`（`VeloxJsonFormat`），以及
`WithExcludedPropertyTypes`（`CompiledGraphEx` 用它把节点引用挡在快照外）。三个 Newtonsoft 时代的开关
（`WithTypeNameHandling` / `WithNullValueHandling` / `WithDefaultValueHandling`）随引擎一起删了 ——
它们配的是一个已经不在的库。

**同一命名空间下还有两个「物理在本项目、语义属于别的模块」的文件**：`CompiledGraphEx.cs`
（编译图存/读，见 [`WorkflowSystem/compiler-execution.md`](../WorkflowSystem/compiler-execution.md) §八）
与 `CheckpointEx.cs`（运行检查点存/读 + `FileCheckpointStore`，见同文件 §十一）。

### 八·一、闭世界：什么类型能进文档

**一个类型能进文档，当且仅当生成器为它编出了读写器**。收录条件是：它是四个组件接口之一的实现、
或带 `[VeloxProperty]` / `[VeloxCommand]`、或带 `[VeloxSerializable]`（**非 ViewModel 的普通文档类型
用它自报家门**，`ExecutionCheckpoint` 就是），或者能从这些类型出发沿**成员的声明类型**走到。
生成器走整编译遍历而不是特性触发集：组件的身份是「实现了哪个接口」，特性触发表达不了。

**代价是真的**：一个没被标注、又没有成员声明它的普通 POCO 进不了文档 —— 写它会抛
`MissingWriter`（错误信息直接写着为什么）。这不是 bug，是这套东西能裁剪的前提。

**两条契约由此而来**（都在 `memory/modules/AI/architecture.md` §七 记着，这里只给结论）：
- 组件接口是**另一个生成器**加上去的，所以收录认的是作者写下的 `[WorkflowBuilder.*]` 特性，不是最终接口。
- 泛型类型（`SlotEnumerator<T>` 这类）只有**封闭实例**才有条目，而条目由**见过那个组合的那一边**发出。

### 八·二、逐字节兼容

新引擎的输出与旧 Newtonsoft 引擎**逐字节相同**，这是硬要求（用户机器上已有存档）。冻结在
`Src/Core/VeloxDev.Core.Extension.Test/Serialization/Golden/` 的四份文档是契约，由
`VeloxJsonSerializerTests` 逐字节比对。**成员顺序是最难的一条**，量出来的规则是：

> 手写的可写属性按声明顺序在前，`[VeloxProperty]` 提升出来的属性按字段顺序在后。

`SerializationOrderTests` 对六个代表形状（含 `SlotEnumerator<TSlot>` 这种混合的）钉住它。**改生成器的
成员收录顺序会直接打翻这条**，改之前先看那份测试。

### 八·三、读入侧只有两处按名字找类型（实测确认消不掉）

`SlotEnumerator` 的选择器类型走的是**目录查询**（`AgentTypeResolver.ResolveType`，实测换过去全绿）。
另外两处**实测过**换不掉，注释里写着实测结果：

| 处 | 换过去会怎样 |
|---|---|
| `CompileKeyNormalizer` 的路由键 | Extension 4 条编译图序列化测试当场红，症状 `Branch 'Low' has no downstream node` —— 键停在 `long`，动态分支谁都不匹配 |
| `TransitionProperty.FindIndexer` 的索引器属性 | `Expression must be writeable (Parameter 'left')` —— 写路径是 `Expression.Assign(<成员访问>, value)`，方法调用不是可赋值的左值 |

两处都是「宿主在运行期选定的类型、编译单元里没有任何地方提到它」，所以目录收录不到。**代价写在各自的
注释里**：宿主必须自己保住那些类型的元数据。

**⚠ 一个可写属性会被写出去，包括委托。** 新引擎同样只看「能不能写」：一个
`public Action<T>? Hook { get; set; }` 写出去没问题，**读回来时构造委托会抛**。运行期状态用
`{ get; private set; }`，需要外部可设就用**方法**而不是属性。2026-09-27 实测过一次（`ControllerViewModel`
加了这样的钩子，三条编译图快照测试当场红），那条结论不受引擎更换影响。

### 八·四、嵌在容器里的容器：写得出、读不回（2026-10-03 修）

**写侧对容器是按形状递归的**（`VeloxJsonSerializer.WriteValue` 的 `IDictionary` / `IEnumerable` 两条
分支），所以任何嵌套都写得出去。**读侧没有这条分支**：一个成员自己那层容器由该成员的生成 reader 就地填
（`VeloxJsonCodeWriter.ReadMember` 发 `ReadArray` / `ReadMap`），再往里一层就没有「就地」了 ——
`ReadValue` 落到 `ReadObjectValue`，而容器类型不会有生成条目，于是抛
`'…' has no registered JSON reader`。

**症状是「带连接的树存下来读不回去」**：`IWorkflowTreeViewModel.LinksMap` 是
`Dictionary<IWorkflowSlotViewModel, Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>>`
（`WorkflowTreeEx` 那一片都在读写它）。`TryDeserialize` 会把这个异常吞成 `false`，所以七个 demo 的
「加载工作流」看起来只是按钮没反应。**框架树和 demo 树同病** —— 两边生成的是同一句
`ReadMap(reader, map, typeof(…IWorkflowSlotViewModel), typeof(Dictionary<…>), interfaceKeys: true)`。

**修法：登记构造，不登记 reader。** 生成器为它见过的每个嵌容器组合发一句
`VeloxJsonRegistry.RegisterContainerFactory(typeof(闭容器), () => new …())`；读侧 `ReadValue` 命中就先造实例、
再按声明类型推形状递归读（`ReadValue` 里那一段 + 私有 `ReadContainer`）。形状规则因此只有一处，
和写侧同源。

**为什么不能顺手用反射造**：`Activator.CreateInstance(Type)` / `MakeGenericType` 正是这套东西要躲开的 ——
同一条理由让 `ReadValue` 连顶层数组入口都不给（`Array.CreateInstance` 是 `RequiresDynamicCode`）。
生成器侧落点：`VeloxJsonModelBuilder.CollectNestedContainers`（起点是成员的 `ElementType`，**不是**
`DeclaredType` —— 外面那层由成员自己读）、`IsContainerReadable`（序列只在 `List<T>` 顶得住时才登记，
`HashSet` / `Queue` / `Stack` 不是 `IList`，声明处本来就跳过）、`VeloxJsonCodeWriter.ContainerFactory`
（接口类型落到 `Dictionary<K,V>` / `List<T>`）。

**守卫只有两处，`Golden/tree.json` 不守这条**（那棵树是 `SerializationGoldenTests.BuildTree` 建的，
**没有连接**，`LinksMap` 恰好是 `{}`）：`Serialization/DemoTreeRoundTripTests.cs` 三条
（空树 / 单节点 / 带连接）与 `Agent/Workflow/Functions/WorkflowSerializationTests.cs` 的
`TreeWithAConnection_RoundTripsItsLinksMap`。改容器的写读两侧之前先看它们。

**一个连带依赖**：接口键的 map 靠 `reader.ResolveReference(int.Parse(成员名))` 解键，所以键对象必须
**先于**这张 map 被登记 —— 文档里 `Nodes` 排在 `LinksMap` 前面才成立；成员顺序反过来会静默丢条目
（`ReadMap` 里 `key is null → SkipValue`）。

**⚠ 修的是生成器，所以 Release 不受益。** `VeloxJsonRegistry` / `ReadValue` 那半边在源码里、两种配置都生效；
但**工厂是生成代码**，而 Release 走的是 NuGet 包（`VeloxDev.Core.csproj:19` 的 `Version="10.0.0"`、
`VeloxDev.Core.Extension.Test.csproj:33` 的生成器包 `Version="10.0.0"`）—— 不升包，Release 下带连接的树仍然读不回来。
哪几处要一起升见 [`VeloxDev.Core.Generator/extension.md`](../VeloxDev.Core.Generator/extension.md) §四。
