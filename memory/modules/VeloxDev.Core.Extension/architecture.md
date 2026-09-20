# VeloxDev.Core.Extension — 架构

> 代码：`Src/Core/VeloxDev.Core.Extension/`（53 个源 .cs，`Agent/` 4 + `Agent/MCP/` 8 + `Agent/Skills/` 11 + `Agent/SubAgents/` 6 + `Agent/Pipelines/` 7 + `Agent/Dashboard/` 5 + `Agent/Workflow/` 4 + `Agent/Workflow/Functions/` 6，外加根目录 `AgentEx.cs`、`ComponentModelEx.cs`）。
> **依赖**：`Src/Core/VeloxDev.Core/AI/`（命名空间 `VeloxDev.AI`，13 个文件）。本模块**是它的调用方**，Core 对本科目零引用。
> **外部包**：MAF 固定在 `Microsoft.Agents.AI` **1.22.0**（`Microsoft.Extensions.AI` 必须 ≥ 10.10.0，`ModelContextProtocol` 2.2.0）。MAF 有 51 个类型标着 `[Experimental]`（MAAI001），**整个 `Microsoft.Agents.AI.Compaction` 命名空间在内** —— 见 `native-capabilities.md`。
> 嵌入资源：`Resources/Workflow/{en,zh}/{References,Safety,Skills}/`，32 个 .md。
> 宿主样例：`Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/AgentHelper.cs`。
> 测试：`Src/Core/VeloxDev.Core.Extension.Test/`（36 个文件，352 条）。

本文只写「读完这些文件才知道的东西」。类型清单、成员表、继承树请看 IDE。

---

## 一、这个模块是什么，不解决什么

**是什么。** 把一棵工作流树（`IWorkflowTreeViewModel`）包成一个 **MAF agent 可读可写、且被宿主节制的工具面**。三件事：

1. **统一的追踪包装** —— 任意来源的工具（内置的、`WithTools` 注册的、技能贡献的、MCP 服务器贡献的、子代理子系统贡献的）都会被同一个包装器接管，于是「编组到宿主线程 / 计预算 / 触发回调 / 标脏」只有一处实现（`Agent/Workflow/Functions/WorkflowAgentToolkit.cs:222`）。
2. **上下文渲染** —— 每轮把「框架有哪些类型 / 图现在长什么样 / 有哪些技能与 MCP 服务器 / 名册上有哪些子代理」渲染成提示，按 `Version` 缓存。
3. **四块可独立使用的子系统** —— Skills、MCP、SubAgents（让模型派出能力被夹紧的背景子代理）、Dashboard（宿主 UI 的只读镜像）。

**不解决什么：**

| 不在本模块内 | 实际归谁 |
|---|---|
| 命令发现、参数绑定、属性读写、按名解析类型 | 大多在 `Src/Core/VeloxDev.Core/AI/`（`AgentCommandDiscoverer` / `AgentMethodInvoker` / `AgentPropertyAccessor` / `AgentTypeResolver`）。**但「命令的发现/调用」与「属性写入」在本模块各有一份分叉实现**：`.../Agent/Workflow/Functions/CommandInvoker.cs:21`（自带的 `DiscoverCommands` 与自带 `CommandDescriptor`，`:171`）、`.../Agent/Workflow/Functions/ComponentPatcher.cs:236`（自带的 `CopyScalarProperties`）。改 Core 的这两条**不会**影响这里实际跑的路径；详见 `memory/modules/AI/architecture.md` |
| undo/redo 栈 | Core。这是**没有复合工具**的理由 —— 见 `WorkflowAgentToolkit.cs:185-186`：「每个操作都是单个组件命令步骤，这样 undo 栈不会被绕过或重复提交」 |
| 对话历史 / 会话状态 | `Microsoft.Agents.AI` 的 `AgentSession`。`AgentTranscript` 不是它，见 `pipelines.md` |
| 模型调用与 tool-calling 循环 | `Microsoft.Agents.AI` |
| 面板控件 | 模块只发出可绑定的 `[VeloxProperty] partial` 对象，控件在宿主（`Examples/Workflow/Jalium/Demo/MainWindow.cs:650`、`Examples/Workflow/Blazor/Demo/Demo/Components/Pages/Workflow.razor.cs:24`、`Examples/Workflow/Avalonia/Demo/Views/Workflow/WorkflowView.axaml:11`） |

**本模块自己加的，不是 AI 层给的**：工具预算（三档 + 逃生舱，账本是树共享的一口锅 —— 见 `sub-agents.md` §二）、把整段调用编组到 `SynchronizationContext`、事件管线、技能 / MCP / 子代理三个子系统、被禁用工具的闸门。

---

## 二、分层与依赖方向

```
宿主 / demo
  │  ① tree.AsAgentScope()            AgentEx.cs:7
  ▼
WorkflowAgentScope                      Agent/Workflow/WorkflowAgentScope.cs
  │  配置 facade：Version / 语言 / 预算 / 开关 / 子系统 / 工具注册
  │
  ├─ CreateToolkit()  ──► WorkflowAgentToolkit          (每个 scope 一个实例，:1317)
  │                          ├─ CreateTools()    受 IsToolEnabled 过滤，给模型看   :74
  │                          └─ CreateAllTools() 不过滤，给宿主 UI 列全             :82
  │                          └─ Tools(ToolPipeline)  每 scope 一个，Refuse=CheckBudget  :242
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

- `WorkflowAgentContextProvider.cs:15-19`：**provider 是工具的唯一来源**。Agent Framework 会把 provider 贡献的工具与 `ChatOptions.Tools` **并集**，且**不按名去重** —— 两条通道给同一个工具，模型就收到两份。所以 `Agent/AgentClientExtensions.cs` 的 `AsAIAgent(providers, instructions)` **刻意没有 tools 参数**。
- `WorkflowAgentToolkit.cs:231-235`：`Tools` 是**每 scope 一个实例**，并且**同一个引用**交给各子系统 provider —— 这是「MCP 或技能贡献的工具与内置工具受同一套预算约束」的实现方式。钩子读 scope 的**实时**值，所以子系统挂上之后再调 `With*` 也能到达它们的工具。
- `WorkflowAgentContextProvider.cs:92`：本 provider 只按 **`_scope.ContextKey`**（= `Version` + 预算用量档，见 `WorkflowAgentScope.cs:1832`）缓存。技能与 MCP 由各自的 provider 渲染，它们的版本变了不会影响这里，按它们开键只会重渲染一个没动过的切片。用量档进键是因为**它是唯一一个不 bump `Version` 也会变的事实**（每次工具调用都在动），而包络会陈述它。

---

## 三、一次工具调用的完整流向

1. 模型调用工具。`WorkflowAgentToolkit.WrapTool`（`:222`）保证**任何 `AIFunction` 型工具**（内置、`WithTools`、技能、MCP）都被 `Agent/TrackedAIFunction.cs:29` 包住。非 `AIFunction` 的工具（如 MCP 客户端原始工具）原样放行，**不受包装**。
2. `TrackedAIFunction` 先把**整段调用**编组到宿主的 `SynchronizationContext`（`TrackedAIFunction.cs:49` → `RunOnContextAsync` `:104`）。**工具体里刻意没有 `ConfigureAwait(false)`** —— 加了会把 await 之后的工作挪到线程池，而那正是「连接校验 / `ExecuteNodes` 的第二个节点 / 执行引擎驱动编译链」发生的地方。
3. 在编组块内、工具体之前，先跑 `ToolPipeline.CheckRefusal`（`TrackedAIFunction.cs:59`）。拒绝 ⇒ 不跑工具体，直接发 `AgentToolCallCompleted{Refused}` 并返回 `{"status":"error",…}`（`:63-64`、`:130`）。
4. `WorkflowAgentToolkit.CheckBudget`（`:277`）按顺序判：**被宿主关掉的工具** → **预算工具本身放行** → **根账本上限（子 scope 才有）** → `MaxToolCalls` → 写/读分档上限。命中就返回 `BudgetRefusal`（`:475`），**文案按「这个 scope 是不是根」分叉**：根被指去 `ResetToolCallLimit`（`LimitRefusal` `:485`），子 scope 被告知「你自己扩不了，向上报告」—— 把孩子指去一个它不持有的工具只会教会它重试。见 `sub-agents.md` §四。
5. 工具体跑完 ⇒ `ReportAsync`（`TrackedAIFunction.cs:90`）发 `AgentToolCallCompleted{Succeeded|Failed}`，带耗时。
6. `AccountingStage`（`WorkflowAgentToolkit.cs:258`）只对 **`Succeeded`** 记账（`:266`）。`AccountAsync`（`:359`）先跳过预算工具本身（否则重置会把自己刚清零的计数再加回去 = 「重置撤销了自己」），再 `_ledger.Spend(IsQueryTool(toolName))`（沿账本链一路上行）、发 `RaiseToolCalledAsync`、按需要 `MarkDirty()`（`:373`）。
7. 事件继续沿 `AgentPipeline` 走到 `TextPipeline` / `ToolPipeline`，写进 `AgentTranscript`。

**为什么 `CheckBudget` 同时管「被关掉」而不只是过滤：** 见 `:279-282` —— 过滤（`CreateTools` 的 `.Where`）只到得了工作流内置工具；这个钩子被所有切片共享，所以一个开关能到达 MCP 和技能的工具。只过滤的话，关掉 `ListSkills` 会**静默无效**。

**为什么 `ResetToolCallLimit` 不受类别过滤、也不被闸门拦**（`:201-203`、`:286-289`）：它是宿主设的预算的唯一出口，恰好在该用到它的时候消失就没意义了。

---

## 四、四条承重的不变量

1. **工具只从 provider 出，不从 `ChatOptions.Tools` 出。** 理由见上（并集不去重）。
2. **每个 scope 一个 `WorkflowAgentToolkit`、一个 `ToolPipeline`、一个 `WorkflowAgentContextProvider`。** 工具计数与状态都在 toolkit 实例上，但**计数不再是三个裸字段**：每个 toolkit 持一个 `ToolCallLedger`（`WorkflowAgentToolkit.cs:31`），根 scope 的账本 `Outer == null`，被 spawn 出来的子 scope 经 `WorkflowAgentScope.ParentLedger`（`:1305`）接到父的账本上。换实例 = 换账本；**没有父子关系时账本的每个成员逐字退化成那三个计数器**，这是既有预算测试仍然有意义的前提。`WorkflowAgentScope.CreateToolkit()` 是 `_toolkit ??= new(this, ParentLedger)`（`:1317`），`CreateTools` 每次调用**新建工具列表**但复用同一个账本。这与 `ProvideTools()`（`:1327`，一次快照）的区别写在 `skills/veloxdev-drive-workflow-with-ai/SKILL.md:43`。详见 `sub-agents.md` §二。
3. **提示词按三个时钟切成三份，各有各的载体。** 混起来就会得到一个每轮重建 870 KB、或者永远过期的提示词。

| 内容 | 变化时钟 | 载体 | 缓存键 |
|---|---|---|---|
| 静态骨架（行为约束、参考文档、类型清单、安全策略） | 构造后从不 | 宿主 `ChatOptions.Instructions` | 构造期一次 |
| **能力包络**（闸门 / 关掉的工具 / 预算 / 挡位漂移 / 类型差量） | 配置变更 + 预算用量档 | provider 的 `AIContext.Instructions`（框架**追加**在骨架之后，逐字 `"骨架\n"` 前缀稳定） | `ContextKey` = `Version` + 用量档 |
| 预算消耗 | 每轮 | 包络内，**分档**：仅 ≥ 上限 80% 时出现，20% 一档 | 靠分档自然稳定 —— 整个预算生命周期最多变 2 次 |

**骨架收据**（`_skeletonReceipt`，`WorkflowAgentScope.cs:1757`）是「不说两遍」的机制：骨架渲染时记下它写了什么快照，包络只补漂移的那几段，并用「取代上文」措辞。骨架从没被渲染过时，包络全量输出。**代价**：它假定「渲染出来的骨架就是交给模型的那份」—— 拿去当预览/日志渲染会让包络以为已经说过。

**图状态刻意不进包络**：渐进披露，模型自己 `ListNodes`。

4. **一片提示文本只有一个主人，第二个到场的人让位。** 静态骨架与技能子系统都能渲染嵌入语料：`ProvideProgressiveContextPrompt()` 在 `Skills == null` 时把语料拼进骨架，而 `SkillAgentContextProvider` 本也读 `BuildEmbeddedBlock`。同一个 scope 两样都做 ⇒ 每篇技能文档进两次。解法是让**后接的一方**知情：`WorkflowAgentScope` 用 `_embeddedSkillsFrozenIntoPrompt`（sticky，一旦有骨架带着语料出门就置位）把事实传给 `SkillScope.CreateContextProvider(..., embeddedCorpusDelivered:)`，provider 于是改贡献 `SkillScope.BuildWithdrawnBlock`（**当前停用的嵌入技能名单**）而不是正文。**为什么不让它闭嘴**：骨架冻住的是当时的启用状态，`SetEnabled(name, false)` 若不发声就成了静默空操作。**正确的宿主顺序是先 `WithSkills` 再取骨架**（`AgentHelper.cs` 即是），语料归子系统，差量机制闲置。

  这条是 §三 表格的必然推论：骨架住在宿主的 `ChatOptions.Instructions` 里，构造后**撤不回**。同一推理也适用于「技能/MCP 清单不进包络」（§三 表末段）—— 那两片各有 provider，第三个说话的人就是重复。

---

## 五、入口：我要改 X，先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 工具清单 / 分类 / 某个工具的 JSON 形状 | `Agent/Workflow/Functions/WorkflowAgentToolkit.cs`（约 3000 行） |
| 哪些工具算「只读」、脏标记怎么落 | `WorkflowAgentToolkit.cs:332`（`BuildQueryToolNames`）+ `:373` |
| 工具预算（三档、拒绝文案、重置流程） | `WorkflowAgentToolkit.cs:277`（`CheckBudget`）、`:359`（`AccountAsync`）、`:390`（`ResetToolCallLimit`）、`:475`（`BudgetRefusal` 的根/子分叉）、`Agent/Workflow/Functions/ToolCallLedger.cs`（树共享的那口锅）。**上限本身**由 `WithMax{Tool,Read,Write}ToolCalls` 设，三个都 `BumpVersion()` —— 门（`CheckBudget`）读的是实时属性，包络读的是缓存文本，不 bump 就会让模型读到一个不被执行的上限 |
| 子代理（派发 / 窄化 / 任意深度为什么终止） | `Agent/SubAgents/`（见 `sub-agents.md`） |
| 提示词（行为约束、失败处理、渐进模式） | `Agent/Workflow/WorkflowAgentScope.cs:1079`（`ProvideProgressiveContextPrompt`）、`:645`（`BuildFailureHandlingProtocol`） |
| 各级安全挡位注入什么 | `WorkflowAgentScope.cs:597`（`BuildInteractionSafetyPrompt`）+ `Resources/Workflow/{lang}/Safety/*.md` |
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
| `ReadSafetyFiles` | `AgentEmbeddedResources.cs:114` | **零调用者**。单个 `ReadSafety`（`:108`）是活的（`WorkflowAgentScope.cs:609`、`:616`） |
| `ReadReference`（单个）/ `ListReferences` | `AgentEmbeddedResources.cs:86 / :92` | **零调用者**（只有一个「全读」的 `ReadAllReferences` 是活的：`WorkflowAgentScope.cs:1036`、`:1159`） |
| `ProvideAllContexts` | `WorkflowAgentScope.cs:1023 / :1025` | **仓库内零外部调用者**（无参重载只转发给有参重载）。是给宿主的另一档提示模式，宿主样例走的是渐进模式（`AgentHelper.cs` 调 `ProvideProgressiveContextPrompt`）。**它和渐进模式一样会写骨架收据**，所以两档都算「骨架已交付」 |
| `BuildDynamicInstructions()` | `WorkflowAgentScope.cs:1863` | **2b 起不再是预留坑位**：渲染**能力包络**（闸门 / 被关掉的工具 / 调用预算的分档用量 / 相对骨架的漂移段）。按 `ContextKey` 缓存，空闲轮零分配；**绝不调用** `ProvideProgressiveContextPrompt`（骨架一次 870 KB，是它的 1,100 倍） |
| `WorkflowToolCategory.Layout`（`1<<5`）、`Composite`（`1<<8`） | `Agent/Workflow/Functions/WorkflowToolCategory.cs` | 保留位，**没有工具注册在这两个类别下**（与 `WorkflowAgentToolkit.cs:185-186` 的「不做复合工具」一致） |

**推论**：改 `Resources/` 时不要以为加一个 `Scripts/` 目录就会被自动加载 —— 加载器在（`ListScriptCategory`），调用者在（`ReadAllScripts`），但**没有第三方调用它**。同理，加 `Safety/Level4.md` 不会有任何效果，档位取值域是 `_interactionSafety > 0` 且文件名按 `$"Level{_interactionSafety}"` 拼（`WorkflowAgentScope.cs:616`），级别的语义定义在 `McpSelfServiceLevel.cs` 之外的那套交互挡位上，要新加挡位必须同时改宿主。

---

## 七、平台差异

**本模块没有平台差异轴。** `netstandard2.0`（`VeloxDev.Core.Extension.csproj:4`），只依赖 `SynchronizationContext` 抽象，七家 GUI 一视同仁。

平台差异出现在**宿主接线**上：各家 demo 自己决定把 agent 面板、MCP 状态面板挂在哪、用什么对话框实现 `RequestSelection` / `RequestConfirmation`。要照抄接线方式看 `Examples/Workflow/Avalonia/Demo/Views/Workflow/WorkflowView.axaml.cs`、`Examples/Workflow/Blazor/Demo/Demo/Components/Pages/Workflow.razor.cs`、`Examples/Workflow/Jalium/Demo/MainWindow.cs` 三家。平台侧的通用陷阱见 `memory/modules/TransitionSystem/adapters/<平台>.md` 与 `memory/modules/WorkflowSystem/adapters/<平台>.md`，此处不重复。

---

## 八、附：`ComponentModelEx.cs`（物理上在本项目，名字属于 MVVM）

`Src/Core/VeloxDev.Core.Extension/ComponentModelEx.cs` 的命名空间是 **`VeloxDev.MVVM.Serialization`**，不是 `VeloxDev.AI.*`。它在本项目里的原因只是「需要一个带 Newtonsoft 依赖的地方」，而 `VeloxDev.Core` 是零依赖的。

**它是所有 demo 存/读工作流走的那条路**：`TreeViewModel.cs:316` 的 `this.Serialize()`，以及 Avalonia / Blazor / Jalium / MAUI / WinUI 五家的 `TryDeserialize<TreeViewModel>(...)`。

**读这个文件时值得知道的三点：**

1. **`IndentedSettings` / `CompactSettings` 是缓存的静态实例**（`:74`、`:89`）。理由是 Newtonsoft 的合约缓存挂在 `JsonSerializerSettings` 实例上，每次 `new` 一份就是每次重建整套合约。不带 `SerializationOptions` 的调用返回**缓存实例本身**（`:115`），所以**不要改返回值的属性**。
2. **`WritablePropertiesOnlyResolver`** 把「有无参构造函数 + 可写属性」的 `IEnumerable` 类型当**普通对象**处理，而不是当集合 —— 这是让 `ObservableCollection<…>` 这类带额外状态的集合能按属性序列化的关键。序列化工作流的缩放/锚点相关契约（`_owner` 反写、`[OnSerializing]` 展开世界坐标）在 `memory/modules/WorkflowSystem/` 里，不在这里重复。
3. **`AllowListSerializationBinder` 是被刻意移除的**，文件结尾有注释说明。`WithTypeNameHandling(...)` 仍然开放（`:40`）—— 宿主打开它就等于接受类型名反序列化的攻击面，这是**宿主的选择**，不是模块的默认。

