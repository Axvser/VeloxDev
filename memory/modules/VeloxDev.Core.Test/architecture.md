# VeloxDev.Core.Test — 架构

> 代码：`Src/Core/VeloxDev.Core.Test/`（152 个 .cs，不含 `bin/`、`obj/`、`TestResults/`；132 个 `[TestClass]`，1129 个 `[TestMethod]`）
> 被测：`Src/Core/VeloxDev.Core/`（`Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:5` 是五目标 `netstandard2.0;netframework4.6.1;net5.0;netcoreapp3.0;net8.0`）
> 姊妹模块：`memory/modules/VeloxDev.Core.Extension.Test/`（那个引 `Lib`、测 AI 工具面）。两者的共享面只有「同样一行并行设置」。

本文只写「读完这些文件才知道的东西」。测试方法名不在此列 —— IDE 里按类名跳过去就有。

---

## 一、这是什么、不解决什么

**是什么。** 一个**零 GUI** 的契约测试宿主：项目引 `VeloxDev.Core`（`VeloxDev.Core.Test.csproj:20`）与生成器本身（`:28`，`OutputItemType="Analyzer"`，**不分 Debug/Release**，见 §三），所有平台能力都用手写的**替身**顶替。

**不是什么** —— 这几条决定了「我要验的东西不在这」时该往哪走：

| 你以为在这里 | 其实在哪 |
|---|---|
| 验 GUI / 渲染 / 命中测试 | **哪儿也没有**。两个测试项目都不引用任何 `Src/Adapters/VeloxDev.*`；测试源码里没出现过 `VeloxDev.WPF`/`Avalonia`/`MAUI`/`WinUI`/`WinForms`/`Razor`/`Jalium` 命名空间。要验渲染只能靠 `Examples/<模块>/<GUI>/Demo` 手工跑 |
| 验适配器采样器的覆盖面 | `Examples/Transition/AUTO TEST/`（独立 csproj，不在 `VeloxDev.slnx`） |
| 验 AOT / 裁剪 | `Src/Verification/VeloxDev.TrimProbe/`。**这条边界是双向的**：那目录在工作区里只剩 `bin/Debug/net10.0/win-x64/` 产物，没有源码、无被跟踪文件、不在 `VeloxDev.slnx` —— 它不跑这里的测试，这里的测试也覆盖不到它（它有自己的验证线） |
| 验 CLI 入口 | `Src/CLI/`，独立模块 |
| 跨 TFM 行为一致 | 只有 **net8.0** 这一份资产被真正执行，见 §二 |

---

## 二、多目标与「测的是哪一份 Core」

Core 是五目标项目；**测试项目是单目标 `net10.0`**（`VeloxDev.Core.Test.csproj:4`），所以 ProjectReference 从 Core 的五份资产里挑出**最近兼容的那一份 = `net8.0`**。

实测（两份 DLL 的 md5 相同，故这是硬结论而非推断）：

| 文件 | 字节 | md5 |
|---|---|---|
| `Src/Core/VeloxDev.Core/bin/Debug/net8.0/VeloxDev.Core.dll` | 1110528 | `fd09b4c6ef3f1a5c06f8b5a4f6ce89bd` |
| `Src/Core/VeloxDev.Core.Test/bin/Debug/net10.0/VeloxDev.Core.dll` | 1110528 | 同上（`fd09b4c6ef3f1a5c06f8b5a4f6ce89bd`） |

旁证：`Src/Core/VeloxDev.Core.Test/obj/project.assets.json:1222` 记的 `VeloxDev.Core` 资产的 `"framework": ".NETCoreApp,Version=v8.0"`。

| TFM | 被测试执行? | 后果 |
|---|---|---|
| `net8.0` | ✅ 唯一被跑的 | `#if !NETSTANDARD2_0` 的采样器真的被测（`Src/Core/VeloxDev.Core/TransitionSystem/NativeSamplers/Vector2Sampler.cs:5` 一类）；`#if NET` 的 `AspectOriented/` 被编译进来**且有测试**（`AspectOriented/` 目录） |
| `net5.0` | ❌ | 只有 Debug 构建产物存在，测试不再加载它 |
| `netstandard2.0` | ❌ | 该分支的 `#if` 代码路径永远不被执行 |
| `netcoreapp3.0` | ❌ | 同上 |
| `netframework4.6.1` | ❌ | 同上；polyfill 分支（如 `#if !NET5_0_OR_GREATER` 的 `IsExternalInit`）在此永不进入 |

想把后四档补上，只能给测试 csproj 加 TFM —— 但那会立刻撞上 MSTest 4.0.2 的可用目标集。

---

## 三、框架与测试替身

| 项 | 值 | 依据 |
|---|---|---|
| 测试框架 | **MSTest 4.0.2**（单包 = 框架 + 断言 + 适配器） | `VeloxDev.Core.Test.csproj:12` |
| 覆盖率 | `coverlet.collector` 6.0.4，`PrivateAssets=all` | `:13-16` |
| mock 库 | **没有**。没有 Moq / NSubstitute / FakeItEasy | 全 csproj 的 `PackageReference` 只有 MSTest、coverlet.collector 与一个 `Microsoft.CodeAnalysis.CSharp`（`:31`，供直驱生成器），无一 mock 库 |
| 断言命名空间 | 全局注入 `Microsoft.VisualStudio.TestTools.UnitTesting` | `:35` |
| 全局 using | 只有两条：`VeloxDev.Threading`、`VeloxDev.TimeLine` | `GlobalUsings.cs` |
| 源生成器 | **已引用**（2026-10-02 起）→ 可以 `new VeloxDev.Generators.MVVM()` / `new Command()` 直驱生成器；csproj 把生成器项目同时以 `OutputItemType="Analyzer"` 与普通 `Reference` 引入，并钉 `Microsoft.CodeAnalysis.CSharp` 4.8.0 | `VeloxDev.Core.Test.csproj:27-32` |
| `NoWarn` | `MSTEST0032`、`MSTEST0037`、`CS0067` | `:8` |

**手写替身才是本模块的基础设施**（没有 mock 库的替代品）：

| 替身 | 在哪 | 顶替什么 |
|---|---|---|
| `ImmediateHost` | `TestHosts.cs:7` | 「永远在对的线程上」的宿主：`PostCore` 直接 `action()` |
| `InlinePostHost<TPriority>` | `TestHosts.cs:24` | 永远**不在**目标线程 → 逼所有写入走 post 通道、逼断言去看 priority 参数 |
| `DeferredHost` | `TestHosts.cs:44` | 即发即弃的适配器：写入排队，`Pump()` 才落地（模拟「动画已取消但消息还在队列里」） |
| `FakeTimeSource` | `Timing/FakeTimeSource.cs:21` | 手驱动的 `ITimeSourceControl`。`Advance`/`AdvanceSteps` 移时钟**不做 rebase**；`Pause()`/`Resume()`（`:73`/`:81`）、`Stall()`/`Feed()`（`:58`/`:66`）控制 `IsAdvancing`（`:41`） |
| `StubCommand` / `StubSlot` / `StubNode` / `StubTree` | `WorkflowSystem/Support/WorkflowTestKit.cs:14,44,66,92` | 工作流四组件的空实现（此前在三个测试文件里各抄一份，现集中） |
| `TestCommand` / `TestSlot` / `ProbeNode` | `WorkflowSystem/CompilerEx/ProbeNodes.cs:20,60,123` | 编译 / 运行测试的图件：不需要树、不需要撤销栈 |

`InternalsVisibleTo` 有两处，**形式不同**：Core 用特性（`Src/Core/VeloxDev.Core/Properties/AssemblyInfo.cs:3`），Core.Extension 用 csproj 项（`Src/Core/VeloxDev.Core.Extension/VeloxDev.Core.Extension.csproj:52`）。所以本模块可以直接测 `internal` 类型（`TransitionRun` 就是 internal）。

---

## 四、跑起来

| 命令 | 说明 |
|---|---|
| `dotnet test Src/Core/VeloxDev.Core.Test/VeloxDev.Core.Test.csproj` | 单项目 |
| `dotnet test Src/Core/VeloxDev.Core.Test --filter "FullyQualifiedName~CompilerEx"` | 按命名空间定位一个目录；`README.md:280` 记的就是这条 |

实测（本机，Debug）：

| 项 | 值 |
|---|---|
| `[TestMethod]` 条数 | **1129**（在树里数出；另有 Extension.Test 的 642） |
| `[TestClass]` 条数 | **132**（同上） |
| 全量耗时 | **30 s** —— **不可复核**（提交信息里的旧值，没有可重跑的依据） |
| 8 次连跑的失败次数 | **1** —— **不可复核**（同上；原因见 §六）。**`MVVM/` 已不是来源**：2026-10-01 重写为 TCS 门控后连跑全绿 |

`TestResults/` 被 `.gitignore` 的 `[Tt]est[Rr]esult*/` 排除 —— 里面 `.trx` 是本地产物，**不可作为依据**。

---

## 五、组织方式

**目录镜像被测目录**；命名空间 = `VeloxDev.Core.Test.<目录路径>`（`Src/Core/VeloxDev.Core/WorkflowSystem/CompilerEx/` → `VeloxDev.Core.Test.WorkflowSystem.CompilerEx`）；类名一律 `<被测类型>Tests`。参数化用 `[DataRow]`，没有别的参数化机制。

| 测试目录 | 文件数（其中 `[TestClass]`） | 测什么 |
|---|---|---|
| `TransitionSystem/` | 26（26） | 采样循环、调度器、帧集、pacer、链与 `Repeat`、内建采样器、缓动 |
| `WorkflowSystem/` | 53（49：32 直接 + 17 `CompilerEx/`；`CompilerEx/` 另有 `ProbeGraph.cs`/`ProbeNodes.cs`，`Support/` 另有 `WorkflowInputTestBase.cs`/`WorkflowTestKit.cs`，共 4 个非测试文件） | 树 / 节点 / slot 枚举 / 虚拟化数学 / 编译运行 |
| `AI/` | 9（9） | 工具调用与上下文拼装 |
| `Timing/` | 6（5；`FakeTimeSource.cs` 非测试） | 时钟、两类采样器 |
| `TimeLine/` | 4（4） | Tickable 总线与管理器 |
| `WeakTypes/` | 4（4） | 弱引用集合 |
| `DynamicTheme/` | 2（2） | 主题切换 |
| `AspectOriented/` | 2（1；`AopFixture.cs` 非测试） | AOP 代理与钩子 |
| `MVVM/` | 33（22） | `VeloxCommand` 全语义（并发/排队/锁/中断/取消/事件时序/异常路径/CTS 释放/完成等待/忙碌状态/事件编组/分配回归）+ 生成器签名覆盖 + `ObservableCollectionTracker` + `CommandEventArgs`；11 个非测试文件（`CommandTestKit.cs`、各 `*ViewModel.cs`、`GeneratorProbe.cs` 等） |
| `Serialization/` | 10（10） | 归档序列化：金样本、读写器 / 序列化器的边界与文本拼写、注册表所有权、`[Archive]` 诊断 |
| 根目录 | 3 | `GlobalUsings.cs`、`MSTestSettings.cs`、`TestHosts.cs`（三者都不是测试） |

**哪些 Core 主题目录没有对应测试目录**：

| Core 目录 | 情况 |
|---|---|
| `Src/Core/VeloxDev.Core/Interfaces/`（43 个 .cs） | 只有接口与少量数据（`TimeSample` 等），没有专属测试目录；行为由各实现侧的测试覆盖 |
| `Src/Core/VeloxDev.Core/Lifetime/`（`IApplicationState.cs`） | 零测试目录 |
| `Src/Core/VeloxDev.Core/Threading/`（4 个 .cs） | 零直接测试，只经由 `TestHosts.cs` 的宿主间接走到 |

（`Src/Core/VeloxDev.Core/AspectOriented/` 曾在这张表里，**现在有测试了** —— 见 `AspectOriented/` 目录。`Src/Core/VeloxDev.Core/Serialization/` 也曾在这张表里，**现在有测试了** —— 见 `Serialization/` 目录。`Properties/` 只有一个 `AssemblyInfo.cs`，不算主题目录。）

**已知无测试的入口（结构性缺口，不是疏漏）：**

| 符号 | 为什么测不到 |
|---|---|
| `RotationDirection`（`Src/Core/VeloxDev.Core/TransitionSystem/`） | 两个测试项目里**零引用**；只有适配器采样器（如 `Src/Adapters/VeloxDev.Avalonia/PlatformAdapters/Samplers/TransformSampler.cs`）与 `Examples/` 演示消费它 —— 测它等于测适配器，而适配器不在引用图里 |
| `TransitionCoreEx.AwaitThen` / `.Await`（`Src/Core/VeloxDev.Core/TransitionSystem/Effects/TransitionEx.cs:8`、`:20-24`） | 两个测试项目里零调用；只出现在七家 `Examples/` 演示里。`Repeat` / `Then` 是唯一被间接走到的（经 `ChainRepeatTests.cs:66` 的 `ChainNode`） |
| `MVVM/` 的 `VeloxCommandAttribute.cs`、`VeloxPropertyAttribute.cs` | 两个纯声明特性，没有行为可测 —— 它们的效果全在生成器产物里 |
| `AI/` 的 20 个源文件里的 11 个 | 20 源 vs 9 测试文件 |

---

## 六、并行与真实时钟（本模块最要紧的一条）

**并行设置只有一行，全仓没有 `.runsettings`**：

```
Src/Core/VeloxDev.Core.Test/MSTestSettings.cs:1
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
```

方法级 = **同一个类的两个方法可以同时在跑**。已全仓搜过，没有任何 `.runsettings` 文件，所以这一行就是并行的唯一事实源。

**16 个类用 `[DoNotParallelize]` 把自己摘出去**，理由分四档（档内按行号）：

| 档 | 类（`文件:行`） | 为什么 |
|---|---|---|
| 进程级静态状态 | `TimeLine/TickableBusTests.cs:23`、`TimeLine/TickManagerTests.cs:10`、`DynamicTheme/ThemeTransitionTests.cs:19`、`Timing/TimerCoreRegistryTests.cs:16`、`MVVM/VeloxCommandDiagnosticsTests.cs:16` | 静态注册表 / 总线 / 进程级静态事件（诊断钩子）。`TimerCoreRegistryTests.cs:9-16` 自己写明：覆盖 `ITimeSourceControl` 会把「时钟永不动」的源交给每个并发动画，而停在冻结时钟上的动画**不报错，它挂起** |
| 进程级测量 | `TransitionSystem/ReusableTimerWaitTests.cs:15`、`MVVM/CommandAllocationTests.cs:15`、`MVVM/CommandBoxingTests.cs:18` | 分配断言量的是 `GC.GetTotalAllocatedBytes`（进程级），并行时别的方法的分配会落进测量窗口，best-of-2 只是缓解 |
| 实时动画 / 时钟 | `Timing/TimeSourceContractTests.cs:15`、`TransitionSystem/FramePacerTests.cs:17`、`TransitionSystem/TimelineControlTests.cs:18`、`TransitionSystem/TransitionRunThreadAffinityTests.cs:18`、`TransitionSystem/TransitionSchedulerAwakeTests.cs:16`、`TransitionSystem/TransitionSchedulerPrepareTests.cs:16` | `TimelineControlTests.cs:8-18` 写明：观察的是实时运行的动画，断言是比值不是绝对时间 |
| 纵深防御 | `TransitionSystem/InterpolatorCoreTests.cs:13` | 注释（`:9-12`）自己写明：这些断言与并行无关，保留 `[DoNotParallelize]` 纯粹是防御 |
| 未注明理由 | `MVVM/CommandArityTests.cs:40` | 只有裸 `[DoNotParallelize]`，类注释里没写为什么串行 |

**「用真实时钟但没摘出去」的类 —— 这就是偶发失败的全部来源**（原为 4 个，`MVVM/` 于 2026-10-01 消除，剩 3 个）：

| 类 | 真实时钟用法 | `[DoNotParallelize]` |
|---|---|---|
| `Timing/CompensatingTimeSamplerTests.cs` | `:271` `Thread.Sleep(60)`；`:284-286` 断言余数 `< 11ms` | ✗ |
| `Timing/UncompensatedTimeSamplerTests.cs` | `:112` `Thread.Sleep(60)`（暂停区间）、`:154` `Thread.Sleep(30)`；`:157-159` 断言落在 20–200ms | ✗ |
| `TransitionSystem/ChainRepeatTests.cs` | `:194/:215/:233/:248/:270/:302` 固定 `Task.Delay(80)` 稳定窗；`:332` `Task.Delay(40)` 与 `:334` `Task.Delay(150)` 再稳定；`:338` 断言精确顺序 | ✗ |
| ~~`MVVM/VeloxCommandTests.cs`~~ | **2026-10-01 已消除**：该文件重写为 TCS 门控，MVVM 目录新增的测试也全部走 `CommandTestKit` 的信号等待，无一处真实时钟 | — |

（`TransitionSystem/EaseOvershootTests.cs:48` 用了真实 120ms 时长，但断言走 `FixedEase`，与帧时序无关 —— 不是风险源。）

**已记录的失败样本（不可复核）**：`TestResults/step4.trx`（gitignored 本地产物，早已不在树里）里失败的是 `AgainstTheDefaultSourceTheStepsTrackItsOwnClock`，栈顶落在 `CompensatingTimeSamplerTests.cs:284-286`，消息给的实测值是 `position 00:00:00.0875088, pushed 00:00:00.0700000` —— 最后一次内部读时钟与断言再读时钟之间过去了约 17.5ms。**持久的锚点是 `:284-286` 这几行**，不是那个 trx。

**代码与注释不一致（以代码为准）**：

> `CompensatingTimeSamplerTests.cs:10-13` 的类备注写：「Every assertion here is an exact integer comparison against a hand-driven clock. … a test that measured it against real time could only ever assert a ratio.」

但同一个类里 `:263-287` 的 `AgainstTheDefaultSourceTheStepsTrackItsOwnClock` **正是**那条「measure against real time」的测试（`Thread.Sleep(60)` + 11ms 容差），而且它**没有** `[DoNotParallelize]`。所以「这个类的断言全是精确整数比较」是错的；注释想说的只是「除了一条以外」。

（`UncompensatedTimeSamplerTests.cs:7-13` 的类备注**没有**这个问题 —— 它讲的是「暂停后不出现尖峰」，与代码一致。别把这两个类的备注当成同一回事。）

---

## 七、入口：我要改 X，先打开哪个文件

| 我想改 | 打开 |
|---|---|
| 并行度 / 让某个类串行 | `MSTestSettings.cs:1`（全局）；`[DoNotParallelize]`（局部） |
| 加一个宿主替身（新的编组时序） | `TestHosts.cs` |
| 加一个手驱时钟 | `Timing/FakeTimeSource.cs` |
| 工作流四组件的空实现 | `WorkflowSystem/Support/WorkflowTestKit.cs` |
| 编译 / 运行的最小图件 | `WorkflowSystem/CompilerEx/ProbeNodes.cs`、`WorkflowSystem/CompilerEx/ProbeGraph.cs` |
| 全局 using | `GlobalUsings.cs`（只加在这里，别每个文件写一遍） |
| 让 netstandard2.0 也进测试 | `VeloxDev.Core.Test.csproj:4` —— 先确认 MSTest 4.0.2 在该 TFM 上可用 |
