# 视觉后端工作流表面

> 代码：`Src/Adapters/VeloxDev.WinForms/WorkflowSystem/VisualHosting/`。
> 定位：在 `Xaml/Visuals`（单 HWND + `VisualElement`）之上复用的工作流表面；**不是**替换 `AttachedBehaviors/` 那套 HWND 池化表面，两条路并存。

## 一、组成与职责

| 类型 | 职责 | 谁写状态 |
| --- | --- | --- |
| `VisualWorkflowSurface` | 单 HWND 宿主；组合 grid → canvas → 刻度带 → 小地图，节点卡与连线层在 canvas 里；把视觉输入翻译进 `WorkflowInput`，并处理拖节点/平移/缩放/插槽连线；实现 `IWorkflowSurfaceScroller` / `IWorkflowSurfaceNavigator` | `Tree`、`Pan`（经 `Canvas`）、`WorkflowInput.Scroller/Navigator` |
| `VisualWorkflowCanvas` | 定位节点卡、铺满连线层、arrange 后同步槽锚点与曲线 | `Pan`、节点视图集合 |
| `VisualWorkflowLinkLayer` | 画布首个子项、`IsHitTestVisible=false`、一趟画完所有线并按模型空间 `PublishCurve` | 已发布曲线、`Hovered`、flow |
| `VisualWorkflowNodeBehavior` | 给**任意** `VisualElement` 卡片挂节点：`DataContext`、拖动（视图空间 delta）、槽视图创建/排布/锚点；实现 `IVisualWorkflowNodeContent` 的卡片另拿一个缩放的正文宿主 | 节点引用、槽视图集合、拖动状态 |
| `VisualWorkflowSlotBehavior` | 给**任意** `VisualElement` 字形挂一个 slot，并在 State/Channel/Anchor 变化时让它重画 | 只存 slot 引用 |
| `DefaultVisualWorkflowNodeView` / `DefaultVisualWorkflowSlotView` | 无选择器/无 SlotTemplate 时的内部兜底卡片与字形；**不是**模板基类 | 只画 |
| `VisualWorkflowRulerOverlay` | 上/左悬浮半透明刻度带（默认 28），压在所有卡片之上、小地图之下；不取命中；字号按带宽自动取；左带标签保持**横排**，大值先缩成 K/M，必要时再降半档字号，不加宽带 | `Pan`（与网格同步） |
| `VisualWorkflowInputRouter` | 视觉命中 → 槽/节点/连线，再交给 Core 路由 | 无（Core 持有 pointer target） |
| `VisualWorkflowMenu` | `VisualContextMenu` 的条目容器；默认只给 link 一条 Delete | 由宿主 builder 填 |

## 二、坐标契约（改这里之前先读）

- `NodeDefaultComponent.Anchor/Size` 的 getter 已按 `Layout.Scale` 坍缩；画布**不再乘/除 scale**，只加 `Pan`。
- 模型点 = 表面点 − `Canvas.AbsoluteBounds.Location` − `Pan`；`VisualWorkflowCanvas.SurfaceToModel` 是唯一入口。
- 曲线在模型空间构建、发布与命中；只有 `OnRender` 时才加 `Pan`。**不要**用 `Visual.Transform` 做平移 —— 发布曲线和命中都要用同一个空间。
- 拖动 delta 是**视图空间**（与 `node.Anchor` 同系）；`INodeComponent.MoveCommand`/`StandardMove` 自己按 `Layout.Scale` 展开，宿主**不要再乘 scale**，否则偏移是 `delta × scale²`。
- 槽锚点在 arrange 之后由 `VisualWorkflowNodeBehavior.SyncAnchors` 从字形 `slotView.Bounds` 算出，写 `slot.Anchor`；禁止 `PointToScreen`。`AnchorCache` 用来跳过没动过的写入。
- 缩放：改 `Layout.Scale` 后重算 `Pan` 让指针下的世界点不动（见 `VisualWorkflowCanvas.ZoomAt`）；节点位置与大小由下一次 arrange 的 `SyncViewGeometry` 重取。
- `Visual.Transform` 现在是**局部**变换（`Visual.Render` 用 `MatrixOrder.Prepend`）：`Viewbox`、代码卡片的 `ScaledContentHost` 都靠它按设计尺寸缩放，不会再缩放祖先的 `Bounds` 平移。
- 槽位按设计行排（标题下 72、行高 22、输入内缩 2 / 输出内缩 12），位置与字形都乘当前比例；字形最小 10px，避免缩得太小点不到。
- 网格、刻度带、小地图三者共用 `Canvas.Pan`；`VisualWorkflowSurface.SyncSurfacePan` 是唯一同步点。
- 视觉后端公开坐标是**逻辑 DIP**（96 DPI）：`VisualSurface` 自己把 `Graphics` 乘 `DeviceDpi / 96`，并把指针、原生、弹出与 damage 坐标换算到同一空间；宿主读 `DpiScale` / `LogicalClientSize`。宿主工程设 `ApplicationHighDpiMode=PerMonitorV2`（两个 WinForms workflow demo 同款；XamlGallery 是纯 XAML demo，仍用 `DpiUnawareGdiScaled`）。原生覆盖层（`InfoOverlay` 这类 Panel）的固定像素指标自己乘 `DeviceDpi / 96`，并在 `OnDpiChangedAfterParent` 重算；点制字体不手动缩放。原生外壳两家不同：trimmed demo 的窗体 `AutoScaleMode=None`、自己按 `DeviceDpi` 定客户区；全量 demo 交给 `AutoScaleMode.Font` 自动缩放。

## 三、必须知道的坑

- **`LinkHitTestEx.HitTestVisibleLinks` 只读 `VisibleItems`。** 默认 `TreeDefaultComponent` 装的是无虚拟化的 `TreeHelper()`，`VisibleItems` 永远为空，于是任何连线都命中不到。测试或宿主若要用连线命中，树 helper 必须是虚拟化版（`new TreeHelper(200)` 或 demo 的 `AgentHelper`）。
- 表面在每次 arrange / pan / zoom 后把 `Viewport(-Pan.X, -Pan.Y, width, height)` 写回 helper；这是 `VisibleItems` 跟随视口的唯一驱动，**不是** `CanvasLayout.ActualOffset`（视觉宿主不使用该偏移）。
- `VisualWorkflowSurface` 是 `sealed`。宿主换卡走 `TemplateSelector`（优先）/ `NodeViewFactory`，换菜单走 `LinkMenuBuilder`/`NodeMenuBuilder`，换配色走 `LinkLayer`/`GridDecorator`/`RulerOverlay`/`Minimap` 的属性 —— 按 layer-ownership 规范，外观与策略留在 demo。
- `TemplateSelector` / `NodeViewFactory` 接受**任意** `VisualElement`；表面给它挂 `VisualWorkflowNodeBehavior`。卡片想用代码而不是标记构建正文时实现 `IVisualWorkflowNodeContent`。
- 槽/节点/grid/minimap **都没有** WorkflowSystem 专用基类：模板 slot 是普通视觉控件 + `VisualWorkflowSlotBehavior.GetSlot(this)`；grid 是普通视觉 + `IWorkflowGridDecorator` 在 `OnRender` 自绘；minimap 是普通视觉组合 sealed 默认件。`VisualWorkflowGridDecorator` / `VisualWorkflowMinimap` 只是 sealed 角色实现。
- DPI 测试接缝：`VisualSurface.DpiScaleOverride` 固定缩放（0 恢复自动）；`RenderTo` 永远按逻辑坐标、scale 1 渲染，不读 `DeviceDpi`。点制字体在 `Visual.ResolveDrawFont` 里按 `96 / graphics.DpiY` 重导出，像素字体原样；`RenderDpi` 恒为 96（测量空间）。
- 指针事件在根元素（`WorkflowRoot`）汇总后转发，所以直接调 `VisualSurface.ProcessPointerDown/Move/...`（无消息循环的测试）也会走完 Core 路由；键事件要求根元素持有逻辑焦点，`FocusSurface()` 是测试与宿主的显式入口。
- Delete 的默认策略在表面里（悬停线存在且未被 `PreventDefault` 时执行 `DeleteCommand`）；demo 若自己删线必须置 `PreventDefault`，否则会删两次。
- 插槽连线：在 sender 槽按下 → `SendConnectionCommand`；移动时 `SetPointerCommand` + `StandardSetPreviewReceiver`（槽命中用 `SlotHitTestEx`）；在接收槽上松手 → `ReceiveConnectionCommand`，否则 `ResetVirtualLinkCommand`；Escape 取消。模板 slot 只画字形，不再挂连接行为。
- **小地图按哪儿就把视口中心移到哪儿**（与其余六家一致），不是只做相对拖拽：按下与每次移动都经 `IWorkflowSurfaceNavigator.CenterOn`；`IWorkflowSurfaceScroller` 只在 navigator 不可用时兜底。
- 槽视图由 `TemplateSelector` 的 `SlotTemplate` 创建，再挂 `VisualWorkflowSlotBehavior`；宿主不给时才退回内部默认字形。`node.Slots` 里的条件分支槽（`MultipleBoth`）也会各得一个字形，所以卡片右缘会出现多个槽。
- DPI 剩余的软点：`CacheAsBitmap` / 组 `Opacity` / `Effect` 子树先按逻辑尺寸落到位图再被世界变换放大，文字大小正确但可能略软；`NativeControlHost.MeasureOverride` 拿原生首选尺寸（设备像素），未给显式逻辑尺寸时可能量得过大；`LogicalClientSize` 取整会在右/下留不到一个物理像素的缝。

## 四、扩展点

| 我想改 | 改哪 |
| --- | --- |
| 卡片外观 | `TemplateSelector` / `NodeViewFactory` 返回普通视觉控件；代码构建正文时实现 `IVisualWorkflowNodeContent` |
| 槽位字形 | `TemplateSelector.SlotTemplate` 返回普通视觉控件，`OnRender` 里用 `VisualWorkflowSlotBehavior.GetSlot(this)` 取状态 |
| 连线颜色/粗细/流光 | `VisualWorkflowLinkLayer.LineColor` / `HoverLineColor` / `LineWidth` / `HoverLineWidth` / `Glow` / `SetFlow` |
| 网格 | 派生 `VisualWorkflowGridDecorator` 或改 `GridSpacing` / `MajorLineEvery` / 三种线色 |
| 刻度带 | `RulerOverlay` 的 `RulerThickness` / `GridSpacing` / 五种颜色 / `LabelFont` |
| 小地图 | 派生 `VisualWorkflowMinimap` 或改 `NodeBrush` / `ViewportStroke` |
| 槽位模板 | `TemplateSelector.SlotTemplate = new VisualDataTemplate(() => new MySlotView())` |
| 右键菜单 | 宿主给 `LinkMenuBuilder` / `NodeMenuBuilder`；默认 link 菜单只有 Delete |
| 新控件上的工作流表面 | 宿主 XAML 直接放 `VisualWorkflowSurface`（`assembly=VeloxDev.WinForms`），不要另写一个派生壳去复制组合 |
