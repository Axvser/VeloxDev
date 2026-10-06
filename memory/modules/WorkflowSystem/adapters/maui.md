# MAUI — WorkflowSystem 适配器

> **另（2026-10-05）：悬停高亮与 Delete 都搬出适配层了** —— overlay 不再有 `SelectedLink`/`SelectedLinkColor`，
> 删除也不再由库执行；两者都在 demo 里（订 `IInputEvents` 自己画 / 自己执行 `DeleteCommand`）。
> 下文凡提这两个名字的行，按「宿主自己写」读。
> ⚠ **交互那几段记的是 2026-10-04 之前的世界。** 当时 Core 有一个 `LinkInteraction` hub，事件是 `HoverChanged` /
> `LinkPressed` / `ContextMenuRequested` 这些按组件定制的语义事件，高亮由 hub 经 `ILinkHighlight` 点亮。
> **现在**：输入是一套**标准输入**（`WorkflowInput.For(tree).Route(...)` + `IInputEvents`），
> 外观是宿主的，菜单由适配器从 `PointerPressed(Right, link)` 里自己弹、用 `tree.GetHelper().LinkRemoved` 收尾。
> 完整规则见 [WorkflowSystem/architecture.md §3.6](../architecture.md)。**下面凡是提到 hub / 那几个事件名 /
> `ILinkHighlight` 的句子都按这个替换读**；与交互无关的部分（坐标换算、焦点、弹窗平台的怪癖、命中几何）仍然有效。

> 代码：`Src/Adapters/VeloxDev.MAUI/Attached/Workflow/`。
> 七个视图角色的职责、附着属性命名约定、绑定挂哪儿，在 `../extension.md` §3.9 与
> `skills/veloxdev-create-workflow/references/view-layer.md`（七角色表在 `:7-19`）；给使用者的平台说明在
> `skills/veloxdev-create-workflow/references/gui/maui.md`。本文不重抄那两份，只写**这一家的硬限制、刻意背离、改动时的坑**。
> 参考实现是 `Examples/Workflow/MAUI Trimmed/Demo/Controls/Workflow/`（注意在 `Controls/` 下）。

---

## 一、这家必须实现哪些契约成员，为什么是这些

七角色在本家的落点（角色定义见 `view-layer.md:7-19`）：

| 角色 | 本家的实现 | 说明 |
|---|---|---|
| 画布宿主 | `WorkflowSurfaceBehavior.cs` | 唯一的「知晓一切」的类（1738 行）：解析命名控件、喂装饰器、平移、缩放、可见区、连线右键菜单接线 |
| 画布变换 | —— **本家没有这个类** | 见 §三·1；职责由宿主 + `ViewManager` 分担 |
| 视图池 | `ViewPool.cs` / `ViewManager.cs` | 见 §二·1 |
| 节点拖拽 | `WorkflowNodeDragBehavior.cs` | 见 §三·5 |
| 插槽连接 | `WorkflowSlotConnectionBehavior.cs` | 见 §三·6 |
| 插槽布局 | `WorkflowSlotLayoutBehavior.cs` | 见 §二·5 |
| 网格装饰器 / 小地图 | 小地图有 `WorkflowMinimapOverlay.cs`；**装饰器适配器不提供**，由模板/demo 写（`Examples/Workflow/MAUI Trimmed/Demo/Controls/Workflow/WorkflowGridDecorator.cs`） | 与 WPF/Avalonia/WinUI/Jalium 同为「适配器不带装饰器」；只有 Razor 与 WinForms 在适配器里自带一份 |

**另有第八个类，它不是七角色里的任何一个，但删不掉**：`WorkflowLinkOverlay.cs` —— 链接层（画 Core 的可见集），见 §三·2。

本家必须自己写、且别家写法不能照搬的成员就三处：`WorkflowSurfaceBehavior` 里的平移/滚动提交顺序（§二·6/§二·7）、
`WorkflowSlotLayoutBehavior` 的重同步信号源（§二·5）、`WorkflowLinkOverlay` 的整层绘制与裁剪（§二·9/§二·8）。
其余都是与别家同形（附着属性 + `FindByName` 解析 `PART_*`、`UseVirtualization` 走 Core 的 `Viewport`）。

---

## 二、平台硬限制（别家不能照抄这里的哪些前提）

### 1. MAUI 没有 `DispatcherPriority`，所以视图物化与槽位同步都靠 16ms 一次性定时器

- 视图池：`BatchSize = 8`，用一个**非重复**的 16ms `IDispatcherTimer` 逐帧分批建视图，理由写在
  `ViewManager.cs:145-146`（「MAUI lacks WPF's DispatcherPriority.Background，所以用逐帧定时器把视图创建摊到多帧」）。
  分批跑完若还有待建项，**自己再排下一次**（`:181-185`）。
- **选择器物化不了的项在入队前就筛掉**（2026-10-03）：`ViewManager.CanMaterialize`（`ViewManager.cs:420`）问一次选择器，
  拿到模板才进 `_pendingViews`；选择器抛异常（典型：只声明 `NodeTemplate` 的选择器遇到连线）或返回 null 的，
  按类型记进 `_unsupportedTypes`（`:17`）跳过 —— 不排队、不重复抛。于是**只有节点模板的 demo/模板可以把
  `ViewPool.ItemsSource` 直绑 `Helper.VisibleItems`**，连线交给共享链接层画，池不再需要模板侧的过滤包装
  （原 `NodeOnlyVisibleItems` 已从模板与两个 demo 删除）。
- 槽位同步：同样 16ms，但语义是**合并**（同一控件多次请求只留一次 `Sync`），`WorkflowSlotLayoutBehavior.cs:307-343`。
- **这两个 `IsRepeating = false` 与 Transition 侧 pacer 的 `IsRepeating = true` 不矛盾**：pacer 的定时器每拍由
  `FramePacerCore.Fire()` 先 `Disarm()`，重复正是为了让下一拍还在；这两个是「干完这一批就停、需要时再排」。
  照抄 `IsRepeating` 的取值前先确认你要的是哪一种（对位：`Src/Adapters/VeloxDev.MAUI/PlatformAdapters/TransitionInterpreter.cs:40-43`）。
- 两个定时器的 `Tick` 都必须自带 try：`IDispatcherTimer.Tick` 抛出的异常 MAUI 不接，直接冒到 WinUI 的
  `UnhandledException`（dotnet/maui #12245，`ViewManager.cs:197-201`、`WorkflowSlotLayoutBehavior.cs:334-339`）。

### 2. `GraphicsView` 的 `Draw` 回调没有异常保护

dotnet/maui #14567（`WorkflowMinimapOverlay.cs:736-738`）：`Draw` 里抛出的异常（典型是 NaN 坐标）直接进
WinUI 的 `UnhandledException`。所以本家所有绘制都整段包 try（`:732-812`），并且 `dirtyRect.Width/Height`
本身可能是 NaN（`:741-747` 的注释：`NaN > 0` 与 `NaN <= 0` 都是 false，必须显式 `IsNaN`）。

### 3. `GraphicsView` 的触摸事件会带着空/陈旧的 `Touches` 到达

dotnet/maui #13452（`WorkflowMinimapOverlay.cs:523-527`）：`StartInteraction` 可能在 `Touches` 为空时触发，非致命。
所以小地图的三个交互回调都做「空 → 直接 return」并各包一层 try（`:500-528`、`:530-543`、`:545-562`）。

### 4. Win2D 的 ~16k 设备像素纹理上限（决策了整个链接层的形状）

`WorkflowLinkOverlay.cs:9-25` 的类文档：**画布尺寸**的 `GraphicsView` 在深缩放下超过 Win2D 纹理上限，整层**静默消失**。
于是链接层取**视口尺寸**、住在装饰器坐标系里（网格/标尺用的同一身份：
`px = RulerThickness + c + ContentOffset − ScrollOffset`，`:21`），并因此**必须自己做包围盒裁剪**
（`CullMargin = 24`，`:49`，裁剪判定 `:1324`）。别家可以照抄「一个视口尺寸的 overlay」，但**不能省掉裁剪**：
这一层永远是满屏尺寸 —— 满屏的是**视口**，不是内容。
**每帧画哪些线由枚举源决定**：`EnumerateVisibleLinks`（`:1225-1242`）取 Core 的虚拟化可见集（`tree.GetHelper().VisibleItems`
里过滤出 `IWorkflowLinkViewModel`），因此是 O(可见) 而不是 O(全部) —— 与其余六家同源。包围盒裁剪留作第二道筛子：
可见集用的是「两端节点包围盒的并集」，比曲线本身粗，并集擦过视口时曲线仍可能落在外面。
虚拟连线不在 `tree.Links` 里（可见集里那份是同一个实例），所以它从可见集里排除、单独补在最后 —— 橡皮筋要压在实连线之上。
**按可见集裁剪不会漏画真在视口里的线**：`NodePairBoundsProvider.CalculateBounds()` 取的是两端节点包围盒的**并集**
（`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Virtualization/NodePairBoundsProvider.cs` 的 `Viewport.Union(bA, bB)`），
而本家的贝塞尔控制点只在两端之间横向拉开 ⇒ 曲线恒在这四点的凸包内、也就恒在该并集内 ⇒ 并集不与视口相交时曲线也不可能可见。
（对「自定义链接视图画出并集之外的东西」才不成立 —— 其余六家同样如此，属已知局限。）

### 5. 托管的 `SizeChanged` 是 **arrange 途中**，不是 arrange 之后

这是本家历史结论，今天仍然成立，依据是三处注释与它们的实现：
`WorkflowSlotLayoutBehavior.cs:108-113`（`Attach` 里 Windows 装原生信号）、`:170-179`（`TryInstallResizeSignal` 的文档）、
`:256-259`（`OnNodeResized` 的注释：「MAUI 的托管 SizeChanged 在 arrange 途中触发，节点内部槽位还没定型，
测出来的锚点会比最终几何过冲 ~10ms（见 git 历史里的逐格端点弹跳）」）。

- Windows：经 `control.Handler?.PlatformView` 拿 `Microsoft.UI.Xaml.FrameworkElement`，挂**原生** `LayoutUpdated`
  （`:180-207`）—— 那是「整个子树已 arrange 完」的信号；装上原生信号后**立刻摘掉**托管兜底（`:201-206`）。
- 非 Windows：没有框架级 `LayoutUpdated`，只能留托管 `SizeChanged`（`:114-116`）。
- 别家（WinUI/WPF 家族）本来就在 `LayoutUpdated` 系信号上同步，没有这一步；**这条不是「MAUI 的坑」而已，是「照抄别家的信号源会错」**。

### 6. `ScrollToAsync` 是 fire-and-forget 的 `ChangeView`，不保证落在请求值上

`WorkflowSurfaceBehavior.cs:1119-1122` 的注释：MAUI 在每次原生 `ViewChanged` 上都会发 `Scrolled`，`ScrollX/ScrollY` 是那一刻的
原生真值；而 `ScrollToAsync` 可能落不到请求值（Windows 上原生 ScrollViewer 还会自己动内容）。结论：

- **不许**在平移期间抑制 `Scrolled` —— 抑制过装饰器就被冻在「请求值」上，原生最后落在别处时松手一跳（那正是当初调查的「松手跳」）。
- 装饰器与视口**只从 `ScrollX/ScrollY` 写**（`ApplyLayout` 里 `:1205-1209`、`ApplyVisibleRegion` 里 `:1626-1653`），
  请求的目标值从不作为写入源（`:1627` 的注释明写：写请求值会让网格在松手后弹回真位置）。

### 7. 原生 extent **异步**重测，模型是**同步**长大的

滚到边缘时画布扩张：模型侧（`Layout.ActualSize` 与 `ApplyLayout` 写的 `WidthRequest/HeightRequest`）**同一帧**就变了，
而原生内容重测是异步的，快拖时原生 extent 能落后模型几千像素。这造成两条**方向相反**的写法则，都不是笔误：

- **平移**：apply 之前用原生 extent 夹取（`GetNativeScrollMaximum`，`WorkflowSurfaceBehavior.cs:1568-1587`；Windows 专用读
  `ExtentWidth/ViewportWidth`，非 Windows 回退模型 `:1586`）。落地后再把锚点回写到**夹取后**的值（`:1511-1518`），
  否则记账跑在原生前面 → 内容先钉住再跳一帧。夹带一次 `Task.Yield()` 等比原生重测（缩放回中 `:929`/`:944`、`ApplyPendingScrollRestoreCore` `:1711`）。
- **小地图拖动**：最大值必须取**模型**，不能用 `_scrollView.ContentSize`（那个跟着异步布局落后）—— 用 ContentSize 会把目标钉在当前边缘，
  2px 节流随即跳过滚动，画布永远长不大，形成**自锁**（`WorkflowMinimapOverlay.cs:639-644` 的注释；`:659-668` 扩张后重算）。
  它同样在扩张后用 `WorkflowSurfaceBehavior.Refresh` 主动推一次布局（`:660-665`）。

### 8. `Handler.PlatformView` 是唯一通往原生的路，且它有三个时序坑

- **平台元素没有 `DataContext`**：写在平台层的处理器要从闭包里捕获宿主（`WorkflowSurfaceBehavior.cs:822` 的注释明写），
  按 WPF 的写法写会发现「什么也找不到」。
- **`HandlerChanged` 早于平台视图创建**：所以允许一次延后重试，并必须**有界**（`WorkflowMinimapOverlay.cs:206-217` 的
  `_platformAttachPending`；`OnHandlerChanged` 里重置它 `:159-167`）。无界重试 = 视图永不出现时的死循环。
- **换 handler 必须摘钩子**：`OnHandlerChanging` 里把 `PointerPressed/Moved/Released/Canceled/CaptureLost` 逐个 `RemoveHandler`
  （`WorkflowMinimapOverlay.cs:169-195`），否则页面导航后泄漏。

### 9. 本版 `ICanvas` 没有描边渐变，所以「流光」是采样出来的几何而不是渐变笔

类文档 `WorkflowLinkOverlay.cs:27-34`：本版 `ICanvas` 没有描边渐变的等价物（`SetFillPaint` 只在填充侧），
彗星因此是**按弧长切出来的几何**：曲线先按 `LinkCurve.DefaultSampleCount = 128` 采样成弧长表（采样与弧长表归 Core
`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Interaction/LinkCurve.cs:29` 的 `DefaultSampleCount`、`:168` 的 `BuildLinkCubic`；
本层调用在 `WorkflowLinkOverlay.cs:1379`），再在表上取头与尾，用 `TailSegments = 16` 段、每段一个透明度的 `DrawLine` 画出来
（`:302`、`DrawComet :433-475`；两遍：先光晕后本体）。**这条限制带来的好处仍在**：按弧长走的光会跟着弯走，
而渐变刷的轴是两端的连线（弦），光在弯链上会离开绳子跑到弦上。

### 10. 非 Windows 上拿不到原生指针捕获

`WorkflowSlotConnectionBehavior.cs:175-176` 的注释：MAUI 库的 TFM 是平台中立的，所以原生 pointer capture 可能不可用 ——
一旦 `Pan` 手势开始，就让它独占这次拖拽（`:176-178`）。Windows 上则由 `element.CapturePointer` 显式捕获
（`WorkflowNodeDragBehavior.cs:190`、`WorkflowSlotConnectionBehavior.cs:548-565` 的原生分支）。小地图更彻底：Windows 上
**两条独立通道**（原生 `PointerPressed/Moved/Released` 探针 + `PanGestureRecognizer` 的 manipulation 增量）同时供数据，
因为只有捕获所有者能收到越界事件（`WorkflowMinimapOverlay.cs:94-105`、`:197-279`）；并且 Windows 上
`EndInteraction` 必须**直接 return**（`:549-552`）—— MAUI 指针一离开小地图就报 `EndInteraction`，而拖拽还没结束。

**视口往返（2026-10-03 接上挂树口）**：这家本来就有整套恢复机（`RequestViewportRestore` → 调度器 +
`Task.Yield` + `ToScreen` + clamp），但它只对宿主开放，宿主不调就不恢复。现在
`Attach`/`OnLoaded`/`OnBindingContextChanged` 三处都会 `CaptureViewportRestore`（树引用变了才算，
且必须在 `Refresh` **之前** —— 那次 `UpdateVisibleRegion` 会把它覆盖成本地 (0,0)）。同一轮还修了一处坐标
不一致：持久化过去写原始滚动值、恢复却按世界坐标处理，`ActualOffset ≠ 0` 时会加两次；现在两侧都写世界。
见 [../extension.md](../extension.md) §3.9-10。

---

## 三、与其它家的刻意背离

1. **七家里唯一没有 `WorkflowCanvasTransformBehavior` 的一家。** 这里的做法和其他家不一样，因为 MAUI 没有 `Viewbox`，
   而且本家**刻意不使用 RenderTransform**（变换会污染槽位布局读到的 layout 位置，见 demo 的 `NodeView.xaml.cs:18-21`
   「Uses layout-only changes (no render transforms) so the slot-layout behavior's layout-position measurement keeps producing
   correct link anchors」）。于是平移改由两处**代码写几何**承担：整块画布 `TranslationX/Y = Layout.ActualOffset`
   （`WorkflowSurfaceBehavior.cs:1190-1194`，注释说明为什么是画布级而不是逐子元素：同级平移才能让节点/连线/网格同帧一致），
   每一项的落位由 `ViewManager` 每帧 `AbsoluteLayout.SetLayoutBounds`（`ViewManager.cs:391-414`）写。
   **照抄点**：`view-layer.md:52-61` 那条「变换绑定必须挂在 `DataTemplate` 根上」的坑在 MAUI 上**不适用** ——
   这里根本没有那个附着属性可绑。
2. **两个 demo（与模板产物）现在都是「一层」：共享的 `LinkView` 包一个 `WorkflowLinkOverlay`（2026-10-04 复核；「每线一视图」那条路当前无消费者）。**
   `WorkflowLinkOverlay` 是唯一边线宿主：画全部可见连线 + 唯一的光带动画宿主 + 收指针。它仍保留一条跳过判据：
   **一条连线的曲线如果带着 `Visual` 被发布（视图调了 `PublishCurve(curve, this)`），这一层就跳过它** ——
   `WorkflowLinkOverlay.cs` 的 `LinkOverlayDrawable` 里那句（`:1287-1290`）`if (link.HitTarget()?.Visual is not null) continue;`
   （给「每线一视图」留的路，当前仓库里没有视图再走它）。于是：
   - **Trimmed demo**（`Demo/Controls/Workflow/LinkView.xaml(.cs)`）：`ContentView` 包**一个** `WorkflowLinkOverlay`。
     顶部注释写明 per-link GraphicsViews 在深缩放下超 Win2D 纹理上限、已被移除。选择器只给 `NodeTemplate`，
     池只物化节点视图（`TreeView.xaml:34` 的共享 `LinkView` + 只给 `NodeTemplate` 的选择器）。
   - **完整版 demo**：同样「一层」，`WorkflowView.xaml:245` 一个 `WorkflowLinkOverlay`；选择器只给 `NodeTemplate`。
     overlay 照旧画全部连线（含光带），不需要 `LinkTemplate`。
   两条路线共用同一个 hub（`LinkInteraction.For(tree)`）与同一份命中几何，所以交互行为一致。
   **光带仍然只有 overlay 有**（`BandCentre`/`BandMix` 两个标量 + `Transition<WorkflowLinkOverlay>`，
   三段 450/700/450ms 结尾 `Repeat(int.MaxValue)`）；当前没有每线视图，所以「每线视图自己画彗星」**还没做**。
   ⇒ 改这块前先看 `LinkView.xaml` 顶部那段注释：改回每线视图要重付 §二·4 的 Win2D 纹理上限代价。
3. **流光的颜色规则是「本色往白里提」，不是换色**：线体本身是静息的暗线（本色 alpha 降到 55%，`:1356`），
   彗星的每段由本色与白按它在尾上的位置插值、透明度按位置平方衰减、再叠一层更宽更淡的光晕
   （`WorkflowLinkOverlay.cs:433-475`）。与 `view-layer.md:147` 的通则一致，但本家的实现落点在 overlay 的两个标量上。
4. **小地图拖动 = 按下的点直接成为视口中心，没有抓取锚点**。`:513-518` 的注释明写「Match the Jalium adapter」。
5. **节点拖拽按 TFM 分叉**：Windows 走原生 `PointerRoutedEventArgs`（`WorkflowNodeDragBehavior.cs:126-254`），
   非 Windows 走 `PanGestureRecognizer`（`:92-100` + `:271-323`），并且**刻意不加** `PointerGestureRecognizer`
   （`:93-94`：它的 `PointerMoved/Released` 与 Pan 的生命周期打架、没有增益）。别家是单一机制。
   两边的守卫都要认：`IsDraggingNode` 被平移逻辑读（`WorkflowSurfaceBehavior.cs:1237-1246`，拖节点时平移把锚点贴到当前位置，避免节点拖拽结束后平移跳一段）。
6. **插槽锚点写 `SlotAnchorFromCanvasLocal`**（`WorkflowSlotConnectionBehavior.cs:428-430`），因为这里量到的中心**已经是画布局部坐标**：
   `TryGetCenterRelativeTo` 走父链求和，且 `GetLeftInParent/GetTopInParent` 优先取 `AbsoluteLayout.GetLayoutBounds`
   （`Translation` 不在其中，`:456-482`）。**用 `SlotAnchorFromVisualCenter` 会把 `ActualOffset` 减两次**，每条线整体偏移
   （`skills/veloxdev-create-workflow/references/gui/maui.md:39` 同结论）。
7. **Windows 上原生 `ScrollViewer` 是彻底被动的，且平移不再走 manipulation（2026-10-01 重做，见 §五）。**
   `IsScrollInertiaEnabled = false` 且 **`ManipulationMode = None`**（`WorkflowSurfaceBehavior.cs` 的
   `OnScrollViewerHandlerChanged`）。
   **订正**：这里原先写的是「降级成 `ManipulationMode = TranslateX|TranslateY|Scale` 就够，并警告不要用 `None`」——
   那是错的，两半都错。`TranslateX|TranslateY` 正是 ScrollViewer 用来做**操纵滚动**的那两个轴，留着它们它就还是
   第二个滚动驱动者，并会在松手那一刻把自己累积的偏移补上（实测：松手后 60ms 从 423 跳到 186）；
   而 `None` 会让平移失灵这条，只对「平移仍靠 manipulation」那套成立 —— 平移现在改走原生指针事件，
   全树不再需要任何 manipulation，`None` 因此是它该有的取值。详见 §五。
8. **平移用绝对锚点，不用每帧增量累积**：`SurfaceState` 里那组 `PanAccumulated*` / `PanAnchorTotal*`（`:25-37` 的注释）
   在 `Started` 记一次锚，每次 `Running` 用 `anchor − (Total − anchorTotal)` 算绝对目标（`:1273-1277`），
   并让**在飞的上一笔 `ScrollToAsync` 被取消**（`PanCts`，`:46-48`、`:1474-1477`）。注释给的教训：读每帧实际 `ScrollX` 会闪回，
   累积逐帧增量会在被夹取的滚动下漂移（卡住再跳）。

---

## 四、改这里时最容易踩的坑

1. **在平移期间抑制 `OnScrolled`** → 装饰器冻在请求值、松手跳（`WorkflowSurfaceBehavior.cs:1119-1122`）。这条是调查结论，不是猜测。
2. **把 §二·6 与 §二·7 的写法则互相「统一」** → 一边自锁（boundary 不再长大）、一边松手跳。两处方向相反是刻意的。
3. **在 Windows 上把重同步信号换回托管 `SizeChanged`**（或让兜底与原生信号并存）→ 逐格端点弹跳：`~10ms` 的 arrange 途中过冲
   （`WorkflowSlotLayoutBehavior.cs:108-113`、`:170-179`、`:256-259`）。
4. **删掉任何一处 NaN 守卫** → 最小后果是连线永久消失。NaN = 未测量是**跨平台统一机制**，不是本家发明的时序
   （`../extension.md` §3.9-4）：`Viewport`/`Virtualize` 那条路上 NaN 会让**整批 VisibleItems 被清空**
   （`WorkflowSurfaceBehavior.cs:1626` 的注释：`NaN <= 0` 是 false，所以 Core 的守卫抓不到）；
   小地图是成组的（`:395-399` 的节点锚点、`:404-410` 的视口、`:447-455` 的 fit 缩放与原点、`:486-489` 的矩形），
   链接层则在 `TryGetEndpoints` 出口判 NaN（`WorkflowLinkOverlay.cs:1244-1252`）。
5. **给链接层或小地图加新的视觉 DP 却不加 `propertyChanged`** → 改了属性不重画；加了但不合并 → 一帧内多个 DP 写入各发一次
   `Invalidate`（`WorkflowLinkOverlay.cs:1199-1219` 的合流；小地图 `MarkDirty`/`FlushInvalidate` `WorkflowMinimapOverlay.cs:359-380`）。
   `ApplyVisibleRegion` 一帧就连写 6 个 DP（`WorkflowSurfaceBehavior.cs:1679-1684`），合并不是优化而是必需。
6. **去掉 `WorkflowLinkOverlay.InputTransparent = true`**（`:142`）→ 这层满屏盖在节点上，会吞掉全部节点交互。
   它在树里的 z 序靠 XAML 位置（装饰器内、`ScrollView` 之前，`TreeView.xaml:34-41`；`ScrollView` 在 `:42`），挪位置等于改层序。
7. **删掉 `x:Name="Root"`** → 链接层的 `WorkflowTree` 与 `InteractionSource` 是 `Source={x:Reference Root}` 的绑定
   （`TreeView.xaml:8`、`:34-35`），全部解析不到；`ScrollOffset*`/`ContentOffset*`/`RulerThickness` 绑的却是
   `PART_GridDecorator`（`:36-40`），不随 `Root` 一起失效（`skills/veloxdev-create-workflow/references/gui/maui.md:57` 同结论）。
8. **把隐藏视图从 `_layout.Children` 里 `Remove`** → 本家刻意保留子元素、只 `IsVisible=false` + `ZIndex=-100`
   （`ViewManager.cs:286-289`、`ResetAllViews` `:301-323` 的注释：移除会触发昂贵的 MAUI 重排）。
   相应地，隐藏视图仍在树上 —— 遍历子元素时别假设「看不见 = 不在」。
9. **忘了取消在飞的滚动** → `PanCts` 一旦漏取消，`ScrollToAsync` 会叠起来（`:1474-1477`、`:782-783` 的清理）。
10. **删掉 `Refresh` 的 `IsRefreshing` 重入守卫** → 画布扩张 → `Refresh` → 再扩张的级联（`:193-198` 的注释记了
    「每次扩张级联 2-3 次 Refresh，正反馈减速螺旋」）。
11. **给链接层加「可见集之外也要补画」的兜底**（最典型的是把全量 `tree.Links` 再拉回来做差分）→ 又和其余六家分叉，
    而且不需要：新连线进可见集的时机就是 Core 的「量完才画」，与 WinUI 那条修好的行为一致。这条窗口真出问题时
    要报出来，不是在绘制侧绕过。
12. **每线视图（`LinkView`）的盒子：`Path` 只画在自己的布局槽里，落在槽外的几何被整段裁掉 —— 而且不报错。**
    2026-10-03 实测，踩了一路，四条一起记住：
    - 几何必须按**视图盒子自己的原点**换算。画布局部坐标常常是负的（demo 的端口就在 `(101,−53)`），
      所以「视图铺满画布 `(0,0,W,H)` + 几何直接用画布坐标」等于把每条 y 为负的连线画到盒子外 —— **一条都看不见**。
    - `Aspect="None"` 是**生效**的（几何不会被拉伸去填盒子）。别信「MAUI 一定 stretch」的猜测，实测是不拉。
    - **盒子不能每帧跟着曲线包围盒走**。写 `AbsoluteLayout.SetLayoutBounds` 是能生效的，但一帧一变时
      视图会停在一两个状态之前的盒子上（实测），正在拖动的橡皮筋因此整条消失。
      现在做法是 `LinkView.EnsureBox`：盒子 = 画布 + 一圈余量，**建一次就不动**，只有连线真的跑出这个区域才长大一次（长大之后又稳定）。
    - **`ViewManager.ApplyLayout` 对连线的分支必须保持「不写 bounds」**（`ViewManager.cs` 里那段），
      否则池子会把盒子刷回画布尺寸，把上面两条一起作废。
13. **橡皮筋（虚拟连线）在新旧两版都画得出来 —— 我曾误判成「从来没画过」（2026-10-03 订正）。**
    上一版（overlay 画全部连线的时代）与这一版（每线视图）都能画出橡皮筋；我先前那次「回到改动前也看不到」
    是因为用 `SetCursorPos` 驱动拖拽，**连线手势根本没起来**（见下面第 15 条），于是把「没驱动起来」当成了
    「没画出来」，还写进了这条记忆里。教训：截图里「没有某个东西」之前，先确认那次操作**真的发生过**。
14. **⚠ 未修：每线视图的高亮改完不重画（2026-10-03 实测，重复 5+ 次）。** 现象与已知的边界：
    - hub 的 `AutoHighlight` **确实**把 `IsHighlighted` 置到了**正在画这条线的那一个实例**上
      （临时日志：`paint#<hash> hl=True bc=True` 是最后一行），`LinkView.ApplyPaint` 也确实跑了；
    - 而屏幕**一个像素都不变**（同一帧序列里 rest→hover 逐像素差 = 0；同一串动作改成按 Delete，差 = 1574）；
    - 试过且**都无效**：换新画刷、`PART_Halo.IsVisible` 开关、重挂一个新 `Data` 实例、`InvalidateMeasure()`；
    - 反面对照：模型变了（Delete 把连线移出可见集）立刻重画 —— 也就是说重画是被**池子的布局那一路**带出来的，
      视图自己改属性带不出来。
    ⇒ 结论：这一步卡在「视图自身的属性变化没有变成 WinUI 的一次重绘」，**不是** hub 或契约的问题。
    下一步该查的方向：MAUI 的 `ShapeViewHandler` 在这条嵌套（`AbsoluteLayout` 里的 `ContentView` → `Grid` → `Path`）下
    是否把属性映射吃掉了，或改用「把高亮做成另一层/另一种元素」绕开它。
15. **合成输入：本家的**节点拖拽**与**连线手势**只认 `SendInput` 的移动，`SetCursorPos` 驱动的**一个都不生效**。**
    2026-10-03 实测：同一串动作换成 `SendInput`（harness 的 `moveto:`）之后，节点拖拽与橡皮筋立刻都出来了；
    用 `move:`（`SetCursorPos`）则两者都「看着像没反应」—— 而这家的悬停（`PointerMoved` 钩子）又**时而**能收到，
    所以症状是「有时好有时坏」，很容易被误判成坐标不对或功能坏。**验证拖拽类行为一律用 `moveto:`。**
    （这条推翻了本文早先「`SetCursorPos` 也能驱动拖拽」的隐含假设。）

---

## 五、非 Trimmed demo 连线层的三件事（悬停命中 / Delete / 右键菜单）

本家与另外六家不同形：**连线不是视图**，是满屏 `GraphicsView` 一趟画完（§二·4）。本层因此只做两件平台的事：
**把每条可见链的曲线按 canvas-local 发布给 Core 当命中几何**、**把指针/按键翻译成标准输入事件转发进 hub**
（`AttachInteraction` `:923-932`）。命中裁决、高亮互斥、删除与右键请求全归 Core 的 `LinkInteraction`
（一棵树一个 hub，`:55-59`），七家共用一个答案 —— 本层**自己不算命中**（旧的 `HitTestLink` 已删）。
非 Trimmed demo 的池因此不物化连线（数据源直绑 `Helper.VisibleItems`，由 §二·1 的入队过滤跳过），
模板/demo 的 code-behind 里没有一行过滤代码。

| 事 | 落点 | 依据 |
|---|---|---|
| 谁来收输入 | DP `InteractionSource`（`View`）；宿主绑**页面根**，不绑这层自己 —— 这层 `InputTransparent` 且压在 `ScrollViewer` 下，收不到指针 | `WorkflowLinkOverlay.cs:87`、`:142`；`Examples/Workflow/MAUI/Demo/Controls/Workflow/WorkflowView.xaml:246` |
| 命中 | 本层在绘制时把曲线按 **canvas-local** 发布（`PublishCurve`，不带 visual）；指针经 `ToCanvasLocal`（`ToViewport` 的逆）转成同一坐标系交给 hub，hub 用 `HitTestVisibleLinks` 逐条判距，半径 6（七家同一个 `DefaultHitRadius`） | `:1310-1312`、`:622-629`；Core `LinkHitTestEx.cs:18`、`LinkInteraction.cs:309-310` |
| 高亮 | 本层没有「每线的可视对象」，hub 的 `AutoHighlight` 无处可点；改由本层订阅 `HoverChanged`，选中那条换 `SelectedLinkColor`（默认**白** `#FFFFFFFF`，不是红）并整条加粗 1.5（管壁、彗星一起） | `:930`、`:945`、`:1298-1302`、`:1333`、`:54` |
| 删除 | 本层只把 Delete 键翻成 `KeyEvent` 交给 hub（仅当 `HoveredLink` 非空）；hub 的 `AutoDelete` 执行 `DeleteCommand` —— 本层不再订 `LinkDeleteRequested`。连线离开 `Links` 时把选中一并清掉（撤销/别处删也走这条） | `:861-876`、`:926`；Core `LinkInteraction.cs:254`；`:1146` |
| 菜单 | 本层不懂菜单：右键（或非 Windows 的长按）发进 hub 后由 hub 报 `ContextMenuRequested`。**接线在适配层**（2026-10-03 用户改定）：表面 `WorkflowSurfaceBehavior` 按附着属性 `LinkMenuKey` 从模板声明的 `LinkContextMenu` 资源取菜单，自己订 `ContextMenuRequested` / `ContextMenuDismissRequested`、按 `RulerBand + 锚点 + 内容偏移 − 滚动偏移` 定位、弹出、并把 `Opened`/`Closed` 报回 hub。菜单指着的那条线离开 `tree.Links`（Agent / Undo / 别处删都算）时 hub 报 `ContextMenuDismissRequested`，**表面只负责收起自己那份弹窗**（弹窗是平台的，Core 收不了），收起照常报 `Closed`。模板/demo 的 code-behind 因此没有一行菜单代码 | `WorkflowSurfaceBehavior.cs:143`（属性）、`:216`（`WireLinkMenu`）、`:334`/`:397`（两条 `ShowLinkMenu`）、`:468`（`DismissLinkMenu`）；Core `LinkInteraction.cs:262-279`、`:180`、`:284-292`；`WorkflowView.xaml:16`（`LinkMenuKey`）、`WorkflowLinkOverlay.cs:601-603`/`:675-683`（本层转发） |
| 取焦点会不会带滚画布 | **不会** —— `Focus()` 打在 `InteractionSource`（页面根）上，而它是画布 `ScrollView` 的**祖先**；WinUI 的 bring-into-view 只从**焦点元素往上冒**，画布那个 `ScrollViewer` 根本不在那条路上 | `:722`、`:614-617`；`WorkflowView.xaml:246`（`Root` 是 ContentView 根，`PART_ScrollViewer` 在它里面 `:256`） |

七条结论（凡标「实测」的都是这台机器上跑出来的，不是推导）：

1. **Windows 上不能用 `PointerGestureRecognizer` 收悬停**：挂上去之后 `PointerMoved` 一次都不来（同一次会话里改成
   `AddHandler(UIElement.PointerMovedEvent, …, handledEventsToo: true)` 挂到**同一个** `ContentPanel` 上立刻就有）。
   所以 `AttachPlatformHooks`（`:732`）走原生路由事件（`PointerMovedEvent` 挂在 `:762`），`PointerGestureRecognizer`
   只留在 `#if !WINDOWS`（`:513-521`，moved 回调 `:632`）。方向与 `WorkflowNodeDragBehavior.cs:93-94` 的注释一致 ——
   那边也是嫌它不可靠才不用。别照抄「用 PointerGestureRecognizer 做 hover」的通用建议。
2. **`SelectLink` / `OnPressed` 里仍 `Focus()`，但它已降级为第二道保险**（`:720-723`、`:611-617`）：主修法是结论 7
   把键盘钩子挂到窗口根，键路由不再依赖焦点。保留 Focus 是因为「选中要能立刻接住 Delete」这条观感仍靠它，
   而 `Focus()` 打在**页面根**上（画布的祖先，见上表最后一行），代价只是一次不可见的焦点移动。
3. **非 Windows 的菜单手势是长按，不是右键（2026-10-03 新增）**：`#if !WINDOWS` 下左键按下起一个非重复定时器，
   `LongPressDelay = 500` ms（`:112`）；位移超过 `LongPressMoveSlop = 8` 设备无关单位或松开就取消计时
   （`:640-644`、`:671`）。到点 `OnLongPressTick` 发一个**合成右键** `PointerEvent(Pressed, canvasLocal, Right)`
   （`:681`）—— 与 Windows 的真右键同义，hub 照常判命中并报 `ContextMenuRequested`，菜单本身仍由**表面（适配器）**弹。
   右键按下不走长按（本来就是菜单手势，`:663-666`）。Windows 不变：右键即弹，原生 flyout。
4. **菜单一开的那发 `PointerExited` 现在由 Core 收**：本层 `OnHoverExited` 无条件发 `Exited`（`:589-592`），
   由 hub 的 `IsSuspended` 挡掉（`LinkInteraction.cs:187-191`）。挂起/恢复由**表面**报：弹出时
   `Publish(ContextMenuEvent(Opened))`、关掉时 `Publish(..., Closed)`（`WorkflowSurfaceBehavior.cs` 的 `ShowLinkMenu` /
   `DismissLinkMenu`；Core `:262-279`）。**本层的 `_menuOpen` 与平台侧 `IsSuspended` 守卫都已删除** —— 这个状态只能有一个家，现在在 hub 上。
   **菜单不得比它作用的连线活得久，这条守卫也在 Core**：那条线离开 `tree.Links` 时 hub 报 `ContextMenuDismissRequested`
   （Core `:180`、`:284-292`），表面按 `ReferenceEquals(state.MenuLink, e.Link)` 判定后收自己那份弹窗 —— Windows 调原生
   flyout 的 `Hide()`，非 Windows 走 `DismissLinkMenu(state)`。随后照常报 `Closed`，`IsSuspended` 由此放开；
   hub 刻意不自己放这个状态，单一责任人不变。
5. **菜单条目来自声明的资源，接线与呈现都在适配层（2026-10-03 改定）**：模板/demo 在 XAML 里声明 `LinkContextMenu`
   （`WorkflowView.xaml:16` 的 `LinkMenuKey="LinkContextMenu"` 属性 + 资源里那条
   `MenuFlyoutItem Text="Delete" Command="{Binding DeleteCommand}"`），表面按这个键取菜单。
   **模板/demo 的 code-behind 因此没有一行菜单代码**：`UpdateInteraction` / `ShowLinkMenu` / `BuildPlatformMenu` / `RunItem` /
   `SelectMenuItem` / `DismissLinkMenu` / `OnLinkMenuScrimTapped` 那整套都已删，落进 `WorkflowSurfaceBehavior.cs` 的
   `WireLinkMenu` / `ShowLinkMenu` / `RunItem`。弹出分两路：
   - **Windows**：把声明的条目翻成原生 `Microsoft.UI.Xaml.Controls.MenuFlyout`（`BuildPlatformMenu`）
     再 `ShowAt` —— 只有原生 flyout 能在指定点弹。这份 flyout 记在 `state.OpenFlyout` 里（`Hide()` 得用它，
     在它的 `Closed` 里清掉），`state.MenuLink` 也在 Windows 上置上，供 `ContextMenuDismissRequested` 的同一判据用。
   - **非 Windows**：没有跨平台的点弹出物，于是把同一声明物化进**适配器自建**的浮层（`EnsureLinkMenuLayer:484`：
     建 `Grid`（scrim + `Border`）挂到装饰器最近的一层 `Layout` 上，条目填进 `state.LinkMenuItems`，用算出来的 `Margin` 定位）。
     **`PART_LinkMenuLayer` 那层标记已从模板与两个 demo 的 XAML 里删除** —— 它本来就是呈现，归适配器。
     收起只有 `DismissLinkMenu(state)` 一条路：用户选择、点 scrim、以及 `ContextMenuDismissRequested` 都走它。
   **这份浮层的局限都是设计取舍、不是待修的缺陷**：它是宿主 Layout 的子元素、不是窗口级弹出物（没有平台样式与键盘语义）；
   `Margin` 只在负值时夹到 0、不做贴边翻转；`MenuFlyoutSubItem` 会被 `case MenuFlyoutItem` 吃掉、当成一个
   平铺按钮渲染，嵌套项不展开。`ShowDefaultContextMenu` / `ShowDeleteMenu` 那套内置「删除连线」已删除 ——
   条目就是用户在资源里写的那些，没声明就没条目。
6. **悬停取焦点不会带滚画布（Avalonia/Jalium 那条缺陷在本家不存在，实测）**。画布滚到非零偏移
   （HUD 读作 `视口(画布) 320, 195`）后：`SelectLink` → `Focus()` 走 5 轮、外加 3 秒连打，滚动在
   **同一回合 / +250ms / +750ms** 三处都一位没动，HUD 那行逐字相同；真指针 hover（`SendInput` 走完，
   先确认应用收到了指针：`_lastPointer` 从 nil 变成那个点）同样一次没动，而选中确实生效 —— 截图里同一条线
   从静息蓝 `#CC38BDF8` 变成选中白 `#FFFFFFFF`。**原因是结构而非运气**：见上表最后一行，焦点元素是画布的祖先而不是后代。
   反例（说明这套检测看得出「焦点带来的滚」）：往 `PART_Canvas` 里塞一个 `Entry` 放在画布 (2400,120) 再 `Focus()`，
   画布**同一回合**就从 `320,194.667` 跳到 `1292.667,148` ⇒ 这条 `ScrollView` 的「焦点就滚」是**开着**的
   （MAUI 没碰 `BringIntoViewOnFocusChange`，MAUI 的程序集里根本没引用过这个名字）。
   **所以别为了「保险」去关掉自动滚进视口**：节点卡里的输入框仍该滚进来，本家不需要任何修补。
   同一轮也验了 Delete 没退化：按 hover 那条路选中之后真按一次 Delete，`Links` 12 → 11。
7. **键盘必须挂在「窗口根」上，不能挂在交互源上（2026-10-03 修，用户报「点一下再 Delete 删不掉」）**。
   - **症状**：悬停 → Delete 能删；**点一下连线再按 Delete，无声无息**。
   - **根因**：键事件只从**焦点所在的那个元素**往上冒，而平台在处理按下时会把焦点挪到被点的元素上
     （临时探针实测：点击前后焦点都是某个 `MauiButton`）。键钩子原来挂在交互源的元素上，焦点离开那棵
     子树之后 Delete 就不再经过它 —— 而且**不报错**。`SelectLink` 里那次 `Focus()` 也救不回来：
     它返回 `true`，焦点却没动。
   - **顺带补的第二处**：本家原来只转发**右键**（`OnSecondaryPressed`），而六家（WPF/Avalonia/WinUI/WinForms）
     左右键都转发 —— 契约是「按下的那条就是选中的那条」，左键才是「点一下连线」产生的事件。现在 `OnPressed`
     用同一个 `PointerButtonKind` 左右都发（`:601-603`；Windows 分支 `:752-754`、非 Windows `:653-655`）。
   - **修法**（`WorkflowLinkOverlay.cs` 的 `AttachKeyHook` / `TryUpgradeKeyHook`）：把 `KeyDownEvent` 挂到
     `element.XamlRoot.Content`（窗口根 `:776`），键路由因此不再依赖焦点落在哪；`XamlRoot` 在 Attach 那一刻**还是
     `null`**（实测），所以升级交给第一次指针移动（`TryUpgradeKeyHook` `:794-806`，由 `OnHoverMoved` `:582` 调），
     拿不到窗口根就回退到交互源（`:782-790`）。`handledEventsToo: false` 保持不变 —— 聚焦的输入框吃掉 Delete
     改自己光标时必须让它赢（`:789`）。
   - **验证**：点击 → Delete（HUD `连线 1/1 → 0/0`）、悬停 → Delete、右键/长按弹菜单；
     适配器五个 TFM + 两个 demo 0 警告 0 错误、Core 1012 测试全过。

改这块时的两条禁令：**别去掉 `InputTransparent = true`**（`:142`，同 §四·6）；**别把命中半径放大**
（Core `LinkHitTestEx.DefaultHitRadius = 6d`，`LinkHitTestEx.cs:18`）—— 那会让画布空白处每一次移动都命中某条线。

---

## 六、demo 的界面控件放在哪：`MainPage` 是裸宿主

2026-09-27 把「运行控制」（Pause / Resume / 从检查点继续）接进七家时的一条：**MAUI 的 `MainPage.xaml` 只是一个满屏宿主**（`<controls:WorkflowView x:Name="WorkflowSurface" Session="{Binding DemoSession}" />`，全文件十来行），demo 自己的命令（Save / Select / Load Workflow Demo）与整条侧栏都在**被它托管的 `Controls/Workflow/WorkflowView.xaml`** 里。

⇒ 新控件放 `WorkflowView.xaml`，处理器也只能放**它自己的** code-behind：XAML 的 `Clicked` 处理器必须声明在写出这个名字的那个 XAML 文件的 code-behind 中，所以「标记一处、处理器另一处」是编译不过的。会话经 `WorkflowView.Session` 传进来（`MainPage` 把它设成 `_demo`），与 `MainPage` 手里那个是同一个对象。

（对比：`Avalonia` / `WPF` / `WinUI` / `Blazor` / `Jalium` 的控件放各自宿主外壳的侧栏，`WinForms` 放 `Form1` 的工具栏。**不要放节点卡上** —— 卡（`Controls/WorkflowNodeCard.cs`）的上下文只有节点 VM，而门与检查点是会话级的。）

## 七、松手回弹：原生 `ScrollViewer` 是第二个滚动驱动者（2026-10-01 实测确诊并修复）

**症状**：非 Trimmed demo 拖动画布，**松开鼠标后画布弹回一段**（用户报告；Trimmed 无此问题）。

**复现**（可重复，4/4）：窗口先最大化（`SetWindowPos` TOPMOST + `ShowWindow(SW_MAXIMIZE)` —— 不这么做别的窗口会压在截图区域上，
测出假的「位移」）；脚本用小步鼠标拖拽（`SendInput` 绝对坐标；一步瞬移不派发 pointer 移动）；在按下前 / 保持时 / 松开后各截一帧，
用采样均差比较（每 6px 取一点，累加两帧的 L1 距离）。结果：

| demo | drag（按下→保持） | 松手后（保持→松开） |
|---|---|---|
| 非 Trimmed | 6.4–8.1 | **6.7–8.3 —— 又动了一次，4/4** |
| Trimmed | 16–60 | **0 —— 不动，4/4** |

**机制**（临时探针写 `%TEMP%` 日志，做法同 §四）：松手 60ms 后原生 ScrollViewer 自己滚了一次 ——

```
PAN released now=(423,171)
SCROLLED (423,153)
SCROLLED (186,153)      ← X 423 → 186
```

适配器自己的三处 `ScrollToAsync`（平移 / 视口恢复 / 缩放回中）一处都没跑。是**原生 ScrollViewer 在手势结束时把它自己累积的操纵偏移补上**。

**六种改法全部实测证伪，别再试**：

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

**实现时踩到的两个坑（都不是 MAUI 的锅，是写错了）**：

- **`StateProperty` 挂在宿主 `ContentView` 上，不在 press source 上。** 照 `WorkflowNodeDragBehavior` 的样子在处理器里读
  `view.GetValue(StateProperty)`，对那个 Border 恒为 `null` ⇒ 钩子静默不装、平移全死、而且**不报错**。
  改成订阅时用一张 `Dictionary<View, SurfaceState>` 登记。
- **绝对目标的写法必须与手势路径同形。** 核心在越界重锚时会写 `PanAnchorTotal* = total`，所以目标要写成
  `anchor − (total − anchorTotal)`；写成 `anchor − total` 会在每次重锚之后再叠加一遍行程 ——
  实测同一次指针位移下 desired 一路 1601 → 2001 → 2401，画布飞出几个屏。

**顺带一条测量陷阱**：探针里「值变了吗」的守卫若写成 `Math.Abs(x - NaN) > 1`，**第一次调用就永远不记录**
（NaN 参与的比较恒为 false，哨兵永远是 NaN）—— 与 §四 那条 NaN 守卫同源，哨兵要用 `double.MinValue`。

## 附：写这份档案时**没能验证 / 不确定**的

- §二·4 的「~16k 设备像素」是代码注释里的数字（`WorkflowLinkOverlay.cs:17`），没有在本仓库实测复现；
  它是**原生 Windows 侧**的限制，Android/iOS 上这一层的必要性未被单独验证。
- `WorkflowMinimapOverlay` 在非 Windows 平台上的拖拽路径（父级 `PointerGestureRecognizer` 越界跟踪，`:583-613`）
  只有代码依据，没有找到运行期验证记录。
- 本家没有 `WorkflowGridDecorator`（见 §一），所以「装饰器的 `RulerBand` 必须转发给虚拟化 inset」这条
  （`WorkflowSurfaceBehavior.cs:1668-1669`）在本仓库里只由 demo 的实现验证过。

### 验证这块时用到的两个事实（下次还要跑真 demo 的话）

- **合成指针输入必须走 `SendInput`**：`SetCursorPos` 能把光标挪过去，但 WinUI 不为它派发 `PointerMoved` ——
  按钮不亮、hover 不触发，看起来像功能的锅。同一段测试改用 `SendInput`（`MOUSEEVENTF_MOVE|ABSOLUTE`）后立刻正常。
- **这台机器上同时跑着别家的 demo**，压在下面的窗口收不到指针：测之前要把自己的窗口抬到最上（`SetWindowPos` 带 `HWND_TOPMOST`），
  否则点击与移动全落到别人窗口上。注意 `SetWindowPos` 的 `HWND` 形参必须按指针宽度传（ctypes 里不声明 argtypes 会把 `-1` 当 32 位传，调用静默失效）。
  另外 `WindowFromPoint` 给的是**最深的子窗口**，WinUI 会在顶层窗口下挂子 HWND —— 比「这块是不是我的」要用 `GetAncestor(hwnd, GA_ROOT)`。
- **别家的注入式测试也在驱动同一个物理指针**：光标会被抢走（写进去的位置，下次读回来已经不是它），所以「悬停选中」这类断言
  **必须先断言应用收到了指针**（读 overlay 的 `_lastPointer`），否则会把「输入没到」误判成「功能坏了」。本家这条结论
  （§五·5）是三路互证：`SelectLink` 直调 5 轮 + 3 秒连打、真指针 hover 成功那一轮、以及焦点反例 —— 不依赖任何单次 hover。
