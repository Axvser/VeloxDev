# VeloxDev.Core.Extension.Test — 扩展

面向两件事：**往这个项目里加一条测试**，和**加完之后别忘了动哪里**。

---

## 一、新增一条测试放哪

按目录镜像 `Src/Core/VeloxDev.Core.Extension/`，**不要新建顶层目录**：

| 被测的东西在 | 测试写到 | 命名空间 |
|---|---|---|
| `…Core.Extension/Agent/Skills/Xxx` | `Agent/Skills/XxxTests.cs` | `VeloxDev.Core.Extension.Test.Agent.Skills` |
| `…Core.Extension/Agent/MCP/Xxx` | `Agent/MCP/XxxTests.cs` | `VeloxDev.Core.Extension.Test.Agent.MCP` |
| `…Core.Extension/Agent/Workflow/Functions/Xxx` | `Agent/Workflow/Functions/XxxTests.cs` | `VeloxDev.Core.Extension.Test.Agent.Workflow.Functions` |
| `…Core.Extension/Agent/Pipelines/Xxx` | `Agent/Pipelines/XxxTests.cs` | `VeloxDev.Core.Extension.Test.Agent.Pipelines` |
| 只有 demo 面板消费的契约（来自 `Lib`） | `Examples/XxxTests.cs` | `VeloxDev.Core.Extension.Test.Examples` |

**命名惯例：**

- 文件名与类名 = `<被测类型>Tests`；`[TestClass]` + `[TestMethod]`；参数化用 `[DataRow]`。
- **必须自己写全 using** —— 本项目**没有** `GlobalUsings.cs`（姊妹模块有）。`Microsoft.VisualStudio.TestTools.UnitTesting` 是 csproj `:35` 全局注入的，那一条不用写。
- 私有桩写成**文件内 `private sealed class`**，名字带语义（`ScriptedChatClient`、`SingleThreadContext`）。本项目**没有共享 fixture 文件**，这是刻意与姊妹模块相反的：两者的桩没有交集（那边是过渡/时钟宿主，这边是 `IChatClient` 与同步上下文），合并只会造一个谁都不认识的垃圾桶。

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
| 起一个 `HttpClient` / 真连 MCP 端点 | 本模块**从不发真实网络请求**；URL 只用 `example.com` / `.invalid` / `localhost` **构造选项**（`.invalid` 是 RFC 保留不解析域，刻意选的） | 断言「选项被正确构造」，或手写替身 |
| 真接一个模型来判断管线对 | 会把「管线对不对」和「模型的脾气」耦死，且测试变成非确定性 | `ScriptedChatClient`（`AgentPipelineTests.cs:26`）重放固定流 |
| 反射进私有方法去调工具 | 测的是私有形状，重构就红，而与 AI 宿主真实走的路径无关 | 走公共路径 `scope.ProvideTools()` → `AIFunction.InvokeAsync`，见 `WorkflowLifecycleFidelityTests.cs:23-24` |
| 用 `Task.Delay(500)` 等后台线程做完 | 慢（本模块全靠 739ms 这个数量级撑着），且在负载下不稳 | `WaitUntilAsync(cond, msg, timeoutMs: 3000)`（`WorkflowLifecycleFidelityTests.cs:196`）：`Stopwatch` + `Task.Delay(5)` 轮询到条件成立 |
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
| `AgentMessageViewModel` 在真面板里长什么样 | 同上。本模块能守的只有它的**形状契约**（`Examples/AgentTranscriptTests.cs` 就是这个定位） |
| MCP 服务器的真实握手 / `stdio` / HTTP 传输 | 手写替身 + demo 手验；真连接只能靠集成环境 |
| 模型真实行为的任何断言（有无推理、工具选择偏好） | **连 demo 都验不了稳定性**。这类断言属于端点/提示词工程，不属于本仓库的测试 |
| Workflow 的 GUI 缩放 / 拖拽 | 姊妹模块 `VeloxDev.Core.Test/WorkflowSystem/` 的**纯数据**模拟，或 `Examples/Workflow/<GUI>/Demo` |

**注意第一条与第二条的落差**：本模块能做的最强的事是「用手写替身把逻辑时序钉死」，而「平台是不是真的这么调度」只能靠 demo 手验。**不要为了补这个落差去引一个 GUI 程序集** —— 那会让本模块的 739ms 与零平台依赖同时消失。

---

## 四、联动清单：改 X 时要同步动哪里

漏一处这里的后果通常是**静默少一条覆盖或编译期换了个生成器版本**，不报错。

| 你改了 | 必须同步 |
|---|---|
| `Examples/Workflow/Common/Lib/` 的 `AgentMessageViewModel`（或 `AgentMessageRole`） | `Examples/AgentTranscriptTests.cs` —— 它是七家面板的契约守卫，本项目是唯一引 `Lib` 的测试项目 |
| 生成器的版本（**9 处** `PackageReference` 硬编码） | `VeloxDev.Core.Extension.Test.csproj:30` 的 `Version="9.0.0"`。这是 `Src/Generators/VeloxDev.Core.Generator/` 那条版本轴的落点之一：Debug 走本地源码、Release 走这个包，改不全会让 Release **静默继续用旧生成器**（同一版本号散落在 9 处 `PackageReference`，升版本必须全改 —— 清单见 `memory/modules/VeloxDev.Core.Generator/extension.md` §四） |
| 工作流模型 / 序列化的形状 | `Agent/Workflow/Functions/WorkflowSerializationTests.cs:98,164` 两条「缩放后保存再加载」回归 —— 它们是唯一用生成类型跑的测试 |
| `VeloxDev.Core.Extension` 新增一个 `Agent/Dashboard/*MemberViewModel` | 本模块对那四个的引用是 **0**；要覆盖得新写，别以为 `AgentDashboardViewModelTests.cs` 管到了 |
| `AIFunction` / `ProvideTools()` 的注册路径 | **21 个测试文件里有 13 个**直接走这条路（`WorkflowLifecycleFidelityTests.cs:29,38,215,221`、`ToolSwitchTests.cs`、`BudgetResetTests.cs`、`AgentDashboardViewModelTests.cs` 等），一起会红 —— 这是**刻意的**，那 13 处正是「按公共路径调用」的守卫 |
| 工具线程归属 / 亲和性 | `Agent/Workflow/Functions/ToolThreadAffinityTests.cs`（`SingleThreadContext` 是唯一范本） |
| 给某条测试引入进程级静态写入或真实时钟 | 自己加 `[DoNotParallelize]`，并照姊妹模块的风格在类注释里写明理由 —— 本项目**没有先例可抄** |

---

## 五、给这个模块写记忆 / 复核时的注意

- 依据只能是 `.cs` / `.csproj` 的行号。`TestResults/*.trx` 是 gitignored 本地产物，**不能当依据**。
- 本项目跑得快（739ms），所以「多跑几遍」的成本很低 —— 但**它没有偶发失败的历史**，别把「跑三遍都绿」当成「结构性稳定」的证明；稳定来自「没有真实时钟 + 0 个 `[DoNotParallelize]`」这两条代码事实。
