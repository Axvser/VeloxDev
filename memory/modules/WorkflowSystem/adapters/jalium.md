# WorkflowSystem — Jalium

> **基准：WPF**：类名、附着属性名、模板形态、装配方式逐项照 WPF；只有**实测走不通**的地方才不同，且每处带**当场量到的依据**。

> **读法**：契约与注册位置在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3，本文不重复；
> 人面向的「这家怎么用」在 `skills/veloxdev-create-workflow/references/gui/jalium.md`。
> 代码在 `Src/Adapters/VeloxDev.Jalium/WorkflowSystem/AttachedBehaviors/`（视图池的各层在 `ViewHosting/`）与
> `Src/Templates/VeloxDev.Jalium.Templates/working/content/`（其中一部分条目是 `.jalxaml`）。

## 〇、`.jalxaml` 运行时能力实测（2026-10-05）

下面每条都是在
`Examples/Workflow/Jalium Trimmed/Demo` 里挂一个真实窗口树、`UpdateLayout()` 之后逐条断言跑出来的
（Debug 与 `PublishTrimmed` 发布版各跑一遍，结果**逐条一致**）。**空着没写 = 还没测过，不是「不行」。**

| 能力 | 结果 |
|---|---|
| `UserControl` 作 `.jalxaml` 根 + `x:Class` partial 配对 | ✅ |
| **`Window` 作 `.jalxaml` 根**（窗口外壳也写标记） | ✅ 2026-10-05 实测：`<Window … x:Class="Demo.MainWindow">` + 几行 cs（只有 `InitializeComponent`）编译与运行都正常 ⇒ **宿主层不需要留在代码里** |
| `RelativeSource AncestorLevel` | ✅ 标记里可用。实测：`AncestorType=UserControl` 落在**最近**的 UserControl（节点卡自己），加 `AncestorLevel=2` 就够到树根 —— 这是「不认自定义 `AncestorType`」的替代路径 |
| **把一个附着属性按 CLR 属性名暴露给绑定** | ✅ 可行：类里写 `public static readonly DependencyProperty XProperty = 那个附着属性;` + `public T? X => GetValue(XProperty) as T;`，模板就能 `{Binding X, RelativeSource=…}`。**这是本家读附着属性值的正解** |
| `UserControl` 上的 `Background` / 子元素 `ElementName` 绑定 | ✅ 都用过（demo 的 `MainView` 与 HUD 的偏移绑定） |
| 根 name scope 里按 `x:Name` 做 `FrameworkElement.FindName` | ✅ |
| `DataTemplate` **内部**的 `x:Name` 对根 `FindName` 可见？ | ❌ 不可见（与 WPF 同）⇒ 枚举器必须走 `ItemContainerGenerator.ContainerFromIndex(i)` + 视觉树后代搜索 |
| `ItemsControl` + `ItemTemplate`(DataTemplate) + `{Binding}` 真物化容器 | ✅ `Items.Count` 正确；`ContainerFromIndex(0)` 返回 **`ContentPresenter`**；容器 `DataContext` 是条目本身 |
| 自定义 attached DP 写进标记并读回（**跨程序集**，`assembly=VeloxDev.Jalium`） | ✅ 含以 `{Binding}` 为值 |
| `Style` / `Setter` / `StaticResource` | ✅ `Setter` 真的改到了属性 |
| 控件根的 `Foreground` 向子 `TextBlock` **继承** | ❌ **不继承** —— 子 `TextBlock` 取的是主题默认（亮色 `#F5F5F7`）。WPF 的节点模板靠根 `Foreground` 一路继承下来，Jalium 必须在**每个** `TextBlock` 上显式写一次；漏了的表现是「白底白字」，但没有任何报错 |
| `ContextMenu` 从宿主继承 `DataContext` | ❌ 菜单不在视觉树里，继承不到 —— 打开前必须 `menu.DataContext = link`，否则条目的 `Command="{Binding …}"` 静默不生效（菜单照常显示、点了没反应） |
| `LayoutUpdated` 事件 | ❌ **不存在**（Jalium 没有这个事件）。槽布局的重新同步只能靠 `Loaded` / `SizeChanged` / 模型 `PropertyChanged` + 共享批次 `WorkflowLayoutBatch.Schedule`（post 是 `Dispatcher.BeginInvoke(DispatcherPriority.Render, …)`）排一拍 |
| 嵌套**结构体**路径绑定（`{Binding Offset.Left}` → `Canvas.Left`） | ✅ |
| `ElementName` 绑定 | ✅ |
| `RelativeSource Self` | ✅ |
| `RelativeSource AncestorType=<框架类型>`（如 `Canvas` / `UserControl`） | ✅ |
| **从 `DataTemplate` 内部**用 `AncestorType=UserControl` 够到模板根 | ✅ |
| 颜色 / `BorderThickness` / `CornerRadius` / `Path Data` 字面量 | ✅ `#DDFFFFFF` 原样、`1,1,1,1`、`6,6,6,6`、`PathGeometry` |
| `PublishTrimmed` 发布版 | ✅ 与 Debug 逐条一致，未裁剪掉标记资源 |
| **`RelativeSource AncestorType` = 自定义 `clr-namespace` 类型** | ❌ 不解析（带不带 `{x:Type}` 都不行）⇒ 用**框架基类**（`UserControl`）替代 |
| **`(附加属性)` 括号路径**（`(behaviors:X.Y)`）出现在绑定路径里 | ❌ 根本不解析。2026-10-05 重测：**`(Canvas.Left)` 这条框架类型自己的也不行** ⇒ 卡的是**括号语法本身**，与前缀无关 |

**这两半在 Jalium 都用不了**：WPF 的 tree 模板靠
`RenderTransform="{Binding RelativeSource={RelativeSource AncestorType=local:模板类}, Path=(behaviors:WorkflowCanvasTransformBehavior.Transform)}"`
把画布变换镜像给池化视图 —— 画布变换必须改由**表面在代码里推给池化视图**（表面本来就持有池与变换值）。

**还有一个坐标事实**：`Visual.TransformToAncestor(ancestor)` 返回的是 **`Jalium.UI.Point`**
（元素在祖先坐标系里的**原点**），**不是** WPF 那个 `GeneralTransform`。⇒ 要拿「控件中心在画布上的位置」
用 `TransformToVisual(ancestor).Transform(center)`；它组合整条链（含 Viewbox 的缩放与节点的
`CanvasTransform`），而 `TransformToAncestor` 只给原点、半个尺寸不会跟着 Viewbox 缩放。

---

## 一、这家必须实现什么，为什么是这些

**七个视图角色仍由适配器提供，形态换成了「附着行为 + 静态助手」（网格装饰器除外，它归模板）。**

| 角色 | 落点 | 形态 |
|---|---|---|
| 表面 | `WorkflowSurfaceBehavior` | `static` + 一组附着属性，按 `FindName` 解析模板声明的部件 |
| 插槽 | `WorkflowSlotLayoutBehavior` | `static` + 一组附着属性；**`slot.Anchor` 的唯一写回点** |
| 节点拖拽 | `WorkflowNodeDragBehavior` | `static` + `IsEnabled` / `CoordinateHostName` / `CoordinateHostType` |
| 连线手势 | `WorkflowSlotConnectionBehavior` | `static` + `IsEnabled` |
| 模型事件 | `WorkflowEvents` | `static` + `Node` / `Slot` / `Tree` 三个 sink 附着属性 |
| 连线自盒化 | `WorkflowLinkBounds` | `static` 助手（**唯一刻意背离**，见 §2.1） |
| 小地图 | `WorkflowMinimapOverlay` | `FrameworkElement`，一组 DP；`ScrollViewerName` 由模板给 |
| 视图池 | `ViewPool`（挂点）+ `ViewHosting/` 九个层 | 挂点建 `ViewSourcePipeline`（Core 泛型组合根）；`ViewFactory` 走选择器 → 资源回退 + `DataTemplate.LoadContent()`，其余层见 §四 |

**网格装饰器不在这里**：与 WPF / Avalonia / WinUI / MAUI / Razor 五家一致，它归**模板**
（`workflow-grid-decorator`，`sealed class : Grid, IWorkflowGridDecorator`），适配器只留接口。

---

## 二、平台硬限制，与由此产生的做法

### 2.1 渲染器按 `RenderSize` 盒裁剪子元素，不按内容判交 —— 连线视图必须"自盒化"

`Jalium.UI.Media.Visual.ShouldRenderChild(DrawingContext, UIElement, Point)` 是 `Private, Static, HideBySig`（反射可证）。它解析到的判定链是：拿子元素的 `RenderSize` 做盒（**只有 `Effect` 的 `EffectPadding` 会扩大它**，`IClipBoundsDrawingContext.CurrentClipBounds` 那条只在有裁剪上下文时生效），再算 `MapChildBoundsToCurrentDrawingSpace` → `Rect.IntersectsWith`。⇒ **内容画到自己盒子外面的部分会被整块丢掉，且不报错。**

**由此产生的做法：一个自绘元素想画到哪里，就必须先把自己的盒挪/撑到那里。** 这条逻辑归两个落点，都在**标记侧**调用：

- **连线视图**：适配器的静态助手 `WorkflowLinkBounds.Apply(view, points, out originX, out originY)`（`WorkflowLinkBounds.cs` 的 `Apply`）在每次端点折叠/移动后，把元素自身 `Canvas.SetLeft/Top` 与 `Width/Height` 设成**这条线自己的 canvas-local 包围盒**（`BoxPad = 6` 外扩）；调用方是**模板产物** `workflow-link-view/TemplateClass.jalxaml.cs` 的 `Refresh()`（四个控制点一起进盒），随后 `BuildCurve()` 把每个点减去助手交回的原点 —— **定位与烘焙相消**，视觉输出与画在 (0,0) 等价。助手的 `<remarks>` 把根因写死了：*"the renderer culls a child entirely when its layout box misses the viewport clip and never looks at the drawn content…"*。
  ⇒ **这是「深缩放连线消失」谱系在 Jalium 的一环**，与共享 Core 的负侧 cover 无关。改连线视图时若把 `WorkflowLinkBounds.Apply` 这一步删掉或让它滞后一帧，盒子就是陈旧的，**线会在深缩放下静默消失**。
- **节点视图**：节点定位全在 item template 标记里 —— `workflow-node-view` 产出的 `UserControl` 由 tree-view 模板的 `NodeTemplate` 用 `Canvas.Left="{Binding Anchor.Horizontal}"` / `Canvas.Top="{Binding Anchor.Vertical}"` / `Width="{Binding Size.Width}"` / `Height="{Binding Size.Height}"` / `Panel.ZIndex="{Binding Anchor.Layer}"` 摆位（`workflow-tree-view/TemplateClass.jalxaml` 的 `NodeTemplate`）。**没有对应的适配器类型**（没有 `WorkflowNodeView`），也不存在 `ApplyPosition()`。

**当前形状**：「自绘但宿主 box-cull」成立，且有 IL 级证据（本节）。「梯度交接测量」不在本家 —— 指的不是这里，是 TransitionSystem 的 `Samplers/BrushSampler.cs` 梯度采样实现，见 `memory/modules/TransitionSystem/adapters/jalium.md` §2.3。「连线视图自盒化（盒 == 每线 bbox，定位+烘焙相消）」归 `WorkflowLinkBounds.Apply`（`WorkflowLinkBounds.cs` 的 `Apply`）＋ 模板的 `BuildCurve()`（`workflow-link-view/TemplateClass.jalxaml.cs`）。本家**没有** `IsVirtual` / `IsDragPreview` / `PortCenter`（全适配器与模板一处都没有）—— 只跳过树的拖拽预览，判据是 `IsVirtualLink(link) => link.Sender.Parent is null && link.Receiver.Parent is null`（`workflow-link-view/TemplateClass.jalxaml.cs` 的 `IsVirtualLink`）：两端都还没落到卡片上的占位槽（`slot.Parent` 为 `null`），脱了插槽的真实连线照样渲染。连线端点直接取绑定送来的 `Sender/Receiver.Anchor`，`slot.Anchor` 是量测结果（§2.3）—— 不从模型几何反查端口中心。

### 2.2 有标记，也有名字作用域

`.jalxaml` 工具链完整
（`Jalium.UI.Build` 的 `EnableDefaultJalxamlItems` + `**\*.jalxaml` + `JalxamlCodeBehindTask`），
`FrameworkElement.FindName` 在模板的 name scope 里按 `x:Name` 正常工作（实测）。表面与节点视图因此
与 WPF 同形地按名字取部件。

⚠ **两条仍要记住的边界**（实测，见 §〇）：
- `DataTemplate` **内部**的 `x:Name` 对根 `FindName` **不可见**（与 WPF 同）。枚举器里的槽只能先拿
  `ItemContainerGenerator.ContainerFromIndex(i)`，再沿视觉树往下找带槽 DataContext 的那个元素。
- `RelativeSource AncestorType` 只认**框架类型**（`UserControl` / `Canvas` 可用），自定义
  `clr-namespace` 类型不解析；**带前缀的附加属性路径**（`(behaviors:X.Y)`）在绑定路径里**根本不解析**。
  这两条合起来毙掉了 WPF 那句「节点模板隔着 DataTemplate 读表面变换」，见 §2.4。

### 2.3 端口位置由槽控件实测写回

`WorkflowSlotLayoutBehavior` 按 `SlotNames` / `SlotEnumeratorNames` 找到模板里那些槽控件，从控件的
`DataContext` 取槽视图模型，量出**中心**，写回 `slot.Anchor`。⇒ `slot.Anchor` **是**测量结果，
是端口命中与连线端点的唯一来源。

⚠ **坐标系**：世界位移发布在**宿主的附着属性**上、由节点/连线模板绑进各自的 `RenderTransform`
（见 §2.4），所以位移在坐标宿主**之内** —— 这里与其余六家同一条契约：`SlotAnchorFromVisualCenter`
（减去 `ActualOffset`）。中心用 `TransformToVisual(coordinateHost).Transform(localCenter)` 量：
`TransformToAncestor` 只给原点，半个尺寸在 Viewbox 缩放下不成立。

⚠ **缩放提交时的时序**：`NotifyZoomCommitted` 在调用方 `UpdateLayout()` 之后同步 `SyncNow` 重测所有节点，
再在 Render 优先级给连线视图补一次重绘。把测量排队到 Render 批次会让节点先动、连线晚一拍 —— 缩放中端点抖动。

### 2.4 画布变换：附着属性发出去，模板绑**普通属性**

与 WPF 同一条路：世界位移作为**附着属性** `WorkflowSurfaceBehavior.CanvasTransform` 发布在**宿主**上，
节点/连线模板把它绑进各自的 `RenderTransform`。

**唯一的不同是「怎么读」**：WPF 的模板直接 `Path=(behaviors:WorkflowCanvasTransformBehavior.Transform)`
读附着属性，而本家的绑定**读不到括号路径**（见 §〇，`(Canvas.Left)` 也不行）。所以**树视图模板的 `UserControl` 类**（`workflow-tree-view/TemplateClass.jalxaml.cs` 的 `CanvasTransformProperty` / `CanvasTransform`）把那个
附着属性**用一个 CLR 属性再暴露一次**：

```csharp
public static readonly DependencyProperty CanvasTransformProperty = WorkflowSurfaceBehavior.CanvasTransformProperty;
public Transform? CanvasTransform => GetValue(CanvasTransformProperty) as Transform;
```

模板于是绑 `CanvasTransform`（按属性名解析，CLR 属性照样带变更通知）。

⚠ **位移不能写在画布自己身上** —— 那种写法会让位移落在坐标宿主**之上**，
逼着槽锚点用一个别家都不用的 `SlotAnchorFromCanvasLocal`。

### 2.4.2 连线视图照 WPF 用绑定 —— 但要多一道 NaN 守卫

**WPF 的写法（四个 `StartLeft/…` 绑定）在本家能用**（实测）；症状（连线冻住、从原点画一条）的主导原因是槽锚点没重测，见 §四-0。

⚠ 但有一处 WPF 不需要、本家**必须**加的东西：

> **绑定送来的是 `NaN`。** 占位槽（拖拽预览的两端）的默认锚点就是 `new(NaN, NaN, 0)`，NaN 进盒子后
> 渲染器会**抛** `ArgumentException: Width and Height cannot be negative`（实测：不加守卫时
> **应用直接退出**）。所以 `Refresh` 在算盒子之前要 `double.IsNaN(...)` 挡一道。
>
> 而且 DP 的默认值是 **0**，与「绑定还没送到」无法区分 —— 所以「不画」要靠模型那条
> `IsVisible && IsRenderReady()` 守卫来定，几何则用 DP。两道守卫要一起在。

**盒子仍然在属性变更期摆，不在 `OnRender` 里摆**（渲染器按盒裁剪，见 §2.1）：DP 一变就重算几何并挪盒子，
`OnRender` 只负责画。盒子要盖住**四个控制点**（曲线凸包），只盖两端点会把鼓出去的那段静默切掉。

### 2.4.3 两个相位的坑：按下要挂预览、收尾要排在别的处理器之后（2026-10-06 重测）

**① 按下的一切都挂在具名按下源上**（`PointerPressSource.PreviewMouseDown` →
`WorkflowSurfaceBehavior.OnPointerPressSourceDown`）。
**宿主收不到的是「按下」，不是「一切」**：26.10.9 实测，宿主的 `PreviewMouseDown`（隧道，含
`handledEventsToo: true`）与冒泡 `MouseDown` **整场不触发**，而**同一处注册的** `PreviewMouseMove`
照常触发 ⇒ 断在按下这条路径怎么搭出来，在 Jalium 侧，不是相位、也不是 `Handled`。

⇒ **别再把按下路由挂回宿主**（`WorkflowSurfaceBehavior.Attach` 的注释记了这一条）。

⚠ **这条相位结论只对「按下」成立，别延伸成「宿主预览相不可靠」**（2026-10-06 重测）：普通滚轮就挂在
**宿主预览相**上（`Mouse.PreviewMouseWheelEvent`，`WorkflowSurfaceBehavior.cs` 的 `HookWheel`；处理器 `OnMouseWheel`，里面让开 Ctrl+那支），实测**收得到** —— 三格滚轮三行。改挂冒泡相
（`Mouse.MouseWheelEvent`）也收到 2/2，但位置与目标取在滚动容器滚过之后，所以留在预览相。
Ctrl+滚轮缩放仍挂 `ScrollViewer.PreviewMouseWheel`（`HookZoom`，只在 `ZoomEnabled` 时挂）。

**按下源比节点卡更浅、在隧道里更早跑**（同构树上以真实分发量过：宿主 → 按下源 → 卡片，三级全触发），
但这条顺序**不保证**，所以两边都做了一遍防御：表面状态里 `RoutedPress` 存的是**按下的事件对象本身**
（一次物理按下全程同一个 args 实例，下一次必然是新的，`SurfaceState.RoutedPress`），
表面与组件各自路由前都拿它比一次，谁都只路由一遍。

**组件（`WorkflowNodeDragBehavior` / `WorkflowSlotConnectionBehavior`）订的是隧道相 `PreviewMouseDown`，
不能再订 `PreviewMouseLeftButtonDown`** —— 后者是隧道相翻译出来的 **Direct** 事件、args 另建一份
（`UIElement.ReRaiseButtonEvent`），拿它就没法和按下源对「是不是同一笔」。`RouteComponentPress`
（`WorkflowSurfaceBehavior.cs` 的 `RouteComponentPress`）是组件交回按下、换回句柄的唯一入口（组件订
`PreviewMouseDown` 就要自己判 `e.ChangedButton`）。

**② 「兜底收虚拟连线」不能无条件排在预览相。** 宿主的预览处理器**早于**槽自己的预览处理器，所以一句
无条件的 `ResetVirtualLinkCommand` 会在槽有机会完成连接**之前**就把橡皮筋收掉 —— 症状是**所有连线都连不成**，
而表面上一切正常（菜单能弹、端口能按）。⇒ 只在**松手没落到端口上**时才兜底；落到端口上的三种出口
（连成 / 松回自己 / 落在接不了的口）由槽自己收尾。

⚠ 这两条都**静默**：没有异常、没有日志，只有把界面真的点一遍才看得见。

### 2.5 缩放/滚动的时序：Jalium 的 `ScrollTo` 不保证同步落地，`ScrollChanged` 不可靠

**这套守卫在适配器的表面行为里**（`WorkflowSurfaceBehavior.cs`）：

- `SurfaceState.ZoomPin`（字段、`ArmZoomPin` 的写入）+ `ZoomPinLifetimeMs = 250`：提交缩放目标后把视口钉在该目标上，直到 viewer 报告落地（±0.5）或超过 250ms（`UpdateViewport(SurfaceState)`）。
- 理由在 `SurfaceState.ZoomPin` 与 `ArmZoomPin` 的注释里：Jalium 的 `ScrollTo` 之后立刻读 offset 可能读到**尚未生效的缩放前值**，而 `ScrollChanged` 会在落地前先发一次；若照读就会用陈旧窗口覆写 `Helper.Viewport`，下一次 `Virtualize` 把刚物化的连线裁掉（节点因为按自己的矩形进池而留下）→ **深缩放链接消失 ~100ms**。
- 兜底：`UpdateViewport(SurfaceState, …)` 里视口未测量时（`vw<=0`）退回整块画布，否则首次 `Virtualize` 会在 0 尺寸视口上空转，初始节点/连线全不出现。
- **宿主零调用者**：`NotifyZoomCommitted` 由表面**内部**在缩放提交后自己调（`OnZoomPreviewMouseWheel` 的两个分支），demo 宿主一处都不调 —— 缩放手势本身归表面（`ZoomEnabled` 附着属性 + `OnZoomPreviewMouseWheel`）。

⇒ **这家的"链接在深缩放下闪没"有两个独立成因**：表面侧的视口竞态（本节，靠 `ZoomPin` 解）与渲染侧的盒裁剪（§2.1，靠自盒化解）；**修一个不会修好另一个**。

### 2.5.1 视口写回与恢复

- **26.11.1 的 `ScrollableWidth/Height` 跟随画布实际尺寸**（`ExtentWidth − ViewportWidth`）。无头夹具必须像 Avalonia/WPF 那样显式把 `CanvasLayout.OriginSize` 设成场景尺寸（两家的夹具都是 8000×6000）；默认 1920×1080 会把表面撑成 2000×2000、滚动范围被夹到 ~1212，小地图拖拽与 `Locate` 的断言就对不上。
- **写回**：`ScrollChanged` → `Refresh` → `UpdateViewport(state, h, v)`，其中 `SetVirtualizeInset`、`helper.Viewport`、`layout.ViewportOffset` 三者同源于**一次** `WorkflowSurfaceMath.ViewportOffsetFromScroll(h, v, layout)`（单一换算点，世界坐标）。
- **未测量那一支（`viewportWidth/Height <= 0`）刻意不写回**：那时写进去的是假的 `(0,0)`，会被当成存档。它照旧只做 `SetVirtualizeInset` 与整块画布的 `Helper.Viewport`，所以「首次 Virtualize 空转」那个原行为没变。
- **恢复**：`CaptureViewportRestore`（按 `LastRestoreTree` 去重，`HasViewportRestore` 为真才存）→ `QueueViewportRestore`（`Dispatcher.BeginInvoke(DispatcherPriority.Loaded, …)`，`ClampValue` 夹到 `ScrollableWidth/Height` 再滚，随后补一次 `UpdateViewport` + `UpdateOverlays`）。
- ⚠ **`Refresh` 与 `BindTree` 两处都要「先捕获、后 `UpdateViewport`」**：`BindTree` 在 `OnLoaded` 里比 `Refresh` 先跑，漏掉它的话它那一次写回会在捕获之前把存档冲掉。
- **与 `ZoomPin`（§2.5）不打架**：单参数的 `UpdateViewport(state)` 在 pin 守卫处就返回（陈旧的 `ScrollChanged` 到不了会写回的那个重载），pin 期间走 `UpdateViewport(state, pin.X, pin.Y)` ⇒ 写回的是 pin 的目标值；`NotifyZoomCommitted` 与 `PanMoved` 也走那个重载。
- 启动瞬间会先写一次 `(0,0)`、再被队列里的恢复盖掉（另外六家同形，不是这家的问题）。

实测（2026-10-11）：滚三格 `ViewportOffset` 走 `(0,0)→(0,48)→(0,96)→(0,144)`；临时种 `ViewportOffset = (300,200)` 后捕获记 `has=True`、启动确实停在 `300,200`（13 秒后仍稳定），种入代码已还原。

⚠ 照旧**不做「没存档就居中」** —— 适配器只负责存与还原，居中是宿主的 UX 决定（[extension.md](../extension.md) §3.9-10 第 10 条）。

### 2.5.2 `ZoomPin` 会吞掉滚轮那次视口写回 —— 节点整段消失、且不会自己回来（2026-10-11 实测）

把「归适配器的滚轮」与「`ZoomPin` 守卫」放在一起，就出现一个**没有报错、只有画面错**的窗口：

| 组 | 视口(画布) | Scale | 元素 |
|---|---|---|---|
| **爆发**（Ctrl+缩放提交后 <250ms 滚一格普通滚轮） | `41,120` | 0.91 | **节点 0/3**（只剩网格） |
| **对照**（等 600ms 再滚同样一格） | `41,120` | 0.91 | **3/3** |

同一视口、同一缩放，0/3 对 3/3 ⇒ **模型视口被 pin 吞了**：`OnMouseWheel` → `Scroller.ScrollBy` 真滚了 viewer；随后 `ScrollChanged` → `Refresh` → `UpdateViewport(state)` 走到 pin 分支（pin 未落且 viewer 已离开 pin 目标），于是 `UpdateViewport(state, pin.X, pin.Y)` **写回的是缩放目标**、滚轮的新位置被丢掉 ⇒ 屏上是新视口、池里是旧视口的项。

⚠ **比「250ms 窗口」更重**：pin 的 250ms 是**惰性**的 —— `ageMs > ZoomPinLifetimeMs` 只在**下一次** `UpdateViewport` 调用时才判。所以爆发之后若没有后续滚动/尺寸/缩放事件，模型视口**一直停在 pin**（实测 +1s 仍是 0/3）。补一格滚轮立刻恢复（`→ 1/3`，pin 因 age>250 被清）。

**可见条件**：机制恒成立，但**看不看得见取决于视口边界外 48px 内有没有节点**。默认数据下「恰好一格」两组收尾逐字相同（`41,328 / 1/3`）⇒ 可见集层面**未复现**；一旦那一格跨过节点边界（delta 更大、或边界恰好压着节点）就是**整段消失且不自愈**。

⇒ 修的方向不是动 `ZoomPin` 的时长，而是让**滚轮那一路的写回不被 pin 分支吃掉**（滚轮不是缩放的伴随动作，它有自己的新位置）。

### 2.5.3 滚轮：挂点 + 闸门 + 默认滚复用平移

**挂点是画布冒泡相 + `handledEventsToo`**（`canvas.AddHandler(Mouse.MouseWheelEvent, handler, true)`，挂/摘共用同一份 `state.MouseWheelHandler`）。实测：`sender=Canvas` 恒成立；**没有双重滚动**；**Ctrl+滚轮缩放不受影响**（同一次 Ctrl 滚轮没有 `ScrollBy` trace —— 两支不重叠）。

**「谁滚的」有 trace 证据**：闸门 `ROUTE` 之后**每一格都有一条 `ScrollBy`** ⇒ 画布滚动**全部由适配器执行，平台链一次都没参与**（`Handled` 在画布处就挡住了 `ScrollContentPresenter`）。这与 Avalonia 相反 —— 那家到边那格是**平台链**送的。

**边缘**：到边后画布 `1920×1080 → 1920×1242`（+162 = `DefaultPanExtendRatio` 0.15 × 1080），`视口 0,162`、`视口(世界) 0,0`（指针底下的世界点不动）。负向与正向边都能撑。

**闸门的 `ROOM` 分支**：demo 的卡片里没有第二台 `ScrollViewer`（Trimmed 是 `Viewbox`+`Grid`；完整版走 `NodeEditorSurface`、**根本不用适配器表面**），所以另用注入的内层 `ScrollViewer` 验（与 Razor 那家用注入容器验闸门同一种做法、同一段代码路径）；「不误伤」那一半是指针压 NodeView 卡上滚一格、画布照常 `0→48`：

```
内层 vOff 0 → 48 → 96 → 144 → 192 → 224(=vMax)      HUD 视口(画布) 恒 0,0
第 6 格（内层到边）⇒ ROUTE ⇒ 画布 0 → 48
```

⇒ 闸门正向分支**确实跑到了**：内层有余量 ⇒ 内层滚、画布不动；到边 ⇒ 画布接。

**深缩放的「连线瞬时闪没」也验了**（用可见集 trace：闪没 ~100ms 而截图采样 ~220ms，追不上）：临时订阅 `VisibleItems.CollectionChanged` 记 `scale/可见节点数/可见连线下标集`，连续 26 格 Ctrl+滚轮缩到 `Scale 0.10`，**期间 trace 一行未增**（恒 3 节点 / 2 连线，进出序列为空）；并用「大幅平移」做**灵敏度对照**（立刻出现 `nodes=3→2→1→0` 的离开序列）证明该 trace 抓得到变化。
⚠ **局限明标**：该数据规模下可见集在缩放中**本就未变**，所以这只证明「**本数据下**无闪没」，不等于「任意数据下不会」。

**单轴写入**：与 Avalonia 同 —— **契约一致性已验证、无可区分 A/B**（另一轴在 `[0,max]` 内时新旧数值相同）。

### 2.6 其余平台面的事实

- `WorkflowMinimapOverlay : FrameworkElement, IWorkflowMinimapOverlay`（`WorkflowMinimapOverlay.cs` 的类声明），`RulerBand => 0`（同类的 `RulerBand`）—— 它不是标尺，所以虚拟化 inset 为 0。
- `WorkflowMinimapOverlay.cs` 的 `OnMiniMouseDown` 先置 `_dragging` 再调 `PanToMini(...)` ⇒ **单击即居中**（不是"拖才动"）。这是小地图的既定语义，与 WPF 那份同款，**不是背离**。
- `WorkflowMinimapOverlay.ScrollViewer` 仍是普通属性，但**适配器里有赋值者**了：`ScrollViewerName` DP + `ResolveScrollViewer()`（`FindName(name) is ScrollViewer viewer`，`Loaded` 时再兜一次）⇒ 模板只要写 `ScrollViewerName="PART_ScrollViewer"`（`workflow-tree-view/TemplateClass.jalxaml` 的 `MinimapOverlay` 元素）就接上了，宿主**不必**自己赋。（非 Trimmed demo `Examples/Workflow/Jalium/` 仍走 `Minimap(viewer)` 构造器那条路，那是它自己的选择。）
- **小地图的内容（节点缩略框 + 包围盒）由 Core 的 `MinimapPipelineCore` 缓存**（`Src/Core/VeloxDev.Core/WorkflowSystem/Minimap/MinimapPipelineCore.cs` 的 `DrawContext` / `ContentBounds` / `ViewportBounds`、`IsDirty` / `Refresh` / `Invalidate`）：`Refresh` 只在 dirty 时重跑 `BuildDrawContext`（遍历 `tree.Nodes` 读 `node.Anchor`/`Size`，并订每个节点的 `Anchor`/`Size` 与每条 link 两端槽的 `Anchor`）。dirty 由树/集合变化、节点的 `Anchor`/`Size`、槽的 `Anchor`、`Metrics` 变化与显式 `Invalidate` 置位。Jalium 侧 `WorkflowMinimapOverlay.MarkDirty` 只把 `InvalidateVisual` 排到 UI 线程；`OnVisualChanged` 对每个视觉 DP 调 `pipeline.Invalidate()`，surface 经 `SurfaceMetricsPush.PushMinimap` 推来的 `ScrollOffset` / `ContentOffset` / `ViewportWidth` 也在其中。
- 节点的 `Anchor`/`Size` 是「原值 ÷ `Layout.Scale`」的折叠值（`Src/Core/VeloxDev.Core/WorkflowSystem/Templates/ViewModels/NodeDefaultViewModel.cs` 的 `Anchor` / `Size` getter）；缩放变化由 `WorkflowNodeScaleTracker` 转成这两个属性的 `PropertyChanged`（同文件的 `_scaleTracker` / `OnScaleDirty`）；`MinimapPipelineCore` 订这两个属性并置脏重算。

---

## 三、与 WPF 的差异

| 差异 | 现在的依据（当场量的） |
|---|---|
| **有 `WorkflowLinkBounds`** | 渲染器按 `RenderSize` 盒裁剪子元素、内容画到盒外**静默丢弃**（§2.1 的 IL 级依据）。WPF 让连线视图铺满整块画布即可，本家那样做会在缩放里陈盒掉整层线 |
| **槽再同步靠 `Loaded`/`SizeChanged`/模型变更 + 共享批次 `Schedule`（`Dispatcher.Render` 排一拍）** | 本家**没有 `LayoutUpdated` 事件**（26.10.9 反射清点，一个都没有） |

⇒ **这两条都是「WPF 那么做在本家跑不起来」，不是「本家想不一样」。**

## 四、坑（带依据）

0. **`PropertyChanged` 的 `sender` 是模型，不是宿主控件。** `WorkflowSlotLayoutBehavior`
   的模型变更回调若写成 `sender is not FrameworkElement control`，因为 sender 是那个**模型**，
   会**每次都在第一行返回**。后果是节点移动后槽锚点从不重测，**连线整条冻在拖动前的几何上**，
   而日志里一个错都没有（`AttachCount` / `SyncCount` 都正常，因为那条路只跑了一次）。
   ⇒ 模型 → 宿主的回指靠自己存（这里用 `ConditionalWeakTable<INotifyPropertyChanged, FrameworkElement>`）。



1. **重做连线视图时最容易漏掉"自盒化"。** 盒子必须**每次端点折叠/移动都同步** `Canvas.SetLeft/Top` + `Width/Height` —— 调 `WorkflowLinkBounds.Apply`（`WorkflowLinkBounds.cs` 的 `Apply`），且把几何减掉它交回的 `originX/originY` 再画（`workflow-link-view/TemplateClass.jalxaml.cs` 的 `BuildCurve()`）。漏任一半 = 深缩放静默消失或整条线画歪。依据：`WorkflowLinkBounds.cs` 的 `<remarks>` 注释与 §2.1 的 IL 判定链。
2. **拖拽预览的跳过条件是 `IsVirtualLink(link) => link.Sender.Parent is null && link.Receiver.Parent is null`**（`workflow-link-view/TemplateClass.jalxaml.cs` 的 `IsVirtualLink`），并把 `CanRender` / `link.IsVisible` / `link.IsRenderReady()` 与 `_hasBounds` 的检查都留下（`Refresh()`、`OnRender`）。判据的要点是「两端都还没落到卡片上」，脱了插槽的真实连线照样渲染。
3. **模板解析失败是「按类型记一次不支持」，不是抛异常。** `ViewPool` 只要 `ItemsSource` 非空就建 `ViewSourcePipeline`（`TemplateSelector` 可以为空，为空时 `ViewFactory` 直接走资源回退）。`ViewFactory.FindTemplate` 的顺序是选择器 → 沿视觉树上溯 `Resources`（按 `DataType` 比）→ `Application.Resources`；三处都落空时 `CreateView` 返回 `null`，Core 池把它记进 `unsupportedTypes` 后**静默跳过该运行时类型**（`ViewPoolCore.Materialize`）⇒ 表现是**什么都不显示，且没有异常**。诊断顺序：先确认模板产物里 `behaviors:ViewPool.TemplateSelector="{StaticResource WorkflowTemplateSelector}"` 与 `ItemsSource` 都写了（`workflow-tree-view/TemplateClass.jalxaml` 的 `PART_Canvas` 元素），再确认选择器覆盖了该 ViewModel 类型。
4. **视图池按具体类型分桶，批大小归 Core 的自适应策略。** Core 池的回收桶用 `item.GetType()` 作键 ⇒ 两种 ViewModel 类型即使视图相同也各建一份；active 表与 pending 集按**引用身份**比较（`ViewReferenceComparer`），同类型的不同实例不会互相顶替，同一引用重复入集也只留一个。适配器默认传 Core 的 `AdaptiveViewPoolPolicy`（起始 64 个 / 8ms，按平滑吞吐与 pending 收敛到 8 / 12 / 16ms 预算）；`Replace` 由 Core 处理（`Remove` + `Add`）。
5. **删视图时同时置 `Collapsed` 与 `DataContext = null`**（`ViewHosting/ViewContainer.cs` 的 `Deactivate`；`Detach` 不摘树，只有 `Destroy` 从 `Children` 移除）。⇒ 视图里读 `DataContext` 的代码在池化回收后会看到 `null`，`DataContext is not null` 不代表已初始化。取模型都靠 `DataContextChanged`：连线模板 `workflow-link-view/TemplateClass.jalxaml.cs` 的 `OnDataContextChanged` 与 `Rebind` 里的 `DataContext as ILinkComponent`；槽/节点侧由 `WorkflowSlotLayoutBehavior` 在 `DataContextChanged` 上重接（`WorkflowSlotLayoutBehavior.cs` 的 `OnDataContextChanged`）。
6. **哪些插槽显示由卡片刻画决定，命中与端点由适配器量测决定 —— 改名字要同步。** `WorkflowSlotLayoutBehavior` 不反射读模型：它按 `SlotNames` / `SlotEnumeratorNames` 找到模板里那些具名控件（`FindName`），从控件 `DataContext` 取槽，量测后写回 `slot.Anchor`。⚠ 但它**硬编码**了几个 watch 的属性名（`RebuildWatchedNames`：`Anchor`、`Size` + 配置进来的名字 + `InputSlot`/`OutputSlot`/`OutputSlots`）—— 改节点 view-model 上这几个属性名会让重测静默丢失对应触发。`DrawCard` / `WorkflowPortGeometry` 都不存在。
7. **csproj 的独有设定会咬人**（详见 `memory/modules/TransitionSystem/adapters/jalium.md` §2.5）：单目标 `net10.0` 无平台后缀（`Src/Adapters/VeloxDev.Jalium/VeloxDev.Jalium.csproj` 的 `TargetFramework`）⇒ 只能引用 `Jalium.UI.Controls` 这个平台中性包；`NoWarn` 是 `1573;1591`（同文件的 `NoWarn`）—— 1591 关「公开成员缺 XML 注释」、1573 关「参数缺 `<param>` 标签」，与平台包或 DP 拆箱无关（模板与适配器里值类型 DP 包装走泛型 `Read<T>`，不产生值类型拆箱告警）。

---

### 4.x 连线右键菜单：条目归模板资源，订阅/定位/开合归表面行为

Jalium 的连线右键菜单分成四件：

- **条目由宿主的标记声明。** tree-view 模板在 `UserControl.Resources` 里放一个 `<ContextMenu x:Key="LinkContextMenu">`
  （`workflow-tree-view/TemplateClass.jalxaml` 的 `<ContextMenu x:Key="LinkContextMenu">`；demo 里就一条 `Delete`），表面用附着属性 `LinkMenuKey="LinkContextMenu"`
  按 key 取（`WorkflowSurfaceBehavior.cs` 的 `LinkMenuKeyProperty`；`OnLinkPointerPressed` 里的 `host.FindResource(menuKey)`）。
  存 key 而不是菜单对象，是因为这个附着属性挂在表面自己的根上，`{StaticResource}` 会在定义它的字典之前求值（`LinkMenuKeyProperty` 的注释）。
- **订阅/定位/开合都在表面行为里。** `OnLinkPointerPressed` 挂在 Core 的 `Input.PointerPressed` 上，判右键 + 连线命中后：
  打开前 `menu.DataContext = link`（菜单不在视觉树里、继承不到宿主的 DataContext），先置 `input.IsSuspended = true`，
  再 `menu.Open(ToMenuPosition(host, e.Position))`；`menu.Closed` 里放开挂起并清状态。
- **定位链**：`e.Position`（表面局部）→ `host.PointToScreen`（物理像素）→ **`root.PointFromScreen(...)`（根视觉局部）** → `menu.Open(...)`
  （`ToMenuPosition`）。最后那一步不能省：`ContextMenu.Open` 把点**直接写进** `Popup.HorizontalOffset/VerticalOffset`，
  而 `Popup` 按**根视觉/窗口客户区**解释它们；直接把 `PointToScreen` 的结果喂进去，菜单会整体偏移一个窗口原点。
- **线被别处删掉（Agent / Undo / …）时收菜单归宿主。** 树 helper 在开着的菜单指着的那条线离开 `tree.Links` 时发 **`LinkRemoved`**
  （Core `Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/Components/ITreeComponent.cs` 的 `LinkRemoved`、`Templates/Helpers/TreeHelper.cs` 的 `LinkRemoved`；
  没有 `ContextMenuDismissRequested`）。表面只认自己这份菜单指着的那条（`ReferenceEquals(state.MenuLink, link)` → `state.LinkMenu?.Close()`：
  `WorkflowSurfaceBehavior.cs` 的 `OnLinkRemoved`），收起后照常报 `Closed`、挂起随之放开；**平台仍然不记任何账**。

另外：Jalium 的 `MenuItem` **不会自己关菜单** —— 条目点完要显式 `menu.Close()` 再执行命令。模板产出的 `ContextMenu` 是**空的**（`workflow-tree-view/TemplateClass.jalxaml` 的 `<ContextMenu x:Key="LinkContextMenu">`，注释写着 "Add or remove entries here"），
demo 往里放了一条 `<MenuItem Header="Delete" Command="{Binding DeleteCommand}" />`（`Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/TreeView.jalxaml` 的 `LinkContextMenu`）；`DataContext` 被表面设成那条线，
所以 `{Binding DeleteCommand}` 解析得到。挂起的承担方仍是 Core —— `WorkflowInput.Route(PointerEventArgs)` 之后的
`ApplyDefault` 在 `IsSuspended` 时直接返回（`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Events/WorkflowInput.cs` 的 `ApplyDefault`）。完整 demo 的 `MouseLeave` 也只清端口悬停 + 转发，
收菜单走它自己那套 `menu.Close(); DeleteLink(link);`（`NodeEditorSurface.cs`），与适配器无关。

### 4.y `Jalium.UI.*` 26.11.1 的 headed 全量物化比 26.10.x 慢约 5 倍，并带两条渲染回归

同一份适配器源码只换包引用，跑 `headed --adapter jalium --policy adaptive --nodes 2000 --links 3000 --iterations 5 --steps 20`（Debug+Optimize，5 轮中位，本机 2026-11-26，跨机不可横比）：Full visibility 416–477 ms / 69–71 MB → 2294–2621 ms / 125–131 MB；Initial 4.1–4.7 → 8.7–10.4 ms；Scroll step 0.9–1.3 → 1.4–2.6 ms。管线迁移（`ViewSourcePipeline`）在两组包上都更快（Full −8%~−12%，Scroll step −22%~−47%）。`JALIUM_RENDER_BACKEND=software` 时 Full 只差 19%，退化集中在 D3D12 后端；热点在 Jalium 布局层（`FrameworkElement.UpdateCssTextInkBounds` 每次 `ArrangeCore` 遍历全部视觉子元素、`Panel.EnsureZOrderMap` 逐项算 CSS z-order），不在适配器。

26.11.1 的两条渲染回归：

- 画布向负方向扩展（`Layout.ActualOffset > 0`）后，节点 `Canvas.Left/Top` 落在视口外、仅靠模板 `RenderTransform`（`CanvasTransform`）移进视口的那部分不上屏 —— HUD 仍报可见、模型仍可拖动。Windows 默认开启的整帧捕获（`Window.ResolveRenderThreadEnabled` 的 `PlatformFactory.IsWindows`）在滚动后只把卡片描边录进帧（`OnPostRender`），填充命令没进帧。
- 圆形输出槽画到卡片外（新增的 Vello 椭圆快路径 `TryEncodeEllipseIntoPendingVello` 处理圆形 `Ellipse`，圆角矩形走正确的 SDF 路径）；按画出的槽命中 `Canvas`/`ContentPresenter`，拖不了连线；关掉整帧捕获仍复现。

适配器侧在 26.11.1 下另有一处坐标偏差：`WorkflowSurfaceBehavior.WorldPoint` 用 `GetPosition(Canvas) − ActualOffset`，而滚动在 Canvas 之下应用，world 点错；用装饰器 `ScrollOffset/ContentOffset` 换算才对。

VeloxDev 的 Jalium 包引用低于 26.11.1，以上慢化与回归在当前树上不生效。

## 五、非 Trimmed demo 的落点

**`Examples/Workflow/Jalium/` 与适配器的耦合面只有小地图一个类**：它只派生适配器的一个类型
（`Views/Workflow/Minimap.cs` 的 `Minimap : WorkflowMinimapOverlay`），其余 `NodeEditorSurface` /
`NodeViewBase` / `NodePorts` / `PortGlyph` / `NodeChrome` 全是它**自己的自绘代码**。

## 六、这份文件没写的东西

- 适配器的成员列表、继承树、文件清单 —— IDE 里一按就有。
- 契约本身（七角色职责、绑定挂在哪、`Viewport` 谁写、渲染就绪门、滚轮方向、缩放提交顺序、注册位置表、`dotnet new` 模板约定）—— 在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3 与 `skills/veloxdev-create-workflow/references/new-adapter.md`。
- 这家怎么用（模板包怎么生成、demo 怎么跑、七个 GUI 页面的对照）—— `skills/veloxdev-create-workflow/references/gui/jalium.md` 与 `references/view-layer.md`。
- 其余六家的差异 —— 同目录另外六份。
