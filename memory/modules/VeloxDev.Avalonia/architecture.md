# VeloxDev.Avalonia — 架构

> 代码：`Src/Adapters/VeloxDev.Avalonia/`。**34 个 .cs、4240 行**，分四块：`PlatformAdapters/` 根 9 个、`PlatformAdapters/Samplers/` 14 个、`Attached/Workflow/` 10 个、`GlobalUsings.cs` 1 个。
> 数法：`git ls-files 'Src/Adapters/VeloxDev.Avalonia/*.cs'` 就能得 **34**（git 的 pathspec 通配符**跨 `/` 匹配**，与 shell 不同）；而 `git ls-files 'Src/Adapters/VeloxDev.Avalonia/**/*.cs'` 只得 **33** —— `**/` **不匹配顶层文件**，七家每家都正好漏掉顶层那一个 `GlobalUsings.cs`。核验用 `git ls-files Src/Adapters/VeloxDev.Avalonia | grep -c '\.cs$'`。
> 最大几个文件：`WorkflowSurfaceBehavior.cs` 898、`WorkflowMinimapOverlay.cs` 593、`WorkflowSlotLayoutBehavior.cs` 346、`ViewManager.cs` 297、`Samplers/TransformSampler.cs` 291、`ThemeValueConverters.cs` 256、`Transition.cs` 236。
> **三条 GUI 轴的平台差异不在这里**：`memory/modules/{TransitionSystem,WorkflowSystem,Templates}/adapters/avalonia.md`；七角色契约与附着属性注册位置在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；「怎么用这套 API」在 `skills/`。本文只写这个项目独有的：四块怎么分、注册与工厂在哪、三条轴各自怎么被宿主接上、程序集级约束、csproj 与 TFM。

---

## 一、一个程序集，三条互不相干的轴

| 目录 | 面向的 Core 模块 | 消费方怎么接入 | 平台差异细节在哪 |
|---|---|---|---|
| `PlatformAdapters/` 根 9 个 | TransitionSystem | `Transition<T>.Create()`（编译期闭泛型，**零引导**） | `TransitionSystem/adapters/avalonia.md` |
| `PlatformAdapters/Samplers/` 14 个 | TransitionSystem（采样器实现） | 同上 —— 注册是构造 `Interpolator` 的副作用，见 §二 | 同上 §二.7、§三 |
| `PlatformAdapters/ThemeValueConverters.cs` | DynamicTheme | `[ThemeConfig<XxxConverter, …>]` 的类型参数 + 一次性 `ThemeManager.SetPlatformInterpolator(new Interpolator())` | 本文 §三.2 |
| `Attached/Workflow/` 10 个 | WorkflowSystem | XAML 附着属性 + `PART_*` 名字解析 | `WorkflowSystem/adapters/avalonia.md` |

**三块之间零交叉引用**，可复核：`grep -rn "using VeloxDev.WorkflowSystem" PlatformAdapters/` 与 `grep -rn "using VeloxDev.TransitionSystem" Attached/` 都为空。每块各面向一个 Core 模块 —— `ThemeValueConverters.cs` 连一条 `using VeloxDev.*` 都没有（它 `namespace VeloxDev.DynamicTheme` 与 `IThemeValueConverter` 同命名空间，工具全是 Avalonia 类型）。

**不解决什么：**

- **网格装饰器这家不自带**：由消费方实现（demo 是 `Examples/Workflow/Avalonia Trimmed/Demo/Demo/Views/Workflow/WorkflowGridDecorator.cs`，`Panel, IWorkflowGridDecorator`），与 WPF 同形。这家自带的只有小地图（`WorkflowMinimapOverlay`，且刻意留成可继承改配色的基类，demo 就是这么用的）。
- **生成器侧**（`[ThemeConfig]`/`[VeloxProperty]` 生成什么）与平台无关，见 `VeloxDev.Core.Generator/architecture.md`。
- 平台差异的「为什么」不在本文，在两篇 `adapters/avalonia.md`。

---

## 二、注册与工厂：`Interpolator` 是 Core 泛型点名本家的地方

- **注册表在 Core**：静态 `ConcurrentDictionary`（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:40`），基类静态 ctor 先注册 15 个来自 `System.Drawing` / `System.Numerics` 的采样器（`:11-27`）。
- **这家补 14 条**（`Interpolator.cs:13-26`），注册的是 Avalonia 的类型，其中两条注册的是**接口** `IBrush` / `ITransform`（`:13-14`）。
- **静态 ctor 什么时候跑**：Core 的泛型节点里写着 `protected TInterpolatorCore interpolator = new();`（`Src/Core/VeloxDev.Core/TransitionSystem/Transition.cs:297`，约束 `where TInterpolatorCore : InterpolatorCore, new()` 在 `:271`）⇒ **每构造一个 transition 节点就 `new` 一个这家的 `Interpolator`**，第一次构造即触发 14 条注册。所以纯动画路径**不需要任何引导代码**，而纯主题路径恰好不构造 `Transition<T>`，这就是它必须补一条 `SetPlatformInterpolator`（§三.2）的原因。
- 同一处约束串解释了这家另外四个类的形状：`THost : ITransitionHost<TPriorityCore>, new()`（`UIThreadInspector`）、`TTransitionInterpreterCore : …, new()`（`TransitionInterpreter`）、`TStateCore : IFrameState, new()`（`State`）、`TEffectCore : ITransitionEffect<TPriorityCore>, new()`（`TransitionEffect`）。**这些空壳/薄壳不是「这家有差异」，是泛型闭合点** —— `State.cs:3` 与 Jalium 的 `State.cs` 内容等价，都只有一句类声明。它们唯一的硬要求是**公开可构造**。
- **工厂只有一个方法**：`CreateScheduler`（`Interpolator.cs:30-33`），按 `effect is ITransitionEffect<DispatcherPriority>` 决定给不给 scheduler；基类默认 `null`（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:126`）。
  - **全仓唯一调用者是主题系统**（`Src/Core/VeloxDev.Core/DynamicTheme/ThemeManager.cs:220`），不是普通动画路径（普通路径走 `TransitionSchedulerCore<…>.FindOrCreate` 的按目标缓存，`Src/Core/VeloxDev.Core/TransitionSystem/TransitionScheduler.cs:197`）。
  - 后果是**全有或全无**：只要有一个目标拿不到 scheduler，整批主题切换退化为瞬时（`ThemeManager.cs:206-208` 无 interpolator / 无分组；`:227-230` 某个目标拿不到 scheduler）。
- 第二个公开入口 `TransitionScheduler<TTarget>`（`TransitionScheduler.cs:5-9`）在仓库内**没有任何调用者**，是给消费方的显式泛型壳（WinUI 也定义了同名同形的一个，同样无人调用）。

---

## 三、三条轴各自「怎么被宿主接上」

1. **Transition：零引导，家在编译期选定。** 消费方写的是这家的 `Transition<T>`（`Transition.cs:15-24`），它把 Core 的七参泛型一次闭合成本家的五件套；用法形状是 `private static readonly Transition<Rectangle> X = Transition<Rectangle>.Create()…`（`Examples/Transition/Avalonia/Demo/Views/MainWindow.axaml.cs:322-325`）。⇒ 「用哪一家」= 引用哪个适配器程序集，没有运行时注册。
   - 控制面（`Transition.Exit` / 暂停等）在 Core 的非泛型 `TransitionCore`（`Src/Core/VeloxDev.Core/TransitionSystem/Transition.cs:19-55`），与平台无关；这家的非泛型 `Transition`（`Transition.cs:10-13`）是空壳，只是把同名 API 放进本家程序集。
   - `TransitionEffects.Empty/Theme/Hover`（`TransitionEffects.cs:7/11/15`）是这家三档预设，**七家同形**（与 WPF 同名文件逐字相同），不是本家设计。
2. **Theme：必须一次性引导，且不引导不报错。** 消费方在启动处调 `ThemeManager.SetPlatformInterpolator(new Interpolator())`（demo：`Examples/Theme/Avalonia/Demo/App.axaml.cs:25`）。两个作用写在 Core 的 remarks 里（`ThemeManager.cs:55-61`，入口 `:62`）：把本家的采样器注册跑起来 + 给 Core 一个它自己点不出名字的 scheduler（Core 原话：which inspector, interpreter and dispatcher priority to animate with is the one thing Core cannot name）。没有它主题照样切换、只是**瞬切无动画**（`ThemeManager.cs:207-209`）—— 最容易误判成「动画坏了」的一处。属性侧是特性的类型参数：`[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["#ffffff"])]`（`Examples/Theme/Avalonia/Demo/ThemeTile.cs:17-18`）。
3. **Workflow：XAML 附着属性 + 名字解析，无代码引导。** 根容器一次性写六七个 `…Name` + `LinkMenuKey`（`Examples/Workflow/Avalonia Trimmed/Demo/Demo/Views/Workflow/TreeView.axaml:12-18`），节点/插槽视图写自己的开关（`NodeView.axaml:11-14`、`SlotView.axaml:8`），画布变换靠绑定 `TreeView` 自己的 `WorkflowCanvasTransformBehavior.Transform`（`TreeView.axaml:26/36`）。宿主也可直接调 `WorkflowSurfaceBehavior.Refresh(host)`（公开静态，`WorkflowSurfaceBehavior.cs:133`）在数据/尺寸变化后重解析 —— demo 在多处这么做（`Examples/Workflow/Avalonia/Demo/Views/Workflow/WorkflowView.axaml.cs:65`/`:199`/`:218`）。
   - **名字解析不需要 using，因为命名空间是嵌套的**：这家所有工作流文件的命名空间是 `VeloxDev.WorkflowSystem.AttachedBehaviors`，而 Core 的类型在 `VeloxDev.WorkflowSystem` / `.StandardEx` ⇒ 父命名空间类型直接可见（所以 `WorkflowMinimapOverlay.cs` 头部没有 `using VeloxDev.WorkflowSystem;`，而 `WorkflowSlotConnectionBehavior.cs:4` 写了那条冗余的）；子命名空间 `StandardEx`（`GetHelper()`）才要显式 using（`WorkflowSurfaceBehavior.cs:15`）。
   - 宿主 XAML 用 **`xmlns:behaviors="using:VeloxDev.WorkflowSystem.AttachedBehaviors"`**（Avalonia 的 `using:` 写法，不是 `clr-namespace:…;assembly=…`）。

---

## 四、`Attached/Workflow/` 的挂载模型（入口清单）

十个文件里 `PlatformDetection` 是死代码（`WorkflowSystem/adapters/avalonia.md` §四.1），其余九个按「挂在哪 + 谁来开」分档：

| 挂载点（`RegisterAttached<Owner, THost, T>` 的 `THost`） | 开关 | 入口/要点 |
|---|---|---|
| 根 `UserControl`：`WorkflowSurfaceBehavior`（`:57-79`） | `IsEnabled`；`ZoomEnabled` 单独再开滚轮（`:75-76`）；`LinkMenuKey`（`:106-107`） | 5 个 `…Name` 全在这里解析（`:409-450`） |
| 节点视图 `UserControl`：`WorkflowSlotLayoutBehavior`（`:23-39`） | `IsEnabled` | 槽名是逗号分隔串，与 WPF 同形（WPF `:408-411`） |
| 拖拽手柄 `InputElement`：`WorkflowNodeDragBehavior`（`:20-30`） | `IsEnabled` | 只认左键（`:85-87`）；坐标宿主靠 `CoordinateHostName` / `CoordinateHostType` 二选一（`:134-148`） |
| 插槽视图 `Control`：`WorkflowSlotConnectionBehavior`（`:10-11`） | `IsEnabled` | 两阶段命令（`:36-49`） |
| 节点容器 `Panel`：`ViewPool`（`:21-27`） | **没有 `IsEnabled`**，两个数据属性（`ItemsSource` + `TemplateSelector`）谁写谁建管理器（`:41-71`） | 管理器放 `ConditionalWeakTable`，`DetachedFromVisualTree` 时 `Detach`（`:73-91`） |
| 画布 `Control`：`WorkflowCanvasTransformBehavior`（`:13-14`） | 无开关：属性只是通知载体，宿主写（`Apply`，`:25-26`）、XAML 绑；回调**刻意为空**（`:28-33`） | 七角色里唯一「只承载不响应」的 |
| 具名子控件：`WorkflowMinimapOverlay : Control, IWorkflowMinimapOverlay`（`:23`） | 不是行为类；宿主把偏移推给它（`WorkflowSurfaceBehavior.cs:829-841`），自己 `Render` 自绘 | 消费方继承改配色（demo 的 `MinimapOverlay.cs`） |
| 模型事件桥 `WorkflowEvents : AvaloniaObject`（`:34`） | 绑 `Node`/`Slot`/`Tree` 到 sink | 订阅跟随 `DataContext`（`WorkflowEvents.cs:104-122`）；转发在 Core `WorkflowEventRelay` |

**成对不变量**：`IsEnabled=false` 与重挂都会走 `Detach` → 解绑全部订阅（`WorkflowSurfaceBehavior.cs:371-389`、`WorkflowNodeDragBehavior.cs:49-78`、`WorkflowSlotLayoutBehavior.cs:66-103`、`ViewPool.cs:41-71`）。新增订阅时必须在对应 `Detach`（或 `UnsubscribeResolvedControls`，`WorkflowSurfaceBehavior.cs:450-467`）里配对 —— 漏掉不报错，只在重挂后事件翻倍。

---

## 五、程序集级约束：一个进程只能装一家

七家适配器把**同名类型放在同一个命名空间**：`VeloxDev.TransitionSystem.{Interpolator, State, Transition, Transition<T>, TransitionEffect, TransitionEffects, UIThreadInspector, TransitionInterpreter, TransitionScheduler<T>}`、`VeloxDev.WorkflowSystem.AttachedBehaviors.*`、`VeloxDev.Adapters.NativeSamplers.*`（逐家 grep 命名空间，七家一致）。两条后果：

- 同时引用两家的项目里直接写这些类型名会 **CS0433**。仓库里为此在测试工程用程序集限定反射：`Examples/Transition/AUTO TEST/Samplers/AvaloniaEntries.cs:61-62` 的 `CrossAdapter`，注释（`:59-60`）就是这条。
- 这家的 14 个采样器里 **8 个与别家重名**（Brush / Color / CornerRadius / GridLength / Point / Size / Thickness / Transform，各自与 WPF/WinUI/Jalium 中的至少一家撞名），只能反射取；另 **6 个是这家独有**（BoxShadows / PixelPoint / PixelRect / PixelSize / RelativePoint / RelativeRect），测试里直接写类型名。该文件 14 条 `Entry(...)` = 6 条 `typeof(...)` + 8 条 `CrossAdapter("…")`。

---

## 六、采样器为什么这家最多（**机制**，不是「这家写得全」）

数一遍（`git ls-files 'Src/Adapters/<家>/PlatformAdapters/Samplers/*.cs' | wc -l`）：**Avalonia 14**、WPF 12、MAUI 12、WinUI 10、Jalium 9、WinForms 1、Razor 1。「别的家不用写这么多」是错的 —— 四个 XAML 家都在 9–12 之间。真实机制：

- **Core 只覆盖「不属于任何 toolkit」的取值类型**：它的静态注册表用的是 `System.Drawing` 与 `System.Numerics` 下的 `Point`/`PointF`/`Size`/`SizeF`/`Color`/`Rectangle`/`RectangleF`/`Vector2-4`/`Quaternion`（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:2-3` 的 using + `:12-27` 的注册）。
- **工具包自己的同名类型是另一个类型**，必须各自注册：这家注册 `Avalonia.Point` / `Avalonia.Size` / `Avalonia.Color`（`Interpolator.cs:16/18/24`），WPF / WinUI / MAUI / Jalium 同理各注册一份自己的。
- **这家多出来的 6 条是「Avalonia 把多少概念命名成了值类型」的函数**：`PixelPoint` / `PixelSize` / `PixelRect`（窗口与屏幕坐标）、`RelativePoint` / `RelativeRect`（相对单位）、`BoxShadows`（圆角阴影是值类型 `BoxShadow` 的集合）。
- **WinForms / Razor 各只有 1 个**，因为它们的可动画属性大多直接落在 Core 已覆盖的类型上（WinForms 的 `Location`/`Size`/`BackColor` 就是 `System.Drawing` 的），例外各只有一个：`PaddingSampler`、`StringSampler`。
- **已知缺口（是遗漏不是背离）**：这家**有** `Avalonia.Rect`（`WorkflowSlotLayoutBehavior.cs:136` 挂 `Visual.BoundsProperty`、`:272` 读 `control.Bounds`，就是它）与 `Avalonia.Vector`，但注册表两个都没有；而 WPF / WinUI / MAUI 三家都注册了自家的 `Rect`。⇒ 这家上对 `Rect`/`Vector` 属性做动画会静默跳过（Core `Interpolator.cs:183` 的 `Warn("Unsampled", …)`）。细节与「该不该补」见 `TransitionSystem/adapters/avalonia.md` §二.7。

---

## 七、`GlobalUsings.cs` 与 csproj

- `GlobalUsings.cs` 只三条：`VeloxDev.TransitionSystem`、`VeloxDev.TransitionSystem.Abstractions`、`VeloxDev.Threading`。**七家逐字相同**（`git ls-files 'Src/Adapters/*/GlobalUsings.cs'` 全部核过）。所以这家的文件里看不到这三个 using 是正常的（`Interpolator.cs:9` 的 `InterpolatorCore`、`TransitionInterpreter.cs:7` 的 `TransitionInterpreterCore<>` 都靠它）。它**不含** `DynamicTheme` 与 `WorkflowSystem`；后者靠 §三.3 的命名空间嵌套，前者各文件自己写。
- TFM：`netstandard2.0;net6.0`（`VeloxDev.Avalonia.csproj:4`）。**七家里只有这一家声明 `netstandard2.0`**（`grep -l netstandard2.0 Src/Adapters/*/*.csproj` 只命中它）。代价是**全模块只有两处条件编译**：`Transition.cs:238-263`（`System.Numerics` 的四个重载）与 `BoxShadowsSampler.cs` 的零调用者死代码，细节见 `TransitionSystem/adapters/avalonia.md` §二.6。
- 平台版本锚点 `AvaloniaVersion=11.1.0`（csproj:14），三处 `PackageReference` 共用（`:26-28`）：升版本改这一处。
- 另外三条：`AvaloniaUseCompiledBindingsByDefault=true`（`:7`，所以 `{Binding $parent[local:TreeView].(behaviors:…)}` 这类写法按编译期绑定解析）、`GeneratePackageOnBuild=True`（`:8`）、`AvaloniaResource Include="Assets\**"`（`:22`）—— 最后一条指向的目录**在仓库里不存在**（`git ls-files` 与磁盘都没有 `Assets/`），空 glob，别按它去找资源。
- `Debug` 走 `ProjectReference`、非 Debug 走 `PackageReference`（`:29-30`）是**七家统一形状**（版本 10.0.0），规则与原因见 `VeloxDev.Core.Generator/architecture.md` §五。
- `obj/` 下另有 `net8.0` / `net10.0` 两个 **csproj 未声明**的 TFM 产物，且只有它们带裁剪元数据（`obj/Debug/net10.0/VeloxDev.Avalonia.AssemblyInfo.cs:13-14` 有 `IsTrimmable` + `IsAotCompatible`；`net8.0` 的 `:13` 只有 `IsTrimmable`）。机制与 Core 那边同：外部 MSBuild 属性覆盖 ⇒ **不能反推「这个包本身可裁剪」**。这家是七家里唯一有裁剪 demo 的（`Examples/Workflow/Avalonia Trimmed/`，其 `Directory.Build.props:16` 显式 root 住 `VeloxDev.Core.Extension`，理由写在 `:7-15`）。

---

## 八、陷阱（这个项目级）

1. **模板解析失败是静默的。** `ViewManager.AddOrReuseView` 找不到 `DataTemplate` 会抛（`ViewManager.cs:166`），但调用它的批次循环把异常吞成 `Debug.WriteLine`（`:137-139`）⇒ 症状是**节点永远不出现，不抛不报**。WPF 那份逐字相同（WPF `ViewManager.cs:137-139`）⇒ 六家共有的约定而非这家 bug；但在**这家最容易撞上**：模板要按「面板 → 沿 `Parent` 链 → `Application.Current.DataTemplates`」三级找（`ViewManager.cs:232-270`）。
2. **全模块唯一的反射点读的是私有框架字段，而且是「做一次就自摘」的。** `WorkflowSurfaceBehavior.cs:703-707` 取 `GestureRecognizerCollection` 的私有 `_recognizers` 列表，把 `ScrollGestureRecognizer` 全删掉（理由写在 `:692`：它会在拖拽中抢走指针捕获，触屏平台尤甚），删完立刻 `viewer.LayoutUpdated -= OnScrollViewerLayoutUpdated`（`:707`）—— 所以这段逻辑只在第一次布局后跑一次，之后不再走。前提是 `GestureRecognizerCollection` 只公开 `Add`（`WorkflowSystem/adapters/avalonia.md` §二.4）。这家**一个裁剪注解都没有**（grep `IsTrimmable|DynamicDependency|RequiresUnreferencedCode` 只命中 `obj/` 产物），而这家恰好有裁剪 demo（§七）。**裁剪会把这里影响到什么程度、是抛还是静默失效，我没有验证 —— 存疑**；要动这里先在裁剪 demo 上实测。
3. **`WorkflowSurfaceBehavior.Refresh` 是 public 静态入口**（`:133`），每次都会重解析名字、重写滚动偏移并写 `viewModel.Layout.ViewportOffset`（`:133-146`、`:787 起`）。别在别处再写一次 `Viewport` / `ScrollViewer.Offset` —— `Viewport` 只有适配器写这条契约（`WorkflowSystem/extension.md` §3.9-3）在这家由这一处落地。
   2026-10-03 起 `Refresh` **也读** `ViewportOffset`：`CaptureViewportRestore`（`:296-309`）在 `UpdateVisibleRegion` 之前取值、换树才算，末尾 `QueueViewportRestore`（`:313-336`）排 `DispatcherPriority.Loaded` 滚过去。⇒ 宿主的加载路径**不要**再自己滚，也不要在绑定前手动捕获 —— 适配器已经做在那一层（`WorkflowSystem/extension.md` §3.9-10）。
4. **`ViewManager.Attach` 要求集合实现 `IEnumerable`**（`:41-42`，否则抛 `ArgumentException`）；`ViewPool` 的两个属性**谁后写谁重建管理器**（`:41-71`），运行期换 `TemplateSelector` 会 `Detach` → 清空并重建整池视图（`:57-71`、`:84-91`）。
5. **故意让一次 XAML 编译失败，会把 Avalonia 的构建服务卡住；此后每一笔「成功」的构建都在产出一个没有编译 XAML 的程序集。** 症状是**启动即抛**
   `Avalonia.Markup.Xaml.XamlLoadException: No precompiled XAML found for <App>`（栈顶在 `App.axaml.cs:13` 的 `Initialize()`）。
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

- `TransitionEffects` 三档预设 + 可变静态：七家同形（Avalonia `TransitionEffects.cs:7/11/15` 与 WPF 同名文件逐字相同）。
- `GlobalUsings.cs` 三条：七家逐字相同。
- `Debug`/非 Debug 双轨：七家同形（只差 TFM）。
- `State` 空壳：七家都是空壳。
- `ViewManager` 的静默 `catch`、`batchSize = 3` + `DispatcherPriority.Background`、`ConditionalWeakTable` 放管理器：与 WPF 逐字同形（WPF `ViewManager.cs:125`）。
- 主题值转换器的 **7 个名字**（Double / Point / Thickness / CornerRadius / Color / Brush / Object）与 WPF / WinUI / MAUI 相同（WinForms 13 个、Razor 4 个、Jalium 没有）；名字是共有的，**实现里用的解析 API 才是各家的**（这是 `ThemeValueConverters.cs` 存在的理由，见 `extension.md` §二）。
