# VeloxDev.Core.Extension.Test — 架构

> 代码：`Src/Core/VeloxDev.Core.Extension.Test/`（36 个 .cs，不含 `bin/`、`obj/`、`TestResults/`；33 个 `[TestClass]`）
> 被测：`Src/Core/VeloxDev.Core.Extension/`（AI 工具面，命名空间 `VeloxDev.AI.*`）
> 姊妹模块：`memory/modules/VeloxDev.Core.Test/`。两者只共享「逐字相同的一行并行设置」，其余差异很大 —— 见 §六那张对照表。

本文只写「读完这些文件才知道的东西」。

---

## 一、这是什么、不解决什么

**是什么。** `VeloxDev.Core.Extension`（Agent / MCP / Skills / 管线 / 工作流工具函数）的**纯逻辑**测试宿主。它同时是**整个仓库里唯一引用 `Examples/Workflow/Common/Lib/Lib.csproj` 的测试项目**（`VeloxDev.Core.Extension.Test.csproj:21`）。

**不是什么：**

| 你以为在这里 | 其实在哪 |
|---|---|
| 真调模型 / 真连 MCP 服务器 | **没有**。网络只用于**构造选项对象**：URL 只用 `example.com` / `.invalid` / `localhost` |
| 验七家面板的渲染 | `Examples/*/Agent/*` 的 demo 面板。本模块守住的是**共享层自己的契约**（`AgentLog` 的行数/前缀、`ConversationMarkdown` 的形状、结构化模型映射），**不是面板渲染** —— 没有面板绑 `AgentMessageViewModel`，见 §一 |
| 验 Workflow 的 GUI / 缩放 | 姊妹模块 `Src/Core/VeloxDev.Core.Test/WorkflowSystem/` |
| 验 Core 的过渡 / 时钟 / 主题 | 姊妹模块 |

**为什么 `Examples/AgentTranscriptTests.cs` 长在这里** —— 因为只有本项目引了 `Lib`，所以任何守 `Lib` 契约的测试只能放这。（它的 `using` 是 `Demo.ViewModels` —— 命名空间来自 `Lib`，不是本模块的。）

**但它的守卫对象和文件头一度宣称的不一样，这点已核实并改正**：它守的是**结构化模型**（`AgentMessageViewModel` / `AgentMessageRole`）与 `TreeViewModel` 的 `AgentLog` / `ConversationMarkdown` 之间的契约，**不是「七个平台面板的渲染」**。全仓核对（`.axaml` / `.xaml` / `.razor` / `.cs` 逐类搜）：**没有任何面板绑定 `AgentMessageViewModel`，也没有任何地方按 `AgentMessageRole` 分支**。七个面板实际绑的是 `TreeViewModel.AgentLog`（`ObservableCollection<string>`，来自 `AgentTranscript.ToPlainTextLines()`），Avalonia 例外，绑 `ConversationMarkdown`（来自 `ToMarkdown()`）。

后果有两面，都值得记住：

- `AgentMessages` 这条结构化列表在实跑路径上近乎空集 —— 它是**第三条形状**，为「想要逐角色渲染的宿主」保留着。往 `AgentMessageRole` 加成员（如 `Reasoning`）**不会改变任何界面**。
- 因此这里的断言是**唯一的守卫**：静默改动会打断面板与共享层的契约，**而不会打断构建**，且没有任何像素能替你发现它。

---

## 二、最大的一条不对称：本模块引了源生成器

`VeloxDev.Core.Extension.Test.csproj:24-32` 是「生成器不随 ProjectReference 传递」这条仓库通则的**唯一一处测试项目级复现**，且注释就写在旁边：

```xml
<!-- 本项目自己也用生成器特性，而 analyzer 不随 ProjectReference 传递。 -->
<ProjectReference Include="...Generator.csproj" OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" Condition="'$(Configuration)' == 'Debug'" />
<PackageReference Include="VeloxDev.Core.Generator" Version="9.0.0"
                  Condition="'$(Configuration)' != 'Debug'" />
```

| | Debug | Release |
|---|---|---|
| 生成器来源 | 本地 `ProjectReference` | NuGet 包 **9.0.0**（不是本地版本） |
| 本模块能用生成器 | ✅ | ✅，但用的是**已发布的旧版** |

后果分两层：

1. 姊妹模块 `VeloxDev.Core.Test` **没有**这组引用 → 那边写不出任何生成器类型。
2. 本模块是**两个测试项目中唯一能声明生成类型的**：`Agent/Workflow/Functions/WorkflowSerializationTests.cs:19` 的 `TestEnumNode`（`:12` 的 `enum TestRouteKind`、`:18` 的 `[WorkflowBuilder.Node<NodeHelper<TestEnumNode>>(workSemaphore: 1)]`、`:19-30` 的 `[VeloxProperty] partial` 属性 + 构造函数里的 `OutputSlots.SetSelector(typeof(TestRouteKind))`）是全仓库测试项目里**唯一**一处生成类型。

同一文件里的 `TreeZoomedBeforeSave_NodesVisibleAfterRoundTrip`（`:98`）和 `TreeZoomedBeforeSave_NodeDefaultViewModel_KeepsAnchorAndSize`（`:164`）是这条通道唯一的价值证明：断言保存进 JSON 的是**世界**坐标 Anchor，且 getter 在加载后恰好折叠一次。

---

## 三、测试替身（同样没有 mock 库）

csproj 只有 MSTest + coverlet 两个 `PackageReference`(`:11-15`)。

| 替身 | 在哪 | 顶替什么 |
|---|---|---|
| `ScriptedChatClient` | `Agent/Pipelines/AgentPipelineTests.cs:26` | 手写的 `IChatClient`，重放固定流。类注释（`:11-17`）写明它证明的是「管线怎么处理模型输出」，**不是**「某个模型会不会产出推理」—— 后者的所有者是端点，不是这里 |
| `SingleThreadContext : SynchronizationContext, IDisposable` | `Agent/Workflow/Functions/ToolThreadAffinityTests.cs:27` | 专用线程 + 真 `BlockingCollection` 队列；断言线程 id 在进入 / await 后 / 返回时不变 |
| 手写 skill 目录 | `Agent/Skills/SkillScopeTests.cs:21` | `%TEMP%\veloxdev-skill-tests\<guid>` 下写 `SKILL.md` |
| `RecordingChatClient` / `OfflineAgent.RunOnce` | `Agent/Workflow/RecordingChatClient.cs:15` | 记录型 `IChatClient`：跑一次真 agent，把 `ChatOptions.Tools` 的名字与所有系统文本录下来。用来断言**模型实际被喂了什么**，而不是「provider 说自己会喂什么」。**共享替身文件之一**（另一个是下面那个）—— 见 §六 |
| `InstantChatClient` / `GateChatClient` / `ToolCallingChatClient` / `FaultingChatClient` / `CountingUIContext` / `SubAgentFixture` | `Agent/SubAgents/SubAgentDoubles.cs` | 子代理专用的整套离线替身，**同一文件里六个类型**：立即答完 / 卡在 `TaskCompletionSource` 上等测试放行（并发、超时、取消全靠它）/ 第二轮往返时回一个指定名字的工具调用（于是轮询路径能离线穿过真 agent）/ 抛异常 / 记录线程 id 的 `SynchronizationContext` / 组装 `WorkflowAgentScope` + 父 `SubAgentScope` + 按名调工具的入口。**`GateChatClient.WaitForCalls`（`:104`）与 `SubAgentFixture.WaitFor`（`:451`）都是 `Thread.Sleep(5)` 轮询、超时 5000 ms**，所以在无 key 的机器上跑全量时它们会成为本模块唯一的真实等待（见 §四） |

**工具是按公共注册路径调的，不是反射**：`Agent/Workflow/Functions/WorkflowLifecycleFidelityTests.cs:23-24` 写明走 `scope.ProvideTools()` → `AIFunction.InvokeAsync`，「the same route an AI host uses — not by reflecting into private methods」。

---

## 四、跑起来与实测数字

| 项 | 值 |
|---|---|
| 命令 | `dotnet test Src/Core/VeloxDev.Core.Extension.Test/VeloxDev.Core.Extension.Test.csproj` |
| 测试条数 | **352**（含 `Agent/SubAgents/SubAgentLiveTests.cs` 的 2 条门控实测） |
| 耗时 | **0.7 s**（无 key，2 条被跳过）/ **6–7 s**（有 `API_KEY_DEEPSEEK`，那 2 条真的走网络） |
| 失败 | 0 |

**为什么离线部分比姊妹模块快得多**：这里几乎没有真实时钟。全部真实等待只有三处：

| 文件:行 | 用法 |
|---|---|
| `Agent/Workflow/Functions/ToolThreadAffinityTests.cs:105` | `await Task.Delay(1, ct)` |
| `Agent/Workflow/Functions/WorkflowLifecycleFidelityTests.cs:198,203` | `WaitUntilAsync`：`Stopwatch` + `Task.Delay(5)` 轮询，`timeoutMs = 3000`（`:196`） |
| `Agent/SubAgents/SubAgentDoubles.cs:104,451` | `WaitForCalls` / `WaitFor`：`Environment.TickCount64` + `Thread.Sleep(5)` 轮询，`timeoutMs = 5000` |

前两处是「等一个后台线程把它做完」；子代理那两处是**「等一个孩子跑到某一步」**，形态更接近并发测试 —— 这也解释了有 key 时的 6–7 s：那 2 条门控测试在真调模型，与它们并行的子代理测试各自在轮询自己的 `Thread.Sleep(5)`。这直接决定了 §五。

### ⚠ 无 key 的机器上，**全量**跑会红 —— 但原因不在本模块

已实测：不带 `API_KEY_DEEPSEEK` 跑全量，会有约 15 条子代理测试报 `TimeoutException` 并伴随 `测试主机进程崩溃`：

```
活动的测试运行已中止。原因: 测试主机进程崩溃 : Unhandled exception.
System.InvalidOperationException: Environment variable 'API_KEY_DEEPSEEK' is not configured.
   at Demo.ViewModels.Workflow.Helper.AgentHelper.ProvideAgent(...) AgentHelper.cs:line 266
   at Demo.ViewModels.Workflow.Helper.AgentHelper.Install(...)      AgentHelper.cs:line 124
   at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__124_1(Object state)
```

**直接原因是 `Examples/` 的既有缺陷，不是子代理代码**：`AgentHelper.Install` 是 `public async override void`（`AgentHelper.cs:119`），里面 `await ProvideAgent(...)`（`:186`）在缺 key 时于 `:265` 抛，异常从 `async void` 逃逸到线程池 → **宿主进程崩溃**。触发它的测试是 `Examples/AgentTranscriptTests.cs:138`（那里把一个 `TreeViewModel` 的 `Helper` 转成 `AgentHelper`，构造即 `Install`）。

那么为什么**以前不红**：抛出的时机是竞态的，只有整轮跑得够久它才落在测试宿主收集结果的窗口内。本模块原有的 281 条跑完只要 0.45 s，够快；加了 69 条子代理测试后，无 key 时整轮变成 ~10 s，它就落进来了。三条独立实验钉住了这一点：`FullyQualifiedName~Test.Examples` 单跑绿（167 ms）；`FullyQualifiedName!~Agent.SubAgents` 跑全部其余 281 条也绿（626 ms）；**排除 2 条门控测试、只留下子代理那 69 条，仍然红**。

**结论**：本模块自己的门控约定是成立的 —— `SubAgentLiveTests` 缺 key 时 `Assert.Inconclusive`，MSTest 4.0.2 下报成**已跳过**（`--filter FullyQualifiedName~Agent.SubAgents` 无 key = 69 通过 + 2 跳过，0 失败）。红的是全量轮次，根因在 `Examples/` 的 `async void`。**修它要动 demo，本仓库当前的选择是不动** —— 所以这条要一直记着，别把它误判成本模块的回归。

---

## 五、并行

与姊妹模块**逐字相同**的一行：

```
Src/Core/VeloxDev.Core.Extension.Test/MSTestSettings.cs:1
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
```

但**全项目 0 个 `[DoNotParallelize]`**（姊妹模块有 12 个）。这个差异不是风格，是**结果**：本模块既没有进程级静态写入，也没有真实时钟断言，所以不需要摘出去。

**子代理那一批没有改变这一点，但它把边界推近了一格**：`SubAgentDoubles.cs:104,451` 的两处 `Thread.Sleep(5)` 轮询带着 5000 ms 的墙钟超时，在满载的 CI 上是「真实时钟断言」的雏形。它今天仍然安全，因为超时只用来**把死锁变成失败**而不是断言性能 —— 一个卡住的 `GateChatClient` 会让测试红，而不会让它假绿。加到 `[DoNotParallelize]` 的门槛是「超时值本身成为断言对象」，不是「存在超时」。

**推论**：往这里加一条测试时，如果引入了「进程级静态状态」或「毫秒级真实时钟断言」，`[DoNotParallelize]` 得**由你自己加** —— 本项目没有先例可抄，抄要去姊妹模块抄（`memory/modules/VeloxDev.Core.Test/architecture.md` §六 列了 12 个类各自的理由）。

---

## 六、组织与覆盖

命名空间 = `VeloxDev.Core.Extension.Test.<目录路径>`（`Agent/Workflow/Functions/` → `VeloxDev.Core.Extension.Test.Agent.Workflow.Functions`）；类名 = `<被测类型>Tests`。

| 目录 | 文件数 | 备注 |
|---|---|---|
| `Agent/` | 33 | 含 `Workflow/` 11（7 直接 + `Functions/` 4）、`SubAgents/` 8（7 个 `[TestClass]` + 1 个替身文件）、`MCP/` 5、`Skills/` 3、`Pipelines/` 3、`Dashboard/` 1，以及直接放在 `Agent/` 下的 2 |
| `Examples/` | 1 | `AgentTranscriptTests.cs`（守 demo 面板的契约，见 §一；**也是 §四那个无 key 崩溃的触发者**） |
| `Serialization/` | 1 | `ComponentModelExTests.cs` |
| 根 | 1 | `MSTestSettings.cs` |

**与姊妹模块的结构性差异**（加测试时最容易踩的四个反直觉点）：

| | `VeloxDev.Core.Test` | `VeloxDev.Core.Extension.Test` |
|---|---|---|
| `GlobalUsings.cs` | 有（2 条） | **没有** → 每个文件自己写全 using（连 `System.Threading` 都显式写） |
| 共享替身文件（如 `TestHosts.cs`） | 有 | **有且仅有两个**：`Agent/Workflow/RecordingChatClient.cs`（+ 同文件的 `OfflineAgent.RunOnce`）与 `Agent/SubAgents/SubAgentDoubles.cs`（六个类型，见 §三）。前者是两个消费者（`AgentCapabilityProvidersTests`、`CapabilityEnvelopeTests`）出现后才提取的；后者是子代理那一批**一次到位**的 —— 因为它那六件替身互相咬着（`SubAgentFixture` 造 scope，scope 要 client，`GateChatClient` 要 `CountingUIContext`）。其余仍是每类各持私有辅助，第一个消费者出现时不要急着上提 |
| 源生成器引用 | 无 | **有**（§二），Debug 走本地 / Release 走包 |
| `[DoNotParallelize]` | 12 个类 | **0** |

**未覆盖的类型**（`Src/Core/VeloxDev.Core.Extension/` 里有源文件、本模块**零引用**，逐个核过）：

| 类型 | 源文件 |
|---|---|
| `AgentMemberViewModel` / `McpMemberViewModel` / `SkillMemberViewModel` / `ToolMemberViewModel` | `Agent/Dashboard/` 下四个同名文件 |
| `AgentEvent` | `Agent/Pipelines/AgentEvent.cs` |
| `PipelineDispatch` | `Agent/Pipelines/PipelineDispatch.cs` |
| `AgentEmbeddedResources` | `Agent/AgentEmbeddedResources.cs` |
| `AgentEx` | `AgentEx.cs` |
| `SkillFrontmatter` | `Agent/Skills/SkillFrontmatter.cs` |

`Dashboard/` 有一个测试文件（`AgentDashboardViewModelTests.cs`）却零引用那四个 Member ViewModel —— Dashboard 的覆盖面是**部分**的，不是全缺。

**子代理那一批的边界（这张表为什么没有新增行）**：`Agent/SubAgents/` 里**没有**任何类型落进上表 —— 包括内部的 `SubAgentScope` / `SubAgentAgentToolkit` / `SubAgentAgentContextProvider`，它们因 `InternalsVisibleTo` 被直接构造。真正按名零引用的是三个 `internal`：`ChildBriefing`、`SubAgentEntry`、`ToolCallLedger`。前两个是纯粹的载体（没有行为可断言，它们的字段经由 `SubAgentSummary` 与面板行被检查），**`ToolCallLedger` 不是缺口而是刻意的** —— 它的每一条性质都由 `SubAgentBudgetTests` 从 `SubAgentScope` 那一侧钉住（一口锅、沿路径递减、`ResetChain` 只向上）。**别为它单写一个测试类**：那样就多了一份「账本自己说自己」，而既有那几条断言的价值正在于它们从不直接读账本。

**唯一一条不能离线证明的事**写在 `SubAgentLiveTests` 的类注释里（`Agent/SubAgents/SubAgentLiveTests.cs:25-31`）：工具描述够不够清楚、模型会不会真的调用 `SpawnSubAgent`。离线套件能证明「被调用时是对的」，证明不了「会不会被调用」。

---

## 七、入口

| 我想改 | 打开 |
|---|---|
| 并行度 | `MSTestSettings.cs:1` |
| 加一个模型替身 | `Agent/Pipelines/AgentPipelineTests.cs:26` 的 `ScriptedChatClient`（目前私有） |
| 加一个同步上下文替身 | `Agent/Workflow/Functions/ToolThreadAffinityTests.cs:27` 的 `SingleThreadContext` |
| 加一个「卡住 / 放行」的模型替身 | `Agent/SubAgents/SubAgentDoubles.cs:104` 的 `GateChatClient`（并发、超时、取消全靠它） |
| 造一个父子 scope 现场 | `Agent/SubAgents/SubAgentDoubles.cs:451` 的 `SubAgentFixture`（`DescendantSubAgents(path)` 拿任意一层孙代理的名册） |
| 让测试能用生成器类型 | `VeloxDev.Core.Extension.Test.csproj:24-32` 那组引用 |
| 声明一个生成类型 | `Agent/Workflow/Functions/WorkflowSerializationTests.cs:12-30`（唯一先例） |
| 等后台线程做完 | `Agent/Workflow/Functions/WorkflowLifecycleFidelityTests.cs:196` 的 `WaitUntilAsync` |
| 改 `AgentTranscript` 的渲染形状 | **两个** `AgentTranscriptTests`：库侧形状在 `Agent/Pipelines/AgentTranscriptTests.cs`（当前 8 条），`Lib` 契约转发在 `Examples/AgentTranscriptTests.cs`（当前 9 条） |

**同名类 `AgentTranscriptTests` 出现两次**（`Agent/Pipelines/` 与 `Examples/`，命名空间不同所以合法）。搜类名会拿到两个结果，这不是重复文件 —— 前者守库的输出形状，后者守它经过 `TreeViewModel` 转发到面板绑定字符串的那一跳。改动渲染时必须同时想到两边。
