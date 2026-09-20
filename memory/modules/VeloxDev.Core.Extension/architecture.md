# VeloxDev.Core.Extension — 架构

> 代码：`Src/Core/VeloxDev.Core.Extension/`（46 个源 .cs，`Agent/` 4 + `Agent/MCP/` 8 + `Agent/Skills/` 11 + `Agent/Pipelines/` 7 + `Agent/Dashboard/` 5 + `Agent/Workflow/` 4 + `Agent/Workflow/Functions/` 5，外加根目录 `AgentEx.cs`、`ComponentModelEx.cs`）。
> **依赖**：`Src/Core/VeloxDev.Core/AI/`（命名空间 `VeloxDev.AI`，13 个文件）。本模块**是它的调用方**，Core 对本科目零引用。
> 嵌入资源：`Resources/Workflow/{en,zh}/{References,Safety,Skills}/`，32 个 .md。
> 宿主样例：`Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/AgentHelper.cs`。
> 测试：`Src/Core/VeloxDev.Core.Extension.Test/`（24 个文件）。

本文只写「读完这些文件才知道的东西」。类型清单、成员表、继承树请看 IDE。

---

## 一、这个模块是什么，不解决什么

**是什么。** 把一棵工作流树（`IWorkflowTreeViewModel`）包成一个 **MAF agent 可读可写、且被宿主节制的工具面**。三件事：

1. **统一的追踪包装** —— 任意来源的工具（内置的、`WithTools` 注册的、技能贡献的、MCP 服务器贡献的）都会被同一个包装器接管，于是「编组到宿主线程 / 计预算 / 触发回调 / 标脏」只有一处实现（`Agent/Workflow/Functions/WorkflowAgentToolkit.cs:194`）。
2. **上下文渲染** —— 每轮把「框架有哪些类型 / 图现在长什么样 / 有哪些技能与 MCP 服务器」渲染成提示，按 `Version` 缓存。
3. **三块可独立使用的子系统** —— Skills、MCP、Dashboard（宿主 UI 的只读镜像）。

**不解决什么：**

| 不在本模块内 | 实际归谁 |
|---|---|
| 命令发现、参数绑定、属性读写、按名解析类型 | 大多在 `Src/Core/VeloxDev.Core/AI/`（`AgentCommandDiscoverer` / `AgentMethodInvoker` / `AgentPropertyAccessor` / `AgentTypeResolver`）。**但「命令的发现/调用」与「属性写入」在本模块各有一份分叉实现**：`.../Agent/Workflow/Functions/CommandInvoker.cs:21`（自带的 `DiscoverCommands` 与自带 `CommandDescriptor`，`:171`）、`.../Agent/Workflow/Functions/ComponentPatcher.cs:236`（自带的 `CopyScalarProperties`）。改 Core 的这两条**不会**影响这里实际跑的路径；详见 `memory/modules/AI/architecture.md` |
| undo/redo 栈 | Core。这是**没有复合工具**的理由 —— 见 `WorkflowAgentToolkit.cs:156-158`：「每个操作都是单个组件命令步骤，这样 undo 栈不会被绕过或重复提交」 |
| 对话历史 / 会话状态 | `Microsoft.Agents.AI` 的 `AgentSession`。`AgentTranscript` 不是它，见 `pipelines.md` |
| 模型调用与 tool-calling 循环 | `Microsoft.Agents.AI` |
| 面板控件 | 模块只发出可绑定的 `[VeloxProperty] partial` 对象，控件在宿主（`Examples/Workflow/Jalium/Demo/MainWindow.cs:650`、`Examples/Workflow/Blazor/Demo/Demo/Components/Pages/Workflow.razor.cs:24`、`Examples/Workflow/Avalonia/Demo/Views/Workflow/WorkflowView.axaml:11`） |

**本模块自己加的，不是 AI 层给的**：工具预算（三档 + 逃生舱）、把整段调用编组到 `SynchronizationContext`、事件管线、技能与 MCP 两个子系统、被禁用工具的闸门。

---

## 二、分层与依赖方向

```
宿主 / demo
  │  ① tree.AsAgentScope()            AgentEx.cs:7
  ▼
WorkflowAgentScope                      Agent/Workflow/WorkflowAgentScope.cs
  │  配置 facade：Version / 语言 / 预算 / 开关 / 子系统 / 工具注册
  │
  ├─ CreateToolkit()  ──► WorkflowAgentToolkit          (每个 scope 一个实例，:1276)
  │                          ├─ CreateTools()    受 IsToolEnabled 过滤，给模型看   :46
  │                          └─ CreateAllTools() 不过滤，给宿主 UI 列全             :54
  │                          └─ Tools(ToolPipeline)  每 scope 一个，Refuse=CheckBudget  :214
  │
  ├─ Pipeline ──► TextPipeline ► SharedTools ► CreateAccountingStage()   （ToolPipeline.cs / AgentPipeline.cs）
  │
  └─ CreateContextProviders() ──► [self, Skills, MCP, …WithContextProvider 工厂]  （固定顺序）
                                   │
                                   ▼ 每轮渲染一次
                             AIContext{Instructions, Tools}
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
- `WorkflowAgentToolkit.cs:203-207`：`Tools` 是**每 scope 一个实例**，并且**同一个引用**交给各子系统 provider —— 这是「MCP 或技能贡献的工具与内置工具受同一套预算约束」的实现方式。钩子读 scope 的**实时**值，所以子系统挂上之后再调 `With*` 也能到达它们的工具。
- `WorkflowAgentContextProvider.cs:64-66`：本 provider 只按 **`_scope.Version`** 缓存。技能与 MCP 由各自的 provider 渲染，它们的版本变了不会影响这里，按它们开键只会重渲染一个没动过的切片。

---

## 三、一次工具调用的完整流向

1. 模型调用工具。`WorkflowAgentToolkit.WrapTool`（`:194`）保证**任何 `AIFunction` 型工具**（内置、`WithTools`、技能、MCP）都被 `Agent/TrackedAIFunction.cs:29` 包住。非 `AIFunction` 的工具（如 MCP 客户端原始工具）原样放行，**不受包装**。
2. `TrackedAIFunction` 先把**整段调用**编组到宿主的 `SynchronizationContext`（`TrackedAIFunction.cs:49` → `RunOnContextAsync` `:104`）。**工具体里刻意没有 `ConfigureAwait(false)`** —— 加了会把 await 之后的工作挪到线程池，而那正是「连接校验 / `ExecuteNodes` 的第二个节点 / 执行引擎驱动编译链」发生的地方。
3. 在编组块内、工具体之前，先跑 `ToolPipeline.CheckRefusal`（`TrackedAIFunction.cs:59`）。拒绝 ⇒ 不跑工具体，直接发 `AgentToolCallCompleted{Refused}` 并返回 `{"status":"error",…}`（`:63-64`、`:130`）。
4. `WorkflowAgentToolkit.CheckBudget`（`:249`）按顺序判：**被宿主关掉的工具** → **预算工具本身放行** → `MaxToolCalls` → 写/读分档上限。命中就返回一句 `LimitRefusal`，里面点名 `ResetToolCallLimit`。
5. 工具体跑完 ⇒ `ReportAsync`（`:90`）发 `AgentToolCallCompleted{Succeeded|Failed}`，带耗时。
6. `AccountingStage`（`:230`）只对 **`Succeeded`** 记账（`:238`）。`AccountAsync`（`:314`）先跳过预算工具本身（否则重置会把自己刚清零的计数再加回去 = 「重置撤销了自己」），再 `Interlocked` 计数、发 `RaiseToolCalledAsync`、按需要 `MarkDirty()`。
7. 事件继续沿 `AgentPipeline` 走到 `TextPipeline` / `ToolPipeline`，写进 `AgentTranscript`。

**为什么 `CheckBudget` 同时管「被关掉」而不只是过滤：** 见 `:251-254` —— 过滤（`CreateTools` 的 `.Where`）只到得了工作流内置工具；这个钩子被所有切片共享，所以一个开关能到达 MCP 和技能的工具。只过滤的话，关掉 `ListSkills` 会**静默无效**。

**为什么 `ResetToolCallLimit` 不受类别过滤、也不被闸门拦**（`:175`、`:260-261`）：它是宿主设的预算的唯一出口，恰好在该用到它的时候消失就没意义了。

---

## 四、两条承重的不变量

1. **工具只从 provider 出，不从 `ChatOptions.Tools` 出。** 理由见上（并集不去重）。
2. **每个 scope 一个 `WorkflowAgentToolkit`、一个 `ToolPipeline`、一个 `WorkflowAgentContextProvider`。** 工具计数与状态都在 toolkit 实例上（`_toolCallCount` 等），换实例 = 换账本。`WorkflowAgentScope.CreateToolkit()` 是 `_toolkit ??= new(...)`（`:1276`），`CreateTools` 每次调用**新建工具列表**但复用同一个计数账本。这与 `ProvideTools()`（`:1286`，一次快照）的区别写在 `skills/veloxdev-drive-workflow-with-ai/SKILL.md:43`。

---

## 五、入口：我要改 X，先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 工具清单 / 分类 / 某个工具的 JSON 形状 | `Agent/Workflow/Functions/WorkflowAgentToolkit.cs`（2914 行） |
| 哪些工具算「只读」、脏标记怎么落 | `WorkflowAgentToolkit.cs:290`（`BuildQueryToolNames`）+ `:331` |
| 工具预算（三档、拒绝文案、重置流程） | `WorkflowAgentToolkit.cs:249`（`CheckBudget`）、`:314`（`AccountAsync`）、`:349`（`ResetToolCallLimit`） |
| 提示词（行为约束、失败处理、渐进模式） | `Agent/Workflow/WorkflowAgentScope.cs:1051`（`ProvideProgressiveContextPrompt`）、`:618`（`BuildFailureHandlingProtocol`） |
| 各级安全挡位注入什么 | `WorkflowAgentScope.cs:570`（`BuildInteractionSafetyPrompt`）+ `Resources/Workflow/{lang}/Safety/*.md` |
| 每轮渲染的指令与工具 | `Agent/Workflow/WorkflowAgentContextProvider.cs:62`（`BuildContext`）；**注意它注入的指令恒为 `null`**，见下节 |
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
| `ReadSafetyFiles` | `AgentEmbeddedResources.cs:114` | **零调用者**。单个 `ReadSafety`（`:108`）是活的（`WorkflowAgentScope.cs:582`、`:589`） |
| `ReadReference`（单个）/ `ListReferences` | `AgentEmbeddedResources.cs:86 / :92` | **零调用者**（只有一个「全读」的 `ReadAllReferences` 是活的：`WorkflowAgentScope.cs:1009`、`:1131`） |
| `ProvideAllContexts` | `WorkflowAgentScope.cs:996 / :998` | **仓库内零外部调用者**（无参重载只转发给有参重载）。是给宿主的另一档提示模式，宿主样例走的是渐进模式（`AgentHelper.cs` 调 `ProvideProgressiveContextPrompt`） |
| `BuildDynamicInstructions()` | `WorkflowAgentScope.cs:1545` | `internal`，**恒返回 `null`**。所以 `WorkflowAgentContextProvider` 贡献的指令**永远是 null** —— 指令必须由宿主经 `ChatOptions.Instructions` 传入（`AgentHelper.cs:162` 起的那段接线）。注释里说「工作流特定的块应该放这里」，即这是个**预留的坑位**，不是 bug |
| `WorkflowToolCategory.Layout`（`1<<5`）、`Composite`（`1<<8`） | `Agent/Workflow/Functions/WorkflowToolCategory.cs` | 保留位，**没有工具注册在这两个类别下**（与 `WorkflowAgentToolkit.cs:156-158` 的「不做复合工具」一致） |

**推论**：改 `Resources/` 时不要以为加一个 `Scripts/` 目录就会被自动加载 —— 加载器在（`ListScriptCategory`），调用者在（`ReadAllScripts`），但**没有第三方调用它**。同理，加 `Safety/Level4.md` 不会有任何效果，档位取值域是 `_interactionSafety > 0` 且文件名按 `$"Level{_interactionSafety}"` 拼（`WorkflowAgentScope.cs:589`），级别的语义定义在 `McpSelfServiceLevel.cs` 之外的那套交互挡位上，要新加挡位必须同时改宿主。

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

