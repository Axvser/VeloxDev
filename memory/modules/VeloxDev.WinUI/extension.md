# VeloxDev.WinUI — 扩展

> 先读 [`architecture.md`](architecture.md) §一 确认你要动的是**三条轴里的哪一条**。三条轴在本项目里互不引用：
> 改 WorkflowSystem 的挂载不会碰到 TransitionSystem 的注册表，反之亦然；但**改 csproj 会同时影响三条轴**。
>
> 本文件只写「怎么做才对」与「哪条路看着能编译但会错」。契约本身（要哪些成员、`FindOrCreate` 为什么必须用、
> `PART_*` 命名）在 `memory/modules/WorkflowSystem/extension.md` 与 `memory/modules/TransitionSystem/extension.md`。

---

## 一、加一个可动画的 WinUI 类型（采样器）

### 步骤

1. **写类**：`Src/Adapters/VeloxDev.WinUI/PlatformAdapters/Samplers/<名字>Sampler.cs`，命名空间必须是 `VeloxDev.Adapters.NativeSamplers`（本模块 10 个采样器全在这个命名空间，见 `Samplers/PointSampler.cs:3`）。
   形状照 `Samplers/PointSampler.cs`（21 行，最小的一份）：实现 `ISampler` 的三个方法 `NormalizeStart` / `NormalizeEnd` / `InsertFrame`；`ISampler` 由 `GlobalUsings.cs:2` 的 `global using VeloxDev.TransitionSystem.Abstractions;` 提供，**不要**再写一行 `using`。
2. **注册**：在 `PlatformAdapters/Interpolator.cs:14-24` 的静态构造里加一行 `RegisterInterpolator(typeof(你要动画的类型), new <名字>Sampler());`。这一张表就是全部注册；没有别的注册点。
3. **登记到闭式解套件**（只有「产出的值本身是值」的采样器才能走这条）：`Examples/Transition/AUTO TEST/Samplers/WinUiEntries.cs` 加一条 `SamplerEntry`，并在同文件的 `Target` 类（`:31-41`）上补一个属性让 `Write` 有真实目标可写。
4. **登记到验收表**（产出的值是 **WinRT `DependencyObject`** 的那一类走这条）：`Examples/Transition/AUTO TEST/Conformance/WinUiConformance.cs` 加一条，并由真跑起来的 demo 驱动（`Samplers/UnreachableSamplers.cs` 是这条路的名字来源）。
5. **跑两条验证线**：`Examples/Transition/AUTO TEST/Samplers/VeloxDev.SamplerTest.csproj`（`SamplerCoverageTests` + `SamplerConformanceTests`）与 `Examples/Transition/AUTO TEST/`（`Conformance/`、`LoadMode/WinUiLoadMode.cs`、`Drivers/WinUIDemoDriver.cs`）。

### 联动清单（漏一处的后果）

| 位置 | 漏掉的后果 |
|---|---|
| `Interpolator.cs:14-24` | **静默**：该类型永远走不到动画路径，`Transition<T>` 不报错、不抛，只是值突变 |
| `Samplers/WinUiEntries.cs` | **响亮**：`SamplerCoverageTests.EveryShippedSampler_IsAccountedFor`（`Samplers/SamplerCoverageTests.cs:65`）会失败 —— 这条测试存在的理由写在同一份测试文件的 remarks（`:12-14`）：注册表查不了自己，漏登记的采样器只是永远不跑、套件反而是绿的，所以必须有一条「反查程序集」的测试。反查是按**类型名**在 `typeof(ProjectionSampler).Assembly`（`WinUiEntries.cs:44`）里找的（拼名在 `:59`），同名类型要显式取 WinUI 那一侧 |
| `Samplers/VeloxDev.SamplerTest.csproj` 的 `ProjectReference`（`:29`） | **响亮**：`SamplerCoverageTests.EveryAdapterAssembly_IsLoadedSoItsSamplersAreCounted`（`:50`）失败，消息直接指向这个 csproj（`:61`） |
| `Conformance/WinUiConformance.cs` + demo 的探针 | **响亮**：`EveryUnreachableSampler_IsCoveredByTheAcceptanceTables`（`:162`）失败，消息是「这些采样器这个套件验不了，而 `VeloxDev.AT` 的表里也没有它们，等于两边都没验」（`:190-191`） |

### 官方做法 vs 看着能编译但错的捷径

- ❌ **不注册，指望 `Transition<T>` 自己按类型找**。没有反射兜底，注册表就是唯一的路（`Interpolator.cs` 的静态构造是唯一注册点）。
- ❌ **注册基类型，却没打算处理这一族的每个子类**。`Interpolator.cs:19` 注册的是**整个 `Projection` 基类**（WPF 那边没有这一条）⇒ 注册表会把**任意** `Projection` 子类都路由到这个采样器上。而 `ProjectionSampler` 实际只认 `PlaneProjection`：`Normalize`（`:64-70`）对不是 `PlaneProjection` 的输入**直接返回一个全新的默认 `PlaneProjection`**（当作「无旋转无位移」的初值），`InsertFrame` 又无条件把 `PlaneProjection` 写回去（`:36`）—— 所以 `Matrix3x4Projection` 这类真正的其它子类不是「也算得对」，是**会被当成单位变换吃掉、再写回一个类型不对的对象**。注册基类型 = 对注册表许下一个你必须自己兑现的承诺；许不起就逐个具体类型注册（另九条都是这么做的）。
- ❌ **把产物是 WinRT `DependencyObject` 的采样器写进闭式解套件**。纯数据进程里 `new SolidColorBrush()` 直接就是 `REGDB_E_CLASSNOTREG`（`WinUiEntries.cs:20-25` 的 remarks 写明了这一点），所以 `BrushSampler` / `ProjectionSampler` / `TransformSampler` 三个**不在** `WinUiEntries` 里，而在 `UnreachableSamplers` + 真 demo 驱动的验收表里。新采样器属于哪一类，先问「它的产物能不能在无 XAML 运行时的进程里构造」。
- ❌ **采样器构造函数的参数约定乱来**。绝大多数采样器是**无参**构造（这是「宿主按名字注册」的前提，`SamplerEntry.cs:38-41` 的 remarks 明说默认走无参构造）；端点烘进每动画构造函数的那些要自己在 `SamplerEntry` 里 `new`。加构造函数前先看有没有同类先例。

---

## 二、加一个主题值转换器

### 步骤

1. 在 `PlatformAdapters/ThemeValueConverters.cs`（或本模块任何文件）里写一个类，**命名空间必须是 `VeloxDev.DynamicTheme`**（该文件 `:11`），实现 `IThemeValueConverter`。现有 7 个：`DoubleConverter` `:13`、`PointConverter` `:34`、`ThicknessConverter` `:64`、`CornerRadiusConverter` `:106`、`ColorConverter` `:143`、`BrushConverter` `:194`、`ObjectConverter` `:231`。
2. **没有第二步。** 转换器**不存在宿主注册调用** —— 消费方在代码里用 `[ThemeConfig<YourConverter, TThemeA, TThemeB>(nameof(Prop), [..], [..])]` 点名（仓内用法样例：`Examples/Theme/Avalonia/Demo/ThemeTile.cs:17-18`），**生成器**把类型名写进生成的代码里，运行期由 `System.Activator.CreateInstance` 激活（`Src/Generators/VeloxDev.Core.Generator/Theme.cs:236`）。⇒ 转换器类只需是**有公开无参构造**的 `class`，与 `VeloxDev.WinUI` 被引没被引无关。

### 官方做法 vs 看着能编译但错的捷径

- ❌ **调 `ThemeCache.RegisterConverter(...)` 注册**。它存在（`Src/Core/VeloxDev.Core/DynamicTheme/ThemeCache.cs:73`，按字符串 key 铸键），但**全仓没有任何调用点**（`grep -rn RegisterConverter Src/ Examples/ --include=*.cs` 只命中它自己那行定义，一个调用者都没有）；那不是这个仓库走的路。走它等于把转换器挂在一个没人会去查的名字上。
- ⚠ **短文名一律解析到本模块这 7 个转换器**：`BrushConverter.Convert` 里 `new ColorConverter()`（`:216`、`:221`）**有意**指向本文件 `:143` 的那一个（把字符串解析成 `Windows.UI.Color` 再包成 `SolidColorBrush`）。所以在这个命名空间里新写转换器时，别指望平台自带的转换器 —— WinUI 侧只有 `ColorHelper` 这类 static helper，没有实现 `IThemeValueConverter` 的类型。**反过来说：WPF 那条「必须全限定平台自带转换器」的坑在这里不存在**（全模块 `grep 'Microsoft.UI.Xaml.Media.BrushConverter'` 零命中），不要照搬。
- ⚠ **`ThemeResourceLookup` 的 `Application.Current` 只有一条路径，而且是守住的**（`ThemeValueConverters.cs:270` 的 `Application.Current?.Resources`，紧跟 `:271-274` 的 `resources is null ⇒ return false`）—— 全文件 `Application.Current` 只出现这一处（模块内另一次在 `ViewManager.cs:262-264`，同样有 `is not null` 守卫）。所以 WPF 的 `ObjectConverter` 那条「两条路径、一条没守、无 `Application` 的进程静默拿到 null」的坑**在这里不存在**，别照抄过来「顺手加固」。真正要记的是它的**返回语义**：找不到资源时返回 `false`（不抛），调用方 `ObjectConverter` 于是给出 null ⇒ 无 `Application` 的进程（单测、控制台探针）症状仍是**静默 null**，不是异常。查找本身是**递归**的：`TryFindInDictionary` 依次看字典自己、`ThemeDictionaries`（`:288`）、`MergedDictionaries`（`:297`），所以写在主题字典或合并字典里的键也找得到 —— 这在 WinUI 上是必须的（浅色/深色值常规就放在 `ThemeDictionaries` 里）。
- ⚠ **本文件的 7 处 `catch` 全部吞掉异常**（`:24`/`:99` 块内 `return null`、`:139`/`:190` 的 `catch { return null; }`、`:58`/`:225`/`:259` 的空 `catch { }`）—— 这是刻意姿态，与 DynamicTheme「异常不逃逸」一致，别顺手「修」成抛异常。
- ⚠ **本模块的转换器在仓内没有任何消费方**（`Examples/Theme/` 下只有 Avalonia 与 WPF 两套）。想在 WinUI 上验证主题切换，得自己起一个宿主并按上面第 2 步写 `[ThemeConfig<>]`。

---

## 三、加一个附着行为 / 改一个角色的挂载

### 步骤

1. **定宿主类型门槛**：项目里现成的四档是 `UserControl`（画布宿主、插槽布局）、`UIElement`（节点拖拽、画布变换值载体）、`Control`（插槽连接）、`Panel`（视图池）。门槛是 `OnIsEnabledChanged` 里的第一句 `if (d is not X …) return;`（如 `WorkflowNodeDragBehavior.cs:59`）。**选宽了会在不相干的元素上挂住处理器，选窄了会静默不工作** —— 两者都不报错。
2. **状态存在私有附着 DP 上**：`private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(XxxState), typeof(本类), new PropertyMetadata(null));`（`WorkflowSurfaceBehavior.cs:72-76`、`WorkflowNodeDragBehavior.cs:39`）。**必须私有**，`Detach` 里 `ClearValue(StateProperty)` 顺带丢状态（`WorkflowSurfaceBehavior.cs:164`）。
3. **`OnIsEnabledChanged` 写成「先 `Detach` 再 `Attach`」**，`Attach` 第一行也调 `Detach`（`WorkflowSurfaceBehavior.cs:133` 与 `:136-138`）⇒ 重复置 `True` 不叠加订阅。
4. **选名字解析机制**（这一步最容易错，见下面那张「两种机制」表）。
5. **在宿主的 `Unloaded` / `Detach` 里退订一切外部订阅**（`UnsubscribeResolvedControls` `:241-259`、`UnhookZoom` `:282-297`），并且**每次重解析都先退订**。
6. **宿主 XAML 用 `using:` 引命名空间**：`xmlns:behaviors="using:VeloxDev.WorkflowSystem.AttachedBehaviors"`（`Examples/Workflow/WinUI Trimmed/Demo/Views/Workflow/TreeView.xaml:6`）。**不要写 `clr-namespace:…;assembly=…`** —— 那是 WPF 的写法，在这里解析不到类型。
7. **同步七家**：附着属性名、`PART_*` 约定、七角色职责表是**七家共用的契约**，改一处要问「另外六家是不是也该改」。契约表在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；面向用户的一侧在 `skills/veloxdev-create-workflow/references/view-layer.md`。

### 名字解析：两种机制，选错就是静默失效

| 你要找的东西 | 该用 | 成员 |
|---|---|---|
| 目标在**当前模板自己的** `x:Name` 域里 | `FindName` | `parentHost.FindName(name)`（`WorkflowSlotLayoutBehavior.cs:341`） |
| 目标在**祖先链**上 | 自己 + 祖先逐个比 `.Name` | `ResolveNamedHost`（`WorkflowSlotLayoutBehavior.cs:428-433`、`WorkflowNodeDragBehavior.cs:176-187`） |

判据：`FindName` 受**名称域**限制（跨 `UserControl` 找不到），`.Name` 遍历**只往上看**（后代、兄弟、别的分支都找不到）。两者都**不抛、不报**。同一个 `NodeView.xaml:7-10` 上就同时用了两种（`SlotNames` 走前者、`CoordinateHostName` 走后者），所以这不是「新旧写法」而是两条并存的既有机制。

### 官方做法 vs 看着能编译但错的捷径

- ❌ **给新行为也用 `FindName` 找祖先**（或反之）。编译过、跑起来是「没有缩放 / 没有小地图 / 锚点不更新」。
- ❌ **把开关的默认值写成 `true`**。**五个**布尔开关（四个 `IsEnabled` + 画布宿主的 `ZoomEnabled`，`WorkflowSurfaceBehavior.cs:70`）的 `PropertyMetadata(false, …)` 是刻意的；默认开会在每个模板实例上挂处理器。
- ❌ **以为名字解析失败会抛**。`FindName` 返回 `null` 就退化成「没这个能力」；改完模板布局后唯一的补救是调 `WorkflowSurfaceBehavior.Refresh(UserControl)`（`:106`）或触发 `Loaded`/`DataContextChanged`。
- ❌ **在 `Refresh` 里加「先退订再重挂」之外的顺序改动**。`ResolveNamedControls` 的退订-重挂是**热路径**：它被 `ScrollViewer.ViewChanged` 每帧调用一次（`architecture.md` §三·2），在那里加锁、加分配、加 `UpdateLayout()` 都要先算成本。
- ❌ **把 `WorkflowCanvasTransformBehavior` 当「画布变换的实现」改**。它只有一个 DP 和一个故意空的回调（`WorkflowCanvasTransformBehavior.cs:25-30`）；真正的写值处在 `WorkflowSurfaceBehavior.ApplyLayout`（`:573-578`），而且 WinUI 的平移走的是 `Canvas.Translation` 合成变换，**不是**那个附着属性（`WorkflowCanvasTransformBehavior.cs:28` 的注释与代码不符，见 `memory/modules/WorkflowSystem/adapters/winui.md` §四·P1）。**照那句注释去模板里补 `RenderTransform` 绑定，会和合成平移叠加成双倍位移。**

---

## 四、改平台级的宿主 / 解释器 / 优先级

这三件事的实参在**同一处**出现三次，改一处必须改另外两处：

| 位置 | 内容 |
|---|---|
| `PlatformAdapters/Transition.cs:17-25` | `Transition<T> : TransitionCore<T, State, TransitionEffect, Interpolator, UIThreadInspector, TransitionInterpreter, DispatcherQueuePriority>` |
| `PlatformAdapters/TransitionScheduler.cs:5-11` | `TransitionScheduler<TTarget> : TransitionSchedulerCore<UIThreadInspector, TransitionInterpreter, DispatcherQueuePriority>` |
| `PlatformAdapters/Interpolator.cs:26-29` | `CreateScheduler` 里的 `effect is ITransitionEffect<DispatcherQueuePriority>` 与 `FindOrCreate` 的类型实参 |

**`Transition<T>` 与 `CreateScheduler` 那两处必须是同一组实参**，否则 `CreateScheduler` 会走到 `:29` 的 `null` 分支 —— 表现是**动画完全不跑且不抛异常**（只是没有调度器）。`effect is ITransitionEffect<TPriority>` 这个判断**只认优先级类型**，传进来一个别家的 `TransitionEffect` 就是这条静默路径。

### 官方做法 vs 看着能编译但错的捷径

- ❌ **「修好」`UIThreadInspector.PostCore` 里的 `Lifetime.SetAlive(accepted)`**（`:56`）。它是**刻意双向**的：队列拒绝说明应用在退出，接纳说明还活着，两个方向都报；Core 侧 remarks（`Src/Core/VeloxDev.Core/Lifetime/IApplicationState.cs:17-21`）点名了「WinUI clears its flag when a single enqueue is refused … has to be able to take it back」。改成只置 false ⇒ **一次瞬时拒绝就让整个进程的动画永久停摆、且什么都不记**。改这里之前先把 `SetAlive` 的消费链读一遍：`TransitionHostBase.cs:12,14`（`Lifetime`/`IsAlive` 两个属性都转发到它）→ `SamplerSet.cs:90`（`CanSetValue`，每次写属性的闸门）。
- ❌ **给 `UIThreadInspector` 加一个「更可靠」的静态初始化**。它已有三层：`QueueFor(target)` 先问 target 自己（`:36-41`）、`EnsureQueue()` 懒捕获（`:25-34`）、`CaptureUIThread()` 让宿主主动交（`:16-21`，非 UI 线程会抛）。**WinUI 侧的仓内调用点是零**（唯一调用在 Blazor demo），因为 `DependencyObject` 目标是常态；加之前先确认你确实有「target 不是 `DependencyObject`」的宿主。
- ❌ **拿 `new TransitionScheduler<T>()` 当调度器**。这个类的 `TTarget` 是个没人用的类型参数、全仓没有构造点（`TransitionScheduler.cs:5-11`）；真正被用的建法只有 `TransitionSchedulerCore<…>.FindOrCreate(target)`（`Interpolator.cs:28`）。手建的那个**不参与 `MutualSchedulers` 归档**，而按 target 找回 run 的三个静态入口 `Exit`/`Pause`/`Seek`（`Src/Core/VeloxDev.Core/TransitionSystem/Transition.cs:30`/`:63`/`:104`，默认 `IncludeMutual = true`）走的正是那张字典（读者是 `CollectSchedulers`，`:217-226`）⇒ 对目标调它们，你手建那个调度器不会被停、不会被定位（`architecture.md` §二·3）。
- ⚠ **`State : StateCore`、`Transition : TransitionCore` 是空壳**，看起来像「忘了实现」而实际就是空的（`PlatformAdapters/State.cs:3`、`Transition.cs:12-15`）。要加状态字段就加在这里，别去 Core 改。

---

## 五、改 csproj / 加 TFM 的联动

| 改这里 | 必须同步 |
|---|---|
| `VeloxDev.WinUI.csproj:3` 的 `TargetFrameworks` | 三个 WinUI demo 各自的 `SetTargetFramework`（`Examples/Workflow/WinUI Trimmed/Demo/Demo.csproj:40`、`Examples/Transition/WinUI/Demo/Demo.csproj:51`、`Examples/Workflow/WinUI/Demo/Demo.csproj:49`）—— 这三处**写死的 TFM 与两个 Workflow demo 的 `TargetFramework`（一个是 net10、一个是 net8）分别对齐**，不锁就会用错框架引用；`Examples/Transition/AUTO TEST/Drivers/WinUIDemoDriver.cs:31-32` 还把 `net8.0-windows10.0.19041.0` 连同 RID 一起写死在启动路径里 |
| `RuntimeIdentifier`（`:6`，默认 `win-x64`）/ `RuntimeIdentifiers`（`:7`） | 产物路径里会多一层 `win-x64/`；`WinUIDemoDriver.cs:31-32` 的启动路径是**整条硬编码**的字符串（含配置、TFM、RID 三段），改其中任何一段都会让它指向一个不存在的 exe ⇒ 表现为 AutoTest 直接找不到进程 |
| `PackageReference Microsoft.WindowsAppSDK`（`:26`，现 1.6.241114003） | **demo 侧是 1.8.x**，两个版本号同进程并存是现状；升其中一侧前先确认另一侧是否要跟 |
| `VeloxDev.Core` 的 Debug/Release 双轨（`:27-28`） | 与生成器那套同形，**两条必须互斥**；Release 走的是包 `Version 9.0.0` |
| 采样器的引用（`Examples/Transition/AUTO TEST/Samplers/VeloxDev.SamplerTest.csproj:29`） | 摘掉会让 `EveryAdapterAssembly_IsLoadedSoItsSamplersAreCounted` 失败（设计如此，消息直接指过来） |
| 想让新类型出现在模板里 | `Src/Templates/VeloxDev.WinUI.Templates/` 是**纯内容包**，改它不产生任何依赖；面向用户的一侧在 `skills/veloxdev-create-workflow/references/gui/winui.md` |

**加 TFM 之前先问一句**：本模块唯一一处条件编译是 `PlatformAdapters/Transition.cs:187-212` 的 `#if !NETSTANDARD2_0`（4 个 `System.Numerics` 重载），而现有两个 TFM 都不带 netstandard ⇒ **它恒为真，是死守卫**。加一个 `netstandard2.0` 会让那 4 个重载真的消失（这正是 Avalonia 那条同款守卫存在的意义）—— 这是改 TFM 时**唯一**一处会被动的代码，其余全是 MSBuild 层的事。

---

## 六、本模块专属的几条禁令

1. **不要为了「对齐别家」把合成平移换回 `RenderTransform` 绑定。** 那会与 `Canvas.Translation`（`WorkflowSurfaceBehavior.cs:573-576`）叠加成双倍位移，而且槽位锚点必须跟着从 `SlotAnchorFromCanvasLocal` 改回 `SlotAnchorFromNode`（`WorkflowSlotLayoutBehavior.cs:390-410` 的分支就是为这件事存在的）。
2. **不要给 `ViewManager.ApplyLayout` 的连线分支补写 `Canvas.SetLeft/SetTop`**（`ViewManager.cs:342-348` 的空 `break`）。它是 offset frame 的前提，注释已说明理由。
3. **不要把 `Refresh` 从 `OnViewChanged` 里摘掉来「省性能」**。它是这一家在「模板换树后重新解析」上的唯一自动路径（名称域只有 `FindName` 一条路）；摘掉之后一切依赖宿主显式调。要优化就在 `ResolveNamedControls`/`UnsubscribeResolvedControls` 内部省，别动调用关系。
4. **不要引用 `Docs/`**（不在本仓库），**也不要把 `docs` 站点的措辞当依据** —— 依据只能是 `Src/` 与 `Examples/` 里的代码。
5. **改这一家时先确认参考实现是哪一个**：工作流是 `Examples/Workflow/WinUI Trimmed/`（**整目录都可照抄** —— code-behind 只有 `InitializeComponent()`；2026-09-20 前那份 `TreeView.xaml.cs` 挂着写入 `%TEMP%` 的遗留诊断探针，已删）；非 Trimmed 的 `Examples/Workflow/WinUI/` 停在 offset frame 之前的老做法。
