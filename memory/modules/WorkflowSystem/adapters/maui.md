# MAUI — WorkflowSystem 适配器

> 悬停高亮与 Delete 归 demo（订 `IInputEvents` 自己画 / 自己执行 `DeleteCommand`）；overlay 没有
> `SelectedLink`/`SelectedLinkColor`。
>
> 连线交互规则见 [WorkflowSystem/architecture.md §3.6](../architecture.md)：输入是标准输入
> （`WorkflowInput.For(tree).Route(...)` + `IInputEvents`），命中归 Core 的共享曲线判定，外观、删除与菜单的接线归宿主/适配器。
>
> 代码：`Src/Adapters/VeloxDev.MAUI/WorkflowSystem/AttachedBehaviors/`。
> 七个视图角色的职责、附着属性命名约定、绑定挂哪儿，在 `../extension.md` §3.9 与
> `skills/veloxdev-create-workflow/references/view-layer.md`（七角色表见 §The seven roles）；给使用者的平台说明在
> `skills/veloxdev-create-workflow/references/gui/maui.md`。
> 参考实现是 `Examples/Workflow/MAUI Trimmed/Demo/Controls/Workflow/`（注意在 `Controls/` 下）。

---

## 一、这家必须实现哪些契约成员，为什么是这些

七角色的落点（定义见 `view-layer.md` §The seven roles）：画布宿主（解析命名控件、喂装饰器）、平移、缩放、可见区、右键菜单接线、组件输入转发的入口与「已路由」登记都在 `WorkflowSurfaceBehavior.cs`（§三·9）；视图池是 `ViewPool.cs`（附着属性）+ `WorkflowSystem/ViewHosting/`（`ViewSourcePipeline` 组合 Core 的池与各层，§二·1）；节点拖拽、插槽连接、插槽布局分别是 `WorkflowNodeDragBehavior.cs`（§三·5）、`WorkflowSlotConnectionBehavior.cs`（§三·6）、`WorkflowSlotLayoutBehavior.cs`（§二·5）。
**没有 `WorkflowCanvasTransformBehavior`**（§三·1），职责由宿主 + `ViewHosting/ViewArranger` 分担。小地图有 `WorkflowMinimapOverlay.cs`；**装饰器适配器不提供**，由模板/demo 写（`Examples/Workflow/MAUI Trimmed/Demo/Controls/Workflow/WorkflowGridDecorator.cs`），与 WPF/Avalonia/WinUI/Jalium/WinForms 同；只有 Razor 在适配器里自带一份。

**契约外的类**：`WorkflowLinkOverlay.cs` —— 链接层（画 Core 的可见集），见 §三·2。

本家不能照搬别家写法的成员有三处：`WorkflowSurfaceBehavior` 的平移/滚动提交顺序（§二·6/§二·7）、
`WorkflowSlotLayoutBehavior` 的重同步信号源（§二·5）、`WorkflowLinkOverlay` 的整层绘制与裁剪（§二·9/§二·8）。
其余与别家同形（附着属性 + `FindByName` 解析 `PART_*`、`UseVirtualization` 走 Core 的 `Viewport`）。

---

## 二、平台硬限制（别家不能照抄这里的哪些前提）

### 1. MAUI 没有 `DispatcherPriority`，视图物化靠调度器连续投递，槽位同步靠 16ms 一次性定时器

- 视图池：Core 的 `ViewSourcePipeline` 以 `NonPriority` 闭合；`ViewScheduler.Schedule` 用
  `Dispatcher.Dispatch` 异步投递当前批，绝不同步执行。池在一批跑完仍有待建项时自己再排下一批
  （`ViewPoolCore` 的 `ProcessNextBatch`）。连续批次累计运行满 32ms 后，下一批改由一个**非重复**的 16ms
  `IDispatcherTimer` 启动，让渲染与输入拿到一帧；定时器 fire 后重新开始连续投递。非重复定时器 fire 一次
  之后不能再 `Start()` 装填，每次走定时器都新建一个。
- 默认池策略是 Core 的 `AdaptiveViewPoolPolicy`，初始 `32` 个视图 / `8ms` 预算，之后按实测吞吐与待建数
  在 8/12/16ms 之间收敛。
- **选择器物化不了的类型按运行时类型缓存跳过**：`ViewFactory.CreateView` 问一次
  `DataTemplateSelector.SelectTemplate`；选择器抛异常（典型：只声明 `NodeTemplate` 的选择器遇到插槽/树节点）
  或返回 null 时，该类型记进 `_unsupportedTypes` 并返回 null，异常不越过工厂。于是**只有节点模板的 demo/模板
  可以把 `ViewPool.ItemsSource` 直绑 `Helper.VisibleItems`**，连线交给共享链接层画，池不需要模板侧的过滤包装。
- **连线不进主池**：`ViewLinkLayer.IsActive = true` 且 `Handles(item) => item is ILinkComponent`，
  主池在入队与物化两处跳过它们；`Render` 是空实现，`WorkflowLinkOverlay` 继续从 `VisibleItems` 画全部可见连线。
- `ViewLayerPolicy` 恒返回 0（层序交给 XAML 元素顺序），`ViewMeasureHost` 是空实现（几何由 MAUI 布局 pass 给出），
  `ViewDiagnostics` 写 `Debug.WriteLine`。
- 槽位同步：同样 16ms，但定时器是**每棵树的共享批次一个**（`WorkflowLayoutBatch` 的 `Schedule`，post 由
  `WorkflowSlotLayoutBehavior.RequestSync` 提供），合并范围是整棵树；布局 pass 那条（Windows 原生 `LayoutUpdated` /
  非 Windows `SizeChanged`）仍同步 `Flush`，见 [../scheduling.md](../scheduling.md)。
- **这几个 `IsRepeating = false` 与 Transition 侧 pacer 的 `IsRepeating = true` 不矛盾**：pacer 的定时器每拍由
  `FramePacerCore.Fire()` 先 `Disarm()`，重复正是为了让下一拍还在；视图池是「建完这一批就停、有待建项再排下一批」。
  对位：`Src/Adapters/VeloxDev.MAUI/TransitionSystem/TransitionInterpreter.cs` 的 `IsRepeating` 设置。
- 定时器 `Tick` 与直接投递的回调都必须自带 try：异常 MAUI 不接，直接冒到 WinUI 的
  `UnhandledException`（dotnet/maui #12245，`ViewScheduler.ScheduleOnFrameTimer`/`Run`、`WorkflowSlotLayoutBehavior.cs` 的 `PostToUiThread`）。

### 2. `GraphicsView` 的 `Draw` 回调没有异常保护

dotnet/maui #14567（`WorkflowMinimapOverlay.cs` 的 `IDrawable.Draw`）：`Draw` 里抛出的异常（典型是 NaN 坐标）直接进
WinUI 的 `UnhandledException`。所以本家所有绘制都整段包 try（同文件的 `IDrawable.Draw`），并且 `dirtyRect.Width/Height`
本身可能是 NaN（`IDrawable.Draw` 里的注释：`NaN > 0` 与 `NaN <= 0` 都是 false，必须显式 `IsNaN`）。

### 3. `GraphicsView` 的触摸事件会带着空/陈旧的 `Touches` 到达

dotnet/maui #13452（`WorkflowMinimapOverlay.cs` 的 `OnStartInteraction`）：`StartInteraction` 可能在 `Touches` 为空时触发，非致命。
所以小地图的三个交互回调都做「空 → 直接 return」并各包一层 try（同文件的 `OnStartInteraction`、`OnDragInteraction`、`OnEndInteraction`）。

### 4. Win2D 的 ~16k 设备像素纹理上限（决策了整个链接层的形状）

`WorkflowLinkOverlay.cs` 的类文档：**画布尺寸**的 `GraphicsView` 在深缩放下超过 Win2D 纹理上限，整层**静默消失**。

⚠ **缩放后的连线上限时机**：槽锚点在布局趟的尾部才写回；`WorkflowLinkOverlay.FlushInvalidate` 除当前帧外再排一次主线程重绘。只发当前那一帧会被正在合成的帧吞掉，连线沿用旧端点，直到下一次指针/滚动事件。
于是链接层取**视口尺寸**、住在装饰器坐标系里（网格/标尺用的同一身份：
`px = RulerThickness + c + ContentOffset − ScrollOffset`），并因此**必须自己做包围盒裁剪**
（`CullMargin = 24`，裁剪判定在 `IDrawable.Draw` 里用 `IntersectsViewport`）。这一层永远是满屏**视口**尺寸，不是内容尺寸，裁剪不能省。
**每帧画哪些线由枚举源决定**：`EnumerateVisibleLinks` 取 Core 的虚拟化可见集（`tree.GetHelper().VisibleItems`
里过滤出 `ILinkComponent`），因此是 O(可见) 而不是 O(全部) —— 与其余六家同源。包围盒裁剪留作第二道筛子：
可见集用的是「两端节点包围盒的并集」，比曲线本身粗，并集擦过视口时曲线仍可能落在外面。
虚拟连线不在 `tree.Links` 里（可见集里那份是同一个实例），所以它从可见集里排除、单独补在最后 —— 橡皮筋要压在实连线之上。
**按可见集裁剪不会漏画真在视口里的线**：`NodePairBoundsProvider.CalculateBounds()` 取的是两端节点包围盒的**并集**
（`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Virtualization/NodePairBoundsProvider.cs` 的 `Viewport.Union(bA, bB)`），
而本家的贝塞尔控制点只在两端之间横向拉开 ⇒ 曲线恒在这四点的凸包内、也就恒在该并集内 ⇒ 并集不与视口相交时曲线也不可能可见。
（对「自定义链接视图画出并集之外的东西」才不成立 —— 其余六家同样如此，属已知局限。）

### 5. 托管的 `SizeChanged` 是 **arrange 途中**，不是 arrange 之后

依据是三处注释与它们的实现：
`WorkflowSlotLayoutBehavior.cs` 的 `Attach`（里面 Windows 装原生信号）、`TryInstallResizeSignal` 的文档、
`OnNodeResized` 的注释（「MAUI 的托管 SizeChanged 在 arrange 途中触发，节点内部槽位还没定型，
测出来的锚点会比最终几何过冲 ~10ms（见 git 历史里的逐格端点弹跳）」）。

- Windows：经 `control.Handler?.PlatformView` 拿 `Microsoft.UI.Xaml.FrameworkElement`，挂**原生** `LayoutUpdated`
  （`TryInstallResizeSignal`）—— 那是「整个子树已 arrange 完」的信号；装上原生信号后**立刻摘掉**托管兜底（同方法）。
- 非 Windows：没有框架级 `LayoutUpdated`，只能留托管 `SizeChanged`（`Attach` 的非 Windows 分支）。
- 别家（WinUI/WPF 家族）本来就在 `LayoutUpdated` 系信号上同步，没有这一步。

### 6. `ScrollToAsync` 是 fire-and-forget 的 `ChangeView`，不保证落在请求值上

`WorkflowSurfaceBehavior.cs` 的 `OnScrolled` 注释：MAUI 在每次原生 `ViewChanged` 上都会发 `Scrolled`，`ScrollX/ScrollY` 是那一刻的
原生真值；而 `ScrollToAsync` 可能落不到请求值（Windows 上原生 ScrollViewer 还会自己动内容）。结论：

- **不许**在平移期间抑制 `Scrolled` —— 抑制过装饰器就被冻在「请求值」上，原生最后落在别处时松手一跳（那就是「松手跳」）。
- 装饰器与视口**只从 `ScrollX/ScrollY` 写**（`ApplyLayout`、`ApplyVisibleRegion`），
  请求的目标值从不作为写入源（`ApplyVisibleRegion` 的注释明写：写请求值会让网格在松手后弹回真位置）。

### 7. 原生 extent **异步**重测，模型是**同步**长大的

滚到边缘时画布扩张：模型侧（`Layout.ActualSize` 与 `ApplyLayout` 写的 `WidthRequest/HeightRequest`）**同一帧**就变了，
而原生内容重测是异步的，快拖时原生 extent 能落后模型几千像素。这造成两条**方向相反**的写法则：

- **平移**：apply 之前用原生 extent 夹取（`GetNativeScrollMaximum`；Windows 专用读
  `ExtentWidth/ViewportWidth`，非 Windows 回退模型）。落地后再把锚点回写到**夹取后**的值（`ApplyPanTargetAsync` 的 `appliedOffset*` 重锚），
  否则记账跑在原生前面 → 内容先钉住再跳一帧。夹带一次 `Task.Yield()` 等比原生重测（缩放回中用 `RecenterOnWorldPointAsync`、恢复用 `ApplyPendingScrollRestoreCore`）。
- **小地图拖动**：最大值必须取**模型**，不能用 `_scrollView.ContentSize`（那个跟着异步布局落后）—— 用 ContentSize 会把目标钉在当前边缘，
  2px 节流随即跳过滚动，画布永远长不大，形成**自锁**（`WorkflowMinimapOverlay.cs` 的 `NavigateToCanvasLocal` 注释；同方法里扩张后重算）。
  它同样在扩张后用 `WorkflowSurfaceBehavior.Refresh` 主动推一次布局（`NavigateToCanvasLocal`）。

### 8. `Handler.PlatformView` 是唯一通往原生的路，且它有三个时序坑

- **平台元素没有 `DataContext`**：写在平台层的处理器要从闭包里捕获宿主（`WorkflowSurfaceBehavior.cs` 的 `OnZoomEnabledChanged` 注释明写），
  按 WPF 的写法写会发现「什么也找不到」。
- **`HandlerChanged` 早于平台视图创建**：所以允许一次延后重试，并必须**有界**（`WorkflowMinimapOverlay.cs` 的
  `_platformAttachPending`；`OnHandlerChanged` 里重置它）。无界重试 = 视图永不出现时的死循环。
- **换 handler 必须摘钩子**：`OnHandlerChanging` 里把 `PointerPressed/Moved/Released/Canceled/CaptureLost` 逐个 `RemoveHandler`
  （`WorkflowMinimapOverlay.cs` 的 `OnHandlerChanging`），否则页面导航后泄漏。

### 9. 本版 `ICanvas` 没有描边渐变，所以「流光」是采样出来的几何而不是渐变笔

类文档 `WorkflowLinkOverlay.cs`：本版 `ICanvas` 没有描边渐变的等价物（`SetFillPaint` 只在填充侧），
彗星因此是**按弧长切出来的几何**：曲线先按 `LinkCurve.DefaultSampleCount = 128` 采样成弧长表（采样与弧长表归 Core
`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Interaction/LinkCurve.cs` 的 `DefaultSampleCount`、`BuildLinkCubic`；
本层调用在 `WorkflowLinkOverlay.cs` 的 `IDrawable.Draw` 里），再在表上取头与尾，用 `TailSegments = 16` 段、每段一个透明度的 `DrawLine` 画出来
（`TailSegments`、`DrawComet`；两遍：先光晕后本体）。按弧长走的光会跟着弯走；渐变刷的轴是两端的连线（弦），光在弯链上会离开绳子跑到弦上。

### 10. 非 Windows 上拿不到原生指针捕获

`WorkflowSlotConnectionBehavior.cs` 的 `OnPanUpdated` 注释：MAUI 库的 TFM 是平台中立的，所以原生 pointer capture 可能不可用 ——
一旦 `Pan` 手势开始，就让它独占这次拖拽（同方法）。Windows 上则由 `element.CapturePointer` 显式捕获
（`WorkflowNodeDragBehavior.cs` 的 `OnPlatformPointerPressed`、`WorkflowSlotConnectionBehavior.cs` 的 `TryCapturePointer` 原生分支）。小地图更彻底：Windows 上
**两条独立通道**（原生 `PointerPressed/Moved/Released` 探针 + `PanGestureRecognizer` 的 manipulation 增量）同时存在，
因为只有捕获所有者能收到越界事件（`WorkflowMinimapOverlay.cs` 的 `_nativePressedHandler` 等字段、`AttachPlatformPressedHandler`）；并且 Windows 上
`EndInteraction` 必须**直接 return**（`OnEndInteraction`）—— MAUI 指针一离开小地图就报 `EndInteraction`，而拖拽还没结束。

⚠ **两轴不能来自 MAUI 的 `ScrollX/ScrollY` 镜像。**（2026-11-18 同一轮实测）
`ScrollViewHandler.ViewChanged` 先写 `VerticalOffset`、再写 `HorizontalOffset`；`IScrollView` 的两个 setter 各发一次 `Scrolled`，
于是 `ScrollX` 与 `ScrollY` 的镜像在不同事件里更新。逐次 `Refresh` 会让网格/缩略图先只动 Y、再只动 X。
现在 `WorkflowSurfaceBehavior` 的 `ActualScroll(state)` 在 Windows 上直接读原生 `ScrollViewer.HorizontalOffset/VerticalOffset`
（同一帧的两轴），`OnScrolled` 也只排一次 `Refresh` 到下一轮 UI 派发；事件不丢，更新成对。

⚠ **两条通道只能有一条 `NavigateToCanvasLocal`。**（2026-11-18 用户报告小地图拖动抖动，X/Y 不匹配）
`PanGestureRecognizer` 用 `_dragStart + TotalX/Y`（累计增量 + 起点）而原生探针用绝对 `PointerMoved` 坐标；两者同时驱动时目标在
两套坐标之间交替，视觉上就是 X/Y 各自抖动。现在用 `_panActive`（手势 Started/Completed）和 `_nativePointerActive`（原生按下/释放）互斥：**Windows 上优先原生绝对坐标**（`PanGesture.TotalX/Y` 在 WinUI 会带屏幕/元素偏移，曾把目标一次推出上千像素），手势只在原生探针没挂上时兜底；`OnDragInteraction` 被两者压住。另：`_nativePressedHandler` **不能**在 `_isDragging` 已为真时覆盖
`OnStartInteraction` 记下的 `_dragStartX/Y` —— 起点混用会让累计增量落在错误的基准上。每次按下还重置
`_lastScrollX/Y`，否则从上一轮继承的 2px 节流会吞掉第一笔拖动。

**视口往返（2026-10-03 接上挂树口）**：这家本来就有整套恢复机（`RequestViewportRestore` → 调度器 +
`Task.Yield` + `ToScroll` + clamp），但它只对宿主开放，宿主不调就不恢复。现在
`Attach`/`OnLoaded`/`OnBindingContextChanged` 三处都会 `CaptureViewportRestore`（树引用变了才算，
且必须在 `Refresh` **之前** —— 那次 `UpdateVisibleRegion` 会把它覆盖成本地 (0,0)）。同一轮还修了一处坐标
不一致：持久化过去写原始滚动值、恢复却按画布局部处理，`ActualOffset ≠ 0` 时会加两次；现在两侧都写画布局部。
见 [../extension.md](../extension.md) §3.9-10。

### 11. 非 Windows 平台没有键态来源，修饰键只能填 `None`

MAUI 没有跨平台的键盘状态 API：`Microsoft.Maui.Controls.PointerEventArgs` 只有 `Button` / `PlatformArgs`，
**没有 `Modifiers`**；`KeyboardAcceleratorModifiers` 只是键盘加速器的枚举，不是「现在按着什么」的现取值。
所以 Core 输入 args 的 `InputModifiers` 在这家分两路取（两个助手都在 `WorkflowSurfaceBehavior`）：

- **Windows 头**与 WinUI 同源：`Modifiers(Windows.System.VirtualKeyModifiers)` 把平台事件自带的 `KeyModifiers`
  逐位映射（`PointerRoutedEventArgs` **有** `KeyModifiers`）。⚠ `KeyRoutedEventArgs` **没有** `KeyModifiers`
  —— 键事件那条路（`WorkflowLinkOverlay.OnSourceKeyDown`）必须走下面的 `ModifiersNow()`，照 Pointer 的写法会 `CS1061`。
- `ModifiersNow()`：`#if WINDOWS` 用 `Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread` 现取当前
  线程键态（对应 WinUI 的 `KeyModifiersNow`）；其余平台（Android/iOS/MacCatalyst）**返回 `None`**。
  指针悬停/按下/滚轮、以及组件自己那次转发（`RouteComponentPress`）都不携带修饰键，在非 Windows 上
  因此**永远看到 `None`** —— 这是平台缺口、不是待修的 bug。`WorkflowLinkOverlay.OnLongPressTick`（仅非 Windows
  的长按菜单手势）保持字面 `None`，注释已写同一原因。

⇒ demo 层「空白画布 Shift+拖拽」那种按修饰键分流的定制，在非 Windows 上拿不到 Shift；Windows 头（与 WinUI 同平台）正常。

---

### 12. 程序化滚动会把键盘焦点留在滚动容器上 —— 键从此不再到达宿主（2026-10-11 实测）

`ScrollView.ScrollToAsync` 会把键盘焦点停到滚动容器上。配上「画布滚动改由适配器执行」（[architecture.md §3.6](../architecture.md)）之后，这一条的后果是**静默的**：滚一次轮，焦点就跑到表面子树之外，于是 Ctrl+Z / Delete / Shift 全都再也到不了宿主的键路由 —— 不报错，只是没反应。

解法两条，都要：

- 滚动走**原生** `ScrollViewer.ChangeView`，不用 `ScrollToAsync`；
- 键钩子要挂在**窗口根**，而 `XamlRoot` 在 attach 那一刻是 **null** ⇒ 先挂在交互源上，再由 `TryUpgradeKeyHook` 在 dispatcher 上重挂到根出现为止（上限 30 tick），并在 attach / 按下 / 滚轮三处各叫它一次 —— 只靠「悬停时升级」永远等不到。

⇒ 「滚轮能滚」与「键还到得了」在这家是**两个耦合的问题**。

### 13. 滚轮「内外一起滚」的 A/B 与未决异常（2026-10-11 探针）

指针压在卡片内层可滚区上滚一格，两组对照（完整版 demo，内层是 `PythonNodeView` 的输入口条）：

| | 内层 | 画布 `视口(画布)` |
|---|---|---|
| **有拦截**（默认适配器） | V 每格 +24（两格 0→48） | V 每格 **+61.8**（两格 0→124） |
| **bypass**（路由前 return） | V 两格 0→48 | **`0,0`，一位没动** |

入口 `handledEnter=True` 是硬证据：内层 `ScrollViewer` 早已滚过并置了 `Handled`。⇒ 与 WinUI 同形 —— **外层那 61.8/格是适配器额外加上去的一滚**，根因是那条无条件 `ScrollBy`；bypass 组外层不动，反证了「滚轮按惯性操纵、链式不生效」。

同批的附带结论：

- ⚠ **默认数据下每张卡 `scrollableH=0`** ⇒ 缺陷 ① 默认不可见。
- **「两轴齐写」未复现副作用**：先把 H 平移到 266.67 再竖滚一格，H 精确保持 266.67。
- **「滚一格把键盘焦点带走」未复现**：滚完之后 `Ctrl+Z` 仍到达宿主的 KeyDown 订阅并执行。⚠ 但 Trimmed 初始树没有可撤销项，「看节点数变没变」这条判据在这家**判定不了**。

### 14. 滚轮：挂点、闸门、两条根因（实测，两条都是跨家的）

**N6-a 的根因：处理器挂的平台视图是 0×0 的 `LayoutPanel`**，空白处的命中落不到它身上。证据：在窗口根挂 `handledEventsToo` 探针逐点记录 12 个空画布点，都进了 App，但多数链从 `OriginalSource` 到窗口根**没有 `[CANVAS]`**（典型链 `LayoutPanel(2240x1340) < ContentPanel < ScrollContentPresenter < Grid < Border < ScrollViewer < …WindowRootView`）；只有落在节点卡上的点链里才有 `[CANVAS]`。那些点上的 HUD 数字照样 +61.8 —— 是**原生 `ScrollViewer` 自己滚的**，既没进路由、也不是适配器的步长。
**修**：挂点改到画布平台元素**往上最近的 `ScrollContentPresenter`**（覆盖整个滚动视口，且在做滚动那一层**之前**冒泡），取不到才退回画布元素。改后同一个空画布点 20 格 ⇒ 画布 `2240×1340 → 2240×1772`。

**N6-b 的根因在适配器，不在 demo**：`ToKey` 只点名了 `LeftShift(160)/RightShift(161)`，**无侧的 `VirtualKey.Shift(16)` 落 `Unknown`** —— 真实键盘左右 Shift 的 `WM_KEYDOWN` 报的都是 `0x10`（左右之分在扫描码里），SendInput 合成的更是**只有**这个码。于是 demo 里 `IsShiftKey(e.Key)` 永远为假：**按下那半还有 `e.Modifiers` 兜着，抬起那半永远不触发** ⇒ 松开 Shift 之后一直按横滚走（实测 `keyup:Shift` 后两格仍是 `124,247`）。demo 的写法**与 WPF 那家逐字相同**，所以修在适配器：补 `Shift/Control/Menu → LeftShift/LeftCtrl/LeftAlt`（与 WPF 的 `KeyInterop` 同一条）。改后 `keyup:Shift` 一格 ⇒ `124,371`（回到竖滚 +61.8）。
⚠ **这是跨家缺陷**：WinUI 用的是**同一套 `VirtualKey` 词汇**、`ToKey` 里同样只有八个侧别键，已一并补上（WinUI 的 demo 也是逐字相同的形状）。

**闸门**：与 WinUI 同形 —— 实测**内层到边那一格照样置 `Handled`**（`inner=128/128` 时 `handled=True`），`if (e.Handled) return;` 单独不够，判据要叠加「沿方向绝对余量」。A/B：内层有余量 11 格 ⇒ 画布 `视口(画布) 0,0`；第 13 格起画布接；空白点 ⇒ 61.8/格。

**边缘**：撑大后与**平移逐位一致**（24 格 ⇒ `2240×2038 / 0,1360`，平移拖到并越过边也是同一组数）；向上回滚 `1360→1236`，画布保持撑大不缩。

**横滚**：`hwheel:` 实测 `x 62→124`（+61.8）、`y` 不变。

**回归**：中部 1 格 `0,62`、5 格 `0,309`（=61.8/格，**不是 123.6**）；Ctrl+滚轮缩放 `121% / Scale 0.83`。

**未决异常（未定性）：**

1. **Trimmed 上有若干空画布点滚轮没进 `OnSurfaceWheel`**（原生 `ScrollViewer` 自己滚了 61.8），而同 demo 另外几个点进了（`hit=LayoutPanel`）；卡片区稳定进。这动摇「挂在画布上就拦得住」这个前提。
**`keyup:Shift` 之后仍按横滚执行 —— 已消解。** 现在 demo 读滚轮事件自带的 `Modifiers`，每个事件各带当时的修饰位；适配器把无侧 `VirtualKey.Shift` 映射到 `LeftShift` 仍保留 —— KeyDown（Ctrl+Z 等）还要用。

### 15. 链接层的指针钩子挂在整页根上，必须按 overlay 边界过滤（2026-10-30）

`WorkflowLinkOverlay` 的 `InteractionSource` 在完整 Demo 里是**整页 `ContentView` 根**（`x:Reference Root`），而侧栏（元素列表）与画布同在这个根里。它的原生 `PointerPressed` 钩子会收到侧栏按钮的按下，`ToOverlayPoint` 又把相对 overlay 的坐标算成负值照收，于是按下瞬间 `_interactionSource.Focus()` 把焦点抢走 —— 真实鼠标按住 100–150ms 时按钮的 `Click` 不再落地；**同一刻 down+up 的合成点击仍然会触发**，所以快速探针会假通过。表现：点元素列表的 Locate 只让 SEED 输入框获得焦点，HUD 不动。

**修**：`ToOverlayPoint` 在负坐标或超出 `Width/Height` 时返回 `null`（未排版时 `Width/Height <= 0` 仍放行）。侧栏的按下/悬停/滚轮不再被链接层接走，画布内的不变。WinUI 的同源问题在表面宿主的 `OnLinkPointerPressed`（用 `IsWithinScrollViewer` 判门），见 [winui.md](winui.md) 三点五。

## 三、与其它家的刻意背离

1. **没有 `WorkflowCanvasTransformBehavior`。** 这里的做法和其他家不一样，因为 MAUI 没有 `Viewbox`，
   而且本家**刻意不使用 RenderTransform**：变换会污染槽位布局读到的 layout 位置（见 demo 的 `NodeView.xaml.cs` 里那段
   「Uses layout-only changes (no render transforms) so the slot-layout behavior's layout-position measurement keeps producing correct link anchors」）。于是平移改由两处**代码写几何**承担：整块画布 `TranslationX/Y = Layout.ActualOffset`
   （`WorkflowSurfaceBehavior.cs` 的 `ApplyLayout`，注释说明为什么是画布级而不是逐子元素：同级平移才能让节点/连线/网格同帧一致），
   每一项的落位由 `ViewHosting/ViewArranger` 写 `AbsoluteLayout.SetLayoutBounds`；Core 池只在物化时 arrange 一次，
   所以 `ViewArranger` 还订阅节点的 `Anchor`/`Size`，每帧合并一次重写。
   `view-layer.md` §The binding rule that bites 那条「变换绑定必须挂在 `DataTemplate` 根上」在 MAUI 上**不适用** ——
   这里没有那个附着属性可绑。
2. **两个 demo（与模板产物）都是「一层」：共享的 `LinkView` 包一个 `WorkflowLinkOverlay`（「每线一视图」当前无消费者）。**
   `WorkflowLinkOverlay` 是唯一边线宿主：画全部可见连线 + 唯一的光带动画宿主 + 收指针。它仍保留一条跳过判据：
   **一条连线的曲线如果带着 `Visual` 被发布（视图调了 `PublishCurve(curve, this)`），这一层就跳过它** ——
   `WorkflowLinkOverlay.cs` 的 `LinkOverlayDrawable` 的 `Draw` 里那句 `if (link.HitTarget()?.Visual is not null) continue;`
   （给「每线一视图」留的路，当前仓库里没有视图再走它）。于是：
   - **Trimmed demo**（`Demo/Controls/Workflow/LinkView.xaml(.cs)`）：`ContentView` 包**一个** `WorkflowLinkOverlay`。
     顶部注释写明 per-link GraphicsViews 在深缩放下超 Win2D 纹理上限、已被移除。选择器只给 `NodeTemplate`，
     池只物化节点视图（`TreeView.xaml` 里共享的 `LinkView` + 只给 `NodeTemplate` 的选择器）。
   - **完整版 demo**：同样「一层」，`WorkflowView.xaml` 里一个 `WorkflowLinkOverlay`；选择器只给 `NodeTemplate`。
     overlay 照旧画全部连线（含光带），不需要 `LinkTemplate`。
   两条路线共用同一个输入路由（`WorkflowInput.For(tree)`）与同一份命中几何（`HitTestVisibleLinks`），所以交互行为一致。
   **光带仍然只有 overlay 有**（`BandCentre`/`BandMix` 两个标量 + `Transition<WorkflowLinkOverlay>`，
   三段 450/700/450ms 结尾 `Repeat(int.MaxValue)`）；当前没有每线视图，所以「每线视图自己画彗星」**还没做**。
   改回每线视图要重付 §二·4 的 Win2D 纹理上限代价（`LinkView.xaml` 顶部那段注释）。
3. **流光的颜色规则是「本色往白里提」，不是换色**：线体本身是静息的暗线（本色 alpha 降到 55%，见 `WorkflowLinkOverlay.cs` 里静息描边的 `stroke-opacity`），
   彗星的每段由本色与白按它在尾上的位置插值、透明度按位置平方衰减、再叠一层更宽更淡的光晕
   （`WorkflowLinkOverlay.cs` 的 `DrawComet`）。与 `view-layer.md` §Painting the connection preview 的通则一致，但本家的实现落点在 overlay 的两个标量上。
4. **小地图拖动 = 按下的点直接成为视口中心，没有抓取锚点**。`WorkflowMinimapOverlay.cs` 的 `OnStartInteraction` 注释明写「与 Jalium 家一致」。
5. **节点拖拽按 TFM 分叉**：Windows 走原生 `PointerRoutedEventArgs`（`WorkflowNodeDragBehavior.cs` 的 `OnPlatformPointerPressed` / `OnPlatformPointerMoved` / `OnPlatformPointerReleased`），
   非 Windows 走 `PanGestureRecognizer`（同文件的 `OnPointerPressed` 等转发 + `OnPanUpdated`），并且**刻意不加** `PointerGestureRecognizer`
   （同文件 `Attach` 的注释：它的 `PointerMoved/Released` 与 Pan 的生命周期打架、没有增益）。别家是单一机制。
   两边的守卫都要认：`IsDraggingNode` 被平移逻辑读（`WorkflowSurfaceBehavior.cs` 的 `ApplyLayout` / `ApplyVisibleRegion`，拖节点时平移把锚点贴到当前位置，避免节点拖拽结束后平移跳一段）。
6. **插槽锚点写 `SlotAnchorFromCanvasLocal`**（`WorkflowSlotConnectionBehavior.cs` 的 `SynchronizeSlotAnchor`），因为这里量到的中心**已经是画布局部坐标**：
   `TryGetCenterRelativeTo` 走父链求和，且 `GetLeftInParent/GetTopInParent` 优先取 `AbsoluteLayout.GetLayoutBounds`
   （`Translation` 不在其中）。**用 `SlotAnchorFromVisualCenter` 会把 `ActualOffset` 减两次**，每条线整体偏移
   （`skills/veloxdev-create-workflow/references/gui/maui.md` §Links — one overlay for the whole graph 同结论）。
7. **Windows 上原生 `ScrollViewer` 是彻底被动的，且平移不走 manipulation（见 §五）。**
   `IsScrollInertiaEnabled = false` 且 **`ManipulationMode = None`**（`WorkflowSurfaceBehavior.cs` 的
   `OnScrollViewerHandlerChanged`）—— 两半都必要：`TranslateX|TranslateY` 正是 ScrollViewer 用来做**操纵滚动**的那两个轴，
   留着它们它就还是第二个滚动驱动者，并会在松手那一刻把自己累积的偏移补上（实测：松手后 60ms 从 423 跳到 186）；
   而平移现在改走原生指针事件，全树不再需要任何 manipulation，所以 `None` 是它该有的取值。详见 §五。
8. **平移用绝对锚点，不用每帧增量累积**：`SurfaceState` 里那组 `PanAccumulated*` / `PanAnchorTotal*`（字段上的注释）
   在 `Started` 记一次锚，每次 `Running` 用 `anchor − (Total − anchorTotal)` 算绝对目标（`OnPanUpdated`），
   并让**在飞的上一笔 `ScrollToAsync` 被取消**（`PanCts`，`SurfaceState` 字段与 `ApplyPanTargetAsync`）。读每帧实际 `ScrollX` 会闪回，
   累积逐帧增量会在被夹取的滚动下漂移（卡住再跳）。
9. **输入转发是「组件各自转发 + 已路由标记」，不是「表面在隧道相转发一次」。** 本家**没有隧道路由相**，
   而节点/插槽的处理器比链接层挂在 `InteractionSource` 上的钩子更早跑（平台事件从命中元素往上冒泡，越深越早），
   所以「这一笔的句柄」只能由**组件自己**那次转发产出 —— 七个自带手势因此各自读自己那份：
   - **节点 / 插槽**：`WorkflowNodeDragBehavior`（Windows 的 `OnPlatformPointerPressed`、非 Windows 的
     `OnPanUpdated` Started）与 `WorkflowSlotConnectionBehavior.TryBeginConnection` 调
     `WorkflowSurfaceBehavior.RouteComponentPress(view, 画布坐标)`，**目标就是组件自己** ——
     这是 `WorkflowInput.Chain` 里 `slot → node → tree` 那条链唯一的入口（只认连线的路由永远只答「连线或空白」）。
     非 Windows 的 Pan 手势不给指针位置，锚点退回组件自己的 `Anchor`。
   - **平移**：Windows 在 `OnPlatformPanPressed` 自己转发（`RouteSurfacePress`，落点取指针在画布元素里的坐标）；
     非 Windows 的手势**转发不出来**，只能在 Started 读 `PeekRoutedPress`（那一笔由链接层转发过）。
   - **缩放**：只有 Windows 的 Ctrl+滚轮进路由（`OnZoomWheelChanged` → `RouteSurfaceWheel`）；
     非 Windows 的捏合**没有可对应的滚轮事件**，当前不转发、也就否决不了。
   「一笔只转发一次」由 `WorkflowSurfaceBehavior` 的两组静态登记保证：
   **按下**按**树**认（`RoutedPressTree`/`RoutedPressHandle`，`WorkflowSurfaceBehavior.cs` 的静态字段），由链接层转发**松手**时清（`ClearRoutedPress`）；
   **滚轮**按**物理事件对象**认（`RoutedWheelEvent`/`RoutedWheelHandle`）—— 先到者转发并登记、后到者读现成的（`RouteWheelOnce`）。
   ⚠ 滚轮的登记**必须**按事件认：按树时登记要由**后到者消费**才清，而普通滚轮只有链接层一条来者 ⇒ 登记没人消费、留到**下一笔**上把那一笔判成「已经路由过」而静默丢掉。一次物理滚轮必然是新实例，所以按事件认没有收尾问题。
   加第二次 `Route` 会让宿主一笔听见两次，还会多收一对 `Entered`/`Exited`。

---

## 四、改这里时最容易踩的坑

1. **在平移期间抑制 `OnScrolled`** → 装饰器冻在请求值、松手跳（`WorkflowSurfaceBehavior.cs` 的 `OnScrolled`）。
2. **把 §二·6 与 §二·7 的写法则互相「统一」** → 一边自锁（boundary 不再长大）、一边松手跳。两处方向相反是刻意的。
3. **在 Windows 上把重同步信号换回托管 `SizeChanged`**（或让兜底与原生信号并存）→ 逐格端点弹跳：`~10ms` 的 arrange 途中过冲
   （`WorkflowSlotLayoutBehavior.cs` 的 `Attach`、`TryInstallResizeSignal`、`OnNodeResized`）。
4. **删掉任何一处 NaN 守卫** → 最小后果是连线永久消失。NaN = 未测量是**跨平台统一机制**
   （`../extension.md` §3.9-4）：`Viewport`/`Virtualize` 那条路上 NaN 会让**整批 VisibleItems 被清空**
   （`WorkflowSurfaceBehavior.cs` 的 `ApplyVisibleRegion` 注释：`NaN <= 0` 是 false，所以 Core 的守卫抓不到）；
   小地图是成组的（`WorkflowMinimapOverlay.cs` 的 `ComputeDrawing` / `GetClampedViewportRect` 里各段 NaN 守卫），
   链接层则在 `TryGetEndpoints` 出口判 NaN（`WorkflowLinkOverlay.cs`）。
5. **给链接层或小地图加新的视觉 DP 却不加 `propertyChanged`** → 改了属性不重画；加了但不合并 → 一帧内多个 DP 写入各发一次
   `Invalidate`（`WorkflowLinkOverlay.cs` 的 `ScheduleInvalidate` / `FlushInvalidate`；小地图 `MarkDirty` / `FlushInvalidate`）。
   `ApplyVisibleRegion` 一帧就连写 6 个 DP（`WorkflowSurfaceBehavior.cs` 的 `ApplyVisibleRegion`），合并不是优化而是必需。
6. **去掉 `WorkflowLinkOverlay.InputTransparent = true`**（`WorkflowLinkOverlay` 的构造函数）→ 这层满屏盖在节点上，会吞掉全部节点交互。
   它在树里的 z 序靠 XAML 位置（装饰器内、`ScrollView` 之前，`TreeView.xaml` 的 `LinkView` 与 `ScrollView` 元素顺序），挪位置等于改层序。
7. **删掉 `x:Name="Root"`** → 链接层的 `WorkflowTree` 与 `InteractionSource` 是 `Source={x:Reference Root}` 的绑定
   （`TreeView.xaml` 的根 `x:Name="Root"` 与 `Source={x:Reference Root}`），全部解析不到；`ScrollOffset*`/`ContentOffset*`/`RulerThickness` 绑的却是
   `PART_GridDecorator`（同文件的 `Source={x:Reference PART_GridDecorator}`），不随 `Root` 一起失效（`skills/veloxdev-create-workflow/references/gui/maui.md` §The connection preview and the wheel 同结论）。
8. **把隐藏视图从 `_layout.Children` 里 `Remove`** → 本家刻意保留子元素：`ViewContainer.Detach` 是空的，
   `Deactivate` 只写 `IsVisible=false` + `ZIndex=-100` + `BindingContext=null`；`Destroy` 才移除（且默认策略不限回收，
   基本不会走到）。移除会触发昂贵的 MAUI 重排。相应地，隐藏视图仍在树上 —— 遍历子元素时别假设「看不见 = 不在」。
9. **忘了取消在飞的滚动** → `PanCts` 一旦漏取消，`ScrollToAsync` 会叠起来（`WorkflowSurfaceBehavior.cs` 的 `ApplyPanTargetAsync` 与 `SurfaceScroller` 里的清理）。
10. **删掉 `Refresh` 的 `IsRefreshing` 重入守卫** → 画布扩张 → `Refresh` → 再扩张的级联（`Refresh` 的注释记了
    「每次扩张级联 2-3 次 Refresh，正反馈减速螺旋」）。
11. **给链接层加「可见集之外也要补画」的兜底**（最典型的是把全量 `tree.Links` 再拉回来做差分）→ 又和其余六家分叉，
    而且不需要：新连线进可见集的时机就是 Core 的「量完才画」，与 WinUI 那条的行为一致。
12. **每线视图（`LinkView`）的盒子：`Path` 只画在自己的布局槽里，落在槽外的几何被整段裁掉 —— 而且不报错。**
    2026-10-03 实测的四条：
    - 几何必须按**视图盒子自己的原点**换算。画布局部坐标常常是负的（demo 的端口就在 `(101,−53)`），
      所以「视图铺满画布 `(0,0,W,H)` + 几何直接用画布坐标」等于把每条 y 为负的连线画到盒子外 —— **一条都看不见**。
    - `Aspect="None"` 是**生效**的（几何不会被拉伸去填盒子）。
    - **盒子不能每帧跟着曲线包围盒走**。写 `AbsoluteLayout.SetLayoutBounds` 是能生效的，但一帧一变时
      视图会停在一两个状态之前的盒子上（实测），正在拖动的橡皮筋因此整条消失。
      现在做法是 `LinkView.EnsureBox`：盒子 = 画布 + 一圈余量，**建一次就不动**，只有连线真的跑出这个区域才长大一次（长大之后又稳定）。
    - **`ViewArranger` 对连线的分支必须保持「不写 bounds」**（`ViewHosting/ViewArranger.cs` 里那段），
      否则池子会把盒子刷回画布尺寸，把上面两条一起作废。
13. **橡皮筋（虚拟连线）画得出来。** 看不到的已知原因是 `SetCursorPos` 驱动不起连线手势（见第 14 条）。
14. **合成输入：本家的**节点拖拽**与**连线手势**只认 `SendInput` 的移动，`SetCursorPos` 驱动的**一个都不生效**。**
    2026-10-03 实测：同一串动作换成 `SendInput`（harness 的 `moveto:`）之后，节点拖拽与橡皮筋立刻都出来了；
    用 `move:`（`SetCursorPos`）则两者都「看着像没反应」—— 而这家的悬停（`PointerMoved` 钩子）又**时而**能收到，
    所以症状是「有时好有时坏」，很容易被误判成坐标不对或功能坏。**验证拖拽类行为一律用 `moveto:`。**
15. **手势的 `PreventDefault` 只到「读得到句柄」。** 平移与节点/插槽的否决已生效
    （不捕获指针、不起拖拽/连线）；**非 Windows 的捏合缩放**没有可对应的滚轮事件，转发不出来，当前否决不了；
    Windows 的 Ctrl+滚轮**否决后要不要退回平台自己的滚动**，全仓七家还没拉平。
    另一条：Windows 的 `RouteSurfacePress` 必须在 `element.CapturePointer` **之前**读句柄（`WorkflowSurfaceBehavior.cs`
    的 `OnPlatformPanPressed`）—— 否决了还去捕获，那一笔要走的宿主定制就拿不到指针。

---

## 五、非 Trimmed demo 连线层的三件事（悬停命中 / Delete / 右键菜单）

本家与另外六家不同形：**连线不是视图**，是满屏 `GraphicsView` 一趟画完（§二·4）。本层因此只做两件平台的事：
**把每条可见链的曲线按 canvas-local 发布给 Core 当命中几何**、**把指针/按键翻译成标准输入事件转发进输入路由**
（`WorkflowLinkOverlay` 的 `AttachInteraction`）。**命中裁决归 Core 的共享判定**（本层调 `input.Tree.HitTestVisibleLinks`，只提供坐标），
**高亮与删除归宿主/demo**，右键请求由本层转发、菜单由表面（适配器）弹 —— 本层**自己不算命中**（没有 `HitTestLink`）。
非 Trimmed demo 的池因此不物化连线（数据源直绑 `Helper.VisibleItems`，由 §二·1 的入队过滤跳过），
模板/demo 的 code-behind 里没有一行过滤代码。

| 事 | 落点 | 依据 |
|---|---|---|
| 谁来收输入 | DP `InteractionSource`（`View`）；宿主绑**页面根**，不绑这层自己 —— 这层 `InputTransparent` 且压在 `ScrollViewer` 下，收不到指针 | `WorkflowLinkOverlay.cs` 的 `InteractionSourceProperty`、构造函数里的 `InputTransparent = true`；`Examples/Workflow/MAUI/Demo/Controls/Workflow/WorkflowView.xaml` 的 `InteractionSource` 绑定 |
| 命中 | 本层在绘制时把曲线按 **canvas-local** 发布（`PublishCurve`，不带 visual）；指针经 `ToCanvasLocal`（`ToViewport` 的逆）转成同一坐标系，本层调 `input.Tree.HitTestVisibleLinks` 逐条判距，半径用 `input.HitRadius`（默认 6，七家同一个 `DefaultHitRadius`） | `WorkflowLinkOverlay.cs` 的 `IDrawable.Draw`（`LinkCurve.BuildLinkCubic` + `PublishCurve`）、`ToCanvasLocal`、`OnHoverMoved`；Core `LinkHitTestEx` 的 `DefaultHitRadius` |
| 高亮 | 本层没有「每线的可视对象」，连线高亮归 **demo 的 `LinkHighlightLayer`**：它订树 helper 的 `Input.PointerEntered` / `PointerExited` 自己画光带，并在 `LinkRemoved` 上清掉旧像素（线被删时指针不动、收不到 `Exited`） | `Examples/Workflow/MAUI/Demo/Controls/Workflow/LinkHighlightLayer.cs` 里订 `PointerEntered` / `PointerExited` / `LinkRemoved` 的那段（Trimmed 同文件同形） |
| 删除 | 本层只把 Delete 键翻成 `KeyEvent` 路由进输入面（`OnSourceKeyDown`，仅当 `input.HoveredLink` 非空）；**执行归 demo** —— 订树 helper 的 `Input.KeyDown` 执行 `link.DeleteCommand`。`KeyUp` 现在也路由（`OnSourceKeyUp`，无悬停筛选，见 §五·7） | `WorkflowLinkOverlay.cs` 的 `OnSourceKeyDown`、`OnSourceKeyUp`；`Examples/Workflow/MAUI/Demo/Controls/Workflow/WorkflowView.xaml.cs` 与 `Examples/Workflow/MAUI Trimmed/Demo/Controls/Workflow/TreeView.xaml.cs` 里订 `Input.KeyDown` 的处理器 |
| 菜单 | 本层不懂菜单：右键（或非 Windows 的长按）翻成 `PointerPressed(Right, …)` 发进输入路由，**表面（适配器）订它、自己弹**。表面 `WorkflowSurfaceBehavior` 按附着属性 `LinkMenuKey` 从模板声明的 `LinkContextMenu` 资源取菜单、按 `RulerBand + 锚点 + 内容偏移 − 滚动偏移` 定位、弹出，`Opened`/`Closed` 由表面自己置/放 `input.IsSuspended`。菜单指着的那条线离开 `tree.Links` 时树报 `LinkRemoved`，**表面只负责收起自己那份弹窗** | `WorkflowSurfaceBehavior.cs` 的 `LinkMenuKeyProperty`、`WireLinkMenu`、两条 `ShowLinkMenu`（Windows / 非 Windows）、`DismissLinkMenu`、`LinkRemoved` 处理、以及置/放 `IsSuspended` 的路径；`WorkflowView.xaml` 的 `LinkMenuKey`、`WorkflowLinkOverlay.cs` 的 `OnPressed` / `OnLongPressTick`（本层转发） |
| 取焦点会不会带滚画布 | **不会** —— `Focus()` 打在 `InteractionSource`（页面根）上，而它是画布 `ScrollView` 的**祖先**；WinUI 的 bring-into-view 只从**焦点元素往上冒**，画布那个 `ScrollViewer` 根本不在那条路上 | `WorkflowLinkOverlay.cs` 的 `FocusHoveredLink`、`OnPressed`；`WorkflowView.xaml`（`Root` 是 ContentView 根，`PART_ScrollViewer` 在它里面） |

结论：

1. **Windows 上不能用 `PointerGestureRecognizer` 收悬停**：挂上去之后 `PointerMoved` 一次都不来（同一次会话里改成
   `AddHandler(UIElement.PointerMovedEvent, …, handledEventsToo: true)` 挂到**同一个** `ContentPanel` 上立刻就有）。
   所以 `AttachPlatformHooks` 走原生路由事件（`PointerMovedEvent` 也挂在 `AttachPlatformHooks` 里），`PointerGestureRecognizer`
   只留在 `#if !WINDOWS`（`AttachInteractionSource` 的非 Windows 分支，moved 回调 `OnGesturePointerMoved`）。方向与 `WorkflowNodeDragBehavior.cs` 的 `Attach` 注释一致 ——
   那边也是嫌它不可靠才不用。
2. **`SelectLink` / `OnPressed` 里仍 `Focus()`，但它已降级为第二道保险**（`FocusHoveredLink`、`OnPressed`）：主修法是结论 7
   把键盘钩子挂到窗口根，键路由不再依赖焦点。保留 Focus 是因为「选中要能立刻接住 Delete」这条观感仍靠它，
   而 `Focus()` 打在**页面根**上（画布的祖先，见上表最后一行），代价只是一次不可见的焦点移动。
3. **非 Windows 的菜单手势是长按，不是右键**：`#if !WINDOWS` 下左键按下起一个非重复定时器，
   `LongPressDelay = 500` ms；位移超过 `LongPressMoveSlop = 8` 设备无关单位或松开就取消计时
   （`OnGesturePointerMoved`、`OnGesturePointerReleased`）。到点 `OnLongPressTick` 发一个**合成右键** `PointerEvent(Pressed, canvasLocal, Right)`
   （`OnLongPressTick`）—— 与 Windows 的真右键同义，输入路由照常判命中，**表面订到这次 `PointerPressed(Right, …)` 后弹菜单**。
   右键按下不走长按（本来就是菜单手势，`OnGesturePointerPressed`）。Windows 不变：右键即弹，原生 flyout。
4. **菜单一开的那发 `PointerExited` 由输入路由挡**：本层 `OnHoverExited` 无条件发 `Exited`（`OnHoverExited` 及其上方注释明写「由输入路由认 `IsSuspended`，本层不再重复拦一遍」），
   `WorkflowInput.ApplyDefault` 在 `IsSuspended` 时直接返回、不改指针目标（`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Events/WorkflowInput.cs` 的 `ApplyDefault`）。挂起/恢复由**表面**自己报：弹出时置 `input.IsSuspended = true`、收起时复位
   （`WorkflowSurfaceBehavior.cs` 的 `ShowLinkMenu` / `DismissLinkMenu`）。**本层没有 `_menuOpen`，平台侧也没有 `IsSuspended` 守卫** —— 这个状态只能有一个家，在输入路由上。
   **「菜单不得比它作用的连线活得久」这条守卫由树的 `LinkRemoved` 实现**：那条线离开 `tree.Links` 时树报 `LinkRemoved`
   （`Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/Components/ITreeComponent.cs` 的 `LinkRemoved`、`Templates/Helpers/TreeHelper.cs` 的 `LinkRemoved`），表面按 `ReferenceEquals(state.MenuLink, link)` 判定后收自己那份弹窗 —— Windows 调原生
   flyout 的 `Hide()`，非 Windows 走 `DismissLinkMenu(state)`（`WorkflowSurfaceBehavior.cs` 的两条 `LinkRemoved` 处理）。收起照常复位 `IsSuspended`，单一责任人不变。
5. **菜单条目来自声明的资源，接线与呈现都在适配层**：模板/demo 在 XAML 里声明 `LinkContextMenu`
   （`WorkflowView.xaml` 的 `LinkMenuKey="LinkContextMenu"` 属性 + 资源里那条
   `MenuFlyoutItem Text="Delete" Command="{Binding DeleteCommand}"`），表面按这个键取菜单。
   **模板/demo 的 code-behind 没有菜单代码**：落点在 `WorkflowSurfaceBehavior.cs` 的
   `WireLinkMenu` / `ShowLinkMenu` / `RunItem`。弹出分两路：
   - **Windows**：把声明的条目翻成原生 `Microsoft.UI.Xaml.Controls.MenuFlyout`（`BuildPlatformMenu`）
     再 `ShowAt` —— 只有原生 flyout 能在指定点弹。这份 flyout 记在 `state.OpenFlyout` 里（`Hide()` 得用它，
     在它的 `Closed` 里清掉），`state.MenuLink` 也在 Windows 上置上，供 `LinkRemoved` 的同一判据用。
   - **非 Windows**：没有跨平台的点弹出物，于是把同一声明物化进**适配器自建**的浮层（`EnsureLinkMenuLayer`：
     建 `Grid`（scrim + `Border`）挂到装饰器最近的一层 `Layout` 上，条目填进 `state.LinkMenuItems`，用算出来的 `Margin` 定位）。
     模板与两个 demo 的 XAML 里没有 `PART_LinkMenuLayer` —— 那层呈现归适配器。
     收起只有 `DismissLinkMenu(state)` 一条路：用户选择、点 scrim、以及树的 `LinkRemoved` 都走它。
   **这份浮层的局限都是设计取舍、不是待修的缺陷**：它是宿主 Layout 的子元素、不是窗口级弹出物（没有平台样式与键盘语义）；
   `Margin` 只在负值时夹到 0、不做贴边翻转；`MenuFlyoutSubItem` 会被 `case MenuFlyoutItem` 吃掉、当成一个
   平铺按钮渲染，嵌套项不展开。没有 `ShowDefaultContextMenu` / `ShowDeleteMenu` 那套内置「删除连线」——
   条目就是用户在资源里写的那些，没声明就没条目。
6. **悬停取焦点不会带滚画布（Avalonia 那条缺陷在本家不存在，实测）**。画布滚到非零偏移
   （HUD 读作 `视口(画布) 320, 195`）后：`SelectLink` → `Focus()` 走 5 轮、外加 3 秒连打，滚动在
   **同一回合 / +250ms / +750ms** 三处都一位没动，HUD 那行逐字相同；真指针 hover（`SendInput` 走完，
   先确认应用收到了指针：`_lastPointer` 从 nil 变成那个点）同样一次没动，而选中确实生效 —— 截图里同一条线
   从静息蓝 `#CC38BDF8` 变成选中白 `#FFFFFFFF`。原因是结构：见上表最后一行，焦点元素是画布的祖先而不是后代。
   反例（说明这套检测看得出「焦点带来的滚」）：往 `PART_Canvas` 里塞一个 `Entry` 放在画布 (2400,120) 再 `Focus()`，
   画布**同一回合**就从 `320,194.667` 跳到 `1292.667,148` ⇒ 这条 `ScrollView` 的「焦点就滚」是**开着**的
   （MAUI 没碰 `BringIntoViewOnFocusChange`，MAUI 的程序集里根本没引用过这个名字）。
   节点卡里的输入框仍该滚进来，不需要关掉自动滚进视口。
   同一轮验了 Delete 没退化：按 hover 那条路选中之后按 Delete 能删。
7. **键盘必须挂在「窗口根」上，不能挂在交互源上**。
   - **症状**：悬停 → Delete 能删；**点一下连线再按 Delete，无声无息**。
   - **根因**：键事件只从**焦点所在的那个元素**往上冒，而平台在处理按下时会把焦点挪到被点的元素上。
     钩子挂在交互源上时，焦点离开那棵子树之后 Delete 不再经过它，且**不报错**。
   - **顺带补的第二处**：`OnPressed` 用同一个 `MouseButton` 左右都发（Windows 分支 `OnGesturePointerPressed`、非 Windows `OnGesturePointerPressed` 的另一段）—— 契约是「按下的那条就是选中的那条」，左键才是「点一下连线」产生的事件。
   - **修法**（`WorkflowLinkOverlay.cs` 的 `AttachKeyHook` / `TryUpgradeKeyHook`）：把 `KeyDownEvent` 挂到
     `element.XamlRoot.Content`（窗口根，`AttachKeyHook`），键路由因此不再依赖焦点落在哪；`XamlRoot` 在 Attach 那一刻**还是
     `null`**（实测），所以升级交给第一次指针移动（`TryUpgradeKeyHook`，由 `OnHoverMoved` 调），
     拿不到窗口根就回退到交互源（`AttachKeyHook` 的 `sourceElement`）。`handledEventsToo: false` 保持不变 —— 聚焦的输入框吃掉 Delete
     改自己光标时必须让它赢（`AttachKeyHook` 的注册参数）。
   - **`KeyUp` 与 `KeyDown` 同址同策略**：`AttachKeyHook` 在同一台窗口根上再挂一个
     `KeyUpEvent`（`AttachKeyHook`；`DetachPlatformHooks` 同摘），`OnSourceKeyUp` 与 `OnSourceKeyDown` 同形，
     但**不置 `Handled`、也不筛悬停** —— 另外六家都无条件上报，键抬起本身没有可删的东西，筛掉只会让订阅者
     在不悬停时收不到配对的 `KeyUp`。实测按 Delete / Escape 各上报一次。
   - **验证**：点击 → Delete、悬停 → Delete、右键/长按弹菜单。
   - **demo 侧的订阅要按「树实例」去重，而且要用一张集合**（`Examples/Workflow/MAUI/Demo/Controls/Workflow/WorkflowView.xaml.cs` 的 `HashSet<TreeViewModel> ProbedTrees`）：demo 的会话会被换上好几轮（A → B → A），只比「上一棵」的话回到 A 时会再订一遍 ⇒ 同一棵树上挂两个订阅，每笔输入送达两次。这是 demo 的写法，不是适配器契约。

**`InputTransparent = true` 不能去掉**（`WorkflowLinkOverlay` 的构造函数，同 §四·6）；**命中半径放大会让画布空白处每一次移动都命中某条线**
（Core `LinkHitTestEx.DefaultHitRadius = 6d`，`LinkHitTestEx.cs`）。

---

## 六、demo 的界面控件放在哪：`MainPage` 是裸宿主

**MAUI 的 `MainPage.xaml` 只是一个满屏宿主**（`<controls:WorkflowView x:Name="WorkflowSurface" Session="{Binding DemoSession}" />`），demo 自己的命令（Save / Select / Load Workflow Demo）与整条侧栏都在**被它托管的 `Controls/Workflow/WorkflowView.xaml`** 里。

⇒ 新控件放 `WorkflowView.xaml`，处理器也只能放**它自己的** code-behind：XAML 的 `Clicked` 处理器必须声明在写出这个名字的那个 XAML 文件的 code-behind 中，所以「标记一处、处理器另一处」是编译不过的。会话经 `WorkflowView.Session` 传进来（`MainPage` 把它设成 `_demo`），与 `MainPage` 手里那个是同一个对象。

（对比：`Avalonia` / `WPF` / `WinUI` / `Blazor` / `Jalium` 的控件在各自宿主外壳的侧栏，`WinForms` 在 `Form1` 的工具栏。节点卡（`Controls/WorkflowNodeCard.cs`）的上下文只有节点 VM，而门与检查点是会话级的。）

## 七、松手回弹：原生 `ScrollViewer` 是第二个滚动驱动者（2026-10-01 实测）

**症状**：非 Trimmed demo 拖动画布，**松开鼠标后画布弹回一段**（用户报告；Trimmed 无此问题）。

**复现**：窗口先最大化（`SetWindowPos` TOPMOST + `ShowWindow(SW_MAXIMIZE)` —— 不这么做别的窗口会压在截图区域上，
测出假的「位移」）；脚本用小步鼠标拖拽（`SendInput` 绝对坐标；一步瞬移不派发 pointer 移动）；在按下前 / 保持时 / 松开后各截一帧，
用采样均差比较（每 6px 取一点，累加两帧的 L1 距离）。结果：

| demo | drag（按下→保持） | 松手后（保持→松开） |
|---|---|---|
| 非 Trimmed | 6.4–8.1 | **6.7–8.3 —— 又动了一次** |
| Trimmed | 16–60 | **0 —— 不动** |

**机制**（探针写 `%TEMP%` 日志）：松手 60ms 后原生 ScrollViewer 自己滚了一次 ——

```
PAN released now=(423,171)
SCROLLED (423,153)
SCROLLED (186,153)      ← X 423 → 186
```

适配器自己的三处 `ScrollToAsync`（平移 / 视口恢复 / 缩放回中）一处都没跑。是**原生 ScrollViewer 在手势结束时把它自己累积的操纵偏移补上**。

**六种改法实测证伪**：

1. 去掉 demo 的 `ScrollView` 上的 `BackgroundColor="Transparent"` —— 无效（MAUI 自己会补画刷）。
2. 关掉 demo 载入后的居中 `ScrollToAsync` —— 无效。
3. `ManipulationMode = Scale`（去掉 Translate 两个轴）—— 回弹没了，**平移也彻底失灵**（drag 7.6 → 0.2）。
4. 保留 Translate + `Horizontal/VerticalScrollMode = Disabled` —— 无效。
5. 把 Translate 给**画布**（验证过确实生效：`LayoutPanel` 的 mode 已改）—— 无效，它照样滚。
6. 画布给 Translate + ScrollViewer 给 `None` —— 无效。

⇒ **它响应的是冒泡到它身上的 manipulation 事件，与管理它自己的 `ManipulationMode` 无关**：只要树里还有 manipulation，它就会滚。

**修法**：让整棵树不再产生 manipulation —— Windows 上平移改走**原生指针事件**（`PART_SurfaceBorder` 的平台元素上的
`PointerPressed/Moved/Released/CaptureLost`，与 `WorkflowNodeDragBehavior` 的 Windows 分支同一个套路），
ScrollViewer 这才可以设成 `ManipulationMode = None`。非 Windows 仍走 `PanGestureRecognizer`，未改动。
效果：非 Trimmed 松手后 6.7–8.3 → **0.4–0.5**（彗星动画的噪声地板），Trimmed 不变（0，且平移照常）。
`ApplyPanTargetAsync` 是从原 `ApplyPanAsync` 里抽出来的核心，两条路径共用夹取 / 扩张 / 应用那段。

**实现时的两个坑**：

- **`StateProperty` 挂在宿主 `ContentView` 上，不在 press source 上。** 照 `WorkflowNodeDragBehavior` 的样子在处理器里读
  `view.GetValue(StateProperty)`，对那个 Border 恒为 `null` ⇒ 钩子静默不装、平移全死、而且**不报错**。
  改成订阅时用一张 `Dictionary<View, SurfaceState>` 登记。
- **绝对目标的写法必须与手势路径同形。** 核心在越界重锚时会写 `PanAnchorTotal* = total`，所以目标要写成
  `anchor − (total − anchorTotal)`；写成 `anchor − total` 会在每次重锚之后再叠加一遍行程 ——
  实测同一次指针位移下 desired 一路 1601 → 2001 → 2401，画布飞出几个屏。

**顺带一条测量陷阱**：探针里「值变了吗」的守卫若写成 `Math.Abs(x - NaN) > 1`，**第一次调用就永远不记录**
（NaN 参与的比较恒为 false，哨兵永远是 NaN）—— 与 §四 那条 NaN 守卫同源，哨兵要用 `double.MinValue`。

## 附：未验证 / 不确定的

- §二·4 的「~16k 设备像素」是代码注释里的数字（`WorkflowLinkOverlay.cs` 的类文档），没有在本仓库实测复现；
  它是**原生 Windows 侧**的限制，Android/iOS 上这一层的必要性未被单独验证。
- `WorkflowMinimapOverlay` 在非 Windows 平台上的拖拽路径（父级 `PointerGestureRecognizer` 越界跟踪，`SubscribeParentGestureCapture` / `OnParentPointerMoved`）
  只有代码依据，没有找到运行期验证记录。
- 本家没有 `WorkflowGridDecorator`（见 §一），所以「装饰器的 `RulerBand` 必须转发给虚拟化 inset」这条
  （`WorkflowSurfaceBehavior.cs` 的 `UpdateGridDecorator`）在本仓库里只由 demo 的实现验证过。

### 跑真 demo 时的两个事实

- **合成指针输入必须走 `SendInput`**：`SetCursorPos` 能把光标挪过去，但 WinUI 不为它派发 `PointerMoved` ——
  按钮不亮、hover 不触发，看起来像功能的锅。同一段测试改用 `SendInput`（`MOUSEEVENTF_MOVE|ABSOLUTE`）后立刻正常。
- **这台机器上同时跑着别家的 demo**，压在下面的窗口收不到指针：测之前要把自己的窗口抬到最上（`SetWindowPos` 带 `HWND_TOPMOST`），
  否则点击与移动全落到别人窗口上。注意 `SetWindowPos` 的 `HWND` 形参必须按指针宽度传（ctypes 里不声明 argtypes 会把 `-1` 当 32 位传，调用静默失效）。
  另外 `WindowFromPoint` 给的是**最深的子窗口**，WinUI 会在顶层窗口下挂子 HWND —— 比「这块是不是我的」要用 `GetAncestor(hwnd, GA_ROOT)`。
- **别家的注入式测试也在驱动同一个物理指针**：光标会被抢走（写进去的位置，下次读回来已经不是它），所以「悬停选中」这类断言
  **必须先断言应用收到了指针**（读 overlay 的 `_lastPointer`），否则会把「输入没到」误判成「功能坏了」。本家这条结论
  （§五·5）有三路互证，不依赖任何单次 hover。
