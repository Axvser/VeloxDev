# WorkflowSystem — Jalium

> **基准：WPF。** 2026-10-05 用户定：「WPF 怎么来，Jalium 就怎么来。」
> 本文从这一条出发 —— 本家**不再**有自己的形状：类名、附着属性名、模板形态、装配方式逐项照 WPF。
> 只有**实测走不通**的地方才允许不同，且每一处都必须带着**当场量到的依据**，不许写「这家一向如此」。
> 旧的那批「Jalium 必须不一样」的结论（附加助手、代码绘制、端口几何、不测量视觉、没有画布变换通道…）
> **全部作废** —— 它们要么已经被推翻，要么还剩下的几条在 §三 里按测量重列。

> **读法**：契约与注册位置在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3，本文不重复；
> 人面向的「这家怎么用」在 `skills/veloxdev-create-workflow/references/gui/jalium.md`，本文只指路不抄。
> 代码在 `Src/Adapters/VeloxDev.Jalium/Attached/Workflow/`（**9 个文件**，与 WPF 适配器同数）与
> `Src/Templates/VeloxDev.Jalium.Templates/working/content/`（七个条目，其中四个是 `.jalxaml`）。

## 〇、`.jalxaml` 运行时能力实测（2026-10-05，探针实测后已删）

**为什么记**：`.jalxaml` 能**编译**不等于能**运行**。下面每条都是在
`Examples/Workflow/Jalium Trimmed/Demo` 里挂一个真实窗口树、`UpdateLayout()` 之后逐条断言跑出来的
（Debug 与 `PublishTrimmed` 发布版各跑一遍，结果**逐条一致**）。探针本身按惯例删了，结论留在这里；
下次要动 Jalium 的标记化，不必再摸一遍。**空着不写的地方 = 还没测过，不是「不行」。**

| 能力 | 结果 |
|---|---|
| `UserControl` 作 `.jalxaml` 根 + `x:Class` partial 配对 | ✅ |
| **`Window` 作 `.jalxaml` 根**（窗口外壳也写标记） | ✅ 2026-10-05 实测：`<Window … x:Class="Demo.MainWindow">` + 9 行 cs（只有 `InitializeComponent`）编译与运行都正常 ⇒ **宿主层不需要留在代码里** |
| `UserControl` 上的 `Background` / 子元素 `ElementName` 绑定 | ✅ 都用过（demo 的 `MainView` 与 HUD 的偏移绑定） |
| 根 name scope 里按 `x:Name` 做 `FrameworkElement.FindName` | ✅ |
| `DataTemplate` **内部**的 `x:Name` 对根 `FindName` 可见？ | ❌ 不可见（与 WPF 同）⇒ 枚举器必须走 `ItemContainerGenerator.ContainerFromIndex(i)` + 视觉树后代搜索 |
| `ItemsControl` + `ItemTemplate`(DataTemplate) + `{Binding}` 真物化容器 | ✅ `Items.Count` 正确；`ContainerFromIndex(0)` 返回 **`ContentPresenter`**；容器 `DataContext` 是条目本身 |
| 自定义 attached DP 写进标记并读回（**跨程序集**，`assembly=VeloxDev.Jalium`） | ✅ 含以 `{Binding}` 为值 |
| `Style` / `Setter` / `StaticResource` | ✅ `Setter` 真的改到了属性 |
| 控件根的 `Foreground` 向子 `TextBlock` **继承** | ❌ **不继承** —— 子 `TextBlock` 取的是主题默认（亮色 `#F5F5F7`）。WPF 的节点模板靠根 `Foreground` 一路继承下来，Jalium 必须在**每个** `TextBlock` 上显式写一次；漏了的表现是「白底白字」，但没有任何报错 |
| `ContextMenu` 从宿主继承 `DataContext` | ❌ 菜单不在视觉树里，继承不到 —— 打开前必须 `menu.DataContext = link`，否则条目的 `Command="{Binding …}"` 静默不生效（菜单照常显示、点了没反应） |
| `LayoutUpdated` 事件 | ❌ **不存在**（Jalium 没有这个事件）。槽布局的重新同步只能靠 `Loaded` / `SizeChanged` / 模型 `PropertyChanged` + `Dispatcher.BeginInvoke(DispatcherPriority.Render, …)` 排一拍 |
| 嵌套**结构体**路径绑定（`{Binding Offset.Left}` → `Canvas.Left`） | ✅ |
| `ElementName` 绑定 | ✅ |
| `RelativeSource Self` | ✅ |
| `RelativeSource AncestorType=<框架类型>`（如 `Canvas` / `UserControl`） | ✅ |
| **从 `DataTemplate` 内部**用 `AncestorType=UserControl` 够到模板根 | ✅ |
| 颜色 / `BorderThickness` / `CornerRadius` / `Path Data` 字面量 | ✅ `#DDFFFFFF` 原样、`1,1,1,1`、`6,6,6,6`、`PathGeometry` |
| `PublishTrimmed` 发布版 | ✅ 与 Debug 逐条一致，未裁剪掉标记资源 |
| **`RelativeSource AncestorType` = 自定义 `clr-namespace` 类型** | ❌ 不解析（带不带 `{x:Type}` 都不行）⇒ 用**框架基类**（`UserControl`）替代 |
| **`(附加属性)` 括号路径**（`(behaviors:X.Y)`）出现在绑定路径里 | ❌ 根本不解析。2026-10-05 重测：**`(Canvas.Left)` 这条框架类型自己的也不行** ⇒ 卡的是**括号语法本身**，与前缀无关 |

**两条失败合起来毙掉的一句**：WPF 的 tree 模板靠
`RenderTransform="{Binding RelativeSource={RelativeSource AncestorType=local:模板类}, Path=(behaviors:WorkflowCanvasTransformBehavior.Transform)}"`
把画布变换镜像给池化视图 —— 这**两半在 Jalium 都用不了**。Jalium 若要走标记，画布变换必须改由
**表面在代码里推给池化视图**（表面本来就持有池与变换值）。

**还有一个坐标事实**：`Visual.TransformToAncestor(ancestor)` 返回的是 **`Jalium.UI.Point`**
（元素在祖先坐标系里的**原点**），**不是** WPF 那个 `GeneralTransform`。⇒ 要拿「控件中心在画布上的位置」
得自己加半个 `ActualWidth/ActualHeight`，没有 `TranslatePoint(p, ancestor)` 这种一步到位的写法。

---

## 一、这家必须实现什么，为什么是这些

**七个视图角色仍由适配器提供，形态换成了「附着行为 + 可选基类」。**

| 角色 | 落点 | 形态 |
|---|---|---|
| 表面 | `WorkflowSurfaceBehavior` | `static` + 8 个附着属性，按 `FindName` 解析模板声明的部件 |
| 插槽 | `WorkflowSlotLayoutBehavior` | `static` + 5 个附着属性；**`slot.Anchor` 的唯一写回点** |
| 节点拖拽 | `WorkflowNodeDragBehavior` | `static` + `IsEnabled` / `CoordinateHostName` / `CoordinateHostType` |
| 连线手势 | `WorkflowSlotConnectionBehavior` | `static` + `IsEnabled` |
| 模型事件 | `WorkflowEvents` | `static` + `Node` / `Slot` / `Tree` 三个 sink 附着属性 |
| 连线自盒化 | `WorkflowLinkBounds` | `static` 助手（**唯一刻意背离**，见 §2.1） |
| 小地图 | `WorkflowMinimapOverlay` | `FrameworkElement`，14 个 DP；`ScrollViewerName` 由模板给 |
| 视图池 | `ViewPool` / `ViewManager` | Jalium 自己的 `DataTemplateSelector` + `DataTemplate.LoadContent()` + 三级回退 |

**网格装饰器不在这里**：与 WPF / Avalonia / WinUI / MAUI / Razor 五家一致，它归**模板**
（`workflow-grid-decorator`，`sealed class : Grid, IWorkflowGridDecorator`），适配器只留接口。

**删掉的一整套**（想让它们回来之前先读 §〇 与 `adapter-base-class-specifications.md` §一）：
`WorkflowTreeView`、`WorkflowNodeAttachment`、`WorkflowSlotAttachment`、`WorkflowLinkAttachment`、
`WorkflowPortGeometry`、`WorkflowPortLayout`、`WorkflowGridDecorator`、`IWorkflowTemplateSelector`、
`WorkflowTemplateSelector`。

---

## 二、平台硬限制，与由此产生的做法

> 这一节是全文最有价值的部分：它决定别家能照抄什么、不能照抄什么。落点现在是适配器基类。

### 2.1 渲染器按 `RenderSize` 盒裁剪子元素，不按内容判交 —— 连线视图必须"自盒化"

`Jalium.UI.Media.Visual.ShouldRenderChild(DrawingContext, UIElement, Point)` 是 `Private, Static, HideBySig`（反射可证）。它解析到的判定链是：拿子元素的 `RenderSize` 做盒（**只有 `Effect` 的 `EffectPadding` 会扩大它**，`IClipBoundsDrawingContext.CurrentClipBounds` 那条只在有裁剪上下文时生效），再算 `MapChildBoundsToCurrentDrawingSpace` → `Rect.IntersectsWith`。⇒ **内容画到自己盒子外面的部分会被整块丢掉，且不报错。**

**由此产生的唯一正确做法：一个自绘元素想画到哪里，就必须先把自己的盒挪/撑到那里。** 这条在两个角色上各体现一次：

- **连线视图**：适配器基类 `WorkflowLinkView.cs:327-343` 的 `UpdateBounds()` 在每次端点折叠/移动后，把元素自身 `Canvas.SetLeft/Top` 与 `Width/Height` 设成**这条线自己的 canvas-local 包围盒**（`:332-335` 的 `BoxPad = 6` 外扩）；`:310-323` 的 `EndpointsCanvasLocal()` 算端点，`:140-186` 的 `OnRender` 再用 `local = canvas − (_viewX,_viewY)`（`:167-168`）把几何烘回元素局部坐标 —— **定位与烘焙相消**，视觉输出与画在 (0,0) 等价。`:14-22` 的注释把根因写死了：*"the renderer culls a child entirely when its layout box misses the viewport clip and never looks at the drawn content, so a full-canvas box that goes stale during a zoom burst takes the whole link layer with it"*。
  ⇒ **这是本仓库"深缩放连线消失"谱系在 Jalium 的最后一环**，与共享 Core 的负侧 cover 无关，别去 Core 里找。改连线视图时若把 `UpdateBounds` 删掉或让它滞后一帧，盒子就是陈旧的，**线会在深缩放下静默消失**。
- **节点视图**：适配器基类 `WorkflowNodeView.cs:272-282` 的 `ApplyPosition()` 同步设 `Canvas.SetLeft/Top` 与 `Width/Height`，同一份理由。

**核对历史记录**：「自绘但宿主 box-cull」—— **仍成立**，且现在是 IL 级证据（本节）。「梯度交接测量」—— 指的不是这里，是 TransitionSystem 的 `Samplers/BrushSampler.cs:79-94`，见 `memory/modules/TransitionSystem/adapters/jalium.md` §2.3。「连线视图自盒化（盒 == 每线 bbox，定位+烘焙相消）」—— **仍成立**，`WorkflowLinkView.cs:327-343` + `:140-186`。「`IsVirtual` 跳过 + `PortCenter` 反查」—— **前半条已作废**：`IsVirtual` 在 Jalium 适配器与模板里**一处都没有**，它被一个精确得多的判定取代：`IsDragPreview(link) => link.Sender is SlotDefaultViewModel && link.Receiver is SlotDefaultViewModel`（`WorkflowLinkView.cs:191-192`），只跳过树的拖拽预览，脱了插槽的真实连线照样渲染。**后半条仍成立**：`PortCenter` 仍是反查（`:283-306`，常态从 `slot.Parent` 的节点几何算端口中心而非读 `slot.Anchor`；仅当端点已脱离节点、`slot.Parent` 为 `null` 时才退回读一次 `slot.Anchor`），返回 `Point?`，端点为 `null` 时跳过整条线而不是从原点画一条退化线。

### 2.2 有标记，也有名字作用域（2026-10-05 反转）

原来这节写「Jalium 没有标记语言，也无需名字作用域」。**两条都不对了**：`.jalxaml` 工具链完整
（`Jalium.UI.Build` 的 `EnableDefaultJalxamlItems` + `**\*.jalxaml` + `JalxamlCodeBehindTask`），
`FrameworkElement.FindName` 在模板的 name scope 里按 `x:Name` 正常工作（实测）。表面与节点视图因此
与 WPF 同形地按名字取部件。

⚠ **两条仍要记住的边界**（实测，见 §〇）：
- `DataTemplate` **内部**的 `x:Name` 对根 `FindName` **不可见**（与 WPF 同）。枚举器里的槽只能先拿
  `ItemContainerGenerator.ContainerFromIndex(i)`，再沿视觉树往下找带槽 DataContext 的那个元素。
- `RelativeSource AncestorType` 只认**框架类型**（`UserControl` / `Canvas` 可用），自定义
  `clr-namespace` 类型不解析；**带前缀的附加属性路径**（`(behaviors:X.Y)`）在绑定路径里**根本不解析**。
  这两条合起来毙掉了 WPF 那句「节点模板隔着 DataTemplate 读表面变换」，见 §2.4。

### 2.3 这家现在**测量**视觉：端口位置由槽控件实测写回（2026-10-05 反转）

原来这节写「不测量视觉：端口中心由 `WorkflowPortGeometry` 从模型几何算」。**现在反过来**：
`WorkflowSlotLayoutBehavior` 按 `SlotNames` / `SlotEnumeratorNames` 找到模板里那些槽控件，从控件的
`DataContext` 取槽视图模型，量出**中心**，写回 `slot.Anchor`。⇒ `slot.Anchor` 从此**是**测量结果，
它成了端口命中与连线端点的唯一来源；丢掉的是一条「端口位置由模型几何算出」的不变式。

⚠ **坐标系是这一步唯一的坑**：本家把世界位移做成**画布自己的 `RenderTransform`**（`ApplyLayout`），
坐标宿主就是那块画布 —— 变换在宿主**之上**，所以 `TransformToAncestor(canvas)` 交回的已经是不含
`ActualOffset` 的世界坐标，必须用 **`SlotAnchorFromCanvasLocal`**，不是其余六家那个
`SlotAnchorFromVisualCenter`（再减一次 `ActualOffset` 就是那个静默的系统性偏移）。
另：`Visual.TransformToAncestor(ancestor)` 在这家返回 **`Jalium.UI.Point`**（元素在祖先系里的原点），
不是 WPF 的 `GeneralTransform`，取中心要自己加半个尺寸。

### 2.4 画布变换：没有附着属性通道，写在画布上（2026-10-05 改写）

别家让节点与连线模板用
`RenderTransform="{Binding RelativeSource={RelativeSource AncestorType=…}, Path=(behaviors:WorkflowCanvasTransformBehavior.Transform)}"`
把画布变换镜像下来。**这半条路在 Jalium 走不通**（§2.2 的两条实测限制）。

⇒ 本家**不设** `WorkflowCanvasTransformBehavior`：`WorkflowSurfaceBehavior.ApplyLayout` 直接把
`TranslateTransform(ActualOffset)` 写在 **`PART_Canvas` 自己**身上。池化出来的节点与连线全在画布之下，
一起跟着走 —— 等价，且没有「视图后到、变换晚一帧」的同步问题。代价是**画布系坐标就是模型系**，
凡是要比模型的地方都不许再减 `ActualOffset`（见 §2.3）。

### 2.4.2 连线视图照 WPF 用绑定 —— 但要多一道 NaN 守卫（2026-10-05 更正）

**原先这里写的是「几何别走绑定，要用模型读」，依据是「绑定晚一拍」。那条归因不成立** ——
当时那批症状（连线冻住、从原点画一条）的主导原因是另一个 bug（槽锚点没重测，见 §四-0），
我没把它隔离出来就下了结论。**重新测过：WPF 的写法（四个 `StartLeft/…` 绑定）在本家能用。**

⚠ 但有一处 WPF 不需要、本家**必须**加的东西：

> **绑定送来的是 `NaN`。** 占位槽（拖拽预览的两端）的默认锚点就是 `new(NaN, NaN, 0)`，NaN 进盒子后
> 渲染器会**抛** `ArgumentException: Width and Height cannot be negative`（实测：不加守卫时
> **应用直接退出**）。所以 `Refresh` 在算盒子之前要 `double.IsNaN(...)` 挡一道。
>
> 而且 DP 的默认值是 **0**，与「绑定还没送到」无法区分 —— 所以「不画」要靠模型那条
> `IsVisible && IsRenderReady()` 守卫来定，几何则用 DP。两道守卫要一起在。

**盒子仍然在属性变更期摆，不在 `OnRender` 里摆**（渲染器按盒裁剪，见 §2.1）：DP 一变就重算几何并挪盒子，
`OnRender` 只负责画。盒子要盖住**四个控制点**（曲线凸包），只盖两端点会把鼓出去的那段静默切掉。

### 2.4.3 两个相位的坑：按下要挂预览、收尾要排在别的处理器之后（2026-10-05 实测）

**① 「起平移」必须挂在具名按下源的预览相上**（`PointerPressSource.PreviewMouseDown`），与 WPF 同形。
挂在宿主的**冒泡**相上收不到：滚动视口那一层会把 `MouseDown` 标成已处理，宿主的处理器整场不触发。
宿主的转发（`RoutePointer`）也因此一并挪到预览相。

**② 「兜底收虚拟连线」不能无条件排在预览相。** 宿主的预览处理器**早于**槽自己的预览处理器，所以一句
无条件的 `ResetVirtualLinkCommand` 会在槽有机会完成连接**之前**就把橡皮筋收掉 —— 症状是**所有连线都连不成**，
而表面上一切正常（菜单能弹、端口能按）。⇒ 只在**松手没落到端口上**时才兜底；落到端口上的三种出口
（连成 / 松回自己 / 落在接不了的口）由槽自己收尾。

⚠ 这两条都**静默**：没有异常、没有日志，只有把界面真的点一遍才看得见。

### 2.5 缩放/滚动的时序：Jalium 的 `ScrollTo` 不保证同步落地，`ScrollChanged` 不可靠

**这套守卫现在在适配器的表面基类里**（`WorkflowTreeView.cs`），不再是宿主自写：

- `_zoomPin`（`:61`）+ `ZoomPinLifetimeMs = 250`（`:63`）：提交缩放目标后把视口钉在该目标上，直到 viewer 报告落地（±0.5）或超过 250ms（`UpdateViewport`，`:729-751`）。
- 理由在 `:50-60` 与 `:218-226` 的注释里：Jalium 的 `ScrollTo` 之后立刻读 offset 可能读到**尚未生效的缩放前值**，而 `ScrollChanged` 会在落地前先发一次；若照读就会用陈旧窗口覆写 `Helper.Viewport`，下一次 `Virtualize` 把刚物化的连线裁掉（节点因为按自己的矩形进池而留下）→ **深缩放链接消失 ~100ms**。
- 兜底：`:761-770` 视口未测量时（`vw<=0`）退回整块画布，否则首次 `Virtualize` 会在 0 尺寸视口上空转，初始节点/连线全不出现。
- 宿主侧只需在提交缩放后调 `surface.NotifyZoomCommitted(committedX, committedY)`（demo `MainWindow.cs:216,227`）。

⇒ **这家的"链接在深缩放下闪没"有两个独立成因**：宿主侧的视口竞态（本节，靠 `_zoomPin` 解）与渲染侧的盒裁剪（§2.1，靠自盒化解）。**修一个不会修好另一个**，别把两者的现象混着查。

### 2.6 其余平台面的事实

- `WorkflowMinimapOverlay : FrameworkElement, IWorkflowMinimapOverlay`（`WorkflowMinimapOverlay.cs:15`），`RulerBand => 0`（`:48`）—— 它不是标尺，所以虚拟化 inset 为 0。
- `WorkflowMinimapOverlay.cs:250-259` 的 `OnMiniMouseDown` 先置 `_dragging` 再调 `PanToMini(...)` ⇒ **单击即居中**（不是"拖才动"）。这是小地图的既定语义，与 WPF 那份同款，**不是背离**。
- `WorkflowMinimapOverlay.ScrollViewer` 是普通属性（`:61`），**适配器里没有赋值者** —— 宿主必须自己赋（demo `MainWindow.cs:64`）；`WorkflowTreeView.AttachScrollViewer` 只管表面自己的 viewer，不转给小地图。
- **小地图的内容（节点缩略框 + 包围盒）是带脏标记的缓存**（`WorkflowMinimapOverlay.cs` 的 `_nodeRects` / `_contentBounds` / `_pendingRefresh`，入口 `EnsureContent` / `MarkContentDirty`）：只有节点动过才重算（树的集合变化、节点 `Anchor`/`Size` 变更、换树）；视口、配色、尺寸的变化只 `InvalidateVisual`，不置脏 —— 它们只改那一趟 O(1) 的变换。这家的小地图在平移/缩放里每帧都被推着重画（宿主的 DP 变更 → `OnVisualChanged`），缓存因此是必需的：旧写法每帧要把节点表走两遍（包围盒一趟、绘制一趟）。与 WPF / Avalonia / WinUI / MAUI 的同名 `_pendingRefresh` 是同一条规矩。
- ⚠ **但只有脏标记会漏掉缩放**：节点的 `Anchor`/`Size` 是「原值 ÷ `Layout.Scale`」的折叠值（`NodeDefaultViewModel.cs:46,64`），**缩放变化不让任何节点发 `PropertyChanged`**。所以 `EnsureContent` 除了脏标记还比一次 `Layout.Scale` 的两个分量。四家同名实现是靠「任何 DP 变化都置脏」把这件事顺带盖住的，这家把视口变化从脏标记里摘出去了（平移因此不必重算），就得自己补这一句 —— 别把它当冗余删掉。

---

## 三、与 WPF 的差异 —— 只有实测挡住的才留（2026-10-05 重测）

**基准是 WPF。** 下面每一条都**当场量过**才留；量不过就照 WPF 改。旧的那批「这家特殊」的结论一律作废。

| 差异 | 现在的依据（当场量的） |
|---|---|
| **没有 `WorkflowCanvasTransformBehavior`** | Jalium 的绑定**不支持 `(附加属性)` 这条括号路径语法** —— 2026-10-05 重测：`(behaviors:WorkflowSurfaceBehavior.LinkMenuKey)` 与 **`(Canvas.Left)`（框架类型自己的）** 全都解析不出来，与前缀无关。WPF 的节点/连线模板正是靠 `Path=(behaviors:…Transform)` 读它的，这条通道在本家**建不起来**；补上那个类就是**死 API**（照着模板写会以为生效）。画布变换由表面写在画布自己的 `RenderTransform` 上，观感与 WPF 等价 |
| **有 `WorkflowLinkBounds`** | 渲染器按 `RenderSize` 盒裁剪子元素、内容画到盒外**静默丢弃**（§2.1 的 IL 级依据）。WPF 让连线视图铺满整块画布即可，本家那样做会在缩放里陈盒掉整层线 |
| ~~`LinkView` 从模型读几何~~ | **已消除**（2026-10-05 重测）：照 WPF 用四个 DP 绑定可以跑，只需多一道 `IsNaN` 守卫（不加会**抛异常退出**）。原先那条「绑定晚一拍」的归因不成立 |
| **槽锚点用 `SlotAnchorFromCanvasLocal`** | 画布变换在本家写在画布自己身上（见第一条），坐标宿主之上的变换不进 `TransformToAncestor`，用别家那个 `SlotAnchorFromVisualCenter` 会再减一次 `ActualOffset` |
| **槽再同步靠 `Loaded`/`SizeChanged`/模型变更 + `Dispatcher.Render` 排一拍** | 本家**没有 `LayoutUpdated` 事件**（26.10.9 反射清点，一个都没有） |

⇒ **这五条都是「WPF 那么做在本家跑不起来」，不是「本家想不一样」。** 谁要是能证明其中一条现在能跑了，
就照 WPF 改 —— 记忆不是理由，测量才是。

## 四、坑（带依据）

0. **`PropertyChanged` 的 `sender` 是模型，不是宿主控件。** 2026-10-05 实测踩到：`WorkflowSlotLayoutBehavior`
   的模型变更回调第一行写成 `sender is not FrameworkElement control` —— 而 sender 是那个**模型**，
   于是**每次都在第一行返回**。后果是节点移动后槽锚点从不重测，**连线整条冻在拖动前的几何上**，
   而日志里一个错都没有（`AttachCount` / `SyncCount` 都正常，因为那条路只跑了一次）。
   ⇒ 模型 → 宿主的回指要靠自己存（这里用 `ConditionalWeakTable<INotifyPropertyChanged, FrameworkElement>`），
   别指望 sender。



1. **重做连线视图时最容易漏掉"自盒化"。** 盒子必须**每次端点折叠/移动都同步** `Canvas.SetLeft/Top` + `Width/Height`（`WorkflowLinkView.cs:327-343`），且 `OnRender` 必须把几何烘回局部（`:167-168`）。漏任一半 = 深缩放静默消失或整条线画歪。依据：`:14-22` 的注释与 §2.1 的 IL 判定链。
2. **拖拽预览的跳过条件要精确，不要回到 `IsVirtual`。** 用 `IsDragPreview`（`WorkflowLinkView.cs:191-192`），并把 `IsVisible` 与两个 `null` 检查都留下（`:144`、`:329`）。依据：`:188-190` 注释明写"脱了插槽的真实连线不能消失"。
3. **`_selector is null` 时 `AddItem` 静默返回。** `ViewManager.cs:122` —— 集合先到、选择器后到不会报错也不会补，只会**什么都不显示**。诊断顺序：先看 `WorkflowTreeView.TemplateSelector` 是不是设了（`SetTree` 会把它转给 `ViewPool`，`WorkflowTreeView.cs:205`）。
4. **视图池按具体类型分桶、且是即时创建。** `ViewManager.cs:127` 用 `item.GetType()` 作桶键 ⇒ 两种 ViewModel 类型即使视图相同也各建一份；`:33-49` 的 `Attach` 逐个建视图，**没有别家那种分批**（WPF 按 `DispatcherPriority.Background` 三个一批），大集合首帧会卡。依据：`:15`、`:33-49`、`:127`。
5. **删视图时同时置 `Collapsed` 与 `DataContext = null`**（`ViewManager.cs:163-164`）。=> 视图里读 `DataContext` 的代码（含 `WorkflowLinkView` 的守卫）在池化回收后会看到 `null`，别把 `DataContext is not null` 当成"已初始化"。三个基类都靠 `DataContextChanged` 取模型（`WorkflowNodeView.cs:49`、`WorkflowLinkView.cs:61`、`WorkflowSlotView.cs:40`）。
6. **端口是反射读的，不是接口成员。** `WorkflowPortGeometry` 按属性名取 `InputSlot` / `OutputSlot` / `OutputSlots` + `Title`/`Name`（`WorkflowPortGeometry.cs:37-70,123-124`）；改节点 view-model 的属性名会静默错位。卡片刻画（`DrawCard`）与命中读的是**两套**：画由派生类决定，命中读 `PortLayout`/几何。
7. **csproj 的独有设定会咬人**（详见 `memory/modules/TransitionSystem/adapters/jalium.md` §2.5）：单目标 `net10.0` 无平台后缀（`Src/Adapters/VeloxDev.Jalium/VeloxDev.Jalium.csproj:7`）⇒ 只能引用 `Jalium.UI.Controls` 这个平台中性包；`NoWarn` 现在是 `1573;1591`（`:12`）—— 1591 关「公开成员缺 XML 注释」、1573 关「参数缺 `<param>` 标签」，与平台包或 DP 拆箱无关（`WorkflowMinimapOverlay` 的 DP 包装走泛型 `Read<T>`，不产生值类型拆箱告警）。改这条 NoWarn 前先确认没别的地方会触发。

---

### 4.x 连线右键菜单：`ContextMenu.Open(Point)` 吃的是**根视觉坐标**，不是屏幕像素（2026-10-03 实测）

基类 `WorkflowTreeView` 新增 `OnBuildLinkMenu(menu, link)`（`WorkflowTreeView.cs:285`，模板派生后增删条目）
与 `OnConnecting`/`OnConnected`（`:268`/`:272`）；菜单的订阅、定位、开合上报都在基类（`:425-488`）。定位那条链值得记：

`e.Position`（表面局部）→ `PointToScreen`（物理像素）→ **`root.PointFromScreen(...)`（根视觉局部）** → `menu.Open(...)`。
最后那一步不能省：反编译 `Jalium.UI.Controls` 26.10.8 可见 `ContextMenu.Open` 把点**直接写进**
`Popup.HorizontalOffset/VerticalOffset`，而 `Popup` 按**根视觉/窗口客户区**解释它们、自己再转屏幕；
框架内部右键路径传的也是 `e.GetPosition(null)`。直接把 `PointToScreen` 的结果喂进去，菜单会整体偏移一个窗口原点。

另外两条。其一：Jalium 的 `MenuItem` **不会自己关菜单** —— 点完要显式 `menu.Close()` 再执行删除
（基类 `WorkflowTreeView.cs:291-292` 的 `menu.Close(); link.DeleteCommand.Execute(null);`；完整 demo 同款，
`NodeEditorSurface.cs:296-297` 的 `menu.Close(); DeleteLink(link);`）。其二：**菜单开着时不再需要平台侧提前返回，
也不用平台自己记「菜单指着的那条线没了」**。`WorkflowTreeView.OnMouseLeave` 现在只管原样转发 `Exited`
（`WorkflowTreeView.cs:490-497`），同样地完整 demo 的 `MouseLeave` 也只清端口悬停 + 转发
（`NodeEditorSurface.cs:130-135`）；挂起由 Core 承担 —— `LinkInteraction.Publish(PointerEvent)` 在 `IsSuspended`
时同时忽略 `Exited` 与 `Moved`（`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Events/LinkInteraction.cs:187-196`）。
`OnMouseLeave` 里旧那层 `if (_linkMenu?.IsOpen == true) return;` 已删除，别再加回来（`OnContextMenuRequested`
里那句同形的 `if (_linkMenu?.IsOpen == true) return;` 是另一回事：它挡的是同一时刻开第二个菜单，
`WorkflowTreeView.cs:454`）。线被别处删掉（Agent / Undo / …）时收菜单这一件同样归 Core —— hub 在开着的菜单
指着的那条线离开 `tree.Links` 时发 `ContextMenuDismissRequested`，宿主只收自己这份弹窗（先与 `_menuLink` 比对，
再 `_linkMenu?.Close()`：基类 `WorkflowTreeView.cs:476-480`、demo `NodeEditorSurface.cs:282-286`），收起后照常报
`Closed`、挂起随之放开；**平台仍然不记任何账**。

## 五、非 Trimmed demo 的落点（2026-10-05 更新）

**`Examples/Workflow/Jalium/`（4556 行）在 2026-10-05 那次改造里零改动。** 它只派生适配器的一个类型
（`Views/Workflow/Minimap.cs:8` 的 `Minimap : WorkflowMinimapOverlay`），其余 `NodeEditorSurface`(1879) /
`NodeViewBase` / `NodePorts` / `PortGlyph` / `NodeChrome` 全是它**自己的自绘代码**，不引被删的那一套。
⇒ 下次再动 Jalium 适配器时，**先按这一条估爆炸半径**：全 demo 与适配器的耦合面只有小地图一个类。

## 六、这份文件没写的东西

- 适配器 12 个文件的成员列表、继承树、文件清单 —— IDE 里一按就有。
- 契约本身（七角色职责、绑定挂在哪、`Viewport` 谁写、渲染就绪门、滚轮方向、缩放提交顺序、注册位置表、`dotnet new` 模板约定）—— 在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3 与 `skills/veloxdev-create-workflow/references/new-adapter.md`。
- 这家怎么用（模板包怎么生成、demo 怎么跑、七个 GUI 页面的对照）—— `skills/veloxdev-create-workflow/references/gui/jalium.md` 与 `references/view-layer.md`。
- 其余六家的差异 —— 同目录另外六份。
