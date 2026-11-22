# 视觉后端工作流表面

> 代码：`Src/Adapters/VeloxDev.WinForms/WorkflowSystem/VisualHosting/`。
> 定位：在 `Xaml/Visuals`（单 HWND + `VisualElement`）之上复用的工作流表面；**不是**替换 `AttachedBehaviors/` 那套 HWND 池化表面，两条路并存。

## 一、组成与职责

| 类型 | 职责 | 谁写状态 |
| --- | --- | --- |
| `VisualWorkflowSurface` | 单 HWND 宿主；组合 grid → canvas → 单连线层 → 节点卡；把视觉输入翻译进 `WorkflowInput`，并处理拖节点/平移/缩放/插槽连线；实现 `IWorkflowSurfaceScroller` / `IWorkflowSurfaceNavigator` | `Tree`、`Pan`（经 `Canvas`）、`WorkflowInput.Scroller/Navigator` |
| `VisualWorkflowCanvas` | 定位节点卡、铺满连线层、arrange 后同步槽锚点与曲线 | `Pan`、节点视图集合 |
| `VisualWorkflowLinkLayer` | 画布首个子项、`IsHitTestVisible=false`、一趟画完所有线并按模型空间 `PublishCurve` | 已发布曲线、`Hovered`、flow |
| `VisualWorkflowNodeView` | 默认卡片；拖动走 `MoveCommand`；`BuildContent()` 是宿主换卡的点 | 槽视图集合、拖动状态 |
| `VisualWorkflowSlotView` / `VisualWorkflowSlotLayout` | 槽位字形与实测锚点 | 字形只画；布局写 `slot.Anchor` |
| `VisualWorkflowInputRouter` | 视觉命中 → 槽/节点/连线，再交给 Core 路由 | 无（Core 持有 pointer target） |
| `VisualWorkflowMenu` | `VisualContextMenu` 的条目容器；默认只给 link 一条 Delete | 由宿主 builder 填 |

## 二、坐标契约（改这里之前先读）

- `NodeDefaultComponent.Anchor/Size` 的 getter 已按 `Layout.Scale` 坍缩；画布**不再乘/除 scale**，只加 `Pan`。
- 模型点 = 表面点 − `Canvas.AbsoluteBounds.Location` − `Pan`；`VisualWorkflowCanvas.SurfaceToModel` 是唯一入口。
- 曲线在模型空间构建、发布与命中；只有 `OnRender` 时才加 `Pan`。**不要**用 `Visual.Transform` 做平移 —— 发布曲线和命中都要用同一个空间。
- 槽锚点在 arrange 之后由 `VisualWorkflowSlotLayout.Sync` 从字形 `slotView.Bounds` 算出，写 `slot.Anchor`；禁止 `PointToScreen`。`AnchorCache` 用来跳过没动过的写入。
- 缩放：改 `Layout.Scale` 后重算 `Pan` 让指针下的世界点不动（见 `VisualWorkflowCanvas.ZoomAt`）；节点位置与大小由下一次 arrange 的 `SyncViewGeometry` 重取。

## 三、必须知道的坑

- **`LinkHitTestEx.HitTestVisibleLinks` 只读 `VisibleItems`。** 默认 `TreeDefaultComponent` 装的是无虚拟化的 `TreeHelper()`，`VisibleItems` 永远为空，于是任何连线都命中不到。测试或宿主若要用连线命中，树 helper 必须是虚拟化版（`new TreeHelper(200)` 或 demo 的 `AgentHelper`）。
- 表面在每次 arrange / pan / zoom 后把 `Viewport(-Pan.X, -Pan.Y, width, height)` 写回 helper；这是 `VisibleItems` 跟随视口的唯一驱动，**不是** `CanvasLayout.ActualOffset`（视觉宿主不使用该偏移）。
- `VisualWorkflowSurface` 是 `sealed`。宿主换卡走 `TemplateSelector`（优先）/ `NodeViewFactory` / 重写 `VisualWorkflowNodeView.BuildContent`，换菜单走 `LinkMenuBuilder`/`NodeMenuBuilder`，换配色走 `LinkLayer`/`GridDecorator`/`Minimap` 的属性 —— 按 layer-ownership 规范，外观与策略留在 demo。
- `TemplateSelector` 取 `VisualTemplateSelector.SelectTemplate` 的结果并只接受 `VisualWorkflowNodeView`；返回别的视觉元素会落到下一级（工厂，再默认卡）。
- `VisualWorkflowSlotView` / `VisualWorkflowGridDecorator` / `VisualWorkflowMinimap` 可继承：槽位覆写 `OnRender`，网格/minimap 设调色板。`WinForms` 的对应 item template 就分别是这三种派生 + `VisualTemplateSelector` 派生。
- 指针事件在根元素（`WorkflowRoot`）汇总后转发，所以直接调 `VisualSurface.ProcessPointerDown/Move/...`（无消息循环的测试）也会走完 Core 路由；键事件要求根元素持有逻辑焦点，`FocusSurface()` 是测试与宿主的显式入口。
- Delete 的默认策略在表面里（悬停线存在且未被 `PreventDefault` 时执行 `DeleteCommand`）；demo 若自己删线必须置 `PreventDefault`，否则会删两次。
- 插槽连线：在 sender 槽按下 → `SendConnectionCommand`；移动时 `SetPointerCommand` + `StandardSetPreviewReceiver`（槽命中用 `SlotHitTestEx`）；在接收槽上松手 → `ReceiveConnectionCommand`，否则 `ResetVirtualLinkCommand`；Escape 取消。模板 slot 只画字形，不再挂连接行为。

## 四、扩展点

| 我想改 | 改哪 |
| --- | --- |
| 卡片外观 | `TemplateSelector` 返回派生 `VisualWorkflowNodeView`；或 `NodeViewFactory`；或覆写 `BuildContent` |
| 槽位字形 | 派生 `VisualWorkflowSlotView` 覆写 `OnRender`；基础卡的槽位仍由基类生成 |
| 连线颜色/粗细/流光 | `VisualWorkflowLinkLayer.LineColor` / `HoverLineColor` / `LineWidth` / `HoverLineWidth` / `Glow` / `SetFlow` |
| 网格 | 派生 `VisualWorkflowGridDecorator` 或改 `GridSpacing` / `MajorLineEvery` / 三种线色 |
| 小地图 | 派生 `VisualWorkflowMinimap` 或改 `NodeBrush` / `ViewportStroke` |
| 右键菜单 | 宿主给 `LinkMenuBuilder` / `NodeMenuBuilder`；默认 link 菜单只有 Delete |
| 新控件上的工作流表面 | 宿主 XAML 直接放 `VisualWorkflowSurface`（`assembly=VeloxDev.WinForms`），不要另写一个派生壳去复制组合 |
