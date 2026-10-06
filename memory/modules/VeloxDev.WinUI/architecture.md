# VeloxDev.WinUI — 架构

> 代码：`Src/Adapters/VeloxDev.WinUI/`。**29 个 .cs、4869 行**（`Attached/Workflow/` 9 个 3366 行，
> 最大三个是 `WorkflowSurfaceBehavior.cs` 1326、`WorkflowMinimapOverlay.cs` 592、`WorkflowSlotLayoutBehavior.cs` 505；
> `PlatformAdapters/` 9 个 757 行，最大 `ThemeValueConverters.cs` 314、`Transition.cs` 243；
> `PlatformAdapters/Samplers/` 10 个 743 行，最大 `TransformSampler.cs` 207、`BrushSampler.cs` 199；顶层 `GlobalUsings.cs` 3 行）。
> **计数写法**：`git ls-files Src/Adapters/VeloxDev.WinUI | grep -c '\.cs$'`。
> 只写 `'Src/Adapters/VeloxDev.WinUI/**/*.cs'` 会得到 **28** —— git 的 `**/` 不匹配**目录本级**的文件，
> 漏掉的正是唯一一个平地文件 `GlobalUsings.cs`。而 `'Src/Adapters/VeloxDev.WinUI/*.cs'` 在 git 里同样得 **29**
> （`*` 跨 `/`，不是只匹配本级），所以别指望靠它补那一个。
> **七家适配器每家都正好漏这 1 个**，所以「28」这个数字一出现就是同一个错法。
>
> 本文只写「读完这 29 个文件才知道的东西」。类型清单、成员表、继承树请看 IDE。
>
> **本模块没有 `adapters/` 子目录，也不该有**：模块名本身就是一个平台，不存在平台轴。它在三条轴上的平台差异分别落在
> `memory/modules/WorkflowSystem/adapters/winui.md`、`memory/modules/TransitionSystem/adapters/winui.md`、
> `memory/modules/DynamicTheme/architecture.md`，本文**指路不抄**。

---

## 一、一个项目、三条轴、四份命名空间

这个项目叫「WinUI 适配器」，但它实际同时实现了三个 Core 模块的平台侧契约，彼此**在程序集内互不引用**（实测：`Attached/` 下零 `Transition`/`Interpolator`/`ThemeManager`/`ISampler` 符号，`PlatformAdapters/` 下零 `Workflow` 符号）：

| 轴 | Core 契约 | 本项目落点 | 平台差异记在哪 |
|---|---|---|---|
| WorkflowSystem | 七个视图角色 + 模型事件桥 | `Attached/Workflow/`（9 文件） | `memory/modules/WorkflowSystem/adapters/winui.md` |
| TransitionSystem | 宿主 / 解释器 / 调度器 / 帧 pacer / 采样器 | `PlatformAdapters/`（9 类型 + `Samplers/` 10 个） | `memory/modules/TransitionSystem/adapters/winui.md` |
| DynamicTheme | `IThemeValueConverter` | `PlatformAdapters/ThemeValueConverters.cs` | `memory/modules/DynamicTheme/architecture.md` |

**目录名与轴不对齐，别按目录推契约**：`Attached/` 只有 WorkflowSystem 一条轴（名字起得像「所有附着行为」，实际不是）；TransitionSystem 与 DynamicTheme 两条轴混在 `PlatformAdapters/` 里。

**四条命名空间全部寄生在 Core 上**（`grep -h '^namespace'` 的实测分布：`VeloxDev.WorkflowSystem.AttachedBehaviors` 9 个、`VeloxDev.TransitionSystem` 8 个、`VeloxDev.Adapters.NativeSamplers` 10 个、`VeloxDev.DynamicTheme` 1 个；合计 28，第 29 个是 `GlobalUsings.cs`，它没有命名空间）：

| 命名空间 | 文件数 | 谁是主人 |
|---|---|---|
| `VeloxDev.WorkflowSystem.AttachedBehaviors` | 9 | **Core 里不存在这份命名空间** —— 七家适配器各自声明一份同名（`grep -l` 命中七家、命中 Core 0 次） |
| `VeloxDev.TransitionSystem` | 8 | 与 Core 同带，是「续写」不是「新增」 |
| `VeloxDev.DynamicTheme` | 1 | 同上 |
| `VeloxDev.Adapters.NativeSamplers` | 10 | **与 Core 的采样器不是同一个命名空间**：Core 在 `VeloxDev.TransitionSystem.NativeSamplers`（`Src/Core/VeloxDev.Core/TransitionSystem/NativeSamplers/PointSampler.cs:3`），本模块在 `VeloxDev.Adapters.NativeSamplers`（`Samplers/PointSampler.cs:3`）。两边各有 `PointSampler`/`SizeSampler`/`ColorSampler` 三个**同名不同型**（本模块的是 `Windows.Foundation.Point`/`Windows.UI.Color`，Core 的是 `System.Drawing.Point`/`Color`）。分开两个命名空间正是让 `Interpolator.cs:14-23` 的 `new PointSampler()` 无歧义 —— 理由见 `TransitionSystem/adapters/winui.md` §一 |

**宿主 XAML 引行为类型写 `using:`，不写 `clr-namespace:…;assembly=…`。**
这是这一家在**宿主接线**上最容易照抄错的一处：`Examples/Workflow/WinUI Trimmed/Demo/Views/Workflow/TreeView.xaml:6` 与 `NodeView.xaml` 都是 `xmlns:behaviors="using:VeloxDev.WorkflowSystem.AttachedBehaviors"`（三个 WinUI demo 与 `Src/Templates/VeloxDev.WinUI.Templates/` 全仓 `grep clr-namespace` **零命中**）。
对照 WPF 必须在同一处写 `clr-namespace:…;assembly=VeloxDev.WPF`（`memory/modules/VeloxDev.WPF/architecture.md:29`）。⇒ **把 WPF 那一行抄过来会解析不到类型**，反过来把 `using:` 抄去 WPF 同样不行。

**不解决什么（常被误以为在这里）：**

| 不在模块内 | 实际归谁 |
|---|---|
| 七个角色的职责、附着属性名、`PART_*` 约定、注册位置 | `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3 |
| 缩放/平移/虚拟化/槽位锚点的**数学** | Core `WorkflowSurfaceMath`、`WorkflowSlotUpdateGate` 等 |
| 契约需要哪些成员、`FindOrCreate` 为什么必须用 | `memory/modules/TransitionSystem/extension.md` |
| 「主题是什么」「切换什么时候发生」、转换器怎么被激活 | DynamicTheme 模块（激活由**生成器**做，见 `extension.md`） |
| 生成器 / 分析器 | **本项目不引用分析器包**（`VeloxDev.WinUI.csproj:26-31` 只有 SDK.BuildTools、WindowsAppSDK、`VeloxDev.Core` 三条），自己一行 `[VeloxProperty]` 都没有 |
| `dotnet new` 模板包 | `Src/Templates/VeloxDev.WinUI.Templates/` 是**纯内容包**（`IncludeBuildOutput=false`、`EnableDefaultCompileItems=false`、无任何 `ProjectReference`）⇒ 装模板**不会**带来 `VeloxDev.WinUI` 依赖，宿主必须自己引。**另外它有一条恒不匹配的 glob**：模板包把 `..\..\skills\...\references\**\*` 打进 `references/`，而相对路径算下来不是仓库根的 `skills/` ⇒ 这一条**静默什么都不打**；同样的 glob 在 Avalonia / MAUI / Razor / WPF / WinForms 五个包里逐字重复（Jalium 的 csproj 没有 `working/` 那层、也就没有这条）—— 整族模板包的同一个静默缺陷，不是 WinUI 特有的 |
| 网格装饰器本体 | **七家适配器都没有** `WorkflowGridDecorator` 的实现（`grep -rn "class WorkflowGridDecorator" Src/Adapters/` 零命中；契约 `IWorkflowGridDecorator` 只被当接口用），它是**示例产物**，`Examples/Workflow/` 下几个平台各有一份（WinUI 那份在 `Examples/Workflow/WinUI Trimmed/Demo/Views/Workflow/WorkflowGridDecorator.cs`，`sealed class : Grid, IWorkflowGridDecorator`）。但契约要求的 `RulerBand → SetVirtualizeInset` 转发**在适配器里**（见 §三·5） |

---

## 二、`PlatformAdapters/`：注册与工厂

### 2.1 三条注册路径，只有一条需要宿主动手

| 要注册的东西 | 注册点 | 什么时候真的发生 |
|---|---|---|
| 10 个采样器 | `Interpolator` 的**静态构造**（`Interpolator.cs:12-25`；10 条登记在 `:14-23`） | 第一次**构造** `Transition<T>` 时：Core 的字段初始化 `protected TInterpolatorCore interpolator = new();`（`Src/Core/VeloxDev.Core/TransitionSystem/Effects/Transition.cs:296`，约束 `new()` 在 `:270`） |
| 采样器所在的宿主/解释器/优先级 | `TransitionScheduler<TTarget>` 的类型实参（`TransitionScheduler.cs:5-11`） | 同上，全部编译期写死 |
| DynamicTheme 的调度器工厂 | `Interpolator.CreateScheduler`（`Interpolator.cs:27-30`） | **宿主必须显式调** `ThemeManager.SetPlatformInterpolator(new Interpolator())` |

**没有程序集级入口，也没有 `Initialize()`**：全模块 `grep ModuleInitializer` / `[assembly:` 零命中，唯一的静态构造就是 `Interpolator.cs:12`。⇒ 只用 `Transition<T>` 的宿主什么都不用做；只走 DynamicTheme 的宿主漏掉 `SetPlatformInterpolator` 会**静默瞬切**（这条与 WPF 同形，理由与各家差异见 DynamicTheme 与 TransitionSystem 的记忆）。

`CreateScheduler` 里的类型实参是三处的**第三份**抄写（另两处是 `TransitionScheduler.cs:5-11` 与 `Transition.cs:17-25` 的基类列表）。`effect is ITransitionEffect<DispatcherQueuePriority>` 这个判断**只认 `DispatcherQueuePriority`**（这里是唯一与别家不同的类型实参：WPF/Avalonia/Jalium 是 `DispatcherPriority`，MAUI/WinForms/Razor 是 `NonPriority`）⇒ 传一个别家的 `TransitionEffect` 进来会**静默返回 `null`**（没有调度器、也没有异常）。

### 2.2 这家的线程判定：先问 target，再问「缓存的 UI 线程」，而且会**自己报死**

`UIThreadInspector`（`UIThreadInspector.cs`，64 行）分三层：

1. `QueueFor(target)`（`:36-41`）：target 是 `DependencyObject` 且有 `DispatcherQueue` 就取**它自己的**；
2. 否则走 `EnsureQueue()`（`:25-34`）：**懒捕获**当前线程的队列并用 `Interlocked.CompareExchange` 存进静态字段（首次非空者胜，之后不再覆盖）；
3. `public static CaptureUIThread()`（`:16-21`）是**给宿主主动调**的入口，在非 UI 线程上会抛 —— 但**全仓没有 WinUI 侧调用点**（唯一调用点在 Razor 的 Blazor demo）。这条 API 在本模块里是**为「target 不是 `DependencyObject`」的宿主留的**，靠第 2 层兜底时它其实用不上。

**真正的这一家独有处在 `PostCore`（`:54-62`）：**

```csharp
var accepted = queue.TryEnqueue(priority, () => action());
Lifetime.SetAlive(accepted);
return accepted;
```

`Lifetime` 定义在 Core `Runtime/TransitionHostBase.cs:12`（`protected ApplicationState Lifetime { get; } = new();`），`IsAlive` 在 `:15` 转发它，消费方是 `SamplerSet.CanSetValue()`（`Src/Core/VeloxDev.Core/TransitionSystem/Sampling/SamplerSet.cs:90`，`=> _host.IsAlive`）—— 也就是**每一次属性写入的闸门**。

⇒ 这条链的语义是：**队列拒绝一次 `TryEnqueue`，整个宿主就被标记为「应用已死」，所有属性写入立刻停；再有一次被接纳就复活。** 七家里**只有这一家**写了 `SetAlive`（`grep -rc SetAlive Src/Adapters/VeloxDev.<家>/` 只有 `VeloxDev.WinUI/PlatformAdapters/UIThreadInspector.cs` 命中源码，其余全是 `bin/obj` 里 `VeloxDev.Core.dll` 的二进制命中）；Core 侧那句 remarks 直接点名了这一家（`Src/Core/VeloxDev.Core/Lifetime/IApplicationState.cs:17-21`：「WinUI clears its flag when a single enqueue is refused … has to be able to take it back」）。**所以 `SetAlive` 是刻意双向的，别把它「修」成只置 false。**

`InternalPriority => DispatcherQueuePriority.Normal`（`:51`）与 `TransitionEffect.Priority` 默认 `High`（`TransitionEffect.cs:9` 是那个属性，理由写在 `:7` 的注释里：以 High 优先级排队动画帧写入、让它们在渲染前处理以减少卡顿）是两个不同的档位来源，不要混。全模块只用过 `High`/`Low`/`Normal` 三个档（`grep -oh 'DispatcherQueuePriority\.[A-Za-z]*'` 命中 1/5/2 次，**没有任何 `Render`**）—— 这一家的档位表见 `memory/modules/TransitionSystem/adapters/winui.md`。

### 2.3 空壳、默认值与两个「读过才知道」的形状

- `State : StateCore`（`State.cs`，7 行）、`Transition : TransitionCore`（`Transition.cs:12-15`）是空壳，**别以为漏写了**。
- `TransitionEffects` 的三个预设（`TransitionEffects.cs:8`/`:13`/`:18`：`Empty` 0s / `Theme` 0.46s / `Hover` 0.32s）**七家逐字相同**（含 `{ get; set; }` 的可写形态），不是这家特有的；唯一的差别是这里写成 `class` 而非 `static class`（七家里只有这一家不带 `static`，逐家 grep 实测确认）。因为三个成员在**任何一家**都是可写的静态属性，宿主可以全局改掉 `TransitionEffects.Hover` 的时长 —— 之后所有新动画跟着变，这不是 WinUI 独有的坑，只是在这一家更容易被误当成「实例属性」。
- **`TransitionScheduler<TTarget>` 的 `TTarget` 是个没人用的类型参数**（`TransitionScheduler.cs:5-11`，类体空）：全仓 `grep 'new TransitionScheduler<'` **零命中**（没有任何构造点）；带 `<TTarget>` 这个形状的只有本家与 Avalonia 两家，另外五家是**非泛型**的 `TransitionScheduler`。真正被 `CreateScheduler` 用来建调度器的是 Core 的 `TransitionSchedulerCore<THost, TInterpreter, TPriority>.FindOrCreate(target)`（`Src/Core/VeloxDev.Core/TransitionSystem/Runtime/TransitionScheduler.cs:197`，`GetValue` 原子安装 + `TargetRef` 弱引用）。**别拿 `new TransitionScheduler<T>()` 当 `Transition<T>` 的调度器**：它不参与 `MutualSchedulers` 归档，而按 target 找回 run 的操作（`TransitionCore.Pause`/`Seek` 那条链）走的正是那张字典，`Src/Core/VeloxDev.Core/TransitionSystem/Effects/Transition.cs:217-226` 的 `CollectSchedulers` 就是它的读者。
- `TransitionInterpreter` 声明成 `partial`（`TransitionInterpreter.cs:7`）但**全项目没有第二半**（grep `partial class TransitionInterpreter` 各只有它自己那一行）。MAUI 同形，看不出理由，**只能存疑**。
- 帧 pacer 用 `DispatcherQueueTimer` 且 `IsRepeating = false`（`TransitionInterpreter.cs:43`，建表在 `CreateTimer` `:40-46`），靠 `Arm` 里再 `Start()` 续帧（`:19-24`）—— 「一次性定时器 = 帧时钟」这个形状是这一家的。`CreateFramePacer` 在 `:10-13`，`TryGet<DispatcherQueue>` 失败就返回 `null`（拿不到队列就没有 pacer，动画照跑）。
- `Transition.cs` 的 `ICollection<Transform>` 重载（`:56-76`）是采集多重变换的唯一入口，注释讲清了为什么单 `Transform` 要保住运行时类型 —— 属 TransitionSystem 轴，不在此展开。

---

## 三、`Attached/Workflow/`：每个角色的入口与挂载方式

> 契约要哪些成员、`PART_*` 怎么命名、七角色各自负责什么，在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3 与本家轴文档 §一。本节只写**这一家挂上去之后长什么样**。

### 3.1 八种挂法，五种宿主门槛

| 角色 | 怎么挂上去 | 宿主类型门槛 | 每元素状态存哪 | 需要谁的 `DataContext` |
|---|---|---|---|---|
| 画布宿主 `WorkflowSurfaceBehavior` | `IsEnabled="True"` + `ZoomEnabled` + 5 个 `*Name` + `LinkMenuKey` | **`UserControl`**（`:348`） | 私有附着 DP `StateProperty`，装 `SurfaceState`（`:110-114`） | **宿主自己**的 DC 必须是 `IWorkflowTreeViewModel`（`:269`/`:316`/`:550`/`:728`/`:805`/`:952`/`:1064`） |
| 插槽布局 `WorkflowSlotLayoutBehavior` | `IsEnabled="True"` + `SlotNames`/`SlotEnumeratorNames`/`CoordinateHost*` | **`UserControl`**（`:82`） | 私有附着 DP `StateProperty`（`:59`） | 被挂的那个 `UserControl` 自己的 DC 是 node VM |
| 节点拖拽 `WorkflowNodeDragBehavior` | `IsEnabled="True"` + `CoordinateHostName/Type` | **`UIElement`**（`:59`）——最宽 | 私有附着 DP `StateProperty`（`:39`） | 自己或**任一祖先**（`ResolveNode` `:189`） |
| 插槽连接 `WorkflowSlotConnectionBehavior` | `IsEnabled="True"` | **`Control`**（`:22`） | 无 | **必须是自己**，不找祖先 |
| 视图池 `ViewPool` | 赋 `ItemsSource` + 可选 `TemplateSelector` | **`Panel`** | 唯一用 `ConditionalWeakTable<Panel, ViewManager>`（`ViewPool.cs:26`） | 不读；它是**写** DC 的那一方（`ViewManager.cs:187`） |
| 画布变换 `WorkflowCanvasTransformBehavior` | 不用挂：它是**被写**的通道（`WorkflowSurfaceBehavior.ApplyLayout` 写它，`WorkflowCanvasTransformBehavior.cs:22-23`）。⚠ WinUI 侧**没有任何 XAML 读它**，画布平移走 `Canvas.Translation` —— 见 §五·5 | `UIElement`（值载体） | 无 | 不读 |
| 小地图 `WorkflowMinimapOverlay` | 不用挂：**继承它** + 赋 `WorkflowTree`/`ScrollViewerName` | 继承（`Canvas` 子类、实现 `IWorkflowMinimapOverlay`，全模块唯一非静态的公开可继承元素类，`WorkflowMinimapOverlay.cs:21`） | 实例字段（它本身就是元素） | 不读 |
| 模型事件桥 `WorkflowEvents` | 绑 `Node`/`Slot`/`Tree` 到 sink | `FrameworkElement` | `ConditionalWeakTable<FrameworkElement, Attachment>` | 订阅跟随元素自己的 DC（转发在 Core `WorkflowEventRelay`） |

三条容易踩的：**五个布尔开关默认全是 false**（四个 `IsEnabled` 的 `PropertyMetadata(false, …)` 加 `ZoomEnabled`，`WorkflowSurfaceBehavior.cs:56`/`:92`），必须显式打开；`ViewPool` 与 `WorkflowCanvasTransformBehavior` **没有** `IsEnabled`；插槽连接挂在插槽的**外层容器**上会因为 DC 不在自己身上而静默失效（节点拖拽反而会沿祖先找到，别拿它推）。

每个 `OnIsEnabledChanged` 都是**先 `Detach` 再 `Attach`**（`WorkflowSurfaceBehavior.cs:364` 是 `Attach` 的第一行；`OnIsEnabledChanged` 在 `:346-360`）⇒ 重复置 `True` 不会叠加订阅；`Detach` 里 `control.ClearValue(StateProperty)`（`:408`）顺带丢状态。**注意 `Attach` 里 `ResolveNamedControls` 与 `Refresh` 是连着调的**（`:384-385`），而 `Refresh` 内部第一件事又是 `ResolveNamedControls`（`:157`）⇒ 挂载时解析跑两遍。不是笔误，但也没必要。

### 3.2 `Refresh` 是唯一的重驱动入口，而**这条自愈路径会先把自己退订**

`WorkflowSurfaceBehavior.Refresh(UserControl)`（`:148-162`）＝ 重解析 5 个命名部件 → `WireLinkMenu` → `CaptureViewportRestore` → `ApplyLayout` → `UpdateVisibleRegion`；判断在第一行：`if (!GetIsEnabled(host)) return;`（`:150-153`）⇒ 没打开开关时调它**什么也不做、不抛**。

除 §3.1 说的 `Attach`（`:385`）那次之外，它还被三处自动调用：`Loaded`（`:411-417`）、`DataContextChanged`（`:427-433`）、以及 **`ScrollViewer.ViewChanged`（`OnViewChanged` `:936-948`）**。最后这条是本家与别家最大的行为差异，值得单说：

- `OnViewChanged` 先沿视觉树从 `ScrollViewer` 往上找**第一个 `GetIsEnabled` 为真的 `UserControl`**（`:943`），找到就调**全量** `Refresh(host)`（`:946`）；
- 于是**滚动/缩放期间每一帧**都会跑一遍 `ResolveNamedControls`；
- 而 `ResolveNamedControls` 第一行是 `UnsubscribeResolvedControls(state)`（`:437`），它会 `state.ScrollViewer.ViewChanged -= OnViewChanged`（`:494`），**把 `PointerPressSource`/`ScrollViewer`/`Canvas`/`GridDecorator`/`MinimapOverlay` 全部置 null（`:498-502`）并 `UnhookZoom`**，然后由下面的解析循环重新赋值（`ViewChanged` 与 `HookZoom` 的新订阅在 `:476`/`:481`）。**也就是说：这条「自愈」路径在每次被调用时先把自己的订阅拆掉、再按名字挂回去。**
- **推论（改这里必须先想清楚的一条）**：只要有一次重解析没找到 `ScrollViewer`（模板被换掉、名字域丢了、宿主正在离树），`ViewChanged` 订阅就**永久丢失**，而它正是那个「自己会回来」的机制 ⇒ 之后只剩 `Loaded` / `DataContextChanged` / 宿主显式调 `Refresh` 三条路。名字解析失败本身是静默的（`FindName` 返回 null，不抛）。

这也解释了为什么**参考实现里一次 `Refresh` 都没有**：`Examples/Workflow/WinUI Trimmed/Demo/Views/Workflow/TreeView.xaml.cs` 全文 `grep Refresh` 零命中；而非 Trimmed 的旧 demo 有四处。

### 3.3 同一个 `UserControl` 上，名字解析有两套机制

这是只读这两个文件才看得出来、而且两种机制**在同一份 XAML 声明里相邻出现**的事：

| 附着属性 | 谁来解析 | 机制 | 失败条件 |
|---|---|---|---|
| `SlotNames` / `SlotEnumeratorNames` | `SyncNamedSlot` → `parentHost.FindName(controlName)`（`WorkflowSlotLayoutBehavior.cs:330`）；枚举名走 `SyncSlotEnumerator` | **名称域查找**（`FindName`） | 名字不在那个 `UserControl` 的 `x:Name` 域里就找不到 —— 哪怕它在视觉树上、哪怕它的 `.Name` 就是那个字符串 |
| `CoordinateHostName` | `ResolveNamedHost`（`WorkflowSlotLayoutBehavior.cs:416-421`；`WorkflowNodeDragBehavior.cs:176-187` 同形） | **自己 + 祖先逐个比 `.Name`** | 目标必须是**祖先**（后代/兄弟不行） |

⇒ 三条推论：槽位名必须写在该模板**自己的名称域**里（`x:Name="PART_InputSlot"` 在 `NodeView.xaml` —— 这是 NodeView 自己的域，不是宿主 TreeView 的）；坐标宿主名必须指向**祖先**（`CoordinateHostName="PART_Canvas"` 指向 `TreeView.xaml` 里的画布）；**同一个名字在两种机制下的可见范围不同**，把两者互换会静默退化。

`ResolveCoordinateHost` 还有一条默认（`WorkflowSlotLayoutBehavior.cs:400-414`、`WorkflowNodeDragBehavior.cs:160-174`）：名字没配或找不到时按**类型**往上找，节点拖拽的兜底类型是 `typeof(Canvas)` —— 也就是说没配 `CoordinateHost` 时不报错、而是悄悄换成「最近的 Canvas」。

### 3.4 `ViewManager` 对**连线**视图一个属性都不写

`ViewManager.ApplyLayout`（`ViewManager.cs:338-359`）分两支：

- 节点视图：写 `Canvas.SetLeft/SetTop/SetZIndex`，并按 `Size` 是否 > 0 决定写 `Width`/`Height` 还是 `double.NaN`（`:342-350`，注释：`(0,0)` 是「还没测量」，NaN 让平台自己定尺寸 —— WinUI 里 `Width = 0` 是**真的宽 0**）；
- 连线视图：**什么都不写**（`:351-357` 的空 `break`）。注释给出的理由是：Trimmed 的 `LinkView` 自己定位到 `−ActualOffset` 并把 `+ActualOffset` 烘进几何（offset frame），在 `Sender`/`Receiver`/`Anchor` 变更时把它钉回 `(0,0)` 会**抹掉这个自定位**、让整条线平移一个 cover 的量；而**不自定位的视图保持默认 `(0,0)`，与旧的钉法等价**。

⇒ 换上一套「不自定位、指望池化器摆位置」的连线模板时，行为**恰好**退化成旧的钉 `(0,0)`，不报错 —— 所以看到空 `break` 不要以为漏写了。对照 WPF/Avalonia 的 `ViewManager` 只写 `Visibility` + `DataContext`，几何全交模板。

另外三条：分批物化是 `Low` 优先级、每批 3 个（`ViewManager.cs:110-157`，入队被拒时把旗标放回 `:122-127`）；`SubscribeToLayoutChanges` 只认 `Anchor`/`Size`/`Sender`/`Receiver`/`Slot.Anchor` 五个属性名（`:286-313`），跨线程时排一个 `Low` 闭包**且不重校验**（`:307`，见 §五·2）。

### 3.5 小地图：`RulerBand` 恒为 0，而虚拟化 inset 读的**不是**它

- `WorkflowMinimapOverlay` 是**基类**，宿主写子类（demo 的空壳子类只设画刷）；它继承 `Canvas` 并实现 `IWorkflowMinimapOverlay`（`:21`），因此**同时满足 `IWorkflowGridDecorator`**（契约 `Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/IWorkflowMinimapOverlay.cs:18` 继承 `IWorkflowGridDecorator`）。
- 它的 `public double RulerBand => 0;`（`:130`，带 `/// <inheritdoc />`），而 `RulerThicknessProperty`（`:65-66`）与它的 CLR 访问器（`:137`）**全模块从不被读**（grep 只有声明与访问器命中）—— 与 WPF 逐条同形（那条坑在 `WorkflowSystem/adapters/wpf.md` 坑 2，本条是它在 WinUI 侧的**推论**）。
- 而虚拟化 inset 的真实来源是**宿主配在 `GridDecoratorName` 上的那个装饰器**：`WorkflowSurfaceBehavior.UpdateGridDecorator`（`:1099-1114`）`viewModel.SetVirtualizeInset(left: decorator.RulerBand, top: decorator.RulerBand)`（`:1112`）。demo 的 `WorkflowGridDecorator.RulerBand` 返回它的 `RulerThickness`。
  ⇒ **把 `GridDecoratorName` 指到小地图上会编译通过、跑起来也不抛**，但 inset 静默变成 0（浮标尺带下面的节点会被提前裁掉一整个尺宽）。`MinimapOverlayName` 与 `GridDecoratorName` 各自独立解析（`ResolveNamedControls`），不要混用。
- 它自己找 `ScrollViewer` 只靠自己那一侧：`Loaded` 时 `ResolveScrollViewer`（`:204-228`）沿视觉树上溯到最近的 `UserControl` 再 `FindName`，**不问**同宿主上的 `WorkflowSurfaceBehavior`。顺带在 `ScrollViewer.SizeChanged` 上读 `ActualWidth/Height` 重推视口（`:216`/`:230-244`）。`ScrollViewerName` 是无回调的普通 DP ⇒ 晚设就永远没有 `_scrollViewer`，小地图变成只看不动的图（WPF 同形，见其架构记忆 §五·6）。
- 重绘是**节流不是去抖**（`ScheduleRebuild` `:332-349`，注释 `:335-339`），配 ctor 里建的 16 ms `DispatcherQueueTimer`（`:172-202`，Tick 吞 `COMException` `:190`）。指针按下**总是重新居中**、没有抓取锚点（`:424` 的注释）。

---

## 四、`GlobalUsings.cs` 与 `VeloxDev.WinUI.csproj`

**`GlobalUsings.cs` 三行，七个适配器逐字相同**（`global using` `VeloxDev.TransitionSystem` / `VeloxDev.TransitionSystem.Abstractions` / `VeloxDev.Threading`）：

- 这就是为什么本模块的 `PlatformAdapters` 文件都不写 `using VeloxDev.TransitionSystem;` 却能用 `InterpolatorCore`、`ISampler`、`DispatcherQueuePriority`。
- **它不含 `VeloxDev.WorkflowSystem`** ⇒ `Attached/Workflow/` 里需要的文件各自写 `using VeloxDev.WorkflowSystem;`。
- **`global using` 是编译期的，不随包/`ProjectReference` 传给消费者**：宿主要用 `Transition<T>` 仍得自己写 `using VeloxDev.TransitionSystem;`。

**csproj 里影响代码本身的条件**（`VeloxDev.WinUI.csproj`，32 行）：

| 事实 | 行 | 后果 |
|---|---|---|
| `<TargetFrameworks>net8.0-windows10.0.19041.0;net10.0-windows10.0.19041.0` | `:3` | 七家里唯一这个二元组。注意它与 `:6` 的 `TargetPlatformMinVersion 10.0.17763.0` 是两个不同的数，别当成重复而删掉一处 |
| `RuntimeIdentifier` 默认 `win-x64`、`RuntimeIdentifiers` 三档（`:8-9`，都带 `Condition="'$(X)' == ''"`） | | 所以**输出路径带 rid**（`bin/Debug/net8.0-windows10.0.19041.0/win-x64/`）；找产物时按 TFM 直接搜会少一层 |
| `<UseWinUI>true` + `WinUISDKReferences false` + `EnableCoreMrtTooling false`（`:10`/`:12`/`:13`） | | 后两条是**关掉**默认注入（XAML 引用与 MRT 资源工具）—— 这一家不产出 WinUI 资源索引，`Examples/Workflow/WinUI Trimmed/Demo/Demo.csproj:14` 也是 `false` 同款 |
| `<Nullable>enable`（`:11`）、`LangVersion latest`（`:14`）、`AllowUnsafeBlocks`（`:15`） | | csproj 已开可空，但模块里仍有 2 个文件在**文件头**重复写 `#nullable enable`（`PlatformAdapters/ThemeValueConverters.cs:1`、`PlatformAdapters/UIThreadInspector.cs:1`）—— 冗余而非必需，别以为 csproj 那条没生效 |
| **没有** `GenerateDocumentationFile`、**没有** `NoWarn` | — | 与 WPF 不同（WPF 两个都有）。⇒ 这一家不产 XML 文档、也没有 WPF §五·2 那类静默覆盖 |
| Debug → `ProjectReference`（`:29`）／非 Debug → `PackageReference VeloxDev.Core 10.0.0`（`:30`） | | 与生成器那套双轨同形 |
| `GeneratePackageOnBuild`（`:16`）+ `Version 10.0.0`（`:18`）+ 图标（`:21`） | | 与本仓库其它非 Core 项目同款 |

**条件编译只有一处，而且在这家恒为真**：`PlatformAdapters/Transition.cs:212-241` 的 `#if !NETSTANDARD2_0` 包着 4 个 `System.Numerics` 重载（`Vector2`/`Vector3`/`Vector4`/`Quaternion`）—— 本模块两个 TFM 都不带 netstandard，所以这 4 个重载**永远编进去**。（全模块只有这一个 `#if`，`grep '^#if'` 命中 1 文件 1 处。）这与 WPF 那条坑同形（WPF 的守卫同样恒为真），但**同样的守卫在 Avalonia 那边是真的有用**（它有 netstandard2.0）。顺带一个不显眼的事实：同文件 `:204` 的 `RectangleF` 重载用的是 `System.Drawing.RectangleF`（来自 `System.Drawing.Primitives`，与 Windows 无关），**没有**被任何守卫包住。

**demo 侧的耦合**：三个 WinUI demo 都用 `SetTargetFramework` 锁到自己的 TFM 再引用本模块（`Examples/Workflow/WinUI Trimmed/Demo/Demo.csproj` = `net10.0-…`、`Examples/Transition/WinUI/Demo/Demo.csproj` 与 `Examples/Workflow/WinUI/Demo/Demo.csproj` = `net8.0-…`），且 demo 自己引的 `Microsoft.WindowsAppSDK` 是 **1.8.x**，比本模块的 **1.6.241114003**（`:28`）新 —— 两个版本号在同一个进程里并存是既成事实，不是笔误。

---

## 五、陷阱（带依据）

1. **参考实现的 code-behind 是空壳，不是探针。** `Examples/Workflow/WinUI Trimmed/Demo/Views/Workflow/TreeView.xaml.cs` 现在只有 `using Microsoft.UI.Xaml.Controls;` 加一个 `InitializeComponent()` 构造函数（11 行）；`GetTempPath` / `DispatcherTimer` / `File.AppendAllText` 在整个 `WinUI Trimmed` 项目下**零命中**。
   **没有漂移探针**：那份文件是纯空壳（`GetTempPath` / `DispatcherTimer` / `File.AppendAllText` 在该项目下零命中）。**skill 侧已同步**：`skills/veloxdev-create-workflow/references/gui/winui.md` 不再提「demo 的 `TreeView.xaml.cs` 带着遗留漂移探针」—— 现在那个目录（`.xaml` 与 `.xaml.cs`）**整体可照抄**。
2. **`ViewManager.cs:307` 的延迟闭包不重校验自己还是当前状态。** 跨线程那条 `TryEnqueue(Low, () => ApplyLayout(view, viewModel))` 捕获了 `view`/`viewModel` 后没有确认这一对还在活跃；对照同目录 `WorkflowSurfaceBehavior.cs:1033-1042`（排低优先级之前与落地之后都做 `GetIsEnabled(host)` + `ReferenceEquals(currentState, state)`）。**后果**：一帧内视图被回收并复用给另一个 ViewModel 时，旧几何会写到新宿主上 —— 且不报错。
3. **`Refresh` 挂在 `ViewChanged` 上 ⇒ 每帧一次全量重解析，且成功是「自己把自己重挂一次」**（§三·2）。改 `ResolveNamedControls` 或 `UnsubscribeResolvedControls` 时记住：**这两段的副作用是热路径**，在滚动期间每帧执行。
4. **`ViewManager` 模板缓存按 `Type` 键、永不失效**（`ViewManager.cs:22` 的 `_templateMap`，读取短路 `:244-247`）—— `DataTemplateSelector` 对同一类型的第二次返回不会被再问一遍；只有在 `SetTemplateSelector` 时才 `Clear()`（`:31`），没有别的清缓存 API（WPF 同形，见其架构记忆 §五·7）。
5. **`WorkflowCanvasTransformBehavior.cs:28` 的注释与代码不符 —— 这一条已在 `memory/modules/WorkflowSystem/adapters/winui.md` §四·P1 记过，本文不重复**，只提醒它就在本模块的 `Attached/Workflow/` 里，且 `ApplyLayout` 现在**同时**把偏移写进 `Canvas.Translation`（`WorkflowSurfaceBehavior.cs:1013-1016`）。
6. **`IsSurfaceBlankInteraction` 认类型靠字符串**（`WorkflowSurfaceBehavior.cs:1182` 的 `"ScrollContentPresenter"`、`BezierCurveView`/`PolylineCurveView` 判据）—— 同轴文档 §二·L8 已记；补充一条本模块侧的事实：**契约类型那一半用的是 `DataContext is IWorkflowNodeViewModel or IWorkflowSlotViewModel`（`:1186`），可靠**；不可靠的只有 WinUI 内部类型与 demo 类型。
7. **`ViewPool` 的生命周期只在 `Panel` 上**：`ConditionalWeakTable<Panel, ViewManager>`（`ViewPool.cs:26`）+ `panel.Unloaded`（`:61`）⇒ 换 `ItemsSource` 走 `CleanupManager`（旧值非空时，`:49`；`Managers.Remove` 在 `:91`），**没有**显式的「拆下」API；`Panel` 活着但已被移出可视树时，manager 仍持有它。注意 `TemplateSelector` 变了只 `SetTemplateSelector` 重建缓存（`:64-75`），**不**重建池。
8. **模块里若干文件带 UTF-8 BOM，而 `State.cs` 的 `namespace` 正好在第 1 行** ⇒ 任何按行首锚定的统计（`grep -c '^namespace'`、`sed -n '1p'`）都会**静默少算**，把 `VeloxDev.TransitionSystem` 数成比实际少一个。要数就用 `sed -e '1s/^\xef\xbb\xbf//'` 先削 BOM 再数。这条值得单独记，是因为少算出来的数字看起来完全正常 —— 与 §一 那个 28/29 的漏法是两回事：那个是 **pathspec** 漏掉目录本级文件，这个是**文件内容**骗过行首锚点。

---

## 六、入口：我要改 X 先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 画布平移 / 缩放 / Ctrl+滚轮 / 空白判定 / 平移到边时的扩张 | `Attached/Workflow/WorkflowSurfaceBehavior.cs`（数学在 Core `WorkflowSurfaceMath`） |
| 连线右键菜单 / 悬停焦点 / Delete / 视口恢复 | 同上（`WireLinkMenu` `:166`、`ShowLinkMenu` `:263`、`OnLinkKeyDown`、`CaptureViewportRestore`） |
| 「模型改完了让视图重算」的入口 | 同上 `Refresh(UserControl)`（`:148`）；先看 §三·2 再决定要不要调 |
| 节点拖拽的落点与坐标宿主 | `Attached/Workflow/WorkflowNodeDragBehavior.cs` |
| 插槽锚点写回、刷新时机 | `Attached/Workflow/WorkflowSlotLayoutBehavior.cs`（注意 `:330` 与 `:416-421` 是两种名字解析） |
| 插槽两阶段连接命令 | `Attached/Workflow/WorkflowSlotConnectionBehavior.cs` |
| 视图池、`DataTemplate` 查找、分批 | `Attached/Workflow/ViewManager.cs`（挂点 `ViewPool.cs`） |
| 小地图外观与导航 | `Attached/Workflow/WorkflowMinimapOverlay.cs` |
| 把模型事件接给宿主的 sink | `Attached/Workflow/WorkflowEvents.cs`（转发在 Core `WorkflowEventRelay`） |
| 画布变换的值（写值处） | `Attached/Workflow/WorkflowSurfaceBehavior.cs:999-1022` 的 `ApplyLayout`（**不是** `WorkflowCanvasTransformBehavior.cs`，后者只有 30 行：一个 DP 加一个故意空的回调） |
| 线程、优先级、pacer、调度器、`Lifetime` | `PlatformAdapters/` 九个类型 —— 差异与坑见 `memory/modules/TransitionSystem/adapters/winui.md` |
| 让某个 WinUI 类型可动画 | `PlatformAdapters/Samplers/` + `PlatformAdapters/Interpolator.cs:14-23` 的注册表 |
| 主题字符串 → 值 | `PlatformAdapters/ThemeValueConverters.cs`（含 `ThemeResourceLookup`，会递归 `ThemeDictionaries` 与 `MergedDictionaries`） |
| 包结构、TFM、rid、双轨引用 | `VeloxDev.WinUI.csproj` |
| **宿主怎么把这些接起来（最小可读样本）** | `Examples/Workflow/WinUI Trimmed/Demo/Views/Workflow/` 下的五个 `.xaml`：`TreeView.xaml`（画布宿主 + 装饰器 + 视图池 + 小地图）、`NodeView.xaml`（节点模板 + 插槽布局 + 节点拖拽）、`SlotView.xaml`（插槽连接）、`LinkView.xaml`（连线视图）、`InfoOverlay.xaml`（浮层）。**只看 `.xaml`；`.xaml.cs` 见 §五·1** |

---

## 七、这份文件没写的东西

- 七个角色各自要暴露什么成员、`PART_*` 命名约定、七角色职责表 —— `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3 与 `skills/veloxdev-create-workflow/references/view-layer.md`。
- 这家的平台硬限制、与别家的刻意背离、逐条坑（L1–L8、D1–D7、P1–P7：无 Preview 阶段、无公共 `OnRender`、`LayoutUpdated` 挂两级、名称域只有 `FindName`、合成平移与 offset frame、优先级只有三档、WinRT 激活、类型名字符串命中测试）—— `memory/modules/WorkflowSystem/adapters/winui.md`。
- TransitionSystem 侧的采样器逐一分析、`DispatcherQueueTimer` 帧 pacer、`Lifetime.SetAlive` 的取舍 —— `memory/modules/TransitionSystem/adapters/winui.md`。
- 主题切换的完整流向、两级缓存、转换器如何被**生成器**激活 —— `memory/modules/DynamicTheme/architecture.md`。
- 工作流宿主怎么写（`using:` 命名空间、`Clip="{x:Null}"`、offset frame 的模板写法、`NodeView` 的 Viewbox 与 `PART_*` 摆位）—— `skills/veloxdev-create-workflow/references/gui/winui.md`。
- 本模块的验证线在 `Examples/Transition/AUTO TEST/`（`Samplers/VeloxDev.SamplerTest.csproj` 直接引本模块，`Samplers/WinUiEntries.cs`、`Conformance/WinUiConformance.cs`、`LoadMode/WinUiLoadMode.cs`、`Drivers/WinUIDemoDriver.cs` 是这家的四个入口）；工作流侧可跑的宿主就是 §六 表末那一组 `.xaml` 所在的 demo。
