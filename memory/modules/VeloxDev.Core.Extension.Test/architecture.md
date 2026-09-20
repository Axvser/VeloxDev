# VeloxDev.Core.Extension.Test — 架构

> 代码：`Src/Core/VeloxDev.Core.Extension.Test/`（27 个 .cs，不含 `bin/`、`obj/`、`TestResults/`；25 个 `[TestClass]`）
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
| 验七家面板的渲染 | `Examples/*/Agent/*` 的 demo 面板。本模块只**守住**面板依赖的 ViewModel 契约（§三） |
| 验 Workflow 的 GUI / 缩放 | 姊妹模块 `Src/Core/VeloxDev.Core.Test/WorkflowSystem/` |
| 验 Core 的过渡 / 时钟 / 主题 | 姊妹模块 |

**为什么 `Examples/AgentTranscriptTests.cs` 长在这里** —— 它的文件头（`:8-14`）写明了：七个平台面板都渲染 `AgentMessageViewModel`，工具调用行的形状（折叠行 + 解析出的状态）就是面板与共享层之间的**全部契约**；这里静默变化会同时打断每家面板，**而不会打断构建**。而只有本项目引了 `Lib`，所以它只能放这。（它的 `using` 是 `Demo.ViewModels` —— 命名空间来自 `Lib`，不是本模块的。）

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
| `RecordingChatClient` / `OfflineAgent.RunOnce` | `Agent/Workflow/RecordingChatClient.cs:15` | 记录型 `IChatClient`：跑一次真 agent，把 `ChatOptions.Tools` 的名字与所有系统文本录下来。用来断言**模型实际被喂了什么**，而不是「provider 说自己会喂什么」。**全模块唯一的共享替身文件** —— 见 §六 |

**工具是按公共注册路径调的，不是反射**：`Agent/Workflow/Functions/WorkflowLifecycleFidelityTests.cs:23-24` 写明走 `scope.ProvideTools()` → `AIFunction.InvokeAsync`，「the same route an AI host uses — not by reflecting into private methods」。

---

## 四、跑起来与实测数字

| 项 | 值 |
|---|---|
| 命令 | `dotnet test Src/Core/VeloxDev.Core.Extension.Test/VeloxDev.Core.Extension.Test.csproj` |
| 测试条数 | **270** |
| 耗时 | **380 ms** |
| 失败 | 0 |

**为什么比姊妹模块快约 40 倍**：这里几乎没有真实时钟。全部真实等待只有两处：

| 文件:行 | 用法 |
|---|---|
| `Agent/Workflow/Functions/ToolThreadAffinityTests.cs:105` | `await Task.Delay(1, ct)` |
| `Agent/Workflow/Functions/WorkflowLifecycleFidelityTests.cs:198,203` | `WaitUntilAsync`：`Stopwatch` + `Task.Delay(5)` 轮询，`timeoutMs = 3000`（`:196`） |

**「等一个后台线程把它做完」这种测试形态只有第二处一个**，其余全是同步断言。这直接决定了 §五。

---

## 五、并行

与姊妹模块**逐字相同**的一行：

```
Src/Core/VeloxDev.Core.Extension.Test/MSTestSettings.cs:1
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
```

但**全项目 0 个 `[DoNotParallelize]`**（姊妹模块有 12 个）。这个差异不是风格，是**结果**：本模块既没有进程级静态写入，也没有真实时钟断言，所以不需要摘出去。

**推论**：往这里加一条测试时，如果引入了「进程级静态状态」或「毫秒级真实时钟断言」，`[DoNotParallelize]` 得**由你自己加** —— 本项目没有先例可抄，抄要去姊妹模块抄（`memory/modules/VeloxDev.Core.Test/architecture.md` §六 列了 12 个类各自的理由）。

---

## 六、组织与覆盖

命名空间 = `VeloxDev.Core.Extension.Test.<目录路径>`（`Agent/Workflow/Functions/` → `VeloxDev.Core.Extension.Test.Agent.Workflow.Functions`）；类名 = `<被测类型>Tests`。

| 目录 | 文件数 | 备注 |
|---|---|---|
| `Agent/` | 24 | 含 `Workflow/` 11（7 直接 + `Functions/` 4）、`MCP/` 5、`Skills/` 3、`Pipelines/` 2、`Dashboard/` 1，以及直接放在 `Agent/` 下的 2 |
| `Examples/` | 1 | `AgentTranscriptTests.cs`（守 demo 面板的契约，见 §一） |
| `Serialization/` | 1 | `ComponentModelExTests.cs` |
| 根 | 1 | `MSTestSettings.cs` |

**与姊妹模块的结构性差异**（加测试时最容易踩的四个反直觉点）：

| | `VeloxDev.Core.Test` | `VeloxDev.Core.Extension.Test` |
|---|---|---|
| `GlobalUsings.cs` | 有（2 条） | **没有** → 每个文件自己写全 using（连 `System.Threading` 都显式写） |
| 共享替身文件（如 `TestHosts.cs`） | 有 | **有且仅有一个**：`Agent/Workflow/RecordingChatClient.cs`（+ 同文件的 `OfflineAgent.RunOnce`）。`AgentCapabilityProvidersTests` 与 `CapabilityEnvelopeTests` 两个消费者出现后才提取的 —— 其余仍是每类各持私有辅助，第一个消费者出现时不要急着上提 |
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

---

## 七、入口

| 我想改 | 打开 |
|---|---|
| 并行度 | `MSTestSettings.cs:1` |
| 加一个模型替身 | `Agent/Pipelines/AgentPipelineTests.cs:26` 的 `ScriptedChatClient`（目前私有） |
| 加一个同步上下文替身 | `Agent/Workflow/Functions/ToolThreadAffinityTests.cs:27` 的 `SingleThreadContext` |
| 让测试能用生成器类型 | `VeloxDev.Core.Extension.Test.csproj:24-32` 那组引用 |
| 声明一个生成类型 | `Agent/Workflow/Functions/WorkflowSerializationTests.cs:12-30`（唯一先例） |
| 等后台线程做完 | `Agent/Workflow/Functions/WorkflowLifecycleFidelityTests.cs:196` 的 `WaitUntilAsync` |
