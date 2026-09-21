# VeloxDev.Core.Extension.Test — 扩展

面向两件事：**往这个项目里加一条测试**，和**加完之后别忘了动哪里**。

---

## 一、新增一条测试放哪

按目录镜像 `Src/Core/VeloxDev.Core.Extension/`，**不要新建顶层目录**：

| 被测的东西在 | 测试写到 | 命名空间 |
|---|---|---|
| `…Core.Extension/Agent/Skills/Xxx` | `Agent/Skills/XxxTests.cs` | `VeloxDev.Core.Extension.Test.Agent.Skills` |
| `…Core.Extension/Agent/SubAgents/Xxx` | `Agent/SubAgents/XxxTests.cs` | `VeloxDev.Core.Extension.Test.Agent.SubAgents` |
| `…Core.Extension/Agent/MCP/Xxx` | `Agent/MCP/XxxTests.cs` | `VeloxDev.Core.Extension.Test.Agent.MCP` |
| `…Core.Extension/Agent/Workflow/Functions/Xxx` | `Agent/Workflow/Functions/XxxTests.cs` | `VeloxDev.Core.Extension.Test.Agent.Workflow.Functions` |
| `…Core.Extension/Agent/Pipelines/Xxx` | `Agent/Pipelines/XxxTests.cs` | `VeloxDev.Core.Extension.Test.Agent.Pipelines` |
| 只有 demo 面板消费的契约（来自 `Lib`） | `Examples/XxxTests.cs` | `VeloxDev.Core.Extension.Test.Examples` |

**命名惯例：**

- 文件名与类名 = `<被测类型>Tests`；`[TestClass]` + `[TestMethod]`；参数化用 `[DataRow]`。
- **必须自己写全 using** —— 本项目**没有** `GlobalUsings.cs`（姊妹模块有）。`Microsoft.VisualStudio.TestTools.UnitTesting` 是 csproj `:35` 全局注入的，那一条不用写。
- 私有桩写成**文件内 `private sealed class`**，名字带语义（`ScriptedChatClient`、`SingleThreadContext`）。本项目**有两个共享替身文件**：`Agent/Workflow/RecordingChatClient.cs`（含同文件的 `OfflineAgent.RunOnce`，消费者是 `AgentCapabilityProvidersTests` 与 `CapabilityEnvelopeTests`）与 `Agent/SubAgents/SubAgentDoubles.cs`（六个互相咬着的类型，见 `memory/modules/VeloxDev.Core.Extension.Test/architecture.md` §三）。前者是**两个消费者都出现之后**才提取的，后者是一次到位；其余仍是每类各持私有辅助 —— 第一个消费者出现时不要急着上提。

**要不要注册到某个公共 fixture —— 不要。**

| 你想做的事 | 本模块的做法 |
|---|---|
| 「让所有测试能拿到一个脚本化的模型」 | **不注册**。`ScriptedChatClient` 留在 `AgentPipelineTests.cs:26` 私有；第二个消费者出现时再上提成本文件的 `internal`，**仍然不进共享文件** |
| 「让所有测试跑在一个同步上下文里」 | **不注册**。`SingleThreadContext`（`ToolThreadAffinityTests.cs:27`）是 `IDisposable`，必须 `using` 包住 —— 全局注册一个只会泄漏线程 |
| 「把测试产生的临时目录收在一处」 | **目前没有**。`SkillScopeTests.cs:21` 自建 `%TEMP%\veloxdev-skill-tests\<guid>`，且**没有 `[TestCleanup]`、没有 `Directory.Delete`** —— 每跑一次就漏一个目录。这是现状而不是做法：**新写的临时目录测试请配 `[TestCleanup]` 删掉** |

---

## 二、官方做法 vs 看着能编译、但错的捷径

| 错的捷径 | 为什么错 | 官方做法 |
|---|---|---|
| 起一个 `HttpClient` / 真连 MCP 端点 | 本模块**只有一处例外**（下面那条），其余从不发真实网络请求；URL 只用 `example.com` / `.invalid` / `localhost` **构造选项**（`.invalid` 是 RFC 保留不解析域，刻意选的） | 断言「选项被正确构造」，或手写替身 |
| 真接一个模型来判断管线对 | 会把「管线对不对」和「模型的脾气」耦死，且测试变成非确定性 | `ScriptedChatClient`（`AgentPipelineTests.cs:26`）重放固定流。**唯一的例外**是 `Agent/SubAgents/SubAgentLiveTests.cs` 的 2 条门控实测 —— 它们问的正是「离线替身答不了的问题」（模型会不会真的调用 `SpawnSubAgent`），因此读 `API_KEY_DEEPSEEK`，缺失时 `Assert.Inconclusive` 报成**已跳过**（MSTest 4.0.2 实测确认），有 key 时整轮多花约 6 s |
| 反射进私有方法去调工具 | 测的是私有形状，重构就红，而与 AI 宿主真实走的路径无关 | 走公共路径 `scope.ProvideTools()` → `AIFunction.InvokeAsync`，见 `WorkflowLifecycleFidelityTests.cs:23-24` |
| 用 `Task.Delay(500)` 等后台线程做完 | 慢（离线部分全靠 0.7 s 这个数量级撑着），且在负载下不稳 | `WaitUntilAsync(cond, msg, timeoutMs: 3000)`（`WorkflowLifecycleFidelityTests.cs:196`）：`Stopwatch` + `Task.Delay(5)` 轮询到条件成立。子代理那批用的是同款但更糙的 `Thread.Sleep(5)` + `Environment.TickCount64`（`SubAgentDoubles.cs:106,535`），因为等的是**另一个线程上的孩子**而不是本线程的 await |
| 直接在测试里声明一个生成类型 | 编译不过 —— analyzer 不随 ProjectReference 传递 | 先确认 `VeloxDev.Core.Extension.Test.csproj:24-32` 那组引用在，再抄 `WorkflowSerializationTests.cs:12-30` 的形状（`[WorkflowBuilder.Node<...>]` + `[VeloxProperty] partial`，且构造函数里要 `InitializeWorkflow()`） |
| 加一条依赖真实时钟的断言，不加 `[DoNotParallelize]` | 方法级并行下会偶发红（姊妹模块已实测 8 次红 1 次） | 要么改成同步断言，要么加 `[DoNotParallelize]` **并在类注释里写明理由** |
| 指望 Release 跑本地生成器 | Release 走的是 NuGet **9.0.0**（`VeloxDev.Core.Extension.Test.csproj:30`），不是本地版本 | 验生成器改动用 `-c Debug`；Release 只用来复现「已发布版本下的行为」 |

---

## 三、什么样的测试在这个仓库里写不出来

**判据：如果一条测试需要一个「真实的 X」，而 X 住在 `Src/Adapters/` 或某个平台程序集里 —— 它不属于本模块，也不属于任何测试项目。**

本模块**不引用任何 GUI 适配器**（没有 `VeloxDev.WPF`/`Avalonia`/`MAUI`/`WinUI`/`WinForms`/`Razor`/`Jalium` 引用）。所以：

| 验不了的东西 | 只能靠什么 |
|---|---|
| 真实 UI 线程的 `SynchronizationContext` 语义 | 手写替身（`SingleThreadContext`）能验「逻辑对不对」，**验不了「平台是不是这样」** |
| 七家 Agent 面板的实际渲染 | `Examples/*/Agent/*` 的 demo 手工跑 |
| `AgentMessageViewModel` 在真面板里长什么样 | **没有任何面板渲染它** —— 全仓核对过（`.axaml`/`.xaml`/`.razor`/`.cs`）：7 个面板绑的是 `TreeViewModel.AgentLog`（来自 `ToPlainTextLines()`），Avalonia 绑 `ConversationMarkdown`（来自 `ToMarkdown()`）。本模块能守的只有这个**结构化模型自身的形状契约**（`Examples/AgentTranscriptTests.cs` 就是这个定位），**不是**「面板渲染成什么样」 |
| MCP 服务器的真实握手 / `stdio` / HTTP 传输 | 手写替身 + demo 手验；真连接只能靠集成环境 |
| 模型真实行为的任何断言（有无推理、工具选择偏好） | **连 demo 都验不了稳定性**。这类断言属于端点/提示词工程，不属于本仓库的测试 |
| Workflow 的 GUI 缩放 / 拖拽 | 姊妹模块 `VeloxDev.Core.Test/WorkflowSystem/` 的**纯数据**模拟，或 `Examples/Workflow/<GUI>/Demo` |

**注意第一条与第二条的落差**：本模块能做的最强的事是「用手写替身把逻辑时序钉死」，而「平台是不是真的这么调度」只能靠 demo 手验。**不要为了补这个落差去引一个 GUI 程序集** —— 那会让本模块的 0.7 s 与零平台依赖同时消失。

---

## 四、联动清单：改 X 时要同步动哪里

漏一处这里的后果通常是**静默少一条覆盖或编译期换了个生成器版本**，不报错。

| 你改了 | 必须同步 |
|---|---|
| `Examples/Workflow/Common/Lib/` 的 `AgentMessageViewModel`（或 `AgentMessageRole`） | `Examples/AgentTranscriptTests.cs`，**并且**注意它守的不是面板渲染：7 个面板绑 `TreeViewModel.AgentLog`、Avalonia 绑 `ConversationMarkdown`，**没有任何地方绑 `AgentMessageViewModel` 或按 `AgentMessageRole` 分支**。本项目是唯一引 `Lib` 的测试项目，所以这条契约只有这里能守 |
| `VeloxDev.Core.Extension` 里 `AgentTranscript.ToMarkdown` 的渲染形状（含 `AgentMarkdownOptions`） | 两处：库侧 `Agent/Pipelines/AgentTranscriptTests.cs`（形状本身，含围栏长度与 info string 消毒）、`Examples/AgentTranscriptTests.cs` 的 `ConversationMarkdown_WrapsReasoningInAFence`（形状经 `TreeViewModel` 转发到 Avalonia 绑定的那个字符串）。**Avalonia 面板一行 markup 都不按角色分支**，所以面板侧没有编译期信号 |
| 生成器的版本（**9 处** `PackageReference` 硬编码） | `VeloxDev.Core.Extension.Test.csproj:30` 的 `Version="9.0.0"`。这是 `Src/Generators/VeloxDev.Core.Generator/` 那条版本轴的落点之一：Debug 走本地源码、Release 走这个包，改不全会让 Release **静默继续用旧生成器**（同一版本号散落在 9 处 `PackageReference`，升版本必须全改 —— 清单见 `memory/modules/VeloxDev.Core.Generator/extension.md` §四） |
| 工作流模型 / 序列化的形状 | `Agent/Workflow/Functions/WorkflowSerializationTests.cs:98,164` 两条「缩放后保存再加载」回归 —— 它们是唯一用生成类型跑的测试 |
| `VeloxDev.Core.Extension` 新增一个 `Agent/Dashboard/*MemberViewModel` | 本模块对那四个的引用是 **0**；要覆盖得新写，别以为 `AgentDashboardViewModelTests.cs` 管到了 |
| `AIFunction` / `ProvideTools()` 的注册路径 | **33 个测试类文件里有 6 个**直接调 `scope.ProvideTools()`（`AgentDashboardViewModelTests.cs`、`BudgetResetTests.cs`、`CapabilityEnvelopeTests.cs`、`ToolThreadAffinityTests.cs`、`WorkflowLifecycleFidelityTests.cs`、`ToolSwitchTests.cs`）；把「经工具路径」放宽到 `ProvideTools|ProvideToolkit|CreateTools|RunOnce|InvokeAsync` 则命中 17 个文件，其中 **15 个是 `[TestClass]`**（另两个是共享替身文件 `RecordingChatClient.cs` 与 `SubAgentDoubles.cs` —— 后者就是替子代理调工具的那个入口）。一起会红 —— 这是**刻意的**，那几处正是「按公共路径调用」的守卫 |
| 工具线程归属 / 亲和性 | `Agent/Workflow/Functions/ToolThreadAffinityTests.cs`（`SingleThreadContext` 是唯一范本） |
| 子代理的能力面（`Agent/SubAgents/`、`ToolCallLedger`、`WorkflowAgentScope.WithSubAgents`） | `Agent/SubAgents/` 那 7 个 `[TestClass]`，按职责分：窄化与两份名单 → `SubAgentNarrowingTests.cs`、一口锅与夹紧算术 → `SubAgentBudgetTests.cs`、名册隔离 / 深度 / 任意深度 → `SubAgentHierarchyTests.cs`、派发 / 等待 / 取消 / 超时 → `SubAgentDispatchTests.cs`、工具的 JSON schema 与可选参数 → `SubAgentToolSchemaTests.cs`、树面板 → `SubAgentTreeViewModelTests.cs`。改之前先读 `memory/modules/VeloxDev.Core.Extension/sub-agents.md`，那里面写着哪几处是**故意**的（`ResetChain` 只向上、拒绝文案分叉、`[]` ≠ `null`） |
| `Examples/Workflow/Common/Lib/` 里 `AgentHelper.Install` 的构造期行为 | **先把无 key 的机器想清楚**。`Install` 是 `async void`（`AgentHelper.cs:135`），里面 `await ProvideAgent`（`:140`）在缺 `API_KEY_DEEPSEEK` 时于 `:285` 抛，异常逃逸到线程池 → **测试宿主进程崩溃**。实测：无 key 跑全量，整轮在跑到 122~246 条之间被中止（计数是竞态的），被连坐的失败全是**正在跑的**子代理测试的超时，没有一条是完整的断言失败。根因在 demo（本仓库当前的选择是不动它），但**凡是构造 `TreeViewModel` + `AgentHelper` 的测试都踩在同一颗雷上**（`Examples/AgentTranscriptTests.cs:136,138`）。判据、复现与三条隔离实验写在 `memory/modules/VeloxDev.Core.Extension.Test/architecture.md` §四 |
| 给某条测试引入进程级静态写入或真实时钟 | 自己加 `[DoNotParallelize]`，并照姊妹模块的风格在类注释里写明理由 —— 本项目**没有先例可抄** |

---

## 五、给这个模块写记忆 / 复核时的注意

- 依据只能是 `.cs` / `.csproj` 的行号。`TestResults/*.trx` 是 gitignored 本地产物，**不能当依据**。
- 本项目离线部分跑得快（约 0.7 s / 350 条），所以「多跑几遍」的成本很低 —— 但**它没有偶发失败的历史**这个说法要按下面两条拆开来看：稳定来自「离线部分没有真实时钟 + 0 个 `[DoNotParallelize]`」这两条代码事实；而**「无 key 的机器上红」不是偶发，是必现**（根因在 `Examples/` 的 `async void`，见 §四），只是以前整轮只要 0.45 s、赶不上那个竞态，现在整轮长了才露出来。
- 有 `API_KEY_DEEPSEEK` 时整轮 6–7 s，其中约 6 s 是那 2 条门控测试在真调模型。**报数字要说清是哪一种**，否则「本模块很快」和「本模块要跑 7 秒」听起来像在互相打脸。
