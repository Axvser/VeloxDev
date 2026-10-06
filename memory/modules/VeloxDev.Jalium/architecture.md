# VeloxDev.Jalium — 架构

> ⚠ **2026-10-05：Jalium 适配器已整体转成标记驱动（`.jalxaml`），与 WPF 逐行同形。**
> `WorkflowTreeView` / 三个 `*Attachment` / `WorkflowPortGeometry` / `WorkflowPortLayout` /
> `IWorkflowTemplateSelector` / `WorkflowTemplateSelector` / `WorkflowNodeView` / `WorkflowSlotView` /
> `WorkflowLinkView` 这些类型都不存在；本文下面凡提这些名字的段落，落点以
> [WorkflowSystem/adapters/jalium.md](../../WorkflowSystem/adapters/jalium.md) §一 与 §〇 为准。

> 代码：`Src/Adapters/VeloxDev.Jalium/`。**27 个 .cs、4411 行**
> （`Attached/Workflow/` 9 个 3435 行，最大 `WorkflowSurfaceBehavior.cs` 1353、`WorkflowSlotLayoutBehavior.cs` 538、`WorkflowMinimapOverlay.cs` 493、`WorkflowNodeDragBehavior.cs` 254；
> `PlatformAdapters/` 8 个 325 行，最大 `Transition.cs` 154；`PlatformAdapters/Samplers/` 9 个 648 行，最大 `TransformSampler.cs` 274、`BrushSampler.cs` 134；顶层 `GlobalUsings.cs` 3 行）。
> **计数写法**：`git ls-files 'Src/Adapters/VeloxDev.Jalium/*.cs' 'Src/Adapters/VeloxDev.Jalium/**/*.cs' | sort -u`。`git ls-files` 的 glob `'.../**/*.cs'` 只匹配**子目录**里的文件（七家都漏掉本级 `GlobalUsings.cs`）。
>
> 本文只写「读完这 27 个文件才知道的东西」。类型清单、成员表、继承树请看 IDE。
>
> **本模块没有 `adapters/` 子目录，也不该有**：模块名本身就是一个平台，不存在平台轴。它在两条轴上的平台差异分别落在
> `memory/modules/TransitionSystem/adapters/jalium.md`、`memory/modules/WorkflowSystem/adapters/jalium.md`、`memory/modules/Templates/adapters/jalium.md`，本文**指路不抄**。

---

## 一、一个项目、两条轴（不是三条）

这个项目叫「Jalium 适配器」，但它只实现了 **两个** Core 模块的平台侧契约，彼此**在程序集内互不引用**：

| 轴 | Core 契约 | 本项目落点 | 平台差异记在哪 |
|---|---|---|---|
| TransitionSystem | 宿主 / 解释器 / 调度器 / 帧 pacer / 采样器 | `PlatformAdapters/`（8 个类型 + `Samplers/` 9 个） | `memory/modules/TransitionSystem/adapters/jalium.md` |
| WorkflowSystem | 七个视图角色 | `Attached/Workflow/`（9 文件：表面/插槽/拖拽/连线手势/模型事件五个附着行为 + 连线自盒化助手 + 小地图 + 视图池两件套；见 §三） | `memory/modules/WorkflowSystem/adapters/jalium.md` |

**第三条轴（DynamicTheme）在这一家是空的，这是它与 WPF / Avalonia / WinUI / MAUI / WinForms / Razor 的六家差异里最容易被忽略的一条**：

- 全模块**没有 `ThemeValueConverters.cs`**；`IThemeValueConverter` 在本项目零命中。
- Jalium 也**没有主题 demo**：`Examples/Theme/` 下只有 `Avalonia` / `Avalonia Trimmed` / `WPF` / `WPF Trimmed`。
- 所以 `Interpolator.CreateScheduler` 在本仓库**是一条无人的路**：它的唯一调用者是 Core `Src/Core/VeloxDev.Core/DynamicTheme/ThemeManager.cs`，而 `ThemeManager.SetPlatformInterpolator` 在本仓库只有 4 个非测试调用点，全是 Avalonia / WPF。
  ⇒ **别把它当成过渡轴的优先级选择来读**：过渡轴走的是 `Transition<T>` 的编译期类型实参，`CreateScheduler` 只是 DynamicTheme 的工厂接缝（休眠，但留着是对的）。

**两条轴的命名空间都寄生在 Core 上**：`VeloxDev.TransitionSystem`（`PlatformAdapters/*`）与 `VeloxDev.WorkflowSystem.AttachedBehaviors`（`Attached/Workflow/*`）。唯一例外是 9 个采样器，它们在 `VeloxDev.Adapters.NativeSamplers`（`PlatformAdapters/Samplers/PointSampler.cs:3`）—— 与另外六家同名同命名空间，所以**同时引用两家适配器的工程必然 CS0433**。

**目录名与轴不对齐，别按目录推契约**：`Attached/` 只有 WorkflowSystem 一条轴。

---

## 二、`PlatformAdapters/`：注册与工厂

### 2.1 两段静态链，都不需要宿主动手

| 要注册的东西 | 注册点 | 什么时候真的发生 |
|---|---|---|
| 9 个采样器（10 条登记） | `Interpolator` 的**静态构造**（`PlatformAdapters/Interpolator.cs:13-22`） | 第一次构造 `Transition<T>` 时：Core 的字段初始化 `protected TInterpolatorCore interpolator = new();` |
| 采样器所在的宿主 / 解释器 / 优先级 | `TransitionScheduler` 的类型实参（`PlatformAdapters/TransitionScheduler.cs`） | 同上，全部编译期写死 |
| DynamicTheme 的调度器工厂 | `Interpolator.CreateScheduler`（`Interpolator.cs:26-29`） | 宿主显式调 `SetPlatformInterpolator` —— **本仓库无人调**（§一） |

**没有程序集级入口，也没有 `Initialize()`**：全模块 `ModuleInitializer` / `[assembly:` 零命中，唯一的静态构造就是 `Interpolator.cs:12`。⇒ 只用 `Transition<T>` 的宿主什么都不用做。

### 2.2 十行登记里的两条非常规写法

```csharp
// Interpolator.cs:12 的注释：按精确类型查找 —— Brush 与 SolidColorBrush 都要注册
RegisterInterpolator(typeof(Brush), new BrushSampler());            // :19
RegisterInterpolator(typeof(SolidColorBrush), new BrushSampler());  // :20 —— 同一个实例，两条登记
```

两条只在读这个文件时才知道的推论：

1. **`BrushSampler` 是唯一被登记两次的采样器**（`:19-20` 共享一个实例）。Core 的解析次序是「精确类型 → 最近基类 → 接口按名」，`SolidColorBrush` 的最近基类本来就是 `Brush`，所以 `:20` 严格来说是**冗余**的 —— 它防的是「宿主把属性声明成 `SolidColorBrush` 且基类回溯在某些路径下不生效」，代价为零。**别以为漏登记的族类型会被基类接住而放心不加**：这一家的姿态是所有要动画的类型都显式登记。
2. **`Transform3D` 用了全限定名**（`:22`）—— 因为 `Jalium.UI.Media` 下同时有 `Transform`（2D）与 `Media3D.Transform3D`。这一条与 WPF 的 `Transform` 家族登记是两个不同的形状：WPF 登记 `typeof(Transform)` 一登记就接住整族，Jalium 两个都显式登记（`Transform` + `Transform3D`），**没有登记 `TransformGroup` / `TranslateTransform` 这些子类**。

### 2.3 这家的三个平台类型是它独有的

`TransitionEffect.Priority` 硬编码为 `DispatcherPriority.Render`（`TransitionEffect.cs:8`），`UIThreadInspector.InternalPriority` 是 `Send`（`UIThreadInspector.cs:34`）—— 两个优先级**语义不同、刻意不同**：前者是动画帧写入用的优先级，后者是「让回调插队到最前」用的。改成同一个会静默改变观感（帧晚一档）。
`UIThreadInspector.ThreadFor` 的兜底链是**四级**（`UIThreadInspector.cs:15-19`）：target 自己的 `Dispatcher` → `Application.Current?.Dispatcher` → `Dispatcher.FromThread(CurrentThread)` → `Dispatcher.MainDispatcher`；整块包在 `try` 里，落空退 `ThreadRef.None`（`:25`），后果只是这个目标**没有 pacer**。`PostCore`（`:37-42`）另有两道闸：拿不到 dispatcher 返回 false，`HasShutdownStarted`（`:40`）也返回 false。

`.Property(...)` 的**流式重载集不属于 Core**：Core 的 `TransitionCore<...>` 基类里一个 `Property` 都没有，13 个重载全写在各家适配器里（Jalium 13 个在 `PlatformAdapters/Transition.cs`；对照 WPF 28、Avalonia 30、WinForms 17）。⇒ **重载表的长度就是这一家「能被 `.Property(...)` 顺滑写出来的平台类型」的数量**，Jalium 的是：`Brush?`、`ICollection<Transform>`、`Transform3D?`、`Point`、`Rect`、`Thickness`、`CornerRadius`、`Size`、`Color`、`int`、`double`、`float`、`decimal`。注意 **`SolidColorBrush` 没有自己的重载**（虽然它被登记了）—— 声明成 `SolidColorBrush` 的属性靠泛型 `Property<TValue>` 走。

`Property(Expression<Func<T, Transform?>>, ICollection<Transform>)` 是**唯一带分支的重载**（`Transition.cs:54-72`）：`Count == 1` 时把**单个实例**直接 `SetValue`，否则才包 `TransformGroup`。注释 `:58-59` 写明了理由。⇒ **顺手「统一成总是包 TransformGroup」会让依赖具体变换子类类型的宿主静默失效**。

---

## 三、`Attached/Workflow/`：9 个文件，五个附着行为 + 自盒化助手 + 池 + 小地图

**2026-10-05 重构：适配器转成标记驱动，与 WPF 逐项对齐。** 表面不再是可继承的 `Canvas` 基类，
而是**挂在模板根上的附着行为**；节点/连线的样子由模板的 `.jalxaml` 决定。九个文件：

| 文件 | 行 | 角色 | 附着属性 |
|---|---|---|---|
| `WorkflowSurfaceBehavior.cs` | 1353 | **表面**（`static`）：按名字认领模板声明的部件、喂视口、平移、Ctrl+滚轮缩放、把指针与按键翻译进 Core 的输入路由、弹连线菜单、发布画布变换 | **9**：`IsEnabled` / `ScrollViewerName` / `CanvasName` / `GridDecoratorName` / `PointerPressSourceName` / `MinimapOverlayName` / `ZoomEnabled` / `CanvasTransform` / `LinkMenuKey` |
| `WorkflowSlotLayoutBehavior.cs` | 538 | **槽锚点的唯一写回点**：按 `SlotNames` / `SlotEnumeratorNames` 找控件、量中心、写 `slot.Anchor`（`SlotAnchorFromVisualCenter`） | 6：`IsEnabled` / `SlotNames` / `SlotEnumeratorNames` / `CoordinateHostName` / `CoordinateHostType` / `State` |
| `WorkflowMinimapOverlay.cs` | 493 | 小地图，`FrameworkElement, IWorkflowMinimapOverlay`；22 个 DP（尺寸、调色板、视口），`ScrollViewerName` 由模板给 | — |
| `ViewManager.cs` | 341 | 视图池本体：按类型分桶、`DataTemplateSelector` + 三级回退、每批 3 个走 `DispatcherPriority.Background` | — |
| `WorkflowNodeDragBehavior.cs` | 254 | 标题栏拖拽 → `node.MoveCommand` | 4 |
| `WorkflowSlotConnectionBehavior.cs` | 152 | 端口手势：按下送出、松开接收，**并收拾失败**（松回自己、落空 ⇒ 收回虚拟连线） | 1：`IsEnabled` |
| `WorkflowEvents.cs` | 148 | 模型事件转给宿主 | 3：`Node` / `Slot` / `Tree` |
| `WorkflowLinkBounds.cs` | 84 | **自盒化助手**（`static`）：把连线元素的布局盒挪到曲线包围盒上（`BoxPad = 6`）—— 渲染器按 `RenderSize` 盒裁剪，画到盒外会被**静默丢弃** | — |
| `ViewPool.cs` | 72 | 池的挂点：`Panel` 上的 `ItemsSource` / `TemplateSelector` | 2 |

> 9 个文件都在命名空间 `VeloxDev.WorkflowSystem.AttachedBehaviors`（与六家同名同形），所以**同时引用两家适配器必然 CS0433**。

### 3.1 表面与「七个角色」的边界

`WorkflowSurfaceBehavior` 是**唯一持有状态**的角色：它按名字认领模板声明的部件，把可见集喂给 `ViewPool`，
另有一整套交互与记账：

- 部件解析（`FindName` 取 `PART_ScrollViewer` / `PART_Canvas` / `PART_GridDecorator` / `PART_SurfaceBorder` / `PART_MinimapOverlay`）；
- 视口记账 `UpdateViewport`（未测量时退回整张画布；标尺厚度喂给 `SetVirtualizeInset`）；
- 缩放钉 `ZoomPin` + `ZoomPinLifetimeMs = 250` —— **这套深缩放守卫在适配器里，由行为内部提交，宿主不参与**；
- 平移挂在**具名按下源的预览相**上（挂宿主冒泡相收不到：滚动视口那层会把 `MouseDown` 标成已处理）；
- 缩放挂 `ScrollViewer.PreviewMouseWheel` + Ctrl 门控；
- 指针与按键翻译进 `WorkflowInput.For(tree).Route(...)`，右键菜单从输入路由里弹。

其余角色都只伺候这一个表面：节点/连线视图由 `ViewPool` 池化到画布，网格装饰器**由模板提供**
（适配器只按 `IWorkflowGridDecorator` 收），选择器是模板的 `DataTemplateSelector`。
**改「画布怎么动」先打开 `WorkflowSurfaceBehavior.cs`；改「卡片/连线长什么样」在模板的 `.jalxaml` 里。**

### 3.2 模板与 Trimmed demo 是「标记 + 薄 code-behind」

模板产物与 Trimmed demo 同形：`workflow-{tree,node,slot,link}-view` 各是一对 `.jalxaml` + `.jalxaml.cs`，
标记里声明部件、写绑定、挂附着行为，code-behind 只剩 `InitializeComponent`（node 9 行、tree 25 行）。
`workflow-grid-decorator`（449 行）、`workflow-minimap-overlay`（24）、`workflow-template-selector`（49）
仍是 `.cs`。逐个角色的行数与形状见 `memory/modules/Templates/adapters/jalium.md`。

### 3.3 小地图的 `ScrollViewer` 现在由模板按名字接上

`WorkflowMinimapOverlay.ScrollViewer` 仍是普通自动属性（`WorkflowMinimapOverlay.cs:55`），`NavigateToWorld`（`:406`）要求 `_tree` 与 `ScrollViewer` **同时非空**。但**适配器现在会赋值**：`ScrollViewerName` DP（`:160`）+ `ResolveScrollViewer()`（`:182-188`，`FindName(name) is ScrollViewer viewer`，`Loaded` 时再兜一次）⇒ 模板只要写 `ScrollViewerName="PART_ScrollViewer"`（`workflow-tree-view/TemplateClass.jalxaml:66`）就接上，宿主不必自己赋 —— demo 宿主现在是标记，没有 `ScrollViewer = viewer` 这一句。（非 Trimmed demo `Examples/Workflow/Jalium/` 仍走 `Minimap(viewer)` 构造器那条路。）

### 3.4 画布变换现在有一条通道：`CanvasTransform` 附着属性

`ViewManager` / `ViewPool` 没有 `UpdateRenderTransforms` 这对镜像方法（`git grep -n UpdateRenderTransforms -- Src Examples` 零命中）。画布变换的通道是附着属性：表面把世界位移作为 `WorkflowSurfaceBehavior.CanvasTransform` 发布在**宿主**上（`WorkflowSurfaceBehavior.cs:166-173` 注册，`ApplyLayout` `:756-768` 写入），tree-view 模板的 `UserControl` 把同一个 DP **用一个 CLR 属性再暴露一次**（`workflow-tree-view/TemplateClass.jalxaml.cs:19-22`）——因为本家的绑定**读不到括号路径**（`(Canvas.Left)` 也不行，见 §〇）——节点/连线模板于是绑 `RenderTransform="{Binding CanvasTransform, RelativeSource={RelativeSource AncestorType=UserControl}}"`（`workflow-tree-view/TemplateClass.jalxaml:27`、`:35`）。**同一个 DP 对象、同一个值、同一种通知，只是换了个名字给绑定看得见。**

---

## 四、结构性根因：WPF 怎么做，这家就怎么做

**2026-10-05 用户定：以 WPF 为基准。** Jalium 有完整的 `.jalxaml` 工具链，与另外六家同形：

| | 适配器 | 模板与 Trimmed demo |
|---|---|---|
| 表面 | `WorkflowSurfaceBehavior`（附着行为，按名字认领部件） | 标记里挂 `WorkflowSurfaceBehavior.*`、给部件起 `x:Name` |
| 池化 | `ViewPool` + `ViewManager`（`DataTemplateSelector` 三级回退） | 标记里绑 `ViewPool.ItemsSource` / `TemplateSelector` |
| 小地图 | `WorkflowMinimapOverlay`（可继承） | 薄子类设四个颜色；`ScrollViewerName` 接上（§3.3） |
| 节点 | `WorkflowNodeDragBehavior` + `WorkflowSlotLayoutBehavior` | 卡片是 `.jalxaml` 的 `UserControl` |
| 连线 | `WorkflowLinkBounds`（自盒化助手） | 自己的控件 + 自己的 `OnRender` |
| 网格 | 只按 `IWorkflowGridDecorator` 收 | **整个渲染器在模板里**（`sealed class : Grid`） |
| 端口 | `WorkflowSlotLayoutBehavior` 量测后写回 `slot.Anchor` | 标记里声明槽控件（`PART_InputSlot` / `PART_OutputSlots`） |

**唯一不得不换一种写法的是画布变换**：WPF 的模板用 `Path=(behaviors:…Transform)` 读附着属性，
而本家的绑定**读不到括号路径**（§〇 实测：连 `(Canvas.Left)` 也不行）—— 于是 tree-view 模板的类把那
同一个 DP **用一个 CLR 属性再暴露一次**，模板改绑那个名字。同一个 DP 对象、同一个值、同一种通知（§3.4）。

⇒ 剩下与 WPF 的差异只有**实测挡住**的那几条（括号路径、渲染器盒裁剪、没有 `LayoutUpdated`），
清单与依据在 [`WorkflowSystem/adapters/jalium.md` §三](../WorkflowSystem/adapters/jalium.md)。

## 五、仓库里到底被消费了什么（实测）

引用 `VeloxDev.Jalium` **项目**的工程只有 4 个（`git grep -ln 'VeloxDev.Jalium.csproj' -- Examples Src`）：`Examples/Transition/Jalium/Demo/`、`Examples/Transition/AUTO TEST/Samplers/VeloxDev.SamplerTest.csproj`、`Examples/Workflow/Jalium/Demo/`、`Examples/Workflow/Jalium Trimmed/Demo/`。逐个核它们真正用到的类型：

| 工程 | 用到的适配器类型 |
|---|---|
| `Examples/Transition/Jalium/Demo/` | 只用到 `PlatformAdapters/` 的 `TransitionEffect` 与 `Transition<T>`（`MainWindow.cs` 一族）；**`Interpolator` / `UIThreadInspector` 名字零命中**（`TransitionScheduler` **不是** —— `MainWindow.cs:928` 直接调了 `TransitionScheduler.TryGetNoMutualScheduler`）—— 前两个只在 `Transition<T>` 的类型实参里被间接使用 |
| `Examples/Transition/AUTO TEST/Samplers/` | 只走**反射**（`JaliumEntries.cs`、`SamplerCoverageTests.cs` 的 `ExpectedAdapterAssemblies`） |
| `Examples/Workflow/Jalium Trimmed/Demo/` | 附着行为挂了**四个**（`WorkflowSurfaceBehavior` 最多，另有 `WorkflowSlotLayoutBehavior`、`WorkflowNodeDragBehavior`、`WorkflowSlotConnectionBehavior`）—— 第五个 `WorkflowEvents` 在这个 demo 的标记里**没有引用**，加 `WorkflowLinkBounds`、`WorkflowMinimapOverlay`，以及标记里绑的 `ViewPool.ItemsSource`/`TemplateSelector`；`GridDecorator` 实现 `IWorkflowGridDecorator` |
| `Examples/Workflow/Jalium/Demo/` | 只有 `WorkflowMinimapOverlay`（作为基类）；**不用**池化，也不派生其它基类（它自己写 `NodeEditorSurface`） |

**哪些类型在全仓库零消费者**：**选择器的 `SlotTemplate` / `TreeTemplate`**（模板 `workflow-template-selector/TemplateClass.cs`）—— 本仓库所有宿主只给节点与连线两项赋值（`workflow-tree-view/TemplateClass.jalxaml` 只写 `NodeTemplate=` 与 `LinkTemplate=`），插槽与树 item 从不进池；若进了而模板未设，`SelectTemplate` 会抛 `InvalidOperationException`。

⇒ **这份适配器的「已用面积」是：`PlatformAdapters/` 的过渡轴（真被跑），加上整套 Workflow 基类（Trimmed demo/模板真派生）。** 改任何一处之前先确认那条路上真的有调用者。

---

## 六、`GlobalUsings.cs` 与 `VeloxDev.Jalium.csproj`

**`GlobalUsings.cs` 三行，七个适配器逐字相同**（`global using` `VeloxDev.TransitionSystem` / `VeloxDev.TransitionSystem.Abstractions` / `VeloxDev.Threading`）：

- 这就是为什么 `PlatformAdapters/` 的文件除 `Jalium.UI*` 之外几乎不写 `using`，却能直接用 `InterpolatorCore`、`ISampler`。
- **它不含 `VeloxDev.WorkflowSystem`** ⇒ `Attached/Workflow/` 里需要 Core 契约的文件各自写 `using VeloxDev.WorkflowSystem;`。
- **`global using` 是编译期的，不随引用传给消费者**：宿主要用 `Transition<T>` 仍得自己写 `using VeloxDev.TransitionSystem;`。

**csproj 里影响代码本身的条件**（`VeloxDev.Jalium.csproj`）：

| 事实 | 行 | 后果 |
|---|---|---|
| `<TargetFramework>net10.0</TargetFramework>`（**单 TFM、不带平台后缀**） | `:7` | 七家里**只有这家**是单 TFM 且不带 `-windows`（Razor 已改成多 TFM `net6.0;net8.0`，仍不带 `-windows`）。`:4-6` 的注释把理由写明了：适配器只用跨平台核心，**不用 `net10.0-windows` 的 `Jalium.UI.Desktop` 入口包**，所以能同时服务 Windows / Linux / Android |
| 唯一的 Jalium 引用是 `Jalium.UI.Controls 26.10.9` | `:34` | `:31-33` 的注释：这是**能提供 `Canvas`/`ScrollViewer`/`Border`/`Control` + `DrawingContext`/`Geometry`/`FormattedText` 的**最低**平台中立包。**别名包 `Jalium.UI.Desktop` 由消费 demo 自己引** ⇒ 适配器与 demo 的包版本可以不同步 |
| Debug → `ProjectReference`（`:29`）／非 Debug → `PackageReference VeloxDev.Core 10.0.0`（`:30`） | — | 与另外六家同形的双轨；两条同时生效会报重复成员 |
| `NoWarn` 写成**一条** `1573;1591`（`:14`） | — | 与 WPF 现状同形。**注意**：`8605` / `8604`（DP 的 CLR 包装拆箱）在这个集合里没有触发点 —— 模板里值类型 DP 走泛型 `Read<T>`，不注册任何 DP 的 CLR 包装 |

---

## 七、陷阱（带依据）

1. **`ViewPool` 的 `Unloaded` 一次性退订会把画布永久留在空白态。** `panel.Unloaded += (_, _) => manager.Dispose();` 只在 manager **首次创建**时订阅一次（`ViewPool.cs:61`），而 `Dispose()` → `Detach()` → `ClearAllViews()` 会把所有视图 `Collapsed` + `DataContext = null`（`ViewManager.cs:76`/`:63`/`:255`），`_active` 也置空。**表面移出树再放回去不会重新触发 `OnChanged`**（那只在附着属性变化时跑）⇒ 空白画布，无异常。
2. **重设 `ItemsSource` 或 `TemplateSelector` 是「全拆重建」而不是增量对齐。** 两个 DP 共用同一个 `OnChanged`（`ViewPool.cs:45`），而 `Attach` 第一行就是 `Detach()`（`ViewManager.cs:45`）⇒ 只要改其中一个，所有池内视图都会 `Collapsed` 后重挂。
3. **`ViewPool` 的两个 DP 必须同时非空才建 manager**（`ViewPool.cs:55-66`）；只给一个（哪怕先给了 `ItemsSource`）走的是 `else` 分支的 `existing.Detach()`（`:69`）。
4. **`ViewManager` 的池按 `item.GetType()` 键**，而同一个 item 被 `ReferenceEquals` 去重 ⇒ 同一集合里放两个引用相同的 item 只会得到一个视图。**`HideViewFor` 把 `DataContext` 置 `null` 后入池**（`:236-237`），复用时靠 `ApplyContext`（`:331`）重新赋。
5. **槽是按标记里的名字认领的**：`WorkflowSlotLayoutBehavior` 只认 `SlotNames` / `SlotEnumeratorNames` 列出的控件名（`FindName`），槽自己从控件的 `DataContext` 取。⇒ **标记里改掉 `x:Name` 而不改那两个字符串，端口就不会被量测**（`slot.Anchor` 保持旧值，命中与连线端点一起错位），且**不报错**。
6. **删视图时同时置 `Collapsed` 与 `DataContext = null`**（`ViewManager.cs:236-237`）⇒ 视图里读 `DataContext` 的代码（含连线视图的守卫）在池化回收后会看到 `null`。
7. **csproj 的两条独有设定会咬人**：单目标 `net10.0` 无平台后缀（`:7`）⇒ 只能引用 `Jalium.UI.Controls` 这个平台中性包；`NoWarn` 为 `1573;1591`（`:14`）。

---

## 八、入口：我要改 X 先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 画布平移 / 缩放 / 指针路由 / 命中 / 右键菜单 | `Attached/Workflow/WorkflowSurfaceBehavior.cs`（表面行为，`static` 类 + 9 个附着属性） |
| 节点拖拽 | `Attached/Workflow/WorkflowNodeDragBehavior.cs` |
| 插槽连接手势 | `Attached/Workflow/WorkflowSlotConnectionBehavior.cs` |
| 槽的锚点（量测写回 `slot.Anchor`） | `Attached/Workflow/WorkflowSlotLayoutBehavior.cs` |
| 模型事件转接（node / slot / tree sink） | `Attached/Workflow/WorkflowEvents.cs`（转发在 Core `WorkflowEventRelay`） |
| 网格线与标尺的数学 / 刻度 / 标签 | **模板** `Src/Templates/VeloxDev.Jalium.Templates/working/content/workflow-grid-decorator/TemplateClass.cs`（`sealed class : Grid, IWorkflowGridDecorator`，整个渲染器都在里面） |
| 卡片长什么样 | **模板** `workflow-node-view/TemplateClass.jalxaml`（`UserControl` + `WorkflowSlotLayoutBehavior` / `WorkflowNodeDragBehavior`） |
| 连线的几何与自盒化 | 自盒化助手 `Attached/Workflow/WorkflowLinkBounds.cs` + **模板** `workflow-link-view/TemplateClass.jalxaml.cs`（`Refresh` 摆盒、`BuildCurve` 烘焙、`OnRender` 画） |
| 端口在哪、怎么命中 | 适配器 `WorkflowSlotLayoutBehavior.cs`（量测写回 `slot.Anchor`）+ Core `WorkflowSurfaceMath` |
| 视图池、每元素视图的创建/复用/回收 | `Attached/Workflow/ViewManager.cs`（挂点 `ViewPool.cs`；`DataTemplate` 由模板产出） |
| 「item 类型 → 视图」的模板分派 | **模板** `workflow-template-selector/TemplateClass.cs`（`sealed class : DataTemplateSelector`）+ `ViewPool.TemplateSelector` |
| 小地图外观与导航 | `Attached/Workflow/WorkflowMinimapOverlay.cs`（模板只是 `MinimapOverlay` 薄派生设四色） |
| 线程、优先级、pacer、调度器 | `PlatformAdapters/` 那 8 个短文件 + `PlatformAdapters/UIThreadInspector.cs` |
| 让某个 Jalium 类型可动画 | `PlatformAdapters/Samplers/` + `PlatformAdapters/Interpolator.cs:13-22` 的登记表 |
| 流式 `.Property(...)` 支持哪些类型 | `PlatformAdapters/Transition.cs`（13 个重载） |
| 包结构、TFM、双轨引用 | `VeloxDev.Jalium.csproj` |
| **宿主怎么接上适配器** | 纯标记：表面在 `UserControl` 根写 `behaviors:WorkflowSurfaceBehavior.*`（具名部件 + `ZoomEnabled` + `LinkMenuKey`）、`ViewPool.ItemsSource/TemplateSelector` 给 `PART_Canvas`、小地图 `ScrollViewerName="PART_ScrollViewer"`。缩放与视口由表面自己管 —— **不再有** `AttachScrollViewer` / `SetTree` / `NotifyZoomCommitted` 这些宿主调用 |

---

## 九、这份文件没写的东西

- 9 个文件各自的成员列表、继承树、文件清单 —— IDE 里一按就有。
- 七个角色各自的职责、`PART_*` 约定、契约本身 —— `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；**Jalium 侧的角色机制现在都在适配器附着行为里（`Attached/Workflow/`），模板是标记 + 薄派生**。
- 这一家的平台硬限制与刻意背离（`Visual.ShouldRenderChild` 按 `RenderSize` 盒裁剪 ⇒ `WorkflowLinkBounds` 自盒化；`IsVirtualLink` 跳过拖拽预览；`CanvasTransform` 通道与括号路径限制；`ZoomPin` 的来龙去脉）—— `memory/modules/WorkflowSystem/adapters/jalium.md`。
- 过渡轴侧采样器逐一分析、`TransformSampler` 的端点短路与草稿实例类型守卫、`BrushSampler` 为什么只能混一个代表色 —— `memory/modules/TransitionSystem/adapters/jalium.md`。
- 模板侧：七个条目产出什么形状（四个 `.jalxaml` + 三个 `.cs`）、0 个空转符号、标尺 36 现在单一来源在**模板**的 `workflow-grid-decorator` —— `memory/modules/Templates/adapters/jalium.md`。
- 主题切换的完整流向 —— **这一家没有这条线**（§一），见 `memory/modules/DynamicTheme/architecture.md`。
- 人面向的「怎么用这套模板搭一个 Jalium 工作流视图」—— `skills/veloxdev-create-workflow/references/gui/jalium.md`。
