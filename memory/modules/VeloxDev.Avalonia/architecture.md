# VeloxDev.Avalonia — 架构

> 代码：`Src/Adapters/VeloxDev.Avalonia/`，分四块：`PlatformAdapters/` 根、`PlatformAdapters/Samplers/`、`Attached/Workflow/`、`GlobalUsings.cs`。
> 数法：`git ls-files 'Src/Adapters/VeloxDev.Avalonia/*.cs'` 能数到全部 `.cs`（git 的 pathspec 通配符**跨 `/` 匹配**，与 shell 不同）；而 `git ls-files 'Src/Adapters/VeloxDev.Avalonia/**/*.cs'` 会少 —— `**/` **不匹配顶层文件**，七家每家都正好漏掉顶层那个 `GlobalUsings.cs`。核验用 `git ls-files Src/Adapters/VeloxDev.Avalonia | grep -c '\.cs$'`。
> 最大几个文件：`WorkflowSurfaceBehavior.cs`、`WorkflowMinimapOverlay.cs`、`WorkflowSlotLayoutBehavior.cs`、`ViewManager.cs`、`Samplers/TransformSampler.cs`、`ThemeValueConverters.cs`、`Transition.cs`。
> **三条 GUI 轴的平台差异不在这里**：`memory/modules/{TransitionSystem,WorkflowSystem,Templates}/adapters/avalonia.md`；七角色契约与附着属性注册位置在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；「怎么用这套 API」在 `skills/`。本文只写这个项目独有的：四块怎么分、注册与工厂在哪、三条轴各自怎么被宿主接上、程序集级约束、csproj 与 TFM。

---

## 一、一个程序集，三条互不相干的轴

| 目录 | 面向的 Core 模块 | 消费方怎么接入 | 平台差异细节在哪 |
|---|---|---|---|
| `PlatformAdapters/` 根 | TransitionSystem | `Transition<T>.Create()`（编译期闭泛型，**零引导**） | `TransitionSystem/adapters/avalonia.md` |
| `PlatformAdapters/Samplers/` | TransitionSystem（采样器实现） | 同上 —— 注册是构造 `Interpolator` 的副作用，见 §二 | 同上 §二.7、§三 |
| `PlatformAdapters/ThemeValueConverters.cs` | DynamicTheme | `[ThemeConfig<XxxConverter, …>]` 的类型参数 + 一次性 `ThemeManager.SetPlatformInterpolator(new Interpolator())` | 本文 §三.2 |
| `Attached/Workflow/` | WorkflowSystem | XAML 附着属性 + `PART_*` 名字解析 | `WorkflowSystem/adapters/avalonia.md` |

**三块之间零交叉引用**，可复核：`grep -rn "using VeloxDev.WorkflowSystem" PlatformAdapters/` 与 `grep -rn "using VeloxDev.TransitionSystem" Attached/` 都为空。每块各面向一个 Core 模块 —— `ThemeValueConverters.cs` 连一条 `using VeloxDev.*` 都没有（它 `namespace VeloxDev.DynamicTheme` 与 `IThemeValueConverter` 同命名空间，工具全是 Avalonia 类型）。

**不解决什么：**

- **网格装饰器这家不自带**：由消费方实现（demo 是 `Examples/Workflow/Avalonia Trimmed/Demo/Demo/Views/Workflow/WorkflowGridDecorator.cs`，`Panel, IWorkflowGridDecorator`），与 WPF 同形。这家自带的只有小地图（`WorkflowMinimapOverlay`，且刻意留成可继承改配色的基类，demo 就是这么用的）。
- **生成器侧**（`[ThemeConfig]`/`[VeloxProperty]` 生成什么）与平台无关，见 `VeloxDev.Core.Generator/architecture.md`。
- 平台差异的「为什么」不在本文，在两篇 `adapters/avalonia.md`。

---

## 二、注册与工厂：`Interpolator` 是 Core 泛型点名本家的地方

- **注册表在 Core**：静态 `ConcurrentDictionary`（`Src/Core/VeloxDev.Core/TransitionSystem/Sampling/Interpolator.cs` 的 `Interpolators`），基类静态 ctor 先注册一组来自 `System.Drawing` / `System.Numerics` 的采样器。
- **这家补一组**（`Interpolator.cs` 的静态构造函数），注册的是 Avalonia 的类型，其中两条注册的是**接口** `IBrush` / `ITransform`。
- **静态 ctor 什么时候跑**：Core 的泛型节点里写着 `protected TInterpolatorCore interpolator = new();`（`Src/Core/VeloxDev.Core/TransitionSystem/Effects/Transition.cs`），约束 `where TInterpolatorCore : InterpolatorCore, new()` 在 `TransitionCore<…>` 的类型参数表 ⇒ **每构造一个 transition 节点就 `new` 一个这家的 `Interpolator`**，第一次构造即触发全部注册。所以纯动画路径**不需要任何引导代码**，而纯主题路径恰好不构造 `Transition<T>`，这就是它必须补一条 `SetPlatformInterpolator`（§三.2）的原因。
- 同一处约束串解释了这家另外几个类的形状：`THost : ITransitionHost<TPriorityCore>, new()`（`UIThreadInspector`）、`TTransitionInterpreterCore : …, new()`（`TransitionInterpreter`）、`TStateCore : IFrameState, new()`（`State`）、`TEffectCore : ITransitionEffect<TPriorityCore>, new()`（`TransitionEffect`）。**这些空壳/薄壳不是「这家有差异」，是泛型闭合点** —— `State.cs` 与 Jalium 的 `State.cs` 内容等价，都只有一句类声明。它们唯一的硬要求是**公开可构造**。
- **工厂只有一个方法**：`CreateScheduler`（`Interpolator.cs` 的 `CreateScheduler`），按 `effect is ITransitionEffect<DispatcherPriority>` 决定给不给 scheduler；基类默认 `null`（`Src/Core/VeloxDev.Core/TransitionSystem/Sampling/Interpolator.cs` 的 `CreateScheduler`）。
  - **全仓唯一调用者是主题系统**（`Src/Core/VeloxDev.Core/DynamicTheme/ThemeManager.cs` 的 `SetPlatformInterpolator`/`CreateScheduler` 调用处），不是普通动画路径（普通路径走 `TransitionSchedulerCore<…>.FindOrCreate` 的按目标缓存，`Src/Core/VeloxDev.Core/TransitionSystem/Runtime/TransitionScheduler.cs` 的 `FindOrCreate`）。
  - 后果是**全有或全无**：只要有一个目标拿不到 scheduler，整批主题切换退化为瞬时（`ThemeManager.cs` 里 `_interpolator is null` 与 `scheduler is null` 两条分支）。
- 第二个公开入口 `TransitionScheduler<TTarget>`（`TransitionScheduler.cs` 的类声明）在仓库内**没有任何调用者**，是给消费方的显式泛型壳（WinUI 也定义了同名同形的一个，同样无人调用）。

---

## 三、三条轴各自「怎么被宿主接上」

1. **Transition：零引导，家在编译期选定。** 消费方写的是这家的 `Transition<T>`（`Transition.cs` 的 `Transition<T>` 基类列表），它把 Core 的七参泛型一次闭合成本家的五件套；用法形状是 `private static readonly Transition<Rectangle> X = Transition<Rectangle>.Create()…`（`Examples/Transition/Avalonia/Demo/Views/MainWindow.axaml.cs` 里的 `Transition<Rectangle>` 字段）。⇒ 「用哪一家」= 引用哪个适配器程序集，没有运行时注册。
   - 控制面（`Transition.Exit` / 暂停等）在 Core 的非泛型 `TransitionCore`（`Src/Core/VeloxDev.Core/TransitionSystem/Effects/Transition.cs`），与平台无关；这家的非泛型 `Transition`（`Transition.cs` 的 `Transition` 类）是空壳，只是把同名 API 放进本家程序集。
   - `TransitionEffects.Empty/Theme/Hover`（`TransitionEffects.cs` 的那组属性）是这家三档预设，**七家同形**（与 WPF 同名文件逐字相同），不是本家设计。
2. **Theme：必须一次性引导，且不引导不报错。** 消费方在启动处调 `ThemeManager.SetPlatformInterpolator(new Interpolator())`（demo：`Examples/Theme/Avalonia/Demo/App.axaml.cs`）。两个作用写在 Core 的 `SetPlatformInterpolator` remarks 里（`ThemeManager.cs`）：把本家的采样器注册跑起来 + 给 Core 一个它自己点不出名字的 scheduler（Core 原话：which inspector, interpreter and dispatcher priority to animate with is the one thing Core cannot name）。没有它主题照样切换、只是**瞬切无动画**（`ThemeManager.cs` 里 `_interpolator is null` 的分支）—— 最容易误判成「动画坏了」的一处。属性侧是特性的类型参数：`[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["#ffffff"])]`（`Examples/Theme/Avalonia/Demo/ThemeTile.cs` 的两个 `[ThemeConfig<...>]`）。
3. **Workflow：XAML 附着属性 + 名字解析，无代码引导。** 根容器一次性写六七个 `…Name` + `LinkMenuKey`（`Examples/Workflow/Avalonia Trimmed/Demo/Demo/Views/Workflow/TreeView.axaml` 的 `behaviors:WorkflowSurfaceBehavior.*` 属性），节点/插槽视图写自己的开关（`NodeView.axaml`、`SlotView.axaml` 的 `IsEnabled`），画布变换靠绑定 `TreeView` 自己的 `WorkflowCanvasTransformBehavior.Transform`（`TreeView.axaml`）。宿主也可直接调 `WorkflowSurfaceBehavior.Refresh(host)`（公开静态，`WorkflowSurfaceBehavior.Refresh`）在数据/尺寸变化后重解析 —— demo 在多处这么做（`Examples/Workflow/Avalonia/Demo/Views/Workflow/WorkflowView.axaml.cs` 的 `Refresh` 调用）。
   - **名字解析不需要 using，因为命名空间是嵌套的**：这家所有工作流文件的命名空间是 `VeloxDev.WorkflowSystem.AttachedBehaviors`，而 Core 的类型在 `VeloxDev.WorkflowSystem` / `.StandardEx` ⇒ 父命名空间类型直接可见（所以 `WorkflowMinimapOverlay.cs` 头部没有 `using VeloxDev.WorkflowSystem;`，而 `WorkflowSlotConnectionBehavior.cs` 写了那条冗余的）；子命名空间 `StandardEx`（`GetHelper()`）才要显式 using（`WorkflowSurfaceBehavior.cs` 的 `using`）。
   - 宿主 XAML 用 **`xmlns:behaviors="using:VeloxDev.WorkflowSystem.AttachedBehaviors"`**（Avalonia 的 `using:` 写法，不是 `clr-namespace:…;assembly=…`）。

---

## 四、`Attached/Workflow/` 的挂载模型（入口清单）

（`PlatformDetection` 已于 2026-10-18 删除；其余九个）按「挂在哪 + 谁来开」分档：

| 挂载点（`RegisterAttached<Owner, THost, T>` 的 `THost`） | 开关 | 入口/要点 |
|---|---|---|
| 根 `UserControl`：`WorkflowSurfaceBehavior`（`WorkflowSurfaceBehavior` 的 `RegisterAttached` 属性组） | `IsEnabled`；`ZoomEnabled` 单独再开滚轮；`LinkMenuKey` | 若干 `…Name` 全在 `ResolveNamedControls` 里解析 |
| 节点视图 `UserControl`：`WorkflowSlotLayoutBehavior`（`WorkflowSlotLayoutBehavior` 的 `RegisterAttached` 属性组） | `IsEnabled` | 槽名是逗号分隔串，与 WPF 同形（WPF 的 `SlotNames`/`GetAllSlotNames`） |
| 拖拽手柄 `InputElement`：`WorkflowNodeDragBehavior`（`WorkflowNodeDragBehavior` 的 `RegisterAttached` 属性组） | `IsEnabled` | 只认左键（`OnPointerPressed` 的 `PointerPressed` 订阅）；坐标宿主靠 `CoordinateHostName` / `CoordinateHostType` 二选一（`ResolveCoordinateHost`） |
| 插槽视图 `Control`：`WorkflowSlotConnectionBehavior`（`WorkflowSlotConnectionBehavior` 的 `RegisterAttached`） | `IsEnabled` | 两阶段命令（`OnPointerPressed` / `OnPointerReleased`） |
| 节点容器 `Panel`：`ViewPool`（`ItemsSourceProperty`/`TemplateSelectorProperty`） | **没有 `IsEnabled`**，两个数据属性（`ItemsSource` + `TemplateSelector`）谁写谁建管理器（`OnItemsSourceChanged`/`OnTemplateSelectorChanged`） | 管理器放 `ConditionalWeakTable`，`DetachedFromVisualTree` 时 `Detach`（`_managers`/`OnPanelDetached`/`CleanupManager`） |
| 画布 `Control`：`WorkflowCanvasTransformBehavior`（`TransformProperty`） | 无开关：属性只是通知载体，宿主写（`Apply`）、XAML 绑；回调**刻意为空**（`OnTransformChanged`） | 七角色里唯一「只承载不响应」的 |
| 具名子控件：`WorkflowMinimapOverlay : Control, IWorkflowMinimapOverlay`（`WorkflowMinimapOverlay` 的类声明） | 不是行为类；宿主把偏移推给它（`WorkflowSurfaceBehavior.UpdateMinimapOverlay`），自己 `Render` 自绘 | 消费方继承改配色（demo 的 `MinimapOverlay.cs`） |
| 模型事件桥 `WorkflowEvents : AvaloniaObject`（`WorkflowEvents` 的类声明） | 绑 `Node`/`Slot`/`Tree` 到 sink | 订阅跟随 `DataContext`（`WorkflowEvents.Attachment` 的 `Connect`/`OnDataContextChanged`）；转发在 Core `WorkflowEventRelay` |

**成对不变量**：`IsEnabled=false` 与重挂都会走 `Detach` → 解绑全部订阅（`WorkflowSurfaceBehavior.Detach`、`WorkflowNodeDragBehavior.Detach`、`WorkflowSlotLayoutBehavior.Detach`、`ViewPool.OnPanelDetached`）。新增订阅时必须在对应 `Detach`（或 `WorkflowSurfaceBehavior.UnsubscribeResolvedControls`）里配对 —— 漏掉不报错，只在重挂后事件翻倍。

---

## 五、程序集级约束：一个进程只能装一家

七家适配器把**同名类型放在同一个命名空间**：`VeloxDev.TransitionSystem.{Interpolator, State, Transition, Transition<T>, TransitionEffect, TransitionEffects, UIThreadInspector, TransitionInterpreter, TransitionScheduler<T>}`、`VeloxDev.WorkflowSystem.AttachedBehaviors.*`、`VeloxDev.Adapters.NativeSamplers.*`（逐家 grep 命名空间，七家一致）。两条后果：

- 同时引用两家的项目里直接写这些类型名会 **CS0433**。仓库里为此在测试工程用程序集限定反射：`Examples/Transition/AUTO TEST/Samplers/AvaloniaEntries.cs` 的 `CrossAdapter`，它上方的注释就是这条。
- 这家的采样器里 **一部分与别家重名**（Brush / Color / CornerRadius / GridLength / Point / Size / Thickness / Transform，各自与 WPF/WinUI/Jalium 中的至少一家撞名），只能反射取；另 **一部分是这家独有**（BoxShadows / PixelPoint / PixelRect / PixelSize / RelativePoint / RelativeRect），测试里直接写类型名。该文件的 `Entry(...)` 条目分走 `typeof(...)` 与 `CrossAdapter("…")` 两条路。

---

## 六、采样器为什么这家最多（**机制**，不是「这家写得全」）

数一遍（`git ls-files 'Src/Adapters/<家>/PlatformAdapters/Samplers/*.cs' | wc -l`）：各家数量不等，Avalonia 在四家 XAML 平台里最多。「别的家不用写这么多」是错的 —— 四个 XAML 家都在同一量级。真实机制：

- **Core 只覆盖「不属于任何 toolkit」的取值类型**：它的静态注册表用的是 `System.Drawing` 与 `System.Numerics` 下的 `Point`/`PointF`/`Size`/`SizeF`/`Color`/`Rectangle`/`RectangleF`/`Vector2-4`/`Quaternion`（`Src/Core/VeloxDev.Core/TransitionSystem/Sampling/Interpolator.cs` 的 `using` + 静态构造函数里的 `RegisterInterpolator`）。
- **工具包自己的同名类型是另一个类型**，必须各自注册：这家注册 `Avalonia.Point` / `Avalonia.Size` / `Avalonia.Color`（`Interpolator.cs` 里三条对应的 `RegisterInterpolator`），WPF / WinUI / MAUI / Jalium 同理各注册一份自己的。
- **这家多出来的几条是「Avalonia 把多少概念命名成了值类型」的函数**：`PixelPoint` / `PixelSize` / `PixelRect`（窗口与屏幕坐标）、`RelativePoint` / `RelativeRect`（相对单位）、`BoxShadows`（圆角阴影是值类型 `BoxShadow` 的集合）。
- **WinForms / Razor 的采样器很少**，因为它们的可动画属性大多直接落在 Core 已覆盖的类型上（WinForms 的 `Location`/`Size`/`BackColor` 就是 `System.Drawing` 的），例外只有 `PaddingSampler`、`StringSampler`。
- **已知缺口（是遗漏不是背离）**：这家**有** `Avalonia.Rect`（`WorkflowSlotLayoutBehavior.cs` 挂 `Visual.BoundsProperty`、读取 `control.Bounds`，就是它）与 `Avalonia.Vector`，但注册表两个都没有；而 WPF / WinUI / MAUI 三家都注册了自家的 `Rect`。⇒ 这家上对 `Rect`/`Vector` 属性做动画会静默跳过（Core `Sampling/Interpolator.cs` 的 `Warn(WarnStage.Unsampled, …)`）。细节与「该不该补」见 `TransitionSystem/adapters/avalonia.md` §二.7。

---

## 七、`GlobalUsings.cs` 与 csproj

- `GlobalUsings.cs` 只三条：`VeloxDev.TransitionSystem`、`VeloxDev.TransitionSystem.Abstractions`、`VeloxDev.Threading`。**七家逐字相同**（`git ls-files 'Src/Adapters/*/GlobalUsings.cs'` 全部核过）。所以这家的文件里看不到这三个 using 是正常的（`Interpolator.cs` 的 `InterpolatorCore`、`TransitionInterpreter.cs` 的 `TransitionInterpreterCore<>` 都靠它）。它**不含** `DynamicTheme` 与 `WorkflowSystem`；后者靠 §三.3 的命名空间嵌套，前者各文件自己写。
- TFM：`netstandard2.0;net6.0;net8.0`（`VeloxDev.Avalonia.csproj` 的 `TargetFrameworks`）。**七家里只有这一家声明 `netstandard2.0`**（`grep -l netstandard2.0 Src/Adapters/*/*.csproj` 只命中它）。代价是**全模块只有少量条件编译**：`Transition.cs` 的 `#if !NETSTANDARD2_0` 块（`System.Numerics` 那组重载）与 `BoxShadowsSampler.cs` 的零调用者死代码，细节见 `TransitionSystem/adapters/avalonia.md` §二.6。
- 平台版本锚点 `AvaloniaVersion=11.1.0`（csproj 的 `AvaloniaVersion`），三处 `PackageReference` 共用：升版本改这一处。
- 另外三条：`AvaloniaUseCompiledBindingsByDefault=true`（csproj 的 `AvaloniaUseCompiledBindingsByDefault`，所以 `{Binding $parent[local:TreeView].(behaviors:…)}` 这类写法按编译期绑定解析）、`GeneratePackageOnBuild=True`（csproj 的 `GeneratePackageOnBuild`）、`AvaloniaResource Include="Assets\**"`（csproj 的 `AvaloniaResource`）—— 最后一条指向的目录**在仓库里不存在**（`git ls-files` 与磁盘都没有 `Assets/`），空 glob，别按它去找资源。
- `Debug` 走 `ProjectReference`、非 Debug 走 `PackageReference`（csproj 里 `Configuration` 条件的那一对）是**七家统一形状**（版本 10.0.1），规则与原因见 `VeloxDev.Core.Generator/architecture.md` §五。
- `obj/` 下另有 **csproj 未声明**的 TFM 产物（如 `net8.0` / `net10.0`）；当前裁剪元数据只落在 `net8.0` 上（`obj/Debug/net8.0/VeloxDev.Avalonia.AssemblyInfo.cs` 的 `[assembly: AssemblyMetadata("IsTrimmable", "True")]`，`net10.0` 那份没有）。机制与 Core 那边同：外部 MSBuild 属性覆盖 ⇒ **不能反推「这个包本身可裁剪」**。这家是七家里唯一有裁剪 demo 的（`Examples/Workflow/Avalonia Trimmed/`，其 `Directory.Build.props` 不再显式 root `VeloxDev.Core.Extension`，理由写在注释里）。

---

## 八、陷阱（这个项目级）

1. **模板解析失败是静默的。** `ViewManager.AddOrReuseView` 找不到 `DataTemplate` 会抛（`throw new InvalidOperationException($"No DataTemplate found for type…")`），但调用它的批次循环把异常吞成 `Debug.WriteLine`（`ProcessNextBatch` 的 `catch`）⇒ 症状是**节点永远不出现，不抛不报**。WPF 那份逐字相同（WPF `ViewManager` 的 `ProcessNextBatch`）⇒ 六家共有的约定而非这家 bug；但在**这家最容易撞上**：模板要按「面板 → 沿 `Parent` 链 → `Application.Current.DataTemplates`」三级找（`ViewManager.FindDataTemplate`）。
2. **全模块唯一的反射点读的是私有框架字段，而且是「做一次就自摘」的。** `WorkflowSurfaceBehavior.OnScrollViewerLayoutUpdated` 取 `GestureRecognizerCollection` 的私有 `_recognizers` 列表，把 `ScrollGestureRecognizer` 全删掉（理由写在同一方法的注释里：它会在拖拽中抢走指针捕获，触屏平台尤甚），删完立刻 `viewer.LayoutUpdated -= OnScrollViewerLayoutUpdated` —— 所以这段逻辑只在第一次布局后跑一次，之后不再走。前提是 `GestureRecognizerCollection` 只公开 `Add`（`WorkflowSystem/adapters/avalonia.md` §二.4）。这家**没有任何裁剪注解**（grep `IsTrimmable|DynamicDependency|RequiresUnreferencedCode` 只命中 `obj/` 产物），而这家恰好有裁剪 demo（§七）。**裁剪会把这里影响到什么程度、是抛还是静默失效，我没有验证 —— 存疑**；要动这里先在裁剪 demo 上实测。
3. **`WorkflowSurfaceBehavior.Refresh` 是 public 静态入口**，每次都会重解析名字、重写滚动偏移并写 `viewModel.Layout.ViewportOffset`（`Refresh` 与 `UpdateVisibleRegion`）。别在别处再写一次 `Viewport` / `ScrollViewer.Offset` —— `Viewport` 只有适配器写这条契约（`WorkflowSystem/extension.md` §3.9-3）在这家由这一处落地。
   2026-10-03 起 `Refresh` **也读** `ViewportOffset`：`CaptureViewportRestore` 在 `UpdateVisibleRegion` 之前取值、换树才算，末尾 `QueueViewportRestore` 排 `DispatcherPriority.Loaded` 滚过去。⇒ 宿主的加载路径**不要**再自己滚，也不要在绑定前手动捕获 —— 适配器已经做在那一层（`WorkflowSystem/extension.md` §3.9-10）。
4. **`ViewManager.Attach` 要求集合实现 `IEnumerable`**（`throw new ArgumentException("Collection must implement IEnumerable…")`）；`ViewPool` 的两个属性**谁后写谁重建管理器**（`OnItemsSourceChanged`/`OnTemplateSelectorChanged`），运行期换 `TemplateSelector` 会 `CleanupManager` → 清空并重建整池视图（`OnTemplateSelectorChanged`、`CleanupManager`）。
5. **故意让一次 XAML 编译失败，会把 Avalonia 的构建服务卡住；此后每一笔「成功」的构建都在产出一个没有编译 XAML 的程序集。** 症状是**启动即抛**
   `Avalonia.Markup.Xaml.XamlLoadException: No precompiled XAML found for <App>`（栈顶在 `App.axaml.cs` 的 `Initialize()`）。
   机制：XAML 编译由一个**常驻的构建服务进程**做（命令行形如 `dotnet exec .../avalonia.buildservices/<ver>/tools/netstandard2.0/...`），它把目标程序集载进自己进程来改写。一次失败的编译把它留在「载着 `obj/<cfg>/<tfm>/<App>.dll`」的状态，后续构建就改不动那个文件了 —— 而且**增量构建既不报错、也不产出编译后的 XAML**，只有 `-t:Rebuild` 才会明说：
   `AVLN9999: The process cannot access the file '...obj\Debug\net8.0\Demo.dll' because it is being used by another process`。
   解法：先 `dotnet build-server shutdown`，再按命令行把剩下的持有者找出来 kill —— `Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | ? { $_.CommandLine -like '*avalonia.buildservices*' }` —— 然后 `-t:Rebuild`。
   **实测于 2026-09-22**：一次故意写错的绑定（`AVLN2000`）之后，连续两笔构建都报「0 警告 0 错误」，产出的 `Demo.dll` 启动即抛上句；关服务 + `Rebuild` 后恢复正常。同日稍后另一次实测里，持有者**不是**那个服务，而是整解并行构建留下的 `MSBuild.dll /nodemode:1 /nodeReuse:true` 驻留节点，`dotnet build-server shutdown` 一条命令即解。所以持有者至少有两种：Avalonia 自己的构建服务与 MSBuild 的 node-reuse 节点。**先关构建服务、再重试，两步都不成再去找 `avalonia.buildservices`**。（**不可复核**：依赖本机当时进程状态。）
   ⚠ **推论：Avalonia 工程的「构建绿」不足以证明程序集可用。** 改完 XAML 要真启动一次。
   **用户侧的症状就叫「demo 起不来」**：双击 `bin/.../Demo.exe` 一闪即退，从命令行才看得到上句（它走 stderr，进程立刻退出）。**判据是窗口句柄**：`Get-Process Demo | Select MainWindowHandle` 非 0、且 `Responding=True`，才算起来了；「跑 N 秒没输出」不是证据。
6. **kill 掉一个 Avalonia workflow demo 会留下它自己的 WebView2 子进程**，占着 `bin/.../<App>.exe.WebView2\EBWebView`；下一次启动死在
   `COMException 0x800700AA`（资源正在使用中，栈在 `WebView2HwndAdapter.InitializeAsync`）。
   来源是 **`md:MarkdownView`**：`AvalonMarkdown` 8.0.0 直接依赖 `Avalonia.Controls.WebView`（nuspec 里写着），于是每个带对话面板的 workflow demo 都真的起一个 WebView2 —— Debug / Release 都有，与 `AvaloniaUI.DiagnosticsSupport` 无关。
   收尾要连子进程一起收：`taskkill /PID <pid> /T /F`，或按命令行筛 `msedgewebview2.exe`（`CommandLine -like '*<demo 目录>*'`），别只关主进程。

---

## 九、核过、是七家通则、别再查的几处

- `TransitionEffects` 三档预设 + 可变静态：七家同形（Avalonia `TransitionEffects.cs` 与 WPF 同名文件逐字相同）。
- `GlobalUsings.cs` 三条：七家逐字相同。
- `Debug`/非 Debug 双轨：七家同形（只差 TFM）。
- `State` 空壳：七家都是空壳。
- `ViewManager` 的静默 `catch`、`batchSize = 3` + `DispatcherPriority.Background`、`ConditionalWeakTable` 放管理器：与 WPF 逐字同形（WPF `ViewManager` 的 `ProcessNextBatch`）。
- 主题值转换器的**一组名字**（Double / Point / Thickness / CornerRadius / Color / Brush / Object）与 WPF / WinUI / MAUI 相同（WinForms 与 Razor 各有更多，Jalium 没有）；名字是共有的，**实现里用的解析 API 才是各家的**（这是 `ThemeValueConverters.cs` 存在的理由，见 `extension.md` §二）。
