# VeloxDev.Core.Test — 扩展

面向两件事：**往这个项目里加一条测试**，和**加完之后别忘了动哪里**。

---

## 一、新增一条测试放哪

按目录镜像，**不要新建顶层目录**（新建的那一刻 `architecture.md` §五的表就过期了）：

| 被测的东西在 | 测试写到 | 命名空间 |
|---|---|---|
| `Src/Core/VeloxDev.Core/TransitionSystem/Xxx` | `TransitionSystem/XxxTests.cs` | `VeloxDev.Core.Test.TransitionSystem` |
| `Src/Core/VeloxDev.Core/Timing/Xxx` | `Timing/XxxTests.cs` | `VeloxDev.Core.Test.Timing` |
| `Src/Core/VeloxDev.Core/WorkflowSystem/Xxx` | `WorkflowSystem/XxxTests.cs` | `VeloxDev.Core.Test.WorkflowSystem` |
| …… | …… | …… |

- 文件名与类名 = `<被测类型>Tests`。
- **不要写 `using Microsoft.VisualStudio.TestTools.UnitTesting;`** —— csproj `:24` 已全局注入。
- `VeloxDev.Threading` / `VeloxDev.TimeLine` 也不用写（`GlobalUsings.cs` 有）；**其余命名空间必须显式 using**。
- `[TestClass]` + `[TestMethod]`；参数化只用 `[DataRow]`。
- 只被一个文件用的桩就写成文件内 `private sealed class`（`ChainRepeatTests.cs:33` 的 `RecordingSampler` 是这个形态）；**被第二个文件用到时**才上提到 `TestHosts.cs` 或 `WorkflowSystem/Support/WorkflowTestKit.cs` —— 后者的存在理由正是「三个文件各抄一份」被合并（见 `WorkflowTestKit.cs` 头部注释）。

---

## 二、确定性 rig：按这个顺序挑

**优先序：手驱时钟 > 零时长 > 真实 `Task.Delay`（最后一档默认禁用）。**

| 我要测 | 用 | 在哪 |
|---|---|---|
| 采样器的计数 / 单位换算算术 | `FakeTimeSource` + `Advance` / `AdvanceSteps` | `Timing/FakeTimeSource.cs:21` |
| 动画的一趟循环（写了几帧、顺序、完成 / 取消） | `Duration = 0` —— 零时长使每趟恰好采样一次、无真实等待 | `TransitionSystem/SamplingLoopTests.cs:27` 的注释；各文件的 `RunAsync` 辅助 |
| 编组 / priority / 迟到的帧 | `ImmediateHost` / `InlinePostHost<T>` / `DeferredHost` | `TestHosts.cs:7,24,44` |
| 帧集复用、端点捕获 | 自写的 `RecordingSampler` | 形态见 `ChainRepeatTests.cs:33` |
| 纯数据几何 / 数学（缩放、可达性） | 不用时钟也不用宿主，直接算 | 形态见 `WorkflowSystem/DeepZoomReachabilitySimTests.cs` |
| 「真的非等不可」 | 见 §四 | —— |

---

## 三、官方做法 vs 看着能编译、但错的捷径

| 错的捷径 | 为什么错 | 官方做法 |
|---|---|---|
| 断言里读 `DateTime.Now` / `Stopwatch` / `Environment.TickCount64` | 方法级并行下有别的测试在抢 CPU，测出来的值没有上界 | `FakeTimeSource` 手推 |
| 用 `GC.GetTotalAllocatedBytes` 量分配 | 进程级计数，并行时会把别的测试的分配算进来 | 用 `GC.GetAllocatedBytesForCurrentThread()`（线程本地）；`TransitionSystem/FramePathAllocationTests.cs:92-134` 就是这么写的 |
| 覆盖 `ITimeSourceControl` 来造时钟 | 会让每个并发动画拿到冻结时钟，而冻结时钟上的动画**挂起不报错**（`Timing/TimerCoreRegistryTests.cs:9-16` 明说） | 用 `FakeTimeSource` 这个**参数注入**的手驱源；不要改进程级注册表 |
| 「反正是单项目，`Thread.Sleep` 更真实」 | 真实时钟断言 + 方法级并行 = 偶发失败；本模块实测 8 次连跑红 1 次 | 见 §二 |
| 自己 `new` 一个平台件（`DispatcherTimer` 之类） | 平台件在测试项目里根本引不进来（没有适配器引用） | 手写替身；跨项目的范本是姊妹模块的 `SingleThreadContext` |
| 为了测一个 `internal` 而放宽访问性 | 不必要 | `InternalsVisibleTo` 已经有了（`Src/Core/VeloxDev.Core/Properties/AssemblyInfo.cs:3`），直接测 |
| 用 `[DataRow]` 去覆盖 netstandard2.0 / net461 分支 | 覆盖不到：只有 net5.0 资产被加载（见 `architecture.md` §二） | 要么给 csproj 加 TFM，要么承认这条分支不可测 |
| 断言「两个匹配的接口按反射顺序取第一个」 | 反射顺序无保证；`InterpolatorCoreTests.cs:200-212` 用注释写明了这点 | 断言**同一次选择可复现**（`AreSame(first, second)`）加**显式规则**（名字序），不要断言「等于某个具体实现」之外的东西 |

**写新测试时先问自己**：这条断言在「CPU 被别的 12 个类占满」时还成立吗？不成立就换 §二的 rig，或者加 `[DoNotParallelize]` 并**在类注释里写明为什么**（本模块 12 个类里有 4 个是靠注释说清理由的，照这个风格写）。

---

## 四、写不出来的测试（只能靠 `Examples/` 手验）

本模块**不引用任何 GUI 适配器**，所以下面这些在 Core.Test 里原则上写不出来：

| 验不了的东西 | 只能在哪验 |
|---|---|
| 真实渲染输出（WPF 不因 gradient stop 写入而重绘这类） | `Examples/<模块>/<GUI>/Demo` |
| 平台定时器的 tick 时序（`DispatcherTimer`、WinForms `Timer`） | 同上 |
| 平台采样器的覆盖面（`RotationDirection` 只被适配器消费） | `Examples/Transition/AUTO TEST/` |
| 真实 UI 线程的 `SynchronizationContext` 语义 | 手写替身能验「逻辑对不对」，验不了「平台是不是这样」 |
| 跨 TFM 行为（netstandard2.0 / net461 / netcoreapp3.0） | 给测试 csproj 加 TFM |
| AOT / 裁剪下的可达性 | `Src/Verification/VeloxDev.TrimProbe/` —— 仓库外的工作流，与测试项目互不管辖（`architecture.md` §一） |

**判据**：一条测试如果需要一个「真实的 X」，而 X 住在 `Src/Adapters/` 或某个平台程序集里 —— 它不属于本模块。

---

## 五、联动清单：改 Core 时要同步动哪里

漏一处这里的后果通常是**静默少一条覆盖**，构建照样过。

| 你改了 Core 的 | 必须同步 |
|---|---|
| 新增一个内建采样器 | **三处手写期望表**：`TransitionSystem/NativeSamplersTests.cs`、`TransitionSystem/NativeSamplersExtendedTests.cs`、`TransitionSystem/SamplerConformanceTests.cs`（都是逐类型手写的，没有反射枚举兜底） |
| 改 `ITimeSourceControl` 的形状 | `Timing/FakeTimeSource.cs:21` 会编译不过 —— **这是刻意的**，别用 `throw new NotImplementedException()` 蒙过去 |
| 改 `TransitionHostBase<TPriority>` 的虚方法签名 | `TestHosts.cs` 的三个宿主（`:7` / `:24` / `:44`） |
| 改 `IWorkflow{Tree,Node,Slot,Link}ViewModel` | `WorkflowSystem/Support/WorkflowTestKit.cs` 的四个 Stub（`:14,44,66,92`）+ `WorkflowSystem/CompilerEx/ProbeNodes.cs` 的 `TestCommand`/`TestSlot`/`ProbeNode`（`:20,60,119`） |
| 改名一个 `internal` 成员 | 本模块大量直测 internal（`Src/Core/VeloxDev.Core/Properties/AssemblyInfo.cs:3`）；改名前先 grep 本目录 |
| 改 `TransitionEx` 的扩展方法 | `ChainRepeatTests.cs:66` 的 `ChainNode` 是唯一经手 `Then`/`Repeat` 的地方；`AwaitThen` / `Await` 目前**零测试** |
| 新增一个 Core 主题目录 | `architecture.md` §五的「零测试」缺口表要一起改 |
| 改核心的取消 / 生命周期时序 | 至少两条类注释里写着「这里为什么必须串行」：`Timing/TimerCoreRegistryTests.cs:9-16`、`TransitionSystem/ReusableTimerWaitTests.cs:15` —— 时序变了这些理由也要重写 |

---

## 六、给这个模块写记忆 / 复核时的注意

- 依据只能是 `.cs` / `.csproj` 的行号。`TestResults/*.trx` 是 gitignored 本地产物（`.gitignore` 的 `[Tt]est[Rr]esult*/`），**不能当依据**，只能当线索。
- `dotnet test` 两次结果可能不同（偶发失败）。**别把一次绿当成「不存在」** —— 实测 8 次连跑里红 1 次。
