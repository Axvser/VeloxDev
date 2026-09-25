# WorkflowSystem — Jalium

> **读法**：契约与注册位置在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3，本文不重复；
> 人面向的「这家怎么用」在 `skills/veloxdev-create-workflow/references/gui/jalium.md`（七角色表在 `references/view-layer.md`），本文只指路不抄。
> 本文只写这家的**硬限制、刻意背离、该家特有的坑**。代码在 `Src/Adapters/VeloxDev.Jalium/Attached/Workflow/`。
> **路径写法**：下文的裸文件名（`ViewManager.cs:110`、`WorkflowSurfaceBehavior.cs:614` 等）都相对 `Src/Adapters/VeloxDev.Jalium/Attached/Workflow/`；引用 Core、别家适配器、模板与 demo 时一律写全路径。

---

## 一、这家必须实现什么，为什么是这些

七个角色齐全（`WorkflowSurfaceBehavior` / `WorkflowCanvasTransformBehavior` / `ViewPool` + `ViewManager` / `WorkflowNodeDragBehavior` / `WorkflowSlotConnectionBehavior` / `WorkflowSlotLayoutBehavior` / `WorkflowGridDecorator` + `WorkflowMinimapOverlay`），**全目录 11 个文件** —— 另加两个额外文件（`IWorkflowTemplateSelector.cs`、`WorkflowTreeView.cs`），它们的成因是同一条：Jalium 没有标记语言。

### 1.1 `WorkflowTreeView` / `IWorkflowTemplateSelector` 替掉的是什么

- **`IWorkflowTemplateSelector` 替掉 `DataTemplateSelector` + `DataTemplate`。** Jalium 有 `Jalium.UI.DataTemplate`，但**没有 `DataTemplateSelector` 这个类型**（反射清点 `Jalium.UI.Managed` 可证：`DataTemplate` 有三个构造器 `()`/`(object)`/`(Type)`，`DataTemplateSelector` 一个都不存在）。⇒ 别家靠 `DataTemplateSelector.SelectTemplate(object, DependencyObject)` 返回 `DataTemplate` 的分派，在这里必须换成一个返回**已构造控件**的接口：`IWorkflowTemplateSelector.CreateView(object item)`（`IWorkflowTemplateSelector.cs`）。XML 注释自己写着 "mirroring the role of a DataTemplateSelector in the XAML adapters"。
  **注意，这不是 Jalium 独有**：WinForms 在 `Src/Adapters/VeloxDev.WinForms/Attached/Workflow/ViewManager.cs:14-22` 里有一个**逐字同名同形**的接口（`Control CreateView(object item)`，连注释都一样）。⇒ 这条轴上的真实划分是「有标记语言的三家（WPF/Avalonia/WinUI）用 `DataTemplateSelector`，无标记语言的两家（Jalium/WinForms）用自造接口，MAUI/Razor 各按自家形状」。**`memory/modules/WorkflowSystem/adapters/wpf.md` 把 `IWorkflowTemplateSelector` 记成"Jalium 多三个之一"，从 WPF 的视角没错，但别据此以为别家没有。**
- **`WorkflowTreeView` 替掉的是 XAML 里那份"画布外壳"标记。** WPF 的 `Examples/Workflow/WPF/Demo/Views/Workflow/WorkflowView.xaml:14-132` 用标记声明 `PART_SurfaceBorder` / `PART_GridDecorator` / `PART_ScrollViewer` / `PART_Canvas` / `PART_MinimapOverlay` 的嵌套与绑定；Jalium 没有 XAML，于是把同一份结构做成一个可复用的 `Grid` 子类（`WorkflowTreeView.cs:14`），在构造器里 new 出各部件并 `RegisterName`（`:88-91`、`:119`、`:137`），因为**没有 XAML 就没有名字作用域**，`FindName` 必须靠手工注册（见坑 2）。同形的补偿在 WinForms 也有，但它把代码优先的宿主放在**模板**里（`Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-tree-view/TemplateClass.cs:40-166`），没进适配器 —— 这是两家在这条轴上的真实差别。
- **但 `WorkflowTreeView` 在仓库里**零消费者**。Jalium 模板与 **Trimmed** demo 各自写一个 `TemplateClass : Canvas` / `TreeView : Canvas`（`Src/Templates/VeloxDev.Jalium.Templates/working/content/workflow-tree-view/TemplateClass.cs:19`；`Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/TreeView.cs:19`），后者再把自己 `using WorkflowTreeView = Demo.Views.Workflow.TreeView;` 顶掉（`Examples/Workflow/Jalium Trimmed/Demo/MainWindow.cs:12-37`、`Examples/Workflow/WinForms Trimmed/Demo/MainForm.cs:5-23` 同款别名）。**非 Trimmed demo 两者都不用**：它的画布外壳是 `Examples/Workflow/Jalium/Demo/Views/Workflow/NodeEditorSurface.cs:24` 的 `NodeEditorSurface : Canvas`，网格、端口、连线也全归它画（见 §五）。⇒ **它是可选的外壳，不是契约的一部分；改它不会影响任何 demo。** 别把它当"这家的入口"去读。

### 1.2 池化换成 `ConditionalWeakTable`，不是 `ItemsControl`

别家把 `ViewPool` 的附着属性挂在 `ItemsControl` 上、由 `ItemsControl` 的模板机制生成视图。Jalium 的 `ViewPool` 挂在**任意 `Panel`** 上（`ViewPool.cs:11-13`：`ConditionalWeakTable<Panel, ViewManager>`），由 `ViewManager` 手工同步集合。⇒ `WorkflowTreeView` 与 **Trimmed** demo 都把 `ViewPool.SetItemsSource` 直接设在 `Canvas` 自己身上（`WorkflowTreeView.cs:114-115` 同款；demo 见 `Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/TreeView.cs:114-115`）。**非 Trimmed demo 完全不用 `ViewPool`** —— 它自己按模型建卡片、自己画连线（见 §五），所以本节这条与它无关。**这条是后面 §3.3 那条差异的前提**。

---

## 二、平台硬限制，与由此产生的做法

> 这一节是全文最有价值的部分：它决定别家能照抄什么、不能照抄什么。

### 2.1 渲染器按 `RenderSize` 盒裁剪子元素，不按内容判交 —— 连线视图必须"自盒化"

`Jalium.UI.Media.Visual.ShouldRenderChild(DrawingContext, UIElement, Point)` 是 `Private, Static, HideBySig`（反射可证）。它解析到的判定链是：拿子元素的 `RenderSize` 做盒（**只有 `Effect` 的 `EffectPadding` 会扩大它**，`IClipBoundsDrawingContext.CurrentClipBounds` 那条只在有裁剪上下文时生效），再算 `MapChildBoundsToCurrentDrawingSpace` → `Rect.IntersectsWith`。⇒ **内容画到自己盒子外面的部分会被整块丢掉，且不报错。**

**由此产生的唯一正确做法：一个自绘元素想画到哪里，就必须先把自己的盒挪/撑到那里。** 这条在两个角色上各体现一次：

- **连线视图**：`Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/LinkView.cs:194-222` 的 `UpdateBounds()` 在每次端点折叠/移动后，把元素自身 `Canvas.SetLeft/Top` 与 `Width/Height` 设成**这条线自己的 canvas-local 包围盒**（`BoxPad` 外扩）；`:224-256` 的 `OnRender` 再用 `local = canvas − (_viewX,_viewY)` 把几何烘回元素局部坐标 —— **定位与烘焙相消**，视觉输出与画在 (0,0) 等价。模板版同形（`Src/Templates/VeloxDev.Jalium.Templates/working/content/workflow-link-view/TemplateClass.cs:204`、`:232`）。`:194-197` 的注释把根因写死了：*"the renderer culls children by that box, not by content, and a full-canvas stale box is exactly what vanished at deep zoom"*。
  ⇒ **这是本仓库"深缩放连线消失"谱系在 Jalium 的最后一环**，与共享 Core 的负侧 cover 无关，别去 Core 里找。改连线视图时若把 `UpdateBounds` 删掉或让它滞后一帧，盒子就是陈旧的，**线会在深缩放下静默消失**。
- **节点视图**：`NodeView.ApplyPosition()` 同步设 `Canvas.SetLeft/Top` 与 `Width/Height`（`Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/NodeView.cs:153-166`），同一份理由。

**核对历史记录**：「自绘但宿主 box-cull」—— **仍成立**，且现在是 IL 级证据（本节）。「梯度交接测量」—— 指的不是这里，是 TransitionSystem 的 `Samplers/BrushSampler.cs:79-94`，见 `memory/modules/TransitionSystem/adapters/jalium.md` §2.3。「连线视图自盒化（盒 == 每线 bbox，定位+烘焙相消）」—— **仍成立**，`LinkView.cs:194-256`。「`IsVirtual` 跳过 + `PortCenter` 反查」—— **前半条已作废**：`IsVirtual` 在 Jalium 适配器与模板里**一处都没有**（全目录搜索零命中），它被一个精确得多的判定取代：`IsDragPreview(link) => link.Sender is SlotDefaultViewModel && link.Receiver is SlotDefaultViewModel`（`LinkView.cs:128-133`），只跳过树的拖拽预览，脱了插槽的真实连线照样渲染。**后半条仍成立**：`PortCenter` 仍是反查（`:135-190`，不读 `slot.Anchor`，从 `slot.Parent` 的节点几何算端口中心），返回 `Point?`，端点为 `null` 时跳过整条线而不是从原点画一条退化线。

### 2.2 没有标记语言 ⇒ 没有名字作用域，`RegisterName` 是硬要求

`WorkflowTreeView.cs:88-91`、`:119`、`:137` 手工 `RegisterName`，因为 Jalium 没有 XAML 编译器替你建名字作用域。⇒ **`WorkflowSurfaceBehavior` 用名字解析宿主部件（`PART_Canvas` / `PART_ScrollViewer` / …）时，名字不是"起个名"，是"必须注册过"**；漏一个就是运行时静默找不到部件（行为读到 `null` 就早退，看不出错）。

### 2.3 这家不测量、不定位任何视图 —— 全部由视图自己算

**这是本节最需要记住的一条。** 适配器里**没有任何一处** `Canvas.SetLeft/SetTop`（全适配器搜索只命中一条注释：`WorkflowSlotLayoutBehavior.cs:352`）。视图的位置与尺寸由视图自己在 C# 里写（`NodeView.ApplyPosition()`），插槽锚点由纯模型数学算（下一条）。

历史记录的「梯度交接测量」不适用于此：这里不做视觉测量，做的是**模型数学**。

- `WorkflowSlotLayoutBehavior.SyncSlot`（`WorkflowSlotLayoutBehavior.cs:343-359`）的实参里有 `host` 与 `coordinateHost`，**函数体一次都没读它们** —— 它只用 `Canvas.GetLeft/Top(control) + ActualWidth/Height/2` 得局部偏移，再交给 `WorkflowSurfaceMath.SlotAnchorFromNode(...)`。`:350-352` 的注释把理由写明了：*"Pure model math — no TranslatePoint / render-transform / canvas dependence, so it never goes stale on pan"*。
  ⇒ **这条与另外六家全部相反**：WPF 分支在 `coordinateHost is not null` 上走 `TranslatePoint` + `SlotAnchorFromVisualCenter`（`Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowSlotLayoutBehavior.cs:349-362`），Avalonia `:284`，WinUI 用 `TransformToVisual` + `SlotAnchorFromCanvasLocal`（`:399-408`），MAUI 用 `GetCenterRelativeTo`（`:453-478`）。**别把 Jalium 的 `SyncSlot` 当模板抄到需要视觉测量的平台，反之亦然。**
- 连带后果：`host` / `coordinateHost` 那条参数链（`:267`、`:304`、`:317`、`:321`、`:338`）与 `GetActualOffset`（`:407-415`）**全是死的**。前者是历史残留，后者零调用者。改这块时不要"顺手用起来"——用起来就等于把一个已收敛的纯模型路径改回视觉测量。
- **类文档是错的**：`WorkflowSlotLayoutBehavior.cs:12-14` 仍写着 *"measures each slot control center **relative to a coordinate host, subtracts the tree's Layout.ActualOffset**, and writes it back to slot.Anchor"*。**这两件事代码都不做**（既不读 host，也不减 `ActualOffset`，减偏移是在 `LinkView` 的 `EndpointsCanvasLocal` 里做的）。以代码为准。

### 2.4 变换通道是死的，且注释描述的机制不存在

- `WorkflowCanvasTransformBehavior.cs:23-24` 的 `Apply` **在 `Src/` 与 `Examples/` 里零调用者**（另四家都在用：WPF `:571`、Avalonia `:501`、WinUI `:578`、WinForms `:479`）。⇒ 这家没有"画布变换"这条通道，因为视图按世界坐标自定位（§2.3），不需要宿主把渲染变换镜像给它们。
- `:26-30` 的 `OnTransformChanged` 故意为空，注释说 *"The ViewManager mirrors it onto active node/link views' RenderTransform"* —— **这个镜像的代码是写了的，但整条路径没有任何调用者**：`ViewManager.UpdateRenderTransforms`（`ViewManager.cs:67-73`，第 `:71` 行就是 `item.View.RenderTransform = transform;`）只被 `ViewPool.UpdateRenderTransforms`（`ViewPool.cs:40-46`）在 `:44` 调一次，而后者是 **`internal static`** —— 既在 `Src/` 与 `Examples/` 里零调用者，**别家程序集就算想调也调不到**。⇒ 状态是「有实现、结构上不可达」，**不是「没有实现」** —— 这两句话对下一个 agent 的后果完全不同：前者是「要不要接上」，后者是「要从头写」。类文档 `:6-9` 描述的就是这段可达性为零的机制。**判断依据以调用图为准，不要以注释为准；也别去 demo 里找它的调用者，找不到不是遗漏。**
  > ⚠️ 连带修正一个曾经写反的判断：人面向的 `skills/veloxdev-create-workflow/references/gui/jalium.md:35` 那句 *"`ViewManager` mirrors any transform onto the pooled views instead"* **与代码是相符的**（`:71` 就是这句的实现），它的问题不是"说错了"，而是**描述了一条永远不会被走到的路** —— 读那页的人会以为变换通道是活的。要改的是把它标成 dead，不是把它改写成"没有这个机制"。

### 2.5 `ApplyLayout` 的注释描述的是 **demo** 的模型，不是适配器的行为

`WorkflowSurfaceBehavior.cs:566-578` 的注释说 *"views are positioned at world + ActualOffset via Canvas.Left/Top (NO RenderTransform)"*，但函数体只调了 `UpdateGridDecorator` 与 `UpdateMinimapOverlay` —— **它没有定位任何视图，也做不到**（视图不归适配器管，§2.3）。请把这段注释读作"这家的坐标模型声明"，而不是"这段代码在做什么"。

### 2.6 缩放/滚动的时序：Jalium 的 `ScrollTo` 不保证同步落地，`ScrollChanged` 不可靠

这条**不在适配器里**，而在宿主视图（两个 demo + 模板）里，因为 wheel 缩放由宿主驱动（见 §3.2）：

- `Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/TreeView.cs:52-60` 的 `_zoomPin` + `ZoomPinLifetimeMs = 250`：提交缩放目标后把视口钉在该目标上，直到 viewer 报告落地（±0.5）或超过 250ms。
- 理由在 `:222-237` 与 `:153-178` 的注释里：Jalium 的 `ScrollTo` 之后立刻读 offset 可能读到**尚未生效的缩放前值**，而 `ScrollChanged` 会在落地前先发一次；若照读就会用陈旧窗口覆写 `Helper.Viewport`，下一次 `Virtualize` 把刚物化的连线裁掉（节点因为按自己的矩形进池而留下）→ **深缩放链接消失 ~100ms**。
- 兜底：`:187-199` 视口未测量时（`vw<=0`）退回整块画布，否则首次 `Virtualize` 会在 0 尺寸视口上空转，初始节点/连线全不出现。
- 同一份代码在模板里（`Src/Templates/VeloxDev.Jalium.Templates/working/content/workflow-tree-view/TemplateClass.cs:52-58`、`:156-160`）。

⇒ **这家的"链接在深缩放下闪没"有两个独立成因**：宿主侧的视口竞态（本节，靠 `_zoomPin` 解）与渲染侧的盒裁剪（§2.1，靠自盒化解）。**修一个不会修好另一个**，别把两者的现象混着查。

### 2.7 其余平台面的事实

- **带 `RenderSize` 的框架元素是 `FrameworkElement`**，`IsWorkflowNodeOrSlotVisual` 按 `DataContext` 判类型（`WorkflowSurfaceBehavior.cs:662-663`）—— 与 WPF 逐字相同，非差异。
- `WorkflowGridDecorator : Decorator, IWorkflowGridDecorator`（`WorkflowGridDecorator.cs:13`），`RulerBand => RulerThickness`（`:46`），默认 `RulerThickness = 28.0`（`:16`）；`ArrangeOverride` **故意不给子元素留边**（`:68-80`），标尺是浮在视口上的、不占布局。
- `WorkflowMinimapOverlay : FrameworkElement, IWorkflowMinimapOverlay`（`WorkflowMinimapOverlay.cs:15`），`RulerBand => 0`（`:48`）。**两个角色的 `RulerBand` 语义不同**：装饰器返回标尺厚度（它占了一条视觉带），小地图返回 0（它不是标尺）。两者都转发给 `WorkflowSpatialEx.SetVirtualizeInset` —— 装饰器那条在 `WorkflowSurfaceBehavior.cs:614`，小地图那条靠 `UpdateMinimapOverlay`（`:618-632`）不设 inset，所以它返回什么都无所谓。⇒ **有标尺的那家（装饰器）必须让 `RulerBand` 有值，否则标尺下面的节点看不见**（`extension.md` §3.8 同结论）。
- `WorkflowMinimapOverlay.cs:244-262` 的 `OnMiniMouseDown` 先置 `_dragging` 再 `PanToMini(...)` ⇒ **单击即居中**（不是"拖才动"）。这是小地图的既定语义，与 WPF 那份同款，**不是背离**。

---

## 三、与其它六家的差异

> **先给一个校准**：与其余六家**同形**的有：滚轮门（`WorkflowSurfaceBehavior.cs:322-328`，`HasFlag(Control)` 判修饰键、`delta > 0 ? 1/1.1 : 1.1`）、平移的 `ClampScrollOffset` 用法（`:535-538`）、`empty` 的变换回调（另三家也是空的）、`ViewManager` 的 `Replace` 处理（与 WinForms 同，与 WPF/Avalonia/WinUI/MAUI 不同 —— 见 `adapters/wpf.md` 坑 5）。下面只列真正的背离。

1. **这里的适配器不拥有画布变换，别家都拥有。** 别家的 `WorkflowCanvasTransformBehavior.Apply` 被 `WorkflowSurfaceBehavior` 主动调用，把渲染变换推给视图（WPF `:571`、Avalonia `:501`、WinUI `:578`、WinForms `:479`）；**这里的 `Apply` 零调用者，`ViewPool.UpdateRenderTransforms` 也零调用者**（`WorkflowCanvasTransformBehavior.cs:23-24`、`ViewPool.cs:40-46`；注意 `ViewManager.UpdateRenderTransforms` 自己是有实现的，见 §2.4）。**这里的做法和其他家不一样，因为这家的视图按世界坐标自定位、连线的盒随端点自盒化（§2.1/§2.3），没有任何东西需要被"镜像变换"。** ⇒ 照抄别家往这里补调用会画出双重偏移。
2. **这里的适配器不测量、不定位视图，别家都测量。** 另六家的插槽布局分支在视觉坐标系里量（`TranslatePoint` / `TransformToVisual` / `GetCenterRelativeTo` + `SlotAnchorFromVisualCenter` / `SlotAnchorFromCanvasLocal`）；**这里走纯模型数学 `SlotAnchorFromNode`，连传进来的 `host` 都不读**（§2.3）。**这里不一样，因为这家把"世界坐标 = 模型坐标"当成了不变式**，量视觉反而会在平移/自动长大时变陈旧。
3. **这里的链接层与节点层池化在同一个 `Panel` 上，别家分层。** `ViewPool` 挂在 `Canvas` 自己身上（`WorkflowTreeView.cs:114-115`、demo `TreeView.cs:114-115`），所以**任何**连线视图沿祖先链都会命中 `state.Canvas`。⇒ 这家的 `IsSurfaceBlankInteraction`（`WorkflowSurfaceBehavior.cs:634-660`）**没有** WPF/Avalonia/WinUI 那一步 `IsWorkflowLinkVisual`（三家都有：WPF `:678-682` 带 `BezierCurveView`/`PolylineCurveView` 类名白名单，Avalonia `:602`，WinUI `:737`）。**这里的做法和其他家不一样，因为链接视图与节点视图同宿主，空白判定在末尾那条 `ancestors.Any(x => x == state.Canvas)`（`:656-659`）就已经命中** —— 补一步反倒是死代码。（`IsWorkflowLinkVisual` 在三家是给独立链接层的兼容兜底，Jalium 没有那层。）
4. **`ViewPool` 的两个附着属性共用同一个回调、且顺序无关。** `ItemsSource` 与 `TemplateSelector` 都挂 `OnChanged`（`ViewPool.cs:15-25`），回调里同时读两者、只有**都非空**才建 `ViewManager`（`:58-69`），任一为空就 `Detach()`（`:70-73`）。**这里的做法和其他家不一样，因为别家由 `ItemsControl` 自己管模板与集合的顺序，这里得自己容忍"先设集合后设选择器"（以及反过来的 detach 抖动）。** 另外 `ViewManager` 把选择器存成**字段**（`ViewManager.cs:18`、`:27`），而 WPF 是从 panel 上惰性再读（`Src/Adapters/VeloxDev.WPF/Attached/Workflow/ViewManager.cs:234`）—— 换选择器必须走 `SetTemplateSelector`，直接改面板属性在 Jalium 上无效。
5. **`WorkflowTreeView` 是唯一一个"适配器自带的完整宿主控件"**（除 Razor 的 razor-component 版）。别家把宿主留在模板/demo 的标记或代码里。这是 §1.1 那条（无标记语言）的直接后果。
6. **`WorkflowGridDecorator` 装在适配器里，别家多数由模板/demo 提供。** WPF 是模板把 demo 的装饰器 cast 成 `IWorkflowGridDecorator`（`adapters/wpf.md` §一 的注）；这里的装饰器在 `WorkflowGridDecorator.cs`，`WorkflowTreeView` 还能 `SwapGridDecorator` 换掉它（`WorkflowTreeView.cs:32-42`）。

---

## 四、坑（带依据）

1. **`SyncSlot` 的参数是诱饵。** `WorkflowSlotLayoutBehavior.cs:343-359` 的 `host`/`coordinateHost` 传了但从不读；`GetActualOffset`（`:407-415`）零调用者。**想改插槽锚点就改 `SlotAnchorFromNode` 那三个实参**，不要"修好"那些未用参数。依据：函数体与全仓调用搜索。
2. **忘了 `RegisterName` 就是静默失效。** 没有 XAML 名字作用域（§2.2），`WorkflowSurfaceBehavior` 按 `PART_*` 名字取部件，取不到就早退。`WorkflowTreeView.cs:88-91`/`:119`/`:137` 是范本。
3. **重做连线视图时最容易漏掉"自盒化"。** 盒子必须**每次端点折叠/移动都同步** `Canvas.SetLeft/Top` + `Width/Height`（`LinkView.cs:198-222`），且 `OnRender` 必须把几何烘回局部（`:241-242`）。漏任一半 = 深缩放静默消失或整条线画歪。依据：`:194-197` 的注释与 §2.1 的 IL 判定链。
4. **拖拽预览的跳过条件要精确，不要回到 `IsVirtual`。** 用 `IsDragPreview`（`LinkView.cs:132-133`），并把 `IsVisible` 与两个 `null` 检查都留下（`:200`、`:228`、`:189`）。依据：`:128-131` 注释明写"脱了插槽的真实连线不能消失"。
5. **`_selector is null` 时 `AddItem` 静默返回。** `ViewManager.cs:131` —— 集合先到、选择器后到不会报错也不会补，只会**什么都不显示**。诊断顺序：先看 `ViewPool` 两个附着属性是不是都设了。
6. **视图池按具体类型分桶、且是即时创建。** `ViewManager.cs:136` 用 `item.GetType()` 作桶键 ⇒ 两种 ViewModel 类型即使视图相同也各建一份；`:33-49` 的 `Attach` 逐个建视图，**没有别家那种分批**（WPF 按 `DispatcherPriority.Background` 三个一批），大集合首帧会卡。依据：`:15`、`:33-49`、`:136`。
7. **删视图时同时置 `Collapsed` 与 `DataContext = null`**（`ViewManager.cs:172-173`）。=> 视图里读 `DataContext` 的代码（含 `LinkView` 的守卫）在池化回收后会看到 `null`，别把 `DataContext is not null` 当成"已初始化"。
8. **`WorkflowSurfaceBehavior.cs:401` 有一行遗留的 `System.Diagnostics.Debug.WriteLine`**（在 `ZoomBy` 里）。**不是功能代码**，别当成日志开关去扩展；顺手删是安全的。
9. **csproj 的两条独有设定会咬人**（详见 `memory/modules/TransitionSystem/adapters/jalium.md` §2.5）：单目标 `net10.0` 无平台后缀（`Src/Adapters/VeloxDev.Jalium/VeloxDev.Jalium.csproj:7`）⇒ 只能引用 `Jalium.UI.Controls` 这个平台中性包；`NoWarn` 含 `8605;8604`（`:12`）⇒ **本目录那约二十个 DP 的 CLR 包装（如 `WorkflowSurfaceBehavior.cs:79-97`）与递归下降的 `FindDescendantWithSlotDataContext`（`WorkflowSlotLayoutBehavior.cs:417-430`）现在是被压住的**。谁把这两条 NoWarn 删掉/改成 `TreatWarningsAsErrors`，这两个位置会立刻变错，而别家不会。
10. **改 `WorkflowCanvasTransformBehavior` 之前先确认你要的不是别的角色。** 这个文件在本家是**死的**（§2.4）；三条线索（`Apply` 零调用者、`OnTransformChanged` 空、`ViewPool.UpdateRenderTransforms` 零外部调用者）合起来说明**整条通道不可达**，而两处注释描述的是「有实现但没人走」的路径 —— 是 dead code，**不是注释与代码打架**。**要么整个删掉，要么先想清楚哪条通道真的需要它** —— 不要照注释补实现，也不要因为「注释说了有这个机制」就去找它为什么没生效。
11. **`WorkflowSurfaceBehavior` 的 `ApplyLayout` 注释不是行为说明**（§2.5）。读这一节代码前先看函数体。

---

## 五、非 Trimmed demo 连线三件事的落点（表面自绘，含右键菜单）

**先分清一件事：这家的非 Trimmed demo 没有连线视图。** §2.1 与 §四 里那些"连线视图自盒化"说的是 **Trimmed demo 与模板**的 `LinkView`；`Examples/Workflow/Jalium/Demo/` 下连线、端口、网格、标尺**全由 `Examples/Workflow/Jalium/Demo/Views/Workflow/NodeEditorSurface.cs` 自己画**（`:24` `NodeEditorSurface : Canvas`），没有 `LinkView`、不用 `ViewPool`、也没有 `TreeView`（`MainWindow` 直接 new 表面 + `ScrollViewer` + 缩略图/信息浮层）。⇒ **没有控件可以承担"某一条连线的悬停/焦点/右键"**，三件事只能由画它的表面代管。

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

- 十一个文件的成员列表、继承树、文件清单 —— IDE 里一按就有。
- 契约本身（七角色职责、绑定挂在哪、`Viewport` 谁写、渲染就绪门、滚轮方向、缩放提交顺序、注册位置表、`dotnet new` 模板约定）—— 在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3 与 `skills/veloxdev-create-workflow/references/new-adapter.md`。
- 这家怎么用（模板包怎么生成、demo 怎么跑、七个 GUI 页面的对照）—— `skills/veloxdev-create-workflow/references/gui/jalium.md` 与 `references/view-layer.md`。
- 其余六家的差异 —— 同目录另外六份。
