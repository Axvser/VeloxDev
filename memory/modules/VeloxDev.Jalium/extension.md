# VeloxDev.Jalium — 扩展

> 读法：七角色契约、附着属性名、注册位置在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；
> 两条轴上「这家和别家不一样」的地方在 `memory/modules/{TransitionSystem,WorkflowSystem,Templates}/adapters/jalium.md`。
> 本文**只回答一件事**：在这个项目里加一个 X，动哪几处、按什么顺序、哪条看着能编译的捷径是错的。
> 目录与轴的对照、登记表所在、csproj 的取值、陷阱清单见 `architecture.md`，本文不抄。

---

## 一、扩展点地图

| 我想加 | 官方挂点（具体成员） | 位置 |
|---|---|---|
| 让一个 Jalium 类型可动画 | 实现 `ISampler`，再 `RegisterInterpolator(typeof(T), new XSampler())` | `PlatformAdapters/Interpolator.cs:14-23`（唯一的登记表，10 行） |
| 换线程句柄 / 优先级 / 解释器 | 改 `TransitionSchedulerCore<UIThreadInspector, TransitionInterpreter, DispatcherPriority>` 的类型实参 | `PlatformAdapters/TransitionScheduler.cs:5-8` |
| 一条过渡时长预设 | `TransitionEffects` 的三个静态属性（可 `set`，进程级） | `PlatformAdapters/TransitionEffects.cs:5` / `:9` / `:13` |
| 让 `.Property(...)` 能顺滑写某个 Jalium 类型 | 加一个 `Property(Expression<Func<T, TXxx>>, TXxx, object?)` 重载 | `PlatformAdapters/Transition.cs:34-137`（**这一家 13 个**） |
| 一个新的画布操作面 | `DependencyProperty.RegisterAttached("IsEnabled", …)` + 一个私有附着 `State` DP | `Attached/Workflow/` |
| 换七角色里某一个的实现 | 同名文件 | `Attached/Workflow/` |
| 换「item 类型 → 视图」的工厂 | 实现 `IWorkflowTemplateSelector.CreateView(object item)` | `Attached/Workflow/IWorkflowTemplateSelector.cs:7`（11 行，`DataTemplateSelector` 的等价物） |
| 小地图换皮 | 继承 `WorkflowMinimapOverlay`（`public class`，可继承） | `Attached/Workflow/WorkflowMinimapOverlay.cs:15` |
| 换网格装饰器 | 给 `WorkflowTreeView.GridDecorator` 设一个 `FrameworkElement` 实例 | `Attached/Workflow/WorkflowTreeView.cs:33-42` |
| 让 DynamicTheme 在这一家真动画 | 覆写 `InterpolatorCore.CreateScheduler` —— **已实现但本仓库无人调用** | `PlatformAdapters/Interpolator.cs:26-29`；理由见 `architecture.md` §一 |

**这里没有的扩展点（别去找）：**

- **没有程序集级入口，也没有 `Initialize()`**：全模块 `ModuleInitializer` / `[assembly:` 零命中，唯一的静态构造是 `Interpolator.cs:10`。所以「注册」这件事没有统一挂点 —— 要么落在那行静态构造里，要么由宿主自己调。
- **没有 `ThemeValueConverters.cs`，也没有主题 demo**：这一条轴在这家是空的（`architecture.md` §一）。
- **没有标记语言**：宿主无法用 XAML 挂附着属性，也没有 XAML 名字作用域可借 —— 名字必须由代码 `NameScope.SetNameScope` + `RegisterName` 造出来（§三·B）。
- **没有 `adapters/` 子目录**：模块名本身就是平台。
- **没有「注册一个 ViewModel 类型」的地方**：适配器不 `new` Tree/Node，只消费宿主给的模型。

---

## 二、官方做法 vs 看着能编译、但错的捷径

**这一节是全文最重要的部分。** 下面每一条走捷径都能编译通过，其中几条跑起来还像是好的。

| # | 错的捷径 | 为什么错 | 官方做法 | 依据 |
|---|---|---|---|---|
| 1 | 在 `Samplers/` 加一个 `ISampler` 类就以为生效了 | 登记表是唯一开关；没登记 = 该采样器**永不运行**，且库里没有任何东西会报错 | 同时在 `Interpolator` 的静态构造里加一行 | `PlatformAdapters/Interpolator.cs:14-23` |
| 2 | 登记了但忘了改测试表 | 不会编译错，运行到 `EveryShippedSampler_IsAccountedFor` 才红：它反射所有 `VeloxDev.*` 程序集里的 `ISampler`，再与 `SamplerRegistry.Entries` ∪ `UnreachableSamplers.All` 求差 | `Examples/Transition/AUTO TEST/Samplers/JaliumEntries.cs` 加一条 `Entry(...)`（`All` 在 `:183-257`，`Adapter` 常量在 `:34`） | `Samplers/SamplerCoverageTests.cs:65-87`；`SamplerRegistry.cs:18-22` → `AdapterSamplerEntries.cs:15` → `JaliumEntries.All` |
| 3 | 改采样器类名只改文件 | 测试按**字符串**反射取类型：`Type.GetType($"VeloxDev.Adapters.NativeSamplers.{name}, VeloxDev.Jalium", throwOnError: true)`。编译不报错，**测试运行时才炸** | 改名要同步 `JaliumEntries.cs` 里那 9 个字符串 | `JaliumEntries.cs:38-39`（`throwOnError: true`） |
| 4 | 把登记代码写在采样器自己的文件里 / `App` 里 / `Main` 里 | 没人保证它会跑在第一个 `new Transition<T>()` 之前；`RegisterInterpolator` 是**末位胜出**，谁先谁后直接改解析结果 | 只在静态构造里集中登记 | Core `Transition.cs` 的字段初始化器 `protected TInterpolatorCore interpolator = new();`；`Src/Core/VeloxDev.Core.Test/TransitionSystem/InterpolatorCoreTests.cs` 的 `RegisterInterpolator_OverwritesExisting` |
| 5 | 照 WPF 的样子「登记基类型接住整族」，于是给 `TranslateTransform` / `RotateTransform` / `TransformGroup` 各登记一条 | 这家的姿态是**只登记基类型**（`typeof(Transform)`）＋ 精确类型的少数几条；多余登记会被精确命中抢先，让本该走基类回溯的路径换成另一条实例 | 只登记 `Transform` 与 `Media3D.Transform3D` 两条 | `PlatformAdapters/Interpolator.cs:22-23` |
| 6 | 想在宿主里用 XAML 写 `behaviors:WorkflowSurfaceBehavior.IsEnabled="True"` | 这一家**没有标记语言**，没有这条路 | 代码里 `SetIsEnabled(host, true)`，且**先 `NameScope.SetNameScope` 再 `RegisterName` 再 `SetIsEnabled`**（顺序反了名字解析全 null） | `Attached/Workflow/WorkflowTreeView.cs:61` → `:88-91` → `:93-98`（唯一可读的样本） |
| 7 | 新附着行为放进别的命名空间（哪怕更合理的名字） | 七家适配器的行为都在同一个命名空间 `VeloxDev.WorkflowSystem.AttachedBehaviors`，且**七个程序集里各声明一份** ⇒ 同时引用两家必然歧义；换命名空间则「跨适配器移植」这件事不再成立 | 命名空间固定为 `VeloxDev.WorkflowSystem.AttachedBehaviors` | 该命名空间在 `Attached/Workflow/` 的每个行为文件里各声明一次；跨家重名的代价见 `architecture.md` §一 |
| 8 | 只调 `WorkflowSurfaceBehavior.SetZoomEnabled(host, true)` 就以为滚轮缩放开了 | `HookZoom` 第一行是 `if (control.GetValue(StateProperty) is not SurfaceState state) return;`，而 `StateProperty` **只在 `Attach` 里写** —— 没开 `IsEnabled` 时这句是彻底空操作 | 先 `SetIsEnabled(host, true)`（`ResolveNamedControls` 结尾会顺带 `HookZoom`，所以两者次序不限，但两个都得设） | `Attached/Workflow/WorkflowSurfaceBehavior.cs:281-284`、`:231-234`；**demo 里那句 `SetZoomEnabled` 就是这条捷径的活样本**（`Examples/Workflow/Jalium Trimmed/Demo/MainWindow.cs:60`） |
| 9 | 给 `WorkflowTreeView` 先 `ViewModel` 再 `TemplateSelector` | `TemplateSelector` 是**普通自动属性**（不是 DP、没有回调），唯一转发点是 `OnDataContextChanged` 里的条件赋值；而 `ViewManager.AddItem` 在 `_selector is null` 时**静默返回** | 先 `TemplateSelector` 再 `ViewModel` | `Attached/Workflow/WorkflowTreeView.cs:23` 与 `:150-153`；`ViewManager.cs:131` |
| 10 | 换 `GridDecorator` 时只把新元素塞进视觉树 | 名字仍指向旧实例，`ResolveNamedControls` 会把 inset 喂给一个已经不在树上的元素，**不报错** | 走 `WorkflowTreeView.GridDecorator` setter（它内部 `UnregisterName` + `RegisterName`） | `Attached/Workflow/WorkflowTreeView.cs:103-120`（重注册在 `:118-119`） |
| 11 | 只设 `ViewPool.ItemsSource`（或只设 `TemplateSelector`）就以为视图会出来 | 两个 DP 共用 `OnChanged`，只有**两者都非空**才建 manager；否则走 `else` 分支把 manager `Detach()` | 两个一起给 | `Attached/Workflow/ViewPool.cs:19`/`:25`/`:58-73` |
| 12 | 用静态 `Dictionary<element, state>` 存每元素状态 | 会漏 `Detach` 时的清理，多个同类型元素之间还会串 | 私有附着 DP `"State"`（`WorkflowSurfaceBehavior.cs:73`、`WorkflowNodeDragBehavior.cs:38-42`、`WorkflowSlotLayoutBehavior.cs`） | 同左 |
| 13 | 以为重复置 `IsEnabled=true` 会叠加订阅 | 每个 `OnIsEnabledChanged` 都**先 `Detach` 再 `Attach`**，并顺带丢状态 | 直接依赖这个幂等性 | `WorkflowSurfaceBehavior.cs:115-130` 与 `:146` 的 `Detach` |
| 14 | 把 `WorkflowSurfaceBehavior.Refresh(host)` 当「总是会重算」用 | 第一行就被 `IsEnabled` 挡掉：没打开开关时它**什么也不做、不抛** | 先确认开关已开，再调 | `WorkflowSurfaceBehavior.cs:101-113`（判断在 `:103`） |
| 15 | 给 `WorkflowCanvasTransformBehavior` 的变更回调补逻辑 | 它是**刻意的空回调**；而且它的镜像方 `ViewPool.UpdateRenderTransforms` 全仓零调用者 ⇒ 这条通道在本仓库两头都没接上 | 改「画布怎么变换」去改写值处（`WorkflowSurfaceBehavior` 的布局段），或者干脆别动 | `Attached/Workflow/WorkflowCanvasTransformBehavior.cs:26-30`；`ViewPool.cs:39-46` |
| 16 | 把 `Property(…, ICollection<Transform>)` 的「单个直接赋值」分支改成统一 `TransformGroup` | 会改运行时类型，破坏 `((TranslateTransform)x.RenderTransform).X` 这类嵌套路径 | 保持 `Count == 1` 直赋、其余包组 | `PlatformAdapters/Transition.cs:48-66`（注释 `:51-53`） |
| 17 | 把 `.Property(...)` 的某个重载「补进 Core」 | Core 的 `TransitionCore<...>` 里**一个 `Property` 都没有**，13/28/30/17 个重载分别写在各家适配器里 —— 补进 Core 等于改七家的形状 | 重载留在本家 `PlatformAdapters/Transition.cs` | `Src/Core/VeloxDev.Core/TransitionSystem/Transition.cs` 全文无 `Property` 方法；四家的条数见 `architecture.md` §2.3 |
| 18 | 让 `Attached/` 里的行为直接驱动一个 `Transition<T>`（或反过来） | 两条轴在程序集内互不引用是既成事实；跨一条就再也拆不开 | 想跨轴就在宿主侧接线 | `architecture.md` §一 |
| 19 | 按别家的条数猜这一家有几种采样器 | 条数本来就不等（Jalium 9；WPF 12、Avalonia 14、WinUI 10、WinForms 1、Razor 1 —— 见 `memory/modules/TransitionSystem/adapters/jalium.md`） | 按 `Interpolator.cs:14-23` 现读 | 同左 |

---

## 三、步骤清单

### A. 让一个新的 Jalium 类型可动画（加一个采样器）—— 最常见的路径

1. **建文件** `PlatformAdapters/Samplers/XxxSampler.cs`，命名空间 **`VeloxDev.Adapters.NativeSamplers`**（七家共用这个名字，撞名时这里是唯一能编得过去的位置），实现 `ISampler`。端点约定：`t == 0` / `t == 1` 原样交出调用方给的起点/终点实例（范式见 `PlatformAdapters/Samplers/TransformSampler.cs:22-23`，它还带一条「草稿实例类型与起点不同就重建」的守卫）。
2. **登记**：`PlatformAdapters/Interpolator.cs:14-23` 加一行 `RegisterInterpolator(typeof(X), new XxxSampler());`。要登记的是**基类型**还是**精确类型**，照 §二·5 的姿态选。
3. **补验证表**（**最容易漏的一步**）：`Examples/Transition/AUTO TEST/Samplers/JaliumEntries.cs` 加 ① 私有 `Target` 类上的一条属性（`:42-53`），② 一条 `Entry("XxxSampler", SamplerRule.…, start, end, x => x.属性, t => 闭式解)`，放进 `All`（`:183-257`）。漏了不会在库里报错，但 `EveryShippedSampler_IsAccountedFor` 会红。
4. **可选**：给 `.Property(...)` 加一个对应重载（§三·D），否则调用方只能走泛型 `Property<TValue>`。
5. **同步 `memory/modules/TransitionSystem/adapters/jalium.md`** 的条数与清单。

> 判断「我登记对了没」：`RegisterInterpolator` 返回什么也不告诉你，**没有「列出全部登记项」的公开 API**。最快的自查是跑一次 `Examples/Transition/Jalium/Demo/`。

### B. 加一个附着行为，或把七角色之一换成自己的

1. **建文件** `Attached/Workflow/<Name>Behavior.cs`，`namespace VeloxDev.WorkflowSystem.AttachedBehaviors`，`sealed class : DependencyObject`（无状态的挂点才用 `static class`）。
2. **开关**：`IsEnabledProperty = DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(X), new PropertyMetadata(false, OnIsEnabledChanged))` —— 默认值必须是 `false`（四个既有开关都是），回调里判宿主类型后 `Attach` / `Detach`。
3. **每元素状态**放私有附着 DP `"State"`（`state` 类型是个私有类）。
4. **订阅**一律在 `Attach` 开头先 `Detach` 一次（幂等）。
5. **命名部件**：因为没有 XAML 名字作用域，宿主那边必须 `NameScope.SetNameScope` + `RegisterName`；你自己的解析一律 `host.FindName(name) as T`，null 就是**静默早退**，不要指望有异常。
6. **宿主类型门槛**照别家同一套（要 `FindName` → `FrameworkElement`；只要 `DataContext` → `UIElement`/`Control`），这一家**没有 `UserControl` 这一档的额外约束**（那档在 XAML 六家里是「XAML 名字作用域挂在 UserControl 上」的产物）。
7. **驱动重算**：调 `WorkflowSurfaceBehavior.Refresh(host)` —— 但它被 `IsEnabled` 挡着（§二·14）。

### C. 让宿主接上适配器 —— **两条路，别混**

| | 路 1：用适配器成品表面 | 路 2：自己写表面（模板/demo 那条） |
|---|---|---|
| 拿什么 | `new WorkflowTreeView { TemplateSelector = … }`，然后 `ViewModel = tree` | 自己的 `Canvas` 子类，手写装配 |
| 装配量 | ctor 里全齐（`WorkflowTreeView.cs:61-98`） | **8 步手写**：`new` → 放进 `ScrollViewer(PanningMode=None)` → `AttachScrollViewer`（**必须在 `SetTree` 之前**）→ `SetTree` → `DataContext = tree` → `SetZoomEnabled` → 订阅三个事件喂小地图 → 自己接缩放 + `NotifyZoomCommitted` |
| 缩放 | 靠 `SetZoomEnabled`（**必须先 `SetIsEnabled`**，见 §二·8） | 自己算，深缩放要维护 `_zoomPin` |
| 标尺带 | 28（适配器默认） | 36（模板常量） |
| 画布下界 | 无（`ActualSize` 直接当尺寸） | `Math.Max(2000, …)` |
| 谁在用 | **仓库里无人**（`architecture.md` §五） | `Examples/Workflow/Jalium Trimmed/Demo/` 与模板产物 |

⇒ **改动前先确认你站在哪条路上**：两条路的「同一个名字」量的不是同一个数（标尺 28/36 最典型）。把路 1 的 `WorkflowTreeView` 当成「另一个名字的 TreeView」去改，会踩 §二·9/§二·10 两条静默坑。

### D. 给 `.Property(...)` 加一个重载

1. `PlatformAdapters/Transition.cs` 内，形状一律是：`public Transition<T> Property(Expression<Func<T, TXxx>> propertyLambda, TXxx newValue, object? interpolationOptions = null)`，体三行 —— `state.SetValue(...)` → 可选 `state.SetOptions(...)` → `return this;`。
2. 新重载**必须**与 `Interpolator` 的登记项对应；不对应的重载是死代码（能编译、能用，但那个类型的动画走的是泛型 `Property<TValue>` 或直接不生效）。
3. **不要**把重载搬到 Core：见 §二·17。

### E. 改 TFM / 引用方式

1. `VeloxDev.Jalium.csproj:7` 是**单 TFM、不带平台后缀**的 `net10.0`，`:4-6` 的注释写明了理由（只用跨平台核心，不用 `Jalium.UI.Desktop`）。改成 `net10.0-windows` 会让这个包**不再能服务 Linux / Android**，那是比「多一个 TFM」大得多的改动。
2. `:27` / `:28` 是一对**互斥**的双轨（Debug `ProjectReference` / 非 Debug `PackageReference`），改一条要同时看另一条 —— 两者同时生效会报重复成员。
3. `Jalium.UI.Controls`（适配器，`:32`）与 `Jalium.UI.Desktop`（消费 demo）是**两个包、两个版本位**；升其中一个不必同步另一个，但 `Jalium.UI.Controls` 是「能编过 `Canvas`/`DrawingContext` 的最低包」，降级会缺绘制面。

---

## 四、联动清单（加一个 X 时必须同步修改的所有位置）

**漏一处通常不报错，只是静默少一个能力。**

### 4.1 加一个平台采样器

- [ ] `Src/Adapters/VeloxDev.Jalium/PlatformAdapters/Samplers/XxxSampler.cs`（命名空间 `VeloxDev.Adapters.NativeSamplers`）
- [ ] `Src/Adapters/VeloxDev.Jalium/PlatformAdapters/Interpolator.cs:14-23` 登记（**不登记 = 永不运行**）
- [ ] `Examples/Transition/AUTO TEST/Samplers/JaliumEntries.cs`：`Target` 属性 + 一条 `Entry`（**漏了直接红**）
- [ ] 要进流的 `.Property(...)` 重载：`Src/Adapters/VeloxDev.Jalium/PlatformAdapters/Transition.cs`
- [ ] 与 Core / 别家撞名时，命名空间留在 `VeloxDev.Adapters.NativeSamplers`（同时引用两家的工程必然 CS0433，绕法是 `JaliumEntries.cs:38-39` 那条按程序集限定名的反射）
- [ ] `memory/modules/TransitionSystem/adapters/jalium.md`（条数与清单）

> **这一家没有 `UnreachableSamplers` 条目**（`UnreachableSamplers.cs` 的 6 条全是 WinUI / MAUI），所以**每一个** Jalium 采样器都必须在 `JaliumEntries.All` 里被闭式解验 —— 没有「先挂个理由放着」这条退路。这条退路本身也是可证伪的：`EveryUnreachableSampler_NeedsAValueThatCannotBeBuiltHere` 会真的去构造那个端点值（`SamplerCoverageTests.cs:89-100`）。

### 4.2 加 / 换一个工作流视图角色，或加一个附着行为

- [ ] `Src/Adapters/VeloxDev.Jalium/Attached/Workflow/` 的文件（命名空间固定 `VeloxDev.WorkflowSystem.AttachedBehaviors`）
- [ ] 若新行为要有命名部件：**宿主**（`WorkflowTreeView` 或模板产物）那边加 `NameScope` + `RegisterName` + 附着属性；`WorkflowTreeView.cs:61-98` 是模板
- [ ] `Src/Templates/VeloxDev.Jalium.Templates/working/content/` 下 7 个条目里对应的那个（`workflow-tree-view` / `-node-view` / `-slot-view` / `-link-view` / `-grid-decorator` / `-minimap-overlay` / `-template-selector`）—— 注意**这一家三个条目产出的是 `static class`**，形状差异见 `memory/modules/Templates/adapters/jalium.md` §一
- [ ] 两套 demo 都要过：`Examples/Workflow/Jalium/Demo/`（自己写的 `NodeEditorSurface`）与 `Examples/Workflow/Jalium Trimmed/Demo/`（8 步装配那条）
- [ ] `skills/veloxdev-create-workflow/references/gui/jalium.md`
- [ ] `memory/modules/WorkflowSystem/adapters/jalium.md`
- [ ] `memory/modules/Templates/adapters/jalium.md`（模板侧形状变了才动）
- [ ] `VeloxDev.slnx` **只在新增项目时**才动（适配器本身早就在里面）

### 4.3 动 `WorkflowTreeView` 的 `PART_*` 契约

- [ ] `WorkflowTreeView.cs` 的 `RegisterName` 块（`:88-91`）与对应属性
- [ ] 若换的是 `PART_GridDecorator`：**必须**走 `SwapGridDecorator`（含 `UnregisterName` + `RegisterName`）
- [ ] 若换的是 `PART_MinimapOverlay`：`AddMinimap` 会在 `overlay is WorkflowMinimapOverlay` 时替你赋 `ScrollViewer`（`WorkflowTreeView.cs:131-134`）—— 换成纯 `IWorkflowMinimapOverlay` 就得自己赋
- [ ] 七角色契约里 `PART_*` 的名字约定在 `memory/modules/WorkflowSystem/extension.md` §3.9，改名要连同别家一起看

---

## 五、几个「以为能改、其实不该改」的地方

1. **`TransitionEffects` 的三个时长**（`PlatformAdapters/TransitionEffects.cs:5/9/13`：`Empty` 0s、`Theme` 0.46s、`Hover` 0.32s）**六家逐字相同** —— 改这里等于改所有平台的默认观感，不是 Jalium 一家的事。
2. **`Interpolator.cs:21` 那条看似冗余的 `SolidColorBrush` 登记不要顺手删。** 它确实被 `typeof(Brush)`（`:20`）的基类回溯覆盖，删了多半不报错也不改行为 —— 但它防的是「属性声明成 `SolidColorBrush`」这一类，代价为零。**要删就先跑一遍 `Examples/Transition/Jalium/Demo/`。**
3. **`WorkflowSurfaceBehavior.ZoomBy` 尾部的 `Debug.WriteLine`**（`:401`）是排查缩放问题的现成探针，交付前知道它还在就行，别当成噪音删掉又去重加。
4. **`WorkflowSlotLayoutBehavior` 的类注释与代码不符**（注释说减 `Layout.ActualOffset`，代码里 `SyncSlot` 的两个形参根本没被读，`GetActualOffset` 零调用者）—— 改这里**以代码为准**，别照着注释「补回」那段逻辑。
5. **`IsSurfaceBlankInteraction` 的祖先链里没有连线判定**（`WorkflowSurfaceBehavior.cs:634-660`）—— 落在连线上的按下会被当成空白交互。是刻意还是漏，**树里判不出来（存疑）**；要动它先想清楚「点在线上」本来该干什么。
6. **`_zoomPin` / `NotifyZoomCommitted` 不要往适配器里搬。** 这套守卫的成立前提是「表面自己调 `Virtualize`」；`WorkflowSurfaceBehavior` 不调（`ZoomBy` 里没有虚拟化调用），搬进去就是给一个不存在的时序做守卫。要的是「缩放后立刻重算可见集」，那是另一件事（对照 `Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:350-399` 的做法）。
7. **`WorkflowTreeView.UpdateCanvasSize` 的「没有下界」**（`:222-223`）不要照模板的 `Math.Max(2000, …)` 直接补 —— 模板那个 2000 是它的 `CanvasWidth/CanvasHeight` 下界，与适配器这条路的滚动内容语义不同。要补就补一个与宿主视口相关的下界。
8. **七家的采样器类名不要「统一化」**（如 `PointSampler` → `JaliumPointSampler`）：命名空间的跨家重名是既成事实，改名只会让两处字符串表（`JaliumEntries.cs:38-39`、以及别家同名反射）同时错位。
