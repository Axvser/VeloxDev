# WorkflowSystem — Jalium

> **读法**：契约与注册位置在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3，本文不重复；
> 人面向的「这家怎么用」在 `skills/veloxdev-create-workflow/references/gui/jalium.md`（七角色表在 `references/view-layer.md`），本文只指路不抄。
> 本文只写这家的**硬限制、刻意背离、该家特有的坑**。
> **2026-10-03 起适配器重新拥有整套表面**：七个角色各有可继承基类（`WorkflowTreeView` / `WorkflowNodeView` / `WorkflowLinkView` / `WorkflowGridDecorator` / `WorkflowTemplateSelector` / `WorkflowMinimapOverlay` / 端口几何与布局），模板与 Trimmed demo 只是薄派生。**下文描述的平台限制与机制，落点都在适配器基类里，不再在模板/demo。**
> 代码在 `Src/Adapters/VeloxDev.Jalium/Attached/Workflow/`（12 个文件）与 `Src/Templates/VeloxDev.Jalium.Templates/working/content/`（七个薄条目）。
> **路径写法**：下文的裸文件名（`ViewManager.cs:110` 等）都相对 `Src/Adapters/VeloxDev.Jalium/Attached/Workflow/`；提到模板产物时相对 `Src/Templates/VeloxDev.Jalium.Templates/working/content/`；提到 demo 时相对仓库根的 `Examples/Workflow/Jalium Trimmed/`；引用 Core、别家适配器时一律写全路径。

---

## 一、这家必须实现什么，为什么是这些

**七个视图角色现在都由适配器实现（以可继承基类的形态）。** 契约本身（每个角色要做什么）没变；变的是落点：`WorkflowTreeView`、`WorkflowNodeView`、`WorkflowLinkView`、`WorkflowGridDecorator`、`WorkflowTemplateSelector`、`WorkflowPortGeometry` + `WorkflowPortLayout`、`WorkflowMinimapOverlay`（后一个早在适配器里）。宿主与模板从这些基类派生，只写策略（调色板、卡片画法、端口布局值、工厂接线）。六个曾经的零消费者行为类（`WorkflowSurfaceBehavior` / `WorkflowCanvasTransformBehavior` / `WorkflowNodeDragBehavior` / `WorkflowSlotConnectionBehavior` / `WorkflowSlotLayoutBehavior` / 独立 `WorkflowGridDecorator`）与自装配外壳 `WorkflowTreeView : Grid` **没有**回来；现在是按 `adapter-base-class-specifications.md` 重新切开的七个基类，`WorkflowTreeView : Canvas` 是活表面的基类。

### 1.1 `IWorkflowTemplateSelector` 替掉的是什么

- **`IWorkflowTemplateSelector` 替掉 `DataTemplateSelector` + `DataTemplate`。** Jalium 有 `Jalium.UI.DataTemplate`，但**没有 `DataTemplateSelector` 这个类型**（反射清点 `Jalium.UI.Managed` 可证：`DataTemplate` 有三个构造器 `()`/`(object)`/`(Type)`，`DataTemplateSelector` 一个都不存在）。⇒ 别家靠 `DataTemplateSelector.SelectTemplate(object, DependencyObject)` 返回 `DataTemplate` 的分派，在这里必须换成一个返回**已构造控件**的接口：`IWorkflowTemplateSelector.CreateView(object item)`（`IWorkflowTemplateSelector.cs:7`）。XML 注释自己写着 "mirroring the role of a DataTemplateSelector in the XAML adapters"。基类 `WorkflowTemplateSelector` 把它做成四个工厂 + `virtual CreateView` 分派（`WorkflowTemplateSelector.cs:34-50`）。
  **注意，这不是 Jalium 独有**：WinForms 在 `Src/Adapters/VeloxDev.WinForms/Attached/Workflow/ViewManager.cs:14-22` 里有一个**逐字同名同形**的接口（`Control CreateView(object item)`，连注释都一样）。⇒ 这条轴上的真实划分是「有标记语言的三家（WPF/Avalonia/WinUI）用 `DataTemplateSelector`，无标记语言的两家（Jalium/WinForms）用自造接口，MAUI/Razor 各按自家形状」。
- **表面外壳那份补偿现在进了包。** WPF 的 `Examples/Workflow/WPF/Demo/Views/Workflow/WorkflowView.xaml:14-132` 用标记声明 `PART_SurfaceBorder` / `PART_GridDecorator` / `PART_ScrollViewer` / `PART_Canvas` / `PART_MinimapOverlay` 的嵌套与绑定；Jalium 没有 XAML，于是同一份结构做进适配器基类 `WorkflowTreeView`（持 `PortLayout` / `GridDecorator` / `TemplateSelector` 三个属性，并由 `SetTree` 接线 ViewPool），模板只派生设值。WinForms 同样走「适配器发基类」，但它的宿主装配在模板的 `workflow-tree-view/TemplateClass.cs` 里更多。
  **非 Trimmed demo** 例外：它用自己的画布外壳 `Examples/Workflow/Jalium/Demo/Views/Workflow/NodeEditorSurface.cs:24` 的 `NodeEditorSurface : Canvas`，**不派生**适配器基类（见 §五）。

### 1.2 池化换成 `ConditionalWeakTable`，不是 `ItemsControl`

别家把 `ViewPool` 的附着属性挂在 `ItemsControl` 上、由 `ItemsControl` 的模板机制生成视图。Jalium 的 `ViewPool` 挂在**任意 `Panel`** 上（`ViewPool.cs:13`：`ConditionalWeakTable<Panel, ViewManager>`），由 `ViewManager` 手工同步集合。⇒ `WorkflowTreeView.SetTree` 把两个附着属性设在 `Canvas` 自己身上（`WorkflowTreeView.cs:169-170`）。**非 Trimmed demo 完全不用 `ViewPool`** —— 它自己按模型建卡片、自己画连线（见 §五）。**这条是后面 §三·1 那条差异的前提**。

---

## 二、平台硬限制，与由此产生的做法

> 这一节是全文最有价值的部分：它决定别家能照抄什么、不能照抄什么。落点现在是适配器基类。

### 2.1 渲染器按 `RenderSize` 盒裁剪子元素，不按内容判交 —— 连线视图必须"自盒化"

`Jalium.UI.Media.Visual.ShouldRenderChild(DrawingContext, UIElement, Point)` 是 `Private, Static, HideBySig`（反射可证）。它解析到的判定链是：拿子元素的 `RenderSize` 做盒（**只有 `Effect` 的 `EffectPadding` 会扩大它**，`IClipBoundsDrawingContext.CurrentClipBounds` 那条只在有裁剪上下文时生效），再算 `MapChildBoundsToCurrentDrawingSpace` → `Rect.IntersectsWith`。⇒ **内容画到自己盒子外面的部分会被整块丢掉，且不报错。**

**由此产生的唯一正确做法：一个自绘元素想画到哪里，就必须先把自己的盒挪/撑到那里。** 这条在两个角色上各体现一次：

- **连线视图**：适配器基类 `WorkflowLinkView.cs:255-271` 的 `UpdateBounds()` 在每次端点折叠/移动后，把元素自身 `Canvas.SetLeft/Top` 与 `Width/Height` 设成**这条线自己的 canvas-local 包围盒**（`:260-263` 的 `BoxPad = 6` 外扩）；`:238-251` 的 `EndpointsCanvasLocal()` 算端点，`:89-114` 的 `OnRender` 再用 `local = canvas − (_viewX,_viewY)`（`:99-100`）把几何烘回元素局部坐标 —— **定位与烘焙相消**，视觉输出与画在 (0,0) 等价。`:14-22` 的注释把根因写死了：*"the renderer culls children by that box, not by content, and a full-canvas stale box is exactly what vanished at deep zoom"*。
  ⇒ **这是本仓库"深缩放连线消失"谱系在 Jalium 的最后一环**，与共享 Core 的负侧 cover 无关，别去 Core 里找。改连线视图时若把 `UpdateBounds` 删掉或让它滞后一帧，盒子就是陈旧的，**线会在深缩放下静默消失**。
- **节点视图**：适配器基类 `WorkflowNodeView.cs:191-201` 的 `ApplyPosition()` 同步设 `Canvas.SetLeft/Top` 与 `Width/Height`，同一份理由。

**核对历史记录**：「自绘但宿主 box-cull」—— **仍成立**，且现在是 IL 级证据（本节）。「梯度交接测量」—— 指的不是这里，是 TransitionSystem 的 `Samplers/BrushSampler.cs:79-94`，见 `memory/modules/TransitionSystem/adapters/jalium.md` §2.3。「连线视图自盒化（盒 == 每线 bbox，定位+烘焙相消）」—— **仍成立**，`WorkflowLinkView.cs:255-271` + `:89-114`。「`IsVirtual` 跳过 + `PortCenter` 反查」—— **前半条已作废**：`IsVirtual` 在 Jalium 适配器与模板里**一处都没有**，它被一个精确得多的判定取代：`IsDragPreview(link) => link.Sender is SlotDefaultViewModel && link.Receiver is SlotDefaultViewModel`（`WorkflowLinkView.cs:119-120`），只跳过树的拖拽预览，脱了插槽的真实连线照样渲染。**后半条仍成立**：`PortCenter` 仍是反查（`:211-234`，不读 `slot.Anchor`，从 `slot.Parent` 的节点几何算端口中心），返回 `Point?`，端点为 `null` 时跳过整条线而不是从原点画一条退化线。

### 2.2 没有标记语言，但也无需名字作用域

Jalium 没有 XAML 编译器替你建名字作用域；旧的补偿（模板产物里 `NameScope.SetNameScope` + `RegisterName`）**随旧表面一起消失**。现在适配器基类不按名字解析任何宿主的 `PART_*`（`WorkflowTreeView` 只吃三个属性，不 `FindName`），模板与 Trimmed demo 也一个 `RegisterName` 都没有。⇒ **这条不再是一个约束**；若你打算自己写一个按名取部件的表面，那才需要自己造名字作用域。

### 2.3 这家不测量视觉：端口几何是纯模型数学

**这是本节最需要记住的一条。** 适配器不量任何视觉坐标：端口中心由 `WorkflowPortGeometry`（`WorkflowPortGeometry.cs:93-121`）从**模型几何**算（`node.Anchor + 设计局部 · s`，`s = node.Size / DesignSize`），节点卡由基类按 `Anchor`/`Size` 定位（`WorkflowNodeView.cs:191-201`），连线端点同样走几何（`WorkflowLinkView.cs:211-234`）。没有一处 `TranslatePoint` / `TransformToVisual` / `GetCenterRelativeTo`。

- ⇒ **这条与另外六家全部相反**：六家的插槽布局在视觉坐标系里量（`TranslatePoint` / `TransformToVisual` / `GetCenterRelativeTo` + `SlotAnchorFromVisualCenter` / `SlotAnchorFromCanvasLocal`）；Jalium 连 `WorkflowSurfaceMath.SlotAnchorFromNode` 这类 helper 都**没有调用者**（`git grep` 源码零命中；只有 `bin/` 的编译产物残留符号）。**别把纯模型路径当模板抄到需要视觉测量的平台，反之亦然。**
- 端口枚举（哪个属性是输入口、`SlotEnumerator<T>` 怎么展开）也是按**属性名反射**读的（`WorkflowPortGeometry.cs:37-62,123-124`）—— 这是节点 view-model 里接口没描述的那部分，改属性名会让端口与命中静默错位。
- ⚠ **它从不写 `slot.Anchor`，而连线的命中判定不该要求它写**（2026-10-03 起）。`LinkHitTestEx.HitTest` 的门是「`IsVisible` + 已发布的曲线」，**不是** `IsRenderReady()`（那道门要求锚点非 NaN）。曾经有人为绕开那道门让 Jalium 开始写 `slot.Anchor`，那是错的：那条路线上每一条连线都会被静默拒掉（构建全绿、悬停与 Delete 全死）。**判据永远是曲线在不在，别把这家的不变量改回去。**
  - 在**跑起来的**完整 demo 里核过（不是读代码）：把指针位置喂给 `LinkInteraction.For(tree)` 后 `HoveredLink` 非空、再喂 `KeyEvent(Delete)` 树上的连线从 16 条掉到 15 条 —— 即「不写锚点 ⇒ 连线可命中」是成立的。

### 2.4 画布变换通道不存在

- 这家没有"画布变换"这条通道：视图按世界坐标自定位（§2.3），不需要宿主把渲染变换镜像给它们。
- 旧的镜像方法 `ViewManager.UpdateRenderTransforms` / `ViewPool.UpdateRenderTransforms` **已连同实现一起删除**（`git grep -n UpdateRenderTransforms -- Src Examples` 零命中）—— 不再是「有实现、结构上不可达」的残留。
  > ⚠️ 人面向的 `skills/veloxdev-create-workflow/references/gui/jalium.md` 与 `references/view-layer.md` 若还说「Jalium 把变换镜像到池化视图上」，那是**过期**的：现在没有这条通道。

### 2.5 缩放/滚动的时序：Jalium 的 `ScrollTo` 不保证同步落地，`ScrollChanged` 不可靠

**这套守卫现在在适配器的表面基类里**（`WorkflowTreeView.cs`），不再是宿主自写：

- `_zoomPin`（`:51`）+ `ZoomPinLifetimeMs = 250`（`:53`）：提交缩放目标后把视口钉在该目标上，直到 viewer 报告落地（±0.5）或超过 250ms（`UpdateViewport`，`:539-561`）。
- 理由在 `:43-50` 与 `:182-209` 的注释里：Jalium 的 `ScrollTo` 之后立刻读 offset 可能读到**尚未生效的缩放前值**，而 `ScrollChanged` 会在落地前先发一次；若照读就会用陈旧窗口覆写 `Helper.Viewport`，下一次 `Virtualize` 把刚物化的连线裁掉（节点因为按自己的矩形进池而留下）→ **深缩放链接消失 ~100ms**。
- 兜底：`:571-580` 视口未测量时（`vw<=0`）退回整块画布，否则首次 `Virtualize` 会在 0 尺寸视口上空转，初始节点/连线全不出现。
- 宿主侧只需在提交缩放后调 `surface.NotifyZoomCommitted(committedX, committedY)`（demo `MainWindow.cs:216,227`）。

⇒ **这家的"链接在深缩放下闪没"有两个独立成因**：宿主侧的视口竞态（本节，靠 `_zoomPin` 解）与渲染侧的盒裁剪（§2.1，靠自盒化解）。**修一个不会修好另一个**，别把两者的现象混着查。

### 2.6 其余平台面的事实

- `WorkflowMinimapOverlay : FrameworkElement, IWorkflowMinimapOverlay`（`WorkflowMinimapOverlay.cs:15`），`RulerBand => 0`（`:48`）—— 它不是标尺，所以虚拟化 inset 为 0。
- `WorkflowMinimapOverlay.cs:244-250` 的 `OnMiniMouseDown` 先置 `_dragging` 再调 `PanToMini(...)` ⇒ **单击即居中**（不是"拖才动"）。这是小地图的既定语义，与 WPF 那份同款，**不是背离**。
- `WorkflowMinimapOverlay.ScrollViewer` 是普通属性（`:55`），**适配器里没有赋值者** —— 宿主必须自己赋（demo `MainWindow.cs:64`）；`WorkflowTreeView.AttachScrollViewer` 只管表面自己的 viewer，不转给小地图。

---

## 三、与其它六家的差异

> **先给一个校准**：这家的适配器现在不小了（Workflow 面 11 文件 2139 行），但差异仍能一句说清：池化挂 `Panel`、不测量视觉、没有画布变换通道。

1. **池化挂在任意 `Panel` 上，不是 `ItemsControl`，且两个附着属性顺序无关。** `ItemsSource` 与 `TemplateSelector` 都挂 `OnChanged`（`ViewPool.cs:19`/`:25`），回调里同时读两者、只有**都非空**才建 `ViewManager`（`:49-60`），任一为空就 `Detach()`（`:61-64`）。**这里的做法和其他家不一样，因为别家由 `ItemsControl` 自己管模板与集合的顺序，这里得自己容忍"先设集合后设选择器"。** 另外 `ViewManager` 把选择器存成**字段**（`ViewManager.cs:18`、`:27`），换选择器必须走 `SetTemplateSelector`，直接改面板属性在 Jalium 上无效。`WorkflowTreeView.SetTree` 就是两个一起给。
2. **这家不测量视觉，别家都测量。** §2.3。端口几何走纯模型数学，连 `WorkflowSurfaceMath.SlotAnchorFromNode` 都不再用。**这里不一样，因为这家把"世界坐标 = 模型坐标"当成了不变式**，量视觉反而会在平移/自动长大时变陈旧。
3. **这家没有画布变换通道，别家都拥有。** §2.4。别照抄别家往这里补 `UpdateRenderTransforms` —— 方法已经删了，硬加会在自定位的视图上画出双重偏移。

---

## 四、坑（带依据）

1. **重做连线视图时最容易漏掉"自盒化"。** 盒子必须**每次端点折叠/移动都同步** `Canvas.SetLeft/Top` + `Width/Height`（`WorkflowLinkView.cs:255-271`），且 `OnRender` 必须把几何烘回局部（`:99-100`）。漏任一半 = 深缩放静默消失或整条线画歪。依据：`:14-22` 的注释与 §2.1 的 IL 判定链。
2. **拖拽预览的跳过条件要精确，不要回到 `IsVirtual`。** 用 `IsDragPreview`（`WorkflowLinkView.cs:119-120`），并把 `IsVisible` 与两个 `null` 检查都留下（`:93`、`:257`）。依据：`:116-118` 注释明写"脱了插槽的真实连线不能消失"。
3. **`_selector is null` 时 `AddItem` 静默返回。** `ViewManager.cs:122` —— 集合先到、选择器后到不会报错也不会补，只会**什么都不显示**。诊断顺序：先看 `WorkflowTreeView.TemplateSelector` 是不是设了（`SetTree` 会把它转给 `ViewPool`，`WorkflowTreeView.cs:169`）。
4. **视图池按具体类型分桶、且是即时创建。** `ViewManager.cs:127` 用 `item.GetType()` 作桶键 ⇒ 两种 ViewModel 类型即使视图相同也各建一份；`:33-49` 的 `Attach` 逐个建视图，**没有别家那种分批**（WPF 按 `DispatcherPriority.Background` 三个一批），大集合首帧会卡。依据：`:15`、`:33-49`、`:127`。
5. **删视图时同时置 `Collapsed` 与 `DataContext = null`**（`ViewManager.cs:163-164`）。=> 视图里读 `DataContext` 的代码（含 `WorkflowLinkView` 的守卫）在池化回收后会看到 `null`，别把 `DataContext is not null` 当成"已初始化"。三个基类都靠 `DataContextChanged` 取模型（`WorkflowNodeView.cs:52`、`WorkflowLinkView.cs:54`）。
6. **端口是反射读的，不是接口成员。** `WorkflowPortGeometry` 按属性名取 `InputSlot` / `OutputSlot` / `OutputSlots` + `Title`/`Name`（`WorkflowPortGeometry.cs:37-70,123-124`）；改节点 view-model 的属性名会静默错位。卡片刻画（`DrawCard`）与命中读的是**两套**：画由派生类决定，命中读 `PortLayout`/几何。
7. **csproj 的两条独有设定会咬人**（详见 `memory/modules/TransitionSystem/adapters/jalium.md` §2.5）：单目标 `net10.0` 无平台后缀（`Src/Adapters/VeloxDev.Jalium/VeloxDev.Jalium.csproj:7`）⇒ 只能引用 `Jalium.UI.Controls` 这个平台中性包；`NoWarn` 含 `8605;8604`（`:12`）⇒ `8605` 来自 `WorkflowMinimapOverlay.cs:43-52` 一族 DP 的 CLR 包装（`(double)GetValue(...)` 拆箱），新基类不注册 DP，所以没有新增触发点。改这两条 NoWarn 前先确认没别的地方会触发。

---

### 4.x 连线右键菜单：`ContextMenu.Open(Point)` 吃的是**根视觉坐标**，不是屏幕像素（2026-10-03 实测）

基类 `WorkflowTreeView` 新增 `OnBuildLinkMenu(menu, link)`（模板派生后增删条目）与
`OnConnecting`/`OnConnected`；菜单的订阅、定位、开合上报都在基类。定位那条链值得记：

`e.Position`（画布局部）→ `PointToScreen`（物理像素）→ **`root.PointFromScreen(...)`（根视觉局部）** → `menu.Open(...)`。
最后那一步不能省：反编译 `Jalium.UI.Controls` 26.10.8 可见 `ContextMenu.Open` 把点**直接写进**
`Popup.HorizontalOffset/VerticalOffset`，而 `Popup` 按**根视觉/窗口客户区**解释它们、自己再转屏幕；
框架内部右键路径传的也是 `e.GetPosition(null)`。直接把 `PointToScreen` 的结果喂进去，菜单会整体偏移一个窗口原点。

另外两条：Jalium 的 `MenuItem` **不会自己关菜单**（点完要显式 `menu.Close()`，与完整 demo 里那句
`IsOpen = false` 同因）；`OnMouseLeave` 在菜单开着时要提前返回 —— 那一条是为了躲 `LinkInteraction` 的
`Exited` 不认 `IsSuspended` 的老毛病（Core 已修，这层拦截现在冗余）。

## 五、非 Trimmed demo 连线三件事的落点（表面自绘，含右键菜单）

**先分清一件事：这家的非 Trimmed demo 没有连线视图。** §2.1 与 §四 里那些"连线视图自盒化"说的是 **适配器的 `WorkflowLinkView` 与 Trimmed demo 的派生版**；`Examples/Workflow/Jalium/Demo/` 下连线、端口、网格、标尺**全由 `Examples/Workflow/Jalium/Demo/Views/Workflow/NodeEditorSurface.cs` 自己画**（`:24` `NodeEditorSurface : Canvas`），没有 `LinkView`、不用 `ViewPool`、也不派生适配器基类（`MainWindow` 直接 new 表面 + `ScrollViewer` + 缩略图/信息浮层）。⇒ **没有控件可以承担"某一条连线的悬停/焦点/右键"**，三件事只能由画它的表面代管。

| 事 | 落点 | 依据 |
|---|---|---|
| 命中 | `HitTestLink(canvasPos)` 逐条量"点到折线的距离"，命中半径 6 画布像素 | `:248`（方法）、`:36`（`LinkHitRadius`）、`:1210`（`LinkCurve.DistanceTo`） |
| 悬停即选中 | `UpdateLinkHover` 挂在 `OnMouseMove` 的 `DragKind.None` 分支；**只改选中，不碰键盘焦点**（见结论 2、5） | `:277`（方法）、`:291`（写选中）、`:1739`（调用点） |
| 滚进视口 | 表面吃掉「把表面自己滚进视口」的请求 | `:113`（挂 `RequestBringIntoViewEvent`）、`:196`（处理，目标是自己才拦） |
| 高亮 | 选中那条整条换 `OrangeRed` 并加粗 1.5，彗星跟着换 | `:151`（色）、`:32`（粗）、`:1072-1074`（绘制分支） |
| Delete | 表面 `KeyDown`，加 `MainWindow` 的窗口级预览兜底，两条都进 `DeleteLink` | `:187`、`Examples/Workflow/Jalium/Demo/MainWindow.cs:337`、`:168` |
| 右键菜单 | 表面代开一份复用的原生 `ContextMenu`（只一项「删除连线」），`Placement = MousePoint` | `:1588`（`OnMouseDown` 的右键分支）、`:222`（开菜单）、`:208`（建菜单） |

> 行号写法沿用本文开头的约定：**裸 `:NNN` 都指 `Examples/Workflow/Jalium/Demo/Views/Workflow/NodeEditorSurface.cs`**（除非同格已写全路径或另注文件名）。

五条结论：

1. **命中量与画读的是同一张弧长表。** `HitTestLink` 用 `CurveFor`（`:1283`）取那条线**当前**的 `LinkCurve`，`DistanceTo` 逐采样段量点距（`:1210`）—— 所以**只有画出来的那一道笔画能命中**：两端之间的空当不算，缩放后端点按 `node.Size/DesignSize` 折叠也不会错位。**不要去写第二套几何**：两份几何只要有一处不同，就会出现"看得见抓不住 / 抓得住看不见"。命中半径的不变式是**"不超出画出来的范围"**，不是一个独立挑出来的数：这里的 6 画布像素与最宽那层辉光同量级（`thickness + 9` 即 11px 宽 ⇒ 半宽 5.5px，见 `DrawLink`），读作"辉光能到的地方就能抓"。
2. **Delete 靠窗口级预览兜底，悬停因此不必收焦点（曾经收过，见结论 5）。** 表面仍 `Focusable = true`（`:118`）并有自己的 `KeyDown`（`:187`），但那只是**第二道闸**：真正的主路径是 `Examples/Workflow/Jalium/Demo/MainWindow.cs:331-340` 的窗口级预览，且**故意不看焦点**：判据是"有没有选中"—— 悬停即选中，"指针搭在连线上"本身就说明这一下 Delete 是冲那条线来的；指针不在线上时没有选中，`DeleteSelectedLink()` 返回 `false`，按键原样落回输入框。**改回"焦点不是 TextBox 才处理"会让悬停-按 Delete 在焦点落到输入框时静默失效**（实测：焦点停在侧栏输入框时按 Delete，HUD 的连线数 12→11，画布偏移不变）。⇒ 因此 `UpdateLinkHover` **不调 `Focus()`**：收了焦点就多出一条"滚进视口"（结论 5），而 Delete 根本不需要它。
3. **两个菜单时序坑，都实测过。** 其一：**点菜单项时 `Closed` 先到、`Click` 后到** —— 若在 `Closed` 里清 `_menuTarget`，`Click` 拿到的是 `null`，表现出来是**菜单关了、线没删**（改前实测到的就是这个现象）。所以 `_menuTarget` 不在 `Closed` 里清（`:82` 声明、`:232` 写入、`:217` 读取），只在 `PruneCurves`（`:638-640`）清掉已经不在树上的那条。其二：**菜单项点完不会自己收** —— 不显式关，删完线菜单还杵在画布上挡着东西（`DeleteLink` 里 `:181`，菜单项里 `:216`）。还有一条联动：**弹层一起来，指针就算"离开"了表面**，那条 `MouseLeave` 会把选中一起抹掉，于是菜单开着时"线不亮"；守卫直接读弹层自己的 `IsOpen`（`MenuOpen`，`:87`）而**不另立标志**——标志一旦漏掉回落（比如 `Closed` 没来）就永久卡住，悬停从此不再更新；`MouseLeave`（`:131`）与 `UpdateLinkHover`（`:280`）各读一次。
4. **这家有完整的原生菜单栈，仓库里此前零使用**（反射 `Jalium.UI.Managed.dll` 查到；`Jalium.UI.Controls.dll` 只是转发程序集）：`Jalium.UI.Controls.ContextMenu : MenuBase`（带 `IsOpen` / `Open(Point)` / `StaysOpen` / `Placement` / `PlacementTarget`）、`MenuItem`（`Header` / `Click` / `Command`）、`MenuFlyout : Primitives.FlyoutBase`、`Primitives.Popup`，外加 `FrameworkElement.ContextMenu` + `ContextMenuService.TryOpen/Open` 这套 WPF 式接线，菜单主题也在（`Jalium.UI.Managed` 里的 `_Dict_..._Themes_Controls_MenusToolbars`）。**别把"仓库里没人用过"读成"这家没有"** —— 下一个人不必再反射一遍。但这根线要自己接：`FrameworkElement.ContextMenu` 的自动右键路径认的是**元素**，连线不是元素、表面又是整块画布，直接挂上去等于"画布任意处右键都弹删除菜单"。这里的做法是 `OnMouseDown` 里先 `HitTestLink`，命中才 `IsOpen = true`（`:1588-1590`、`:222-239`）。

5. **「悬停取焦点」曾经会滚动画布，因为表面整块就是画布。** 链路（IL 级证据，反射 `Jalium.UI.Managed.dll`）：`Window.OnPlatformEvent` 里处理安全区/软键盘那个分支先 `InvalidateMeasure()`，紧接着调 `Window.ScrollFocusedEditorIntoViewAfterLayout()`；后者取 `Keyboard.FocusedElement`，在它的**下一次 `LayoutUpdated`** 上对它调 `FrameworkElement.BringIntoView()`（`Jalium.UI.Window+<>c__DisplayClass784_0::<ScrollFocusedEditorIntoViewAfterLayout>b__0`）；`BringIntoView` 抛出 `RequestBringIntoViewEvent`，冒泡到 `ScrollViewer.HandleRequestBringIntoView` → `MakeVisible(TargetObject, TargetRect)` + `e.Handled = true`。⇒ **焦点只要落在表面上，画布就会被滚进视口**，而 2000+ 见方的画布"滚进视口"只能是**跳到原点**（实测：HUD 的 `视口(画布)` 从 `712, 61` 一步跳到 `0, 0`）。这就是用户报的「**极小概率触发滚动**」：要同时满足「表面上恰好有键盘焦点」+「平台事件（安全区/软键盘，台式机上很少见）」+「其后有一次布局」。**别把这条当成"悬停会滚"去复现**——它不依赖悬停本身，依赖的是焦点：
   - **本家的处理是两条一起**：① 去掉因 —— 悬停不再 `Focus()`（结论 2）；② 拦请求 —— 表面在 `:113` 挂 `RequestBringIntoViewEvent`，处理函数（`:196`）**只在自己是目标时** `e.Handled = true`，所以**只拦"把整块画布滚进视口"这一次**，节点卡里输入框发起的同类请求照旧冒泡（作用域不扩散，与 Avalonia 在同一条轴上）。**只做②不做①也够安全**（请求层已封住），但①顺手删掉了一条无用的焦点变化。
   - **还查清一条容易误判的**：`FocusVisualManager.OnKeyboardFocusedChanged` 里也有一处 `BringIntoView`，看起来是"焦点一变就滚"的元凶 —— 但 `EnsureInitialized`（唯一安装该钩子的地方）**在 26.10.8 的任何一个 Jalium 程序集里都没有调用者**（`Jalium.UI.Managed` 只有它内部两处 `ldftn` 自引用；Desktop/Gpu/Xaml 零命中）。**这条是死代码，别照它写复现步骤。**
   - **可复核的数字**：改前扫描悬停 40 次中跳 1 次（且带有"每次悬停前激活窗口"这个模式）；改后同一模式 40 次 + 纯鼠标移动 40 次 + 6 次真正落在连线上的悬停，HUD 偏移**一次没变**；用临时探针直接调 `surface.BringIntoView()`：不拦时 `712,61 → 0,0`，拦后不动（探针已从最终代码里删除，拦截代码未变）。

---

## 六、这份文件没写的东西

- 适配器 11 个文件的成员列表、继承树、文件清单 —— IDE 里一按就有。
- 契约本身（七角色职责、绑定挂在哪、`Viewport` 谁写、渲染就绪门、滚轮方向、缩放提交顺序、注册位置表、`dotnet new` 模板约定）—— 在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3 与 `skills/veloxdev-create-workflow/references/new-adapter.md`。
- 这家怎么用（模板包怎么生成、demo 怎么跑、七个 GUI 页面的对照）—— `skills/veloxdev-create-workflow/references/gui/jalium.md` 与 `references/view-layer.md`。
- 其余六家的差异 —— 同目录另外六份。
