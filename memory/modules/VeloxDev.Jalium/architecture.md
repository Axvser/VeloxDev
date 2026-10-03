# VeloxDev.Jalium — 架构

> 代码：`Src/Adapters/VeloxDev.Jalium/`。**29 个 .cs、3073 行**
> （`Attached/Workflow/` 12 个 2347 行，最大 `WorkflowTreeView.cs` 621、`WorkflowMinimapOverlay.cs` 305、`WorkflowLinkView.cs` 272；
> `PlatformAdapters/` 8 个 300 行，最大 `Transition.cs` 138；`PlatformAdapters/Samplers/` 9 个 631 行，最大 `TransformSampler.cs` 276、`BrushSampler.cs` 131；顶层 `GlobalUsings.cs` 3 行）。
> **计数写法**：`find Src/Adapters/VeloxDev.Jalium -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*'`。
> `git ls-files` 的 glob `'Src/Adapters/VeloxDev.Jalium/**/*.cs'` 只匹配**子目录**里的文件 —— 该目录**本级**的 `GlobalUsings.cs` 要另写 `*.cs`，否则少算一个。（写这份记忆时 `Attached/Workflow/` 下新增的七个基类文件尚未 `git add`，`git ls-files` 因此暂时只报 **22 个 / 1523 行**；提交后为 29 / 3073。）
>
> 本文只写「读完这 29 个文件才知道的东西」。类型清单、成员表、继承树请看 IDE。
>
> **本模块没有 `adapters/` 子目录，也不该有**：模块名本身就是一个平台，不存在平台轴。它在两条轴上的平台差异分别落在
> `memory/modules/TransitionSystem/adapters/jalium.md`、`memory/modules/WorkflowSystem/adapters/jalium.md`、`memory/modules/Templates/adapters/jalium.md`，本文**指路不抄**。

---

## 一、一个项目、两条轴（不是三条）

这个项目叫「Jalium 适配器」，但它只实现了 **两个** Core 模块的平台侧契约，彼此**在程序集内互不引用**：

| 轴 | Core 契约 | 本项目落点 | 平台差异记在哪 |
|---|---|---|---|
| TransitionSystem | 宿主 / 解释器 / 调度器 / 帧 pacer / 采样器 | `PlatformAdapters/`（8 个类型 + `Samplers/` 9 个） | `memory/modules/TransitionSystem/adapters/jalium.md` |
| WorkflowSystem | 七个视图角色 | `Attached/Workflow/`（11 文件：七个角色的适配器侧类型 —— 六个可继承控件基类 + `slot-view` 的设计值/几何 + 视图池三件套；见 §三） | `memory/modules/WorkflowSystem/adapters/jalium.md` |

**第三条轴（DynamicTheme）在这一家是空的，这是它与 WPF / Avalonia / WinUI / MAUI / WinForms / Razor 的六家差异里最容易被忽略的一条**：

- 全模块**没有 `ThemeValueConverters.cs`**（`git ls-files 'Src/Adapters/VeloxDev.Jalium/PlatformAdapters/ThemeValueConverters.cs'` 为空）；`IThemeValueConverter` 在本项目零命中。
- Jalium 也**没有主题 demo**：`Examples/Theme/` 下只有 `Avalonia` / `Avalonia Trimmed` / `WPF` / `WPF Trimmed`（`git ls-files Examples/Theme/`）。
- 所以 `Interpolator.CreateScheduler`（`PlatformAdapters/Interpolator.cs:26-29`）在本仓库**是一条无人的路**：它的唯一调用者是 Core `Src/Core/VeloxDev.Core/DynamicTheme/ThemeManager.cs:219`，而 `ThemeManager.SetPlatformInterpolator` 在本仓库只有 4 个非测试调用点，全是 Avalonia / WPF（`Examples/Theme/Avalonia/Demo/App.axaml.cs:25`、`Examples/Theme/Avalonia Trimmed/Demo/Views/MainWindow.axaml.cs:46`、`Examples/Theme/WPF/Demo/App.xaml.cs:16`、`Examples/Theme/WPF Trimmed/Demo/MainWindow.xaml.cs:47`）。
  ⇒ **别把它当成过渡轴的优先级选择来读**：过渡轴走的是 `Transition<T>` 的编译期类型实参，`CreateScheduler` 只是 DynamicTheme 的工厂接缝（休眠，但留着是对的 —— 它是这份实现唯一能告诉 DynamicTheme「这家的调度器是哪个类型」的地方）。

**两条轴的命名空间都寄生在 Core 上**：`VeloxDev.TransitionSystem`（`PlatformAdapters/*`）与 `VeloxDev.WorkflowSystem.AttachedBehaviors`（`Attached/Workflow/*`）。唯一例外是 9 个采样器，它们在 `VeloxDev.Adapters.NativeSamplers`（`PlatformAdapters/Samplers/PointSampler.cs:3`、`TransformSampler.cs:3`、`BrushSampler.cs:6`）—— 与另外六家同名同命名空间，所以**同时引用两家适配器的工程必然 CS0433**，绕法见 `Examples/Transition/AUTO TEST/Samplers/JaliumEntries.cs`。

**目录名与轴不对齐，别按目录推契约**：`Attached/` 只有 WorkflowSystem 一条轴（名字起得像「所有附着行为」，实际不是）。

---

## 二、`PlatformAdapters/`：注册与工厂

### 2.1 三段静态链，只有第一段需要宿主动手

| 要注册的东西 | 注册点 | 什么时候真的发生 |
|---|---|---|
| 9 个采样器（10 条登记） | `Interpolator` 的**静态构造**（`PlatformAdapters/Interpolator.cs:10-24`） | 第一次构造 `Transition<T>` 时：Core 的字段初始化 `protected TInterpolatorCore interpolator = new();` |
| 采样器所在的宿主 / 解释器 / 优先级 | `TransitionScheduler` 的类型实参（`PlatformAdapters/TransitionScheduler.cs:5-8`） | 同上，全部编译期写死 |
| DynamicTheme 的调度器工厂 | `Interpolator.CreateScheduler`（`PlatformAdapters/Interpolator.cs:26-29`） | 宿主显式调 `SetPlatformInterpolator` —— **本仓库无人调**（§一） |

**没有程序集级入口，也没有 `Initialize()`**：全模块 `ModuleInitializer` / `[assembly:` 零命中，唯一的静态构造就是 `Interpolator.cs:10`。⇒ 只用 `Transition<T>` 的宿主什么都不用做。

### 2.2 十行登记里的两条非常规写法

```csharp
// PlatformAdapters/Interpolator.cs:12-13 的注释：Exact-type lookup — register BOTH Brush and
// SolidColorBrush so brush properties declared as either type animate
RegisterInterpolator(typeof(Brush), new BrushSampler());            // :20
RegisterInterpolator(typeof(SolidColorBrush), new BrushSampler());  // :21 —— 同一个实例，两条登记
```

两条只在读这个文件时才知道的推论：

1. **`BrushSampler` 是唯一被登记两次的采样器**（`:20-21` 共享一个实例）。Core 的解析次序是「精确类型 → 最近基类 → 接口按名」，`SolidColorBrush` 的最近基类本来就是 `Brush`，所以 `:21` 严格来说是**冗余**的 —— 它防的是「宿主把属性声明成 `SolidColorBrush` 且 Core 的基类回溯在某些路径下不生效」，代价为零。**别以为漏登记的族类型会被基类接住而放心不加**：这一家的姿态是所有要动画的类型都显式登记。
2. **`Transform3D` 用了全限定名**（`:23`）—— 因为 `Jalium.UI.Media` 下同时有 `Transform`（2D）与 `Media3D.Transform3D`。这一条与 WPF 的 `Transform` 家族登记是两个不同的形状：WPF 登记 `typeof(Transform)` 一登记就接住整族，Jalium 两个都显式登记（`Transform` + `Transform3D`），**没有登记 `TransformGroup` / `TranslateTransform` 这些子类**。

### 2.3 这家的三个平台类型是它独有的

`TransitionEffect.Priority` 硬编码为 `DispatcherPriority.Render`（`PlatformAdapters/TransitionEffect.cs:7`），`UIThreadInspector.InternalPriority` 是 `Send`（`PlatformAdapters/UIThreadInspector.cs:31`）—— 两个优先级**语义不同、刻意不同**：前者是动画帧写入用的优先级，后者是「让回调插队到最前」用的。改成同一个会静默改变观感（帧晚一档）。
`UIThreadInspector.ThreadFor` 的兜底链是**四级**（`PlatformAdapters/UIThreadInspector.cs:14-18`）：target 自己的 `Dispatcher` → `Application.Current?.Dispatcher` → `Dispatcher.FromThread(CurrentThread)` → `Dispatcher.MainDispatcher`；整块包在 `try` 里，落空退 `ThreadRef.None`（`:21-25`，注释说明了理由），后果只是这个目标**没有 pacer**。`PostCore` 另有两道闸：拿不到 dispatcher 返回 false，`HasShutdownStarted` 也返回 false（`:35-36`）。

`.Property(...)` 的**流式重载集不属于 Core**：Core 的 `TransitionCore<...>` 基类里一个 `Property` 都没有（`Src/Core/VeloxDev.Core/TransitionSystem/Transition.cs` 全文无 `Property` 方法），13 个重载全写在各家适配器里（Jalium 13 个在 `PlatformAdapters/Transition.cs:34-137`；对照 WPF 28、Avalonia 30、WinForms 17）。⇒ **重载表的长度就是这一家「能被 `.Property(...)` 顺滑写出来的平台类型」的数量**，Jalium 的是：`Brush?`、`ICollection<Transform>`、`Transform3D?`、`Point`、`Rect`、`Thickness`、`CornerRadius`、`Size`、`Color`、`int`、`double`、`float`。注意 **`SolidColorBrush` 没有自己的重载**（虽然它被登记了）—— 声明成 `SolidColorBrush` 的属性靠泛型 `Property<TValue>`（`:34`）走。

`Property(Expression<Func<T, Transform?>>, ICollection<Transform>)` 是**唯一带分支的重载**（`Transition.cs:48-66`）：`Count == 1` 时把**单个实例**直接 `SetValue`，否则才包 `TransformGroup`。注释 `:51-53` 写明了理由（包起来会改运行时类型，破坏 `((TranslateTransform)x.RenderTransform).X` 这类嵌套路径）。⇒ **顺手「统一成总是包 TransformGroup」会让依赖具体变换子类类型的宿主静默失效**。

---

## 三、`Attached/Workflow/`：12 个文件，七个角色的可继承基类 + 视图池

**2026-10-03 重构：适配器重新拥有整套工作流表面。** 按 [`adapter-base-class-specifications.md`](../specifications/adapter-base-class-specifications.md) 的新规（无标记语言的平台，每个 item-template 角色都要由适配器提供一个可继承类型，模板只派生重写），适配器与模板的分工整体翻面。**注意**：这不是把当初删掉的六个行为类搬回来 —— 那六个零消费者的 `WorkflowSurfaceBehavior` / `WorkflowCanvasTransformBehavior` / `WorkflowNodeDragBehavior` / `WorkflowSlotConnectionBehavior` / `WorkflowSlotLayoutBehavior` / 独立 `WorkflowGridDecorator` 以及自装配外壳 `WorkflowTreeView : Grid` 都没有回来；现在这七个类型是按「平台机制进包、策略留模板」重新切开的基类，`WorkflowTreeView` 是**活表面的基类**（demo/模板都从它派生），不是当初那个没人用的自装配外壳。

12 个文件：

| 文件 | 行 | 角色 | 谁写状态 |
|---|---|---|---|
| `WorkflowTreeView.cs` | 621 | **表面基类** `: Canvas`：池化接线（`SetTree`→`ViewPool.SetTemplateSelector/SetItemsSource`）、视口记账、缩放钉、`Grow*`/`NavigateToWorld`、平移/节点拖拽/连线手势、网格与标尺渲染（经注入的 `GridDecorator`）、命中测试 | 表面持有 `PortLayout`/`GridDecorator`/`TemplateSelector` 属性；绑定 `_tree` 后驱动 `ViewPool` |
| `WorkflowNodeView.cs` | 212 | **节点卡基类**（`abstract : Canvas`）：`DataContext` 绑定、订阅（节点 `Anchor`/`Size`、插槽集合与 `State`、布局）、定位（`Canvas.SetLeft/Top` + 尺寸）、`Viewbox` + 内层设计画布脚手架、端口状态画刷 `SlotBrush` | 绑定写；卡面由派生类 `DrawCard` 画 |
| `WorkflowLinkView.cs` | 272 | **连线基类** `: FrameworkElement`：`DataContext` 绑定、端点 + 布局订阅、自盒化（`UpdateBounds`）、`OnRender` 烘焙回元素局部 | 自己写自己的盒；几何由模型算 |
| `WorkflowGridDecorator.cs` | 234 | **网格基类**（原为 `static class`，现实例）：世界网格 + 两条浮动标尺的绘制、笔刷缓存 | 派生类设调色板/间距 |
| `WorkflowTemplateSelector.cs` | 51 | **选择器基类** `: IWorkflowTemplateSelector`：四个工厂（Node/Slot/Link/Tree）+ `virtual CreateView` 分派与「工厂未设」诊断 | 派生类设工厂 |
| `WorkflowSlotView.cs` | ~95 | 端口图形：绑定、状态着色、重画 | 派生（改半径与调色板） |
| `WorkflowPortLayout.cs` | 41 | 卡片设计尺寸与端口位置的**值类型**（`sealed`），宿主的设计不是常量 | 宿主/模板赋值 |
| `WorkflowPortGeometry.cs` | 125 | `static`：把节点的输入/输出/标题从 view-model 反射读出来，并把端口中心定位到画布（纯模型数学） | 无状态 |
| `WorkflowMinimapOverlay.cs` | 305 | 小地图，实现 `IWorkflowMinimapOverlay`；`RulerBand => 0`（`:48`） | 视口数值由宿主的附着属性喂 |
| `IWorkflowTemplateSelector.cs` | 11 | 「item 类型 → 视图」的工厂契约，替代 XAML 家的 `DataTemplateSelector`：`CreateView(object item)` 返回**已构造控件** | 宿主实现 |
| `ViewPool.cs` | 66 | 附着属性 `ItemsSource` / `TemplateSelector`，驱动 `ConditionalWeakTable<Panel, ViewManager>`（`:13`） | 无状态，只做转发 |
| `ViewManager.cs` | 207 | 池本体：按 `item.GetType()` 分桶、即时建视图、移除时 `Collapsed` + `DataContext = null` | 唯一写者，池内视图全归它管 |

> 11 个文件都在命名空间 `VeloxDev.WorkflowSystem.AttachedBehaviors`（与六家同名同形），所以**同时引用两家适配器必然 CS0433** —— 与采样器面同一类代价。

### 3.1 表面与「七个角色」的边界

`WorkflowTreeView` 是**唯一持有状态**的角色：它拿一棵树、把可见集喂给 `ViewPool`（`SetTree`，`WorkflowTreeView.cs:161-180`），并拥有一套完整交互与渲染：

- 视口记账 `UpdateViewport`（`:539-589`，含未测量时退回整张画布、把标尺厚度喂给 `SetVirtualizeInset`）；
- 缩放钉 `_zoomPin` + `NotifyZoomCommitted`（`:51-53`、`:182-209`、`:539-561`）—— **这套深缩放守卫现在在适配器里**，不再是「适配器零命中」；
- 手势 `OnMouseDown/Move/Up`（`:345-509`）：输出口起连线、标题栏拖节点、空白处平移，`LostMouseCapture` 收尾；
- 渲染 `OnRender`（网格，`:231-236`）与 `OnPostRender`（标尺 + 拖拽预览，`:239-259`）；
- 命中测试 `HitTestOutputPort`/`HitTestInputPort`/`HitTestTitleBar`/`HitTestCard`（`:279-341`）。

其余六个角色都只伺候这一个表面：节点/连线视图由 `ViewPool` 池化到 `Canvas.Children`，网格装饰器由表面注入（`GridDecorator` 属性），选择器的工厂产出节点/连线视图，端口几何与布局同时供表面、节点卡、连线三处读。**改「画布怎么动」先打开 `WorkflowTreeView.cs`；改「卡片/连线长什么样」在模板或 demo 的派生类里。**

### 3.2 模板与 Trimmed demo 都只是薄派生

模板产物（`Src/Templates/VeloxDev.Jalium.Templates/working/content/`）与 Trimmed demo（`Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/`）现在是**同形的薄派生**：表面是 `sealed class : WorkflowTreeView`、只设四个属性（背景、连线色、`PortLayout`、`GridDecorator` + `TemplateSelector`）；节点卡 `: WorkflowNodeView` 只重写 `DrawCard`；连线 `: WorkflowLinkView` 只设颜色/线宽；网格 `: WorkflowGridDecorator` 只设调色板。逐个角色行数见 `memory/modules/Templates/adapters/jalium.md` §一。

### 3.3 小地图的 `ScrollViewer` 仍然没有适配器侧赋值者

`WorkflowMinimapOverlay.ScrollViewer` 是**普通自动属性**（`:55`），而 `NavigateToWorld`（`:224-240`）要求 `_tree` 与 `ScrollViewer` **同时非空**。适配器里没有任何东西替它赋值 —— 宿主必须自己赋（demo `Examples/Workflow/Jalium Trimmed/Demo/MainWindow.cs:64` 的 `ScrollViewer = viewer`）⇒ 自己 new 出来的小地图不赋就是「只看不动」。（`WorkflowTreeView` 有 `AttachScrollViewer`，但那是**表面自己**的 viewer，不会转给小地图。）

### 3.4 画布变换通道彻底没有了

`ViewManager.UpdateRenderTransforms` / `ViewPool.UpdateRenderTransforms` 这对镜像方法**已连同实现一起删除**（`git grep -n UpdateRenderTransforms -- Src Examples` 零命中）—— 不再是「有实现、结构上不可达」。视图按世界坐标自定位（节点卡 `ApplyPosition`，`WorkflowNodeView.cs:191-201`；连线自盒化，`WorkflowLinkView.cs:255-271`），不需要宿主把渲染变换镜像给它们。**别去 demo 或别家找它的调用者，也别按老记忆在 `ViewManager.cs` 里翻它 —— 它不存在了。**

---

## 四、结构性根因：没有标记语言 ⇒ 适配器出可继承基类，模板只写策略

**这是 Jalium 与另外六家最本质的差异，也是这份文件最该记住的一条。**

六家的模式是「适配器出零件，demo/template 用 XAML 把零件拼成表面」。Jalium 没有标记语言，宿主只能用代码装配；2026-10-03 的规范把这条补偿定成「适配器发基类、模板薄派生」。现在：

| | 适配器（基类） | 模板产物与 Trimmed demo（薄派生） |
|---|---|---|
| 表面 | `WorkflowTreeView : Canvas`（池化、视口、手势、渲染、命中） | `TemplateClass : WorkflowTreeView`，只设 `SurfaceBackground`/`ConnectingLinkColor`/`PortLayout`/`GridDecorator`/`TemplateSelector` |
| 池化 | `ViewPool` + `ViewManager` + `IWorkflowTemplateSelector` | 由 `WorkflowTreeView.SetTree` 自动接上（宿主/模板不必再手接） |
| 小地图 | `WorkflowMinimapOverlay`（可继承） | 模板 `minimap-overlay` 产出一个空子类；demo 宿主 `MainWindow.cs:61-68` new 出来并直接赋 `ScrollViewer`（§3.3） |
| 节点/连线 | `WorkflowNodeView` / `WorkflowLinkView` 基类 | 只重写 `DrawCard` / 设线色 |
| 网格 | `WorkflowGridDecorator` 基类（实例） | 只设调色板与间距 |
| 端口几何/布局 | `WorkflowPortGeometry`（反射读模型）+ `WorkflowPortLayout`（值） | `slot-view` 条目产出 `WorkflowSlotView` 子类 + `Layout` 值 |

**两条推论：**

1. **`_zoomPin` / `NotifyZoomCommitted` 现在长在适配器的表面上。** 它们就在 `WorkflowTreeView.cs:51-53,182-209,539-561`（不再是「适配器零命中」）；模板/demo 的薄派生继承它，宿主只负责在提交缩放后调 `surface.NotifyZoomCommitted(...)`。这套守卫的成立前提是「表面自己调 `Virtualize`」，而表面现在确实在包内。
2. **`IWorkflowTemplateSelector` 不是 Jalium 独有**：WinForms 在 `Src/Adapters/VeloxDev.WinForms/Attached/Workflow/ViewManager.cs:14-22` 里有一个逐字同名同形的接口（`Control CreateView(object item)`，连注释都一样）。这条轴的真实划分是「有标记语言的三家用 `DataTemplateSelector`，无标记语言的 Jalium/WinForms 用自造接口」，见 `memory/modules/WorkflowSystem/adapters/jalium.md` §1.1。

---

## 五、仓库里到底被消费了什么（实测）

引用 `VeloxDev.Jalium` **项目**的工程只有 4 个（`git grep -ln 'ProjectReference Include=".*VeloxDev.Jalium/VeloxDev.Jalium.csproj"'`）：`Examples/Transition/Jalium/Demo/`（`:10`）、`Examples/Transition/AUTO TEST/Samplers/VeloxDev.SamplerTest.csproj`（`:28`）、`Examples/Workflow/Jalium/Demo/`（`:35`）、`Examples/Workflow/Jalium Trimmed/Demo/`（`:56`）。逐个核它们真正用到的类型：

| 工程 | 用到的适配器类型 |
|---|---|
| `Examples/Transition/Jalium/Demo/` | 只用到 `PlatformAdapters/` 的 `TransitionEffect` 与 `Transition<T>`（`MainWindow.cs:419` 一族、`:13` 的类注释「Standalone animation test for the VeloxDev.Jalium PlatformAdapters」）；**`Interpolator` / `TransitionScheduler` / `UIThreadInspector` 名字零命中** —— 它们只在 `Transition<T>` 的类型实参里被间接使用 |
| `Examples/Transition/AUTO TEST/Samplers/` | 只走**反射**（`JaliumEntries.cs`、`SamplerCoverageTests.cs:30` 的 `ExpectedAdapterAssemblies`） |
| `Examples/Workflow/Jalium Trimmed/Demo/` | 整套工作流基类都真被派生：`WorkflowTreeView`（`TreeView.cs:13`）、`WorkflowNodeView`（`NodeView.cs:14`）、`WorkflowLinkView`（`LinkView.cs:11`）、`WorkflowSlotView`（`SlotView.cs:12`）、`WorkflowGridDecorator`（`GridDecorator.cs:11`）、`WorkflowTemplateSelector`（`TemplateSelector.cs:17`）、`WorkflowMinimapOverlay`（`MinimapOverlay.cs:9`）。`ViewPool`/`ViewManager` 由 `WorkflowTreeView.SetTree` 间接驱动 |
| `Examples/Workflow/Jalium/Demo/` | 只有 `WorkflowMinimapOverlay`（作为基类，`Views/Workflow/Minimap.cs:8`）；**不用**池化，也不派生其它基类（它自己写 `NodeEditorSurface : Canvas`，见 §八） |

**哪些类型在全仓库零消费者**：**选择器的 `SlotViewFactory` / `TreeViewFactory`**（`WorkflowTemplateSelector.cs:25,31`）—— 本仓库所有宿主只产出节点与连线 item，插槽与树 item 从不进池（若进了而工厂未设，`CreateView` 会抛 `InvalidOperationException`，`：37-48`）。老记忆里那对 `UpdateRenderTransforms` 镜像方法已随实现删除（§3.4），不再是零消费者。

⇒ **这份适配器的「已用面积」是：`PlatformAdapters/` 的过渡轴（真被跑），加上整套 Workflow 基类（Trimmed demo/模板真派生）。** 改任何一处之前先确认那条路上真的有调用者。

---

## 六、`GlobalUsings.cs` 与 `VeloxDev.Jalium.csproj`

**`GlobalUsings.cs` 三行，七个适配器逐字相同**（`global using` `VeloxDev.TransitionSystem` / `VeloxDev.TransitionSystem.Abstractions` / `VeloxDev.Threading`）：

- 这就是为什么 `PlatformAdapters/` 的文件除 `Jalium.UI*` 之外几乎不写 `using`，却能直接用 `InterpolatorCore`、`ISampler`（定义在 `...Abstractions`）。
- **它不含 `VeloxDev.WorkflowSystem`** ⇒ `Attached/Workflow/` 里需要 Core 契约的文件各自写 `using VeloxDev.WorkflowSystem;`（`WorkflowMinimapOverlay.cs:7`、`WorkflowTreeView.cs:7`）。
- **`global using` 是编译期的，不随引用传给消费者**：宿主要用 `Transition<T>` 仍得自己写 `using VeloxDev.TransitionSystem;`（`Examples/Transition/Jalium/Demo/MainWindow.cs:8`）。

**csproj 里影响代码本身的条件**（`VeloxDev.Jalium.csproj`）：

| 事实 | 行 | 后果 |
|---|---|---|
| `<TargetFramework>net10.0</TargetFramework>`（**单 TFM、不带平台后缀**） | `:7` | 七家里只有这家与 Razor 是单 TFM 且不带 `-windows`（Razor 是 `net6.0`）。`:4-6` 的注释把理由写明了：适配器只用跨平台核心（Controls/Media/Interop/Core），**不用 `net10.0-windows` 的 `Jalium.UI.Desktop` 入口包**，所以能同时服务 Windows / Linux / Android |
| 唯一的 Jalium 引用是 `Jalium.UI.Controls 26.10.8` | `:32` | `:29-31` 的注释：这是**能提供 `Canvas`/`ScrollViewer`/`Border`/`Control` + `DrawingContext`/`Geometry`/`FormattedText` 的**最低**平台中立包。**别名包 `Jalium.UI.Desktop` 由消费 demo 自己引**（`Examples/Workflow/Jalium Trimmed/Demo/Demo.csproj`）⇒ 适配器与 demo 的包版本可以不同步 |
| Debug → `ProjectReference`（`:27`）／非 Debug → `PackageReference VeloxDev.Core 9.0.0`（`:28`） | — | 与另外六家同形的双轨；两条同时生效会报重复成员 |
| `NoWarn` 写成**一条分号列表** `1573;1591;8605;8604` | `:12` | 七家里只有这家是这个集合：多出的 `8605` 是 `WorkflowMinimapOverlay.cs:43-52` 一族 DP 的 CLR 包装（`(double)GetValue(...)` / `(bool)GetValue(...)` 拆箱）真会触发的；`8604` 的原触发点（早先删除的 `WorkflowSlotLayoutBehavior` 里的递归下钻 `FindDescendantWithSlotDataContext`）随该文件消失。**新的工作流基类（`WorkflowTreeView`/`NodeView`/`LinkView`/`GridDecorator`/`TemplateSelector`/`PortGeometry`/`PortLayout`）不注册任何 DP**，所以没有新增的 `8605`/`8604` 触发点 |

---

## 七、陷阱（带依据）

1. **`ViewPool` 的 `Unloaded` 一次性退订会把画布永久留在空白态。** `panel.Unloaded += (_, _) => manager.Dispose();` 只在 manager **首次创建**时订阅一次（`ViewPool.cs:55`），而 `Dispose()` → `Detach()` → `ClearAll()` 会把所有视图 `Collapsed` + `DataContext = null`（`ViewManager.cs:64`、`:52`、`:177-195`），`_collection` 也置空。**表面移出树再放回去不会重新触发 `OnChanged`**（那只在附着属性变化时跑），于是没有任何东西重建视图 ⇒ 空白画布，无异常。demo 从不把表面移出树，所以看不出来。
2. **重设 `ItemsSource` 或 `TemplateSelector` 是「全拆重建」而不是增量对齐。** 两个 DP 共用同一个 `OnChanged`（`ViewPool.cs:19`/`:25`），而 `Attach` 第一行就是 `Detach()`（`ViewManager.cs:35`）⇒ 只要改其中一个，所有池内视图都会 `Collapsed` 后重挂，节点的入场动画/局部状态全部重来。想「只换选择器」做不到。
3. **`ViewPool` 的两个 DP 必须同时非空才建 manager**（`ViewPool.cs:49-60`）；只给一个（哪怕先给了 `ItemsSource`）走的是 `else` 分支的 `existing.Detach()`（`:61-64`）。所以「按 `VisibleItems` 先绑上、稍后补选择器」这种写法在中间态是**什么都没有**，不是「有框没内容」。`WorkflowTreeView.SetTree` 就是两个一起给（`WorkflowTreeView.cs:169-170`）。
4. **`ViewManager` 的池按 `item.GetType()` 键**（`ViewManager.cs:15`/`:127`/`:167`/`:184`），而同一个 item 被 `ReferenceEquals` 去重（`:122`）⇒ 同一集合里放两个引用相同的 item 只会得到一个视图。**`RemoveItem` 把 `DataContext` 置 `null` 后入池**（`:163-164`），复用时靠 `ApplyContext` 重新赋（`:197-200`）—— 视图若在字段里缓存了「我这个 item 是谁」而不监听 `DataContextChanged`，就会拿着旧模型继续画。三个新基类都靠 `DataContextChanged` 取模型（`WorkflowNodeView.cs:52`、`WorkflowLinkView.cs:54`），所以这条在池化复用时是活的。
5. **`WorkflowNodeView` 的端口中心 `WorkflowPortGeometry` 是反射读出来的。** 节点的输入口是普通插槽属性、输出口可能是 `SlotEnumerator<T>`，两者都**不在 `IWorkflowNodeViewModel` 接口上**，只能按属性名反射（`WorkflowPortGeometry.cs:37-62,123-124`）。⇒ 改节点 view-model 的输入/输出属性名会让端口与命中静默错位；卡片画成什么样由 `DrawCard` 决定，但**命中与连线读的是布局与几何，不是卡片画的内容**（`WorkflowNodeView.cs:20-24`）。
6. **删视图时同时置 `Collapsed` 与 `DataContext = null`**（`ViewManager.cs:163-164`）⇒ 视图里读 `DataContext` 的代码（含 `WorkflowLinkView` 的守卫）在池化回收后会看到 `null`，别把 `DataContext is not null` 当成「已初始化」。
7. **csproj 的两条独有设定会咬人**（详见 `memory/modules/TransitionSystem/adapters/jalium.md` §2.5）：单目标 `net10.0` 无平台后缀（`Src/Adapters/VeloxDev.Jalium/VeloxDev.Jalium.csproj:7`）⇒ 只能引用 `Jalium.UI.Controls` 这个平台中性包；`NoWarn` 含 `8605;8604`（`:12`），理由与现状见 §六。

---

## 八、入口：我要改 X 先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 画布平移 / 缩放 / 拖拽 / 插槽连接手势 / 命中 | `Attached/Workflow/WorkflowTreeView.cs`（表面基类；活表面是它的派生） |
| 网格线与标尺的数学 / 刻度 / 标签 | `Attached/Workflow/WorkflowGridDecorator.cs`（派生类只改调色板与间距） |
| 卡片长什么样 | 模板/demo 的 `NodeView.DrawCard`（派生自 `WorkflowNodeView`）；端口状态色在 `WorkflowNodeView.SlotBrush` |
| 连线的几何与自盒化 | `Attached/Workflow/WorkflowLinkView.cs`（`UpdateBounds` + `OnRender` 烘焙）；线色/线宽在派生类 |
| 端口在哪、怎么命中 | `Attached/Workflow/WorkflowPortGeometry.cs` + `WorkflowPortLayout.cs` |
| 视图池、每元素视图的创建/复用/回收 | `Attached/Workflow/ViewManager.cs`（挂点 `ViewPool.cs`） |
| 「item 类型 → 视图」的工厂契约 | `Attached/Workflow/IWorkflowTemplateSelector.cs`（11 行，替换 `DataTemplateSelector`）；基类 `WorkflowTemplateSelector.cs` |
| 小地图外观与导航 | `Attached/Workflow/WorkflowMinimapOverlay.cs` |
| 线程、优先级、pacer、调度器 | `PlatformAdapters/` 那 5 个短文件 + `PlatformAdapters/UIThreadInspector.cs` |
| 让某个 Jalium 类型可动画 | `PlatformAdapters/Samplers/` + `PlatformAdapters/Interpolator.cs:14-23` 的登记表 |
| 流式 `.Property(...)` 支持哪些类型 | `PlatformAdapters/Transition.cs:34-137`（13 个重载） |
| 包结构、TFM、双轨引用 | `VeloxDev.Jalium.csproj` |
| **宿主怎么接上适配器** | 池化由 `WorkflowTreeView.SetTree` 自动完成；小地图：new 子类并赋 `ScrollViewer`（`…/MainWindow.cs:61-68`）；缩放全在窗口侧（`…/MainWindow.cs:121-155` 键与滚轮、`:165-229` `ZoomBy`、提交后 `NotifyZoomCommitted`） |

---

## 九、这份文件没写的东西

- 12 个文件各自的成员列表、继承树、文件清单 —— IDE 里一按就有。
- 七个角色各自的职责、`PART_*` 约定、契约本身 —— `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；**Jalium 侧的角色机制现在都在适配器基类里，模板只派生**。
- 这一家的平台硬限制与刻意背离（`Visual.ShouldRenderChild` 按 `RenderSize` 盒裁剪 ⇒ 自盒化；`IsDragPreview` 跳过拖拽预览；纯模型数学；`_zoomPin` 的来龙去脉）—— `memory/modules/WorkflowSystem/adapters/jalium.md`。落点在适配器基类，`WorkflowSystem/adapters/jalium.md` 里若说「在模板/demo」那是**过期**。
- 过渡轴侧采样器逐一分析、`TransformSampler` 的端点短路与草稿实例类型守卫、`BrushSampler` 为什么只能混一个代表色 —— `memory/modules/TransitionSystem/adapters/jalium.md`。
- 模板侧：七个条目产出什么形状、12 个空转符号、标尺 36 现在单一来源在适配器 —— `memory/modules/Templates/adapters/jalium.md`；包结构与跨平台族划分在 `memory/modules/Templates/architecture.md`。
- 主题切换的完整流向 —— **这一家没有这条线**（§一），见 `memory/modules/DynamicTheme/architecture.md`。
- 人面向的「怎么用这套模板搭一个 Jalium 工作流视图」—— `skills/veloxdev-create-workflow/references/gui/jalium.md`。
