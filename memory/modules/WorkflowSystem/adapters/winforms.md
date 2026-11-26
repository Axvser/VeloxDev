# WorkflowSystem — WinForms

> 连线交互规则见 [WorkflowSystem/architecture.md §3.6](../architecture.md)：输入是标准输入
> （`WorkflowInput.For(tree).Route(...)` + `IInputEvents`），指针底下是谁由适配器解析、认不到才回退 Core 的共享曲线判定。
>
> **读法**：契约在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；人面向的搭建指南在
> `skills/veloxdev-create-workflow/references/practices/gui/winforms.md`；视觉运行时在 Easel 子模块的 `Easel/Src/Easel`。
> **路径写法**：下文裸文件名都相对 `Src/Adapters/VeloxDev.WinForms/WorkflowSystem/AttachedBehaviors/`。

---

## 一、这家写什么

**工作流表面整棵是视觉树**：宿主是 `VisualHost`，`Root` 是视觉 `UserControl`；`WorkflowSurfaceBehavior` 挂在视觉根上，由 Easel 的 `IVisualHostBehavior` 注入 `VisualHost`，相机写在 `PART_Canvas.Transform`，`ViewPool` 按可见集物化节点/插槽/连线。网格装饰器与小地图是**模板角色**（适配器只留 `IWorkflowGridDecorator` / `IWorkflowMinimapOverlay` 契约与小地图基类）。

| 角色 | 这家的实现 | 形状 |
|---|---|---|
| 画布宿主 | `WorkflowSurfaceBehavior`（视觉根 `RegisterAttached` + `IVisualHostBehavior` 控制器；内部 `ConditionalWeakTable<Control, SurfaceState>`） | Easel 注入 `VisualHost` 后按 `VisualElement.Name` 解析 `PART_*`；相机矩阵、Core 输入路由、布局、视口持久化、小地图接线；DataContext 是树唯一来源 |
| 视觉画布 | `ViewPool`（同目录）+ `WorkflowSystem/ViewHosting/` | `PART_Canvas` 上按 `Helper.VisibleItems` 经 Core 池建视觉子元素；节点/插槽/连线由 `DataTemplate` 定位与绘制 |
| 小地图 / HUD | 模板派生 `WorkflowMinimapOverlay`（`VisualElement`）/ 直接用 `WorkflowInfoOverlay` | 与 `PART_SurfaceBorder` 同级进 tree 根 `Grid`；表面推 scroll/content/viewport，拖小地图经 `IWorkflowMinimapScrollSource` 反推；`WorkflowInfoOverlay.Lines` 把 HUD 文本交给宿主（宿主自己做复制按钮，适配器不含剪贴板 UI） |
| 网格装饰器 | 模板条目实现 `IWorkflowGridDecorator` | 视觉 `VisualElement`；标尺带 `RulerBand` 同时喂虚拟化 inset 与内容偏移 |
| item → 视图 | `VisualTemplateSelector` | 返回 `NodeView`/`SlotView`/`LinkView` 视觉元素 |
| 槽/拖动/连接 | `WorkflowSlotLayoutBehavior` / `WorkflowNodeDragBehavior` / `WorkflowSlotConnectionBehavior` | `VisualElement` 附着行为，由 `WorkflowSurfaceBehavior` 在每个视觉元素上 `Ensure`；自己测量槽、拖动节点、完成连接 |

**部件按名字交进来，不是按对象**：`FindVisualNamed` 递归视觉子树、`Ordinal` 比较 `VisualElement.Name`，取第一个命中；找不到静默跳过，不抛。

## 二、平台硬限制，与由此产生的做法

### 2.1 接线靠附着属性、视觉部件名与 `ConditionalWeakTable`

- `WorkflowSurfaceBehavior` 的开关是视觉根上的 XAML 附着属性：`IsEnabled` / `ZoomEnabled` / `VisualTemplateSelector` / `VisualTemplateSelectorKey` / `LinkMenuKey` / 五个 `*Name` / 视觉连线颜色与宽度；树由视觉根 `DataContext` 提供（没有 `WorkflowTree` 属性或 `SetWorkflowTree` 别名）；`LinkMenuBuilder` 只用于动态菜单。状态放 `ConditionalWeakTable<Control, SurfaceState>`（内部宿主键）。**CWT 不可枚举**。
- 相机与刷新只暴露视觉根 API（`GetZoom`/`GetPan`/`SetPan`/`SurfaceToWorld`/`WorldToSurface`/`GetWorldViewport`/`SetWorldViewport`/`ZoomAt`/`ProcessWheel`/`Refresh`）；宿主键实现是私有细节。
- tree 模板把选择器声明成资源 `x:Key="WorkflowTemplateSelector"`；`WorkflowSurfaceBehavior.VisualTemplateSelectorKey` 默认就是这个键，先从 tree 根的 `VisualElement.Resources` 找，再找 `VisualHost.Resources`，显式 `VisualTemplateSelector` 优先。宿主不再 `new` 选择器。
- **`x:Name` 同时写字段与 `VisualElement.Name`**；模板只写 `x:Name="PART_*"` 一次即可。
- 节点模板用 `WorkflowSlotLayoutBehavior` 声明 `SlotNames` / `SlotEnumeratorNames`（逗号分隔的视觉元素名）与 `CoordinateHostName`；标题行用 `WorkflowNodeDragBehavior`；插槽根用 `WorkflowSlotConnectionBehavior`。
- 行为挂在哪个元素上就只对那个元素（及后代）生效；`IsEnabled` 默认 `false`，没挂或没写就不测量、不拖动、不连接（没有整卡/全部插槽的回退）。`WorkflowSurfaceBehavior` 在 `EnsureAttachedBehaviors` 里遍历视觉树对每个元素调 `Ensure`。

### 2.2 指针走视觉根冒泡；按下在 `PreviewPointerPressed` 相路由，滚轮从 `VisualHost` 进入

- `WorkflowSurfaceBehavior` 在 `VisualHost.Root` 上订阅 `PreviewPointerPressed` 与 `PointerPressed/Moved/Released/Wheel/Exited`、`KeyDown/KeyUp`（由 `VisualHost` 冒泡）。**按下在预览相路由**（`OnPreviewPressed` → `RoutePressed`），句柄先落在表面里，节点拖动/插槽连接行为在自己的 `PointerPressed` 里读 `GetPressHandle`；命中后把屏幕点经相机逆矩阵换成世界坐标，再 `WorkflowInput.Route`。
- **滚轮**：`VisualHost.OnMouseWheel` → `ProcessPointerWheel` → 根视觉元素的 `PointerWheel` → `WorkflowSurfaceBehavior.HandleWheel`。`HandleWheel` 先路由 Core 并读 `PreventDefault`，再按 Ctrl 缩放（`ZoomAt`）、Shift 横滚、默认竖滚。**画布上不挂 `MouseWheel`**。
- **路由出去的 `DeltaX`/`DeltaY` 是原始轮刻度（一格 120）**，与 `IWorkflowSurfaceScroller.ScrollBy` 同一单位 —— 不要在这里再除 120，宿主的 `ScrollBy(-e.DeltaY, 0)` 会因此再缩一次而几乎不动。**Shift+竖向滚轮仍按竖向事件路由**（带 Shift 修饰键），不要改写成 `ProcessPointerHWheel`：那样 `DeltaY` 会变成 0，宿主拦下默认动作后横滚量为 0。
- ⚠ 实测：`WM_MOUSEWHEEL` 在 `HKCU\Control Panel\Desktop\MouseWheelRouting = 2`（「悬停时滚动非活动窗口」开着）下按指针派发；关掉时是否退化为按焦点派发未验证。

### 2.3 相机：一条矩阵，世界坐标，输入走逆矩阵

- `ApplyCamera` 把相机矩阵写到 `PART_Canvas.Transform = [zoom, 0, 0, zoom, pan+content]`。节点/插槽/连线元素按世界坐标摆放，文字与字形随同一矩阵缩放。
- **`EnsureTree` 把非 1 的 `Layout.Scale` 复位到 1**，`Anchor`/`Size` 才是世界坐标；缩放只由相机承担，没有逐卡 `Collapse`（节点模板的 `Viewbox` 只把固定设计栅格装进卡片框，不承担 zoom）。
- 输入：屏幕点经逆矩阵换成世界坐标；拖拽增量除以 zoom。`PART_Canvas` 设 `UnboundedHitTest=true`，视觉裁剪交给 `ScrollViewer`。
- 滚轮上滚 `zoom *= 1.1`、下滚 `/1.1`，夹在 `0.1..10`，指针下的世界点不动。

### 2.4 平移、滚动与视口持久化

- `SetPan` / 指针处理改 `state.Pan`；`ApplyCamera` 把 content offset、相机、网格/小地图偏移一次推完。滚动条按**屏幕像素**走：`scroll = -pan`，不减 `ActualOffset`。
- **`PART_ScrollViewer` 是外部滚动模式**：`ApplyCamera` 调 `ScrollViewer.SetExternalOffset`，范围取「`Layout.ActualSize` ∪ 所有节点的世界包围盒 ∪ 当前可见内容区」乘 zoom；可见内容区是 presenter 视口（扣掉两条占位 ScrollBar 的 8px），不是整个客户区。相机拖出内容边缘时这个并集会跟着长大；同一棵树里只长不回，拖回内容区条的长度保持扩展后的状态，换树才重置。`ScrollOffsetRequested` 用 `pan = -(offset + content + 世界原点×zoom)` 反推；按在拇指/轨道上的手势由 `IsOverScrollBar` 判给 ScrollViewer，不启动相机平移。
- 小地图：`ScrollOffset = -(pan+content)/zoom`、`Viewport = presenter 视口/zoom`（`SurfaceViewport`，扣掉条占位）。
- 视口持久化：视觉路径保存**世界坐标左上角**（`GetWorldViewport`），与当时 zoom 无关；`DetachTree` 把它写回 `Layout.ViewportOffset`，`EnsureTree` 从它恢复 pan。
- **新树挂载时相机从零开始**：`EnsureTree` 在发现一棵新树时先清 `Pan`/`Content`，再按 `Layout.ViewportOffset` 恢复；无树期间布局推过的 content 不能带进新树；带过去会让首次 DataContext 绑定（Full Workbench 的 `WorkflowSurface.DataContext = tree`）把节点整体偏掉。
- 网格/标尺仍随相机缩放。

### 2.5 Win32 样式与 HWND 岛

- `WorkflowSurfaceBehavior.Attach` 对 `VisualHost` 调 `NativeWindowStyleHelper.EnsureClipChildren`/`EnsureComposited`。`WS_CLIPCHILDREN` 与 `WS_EX_COMPOSITED` 同值 `0x02000000`，写向不同 index；顶层子窗口超过 `CompositedMaxControlCount = 100` 就不合成。
- `NativeElement`（`<native:…>`）与 `NativeControlHost` 是原生岛；视觉 `TextBox` 聚焦时用私有岛做 IME/无障碍。子 HWND 永远浮在视觉树之上；活体不能变换/混色，需要时用 `IsSnapshot`。
- **小地图/HUD 是视觉树里的同级部件**；它们共享单个 `VisualHost` 和相机，表面推偏移。

### 2.6 连线：选择器给每条 link 一个 LinkView

- `ViewPool` 通过 tree 的 `LinkTemplate` 为每条 link（含 virtual link）实例化一个视图；`DataTemplate` 把 `StartLeft/StartTop/EndLeft/EndTop` 绑到 `Sender.Anchor.*` / `Receiver.Anchor.*`，把 `CanRender` 绑到 `IsVisible`；表面 `ApplyLinkStyles` 再写 `LineColor`/`LineThickness`/`HighlightColor`/`HighlightThickness`/`IsVirtual`（视图 DP 与附着属性同型 `float`，缺属性就跳过）。视图 `OnRender` 画 cubic 并 `PublishCurve`。
- **`LinkView` 的 `Bounds` 是整张画布，曲线可能画到负世界坐标**；`GetInvalidationBounds()` 按贝塞尔控制点的包围盒算脏区（Full 和 WinForms Trimmed 的 LinkView 都这样做），并**并入上一帧的包围盒** —— 只按新曲线算，旧线段留在脏区外，缩放后靠近端口的段会被切成缺口，直到整面重画（如系统截图）才补齐。
- **视图构造里设 `UnboundedHitTest = true`**：Easel 的 `IntersectsRenderDamage` 按 `Bounds` 剔除子树，画布尺寸的盒子会漏掉整个负世界象限（表现是虚拟连线只在 x>0,y>0 渲染）；该开关让 cull 放行，命中仍由 `IsHitTestVisible = false` 关着。
- **虚拟连线（橡皮筋）的脏区 = 整视图 ∪ 曲线包围盒**：它每个指针帧都在动，且可跨到负世界坐标；只返回整视图（local `0..Width`，只覆盖正象限）会正好把负象限那一段截掉 —— 这就是「虚拟连线只在一个象限渲染」的根因。
- **隐藏 / 解绑时回退到最后画过的包围盒**：松开鼠标重置虚拟连线时当前几何已经没了，基础盒（`0..Width`）又不覆盖负象限，旧虚线会留在屏上直到下一次重绘；`GetInvalidationBounds` 因此把基础盒与 `_lastInvalidationBounds` 求并集。
- **曲线是世界坐标**；相机变换只在绘制时加。`Start/End` 与发布给 Core 命中测试的是同一条曲线。
- 命中在世界坐标里按半径判：半径取 `WorkflowInput.HitRadius`（demo 设 `LinkHitRadius = 6f`）。节点卡是不透明的，压在下面的那段线不画、也不应答，端口附近除外。
- **WinForms 的 ViewPool 是 Core 池的闭包**：`Src/Adapters/VeloxDev.WinForms/WorkflowSystem/ViewHosting/` 组合 `ViewSourcePipelineCore<Panel,VisualElement,IWorkflowComponent,NonPriority,…>`；`ViewFactory` 直接调 `VisualTemplateSelector`，`ViewContainer` 把视图留在画布上（`Deactivate` 清 DataContext + `Collapsed`，复用后重新 `Activate`），回收桶、批大小、Replace 归 Core（默认 `AdaptiveViewPoolPolicy`）。新视图先挂父级再写 DataContext，`ElementName`/祖先绑定才能解析；首次挂载按模板 `Panel.ZIndex` 插位（连线 -1 在节点前）。有句柄时调度用 `BeginInvoke`，无句柄同步排空；`Changed` 每批一次。
- **基准**：`Src/Test/Benchmarks/VeloxDev.Adapters.Benchmarks/WinFormsViewPoolBenchmarks.cs`、`WinFormsVisualFrameBenchmarks.cs`，运行 `dotnet run --project ... -c Debug -p:Optimize=true -- --filter "*WinForms*" --job short --inProcess`。ShortRun+400 卡：冷建约 80ms/15MB；半视口 churn 回收后约 123ms/10.3MB；视觉层 15/50 卡静态帧约 4/7ms，400 卡重绘约 108ms（1200×800）。真实 Demo 2000×1292 节点移动帧 p50 约 18ms。
- **相机 60Hz 节流的首个手势**：`_cameraClock` 是 `Stopwatch`，首次拖动前未启动时 `Elapsed` 恒为 0；只比较帧间隔会让首个手势全程 pending，直到 `MouseUp` 的 `FlushCamera` 才跳一下。`ApplyCameraThrottled` 必须先判 `!IsRunning` 并立即 `ApplyCamera` + `Restart`。回归：`FirstCanvasDrag_AppliesTheCameraBeforeTheRelease`。
- **HUD 内建复制按钮**：`WorkflowInfoOverlay.CopyButton` 是 HUD 的视觉子控件（HUD 本体仍 hit-test 透明，按钮是唯一可交互区域），文本绘制右侧预留 66；`CopyFeedback` 供宿主写状态栏。Demo 不再添加原生覆盖层；位置由 HUD 的 `MeasureOverride/ArrangeOverride` 决定，HUD 的实际边界/DPI 不再由宿主复制常量。
- **Full/Trimmed demo 的交互对齐**：Full 的节点右键自动布局菜单走共享 `WorkflowLayoutDemo`，链接样式旋钮（厚度/悬停厚度/虚拟色/glow）由 `ApplyLinkStyles` 推到 LinkView，窗口级 Ctrl+S/O/Z/Y/Shift+P 走 `Form.ProcessCmdKey`；RulerThickness=28；HUD 复制是 demo-local 按钮，item template 不带剪贴板 UI。模板的 tree item 默认 LinkContextMenu 为空；Demo 在 tree 资源里加 `VisualMenuItem`（Delete）并通过根上的 `LinkMenuKey` 打开。
- **流光由 demo 的 LinkView 自己驱动**：`LinkView` 静态构造函数向 `WorkflowFlowClock.RegisterDriver` 注册一个**线程池 `System.Threading.Timer`** 驱动（`Examples/Workflow/Common/Lib/.../WorkflowFlowClock.cs`），按树共享 30fps；tick 投递到 UI 线程后调 `WorkflowFlowClock.Sample()`。时钟按真实时间算相位，每次绘制都会采样并把 `BandHead/BandIntensity` 抄到视图，所以拖动/平移的重绘本身就让彗星前进。流光只重画旧/新彗星尾段的包围盒，不再每帧刷整面；定时帧用 `VisualHost.PaintCount`/`LastPaintWasClockDriven` 区分拖动重绘与定时重绘：上一拍是拖动重绘就让位，没有新重绘时照常推进，按住不动仍走 30fps。**不用 `Transition`**：它的 `await` 续体会把帧 Post 回 `WindowsFormsSynchronizationContext`，句柄状态变化时会在线程池上抛未处理异常；投递失败要吞掉（关闭中）。
- 视图被池释放后绑定仍可能迟到一次锚点更新；`LinkView` 用 `_disposed` 或 `IsRenderReady`/`double.IsFinite` 拦住，否则 GDI+ `GraphicsPath` 进入无效状态后连 `Reset` 都会抛 `ArgumentException`。
- **拖动时每帧重画是主要成本**：`InvalidateArrange` 不失效测量；`Arrange` 在尺寸不变且子树未失效时跳过 `ArrangeOverride`，`Canvas.Left/Top` 只让父级重排，节点拖动因此不再重排每张卡的内部。节点拖动按约 60Hz 合并位移，画布平移/小地图/外部条拖动按 60Hz 合并（`ApplyCameraThrottled`，松手 `FlushCamera`）；`OnLayoutUpdated` 只推相机，行为检查与连线样式在 `ViewPool.Changed` 后合并到一拍，不再每个视图扫一遍整树。
- **滚动范围跟着内容走**：`ApplyCamera` 把 `PART_Canvas.Width/Height` 设成「（原点 ∪ 节点 ∪ 可见内容区）× zoom，至少视口大小」；`ScrollViewer` 的默认模板是 `ScrollContentPresenter` + 两条占 Auto 轨道的 `ScrollBar`，extent 由 presenter 测量内容得到，偏移仍由相机推（`SetExternalOffset`）。可见内容区并进来后，相机拖出原点/节点范围时 Canvas 会真实长大，条随之变长；扩展范围保存在 `SurfaceState.World*`，拖回不缩回，换树重置。拖动/缩放改变内容尺寸时也一样。
- 右侧菜单：tree 根上 `LinkMenuKey="LinkContextMenu"`；**模板只声明空 `VisualContextMenu`**，Demo 在资源里加 `VisualMenuItem` 条目；`SetLinkMenuBuilder` 只用于动态/纯代码菜单（给 `ContextMenuStrip` 加项）。**不挂 `Control.ContextMenuStrip`**。

### 2.7 插槽锚点写在坐标宿主的局部坐标里

`WorkflowSlotLayoutBehavior` 在布局后遍历 `SlotNames` / `SlotEnumeratorNames`，把命名部件的中心经 `PointToSurface`/`PointFromSurface` 换算到坐标宿主（`CoordinateHostName`，默认最近的视觉 `Canvas`，再退到树根）后写 `slot.Anchor`。它不使用 Core 的 `SlotAnchorFrom*` 函数。

## 三、与其它六家的刻意背离

| # | 这里的做法和其他家不一样，因为… | 依据 |
|---|---|---|
| 1 | **启用表面顺手改 Win32 窗口样式（七家里唯一）**：`Attach` 给 `VisualHost` 上 `WS_CLIPCHILDREN`、给顶层窗体上 `WS_EX_COMPOSITED` | `WorkflowSurfaceBehavior.Attach` 里 `EnsureClipChildren` / `EnsureComposited` |
| 2 | **滚轮从 `VisualHost.OnMouseWheel` 进入视觉 `PointerWheel`**：先路由 Core 再按 Ctrl 缩放 / Shift 横滚 / 默认竖滚 | `VisualHost.OnMouseWheel` → `WorkflowSurfaceBehavior.HandleWheel` |
| 3 | **小地图的滚动空间是有符号世界坐标，靠平移塞进 Core 的非负区间**：`MinimapViewportHost` 把 scroll / content 同抬 `1e9`、两轴上限报 `MaxValue`，管线的 `[0,max]` 夹取因此不生效、也不写 `CanvasLayout` 偏移；`ApplyScroll` 再减回去得到世界原点 | `ViewHosting/MinimapPipeline.cs` 的 `MinimapViewportHost` 与 `ScrollOffsetShift` |
| 4 | **Ctrl 判定是位测试**：`(modifiers & Keys.Control) != 0`，Ctrl+Shift+滚轮也缩放；单独 Shift 时横滚 | `HandleWheel` |
| 5 | **视觉相机是实际缩放**：上滚 `zoom *= 1.1`，与用 `Scale` 坍缩的六家手指方向一致 | `HandleWheel` / `ZoomAt` |

## 四、坑

### 4.1 部件名找不到静默跳过

`FindVisualNamed` 不抛不报；`PART_Canvas` 名字写错时表面启动但一个节点都没有。

### 4.2 控制器只认 Easel 注入的 `VisualHost`

行为挂在视觉根上，宿主由 `IVisualHostBehavior.Attach(host)` 传入；`AttachCore` 仍要求 `host is VisualHost`。没有视觉表面（未挂到 `VisualHost`）时部件、池、相机都不存在，界面空着。

### 4.3 视觉子树文字必须走 `VisualText.Draw`

`TextRenderer` 直接写 HDC、不认 world transform；嵌套平移后文字画到表面原点。

### 4.4 模型 `Layout.Scale` 被强制复位到 1

`EnsureTree` 把非 1 的 `layout.Scale` 复位到 1。外部再写 `Layout.Scale` 会被覆盖，缩放应从相机走。

### 4.5 视图几何在 `VisualHost` 的 DPI 下

`VisualHost` 按 `DeviceDpi/96` 物理渲染；相机、模型、装饰偏移是逻辑 DIP。物理客户区/标尺/小地图/滚动条宽经 `DpiScale` 换算。`VisualHost.LogicalClientSize` 是除回后的逻辑尺寸。

## 五、外部参照

- 坐标与缩放数学：`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Math/WorkflowSurfaceMath.cs`（七家共用）。
- 视觉运行时：Easel 子模块的 `Easel/Src/Easel`。
- WPF 对照：`memory/modules/WorkflowSystem/adapters/wpf.md`。
