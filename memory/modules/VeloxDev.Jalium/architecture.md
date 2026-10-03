# VeloxDev.Jalium — 架构

> 代码：`Src/Adapters/VeloxDev.Jalium/`。**22 个 .cs、1541 行**
> （`Attached/Workflow/` 4 个 607 行，最大是 `WorkflowMinimapOverlay.cs` 305、`ViewManager.cs` 216；
> `PlatformAdapters/` 8 个 300 行，最大是 `Transition.cs` 138；`PlatformAdapters/Samplers/` 9 个 631 行，最大是 `TransformSampler.cs` 276、`BrushSampler.cs` 131；顶层 `GlobalUsings.cs` 3 行）。
> **计数写法**：`git ls-files 'Src/Adapters/VeloxDev.Jalium/*.cs' 'Src/Adapters/VeloxDev.Jalium/**/*.cs'`。
> 只写 `'.../**/*.cs'` 会得到 **21** —— 这条 pathspec 只匹配**子目录里**的 `.cs`，该目录**本级**的 `GlobalUsings.cs` 一个都不算（同理 `PlatformAdapters/**/*.cs` 只有 9 个采样器，`PlatformAdapters/` 本级那 8 个文件要另写 `PlatformAdapters/*.cs`）。
>
> 本文只写「读完这 22 个文件才知道的东西」。类型清单、成员表、继承树请看 IDE。
>
> **本模块没有 `adapters/` 子目录，也不该有**：模块名本身就是一个平台，不存在平台轴。它在两条轴上的平台差异分别落在
> `memory/modules/TransitionSystem/adapters/jalium.md`、`memory/modules/WorkflowSystem/adapters/jalium.md`、`memory/modules/Templates/adapters/jalium.md`，本文**指路不抄**。

---

## 一、一个项目、两条轴（不是三条）

这个项目叫「Jalium 适配器」，但它只实现了 **两个** Core 模块的平台侧契约，彼此**在程序集内互不引用**：

| 轴 | Core 契约 | 本项目落点 | 平台差异记在哪 |
|---|---|---|---|
| TransitionSystem | 宿主 / 解释器 / 调度器 / 帧 pacer / 采样器 | `PlatformAdapters/`（8 个类型 + `Samplers/` 9 个） | `memory/modules/TransitionSystem/adapters/jalium.md` |
| WorkflowSystem | 七个视图角色 | `Attached/Workflow/`（4 文件：视图池三件套 + 小地图；六个行为角色已删除，见 §三） | `memory/modules/WorkflowSystem/adapters/jalium.md` |

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

## 三、`Attached/Workflow/`：4 个文件，池化三件套 + 小地图

**七个视图角色的实现不在这份适配器里。** 六个行为类（`WorkflowSurfaceBehavior`、`WorkflowCanvasTransformBehavior`、`WorkflowNodeDragBehavior`、`WorkflowSlotConnectionBehavior`、`WorkflowSlotLayoutBehavior`、`WorkflowGridDecorator`）与自装配外壳 `WorkflowTreeView` 曾经在这里，现已删除（`git ls-files 'Src/Adapters/VeloxDev.Jalium/Attached/Workflow/'` 核不到）—— 它们在仓库里零消费者（§五）。剩下四个文件：

| 文件 | 职责 | 谁写状态 |
|---|---|---|
| `IWorkflowTemplateSelector.cs`（11 行） | 「item 类型 → 视图」的工厂契约，替代 XAML 家的 `DataTemplateSelector`：`CreateView(object item)` 返回**已构造控件** | 宿主实现 |
| `ViewPool.cs`（75 行） | 附着属性 `ItemsSource` / `TemplateSelector`，驱动 `ConditionalWeakTable<Panel, ViewManager>`（`:13`） | 无状态，只做转发 |
| `ViewManager.cs`（216 行） | 池本体：按 `item.GetType()` 分桶、即时建视图、移除时 `Collapsed` + `DataContext = null` | 唯一写者，池内视图全归它管 |
| `WorkflowMinimapOverlay.cs`（305 行） | 小地图，实现 `IWorkflowMinimapOverlay`；`RulerBand => 0`（`:48`） | 视口数值由宿主的附着属性喂，不自己读 |

> 四个文件都在命名空间 `VeloxDev.WorkflowSystem.AttachedBehaviors`（与六家同名同形），所以**同时引用两家适配器必然 CS0433**——与采样器面同一类代价。

### 3.1 一条仍活着但没人走的通道：`UpdateRenderTransforms`

`ViewManager.UpdateRenderTransforms`（`ViewManager.cs:67-73`，`:71` 是 `item.View.RenderTransform = transform;`）经 `ViewPool.UpdateRenderTransforms`（`ViewPool.cs:40-46`）转发，两者都是 `internal` —— **在 `Src/` 与 `Examples/` 里零调用者**。它曾经是已删除的 `WorkflowCanvasTransformBehavior` 的镜像端，现在那一段没了，这条通道只剩实现、**结构上不可达**。⇒ 判断依据以调用图为准：别看它有实现就以为有人调，也别去 demo 里找它的调用者 —— 找不到不是遗漏。

### 3.2 小地图的 `ScrollViewer` 没有适配器侧赋值者

`WorkflowMinimapOverlay.ScrollViewer` 是**普通自动属性**（`:55`），而 `NavigateToWorld`（`:224-240`）要求 `_tree` 与 `ScrollViewer` **同时非空**。旧的自装配外壳 `WorkflowTreeView` 删掉后，**适配器里没有任何东西替它赋值** —— 宿主必须自己赋（demo `Examples/Workflow/Jalium Trimmed/Demo/MainWindow.cs:64` 的 `ScrollViewer = viewer`）⇒ 自己 new 出来的小地图不赋就是「只看不动」。

---

## 四、这一家的结构性根因：没有标记语言，所以活表面在**模板产物**里

**这是 Jalium 与另外六家最本质的差异，也是这份文件最该记住的一条。**

六家的模式是「适配器出零件，demo/template 用 XAML 把零件拼成表面」。Jalium 没有标记语言，宿主只能用代码装配；曾经适配器里有一个自装配的 `WorkflowTreeView : Grid` 兜这个话题，但它零消费者、已被删除（§三）。现在这套补偿整体落在模板边：

| | 适配器（现在） | 模板产物 `workflow-tree-view`（活的那套） |
|---|---|---|
| 表面 | **没有** | `TemplateClass : Canvas`（`Src/Templates/VeloxDev.Jalium.Templates/working/content/workflow-tree-view/TemplateClass.cs`），自绘网格/标尺、自命中、自管虚拟化与视口 |
| 池化 | `ViewPool` + `ViewManager` + `IWorkflowTemplateSelector` | 用成套的 `ViewPool.SetItemsSource` / `SetTemplateSelector` 接上（`Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/TreeView.cs:114-115`） |
| 小地图 | `WorkflowMinimapOverlay`（可继承） | 模板 `minimap-overlay` 产出一个空子类；demo 宿主 `MainWindow.cs:61-68` new 出来并直接赋 `ScrollViewer`（`§3.2`） |
| 七个角色行为 | **没有**（已删除） | 表面自己实现：画布/拖拽/插槽/缩放全在 `TreeView : Canvas` 与 `NodeView`/`LinkView` 里 |

**两条推论：**

1. **`_zoomPin` / `NotifyZoomCommitted` 这套深缩放守卫只长在「自己管虚拟化的表面」那一侧。** 模板产物与 Trimmed demo 的 `TreeView` 有（`Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/TreeView.cs:52-60,153-178,220-239`），适配器里**零命中**（`git grep -n "_zoomPin\|NotifyZoomCommitted" -- Src/Adapters/VeloxDev.Jalium` 无输出）；WinUI 的适配器有（`Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:350-399`，注释明写 "mirrors Jalium TreeView.NotifyZoomCommitted"）。缩放后要立刻重算可见集，只能在这一侧做。
2. **`IWorkflowTemplateSelector` 不是 Jalium 独有**：WinForms 在 `Src/Adapters/VeloxDev.WinForms/Attached/Workflow/ViewManager.cs:14-22` 里有一个逐字同名同形的接口（`Control CreateView(object item)`，连注释都一样）。这条轴的真实划分是「有标记语言的三家用 `DataTemplateSelector`，无标记语言的 Jalium/WinForms 用自造接口」，见 `memory/modules/WorkflowSystem/adapters/jalium.md` §1.1。

---

## 五、仓库里到底被消费了什么（实测）

引用 `VeloxDev.Jalium` **项目**的工程只有 4 个（`git grep -ln 'ProjectReference Include=".*VeloxDev.Jalium/VeloxDev.Jalium.csproj"'`）：`Examples/Transition/Jalium/Demo/`（`:10`）、`Examples/Transition/AUTO TEST/Samplers/VeloxDev.SamplerTest.csproj`（`:28`）、`Examples/Workflow/Jalium/Demo/`（`:35`）、`Examples/Workflow/Jalium Trimmed/Demo/`（`:56`）。逐个核它们真正用到的类型：

| 工程 | 用到的适配器类型 |
|---|---|
| `Examples/Transition/Jalium/Demo/` | 只用到 `PlatformAdapters/` 的 `TransitionEffect` 与 `Transition<T>`（`MainWindow.cs:419` 一族、`:13` 的类注释「Standalone animation test for the VeloxDev.Jalium PlatformAdapters」）；**`Interpolator` / `TransitionScheduler` / `UIThreadInspector` 名字零命中** —— 它们只在 `Transition<T>` 的类型实参里被间接使用 |
| `Examples/Transition/AUTO TEST/Samplers/` | 只走**反射**（`JaliumEntries.cs`、`SamplerCoverageTests.cs:30` 的 `ExpectedAdapterAssemblies`） |
| `Examples/Workflow/Jalium Trimmed/Demo/` | `ViewPool`（`Views/Workflow/TreeView.cs:114-115`）、`IWorkflowTemplateSelector`（`TemplateSelector.cs:14`）、`WorkflowMinimapOverlay`（作为基类，`MinimapOverlay.cs:9`；`ScrollViewer` 在 `MainWindow.cs:64` 直接赋） |
| `Examples/Workflow/Jalium/Demo/` | 只有 `WorkflowMinimapOverlay`（作为基类，`Views/Workflow/Minimap.cs:8`） |

**哪些类型在全仓库零消费者**（按类型名在 `Examples/` 下 grep）：只剩一对镜像方法 —— `ViewManager.UpdateRenderTransforms`（`ViewManager.cs:67-73`）与 `ViewPool.UpdateRenderTransforms`（`ViewPool.cs:40-46`），见 §3.1。

⇒ **这份适配器的「已用面积」是：`PlatformAdapters/` 的过渡轴（真被跑），加上 `ViewPool` + `ViewManager` + `IWorkflowTemplateSelector` + `WorkflowMinimapOverlay`（真被跑）。** 改任何一处之前先确认那条路上真的有调用者。

---

## 六、`GlobalUsings.cs` 与 `VeloxDev.Jalium.csproj`

**`GlobalUsings.cs` 三行，七个适配器逐字相同**（`global using` `VeloxDev.TransitionSystem` / `VeloxDev.TransitionSystem.Abstractions` / `VeloxDev.Threading`）：

- 这就是为什么 `PlatformAdapters/` 的文件除 `Jalium.UI*` 之外几乎不写 `using`，却能直接用 `InterpolatorCore`、`ISampler`（定义在 `...Abstractions`）。
- **它不含 `VeloxDev.WorkflowSystem`** ⇒ `Attached/Workflow/` 里需要 Core 契约的文件各自写 `using VeloxDev.WorkflowSystem;`（`WorkflowMinimapOverlay.cs:7`）。
- **`global using` 是编译期的，不随引用传给消费者**：宿主要用 `Transition<T>` 仍得自己写 `using VeloxDev.TransitionSystem;`（`Examples/Transition/Jalium/Demo/MainWindow.cs:8`）。

**csproj 里影响代码本身的条件**（`VeloxDev.Jalium.csproj`）：

| 事实 | 行 | 后果 |
|---|---|---|
| `<TargetFramework>net10.0</TargetFramework>`（**单 TFM、不带平台后缀**） | `:7` | 七家里只有这家与 Razor 是单 TFM 且不带 `-windows`（Razor 是 `net6.0`）。`:4-6` 的注释把理由写明了：适配器只用跨平台核心（Controls/Media/Interop/Core），**不用 `net10.0-windows` 的 `Jalium.UI.Desktop` 入口包**，所以能同时服务 Windows / Linux / Android |
| 唯一的 Jalium 引用是 `Jalium.UI.Controls 26.10.8` | `:32` | `:29-31` 的注释：这是**能提供 `Canvas`/`ScrollViewer`/`Border`/`Control` + `DrawingContext`/`Geometry`/`FormattedText` 的**最低**平台中立包。**别名包 `Jalium.UI.Desktop` 由消费 demo 自己引**（`Examples/Workflow/Jalium Trimmed/Demo/Demo.csproj`）⇒ 适配器与 demo 的包版本可以不同步 |
| Debug → `ProjectReference`（`:27`）／非 Debug → `PackageReference VeloxDev.Core 9.0.0`（`:28`） | — | 与另外六家同形的双轨；两条同时生效会报重复成员 |
| `NoWarn` 写成**一条分号列表** `1573;1591;8605;8604` | `:12` | 七家里只有这家是这个集合：多出的 `8605` 是 `WorkflowMinimapOverlay.cs:43-52` 一族 DP 的 CLR 包装（`(double)GetValue(...)` / `(bool)GetValue(...)` 拆箱）真会触发的；`8604` 的原触发点（已删除的 `WorkflowSlotLayoutBehavior` 里的递归下钻 `FindDescendantWithSlotDataContext`）随该文件消失 |

---

## 七、陷阱（带依据）

1. **`ViewPool` 的 `Unloaded` 一次性退订会把画布永久留在空白态。** `panel.Unloaded += (_, _) => manager.Dispose();` 只在 manager **首次创建**时订阅一次（`ViewPool.cs:60-65`），而 `Dispose()` → `Detach()` → `ClearAll()` 会把所有视图 `Collapsed` + `DataContext = null`（`ViewManager.cs:52-61`、`:186-204`），`_collection` 也置空。**表面移出树再放回去不会重新触发 `OnChanged`**（那只在附着属性变化时跑），于是没有任何东西重建视图 ⇒ 空白画布，无异常。demo 从不把表面移出树，所以看不出来。
2. **重设 `ItemsSource` 或 `TemplateSelector` 是「全拆重建」而不是增量对齐。** 两个 DP 共用同一个 `OnChanged`（`ViewPool.cs:19`/`:25`），而 `Attach` 第一行就是 `Detach()`（`ViewManager.cs:35`）⇒ 只要改其中一个，所有池内视图都会 `Collapsed` 后重挂，节点的入场动画/局部状态全部重来。想「只换选择器」做不到。
3. **`ViewPool` 的两个 DP 必须同时非空才建 manager**（`ViewPool.cs:58-69`）；只给一个（哪怕先给了 `ItemsSource`）走的是 `else` 分支的 `existing.Detach()`（`:70-73`）。所以「按 `VisibleItems` 先绑上、稍后补选择器」这种写法在中间态是**什么都没有**，不是「有框没内容」。
4. **`ViewManager` 的池按 `item.GetType()` 键**（`ViewManager.cs:15`/`:136`/`:176`/`:193`），而同一个 item 被 `ReferenceEquals` 去重（`:131`）⇒ 同一集合里放两个引用相同的 item 只会得到一个视图。**`RemoveItem` 把 `DataContext` 置 `null` 后入池**（`:172-173`），复用时靠 `ApplyContext` 重新赋（`:206-209`）—— 视图若在字段里缓存了「我这个 item 是谁」而不监听 `DataContextChanged`，就会拿着旧模型继续画。
5. **`WorkflowMinimapOverlay` 单次点击即导航。** `OnMiniMouseDown` 先置 `_dragging = true` 就调 `PanToMini`（`:244-250`）⇒ 单击 = 居中到该点，不是「先选中」。`NavigateToWorld` 要求 `_tree` 与 `ScrollViewer` **同时非空**（`:224-240`），而 `ScrollViewer` 是普通属性（`:55`）、**适配器里没有赋值者**（§3.2）—— 宿主要自己赋（demo `Examples/Workflow/Jalium Trimmed/Demo/MainWindow.cs:64`），不赋就是「只看不动」。

---

## 八、入口：我要改 X 先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 画布平移 / 缩放 / 拖拽 / 插槽布局 / 网格标尺 | **不在适配器里** —— 活表面是模板产物 `workflow-tree-view`（`Src/Templates/VeloxDev.Jalium.Templates/working/content/workflow-tree-view/TemplateClass.cs`）与 demo `Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/TreeView.cs` |
| 视图池、每元素视图的创建/复用/回收 | `Attached/Workflow/ViewManager.cs`（挂点 `ViewPool.cs`） |
| 「item 类型 → 视图」的工厂契约 | `Attached/Workflow/IWorkflowTemplateSelector.cs`（11 行，替换 `DataTemplateSelector`） |
| 小地图外观与导航 | `Attached/Workflow/WorkflowMinimapOverlay.cs` |
| 线程、优先级、pacer、调度器 | `PlatformAdapters/` 那 5 个短文件 + `PlatformAdapters/UIThreadInspector.cs` |
| 让某个 Jalium 类型可动画 | `PlatformAdapters/Samplers/` + `PlatformAdapters/Interpolator.cs:14-23` 的登记表 |
| 流式 `.Property(...)` 支持哪些类型 | `PlatformAdapters/Transition.cs:34-137`（13 个重载） |
| 包结构、TFM、双轨引用 | `VeloxDev.Jalium.csproj` |
| **宿主怎么接上适配器剩下那三类** | 池化：`Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/TreeView.cs:114-115`；小地图：`…/MainWindow.cs:61-68`；缩放全在窗口侧（`…/MainWindow.cs:146-155` 滚轮、`:165-229` `ZoomBy`） |

---

## 九、这份文件没写的东西

- 四个文件各自的成员列表、继承树、文件清单 —— IDE 里一按就有。
- 七个角色各自的职责、`PART_*` 约定、契约本身 —— `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；**Jalium 侧六个行为已删除，这些约定只剩其它六家在实现**。
- 这一家的平台硬限制与刻意背离（`Visual.ShouldRenderChild` 按 `RenderSize` 盒裁剪 ⇒ 自盒化；`IsVirtual` 跳过 + `PortCenter` 反查；纯模型数学；`_zoomPin` 的来龙去脉）—— `memory/modules/WorkflowSystem/adapters/jalium.md`。**注意这些落点现在大多在 demo/模板，不在适配器。**
- 过渡轴侧采样器逐一分析、`TransformSampler` 的端点短路与草稿实例类型守卫、`BrushSampler` 为什么只能混一个代表色 —— `memory/modules/TransitionSystem/adapters/jalium.md`。
- 模板侧：七个条目产出什么形状、`static class` 产物、12 个空转符号、标尺 36 被复制的三处、`RulerReserve` —— `memory/modules/Templates/adapters/jalium.md`；包结构与跨平台族划分在 `memory/modules/Templates/architecture.md`。
- 主题切换的完整流向 —— **这一家没有这条线**（§一），见 `memory/modules/DynamicTheme/architecture.md`。
- 人面向的「怎么用这套模板搭一个 Jalium 工作流视图」—— `skills/veloxdev-create-workflow/references/gui/jalium.md`。
