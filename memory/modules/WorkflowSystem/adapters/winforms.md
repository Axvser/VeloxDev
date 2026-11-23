# WorkflowSystem — WinForms

> 连线交互规则见 [WorkflowSystem/architecture.md §3.6](../architecture.md)：输入是标准输入
> （`WorkflowInput.For(tree).Route(...)` + `IInputEvents`），指针底下是谁由适配器解析、认不到才回退 Core 的共享曲线判定。
>
> **读法**：契约在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；人面向的搭建指南在
> `skills/veloxdev-create-workflow/references/practices/gui/winforms.md`；XAML 运行时在 `memory/modules/WinFormsXaml/architecture.md`。
> **路径写法**：下文裸文件名都相对 `Src/Adapters/VeloxDev.WinForms/WorkflowSystem/AttachedBehaviors/`。

---

## 一、这家写什么

**工作流表面整棵是视觉树**：宿主是 `VisualHost`，`Root` 是视觉 `UserControl`；`WorkflowSurfaceBehavior` 挂在 `VisualHost` 上，相机写在 `PART_Canvas.Transform`，`ViewPool` 按可见集物化节点/插槽/连线。网格装饰器与小地图是**模板角色**（适配器只留 `IWorkflowGridDecorator` / `IWorkflowMinimapOverlay` 契约与小地图基类）。

| 角色 | 这家的实现 | 形状 |
|---|---|---|
| 画布宿主 | `WorkflowSurfaceBehavior`（`RegisterAttached` + `ConditionalWeakTable<Control, SurfaceState>`） | `VisualHost` 上按 `VisualElement.Name` 解析 `PART_*`；相机矩阵、Core 输入路由、布局、视口持久化、小地图接线 |
| 视觉画布 | `ViewPool`（同目录） | `PART_Canvas` 上按 `Helper.VisibleItems` 建视觉子元素；节点/插槽/连线由 `DataTemplate` 定位与绘制 |
| 小地图 / HUD | 模板派生 `WorkflowMinimapOverlay`（`VisualElement`）/ 直接用 `WorkflowInfoOverlay` | 与 `PART_SurfaceBorder` 同级进 tree 根 `Grid`；表面推 scroll/content/viewport，拖小地图经 `IWorkflowMinimapScrollSource` 反推 |
| 网格装饰器 | 模板条目实现 `IWorkflowGridDecorator` | 视觉 `VisualElement`；标尺带 `RulerBand` 同时喂虚拟化 inset 与内容偏移 |
| item → 视图 | `VisualTemplateSelector` | 返回 `NodeView`/`SlotView`/`LinkView` 视觉元素 |
| 槽/拖动/连接 | `WorkflowSlotLayoutBehavior` / `WorkflowNodeDragBehavior` / `WorkflowSlotConnectionBehavior` | `VisualElement` 附着行为，由 `WorkflowSurfaceBehavior` 在每个视觉元素上 `Ensure`；自己测量槽、拖动节点、完成连接 |

**部件按名字交进来，不是按对象**：`FindVisualNamed` 递归视觉子树、`Ordinal` 比较 `VisualElement.Name`，取第一个命中；找不到静默跳过，不抛。

## 二、平台硬限制，与由此产生的做法

### 2.1 接线靠附着属性、视觉部件名与 `ConditionalWeakTable`

- `WorkflowSurfaceBehavior` 的开关是 XAML 附着属性：`IsEnabled` / `ZoomEnabled` / `VisualTemplateSelector` / `WorkflowTree` / `LinkMenuKey`（`LinkMenuBuilder` 为旧回落） / 五个 `*Name` / 视觉连线颜色与宽度；状态放 `ConditionalWeakTable<Control, SurfaceState>`。**CWT 不可枚举**。
- `WorkflowSurfaceBehavior` 的成员以 `VisualHost` 为参数：`SetWorkflowTree`、`GetZoom`/`GetPan`/`SetPan`、`SurfaceToWorld`/`WorldToSurface`、`GetWorldViewport`/`SetWorldViewport`、`ZoomAt`、`ProcessWheel`、`Refresh`。
- tree 模板把选择器声明成资源 `x:Key="WorkflowTemplateSelector"`；`WorkflowSurfaceBehavior.VisualTemplateSelectorKey` 默认就是这个键，先从 tree 根的 `VisualElement.Resources` 找，再找 `VisualHost.Resources`，显式 `VisualTemplateSelector` 优先。宿主不再 `new` 选择器。
- **`x:Name` 同时写字段与 `VisualElement.Name`**；模板只写 `x:Name="PART_*"` 一次即可。
- 节点模板用 `WorkflowSlotLayoutBehavior` 声明 `SlotNames` / `SlotEnumeratorNames`（逗号分隔的视觉元素名）与 `CoordinateHostName`；标题行用 `WorkflowNodeDragBehavior`；插槽根用 `WorkflowSlotConnectionBehavior`。
- 行为挂在哪个元素上就只对那个元素（及后代）生效；`IsEnabled` 默认 `false`，没挂或没写就不测量、不拖动、不连接（没有整卡/全部插槽的回退）。`WorkflowSurfaceBehavior` 在 `EnsureAttachedBehaviors` 里遍历视觉树对每个元素调 `Ensure`。

### 2.2 指针走视觉根冒泡；按下在 `PreviewPointerPressed` 相路由，滚轮从 `VisualHost` 进入

- `WorkflowSurfaceBehavior` 在 `VisualHost.Root` 上订阅 `PreviewPointerPressed` 与 `PointerPressed/Moved/Released/Wheel/Exited`、`KeyDown/KeyUp`（由 `VisualHost` 冒泡）。**按下在预览相路由**（`OnPreviewPressed` → `RoutePressed`），句柄先落在表面里，节点拖动/插槽连接行为在自己的 `PointerPressed` 里读 `GetPressHandle`；命中后把屏幕点经相机逆矩阵换成世界坐标，再 `WorkflowInput.Route`。
- **滚轮**：`VisualHost.OnMouseWheel` → `ProcessPointerWheel` → 根视觉元素的 `PointerWheel` → `WorkflowSurfaceBehavior.HandleWheel`。`HandleWheel` 先路由 Core 并读 `PreventDefault`，再按 Ctrl 缩放（`ZoomAt`）、Shift 横滚、默认竖滚。**画布上不挂 `MouseWheel`**。
- ⚠ 实测：`WM_MOUSEWHEEL` 在 `HKCU\Control Panel\Desktop\MouseWheelRouting = 2`（「悬停时滚动非活动窗口」开着）下按指针派发；关掉时是否退化为按焦点派发未验证。

### 2.3 相机：一条矩阵，世界坐标，输入走逆矩阵

- `ApplyCamera` 把相机矩阵写到 `PART_Canvas.Transform = [zoom, 0, 0, zoom, pan+content]`。节点/插槽/连线元素按世界坐标摆放，文字与字形随同一矩阵缩放。
- **`EnsureTree` 把非 1 的 `Layout.Scale` 复位到 1**，`Anchor`/`Size` 才是世界坐标；缩放只由相机承担，没有逐卡 `Collapse`（节点模板的 `Viewbox` 只把固定设计栅格装进卡片框，不承担 zoom）。
- 输入：屏幕点经逆矩阵换成世界坐标；拖拽增量除以 zoom。`PART_Canvas` 设 `UnboundedHitTest=true`，视觉裁剪交给 `ScrollViewer`。
- 滚轮上滚 `zoom *= 1.1`、下滚 `/1.1`，夹在 `0.1..10`，指针下的世界点不动。

### 2.4 平移、滚动与视口持久化

- `SetPan` / 指针处理改 `state.Pan`；`ApplyCamera` 把 content offset、相机、网格/小地图偏移一次推完。滚动条按**屏幕像素**走：`scroll = -pan`，不减 `ActualOffset`。
- 小地图：`ScrollOffset = -(pan+content)/zoom`、`Viewport = client/zoom`。
- 视口持久化：视觉路径保存**世界坐标左上角**（`GetWorldViewport`），与当时 zoom 无关；`DetachTree` 把它写回 `Layout.ViewportOffset`，`EnsureTree` 从它恢复 pan。
- 网格/标尺仍随相机缩放。

### 2.5 Win32 样式与 HWND 岛

- `WorkflowSurfaceBehavior.Attach` 对 `VisualHost` 调 `NativeWindowStyleHelper.EnsureClipChildren`/`EnsureComposited`。`WS_CLIPCHILDREN` 与 `WS_EX_COMPOSITED` 同值 `0x02000000`，写向不同 index；顶层子窗口超过 `CompositedMaxControlCount = 100` 就不合成。
- `NativeControlHost` 是唯一原生岛；视觉 `TextBox` 聚焦时用私有岛做 IME/无障碍。子 HWND 永远浮在视觉树之上，不能变换/裁剪/混色。
- **小地图/HUD 是视觉树里的同级部件**；它们共享单个 `VisualHost` 和相机，表面推偏移。

### 2.6 连线：选择器给每条 link 一个 LinkView

- `ViewPool` 通过 tree 的 `LinkTemplate` 为每条 link（含 virtual link）实例化一个视图；`DataTemplate` 把 `StartLeft/StartTop/EndLeft/EndTop` 绑到 `Sender.Anchor.*` / `Receiver.Anchor.*`，把 `CanRender` 绑到 `IsVisible`；表面 `ApplyLinkStyles` 再写 `LineColor`/`HighlightColor`/`IsVirtual`。视图 `OnRender` 画 cubic 并 `PublishCurve`。
- **曲线是世界坐标**；相机变换只在绘制时加。`Start/End` 与发布给 Core 命中测试的是同一条曲线。
- 命中在世界坐标里按半径判：半径取 `WorkflowInput.HitRadius`（demo 设 `LinkHitRadius = 6f`）。节点卡是不透明的，压在下面的那段线不画、也不应答，端口附近除外。
- 右侧菜单：条目来自 tree 资源的 `VisualContextMenu`（`SetLinkMenuKey`），适配器从自己的 `PointerPressed(Right, link)` 打开；`SetLinkMenuBuilder` 只在没有视觉菜单键时回落为 `ContextMenuStrip`。**不挂 `Control.ContextMenuStrip`**。

### 2.7 插槽锚点写在坐标宿主的局部坐标里

`WorkflowSlotLayoutBehavior` 在布局后遍历 `SlotNames` / `SlotEnumeratorNames`，把命名部件的中心经 `PointToSurface`/`PointFromSurface` 换算到坐标宿主（`CoordinateHostName`，默认最近的视觉 `Canvas`，再退到树根）后写 `slot.Anchor`。它不使用 Core 的 `SlotAnchorFrom*` 函数。

## 三、与其它六家的刻意背离

| # | 这里的做法和其他家不一样，因为… | 依据 |
|---|---|---|
| 1 | **启用表面顺手改 Win32 窗口样式（七家里唯一）**：`Attach` 给 `VisualHost` 上 `WS_CLIPCHILDREN`、给顶层窗体上 `WS_EX_COMPOSITED` | `WorkflowSurfaceBehavior.Attach` 里 `EnsureClipChildren` / `EnsureComposited` |
| 2 | **滚轮从 `VisualHost.OnMouseWheel` 进入视觉 `PointerWheel`**：先路由 Core 再按 Ctrl 缩放 / Shift 横滚 / 默认竖滚 | `VisualHost.OnMouseWheel` → `WorkflowSurfaceBehavior.HandleWheel` |
| 3 | **小地图不订阅节点**：只收表面推来的偏移；`ComputeLayout` 在绘制时读 `WorkflowTree.Nodes`，表面 `ApplyCamera` 负责 `Invalidate` | `WorkflowMinimapOverlay.ComputeLayout`、`WorkflowSurfaceBehavior.ApplyCamera` |
| 4 | **Ctrl 判定是位测试**：`(modifiers & Keys.Control) != 0`，Ctrl+Shift+滚轮也缩放；单独 Shift 时横滚 | `HandleWheel` |
| 5 | **视觉相机是实际缩放**：上滚 `zoom *= 1.1`，与用 `Scale` 坍缩的六家手指方向一致 | `HandleWheel` / `ZoomAt` |

## 四、坑

### 4.1 部件名找不到静默跳过

`FindVisualNamed` 不抛不报；`PART_Canvas` 名字写错时表面启动但一个节点都没有。

### 4.2 非 `VisualHost` 宿主没有可渲染内容

`Attach` 里 `if (host is not VisualHost) return;`。把行为挂到普通 `Control` 上不报错，界面空着。

### 4.3 视觉子树文字必须走 `VisualText.Draw`

`TextRenderer` 直接写 HDC、不认 world transform；嵌套平移后文字画到表面原点。

### 4.4 模型 `Layout.Scale` 被强制复位到 1

`EnsureTree` 把非 1 的 `layout.Scale` 复位到 1。外部再写 `Layout.Scale` 会被覆盖，缩放应从相机走。

### 4.5 小地图脱离表面会冻住

它不自带节点订阅；单独用这份控件时节点移动不重画。

### 4.6 视图几何在 `VisualHost` 的 DPI 下

`VisualHost` 按 `DeviceDpi/96` 物理渲染；相机、模型、装饰偏移是逻辑 DIP。物理客户区/标尺/小地图/滚动条宽经 `DpiScale` 换算。`VisualHost.LogicalClientSize` 是除回后的逻辑尺寸。

### 4.7 连线被卡片/浮层 HWND 盖住的那段不可悬停

指针落在小地图/HUD 的真窗口上时视觉树收不到 `PointerMoved`；只有画在空白区域的那段可命中。

## 五、外部参照

- 坐标与缩放数学：`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Math/WorkflowSurfaceMath.cs`（七家共用）。
- 视觉运行时：`memory/modules/WinFormsXaml/architecture.md`。
- WPF 对照：`memory/modules/WorkflowSystem/adapters/wpf.md`。
