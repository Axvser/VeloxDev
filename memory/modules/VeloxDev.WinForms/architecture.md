# VeloxDev.WinForms — 架构

> 代码：`Src/Adapters/VeloxDev.WinForms/` —— `WorkflowSystem/AttachedBehaviors/`（`WorkflowSurfaceBehavior`、通用视觉 `ViewPool`、`WorkflowMinimapOverlay`、`WorkflowEvents`、三个视图附着行为）、`WorkflowSystem/ViewHosting/`（Core 视图管线的 WinForms 闭包）、`WorkflowSystem/Platform/`（Win32/DPI 辅助）、视觉 XAML 运行时已移到 Easel 子模块（`Easel/Src/Easel/`）。
> 包版本见 csproj；生成器独立在 `Src/Generators/Easel.Generator/`。
> **单一 XAML 后端**：控件是 `Xaml.Visuals.Controls` 下的 `VisualElement`，`VisualHost` 是唯一 HWND；工作流表面是 `VisualHost` 上的附着行为。每个可见 item 由 `ViewPool` 经 Core 池物化为一个视觉元素，没有第二个 HWND。`Xaml.Controls` 只留共享原语。
>
> **本模块没有 `adapters/` 子目录，也不该有**：模块名本身就是一个平台。它在三条轴上的平台差异分别落在
> `memory/modules/WorkflowSystem/adapters/winforms.md`、`memory/modules/TransitionSystem/adapters/winforms.md`、`memory/modules/Templates/adapters/winforms.md`，本文**指路不抄**。

## 一、一个项目、三条轴、外加一层基类

| 轴 | Core 契约 | 本项目落点 | 平台差异记在哪 |
|---|---|---|---|
| WorkflowSystem | 七个视图角色 + 模型事件 | `WorkflowSystem/AttachedBehaviors/` | `WorkflowSystem/adapters/winforms.md` |
| TransitionSystem | 宿主 / 解释器 / 调度器 / 帧 pacer / 采样器 | `TransitionSystem/`（平台类型 + 采样器） | `TransitionSystem/adapters/winforms.md` |
| DynamicTheme | `IThemeValueConverter` | `DynamicTheme/ThemeValueConverters.cs` | DynamicTheme 模块记忆 |

三个只在读这个目录时才会发现的结构事实：

1. **三条轴互不引用，但没有任何编译期阻挡。** `GlobalUsings.cs` 把 `VeloxDev.TransitionSystem` 灌进每个文件；「在这家写一条跨轴调用」不会被拦下，七家都维持零引用。
2. **命名空间全部寄生在 Core 上。** 唯一例外的采样器命名空间是 `VeloxDev.Adapters.NativeSamplers`（`TransitionSystem/Samplers/PaddingSampler.cs`），与 Core 自己的 `VeloxDev.TransitionSystem.NativeSamplers` 不同。
3. **`VeloxDev.WorkflowSystem.AttachedBehaviors` 在 Core 里不存在**：七家各声明一份同名命名空间。XAML 平台靠 `assembly=` 消歧，C# 宿主只能写全名或别名。

**DynamicTheme 这条轴在这家没有消费者**：`Examples/Theme/` 下只有 Avalonia/WPF；这些转换器没有 demo/测试碰过。

## 二、表面给什么

表面是 `WorkflowSurfaceBehavior`。宿主是 `VisualHost`，`Root` 是视觉树；行为按 `VisualElement.Name` 解析 `PART_Canvas` / `PART_ScrollViewer` / `PART_GridDecorator` / `PART_MinimapOverlay` 等部件，接管 pan/zoom、输入、布局、视口持久化与 teardown。相机矩阵写在 `PART_Canvas.Transform`；`ViewPool` 在 `PART_Canvas` 上按 `Helper.VisibleItems` 建节点/插槽/连线的视觉元素，模板自己绑定模型几何与绘制。

| 类型 | 形态 | 宿主/模板做什么 | 关键成员 |
|---|---|---|---|
| `WorkflowSurfaceBehavior` | 附着行为（`RegisterAttached`） | `VisualHost` 上设 `IsEnabled`/`ZoomEnabled`/`VisualTemplateSelector`/`VisualTemplateSelectorKey` 与部件 `*Name` | `EnsureParts` / `EnsurePool` / `ApplyCamera` / `WorkflowTree` / `LinkMenuKey`（`LinkMenuBuilder` 为旧回落） / `Refresh` / 相机 API |
| `ViewPool` | 附着行为（薄壳，与工作流无关） | `PART_Canvas` 上设 `ItemsSource`/`TemplateSelector`；任一变化或 `Refresh` 重建 `ViewSourcePipeline` | 一个 item 一个视觉子元素；按首次挂载时的模板 `Panel.ZIndex` 插位（连线在节点前），回收交给 Core，`Changed` 每批一次 |
| `ViewSourcePipeline` + `ViewHosting/` 九项 | Core `ViewSourcePipelineCore<Panel,VisualElement,IWorkflowComponent,NonPriority,…>` 的闭包 | 工厂走 `VisualTemplateSelector`；容器留在画布上隐藏复用；调度器有句柄用 `BeginInvoke`、无句柄同步跑 | `ViewFactory` / `ViewContainer` / `ViewScheduler` / `ViewArranger` / `ViewMeasureHost` / `ViewDiagnostics` / `ViewLayerPolicy` / `ViewLinkLayer` |
| `WorkflowSlotLayoutBehavior` / `WorkflowNodeDragBehavior` / `WorkflowSlotConnectionBehavior` | `VisualElement` 附着行为 | 节点/插槽模板在指定元素上开 `IsEnabled` 并给 `CoordinateHostName` | 槽测量写 `slot.Anchor`、标题行拖动、按下-释放在接收口连接 |
| `WorkflowMinimapOverlay` | `VisualElement, IWorkflowMinimapOverlay, IWorkflowMinimapScrollSource` | 模板派生，只给调色板；与 `PART_SurfaceBorder` 同级进 tree 根 `Grid` | 调色板、绘制钩子、推入偏移（setter 标脏）、指针喂给输入层与捕获丢失收尾；映射/放置/拖拽算术在 Core `MinimapPipelineCore` |
| `WorkflowInfoOverlay` | `VisualElement` | 模板直接实例化；绑定 `PART_GridDecorator` 的偏移 | 六行 HUD，视口取 `PART_SurfaceBorder.Bounds` |
| `VisualTemplateSelector` | 抽象基类 | 派生类按 item 类型返回 `NodeView`/`SlotView`/`LinkView` | `SelectTemplate(object?)` → `VisualElement?` |
| `IWorkflowGridDecorator` / `IWorkflowMinimapOverlay` | Core 契约 | 网格装饰器/小地图是模板角色，适配器只留契约 | 表面每次刷新推偏移 |
| `NativeWindowStyleHelper` / `WorkflowDpi` | 平台辅助 | `WS_CLIPCHILDREN`/`WS_EX_COMPOSITED`、`BeginRedraw`/`EndRedraw`、DPI 换算 | 见 `WorkflowSystem/adapters/winforms.md` |

**支撑件**：item 物化归通用视觉 `ViewPool` + `WorkflowSystem/ViewHosting/`（Core 池），定位归模板绑定（`Anchor`/`Size`/`Panel.ZIndex`），缩放归相机；没有 `IWorkflowSurfaceNodeView` 或 `ModelChangeRelay`。

## 三、宿主怎么把它接起来

- **XAML 侧有 DP/附加属性；适配器内部状态仍存在 `ConditionalWeakTable<Control, SurfaceState>`。** XAML 运行时由 `Xaml/DependencyProperty` 提供 DP，`WorkflowSurfaceBehavior` 用 `RegisterAttached` 暴露接线面。
- **名字仍是接线语言。** 部件解析 `FindVisualNamed` 递归遍历视觉子树、`Ordinal` 比较 `VisualElement.Name`，取第一个命中；找不到静默跳过。`x:Name` 同时写字段与 `VisualElement.Name`，模板写一次即可。
- **`Refresh(Control)` 是整树/部件变化后的重推入口**（不检查 `IsEnabled`）：重新解析部件、装池、`ApplyCamera` 把视口推给树与装饰器。
- **平移在 `WorkflowSurfaceBehavior.SetPan` 与指针处理里**；`ApplyCamera` 把 `zoom`/`pan`/`content` 写进 `PART_Canvas.Transform`，并把同一组偏移推给网格/小地图。
- **视图物化由 `ViewPool` + `WorkflowSystem/ViewHosting/` 做**：`EnsurePool` 把 `VisibleItems` 与 tree 选择器装到 `PART_Canvas`，`ViewSourcePipeline` 组合 Core 的池与九项层；工厂直接调 `VisualTemplateSelector`，回收桶、批大小、`Replace` 都归 Core（默认 `AdaptiveViewPoolPolicy`）。重建池期间 `state.RefreshingPool` 压住 `Changed`，`Refresh` 返回后统一扫一遍行为与连线样式。
- **小地图/HUD 在视觉树里**：表面把偏移推给 `WorkflowMinimapOverlay`/`WorkflowInfoOverlay`；拖小地图经 `IWorkflowMinimapScrollSource.ViewportScrollRequested` 反推视口。

## 四、`GlobalUsings.cs` 与 `VeloxDev.WinForms.csproj`

**`GlobalUsings.cs` 三行，与 WPF/WinUI 逐字相同**：`VeloxDev.TransitionSystem` / `VeloxDev.TransitionSystem.Abstractions` / `VeloxDev.Threading`。

- 这就是 `TransitionSystem/` 里不写 `using` 却能用 `InterpolatorCore`/`ISampler` 的原因。
- **它不含 `VeloxDev.WorkflowSystem`** ⇒ 需要 Core 契约的文件各自写 `using`。
- **`global using` 不随包/`ProjectReference` 传给消费者** —— 宿主要用 `Transition<T>` 仍得自己写 `using VeloxDev.TransitionSystem;`。

**csproj 里影响代码本身的条件**：

| 事实 | 元素 | 后果 |
|---|---|---|
| `<TargetFrameworks>netframework4.6.1;net5.0-windows;netcoreapp3.0;net8.0-windows` | `TargetFrameworks` | 七家里只有这家与 WPF 是这个四元组 |
| `UseWindowsForms` + `SuppressTfmSupportBuildWarnings` | 同名属性 | `netcoreapp3.0` 不带 `-windows`，靠后者压掉兼容告警 |
| `Nullable` + `ImplicitUsings` + `LangVersion latest` | 同名属性 | 全模块开可空 |
| `GeneratePackageOnBuild` + `<Version>` | 同名属性 | 与其余六家同；**没有** `GenerateDocumentationFile` |
| Debug → `ProjectReference`／非 Debug → `PackageReference VeloxDev.Core` | 两个引用项 | 与生成器那套双轨同形 |
| Debug → `ProjectReference Easel/Src/Easel`／非 Debug → `PackageReference WinForms.Easel` | Easel 引用 | 视觉运行时永远来自 Easel，不随本仓库发布；nuget.org 上 `Easel` 这个 ID 被保留，包名是 `WinForms.Easel` |
| 四个 TFM 一起编 | — | 新写的 BCL 调用必须在 netframework4.6.1 与 netcoreapp3.0 上都存在 |

**别用 `bin/`/`obj/` 目录名推支持的框架**：两个下游 demo 都是 `net10.0-windows`，解析到 `net8.0-windows` 那份资产。旧 TFM 的兼容层在 `Compat.cs`：IsExternalInit、UnconditionalSuppressMessageAttribute、Clamp/IsFinite/TickCount64。

## 五、陷阱（带依据）

1. **`WorkflowSurfaceBehavior` 的非 `VisualHost` 分支没有可渲染内容**：`Attach` 里 `if (host is not VisualHost) return;`；把行为挂到普通控件上不会报错，只是界面空着。宿主必须是 `VisualHost`。
2. **部件名找不到静默跳过**：`FindVisualNamed` 不抛不报，漏名时对应能力静默缺失。
3. **小地图的推入偏移必须自己标脏**：`WorkflowMinimapOverlay` 的 `ScrollOffsetX/Y`、`ContentOffsetX/Y`、`ViewportWidth/Height` 不在 `MinimapMetrics` 里，`Metrics` 的相等比较看不出变化；setter 漏掉 `pipeline.Invalidate()` 时相机在动、小地图视口块不动（`ApplyCamera` 的 `InvalidateVisual` 只重画、不重算映射）。
4. **`ThemeValueConverters.cs` 的类名与 `System.Drawing` 撞名**：用 GDI+ 那个必须全限定（`System.Drawing.ColorConverter` / `FontConverter`）。
5. **`NativeWindowStyleHelper` 里 `WS_CLIPCHILDREN` 与 `WS_EX_COMPOSITED` 同为 `0x02000000`**，却写向两个不同 index；不要按值合并。`CompositedMaxControlCount = 100` 是设计取舍。
6. **视觉相机方向**：上滚 `zoom *= 1.1`，下滚 `/1.1`，夹在 `0.1..10`。
7. **池化视图被移除后留在画布上**：`ViewContainer.Deactivate` 只清 `DataContext` 并设 `Collapsed`，`Detach` 不摘树 ⇒ `Canvas.Children.Count` 是「曾经物化过的视图数」，活动视图数看 `ViewPool.GetActiveViewCount`。管道销毁时 `ViewSourcePipeline.Dispose` 经 `ReleaseAll` 才真正移除并释放。
8. **`Panel.ZIndex` 不是活动排序**：`ViewContainer.Attach` 只在首次挂载时按当前 `Panel.ZIndex` 插位（连线 -1 落在节点前），之后模板改 `Anchor.Layer` 不会重排；`SetZOrder` 是空实现，避免覆盖模板绑定。
9. **Easel 动画由 Easel 直接驱动**：Easel 自带引擎（`Easel.Animation` 的 internal scheduler/run/interpreter/property/sampler），`VisualTransition` 与触发器动作直接驱动。`VeloxDev.WinForms` 只提供 workflow/MVVM 能力；不存在 `XamlAnimation` 模块、`EaselAnimationBootstrap` 或 `AnimationRuntime.Animator` 注册。
10. **`SurfaceName` 只选子树，不换宿主**：行为仍只认 `VisualHost`；输入/命中用宿主坐标，相机用表面内坐标，`SurfaceToWorld`/`WorldToSurface` 里的 `SurfaceOrigin` 负责两者之差。自己算坐标必须走这两个 API；名字写错时部件、输入、视口都按整棵根树解析（`WorkflowSurfaceBehavior.EnsureParts` / `SurfaceOrigin`）。
11. **Easel `Border` 默认 `Padding=12`**：把它当布局容器（外壳侧栏、标题条、列表行）而不显式写 `Padding="0"`，子内容会被挤小或裁掉（`Easel/Src/Easel/Visuals/Controls/CoreControls.cs` 的 `Border()` 构造）。
12. **`PointFromSurface` 必须从根往叶反解**：先减祖先 `Bounds` / 反祖先 `Transform`，最后才是元素自己。叶到根的顺序会在祖先有偏移时把偏移再缩放一次；嵌入在偏移工作台里的画布一缩放，槽锚点就漂移、连线错位。回归 `PointFromSurface_UndoesAncestorOffsetsBeforeTheChildTransform`（`Easel/Src/Test/Unit/Easel.Test/VisualBackendTests.cs`）。

## 六、入口：我要改 X 先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 表面：部件解析 / 平移 / 视口持久化 / 小地图接线 | `WorkflowSystem/AttachedBehaviors/WorkflowSurfaceBehavior.cs` |
| 相机矩阵、输入路由、世界↔表面、缩放、连线样式 | `WorkflowSystem/AttachedBehaviors/WorkflowSurfaceBehavior.cs` |
| item → 视图物化（池、z 序、回收） | `WorkflowSystem/AttachedBehaviors/ViewPool.cs` + `WorkflowSystem/ViewHosting/` |
| 槽测量 / 标题拖动 / 插槽连接 | `WorkflowSystem/AttachedBehaviors/WorkflowSlotLayoutBehavior.cs` / `WorkflowNodeDragBehavior.cs` / `WorkflowSlotConnectionBehavior.cs` |
| 小地图 | `WorkflowSystem/AttachedBehaviors/WorkflowMinimapOverlay.cs` |
| 网格/标尺调色板与间距 | 模板条目 `workflow-grid-decorator/TemplateClass.cs` |
| item → 视图的选择器 | `Easel.Visuals.VisualTemplateSelector` + 模板派生类 |
| Win32 样式、100 控件阈值 | `WorkflowSystem/Platform/NativeWindowStyleHelper.cs` |
| DPI 换算 | `WorkflowSystem/Platform/WorkflowDpi.cs` |
| 视觉运行时、DP、绑定、样式、模板 | `Xaml/Visuals/` |
| 包结构、TFM、双轨引用 | `VeloxDev.WinForms.csproj` |

## 七、这份文件没写的东西

- 七个角色、附着属性名、`PART_*` 命名约定 —— `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3。
- 平台硬限制与逐条坑 —— `memory/modules/WorkflowSystem/adapters/winforms.md`。
- TransitionSystem 侧 —— `memory/modules/TransitionSystem/adapters/winforms.md`。
- 模板形状 —— `memory/modules/Templates/adapters/winforms.md`。
- 人面向的搭建指南 —— `skills/veloxdev-create-workflow/references/practices/gui/winforms.md`。

## Demo 工作台外壳（示例侧）

`Examples/Workflow/WinForms/Demo` 是全视觉工作台：`Program` 建一个 `Form` + 一个 `VisualHost`，根是 `Views/Workbench/WorkbenchView.xaml`（标题条 / `VisualMenu`、活动栏、侧栏、编辑器标签、检查器、状态栏、`DialogOverlay`）。工作流表面是同一棵树里的 `Views/Workflow/WorkflowSurfaceView.xaml`，用 `WorkflowSurfaceBehavior.SetSurfaceName(host, "WorkflowSurface")` 指定；部件、输入和视口按该子树解析，相机坐标仍是宿主坐标（`SurfaceOrigin` 补原点差）。窗口只能是 WinForms `Form`（Easel 没有 Window），保存/打开仍是系统文件对话框；其余 UI 都是视觉元素。

- 节点卡与端口经 `DataTemplate` + `CustomTemplateSelector` 物化（`ControllerView` / `TimerNodeView` / `PythonNodeView` / `EnumSelectorNodeView` / `WorkflowNodeCard`、`FullSlotView`、`LinkView`），网格/小地图在 `GridDecorator.cs` / `MinimapOverlay.cs`。节点右键自动布局/布局容差在 `WorkbenchView.OnSurfacePointerPressed` / `EnsureNodeMenu`；元素表是 `ElementListView`（Easel 行）。
- 连线流光在 `LinkView.xaml.cs`：静态构造函数向 `WorkflowFlowClock.RegisterDriver` 注册按树共享的 30fps **线程池 `Timer` + UI 投递**驱动（不是 `Transition`，避免其 async 续体投回 WinForms 同步上下文），时钟按真实时间采样，绘制路径也采样。
- 模态对话框是根树里的 `DialogOverlay`（半透明背景 + `DialogContent`），不是 `VisualPopup`；因此能盖住画布与全部外壳。
