# Interfaces — 架构

> 代码：`Src/Core/VeloxDev.Core/Interfaces/`（**42 个 .cs**，49 个接口声明），7 个子目录按模块分：`AspectOriented/`(1 文件 / 1 接口)、`DynamicTheme/`(3/3)、`MVVM/`(5/5)、`Tickable/`(1/1)、`Timing/`(6/5)、`TransitionSystem/`(9/12)、`WorkflowSystem/`(17/22)。文件数 42；其中 **41** 个含接口声明，剩下 1 个是 `Timing/TimeSample.cs`（一个 `readonly struct`，不是契约）。
> 本文与其他模块的 `architecture.md` 写法不同：**这里没有实现，只有契约的集中地**。所以本文不写「这个模块做什么」，只写**契约的分层与归属规则** —— 哪个接口该谁实现、为什么集中在一个目录、跨模块在哪儿咬合。
> 契约**成员语义**归各实现模块：`ITimeSource`/`ITimeSampler` 看 `memory/modules/Timing/`，过渡相关看 `memory/modules/TransitionSystem/`，工作流相关看 `memory/modules/WorkflowSystem/`。平台差异**不在这里重复七遍**，看 `memory/modules/TransitionSystem/adapters/<平台>.md` 与 `memory/modules/WorkflowSystem/adapters/<平台>.md`。

---

## 一、第一条要记住的：`Interfaces/` 是**目录**，不是**命名空间**

`Interfaces/Timing/ITimeSource.cs:1` 的命名空间是 `VeloxDev.Timing`，不是 `VeloxDev.Timing.Interfaces`；`Interfaces/WorkflowSystem/*` 是 `VeloxDev.WorkflowSystem`；`Interfaces/TransitionSystem/*` 是 `VeloxDev.TransitionSystem`。

后果有三条，都是实际操作上的：

| 事实 | 后果 |
|---|---|
| `using` 层面分不出「这个类型在不在 `Interfaces/`」 | 找契约只能靠**路径**；`grep -rn "interface I" Src/Core/VeloxDev.Core/Interfaces/` 才是清单 |
| 移动契约文件**不改命名空间** | 契约在 `Interfaces/` 与实现目录之间来回搬是**源码兼容**的（本仓就这么搬过：`IWorkflowGridDecorator.cs:9-11`、`IWorkflowMinimapOverlay.cs:12-14` 记着自己是从七家适配器里搬过来的） |
| 命名空间 = 模块 | 一个契约属于哪个模块，看它服务谁，而不是看它现在躺在哪个目录 |

---

## 二、`Interfaces/` 装的是**跨边界契约**，不是全部契约

`Src/Core/VeloxDev.Core` 里一共 **101 个 public interface**：49 个在 `Interfaces/`，**52 个在别处**。这不是遗漏，是分层规则（复核命令：`grep -rn "public interface" Src/Core/VeloxDev.Core --include=*.cs | grep -v /obj/`）。按类归并：

| 契约类别 | 位置 | 为什么不在 `Interfaces/` |
|---|---|---|
| `IApplicationState`（1） | `Src/Core/VeloxDev.Core/Lifetime/IApplicationState.cs:4` | 归 `Lifetime` 模块，与它的实现 `ApplicationState`（同文件 `:11`）同住 |
| `IThreadAffinity`、`IThreadDispatcher<TPriorityCore>`（2） | `Src/Core/VeloxDev.Core/Threading/IThreadDispatcher.cs:12`、`:24` | 归 `Threading` 模块 |
| AI 三个 notifier + `IAIContextAccessor`（4） | `Src/Core/VeloxDev.Core/AI/AgentConfirmationEventArgs.cs:37`、`AgentSelectionEventArgs.cs:60`、`AgentToolCallEventArgs.cs:28`、`AI/IAIContextAccessor.cs:19` | 与那份 EventArgs **同文件**，或归 AI 模块 —— 不属于「跨实现模块的契约目录」 |
| 编译期内部管线 `ICompile*`（3） | `Src/Core/VeloxDev.Core/WorkflowSystem/CompilerEx/Compile/Contracts/`（`:12`、`:7`、`:14`） | 只有该子系统实现 |
| 运行期内部管线 `IExecution*`/`ILogWriter`/`INodeRetryPolicy`/`IRedirectable`/`IRuntimeAware`/`IRuntimeContext`（10） | `Src/Core/VeloxDev.Core/WorkflowSystem/CompilerEx/Runtime/Contracts/`（`IRedirectable.cs:11`、`IRuntimeAware.cs:8`、`IRuntimeContext.cs:15` 等） | 运行期内部管线 |
| `IGroupData`（1） | `Src/Core/VeloxDev.Core/WorkflowSystem/CompilerEx/Runtime/Model/GroupData.cs:17` | 引擎注入给节点的只读产物字典（详见 `memory/modules/WorkflowSystem/`） |
| WorkflowSystem 事件/交互族 `IWorkflow*Events`、`IWorkflow*EventSink`、`ILinkHighlight`、`ILinkHitTestable`（8） | `Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Events/{Node,Slot,Tree}/`、`GUI/Interaction/` | Core 内部事件轴，外部不实现 |
| `IConditionalSlot`（1） | `Src/Core/VeloxDev.Core/WorkflowSystem/SelectorEx/ConditionalSlot.cs:12` | SelectorEx 内部 |
| `IAopHookTarget`（1） | `Src/Core/VeloxDev.Core/AspectOriented/AspectHooks.cs:66` | AOP 运行期内部 |
| 强类型命令族 `IVeloxCommand<TParam1..TParamN, TResult>`（14）与 `IVeloxCommandEvents<TParam,TResult>`（1） | `Src/Core/VeloxDev.Core/MVVM/CommandArities.cs`、`MVVM/CommandEventArgs{TParam,TResult}.cs:31` | MVVM 模块内部管线 |
| `IVeloxJson*`（2） | `Src/Core/VeloxDev.Core/Serialization/VeloxJsonRegistry.cs` | 序列化模块内部。**2026-10-04 起只剩读写器这两个** —— 原来的四个生命周期钩子接口已删，钩子改用 BCL 那四个特性，见 `memory/modules/Serialization/pitfalls.md` §七 |

**可执行的判据**：契约进 `Interfaces/`，当且仅当它**跨越实现模块的边界** —— 由一个核心实现 + 若干外部（应用/适配器/生成器）各自实现。只被一个子系统实现、外部从不实现的，跟实现同住。`IContext` 的 XML 自己把这棵树写出来了（`IContext.cs:10-11`：派生 `IAccessContext`/`ITaskContext`/`IRuntimeContext`/`ICompileContext`），其中前两个在 `Interfaces/` 而 `IRuntimeContext`/`ICompileContext` 在 `CompilerEx/` —— 同一棵树，跨目录。

---

## 三、三种归属：契约由谁实现，决定了它的稳定性

这是本模块唯一真正要背下来的东西。**同一批契约的"可改动性"差三个数量级**，取决于它的实现方是谁。

### A 类：消费方实现（应用作者 / 模板 / **生成器注入**）

生成器注入是 A 类里最要紧的一种：`IAspectOriented`/`IThemeObject`/`ITickable`/`IVeloxCommand` 这几个契约**在仓内没有手写生产实现**，它们的实现体是 `obj/` 下的生成文件（测试里有桩实现，见下表）。

| 契约 | 谁实现它 | 依据 |
|---|---|---|
| `IAspectOriented` | **只有生成器**（生成 `VeloxDev.AopInterfaces.*` 接口，再由同一文件产出的 `<接口>Proxy` 实现） | `Src/Generators/VeloxDev.Core.Generator/AopSurface.cs:83`（写基接口那一句）、`:216`（实现类）；钉住这一点的测试是 `Src/Core/VeloxDev.Core.Test/AspectOriented/AopProxyTests.cs`。**用户类不再实现它**（2026-10-02 起）—— 所以「把切面装在真身上」从运行期静默失效变成了编译错误 |
| `IThemeObject` | **生成器**，除非基类已实现（那时只补 `base.` 调用） | `Src/Generators/VeloxDev.Core.Generator/Theme.cs:121-126`、`:151-154`；手写实现只有测试 `Src/Core/VeloxDev.Core.Test/DynamicTheme/ThemeTransitionTests.cs:48` |
| `ITickable` | **生成器**（`TickWriter` 给带 `[Tickable]` 的类型补 7 个成员） | `Src/Generators/VeloxDev.Core.Generator/Writers/TickWriter.cs:66`；手写实现只有测试 `Src/Core/VeloxDev.Core.Test/TimeLine/TickableBusTests.cs:46` |
| `IVeloxCommand` | 实现是 Core 的 `Src/Core/VeloxDev.Core/MVVM/VeloxCommand.cs:23`；**声明方是生成器**（生成 `XXCommand` 属性，类型为 `IVeloxCommand`） | `Src/Generators/VeloxDev.Core.Generator/Writers/CommandWriter.cs:587`（`IVeloxCommand` 全名字符串）、`Writers/WorkflowWriter.cs:664+`（属性落地） |
| `IWorkflow*ViewModel` 四族 | Core 的 4 个 `DefaultViewModel` 是默认实现；应用侧用带 `[WorkflowBuilder.Tree/Node/Slot/Link]` 的 partial 类，生成器补 commands 与属性通知；测试另有桩 | `Src/Core/VeloxDev.Core/WorkflowSystem/Templates/ViewModels/{Tree,Node,Slot,Link}DefaultViewModel.cs:9`；生成器按工作流类型选接口见 `Src/Generators/…/Writers/WorkflowWriter.cs:342-358`；测试桩 `Src/Core/VeloxDev.Core.Test/WorkflowSystem/Support/WorkflowTestKit.cs:92` |
| `IWorkflowActionPair`、`ITheme`、`ISlotProvider` | 前两者 Core 给默认实现、应用可另给；`ISlotProvider` 由应用实现 | `WorkflowActionPair` 结构体 `Src/Core/VeloxDev.Core/WorkflowSystem/WorkflowActionPair.cs:6`；`ITheme` 的 `Light`/`Dark` `Src/Core/VeloxDev.Core/DynamicTheme/{Light,Dark}.cs:3`；`Examples/Workflow/Common/Lib/ViewModels/Workflow/PythonPortProvider.cs:18`（全仓唯一的 `ISlotProvider` 实例） |
| `IWorkflowIdentifiable` | 只有 Core 的 4 个 DefaultViewModel 实现 | `…/Templates/ViewModels/{Tree,Node,Slot}DefaultViewModel.cs:9`、`LinkDefaultViewModel.cs:8`；`RuntimeId` 在需求侧被引用（`Src/Core/VeloxDev.Core/WorkflowSystem/CompilerEx/Runtime/Model/ExecutionCheckpoints.cs:79`） |

### B 类：平台实现（七家适配器必办/可办）

| 契约 | 七家现状 | 依据（每家的锚点） |
|---|---|---|
| `ITransitionHost<TPriorityCore>` | **7/7**：`PlatformAdapters/UIThreadInspector.cs` 继承 Core 的 `TransitionHostBase<…>` | `Src/Core/VeloxDev.Core/TransitionSystem/Runtime/TransitionHostBase.cs:9`；7 家分别 `Src/Adapters/<平台>/PlatformAdapters/UIThreadInspector.cs` |
| `ISampler` | **7/7**，但规模差一个量级：Avalonia 14 个文件、WPF 12、MAUI 12、WinUI 10、Jalium 9、**Razor 1、WinForms 1**（`PlatformAdapters/Samplers/*.cs` 文件数） | 同名目录下每文件一个采样器类 |
| `IWorkflowMinimapOverlay` | **7/7 适配器本体**（`Attached/Workflow/WorkflowMinimapOverlay.cs`；Razor 是 `WorkflowMinimapOverlay.razor.cs`，WinForms 还同时实现 `IWorkflowMinimapScrollSource`） | 各家类声明：Avalonia `:23`、Jalium `:15`、MAUI `:19`、WinForms `:23`、WinUI `:21`、WPF `:19`、Razor `:27` |
| `IWorkflowGridDecorator` | 适配器本体有 **2 家**（Razor `Attached/Workflow/WorkflowGridDecorator.razor.cs:14`、WinForms `Attached/Workflow/WorkflowGridDecorator.cs:23`）；Avalonia/MAUI/WinUI/WPF 在 `Src/Templates/*/working/content/workflow-grid-decorator/TemplateClass.cs` 与 `Examples/Workflow/<平台>/…` 里实现；**Jalium 仍一个实现都没有**：适配器发的是可继承基类 `WorkflowGridDecorator`（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowGridDecorator.cs:22`，用自己的签名，不实现本接口），模板派生它 | 2/7 在适配器，4/7 在模板/demo，1/7（Jalium）无 —— **「七家适配器都实现」这个印象对本契约是错的** |
| `IThemeValueConverter` | **6/7**：`PlatformAdapters/ThemeValueConverters.cs`（Avalonia/MAUI/Razor/WinForms/WinUI/WPF），**Jalium 整个不接 DynamicTheme**（全目录 0 处引用） | 上列六家各一个 `Src/Adapters/VeloxDev.<平台>/PlatformAdapters/ThemeValueConverters.cs`；`Src/Adapters/VeloxDev.Jalium/` 的 `.cs` 里 grep `IThemeValueConverter` 零命中 |

### C 类：Core 内部管线（Core 自己实现，外部**不该**实现）

| 契约 | Core 实现 | 外部该怎么做 |
|---|---|---|
| `ITransitionInterpreter<TPriorityCore>` | `Src/Core/VeloxDev.Core/TransitionSystem/Runtime/TransitionInterpreter.cs:10`、`:31` | 平台**继承** `TransitionInterpreterCore<…>`（7 家 `PlatformAdapters/TransitionInterpreter.cs:5-7`），别直接实现接口 |
| `ITransitionScheduler<TPriorityCore>` / `ITransitionScheduler` / `ITransitionSchedulerCore` | `Src/Core/VeloxDev.Core/TransitionSystem/Runtime/TransitionScheduler.cs:12`、`:229` | 同上：平台继承 `TransitionSchedulerCore<…>`（如 `Src/Adapters/VeloxDev.MAUI/PlatformAdapters/TransitionScheduler.cs:3`） |
| `IFrameState` | `Src/Core/VeloxDev.Core/TransitionSystem/State/State.cs:8` | 不实现 |
| `ITransitionProperty` | `Src/Core/VeloxDev.Core/TransitionSystem/Binding/TransitionProperty.cs:24`、`:157` | 不实现（另有两个内部实现：`Sampling/StructAssembler.cs:92`） |
| `IEaseCalculator` | `Src/Core/VeloxDev.Core/TransitionSystem/Effects/Eases.cs:121,128,134` | 可以自己写缓动，但走注册而不是实现接口 |
| `ISampleable` | `Src/Core/VeloxDev.Core/WorkflowSystem/GUI/GeometryModels/Viewport.cs:10` | 是**值类型**参与动画的入口，应用为自己的 struct 实现（规则见 `ISampleable.cs:8-13`） |
| `ISpatialMap<T>` | `Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Virtualization/SpatialGridHashMap.cs:11` | 不实现 |
| `ISpatialBoundsProvider` | `Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Virtualization/NodeBoundsProvider.cs:9`、`NodePairBoundsProvider.cs:14` | 不实现（`"Bounds"` 名字的通知约定写在 `ISpatialBoundsProvider.cs:19-23`） |
| `IConditionalSlotProvider<TSlot>` | `Src/Core/VeloxDev.Core/WorkflowSystem/SelectorEx/SlotEnumerator.cs:12` | 不实现 |
| `ITimeSource` / `ITimeSourceControl` | `Src/Core/VeloxDev.Core/Timing/TimeSourceCore.cs:66` | **平台可替换**：用 `TimerCore.RegisterTimeSource<TContract>` 注册在**契约类型**这个键上 |

判据：C 类接口上出现 `ITransition…Core` 这种命名、或它的实现里带状态机与生命周期（`Exit()`），就是「外部只继承基类」的信号。

---

## 四、为什么契约集中在 `VeloxDev.Core` 一个项目里

四条，都能在代码里指到：

1. **生成器用字符串全名引用契约。** `Src/Generators/VeloxDev.Core.Generator/Theme.cs:18-20`（`"global::VeloxDev.DynamicTheme.ITheme"` 等三条）、`Writers/TickWriter.cs:66`、`AopSurface.cs:83`、`Writers/CommandWriter.cs:587`、`Writers/WorkflowWriter.cs:342-358`。契约一旦改名或换命名空间，生成器**不会**跟着重构（它只认字符串），所以契约必须住在一个稳定、被所有下游共享的位置。
2. **七家适配器要共享同一份定义。** 这件事在本仓真的发生过：`IWorkflowGridDecorator.cs:9-11` 与 `IWorkflowMinimapOverlay.cs:12-14` 的 XML 明说以前每家各有一份相同副本（Jalium 那份还是派生形状），统一到 Core 后由各平台实现（谁在适配器、谁在模板、谁没有实现见上表）。
3. **契约是注册表的键。** `TimerCore.CreateTimeSource<TContract>() where TContract : class, ITimeSourceControl`（`Src/Core/VeloxDev.Core/Timing/TimerCore.cs:109`）按**精确契约类型**查表，且 XML 明说不做宽/窄回退（`:102-107`）。契约类型本身是 API 的一部分。
4. **契约层不引用任何 GUI。** `Interfaces/` 的全部 `using` 只有 5 个系统命名空间（`System.Reflection`/`System.Linq.Expressions`/`System.ComponentModel`/`System.Threading`/`System.Threading.Tasks`）与 6 个仓内模块（见 §五）。`IVeloxCommand : ICommand` 用的是 `System.Windows.Input`（`IVeloxCommand.cs:1`），在 .NET Core 上由 `System.ObjectModel` 提供，不是 WPF 依赖。

---

## 五、跨模块耦合点（真实咬合处，附接口名）

`Interfaces/` 里的 `using` 只有下面这些（对 42 个文件统计），每一条都是一个耦合点：

| 被引用的模块 | 处数 | 咬合在哪个接口/成员 |
|---|---|---|
| `VeloxDev.AI` | 11 | `[AgentContext]`/`[AgentCommandParameter]` 标在 WorkflowSystem 的契约上（`IWorkflowViewModel.cs:7-8`、`IWorkflowTreeViewModel.cs:31-33`、`IContext.cs:10-11` 等）。读取规则见 `memory/modules/AI/architecture.md` |
| `VeloxDev.MVVM` | 5 | `IVeloxCommand` 作为契约的属性类型（`IWorkflowViewModel.cs:26`、`IWorkflowTreeViewModel.cs:34` 等 22 个命令属性） |
| `VeloxDev.TimeLine` | 1 | `ITickable.cs:1`（`FrameEventArgs`）。2026-10-04 之前这里还有第二处：`ITransitionInterpreter.cs` 的 `using VeloxDev.TimeLine`，在 `TransitionEventArgs` 从 `VeloxDev.TimeLine` 移入 `VeloxDev.TransitionSystem` 之后就成了死引用，已删 |
| `VeloxDev.Threading` | 1 | `ITransitionHost.cs:14` 的 `IThreadDispatcher<TPriorityCore>` |
| `VeloxDev.Lifetime` | 1 | `ITransitionHost.cs:14` 的 `IApplicationState` |
| `VeloxDev.TransitionSystem.Abstractions` | 2 | `ITransitionScheduler.cs:1`（`InterpolatorCore`）、`ITransitionInterpreter.cs:2`（`SamplerSet<TPriorityCore>`）—— **契约引用了具体类**，见 §八·4 |

**反向最要紧的一条：TransitionSystem ↔ Timing 的耦合点是 `ITimeSourceControl`（写侧！）。** `Src/Core/VeloxDev.Core/TransitionSystem/` 有 5 个文件 `using VeloxDev.Timing`（`Sampling/SamplerSet.cs:3`、`State/StateSnapshot.cs:2`、`Effects/Transition.cs:6`、`Runtime/TransitionInterpreter.cs:3`、`Runtime/TransitionRun.cs:2`），实际用到的类型是 `Sampling/SamplerSet.cs:78` 与 `Effects/Transition.cs:393` 的 `TimerCore.CreateTimeSource<ITimeSourceControl>()`、`Runtime/TransitionRun.cs:34` 持有的 `ITimeSourceControl`。而 `Src/Core/VeloxDev.Core/Timing/` 反过来**零处**引用 `VeloxDev.TransitionSystem` —— 依赖单向。注意这与 `ITimeSource.cs:10-11` 里"sampler 拿的是只读面"的说明**不矛盾但也不重合**：动画管线要 `Wake`/`Seek`，所以它拿的是写侧。

---

## 六、不变量（契约层面，违反的后果都写出来了）

1. **`IAspectOriented` 只在 `net5.0` 及以上存在。** 整个文件被 `#if NET` 包住（`Interfaces/AspectOriented/IAspectOriented.cs:1`、`:11`）。实测：`Src/Core/VeloxDev.Core/obj/Debug/{net5.0,net8.0}/VeloxDev.Core.dll` 里能搜到 `IAspectOriented`，而 `netstandard2.0`/`netframework4.6.1`/`netcoreapp3.0` 的三个 DLL 里是 **0 命中**；其余 48 个契约在 5 个 TFM 里都在。所以「在契约目录里」不等于「在所有目标框架里」。`Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:5` 就是这 5 个 TFM 的来源：`netstandard2.0;netframework4.6.1;net5.0;netcoreapp3.0;net8.0`。
2. **契约名带 `Control` 后缀的只有一处**：`ITimeSourceControl`（写侧），且它**继承**读侧 `ITimeSource`（`ITimeSourceControl.cs:10`），不是平级。语义（为什么拆）写在 `:6-9`。
3. **`<TPriorityCore>` 家族一律"窄接口 : 宽接口"**，宽的那侧用 `Core` 后缀：`ITransitionEffect<TPriorityCore> : ITransitionEffectCore`（`ITransitionEffect.cs:5`）、`ITransitionScheduler<TPriorityCore> : ITransitionSchedulerCore`（`ITransitionScheduler.cs:5`）。窄接口用 `new` 重新声明克隆/执行成员（`ITransitionEffect.cs:9`；`ITransitionScheduler.cs:7-11` 是**重载收窄**而不是 `new`）。
4. **空接口有两种，别混用**：`IAspectOriented`（`:5`）、`ITheme`（`:3`）是**约束载体**（只出现在 `where T : …` 里，`ProxyEx.cs:57`、`AopCache.cs:22,46`、`ThemeConfigAttribute.cs:9-105`）；`ITaskContext`（`:14`）、`ITransitionScheduler`（`:14`）是**派生收窄**（继承一个接口、一个成员都不加）。判断法：看它有没有被 `where` 用。
5. **WorkflowSystem 四族的 helper 不在基接口上。** `IWorkflowViewModel`（`:9`）只有初始化/通知/`CloseCommand`；`GetHelper()`/`SetHelper()` 由四个子契约各声明一次（`IWorkflowTreeViewModel.cs:71-72`、`IWorkflowNodeViewModel.cs:67-68`、`IWorkflowSlotViewModel.cs:55-56`、`IWorkflowLinkViewModel.cs:27-28`）—— 所以拿到基接口引用**拿不到 helper**。
6. **命令属性的数量按语义给，不按对称**：`IVeloxCommand` 属性在五个契约里是 1（基）/8（Tree）/8（Node）/4（Slot）/1（Link），共 22 个。
7. **异步成员一律把取消参数放最后**，但默认值不统一：`ITimeSource.cs:100`（`CancellationToken cancellationToken = default`）与 `ITransitionScheduler.cs:11`/`:24`（`CancellationTokenSource? externCts = default`）给默认值，WorkflowSystem 的 `ReceiveAsync`/`BroadcastAsync`/`ReverseBroadcastAsync`/`AccessAsync`（`IWorkflowNodeViewModel.cs:92-107`）不给，必须显式传。
8. **契约里的拼写错误会被固化。** `ITransitionEffectCore` 有 `Canceled` 事件（`:24`）却只有 `InvokeCancled(...)`（`:36`，少一个 `c`），全仓按错拼写用（`Src/Core/VeloxDev.Core/TransitionSystem/Effects/TransitionEffect.cs:56` 的字段就叫 `_cancled`）。
9. **`ITickable` 的名字里曾藏着一个 U+200B 零宽空格 —— 2026-10-01 随重命名一并清除**（当时它还叫 `IMonoBehaviour`）。记录留下是因为它**编译得过、代码评审看不出来**，值一条纪律：文件名与 `ITickable` / `InitializeTickable` 两个标识符里各有一个，而编译器忽略 Cf 类字符 ⇒ 两种拼写**是同一个标识符**，元数据里落地的还是无 ZWSP 的写法。坑**全在人这一侧**：裸路径打不开文件（实测 `FileNotFoundError`）、`grep -l` 漏、**连 `git ls-files` 都把路径转义成 `"…\342\200\213.cs"`**，于是 `git ls-files … | grep -c '\.cs$'` 数出 37 而不是 38。
   **现在这些都不成立了** —— 名字就是普通的 `ITickable`，上面的数法也正常了。**纪律**：新契约的名字里绝不允许出现 Cf 类字符；它编译得过，所以唯一的防线是知道这件事。

---

## 七、我要改 X，先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 「消费者能干什么」的边界（读侧/写侧、谁收到什么接口） | `Interfaces/Timing/ITimeSourceControl.cs:6-9`（本仓唯一的拆分范式） |
| 平台必须实现哪些成员 | `Interfaces/WorkflowSystem/IWorkflowGridDecorator.cs`、`IWorkflowMinimapOverlay.cs`（数据交换契约，逐家实现），以及 `Interfaces/TransitionSystem/ITransitionHost.cs`（组合契约，无成员） |
| 节点/树/槽/链的数据形状 | `Interfaces/WorkflowSystem/IWorkflow{Tree,Node,Slot,Link}ViewModel.cs`（4 族各含 VM + Helper 两个接口） |
| 一次数据流访问的入参 | `Interfaces/WorkflowSystem/IAccessContext.cs`（编译期/运行期共用，靠 `IsCompilePhase` 区分） |
| 生成器会注入哪些成员、注入到哪个接口 | `Src/Generators/VeloxDev.Core.Generator/Writers/`（`TickWriter.cs:66`、`WorkflowWriter.cs:342-358`、`CommandWriter.cs:587`、`Theme.cs:121-154`、`AopSurface.cs:83`） |
| Agent 能否看见这个契约 | `Interfaces/WorkflowSystem/*.cs` 里的 `[AgentContext]`（`IWorkflowTreeViewModel.cs:7-8` 是范式），读取规则在 `memory/modules/AI/architecture.md` |
| 平台差异（不要在这里找） | `memory/modules/TransitionSystem/adapters/<平台>.md`、`memory/modules/WorkflowSystem/adapters/<平台>.md` |
