# Interfaces — 架构

> 代码：`Src/Core/VeloxDev.Core/Interfaces/`，若干子目录按模块分：`AspectOriented/`、`DynamicTheme/`、`MVVM/`、`Tickable/`、`Timing/`、`TransitionSystem/`、`WorkflowSystem/`。这里既有接口，也有少量纯数据：`Timing/TimeSample.cs`、`WorkflowSystem/Connections/ConnectionCheck.cs`、各模块 `Enums/` 下的枚举，以及 `WorkflowSystem/Layout/` 里的 `WorkflowLayoutOptions` / `Problem` / `State` / `Solution` / `Result` / `Context`。具体文件/接口数不在这里维护。
> 本文与其他模块的 `architecture.md` 写法不同：**这里没有实现，只有契约的集中地**。所以本文不写「这个模块做什么」，只写**契约的分层与归属规则** —— 哪个接口该谁实现、为什么集中在一个目录、跨模块在哪儿咬合。
> 契约**成员语义**归各实现模块：`ITimeSource`/`ITimeSampler` 看 `memory/modules/Timing/`，过渡相关看 `memory/modules/TransitionSystem/`，工作流相关看 `memory/modules/WorkflowSystem/`。平台差异**不在这里重复七遍**，看 `memory/modules/TransitionSystem/adapters/<平台>.md` 与 `memory/modules/WorkflowSystem/adapters/<平台>.md`。

---

## 一、第一条要记住的：`Interfaces/` 是**目录**，不是**命名空间**

`Interfaces/Timing/ITimeSource.cs` 写的是 `namespace VeloxDev.Timing`，不是 `VeloxDev.Timing.Interfaces`；`Interfaces/WorkflowSystem/**` 是 `VeloxDev.WorkflowSystem`；`Interfaces/TransitionSystem/*` 是 `VeloxDev.TransitionSystem`。

**`WorkflowSystem/` 内部再分层**（2026-10-30 用户定）：`Common/`（被多个组件族广泛实现的基础契约：`IWorkflowViewModel` / `IWorkflowIdentifiable` / `IWorkflowHelper`）、`Components/`（Node/Slot/Link/Tree + Helper）、`Connections/`（连接预览与裁决）、`Commands/`（`IWorkflowActionPair`；只有一种实现，不算 Common）、`Providers/`、`Spatial/`、`Surface/`、`Execution/`、`Enums/`（`SlotChannel`）。**模块自己的枚举统一放在对应 `Interfaces/<模块>/Enums/` 下**（TransitionSystem 的 `WarnStage`/`ErrorStage`/`RotationDirection` 同理），不留在实现目录里。`Common/` 的收口规则：**至少两个组件族实现、且至少一个其它层依赖**，否则放自己的层。`Layout/`（2026-10-30 起）装布局管道的注入面：数据快照 `WorkflowLayoutProblem` / `State` / `Solution` / `Result` / `Options` 与 `IWorkflowLayoutStage` / `IWorkflowLayoutPositionPolicy` / `IWorkflowLayoutCost` / `IWorkflowLayoutPlanner` / `IWorkflowLayoutApplier` / `IWorkflowLayoutSolver` / `IWorkflowLayoutHost`。**目录叫 `Layout/`，命名空间仍是 `VeloxDev.WorkflowSystem`** —— 实现里也不要建 `.Layout` 子命名空间，它会遮蔽 MAUI 的 `Layout` 类型令适配器报 CS0118（见 `memory/modules/WorkflowSystem/layout.md` §一）。

后果有三条，都是实际操作上的：

| 事实 | 后果 |
|---|---|
| `using` 层面分不出「这个类型在不在 `Interfaces/`」 | 找契约只能靠**路径**；`grep -rn "interface I" Src/Core/VeloxDev.Core/Interfaces/` 才是清单 |
| 移动契约文件**不改命名空间** | 契约在 `Interfaces/` 与实现目录之间来回搬是**源码兼容**的（本仓就这么搬过：`IWorkflowGridDecorator.cs` 与 `IWorkflowMinimapOverlay.cs` 的 `<summary>` 都写着 `Unified in Core so all 7 GUI adapters share one definition`） |
| 命名空间 = 模块 | 一个契约属于哪个模块，看它服务谁，而不是看它现在躺在哪个目录 |

---

## 二、`Interfaces/` 装的是**跨边界契约**，不是全部契约

`Src/Core/VeloxDev.Core` 里的 `public interface` **不只在 `Interfaces/`**：相当一部分声明在实现目录里。这不是遗漏，是分层规则（复核命令：`grep -rn "public interface" Src/Core/VeloxDev.Core --include=*.cs | grep -v /obj/`）。按类归并：

| 契约类别 | 位置 | 为什么不在 `Interfaces/` |
|---|---|---|
| `IApplicationState` | `Src/Core/VeloxDev.Core/Lifetime/IApplicationState.cs` 的 `IApplicationState` | 归 `Lifetime` 模块，与它的实现 `ApplicationState`（同文件）同住 |
| `IThreadAffinity`、`IThreadDispatcher<TPriorityCore>` | `Src/Core/VeloxDev.Core/Threading/IThreadDispatcher.cs` 的 `IThreadAffinity`、`IThreadDispatcher<TPriorityCore>` | 归 `Threading` 模块 |
| AI notifier 族 + `IAIContextAccessor` | `Src/Core/VeloxDev.Core/AI/AgentConfirmationEventArgs.cs` 的 `IAgentConfirmationNotifier`、`AgentSelectionEventArgs.cs` 的 `IAgentSelectionNotifier`、`AgentToolCallEventArgs.cs` 的 `IAgentToolCallNotifier`、`AI/IAIContextAccessor.cs` 的 `IAIContextAccessor` | 与那份 EventArgs **同文件**，或归 AI 模块 —— 不属于「跨实现模块的契约目录」 |
| 编译期内部管线 `ICompile*` | `Src/Core/VeloxDev.Core/WorkflowSystem/CompilerEx/Compile/Contracts/`（`ICompileContext`、`ICompileTimeAware`、`ICompileTimeRouter`） | 只有该子系统实现 |
| 运行期内部管线 `IExecution*`/`ILogWriter`/`INodeRetryPolicy`/`IRedirectable`/`IRuntimeAware`/`IRuntimeContext` | `Src/Core/VeloxDev.Core/WorkflowSystem/CompilerEx/Runtime/Contracts/`（`IRedirectable.cs` 的 `IRedirectable`、`IRuntimeAware.cs` 的 `IRuntimeAware`、`IRuntimeContext.cs` 的 `IRuntimeContext` 等） | 运行期内部管线 |
| `IGroupData` | `Src/Core/VeloxDev.Core/WorkflowSystem/CompilerEx/Runtime/Model/GroupData.cs` 的 `IGroupData` | 引擎注入给节点的只读产物字典（详见 `memory/modules/WorkflowSystem/`） |
| WorkflowSystem 事件/交互族 `IWorkflow*Events`、`IWorkflow*EventSink`、`IWorkflowSurfaceScroller`、`IWorkflowSurfaceNavigator`、`IInputEvents`、`ILinkHitTestable` | `Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Events/`、`GUI/Events/{Node,Slot,Tree,Input}/`、`GUI/Interaction/` | Core 内部事件轴，外部不实现 |
| `IConditionalSlot` | `Src/Core/VeloxDev.Core/WorkflowSystem/SelectorEx/ConditionalSlot.cs` 的 `IConditionalSlot` | SelectorEx 内部 |
| `IAopHookTarget` | `Src/Core/VeloxDev.Core/AspectOriented/AspectHooks.cs` 的 `IAopHookTarget` | AOP 运行期内部 |
| 强类型命令族 `IVeloxCommand<TParam1..TParamN, TResult>` 与 `IVeloxCommandEvents<TParam,TResult>` | `Src/Core/VeloxDev.Core/MVVM/CommandArities.cs`、`MVVM/CommandEventArgs{TParam,TResult}.cs` 的 `IVeloxCommandEvents<TParam, TResult>` | MVVM 模块内部管线 |
| `IVeloxJson*` | `Src/Core/VeloxDev.Core/Serialization/VeloxJsonRegistry.cs` | 序列化模块内部。**只有读写器这两个**（没有生命周期钩子接口，钩子用 BCL 那四个特性，见 `memory/modules/Serialization/pitfalls.md` §七） |

**可执行的判据**：契约进 `Interfaces/`，当且仅当它**跨越实现模块的边界** —— 由一个核心实现 + 若干外部（应用/适配器/生成器）各自实现。只被一个子系统实现、外部从不实现的，跟实现同住。`IContext` 的 XML 自己把这棵树写出来了（`IContext` 上那对 `[AgentContext]`：派生 `IAccessContext`/`ITaskContext`/`IRuntimeContext`/`ICompileContext`），其中前两个在 `Interfaces/` 而 `IRuntimeContext`/`ICompileContext` 在 `CompilerEx/` —— 同一棵树，跨目录。

---

## 三、三种归属：契约由谁实现，决定了它的稳定性

这是本模块唯一真正要背下来的东西。**同一批契约的"可改动性"差三个数量级**，取决于它的实现方是谁。

### A 类：消费方实现（应用作者 / 模板 / **生成器注入**）

生成器注入是 A 类里最要紧的一种：`IAspectOriented`/`IThemeObject`/`ITickable`/`IVeloxCommand` 这几个契约**在仓内没有手写生产实现**，它们的实现体是 `obj/` 下的生成文件（测试里有桩实现，见下表）。

| 契约 | 谁实现它 | 依据 |
|---|---|---|
| `IAspectOriented` | **只有生成器**（生成 `VeloxDev.AopInterfaces.*` 接口，再由同一文件产出的 `<接口>Proxy` 实现） | `Src/Generators/VeloxDev.Core.Generator/AopSurface.cs` 的 `interfaceDeclaration` 基列表（`IAspectOriented`）、`WriteProxy` 里的 `internal sealed class {interfaceName}Proxy : {interfaceName}`；钉住这一点的测试是 `Src/Core/VeloxDev.Core.Test/AspectOriented/AopProxyTests.cs`。**用户类不再实现它**（2026-10-02 起）—— 所以「把切面装在真身上」从运行期静默失效变成了编译错误 |
| `IThemeObject` | **生成器**，除非基类已实现（那时只补 `base.` 调用） | `Src/Generators/VeloxDev.Core.Generator/Theme.cs` 的 `GenerateThemeConfigClass`（`baseHasIThemeObject` 判定与 `addInterface`）；手写实现只有测试 `Src/Core/VeloxDev.Core.Test/DynamicTheme/ThemeTransitionTests.cs` 的 `Subject : IThemeObject` |
| `ITickable` | **生成器**（`TickWriter` 给带 `[Tickable]` 的类型补齐成员） | `Src/Generators/VeloxDev.Core.Generator/Writers/TickWriter.cs` 的 `GenerateBaseInterfaces`；手写实现只有测试 `Src/Core/VeloxDev.Core.Test/TimeLine/TickableBusTests.cs` 的 `PushCounter : ITickable` |
| `IVeloxCommand` | 实现是 Core 的 `Src/Core/VeloxDev.Core/MVVM/VeloxCommand.cs` 的 `VeloxCommand`；**声明方是生成器**（生成 `XXCommand` 属性，类型为 `IVeloxCommand`） | `Src/Generators/VeloxDev.Core.Generator/Writers/CommandWriter.cs` 的 `CanUpcastTo`（`IVeloxCommand` 全名字符串）、`Writers/WorkflowWriter.cs` 的 `LinksMap` 属性模板（属性落地） |
| `IWorkflow*ViewModel` 四族 | Core 的几个 `DefaultViewModel` 是默认实现；应用侧用带 `[WorkflowBuilder.Tree/Node/Slot/Link]` 的 partial 类，生成器补 commands 与属性通知；测试另有桩 | `Src/Core/VeloxDev.Core/WorkflowSystem/Templates/ViewModels/{Tree,Node,Slot}DefaultViewModel.cs` 的 `TreeDefaultViewModel`/`NodeDefaultViewModel`/`SlotDefaultViewModel`、`LinkDefaultViewModel.cs` 的 `LinkDefaultViewModel`；生成器按工作流类型选接口见 `Src/Generators/…/Writers/WorkflowWriter.cs` 的 `GetWorkflowInterfaceName`；测试桩 `Src/Core/VeloxDev.Core.Test/WorkflowSystem/Support/WorkflowTestKit.cs` 的 `StubTree` |
| `IWorkflowActionPair`、`ITheme`、`ISlotProvider` | 前两者 Core 给默认实现、应用可另给；`ISlotProvider` 由应用实现 | `WorkflowActionPair` 结构体 `Src/Core/VeloxDev.Core/WorkflowSystem/WorkflowActionPair.cs` 的 `WorkflowActionPair`；`ITheme` 的 `Light`/`Dark` `Src/Core/VeloxDev.Core/DynamicTheme/{Light,Dark}.cs` 的 `Light`/`Dark`；`Examples/Workflow/Common/Lib/ViewModels/Workflow/PythonPortProvider.cs` 的 `PythonPortProvider`（生产代码里唯一的 `ISlotProvider` 实例，测试另有桩） |
| `IWorkflowIdentifiable` | 只有 Core 的 DefaultViewModel 实现 | `…/Templates/ViewModels/{Tree,Node,Slot}DefaultViewModel.cs`、`LinkDefaultViewModel.cs`（同名类型）；`RuntimeId` 在需求侧被引用（`Src/Core/VeloxDev.Core/WorkflowSystem/CompilerEx/Runtime/Model/ExecutionCheckpoints.cs` 的 `KeyOf`） |

### B 类：平台实现（七家适配器必办/可办）

| 契约 | 七家现状 | 依据（每家的锚点） |
|---|---|---|
| `ITransitionHost<TPriorityCore>` | **七家全部**：`PlatformAdapters/UIThreadInspector.cs` 继承 Core 的 `TransitionHostBase<…>` | `Src/Core/VeloxDev.Core/TransitionSystem/Runtime/TransitionHostBase.cs` 的 `TransitionHostBase<TPriorityCore>`；7 家分别 `Src/Adapters/<平台>/PlatformAdapters/UIThreadInspector.cs` |
| `ISampler` | 七家都实现，但规模差一个量级：Avalonia/WPF/MAUI/WinUI/Jalium 各自有较多采样器文件，Razor/WinForms 只有很少（`PlatformAdapters/Samplers/*.cs`） | 同名目录下每文件一个采样器类 |
| `IWorkflowMinimapOverlay` | **七家全在适配器本体**（`Attached/Workflow/WorkflowMinimapOverlay.cs`；Razor 是 `WorkflowMinimapOverlay.razor.cs`，WinForms 还同时实现 `IWorkflowMinimapScrollSource`） | 各家类声明见 `Src/Adapters/VeloxDev.<平台>/Attached/Workflow/WorkflowMinimapOverlay.cs` 的 `class WorkflowMinimapOverlay`（Razor 为 `.razor.cs`） |
| `IWorkflowGridDecorator` | 适配器本体只有 **Razor 与 WinForms 两家**（Razor `Attached/Workflow/WorkflowGridDecorator.razor.cs` 的 `WorkflowGridDecorator : ComponentBase, IWorkflowGridDecorator`、WinForms `Attached/Workflow/WorkflowGridDecorator.cs` 的 `WorkflowGridDecorator : Panel, IWorkflowGridDecorator`）；Avalonia/MAUI/WinUI/WPF/Jalium 在 `Src/Templates/*/working/content/workflow-grid-decorator/TemplateClass.cs` 与 `Examples/Workflow/<平台>/…` 里实现。**Jalium 没有可继承基类 `WorkflowGridDecorator`**，由模板直接实现接口（`Src/Templates/VeloxDev.Jalium.Templates/working/content/workflow-grid-decorator/TemplateClass.cs` 的 `TemplateClass : Grid, IWorkflowGridDecorator`） | 少数在适配器，其余在模板/demo —— **「七家适配器都实现」这个印象对本契约是错的** |
| `IThemeValueConverter` | **六家**：`PlatformAdapters/ThemeValueConverters.cs`（Avalonia/MAUI/Razor/WinForms/WinUI/WPF），**Jalium 整个不接 DynamicTheme**（全目录零命中） | 上列六家各一个 `Src/Adapters/VeloxDev.<平台>/PlatformAdapters/ThemeValueConverters.cs`；`Src/Adapters/VeloxDev.Jalium/` 的 `.cs` 里 grep `IThemeValueConverter` 零命中 |

### C 类：Core 内部管线（Core 自己实现，外部**不该**实现）

| 契约 | Core 实现 | 外部该怎么做 |
|---|---|---|
| `ITransitionInterpreter<TPriorityCore>` | `Src/Core/VeloxDev.Core/TransitionSystem/Runtime/TransitionInterpreter.cs` 的 `TransitionInterpreterCore<TTransitionEffectCore, TPriorityCore>`、`TransitionInterpreterCore<TTransitionEffectCore>` | 平台**继承** `TransitionInterpreterCore<…>`（7 家 `PlatformAdapters/TransitionInterpreter.cs` 的 `TransitionInterpreter : TransitionInterpreterCore<…>`），别直接实现接口 |
| `ITransitionScheduler<TPriorityCore>` / `ITransitionScheduler` / `ITransitionSchedulerCore` | `Src/Core/VeloxDev.Core/TransitionSystem/Runtime/TransitionScheduler.cs` 的 `TransitionSchedulerCore<…>`、`TransitionSchedulerCore` | 同上：平台继承 `TransitionSchedulerCore<…>`（如 `Src/Adapters/VeloxDev.MAUI/PlatformAdapters/TransitionScheduler.cs` 的 `TransitionScheduler : TransitionSchedulerCore<UIThreadInspector, TransitionInterpreter, NonPriority>`） |
| `IFrameState` | `Src/Core/VeloxDev.Core/TransitionSystem/State/State.cs` 的 `StateCore : IFrameState` | 不实现 |
| `ITransitionProperty` | `Src/Core/VeloxDev.Core/TransitionSystem/Binding/TransitionProperty.cs` 的 `TransitionProperty`、`BoundTransitionProperty` | 不实现（另有两个内部实现：`Sampling/StructAssembler.cs` 的 `CaptureProperty`） |
| `IEaseCalculator` | `Src/Core/VeloxDev.Core/TransitionSystem/Effects/Eases.cs` 的 `EaseDefault`、`EaseInSine`、`EaseOutSine` | 可以自己写缓动，但走注册而不是实现接口 |
| `ISampleable` | `Src/Core/VeloxDev.Core/WorkflowSystem/GUI/GeometryModels/Viewport.cs` 的 `Viewport : ISampleable` | 是**值类型**参与动画的入口，应用为自己的 struct 实现（规则见 `ISampleable.cs` 的 `<remarks>`：`Reference types do not go through this path`） |
| `ISpatialMap<T>` | `Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Virtualization/SpatialGridHashMap.cs` 的 `SpatialGridHashMap<T> : ISpatialMap<T>` | 不实现 |
| `ISpatialBoundsProvider` | `Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Virtualization/NodeBoundsProvider.cs` 的 `NodeBoundsProvider`、`NodePairBoundsProvider.cs` 的 `NodePairBoundsProvider` | 不实现（`"Bounds"` 名字的通知约定写在 `ISpatialBoundsProvider.Bounds` 的 `<remarks>`） |
| `IConditionalSlotProvider<TSlot>` | `Src/Core/VeloxDev.Core/WorkflowSystem/SelectorEx/SlotEnumerator.cs` 的 `SlotEnumerator<TSlot> : IConditionalSlotProvider<TSlot>` | 不实现 |
| `ITimeSource` / `ITimeSourceControl` | `Src/Core/VeloxDev.Core/Timing/TimeSourceCore.cs` 的 `TimeSourceCore : ITimeSourceControl` | **平台可替换**：用 `TimerCore.RegisterTimeSource<TContract>` 注册在**契约类型**这个键上 |

判据：C 类接口上出现 `ITransition…Core` 这种命名、或它的实现里带状态机与生命周期（`Exit()`），就是「外部只继承基类」的信号。

---

## 四、为什么契约集中在 `VeloxDev.Core` 一个项目里

四条，都能在代码里指到：

1. **生成器用字符串全名引用契约。** `Src/Generators/VeloxDev.Core.Generator/Theme.cs` 的 `IThemeFullName` 等三条常量、`Writers/TickWriter.cs` 的 `GenerateBaseInterfaces`、`AopSurface.cs` 的 `interfaceDeclaration`、`Writers/CommandWriter.cs` 的 `CanUpcastTo`、`Writers/WorkflowWriter.cs` 的 `GetWorkflowInterfaceName`。契约一旦改名或换命名空间，生成器**不会**跟着重构（它只认字符串），所以契约必须住在一个稳定、被所有下游共享的位置。
2. **七家适配器要共享同一份定义。** `IWorkflowGridDecorator.cs` 与 `IWorkflowMinimapOverlay.cs` 的 XML 都写明这一点（`Unified in Core so all 7 GUI adapters share one definition`）：每家自己留一份相同副本会分叉（Jalium 那份还是派生形状），定义统一放 Core、由各平台实现（谁在适配器、谁在模板、谁没有实现见上表）。
3. **契约是注册表的键。** `TimerCore.CreateTimeSource<TContract>() where TContract : class, ITimeSourceControl`（`Src/Core/VeloxDev.Core/Timing/TimerCore.cs` 的 `CreateTimeSource<TContract>`）按**精确契约类型**查表，且 XML 明说不做宽/窄回退（同方法 remarks）。契约类型本身是 API 的一部分。
4. **契约层不引用任何 GUI。** `Interfaces/` 的全部 `using` 只有少数系统命名空间（`System.Collections.Concurrent`/`System.Collections.ObjectModel`/`System.ComponentModel`/`System.Linq.Expressions`/`System.Reflection`/`System.Threading`/`System.Threading.Tasks`）与仓内模块（见 §五）。`IVeloxCommand : ICommand` 用的是 `System.Windows.Input`（`IVeloxCommand.cs` 的 `using System.Windows.Input`），在 .NET Core 上由 `System.ObjectModel` 提供，不是 WPF 依赖。

---

## 五、跨模块耦合点（真实咬合处，附接口名）

`Interfaces/` 里的 `using` 只有下面这些，每一条都是一个耦合点：

| 被引用的模块 | 规模 | 咬合在哪个接口/成员 |
|---|---|---|
| `VeloxDev.AI` | 多处 | `[AgentContext]`/`[AgentCommandParameter]` 标在 WorkflowSystem 的契约上（`IWorkflowViewModel` 的 `[AgentContext]` 对、`IWorkflowTreeViewModel` 的 `LinksMap` 注解、`IContext` 的 `[AgentContext]` 等）。读取规则见 `memory/modules/AI/architecture.md` |
| `VeloxDev.MVVM` | 若干文件 | `IVeloxCommand` 作为契约的属性类型（`IWorkflowViewModel` 的 `CloseCommand`、`IWorkflowTreeViewModel` 的 `CreateNodeCommand` 等） |
| `VeloxDev.TimeLine` | 一处 | `ITickable.cs` 的 `using VeloxDev.TimeLine`（`FrameEventArgs`）。没有第二处：`ITransitionInterpreter.cs` 不 `using VeloxDev.TimeLine`（`TransitionEventArgs` 现在在 `VeloxDev.TransitionSystem`） |
| `VeloxDev.Threading` | 一处 | `ITransitionHost<TPriorityCore>` 的基接口列表里的 `IThreadDispatcher<TPriorityCore>` |
| `VeloxDev.Lifetime` | 一处 | `ITransitionHost<TPriorityCore>` 的基接口列表里的 `IApplicationState` |
| `VeloxDev.TransitionSystem.Abstractions` | 两处 | `ITransitionScheduler.cs` 的 `using VeloxDev.TransitionSystem.Abstractions`（`InterpolatorCore`）、`ITransitionInterpreter.cs` 的同名 using（`SamplerSet<TPriorityCore>`）—— **契约引用了具体类**，见 §八·4 |

**反向最要紧的一条：TransitionSystem ↔ Timing 的耦合点是 `ITimeSourceControl`（写侧！）。** `Src/Core/VeloxDev.Core/TransitionSystem/` 有几个文件 `using VeloxDev.Timing`（`Sampling/SamplerSet.cs`、`State/StateSnapshot.cs`、`Effects/Transition.cs`、`Runtime/TransitionInterpreter.cs`、`Runtime/TransitionRun.cs` 的 `using VeloxDev.Timing`），实际用到的类型是 `Sampling/SamplerSet.Run` 与 `Effects/Transition` 的 `TimerCore.CreateTimeSource<ITimeSourceControl>()`、`Runtime/TransitionRun.Timeline` 持有的 `ITimeSourceControl`。而 `Src/Core/VeloxDev.Core/Timing/` 反过来**零处**引用 `VeloxDev.TransitionSystem` —— 依赖单向。注意这与 `ITimeSource.cs` 的 remarks 里"This is the read-only face"的说明**不矛盾但也不重合**：动画管线要 `Wake`/`Seek`，所以它拿的是写侧。

---

## 六、不变量（契约层面，违反的后果都写出来了）

1. **`IAspectOriented` 只在 `net5.0` 及以上存在。** 整个文件被 `#if NET` 包住（`Interfaces/AspectOriented/IAspectOriented.cs` 的 `#if NET` 与 `#endif`）。实测：`Src/Core/VeloxDev.Core/obj/Debug/{net5.0,net8.0}/VeloxDev.Core.dll` 里能搜到 `IAspectOriented`，而 `netstandard2.0`/`netframework4.6.1`/`netcoreapp3.0` 的 DLL 里搜不到；其余契约在所有 TFM 里都在。所以「在契约目录里」不等于「在所有目标框架里」。`Src/Core/VeloxDev.Core/VeloxDev.Core.csproj` 的 `<TargetFrameworks>` 就是这组 TFM 的来源：`netstandard2.0;netframework4.6.1;net5.0;netcoreapp3.0;net8.0`。
2. **契约名带 `Control` 后缀的只有一处**：`ITimeSourceControl`（写侧），且它**继承**读侧 `ITimeSource`（`ITimeSourceControl.cs` 的 `ITimeSourceControl : ITimeSource`），不是平级。语义（为什么拆）写在它的 `<remarks>`。
3. **`<TPriorityCore>` 家族一律"窄接口 : 宽接口"**，宽的那侧用 `Core` 后缀：`ITransitionEffect<TPriorityCore> : ITransitionEffectCore`（`ITransitionEffect.cs` 的声明）、`ITransitionScheduler<TPriorityCore> : ITransitionSchedulerCore`（`ITransitionScheduler.cs` 的声明）。窄接口用 `new` 重新声明克隆/执行成员（`ITransitionEffect.Clone`；`ITransitionScheduler.Execute` 是**重载收窄**而不是 `new`）。
4. **空接口有两种，别混用**：`IAspectOriented` 与 `ITheme`（都是空接口体）是**约束载体**（只出现在 `where T : …` 里，`ProxyEx.cs` 的 `where T : IAspectOriented`、`AopCache.cs` 的两处 `where TInterface : class, IAspectOriented`、`ThemeConfigAttribute.cs` 的 `ThemeConfigAttribute<...>` 系列）；`ITaskContext : IAccessContext`、`ITransitionScheduler : ITransitionSchedulerCore` 是**派生收窄**（继承一个接口、一个成员都不加）。判断法：看它有没有被 `where` 用。
5. **WorkflowSystem 四族的 helper 不在基接口上。** `IWorkflowViewModel` 只有初始化/通知/`CloseCommand`；`GetHelper()`/`SetHelper()` 由四个子契约各声明一次（`IWorkflowTreeViewModel`、`IWorkflowNodeViewModel`、`IWorkflowSlotViewModel`、`IWorkflowLinkViewModel`）—— 所以拿到基接口引用**拿不到 helper**。
6. **命令属性的数量按语义给，不按对称**：`IVeloxCommand` 属性在五个契约里按各自职责给出（Tree/Node 多，基类与 Link 少），不追求五个契约相同。
7. **异步成员一律把取消参数放最后**，但默认值不统一：`ITimeSource.WaitWhileStalledAsync`（`CancellationToken cancellationToken = default`）与 `ITransitionScheduler` 两个 `Execute` 重载的 `externCts = default` 给默认值，WorkflowSystem 的 `ReceiveAsync`/`BroadcastAsync`/`ReverseBroadcastAsync`/`AccessAsync`（`IWorkflowNodeViewModelHelper`）不给，必须显式传。
8. **契约里的拼写错误会被固化。** `ITransitionEffectCore` 有 `Canceled` 事件却只有 `InvokeCancled(...)`（少一个 `c`），全仓按错拼写用（`Src/Core/VeloxDev.Core/TransitionSystem/Effects/TransitionEffect.cs` 的字段就叫 `_cancled`）。
9. **改 `ITickable` 这类名字时当心不可见字符** —— 它曾带着一个 U+200B 零宽空格（2026-10-01 随重命名一并清除，当时它还叫 `IMonoBehaviour`）。为什么值得记：它**编译得过、代码评审看不出来** ——文件名与 `ITickable` / `InitializeTickable` 两个标识符里各有一个，而编译器忽略 Cf 类字符 ⇒ 两种拼写**是同一个标识符**，元数据里落地的还是无 ZWSP 的写法。坑**全在人这一侧**：裸路径打不开文件（实测 `FileNotFoundError`）、`grep -l` 漏、**连 `git ls-files` 都把路径转义成 `"…\342\200\213.cs"`**，于是 `git ls-files … | grep -c '\.cs$'` 都数不对。
   **现在这些都不成立了** —— 名字就是普通的 `ITickable`，上面的数法也正常了。**纪律**：新契约的名字里绝不允许出现 Cf 类字符；它编译得过，所以唯一的防线是知道这件事。

---

## 七、我要改 X，先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 「消费者能干什么」的边界（读侧/写侧、谁收到什么接口） | `Interfaces/Timing/ITimeSourceControl.cs` 的 `<remarks>`（本仓唯一的拆分范式） |
| 平台必须实现哪些成员 | `Interfaces/WorkflowSystem/Surface/IWorkflowGridDecorator.cs`、`IWorkflowMinimapOverlay.cs`（数据交换契约，逐家实现），以及 `Interfaces/TransitionSystem/ITransitionHost.cs`（组合契约，无成员） |
| 节点/树/槽/链的数据形状 | `Interfaces/WorkflowSystem/Components/IWorkflow{Tree,Node,Slot,Link}ViewModel.cs`（4 族各含 VM + Helper 两个接口） |
| 一次数据流访问的入参 | `Interfaces/WorkflowSystem/Execution/IAccessContext.cs`（编译期/运行期共用，靠 `IsCompilePhase` 区分） |
| 生成器会注入哪些成员、注入到哪个接口 | `Src/Generators/VeloxDev.Core.Generator/Writers/`（`TickWriter.cs` 的 `GenerateBaseInterfaces`、`WorkflowWriter.cs` 的 `GetWorkflowInterfaceName`、`CommandWriter.cs` 的 `CanUpcastTo`、`Theme.cs` 的 `GenerateThemeConfigClass`、`AopSurface.cs` 的 `interfaceDeclaration`） |
| Agent 能否看见这个契约 | `Interfaces/WorkflowSystem/**/*.cs` 里的 `[AgentContext]`（`IWorkflowTreeViewModel` 的 `[AgentContext]` 对是范式），读取规则在 `memory/modules/AI/architecture.md` |
| 平台差异（不要在这里找） | `memory/modules/TransitionSystem/adapters/<平台>.md`、`memory/modules/WorkflowSystem/adapters/<平台>.md` |
