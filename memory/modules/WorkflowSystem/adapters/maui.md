# MAUI — WorkflowSystem 适配器

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
| 画布宿主 | `WorkflowSurfaceBehavior.cs` | 唯一的「知晓一切」的类（1203 行）：解析命名控件、喂装饰器、平移、缩放、可见区 |
| 画布变换 | —— **本家没有这个类** | 见 §三·1；职责由宿主 + `ViewManager` 分担 |
| 视图池 | `ViewPool.cs` / `ViewManager.cs` | 见 §二·1 |
| 节点拖拽 | `WorkflowNodeDragBehavior.cs` | 见 §三·5 |
| 插槽连接 | `WorkflowSlotConnectionBehavior.cs` | 见 §三·6 |
| 插槽布局 | `WorkflowSlotLayoutBehavior.cs` | 见 §二·5 |
| 网格装饰器 / 小地图 | 小地图有 `WorkflowMinimapOverlay.cs`；**装饰器适配器不提供**，由模板/demo 写（`Examples/Workflow/MAUI Trimmed/Demo/Controls/Workflow/WorkflowGridDecorator.cs`） | 与 WPF/Avalonia/WinUI/WinForms 同为「不带装饰器」；Jalium 与 Razor 自带一个 |

**另有第八个类，它不是七角色里的任何一个，但删不掉**：`WorkflowLinkOverlay.cs` —— 链接层（画 Core 的可见集），见 §三·2。

本家必须自己写、且别家写法不能照搬的成员就三处：`WorkflowSurfaceBehavior` 里的平移/滚动提交顺序（§二·6/§二·7）、
`WorkflowSlotLayoutBehavior` 的重同步信号源（§二·5）、`WorkflowLinkOverlay` 的整层绘制与裁剪（§二·9/§二·8）。
其余都是与别家同形（附着属性 + `FindByName` 解析 `PART_*`、`UseVirtualization` 走 Core 的 `Viewport`）。

---

## 二、平台硬限制（别家不能照抄这里的哪些前提）

### 1. MAUI 没有 `DispatcherPriority`，所以视图物化与槽位同步都靠 16ms 一次性定时器

- 视图池：`BatchSize = 8`，用一个**非重复**的 16ms `IDispatcherTimer` 逐帧分批建视图，理由写在
  `ViewManager.cs:124-143`（「MAUI lacks WPF's DispatcherPriority.Background，所以用逐帧定时器把视图创建摊到多帧」）。
  分批跑完若还有待建项，**自己再排下一次**（`:171-175`）。
- 槽位同步：同样 16ms，但语义是**合并**（同一控件多次请求只留一次 `Sync`），`WorkflowSlotLayoutBehavior.cs:321-366`。
- **这两个 `IsRepeating = false` 与 Transition 侧 pacer 的 `IsRepeating = true` 不矛盾**：pacer 的定时器每拍由
  `FramePacerCore.Fire()` 先 `Disarm()`，重复正是为了让下一拍还在；这两个是「干完这一批就停、需要时再排」。
  照抄 `IsRepeating` 的取值前先确认你要的是哪一种（对位：`Src/Adapters/VeloxDev.MAUI/PlatformAdapters/TransitionInterpreter.cs:40-43`）。
- 两个定时器的 `Tick` 都必须自带 try：`IDispatcherTimer.Tick` 抛出的异常 MAUI 不接，直接冒到 WinUI 的
  `UnhandledException`（dotnet/maui #12245，`ViewManager.cs:189-194`、`WorkflowSlotLayoutBehavior.cs:355-362`）。

### 2. `GraphicsView` 的 `Draw` 回调没有异常保护

dotnet/maui #14567（`WorkflowMinimapOverlay.cs:773-776`）：`Draw` 里抛出的异常（典型是 NaN 坐标）直接进
WinUI 的 `UnhandledException`。所以本家所有绘制都整段包 try（`:770-851`），并且 `dirtyRect.Width/Height`
本身可能是 NaN（`:779-786` 的注释：`NaN > 0` 与 `NaN <= 0` 都是 false，必须显式 `IsNaN`）。

### 3. `GraphicsView` 的触摸事件会带着空/陈旧的 `Touches` 到达

dotnet/maui #13452（`WorkflowMinimapOverlay.cs:547-551`）：`StartInteraction` 可能在 `Touches` 为空时触发，非致命。
所以小地图的三个交互回调都做「空 → 直接 return」并各包一层 try（`:522-552`、`:555-568`、`:570-589`）。

### 4. Win2D 的 ~16k 设备像素纹理上限（决策了整个链接层的形状）

`WorkflowLinkOverlay.cs:9-24` 的类文档：**画布尺寸**的 `GraphicsView` 在深缩放下超过 Win2D 纹理上限，整层**静默消失**。
于是链接层取**视口尺寸**、住在装饰器坐标系里（网格/标尺用的同一身份：
`px = RulerThickness + c + ContentOffset − ScrollOffset`，`:19`），并因此**必须自己做包围盒裁剪**
（`CullMargin = 24`，`:36`，裁剪判定 `:838`）。别家可以照抄「一个视口尺寸的 overlay」，但**不能省掉裁剪**：
这一层永远是满屏尺寸 —— 满屏的是**视口**，不是内容。
**每帧画哪些线由枚举源决定**：`EnumerateVisibleLinks`（`:746-763`）取 Core 的虚拟化可见集（`tree.GetHelper().VisibleItems`
里过滤出 `IWorkflowLinkViewModel`），因此是 O(可见) 而不是 O(全部) —— 与其余六家同源。包围盒裁剪留作第二道筛子：
可见集用的是「两端节点包围盒的并集」，比曲线本身粗，并集擦过视口时曲线仍可能落在外面。
虚拟连线不在 `tree.Links` 里（可见集里那份是同一个实例），所以它从可见集里排除、单独补在最后 —— 橡皮筋要压在实连线之上。
**按可见集裁剪不会漏画真在视口里的线**：`NodePairBoundsProvider.CalculateBounds()` 取的是两端节点包围盒的**并集**
（`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Virtualization/NodePairBoundsProvider.cs` 的 `Viewport.Union(bA, bB)`），
而本家的贝塞尔控制点只在两端之间横向拉开 ⇒ 曲线恒在这四点的凸包内、也就恒在该并集内 ⇒ 并集不与视口相交时曲线也不可能可见。
（对「自定义链接视图画出并集之外的东西」才不成立 —— 其余六家同样如此，属已知局限。）

### 5. 托管的 `SizeChanged` 是 **arrange 途中**，不是 arrange 之后

这是本家历史结论，今天仍然成立，依据是三处注释与它们的实现：
`WorkflowSlotLayoutBehavior.cs:108-115`（`Attach` 里 Windows 装原生信号）、`:176-185`（`TryInstallResizeSignal` 的文档）、
`:266-271`（`OnNodeResized` 的注释：「MAUI 的托管 SizeChanged 在 arrange 途中触发，节点内部槽位还没定型，
测出来的锚点会比最终几何过冲 ~10ms（见 git 历史里的逐格端点弹跳）」）。

- Windows：经 `control.Handler?.PlatformView` 拿 `Microsoft.UI.Xaml.FrameworkElement`，挂**原生** `LayoutUpdated`
  （`:186-207`）—— 那是「整个子树已 arrange 完」的信号；装上原生信号后**立刻摘掉**托管兜底（`:209-215`）。
- 非 Windows：没有框架级 `LayoutUpdated`，只能留托管 `SizeChanged`（`:118-122`）。
- 别家（WinUI/WPF 家族）本来就在 `LayoutUpdated` 系信号上同步，没有这一步；**这条不是「MAUI 的坑」而已，是「照抄别家的信号源会错」**。

### 6. `ScrollToAsync` 是 fire-and-forget 的 `ChangeView`，不保证落在请求值上

`WorkflowSurfaceBehavior.cs:723-732` 的注释：MAUI 在每次原生 `ViewChanged` 上都会发 `Scrolled`，`ScrollX/ScrollY` 是那一刻的
原生真值；而 `ScrollToAsync` 可能落不到请求值（Windows 上原生 ScrollViewer 还会自己动内容）。结论：

- **不许**在平移期间抑制 `Scrolled` —— 抑制过装饰器就被冻在「请求值」上，原生最后落在别处时松手一跳（那正是当初调查的「松手跳」）。
- 装饰器与视口**只从 `ScrollX/ScrollY` 写**（`ApplyLayout` 里 `:818-822`、`ApplyVisibleRegion` 里 `:1090-1103`），
  请求的目标值从不作为写入源（`:969-973` 的注释明写：写请求值会让网格在松手后弹回真位置）。

### 7. 原生 extent **异步**重测，模型是**同步**长大的

滚到边缘时画布扩张：模型侧（`Layout.ActualSize` 与 `ApplyLayout` 写的 `WidthRequest/HeightRequest`）**同一帧**就变了，
而原生内容重测是异步的，快拖时原生 extent 能落后模型几千像素。这造成两条**方向相反**的写法则，都不是笔误：

- **平移**：apply 之前用原生 extent 夹取（`GetNativeScrollMaximum`，`WorkflowSurfaceBehavior.cs:1012-1042`；Windows 专用读
  `ExtentWidth/ViewportWidth`，非 Windows 回退模型 `:1040`）。落地后再把锚点回写到**夹取后**的值（`:959-967`），
  否则记账跑在原生前面 → 内容先钉住再跳一帧。夹带一次 `Task.Yield()` 等比原生重测（`:539`、`ApplyPendingScrollRestoreCore` `:1174-1176`、缩放回中 `:519`）。
- **小地图拖动**：最大值必须取**模型**，不能用 `_scrollView.ContentSize`（那个跟着异步布局落后）—— 用 ContentSize 会把目标钉在当前边缘，
  2px 节流随即跳过滚动，画布永远长不大，形成**自锁**（`WorkflowMinimapOverlay.cs:668-679` 的注释；`:688-705` 扩张后重算）。
  它同样在扩张后用 `WorkflowSurfaceBehavior.Refresh` 主动推一次布局（`:697-705`）。

### 8. `Handler.PlatformView` 是唯一通往原生的路，且它有三个时序坑

- **平台元素没有 `DataContext`**：写在平台层的处理器要从闭包里捕获宿主（`WorkflowSurfaceBehavior.cs:408-411` 的注释明写），
  按 WPF 的写法写会发现「什么也找不到」。
- **`HandlerChanged` 早于平台视图创建**：所以允许一次延后重试，并必须**有界**（`WorkflowMinimapOverlay.cs:214-227` 的
  `_platformAttachPending`；`OnHandlerChanged` 里重置它 `:165-174`）。无界重试 = 视图永不出现时的死循环。
- **换 handler 必须摘钩子**：`OnHandlerChanging` 里把 `PointerPressed/Moved/Released/Canceled/CaptureLost` 逐个 `RemoveHandler`
  （`WorkflowMinimapOverlay.cs:176-203`），否则页面导航后泄漏。

### 9. 本版 `ICanvas` 没有描边渐变，所以「流光」是采样出来的几何而不是渐变笔

类文档 `WorkflowLinkOverlay.cs:25-32`：本版 `ICanvas` 没有描边渐变的等价物（`SetFillPaint` 只在填充侧），
彗星因此是**按弧长切出来的几何**：曲线先按 `SampleCount = 128` 采样成弧长表（`:216`、`BuildCurve :253-294`），
再在表上取头与尾，用 `TailSegments = 16` 段、每段一个透明度的 `DrawLine` 画出来
（`:223`、`DrawComet :391-433`；两遍：先光晕后本体）。**这条限制带来的好处仍在**：按弧长走的光会跟着弯走，
而渐变刷的轴是两端的连线（弦），光在弯链上会离开绳子跑到弦上。

### 10. 非 Windows 上拿不到原生指针捕获

`WorkflowSlotConnectionBehavior.cs:175-177` 的注释：MAUI 库的 TFM 是平台中立的，所以原生 pointer capture 可能不可用 ——
一旦 `Pan` 手势开始，就让它独占这次拖拽（`:177-179`）。Windows 上则由 `element.CapturePointer` 显式捕获
（`WorkflowNodeDragBehavior.cs:193`、`WorkflowSlotConnectionBehavior.cs:551-568` 的原生分支）。小地图更彻底：Windows 上
**两条独立通道**（原生 `PointerPressed/Moved/Released` 探针 + `PanGestureRecognizer` 的 manipulation 增量）同时供数据，
因为只有捕获所有者能收到越界事件（`WorkflowMinimapOverlay.cs:94-108`、`:205-293`）；并且 Windows 上
`EndInteraction` 必须**直接 return**（`:574-579`）—— MAUI 指针一离开小地图就报 `EndInteraction`，而拖拽还没结束。

---

## 三、与其它家的刻意背离

1. **七家里唯一没有 `WorkflowCanvasTransformBehavior` 的一家。** 这里的做法和其他家不一样，因为 MAUI 没有 `Viewbox`，
   而且本家**刻意不使用 RenderTransform**（变换会污染槽位布局读到的 layout 位置，见 demo 的 `NodeView.xaml.cs:26-29`
   「Uses layout-only changes (no render transforms) so the slot-layout behavior's layout-position measurement keeps producing
   correct link anchors」）。于是平移改由两处**代码写几何**承担：整块画布 `TranslationX/Y = Layout.ActualOffset`
   （`WorkflowSurfaceBehavior.cs:796-812`，注释说明为什么是画布级而不是逐子元素：同级平移才能让节点/连线/网格同帧一致），
   每一项的落位由 `ViewManager` 每帧 `AbsoluteLayout.SetLayoutBounds`（`ViewManager.cs:387-409`）写。
   **照抄点**：`view-layer.md:50-61` 那条「变换绑定必须挂在 `DataTemplate` 根上」的坑在 MAUI 上**不适用** ——
   这里根本没有那个附着属性可绑。
2. **链接是「一层」而不是「每线一视图」，且这一层是本家唯一的链接动画宿主。** 枚举源与其余六家同源
   （Core 的可见集），差别只在喂给谁：别家把可见集喂给每线一视图，本家喂给这唯一一层。
   这与 WPF/Avalonia/WinUI 的每线一视图（demo 的 `PolylineCurveView`，光带写 `GradientStops[i].Offset` / `.Color`）是两条不同路线：
   本家把 `FlowBrush` 换成两个标量 `BandCentre`/`BandMix`，由**一个** `Transition<WorkflowLinkOverlay>` 链驱动
   （`WorkflowLinkOverlay.cs:106-126`、`:145-172` 三段 450/700/450ms 结尾 `Repeat(int.MaxValue)`、`:175-186` 起动、`:189-190` 停止），
   每帧所有线读同一对值（`:393-394`）。**所以本家没有「每条线各自起一条链」的问题，也不该照抄那份做法**。
   视图池因此只物化节点：模板选择器里只有 `NodeTemplate`，`LinkTemplate` 已废
   （`Examples/Workflow/MAUI Trimmed/Demo/Controls/Workflow/TreeView.xaml:14-17`）。
3. **流光的颜色规则是「本色往白里提」，不是换色**：线体本身是静息的暗线（本色 alpha 降到 55%，`:867`），
   彗星的每段由本色与白按它在尾上的位置插值、透明度按位置平方衰减、再叠一层更宽更淡的光晕
   （`WorkflowLinkOverlay.cs:400-432`）。与 `view-layer.md:104-107` 的通则一致，但本家的实现落点在 overlay 的两个标量上。
4. **小地图拖动 = 按下的点直接成为视口中心，没有抓取锚点**。`:535-542` 的注释明写「Match the Jalium adapter」。
5. **节点拖拽按 TFM 分叉**：Windows 走原生 `PointerRoutedEventArgs`（`WorkflowNodeDragBehavior.cs:129-257`），
   非 Windows 走 `PanGestureRecognizer`（`:98-103` + `:275-331`），并且**刻意不加** `PointerGestureRecognizer`
   （`:93-97`：它的 `PointerMoved/Released` 与 Pan 的生命周期打架、没有增益）。别家是单一机制。
   两边的守卫都要认：`IsDraggingNode` 被平移逻辑读（`WorkflowSurfaceBehavior.cs:854-865`，拖节点时平移把锚点贴到当前位置，避免节点拖拽结束后平移跳一段）。
6. **插槽锚点写 `SlotAnchorFromCanvasLocal`**（`WorkflowSlotConnectionBehavior.cs:431-433`），因为这里量到的中心**已经是画布局部坐标**：
   `TryGetCenterRelativeTo` 走父链求和，且 `GetLeftInParent/GetTopInParent` 优先取 `AbsoluteLayout.GetLayoutBounds`
   （`Translation` 不在其中，`:459-485`）。**用 `SlotAnchorFromVisualCenter` 会把 `ActualOffset` 减两次**，每条线整体偏移
   （`skills/veloxdev-create-workflow/references/gui/maui.md:39` 同结论）。
7. **Windows 上把原生 `ScrollViewer` 降级成被动容器**：`IsScrollInertiaEnabled = false` 且
   `ManipulationMode = TranslateX | TranslateY | Scale`（`WorkflowSurfaceBehavior.cs:774-781`）。理由在 `:747-766`：
   原生 ScrollViewer 是 manipulation 能力的控件，用默认 `System` 模式会抢走容器身份、带惯性自己滚，与我们程序化的
   `ChangeView` 打架，并在松手时施加它自己累积的偏移（「松手跳」的根因）。**注释明确警告不要用 `ManipulationModes.None`**
   —— 那会让平移彻底失灵（`:766` 记了这次回归）。
8. **平移用绝对锚点，不用每帧增量累积**：`SurfaceState` 里那组 `PanAccumulated*` / `PanAnchorTotal*`（`:25-37` 的注释）
   在 `Started` 记一次锚，每次 `Running` 用 `anchor − (Total − anchorTotal)` 算绝对目标（`:904-911`），
   并让**在飞的上一笔 `ScrollToAsync` 被取消**（`PanCts`，`:44-46`、`:899-902`）。注释给的教训：读每帧实际 `ScrollX` 会闪回，
   累积逐帧增量会在被夹取的滚动下漂移（卡住再跳）。

---

## 四、改这里时最容易踩的坑

1. **在平移期间抑制 `OnScrolled`** → 装饰器冻在请求值、松手跳（`WorkflowSurfaceBehavior.cs:723-732`）。这条是调查结论，不是猜测。
2. **把 §二·6 与 §二·7 的写法则互相「统一」** → 一边自锁（boundary 不再长大）、一边松手跳。两处方向相反是刻意的。
3. **在 Windows 上把重同步信号换回托管 `SizeChanged`**（或让兜底与原生信号并存）→ 逐格端点弹跳：`~10ms` 的 arrange 途中过冲
   （`WorkflowSlotLayoutBehavior.cs:108-115`、`:176-185`、`:266-271`）。
4. **删掉任何一处 NaN 守卫** → 最小后果是连线永久消失。NaN = 未测量是**跨平台统一机制**，不是本家发明的时序
   （`../extension.md` §3.9-4）：`Viewport`/`Virtualize` 那条路上 NaN 会让**整批 VisibleItems 被清空**
   （`WorkflowSurfaceBehavior.cs:1081-1100` 的注释：`NaN <= 0` 是 false，所以 Core 的守卫抓不到）；
   小地图是成组的（`:414-418` 的节点锚点、`:423-429` 的视口、`:469-475` 的 fit 缩放与原点、`:508-511` 的矩形），
   链接层则在 `TryGetEndpoints` 出口判 NaN（`WorkflowLinkOverlay.cs:765-774`）。
5. **给链接层或小地图加新的视觉 DP 却不加 `propertyChanged`** → 改了属性不重画；加了但不合并 → 一帧内多个 DP 写入各发一次
   `Invalidate`（`WorkflowLinkOverlay.cs:725-740` 的合流；小地图 `MarkDirty`/`FlushInvalidate` `WorkflowMinimapOverlay.cs:373-399`）。
   `ApplyVisibleRegion` 一帧就连写 6 个 DP（`WorkflowSurfaceBehavior.cs:1102-1115`），合并不是优化而是必需。
6. **去掉 `WorkflowLinkOverlay.InputTransparent = true`**（`:82`）→ 这层满屏盖在节点上，会吞掉全部节点交互。
   它在树里的 z 序靠 XAML 位置（装饰器内、`ScrollView` 之前，`TreeView.xaml:26-37`），挪位置等于改层序。
7. **删掉 `x:Name="Root"`** → 链接层的 `WorkflowTree`/`ScrollOffset*`/`ContentOffset*`/`RulerThickness` 全是
   `Source={x:Reference Root}` 的绑定（`TreeView.xaml:8`、`:30-36`），全部解析不到（`skills/veloxdev-create-workflow/references/gui/maui.md:57` 同结论）。
8. **把隐藏视图从 `_layout.Children` 里 `Remove`** → 本家刻意保留子元素、只 `IsVisible=false` + `ZIndex=-100`
   （`ViewManager.cs:279-284`、`ResetAllViews` `:295-317` 的注释：移除会触发昂贵的 MAUI 重排）。
   相应地，隐藏视图仍在树上 —— 遍历子元素时别假设「看不见 = 不在」。
9. **忘了取消在飞的滚动** → `PanCts` 一旦漏取消，`ScrollToAsync` 会叠起来（`:899-902`、`:367-368` 的清理）。
10. **删掉 `Refresh` 的 `IsRefreshing` 重入守卫** → 画布扩张 → `Refresh` → 再扩张的级联（`:134-141` 的注释记了
    「每次扩张级联 2-3 次 Refresh，正反馈减速螺旋」）。
11. **给链接层加「可见集之外也要补画」的兜底**（最典型的是把全量 `tree.Links` 再拉回来做差分）→ 又和其余六家分叉，
    而且不需要：新连线进可见集的时机就是 Core 的「量完才画」，与 WinUI 那条修好的行为一致。这条窗口真出问题时
    要报出来，不是在绘制侧绕过。

---

## 五、非 Trimmed demo 连线层的三件事（悬停命中 / Delete / 右键菜单）

本家与另外六家不同形：**连线不是视图**，是满屏 `GraphicsView` 一趟画完（§二·4）。所以三件事全落在
`WorkflowLinkOverlay.cs` 里，命中靠**自己算几何**，不靠平台命中测试。

| 事 | 落点 | 依据 |
|---|---|---|
| 谁来收输入 | DP `InteractionSource`（`View`）；宿主绑**页面根**，不绑这层自己 —— 这层是 `InputTransparent` 且压在 `ScrollViewer` 下，收不到指针 | `WorkflowLinkOverlay.cs:86`；`Examples/Workflow/MAUI/Demo/Controls/Workflow/WorkflowView.xaml:200` |
| 命中 | `HitTestLink`：每条链的锚点走与绘制**同一条**变换（`ToViewport`）采成折线，逐段判距，半径 6（与其余六家同值） | `:669`、`:372`、`:51` |
| 高亮 | 选中那条换 `SelectedLinkColor`（默认 `Colors.OrangeRed`）并把线宽 +1.5（管壁、彗星一起） | `:1294`、`:1325` |
| 删除 | `DeleteSelectedLink` → `DeleteCommand`；连线离开 `Links` 时把选中一并清掉（撤销/别处删也走这条） | `:753`、`:1121` |
| 右键 | 命中才 `SelectLink` + 平台 `MenuFlyout`（一项「删除连线」） | `:631`、`:885` |
| 取焦点会不会带滚画布 | **不会** —— `Focus()` 打在 `InteractionSource`（页面根）上，而它是画布 `ScrollView` 的**祖先**；WinUI 的 bring-into-view 只从**焦点元素往上冒**，画布那个 `ScrollViewer` 根本不在那条路上 | `:747`；`WorkflowView.xaml:200`（`Root` 是 ContentView 根，`PART_ScrollViewer` 在它里面） |

五条结论（都是这台机器上实测出来的，不是推导）：

1. **Windows 上不能用 `PointerGestureRecognizer` 收悬停**：挂上去之后 `PointerMoved` 一次都不来（同一次会话里改成
   `AddHandler(UIElement.PointerMovedEvent, …, handledEventsToo: true)` 挂到**同一个** `ContentPanel` 上立刻就有）。
   所以 `AttachPlatformHooks`（`:774`）走原生路由事件，`PointerGestureRecognizer` 只留在 `#if !WINDOWS`（`:646-663`）。
   方向与 `WorkflowNodeDragBehavior.cs:93-97` 的注释一致 —— 那边也是嫌它不可靠才不用。别照抄「用 PointerGestureRecognizer 做 hover」的通用建议。
2. **`SelectLink` 里必须 `Focus()`**（`:747`）：键事件从**焦点元素**冒泡，焦点不在源子树里时 `KeyDown` 不经过挂勾子的那个元素。
   实测同一段代码：加之前按 Delete 只看到 `PointerExited`、没有 `KeyDown`；加了之后立刻到。`handledEventsToo` 取 `false` 是刻意的 ——
   输入框吃掉 Delete 改自己的光标时得让它赢。
3. **菜单一开就会来一发 `PointerExited`**（飞出物把指针接管走），不认这一下就会在菜单弹出的瞬间把选中抹掉、违反「右键保持选中」。
   做法是 `_menuOpen` 标记 + `MenuFlyout.Closed` 复位（`:117`、`:622`、`:891`）。**与 WPF/Avalonia 的选择相反**（那两家在
   `MouseLeave`/`PointerExited` 里**不**跳过，理由是 popup 关掉后没有配对的 Entered/Moved、高亮会永久留下）——
   本家能跳过是因为悬停由 `PointerMoved` 驱动：指针一动就重判一次，复位走的是「下一条消息」而不是「配对的 Entered」。
4. **菜单用平台的 `MenuFlyout`，不用 MAUI 那个**（`:885`）：跨平台 `MenuFlyout` 只能整层挂成 `ContextFlyout` ——
   右键落在哪都弹、落在空白处也取消不了（`FlyoutBase.Opening` 在 MAUI 侧不暴露），而契约要求「只有点在连线上才弹」。
   代价写清楚：**非 Windows 上右键菜单是空的**（`ShowDeleteMenu` 的 `#else` 是空实现），那两个平台只剩悬停高亮与 Delete。
5. **悬停取焦点不会带滚画布（Avalonia/Jalium 那条缺陷在本家不存在，实测）**。画布滚到非零偏移
   （HUD 读作 `视口(画布) 320, 195`）后：`SelectLink` → `Focus()` 走 5 轮、外加 3 秒连打，滚动在
   **同一回合 / +250ms / +750ms** 三处都一位没动，HUD 那行逐字相同；真指针 hover（`SendInput` 走完，
   先确认应用收到了指针：`_lastPointer` 从 nil 变成那个点）同样一次没动，而选中确实生效 —— 截图里同一条线
   从静息蓝变成 `OrangeRed`。**原因是结构而非运气**：见上表最后一行，焦点元素是画布的祖先而不是后代。
   反例（说明这套检测看得出「焦点带来的滚」）：往 `PART_Canvas` 里塞一个 `Entry` 放在画布 (2400,120) 再 `Focus()`，
   画布**同一回合**就从 `320,194.667` 跳到 `1292.667,148` ⇒ 这条 `ScrollView` 的「焦点就滚」是**开着**的
   （MAUI 没碰 `BringIntoViewOnFocusChange`，MAUI 的程序集里根本没引用过这个名字）。
   **所以别为了「保险」去关掉自动滚进视口**：节点卡里的输入框仍该滚进来，本家不需要任何修补。
   同一轮也验了 Delete 没退化：按 hover 那条路选中之后真按一次 Delete，`Links` 12 → 11。

改这块时的两条禁令：**别去掉 `InputTransparent = true`**（`:82`，同 §四·6）；**别把命中半径放大成整层包围盒** ——
那会让画布空白处每一次移动都命中某条线（原文的「别把整块画布都算命中」就是这个意思）。

---

## 附：写这份档案时**没能验证 / 不确定**的

- §二·4 的「~16k 设备像素」是代码注释里的数字（`WorkflowLinkOverlay.cs:15-16`），没有在本仓库实测复现；
  它是**原生 Windows 侧**的限制，Android/iOS 上这一层的必要性未被单独验证。
- `WorkflowMinimapOverlay` 在非 Windows 平台上的拖拽路径（父级 `PointerGestureRecognizer` 越界跟踪，`:612-642`）
  只有代码依据，没有找到运行期验证记录。
- 本家没有 `WorkflowGridDecorator`（见 §一），所以「装饰器的 `RulerBand` 必须转发给虚拟化 inset」这条
  （`WorkflowSurfaceBehavior.cs:1130-1132`）在本仓库里只由 demo 的实现验证过。

### 验证这块时用到的两个事实（下次还要跑真 demo 的话）

- **合成指针输入必须走 `SendInput`**：`SetCursorPos` 能把光标挪过去，但 WinUI 不为它派发 `PointerMoved` ——
  按钮不亮、hover 不触发，看起来像功能的锅。同一段测试改用 `SendInput`（`MOUSEEVENTF_MOVE|ABSOLUTE`）后立刻正常。
- **这台机器上同时跑着别家的 demo**，压在下面的窗口收不到指针：测之前要把自己的窗口抬到最上（`SetWindowPos` 带 `HWND_TOPMOST`），
  否则点击与移动全落到别人窗口上。注意 `SetWindowPos` 的 `HWND` 形参必须按指针宽度传（ctypes 里不声明 argtypes 会把 `-1` 当 32 位传，调用静默失效）。
  另外 `WindowFromPoint` 给的是**最深的子窗口**，WinUI 会在顶层窗口下挂子 HWND —— 比「这块是不是我的」要用 `GetAncestor(hwnd, GA_ROOT)`。
- **别家的注入式测试也在驱动同一个物理指针**：光标会被抢走（写进去的位置，下次读回来已经不是它），所以「悬停选中」这类断言
  **必须先断言应用收到了指针**（读 overlay 的 `_lastPointer`），否则会把「输入没到」误判成「功能坏了」。本家这条结论
  （§五·5）是三路互证：`SelectLink` 直调 5 轮 + 3 秒连打、真指针 hover 成功那一轮、以及焦点反例 —— 不依赖任何单次 hover。
