# WorkflowSystem — WinForms

> 连线交互规则见 [WorkflowSystem/architecture.md §3.6](../architecture.md)：输入是标准输入
> （`WorkflowInput.For(tree).Route(...)` + `IInputEvents`），指针底下是谁（节点 / 插槽 / 连线）由适配器解析、认不到才回退 Core 的共享曲线判定，外观、删除与菜单的接线归宿主/适配器。
>
> **读法**：契约（七个视图角色、注册位置、联动清单）在 `memory/modules/WorkflowSystem/extension.md` §3.9 与 §4.3；
> 人面向的「怎么搭一个 WinForms 工作流视图」在 `skills/veloxdev-create-workflow/references/gui/winforms.md`，
> 逐角色职责表在 `skills/veloxdev-create-workflow/references/view-layer.md`（`new-adapter.md` 同源）。
> **路径写法**：下文的裸文件名（`WorkflowSurfaceBehavior.cs` 等）都相对 `Src/Adapters/VeloxDev.WinForms/WorkflowSystem/AttachedBehaviors/`；引用 Core、别家、模板、demo 时一律写全路径。

---

## 一、这家要写什么，为什么是这些

七个视图角色里，宿主、池与三个交互行为落在适配器，网格装饰器与小地图是**模板角色**（适配器只留 `IWorkflowGridDecorator` / `IWorkflowMinimapOverlay` 契约与小地图基类），外加一个**契约外的平台补偿文件**：

| 角色 | 这家的实现 | 这家特有的形状 |
|---|---|---|
| 画布宿主 | `WorkflowSurfaceBehavior`（XAML `DependencyProperty.RegisterAttached` + `ConditionalWeakTable<Control, SurfaceState>`，`WorkflowSurfaceBehavior.cs`）。宿主是普通 `v:UserControl`，部件用 `ScrollViewerName` / `CanvasName` / `GridDecoratorName` / `MinimapOverlayName` / `PointerPressSourceName` 指定 | 同时是**滚轮、缩放、指针与空白平移的宿主**（`SurfaceState : IMessageFilter`）；`Attach` 时挂应用级过滤器（`EnsureMessageFilter`，`filterAdded` 守卫），看得见每一笔 `WM_MOUSEWHEEL` / `WM_MOUSEHWHEEL` |
| 画布变换 | `WorkflowCanvasTransformBehavior`（`static class`，CWT 存一个 `Offset`，`WorkflowCanvasTransformBehavior.cs`） | 只是一个**值载体**，`ApplyPanCore` 末尾 `Apply(host, content)` 写入；仓库里没有读者（§4.2） |
| 视图池 | `ViewPool`（挂点）+ `ViewHosting/ViewSourcePipeline`（Core `ViewSourcePipelineCore` 在这家的闭合），层实现是 `ViewHosting/` 下的 `ViewFactory` / `ViewContainer` / `ViewArranger` / `ViewScheduler` / `ViewDiagnostics` / `ViewLayerPolicy` / `ViewLinkLayer` / `ViewMeasureHost`；节点走 `ViewFactory` 包住的通用 `VeloxDev.WinForms.Xaml.TemplateSelector` | 连线**不物化**：`ViewLinkLayer.IsActive => true` 把连线项挡在池外，`WorkflowLinkLayer` 在 `PART_Canvas` 的 `Paint` 里一趟画完；首次换源时 `BeginInitialReveal` 冻结表面到池排空（2 tick，2000ms 兜底）；调度走 `Control.BeginInvoke` 异步分批，`TPriorityCore` 是 `NonPriority` |
| 节点拖拽 | `WorkflowNodeDragBehavior`（`WorkflowNodeDragBehavior.cs`） | 递归挂**整棵控件树**的 `MouseDown`（`HookControlTree`），不是一次命中测试 |
| 插槽连接 | `WorkflowSlotConnectionBehavior`（`WorkflowSlotConnectionBehavior.cs`） | 应用级 `IMessageFilter` + `WindowFromPoint`（静态 `_messageFilter` / `_activeConnection`、`FindSurfaceControl`、`ConnectionMessageFilter`） |
| 插槽布局 | `WorkflowSlotLayoutBehavior`（`WorkflowSlotLayoutBehavior.cs`） | 七家里唯一额外提供**同步重测**入口 `SyncNow` 的 |
| 网格装饰器 / 小地图 | **模板条目**：`Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-grid-decorator/TemplateClass.cs` 与 `workflow-minimap-overlay/TemplateClass.cs`；两个 demo 各有 `GridDecorator.cs` / `MinimapOverlay.cs`。小地图基类是适配器的 `WorkflowMinimapOverlay`（`Panel + IWorkflowMinimapOverlay + IWorkflowMinimapScrollSource`） | 两个部件都由表面按名字接线；标尺带不由装饰器画 —— 适配器的 `WorkflowRulerOverlayForm` 按 `IWorkflowGridDecorator.RulerBand` 建顶层分层窗口 |
| （契约外）平台补偿 | `NativeWindowStyleHelper`（`internal static`） | 只有这家有 —— Win32 窗口样式是这家的渲染前提，见 §2.6 |

**部件按名字交进来，不是按对象**：`EnsureParts` 用 `FindNamed` 找 `Control.Name`，`state.GridDecorator is IWorkflowGridDecorator` / `state.MinimapOverlay is IWorkflowMinimapOverlay` 时才在 `ApplyPanCore` 里写 `ScrollOffset*` / `ContentOffset*` / `Viewport*`。名字找不到就不推偏移，也不抛。小地图拖动经 `IWorkflowMinimapScrollSource.ViewportScrollRequested` 回到 `SurfaceState.OnMinimapScrollRequested` → `SetScrollOffset`。

⚠ **`WorkflowMinimapOverlay` 自己不订阅节点**（与其余家的同名控件相反），两个后果：

- **内容不能缓存**：没有「节点动过」的信号，缓存会按旧位置反解点击。所以它每次绘制走一趟把包围盒与缩略框一起收齐（`ComputeLayout(List<(double,double,double,double)>? thumbnails = null)`，绘制传 `_nodeRects`），拖拽移动时每帧重算。
- **重画由表面补**：`WorkflowSurfaceBehavior.HookView` 给每个 `WorkflowNodeAttachment` 订阅 `AnchorChanged → SurfaceState.OnNodeAnchorChanged`，那里 `minimap.Invalidate()`；这个订阅在视图第一次 `ControlAdded` 或 `ApplyPanCore` 循环时建立。⇒ 脱离表面单独用这份控件时，节点移动不会重画它；**要加缓存必须自己补上订阅**，否则缩略图会冻住。位置由 `PositionAtTopRight` 在父控件 resize 时钉到右上角（`CornerMargin = 12`，默认 200×140）。

---

## 二、平台硬限制，与由此产生的做法

### 2.1 接线靠附着属性、部件名与 `ConditionalWeakTable`

- **表面行为把开关注册成 XAML 附着属性**：`IsEnabled` / `ZoomEnabled` / `TemplateSelector` / `WorkflowTree` / `LinkMenuBuilder` / 五个 `*Name` / `NodeViewType` / `LinkViewType`；状态放 `ConditionalWeakTable<Control, SurfaceState>`。⇒ **CWT 不可枚举**，需要「遍历所有已挂载控件」时得另存一个普通集合。
- **其余行为的接线面有两种**：`WorkflowNodeDragBehavior` / `WorkflowSlotConnectionBehavior` / `WorkflowSlotLayoutBehavior` 是静态 `Get/Set` + CWT；`WorkflowNodeRowsBehavior` 与 `WorkflowLinkLayer` 注册附着属性。状态都在各自类型的 CWT 里。
- **部件解析是 `FindNamed`**：递归 `Controls`、`Ordinal` 比较 `Control.Name`，取第一个命中。`x:Name` 只生成字段，不写原生 `Name`；`Name="PART_*"` 才是契约。找不到静默跳过。
- **视图池的 item → view**：`ViewPool.SetTemplateSelector`，选择器是通用 `VeloxDev.WinForms.Xaml.TemplateSelector`（`SelectTemplate(object?)` → `DataTemplate?`）。未给选择器时 `NodeViewType` / `LinkViewType` 由 `SurfaceTemplateSelector` 按程序集 + 命名空间反射构造，找不到抛 `InvalidOperationException`。
- **绑定**：`ViewContainer.ApplyBinding` 先看附件（`WorkflowNodeAttachment` / `WorkflowSlotAttachment` / `WorkflowLinkAttachment`），其余角色 `Tag = item`，再对 `"ViewModel"` / `"DataContext"` / `"BindingContext"` 三个属性名反射 `SetValue`（`ViewHosting/ViewContainer.cs` 的 `ApplyBinding`）。⇒ 视图想拿到 VM，要么读 `Tag`，要么有那三个名字之一的属性；其它名字静默拿不到。
- **没有 `Panel.ZIndex`，层级按角色在挂树时定**：`ViewContainer.Attach` 第一次物化时 `Controls.Add`，随后节点 `BringToFront`、连线 `SendToBack`（`Controls` 序号 0 在最前，`Add` 追加到末尾 = 最后面）。池是异步物化的，所以层级必须在挂树这一趟当场定下来；`SetZOrder` 是空操作。别家靠 `Panel.ZIndex`，这家没有对应物。

### 2.2 没有路由/隧道事件 ⇒ `Application.AddMessageFilter`，而且是**进程级单例**

- 滚轮：应用级过滤器由 `Attach` 挂（`EnsureMessageFilter`，`filterAdded` 守卫），**看得见每一笔 `WM_MOUSEWHEEL`**。原生滚轮消息发给焦点或（按系统设置）指针下的窗口，挂在元素上的处理器收不到被卡片盖住的那部分，所以路由只能放在消息泵这一层。`SetZoomEnabled` 只记状态。两枝都在 `PreFilterMessage`：非 Ctrl（或关掉了缩放）→ `RouteWheel` 汇报，没人否决就 `state.Scroller.ScrollBy` 默认滚再吞消息；Ctrl 且 `ZoomEnabled` → 缩放支，可被否决并吞消息。
  ⚠ 实测：三格滚轮 = 三行汇报。
  ⚠ **按指针派发**：用 `WindowFromPoint(Cursor.Position)` 与 `m.HWnd` 逐笔对照，**4 次采样全部 `mHWND == 指针底下的窗口`，且恒 ≠ 焦点窗口**。
  ⚠ **限定条件**：本机 `HKCU\Control Panel\Desktop\MouseWheelRouting = 2`（「悬停时滚动非活动窗口」开着），**关掉该设置时是否退化为按焦点派发未验证**。
- 插槽连接：`_messageFilter` 与 `_activeConnection` 都是**静态字段**（`WorkflowSlotConnectionBehavior.cs` 的 `_messageFilter` / `_activeConnection`），即**整个进程同时只有一条在拖的连线**；`EnsureMessageFilter` 惰性挂、`DetachMessageFilter` 摘下。过滤器监听 `WM_MOUSEMOVE` / `WM_LBUTTONUP`，松手时用 `NativeMethods.WindowFromPoint`（`GetControlAtScreenPoint` / `TryCompleteFromMouseUp`）反查目标控件。
- ⇒ `IMessageFilter` 是应用级的：**(a)** 多个工作流表面同时开着时，靠 `ResolveSurfaceHost` 认领这一笔归谁，而不是靠事件源 —— 先按**光标底下**的控件（`GetControlAtScreenPoint` + `WindowFromPoint` P/Invoke），认不到才退回消息目标 `m.HWnd`（`PreFilterMessage`）；判据是**已挂载**的表面（`state.Attached`，不看缩放开关），因为关掉缩放的宿主也要收到滚轮汇报；**(b)** 任何全局过滤器链上的异常都会影响整个应用的消息泵。

### 2.3 子控件永远画在父控件的 `OnPaintBackground` 之上 ⇒ **透明分层在这家不可用**

- `WorkflowRulerOverlayForm.cs` 的类注释写明：WinForms 子控件总是画在父控件之上，画在视口里的半透明带压不住从下面滚过的不透明卡片，**唯一能压住的是 `WS_EX_LAYERED` 的顶层弹窗**。实现是 `UpdateLayeredWindow` 逐像素 alpha（还带 `WM_NCHITTEST → HTTRANSPARENT` 让下面的平移与拖拽继续可用）。浮层由 `WorkflowSurfaceBehavior.EnsureRulerOverlay` 在 `RulerBand > 0` 且宿主句柄已建时创建，`SyncRulerOverlay` 每次 `ApplyPanCore` 推偏移。
- 节点卡片照这个结论写：整卡用**不透明**填充（`WorkflowNodeAttachment` 构造函数里 `Color.FromArgb(255, surfaceBackdrop)`，注释说明 .NET 10 给没有 `SupportsTransparentBackColor` 的控件设半透明 `BackColor` 会抛）。
- 插槽视图走通用形态：`v:UserControl` 里一个 `v:Path`（`Stretch="Uniform"` 把 1024 图形缩到槽大小），`WorkflowSlotConnectionBehavior` 挂在 `Path` 上；code-behind 只订阅 `ViewModel.PropertyChanged` 的 `State` 并把颜色写到 `RootPath.Fill`。`WorkflowNodeRowsBehavior` 先写 `ViewModel` + `Tag`（`TrySetSlotViewModel`），没有该属性的旧式视图才回退 `WorkflowSlotAttachment`。
- ⇒ WPF/Avalonia/WinUI/MAUI 的透明叠加（半透明标尺、发光描边、阴影）在这家一律换成**不透明绘制**或**顶层分层窗口**，没有第三条路。

### 2.4 平移只有一套模型：`state.PanOffset` + 自绘滚动条

- `SurfaceState.PanOffset` 是唯一平移量。`SetScrollOffset(host, state, x, y)` 写 `PanOffset = (-x, -y)` 再 `ApplyPan`；滚轮、小地图拖拽、滚动条、视口恢复都走它。
- **滚动条**：命名的 `PART_ScrollViewer` 若是 `VeloxDev.WinForms.Xaml.ScrollViewer`，`SyncXamlScrollViewer` 每次 `ApplyPanCore` 写 `ViewportWidth/Height`、`ExtentWidth/Height`、`ScrollOffset = -PanOffset`；条的 `Scroll` 事件反向 `SetScrollOffset`，`SyncingXamlScrollViewer` 截断回环（`OnXamlScrollViewerScrolled`）。
- **正向平移折进 `NegativeOffset`**：`FoldPositivePan` 在缩放枢轴目标计算之前把 `PanOffset > 0` 的部分加进 `layout.NegativeOffset`，并同量回退 `PanOffsetAtPress` / `PanOffset`；负向越界由 `WorkflowSurfaceMath.ClampScrollOffset` 夹住，写入后按增长过的 `ActualSize` 再夹一次。
- **画布尺寸**：`ApplyCanvasSize` 只在命名画布宽高都为 0 时写 `ResolveClientSize(state)`；其它时候画布 `Dock=Fill`，内容由每个视图的 `ApplySurfacePosition` 落位。

### 2.5 滚动只由 `PanOffset` 驱动，别开第二套

`PART_ScrollViewer` 是 `v:ScrollViewer`，构造里 `AutoScroll = false`（`Xaml/Controls/ScrollViewer.cs`）；表面从不写 `AutoScrollPosition` / `AutoScrollMinSize`。在宿主上打开原生 `AutoScroll` 或另写一套平移，会与 `ApplyPan` 推的同一批子窗口位置打架；滚动范围只由 `PanOffset` 与 `CanvasLayout` 决定。

### 2.6 消除闪烁只能靠 Win32 窗口样式，而且句柄重建会丢掉

`NativeWindowStyleHelper`（`internal static`）：

- `WS_CLIPCHILDREN` + `WS_EX_COMPOSITED`，用 `SetWindowLong`/`SetWindowLongPtr` 写（`ApplyStyle` / `SetLong` 分派），再 `SetWindowPos(..., SWP_FRAMECHANGED)` 逼系统重读样式（`ApplyStyle` 里的注释说明不这么做运行时设的样式可能不生效）。
- **`RecreateHandle` 之后样式会丢**，所以挂在 `HandleCreated` 上重设（`EnsureClipChildren` / `EnsureComposited` / `ApplyCompositedToTopLevel` 里的 `HandleCreated` 订阅）。
- **`CompositedMaxControlCount = 100`**：顶层窗口的子控件超过 100 个就**不上** `WS_EX_COMPOSITED`（`ApplyCompositedToTopLevel` 里的 `CountDescendants(top) > CompositedMaxControlCount` 判定），改回「`WS_CLIPCHILDREN` + 拖拽期同步重绘」。⇒ 大图与 demo 的观感不同是**设计取舍**；子窗口数量上限是这家的独有约束。
- 触发点：`WorkflowSurfaceBehavior.Attach` 里给宿主 `EnsureClipChildren` + `EnsureComposited`；`EnsureParts` 给 ScrollViewer / Canvas / GridDecorator 上 `WS_CLIPCHILDREN`（小地图不上）；`WorkflowNodeDragBehavior.Attach` 给节点卡片单独上。
- **帧提交**：`NativeWindowStyleHelper.BeginRedraw` / `EndRedraw` 用 `WM_SETREDRAW` 停掉整棵窗口树，最后一次 `RedrawWindow(... RDW_ALLCHILDREN | RDW_UPDATENOW)`。`ApplyPan`（非手势时）、`ApplyZoomFactor`、`BeginInitialReveal` 都套这一层；`state.IsPanning || host.Capture` 时**不**冻结 —— 每帧冻/解反而造成高频闪烁。

### 2.7 没有合成器 ⇒ `Invalidate()` 只是排队，拖拽必须同步 `Update()`

`WorkflowNodeDragBehavior` 每次移动后：`host.Invalidate(); host.Update();`（`OnMouseMove`）再递归 `RedrawTree`。注释说明理由：`Invalidate()` 只排队，`WM_PAINT` 要等消息循环空闲才合并；拖拽期间鼠标消息高频到达，重绘一直被推迟，节点旧位置的卡片与旧连线来不及擦掉，留下拖影。

配套地，`WorkflowSurfaceBehavior.Refresh` 默认**异步**失效；`ApplyPanCore` 只在 `state.IsPanning || host.Capture` 时对画布 `Update()`（注释与 `linkCanvas.Update()` 分支）。⇒ **刷新策略是「默认异步、手势中同步」**，别家靠合成器自动解决这个问题；`Update()` 调用不是冗余。

**浮动覆盖层是表面的兄弟控件，不挂在滚动视口里**：`PART_MinimapOverlay` 与 `PART_GridDecorator` 同属 `PART_Surface`；`WorkflowMinimapOverlay.PositionAtTopRight` 在父控件 resize 时把 200×140 的块钉到右上角（`CornerMargin = 12`），`Anchor = AnchorStyles.TopRight`。

### 2.8 命中测试：控件是真窗口，连线不是

WinForms 的每个控件都是真窗口，所以对**本身就是控件**的内容不存在 WPF 那种「无背景的 `Grid` 收不到命中」的问题。**连线不是窗口**：两家 demo 都把连线画在 `PART_Canvas` 的 `Paint` 里（`WorkflowLinkLayer`），那条线没有窗口，命中必须在表面的指针处理里手写（§4.9）；反过来，被不透明卡片或浮层真窗口盖住的那一段连线，画布收不到鼠标消息，也就不可能被命中。

对控件内容，它比别家多一道工序：**递归遍历节点卡片的整棵控件树、逐个挂鼠标事件**（`HookControlTree`），并用排除表决定哪些子控件不算拖拽把手（`IsDragHandle`）：

```
control is not TextBoxBase and not ComboBox and not ButtonBase and not CheckBox
    && ResolveSlot(control) is null
```

⇒ 在这家加一个新子控件（自定义按钮、可编辑标签）时，**必须回 `IsDragHandle` 加一条排除**，否则按住它会变成拖节点。

---

### 2.9 没有绑定引擎 ⇒ `WorkflowBindingExtensions`

这家没有 WPF 那种绑定引擎：`DataBindings` 要求每个属性配一个 `BindingSource`。
适配器包的 `WorkflowBindingExtensions` 补上这一块：

- `Control.Bind(model, (view, m) => …)` —— 立刻应用一次，之后模型每报一次变更再应用一次；
- `Control.Bind(model, m => m.Anchor, (view, anchor) => …)` —— 先投影再比，投影值没变就不重放
  （跟一个属性走，不必写属性名字符串）；
- `Control.BindCollection(source, (view, e) => …)` —— 集合变更编组到控件线程。

三条共用的语义两条：**控件销毁即解订**（挂在 `Control.Disposed` 上）；
跨线程编组复用 `ModelChangeRelay`（这家「订阅 / 退订 / `InvokeRequired` 编排」只有这一份）。

⚠ **它是给用户自己写的视图用的**。适配器自带的那几个视图走各自的助手（`WorkflowNodeAttachment` 等），不用它。

### 2.10 卡片/插槽上的按下送不到画布，所以由组件自己路由

指针落在**启用的子控件**（节点卡片 / 插槽）上时画布的 `MouseDown` 根本不触发，句柄只能由组件自己那次转发产出 —— `WorkflowNodeDragBehavior`（`OnMouseDown`）与 `WorkflowSlotConnectionBehavior`（`OnMouseDown` / `OnMouseUp`）调 `WorkflowSurfaceBehavior.RouteComponentPress` / `RouteComponentRelease`，并以组件自身为目标，返回的句柄就地判 `PreventDefault`；目标解析沿命中控件与父链读 `Tag`（优先）/ `ViewModel` / `DataContext` / `BindingContext`，认不到才回退共享曲线判定（`ResolveTarget`）。

- **空白平移**：`SurfaceState.OnCanvasMouseDown` 路由一笔 `PointerPressed`，`PreventDefault` 为假才置 `IsPanning` 并 `canvas.Capture = true`；它同时挂在画布与 `PointerPressSourceName` 解析出的控件上（`HookCanvas` / `HookPressSource`）。
- **指针移动/离开**：`WM_MOUSEMOVE` 在 `PreFilterMessage` 里翻译（`RouteHoverFromCursor`，`lastHoverStamp` 去重）；离开表面由 50ms 的 `pointerWatch` 兜底发 `PointerExited`（`EnsurePointerWatch` / `OnPointerWatchTick`）。
- **滚轮**：在消息过滤器里就被接住（`PreFilterMessage` 调 `RouteWheel`），画布永远看不到（画布上不挂 `MouseWheel`，否则会与过滤器双重路由）。
- **宿主策略**：两个 demo 在 `IInputEvents` 上另订 `KeyDown` / `PointerWheelChanged`，做删除、撤销和 Shift 横滚；全功能 demo 还订 `PointerPressed` 做 Ctrl-click 与 Shift 空白的否决（`WorkflowCanvas.xaml.cs` / `TreeView.xaml.cs`）。

---

## 三、与其它六家的刻意背离

| # | 这里的做法和其他家不一样，因为… | 依据 |
|---|---|---|
| 1 | **启用表面顺手改 Win32 窗口样式（七家里唯一）**：`Attach` 给宿主上 `WS_CLIPCHILDREN`、给顶层窗体上 `WS_EX_COMPOSITED`。这家没有合成器，自绘画布与子窗口的重绘分离必然产生闪烁/鬼影，只能在窗口层解决。 | `WorkflowSurfaceBehavior.Attach` 里 `EnsureClipChildren` / `EnsureComposited` 两行 |
| 2 | **滚轮走应用级 `IMessageFilter`：看得见每一笔滚轮，且 Ctrl+缩放能把消息吃掉（七家里唯一）**：别家都用平台的路由/隧道/预览阶段；WinUI 没有预览相，得挂滚动容器的**内容**（画布）才不漏（见 `wpf.md` §三·1）。这家没有事件路由，只能抢在消息泵那一层；被否决即 `m.Result = IntPtr.Zero; return true;`。 | `WorkflowSurfaceBehavior.cs` 的 `PreFilterMessage` |
| 3 | **插槽连接用消息过滤器 + `WindowFromPoint` + 静态单活动连接（七家里唯一）**：整个手势压在一个应用级过滤器上；按下与抬起可能落在不同的窗口上，必须靠 `WindowFromPoint` 反查目标控件、并自己维护「谁在拖」。 | `WorkflowSlotConnectionBehavior.cs` 的 `_messageFilter` / `_activeConnection`、`GetControlAtScreenPoint`、`TryCompleteFromMouseUp` |
| 4 | **`WorkflowSlotLayoutBehavior.SyncNow(Control)` 只有这家有**：一个公开的**同步**重测入口。延迟路径 `BeginInvoke` 会合并到消息循环，而消息循环排在强制同步重绘之后，所以缩放折叠/画布扩张期间连线会用旧端点画一帧。**调用方**：`WorkflowSurfaceBehavior.ApplyPanCore` / `ApplyViewProjection`、`WorkflowNodeDragBehavior.OnMouseMove`、`WorkflowNodeRowsBehavior`（折叠后）。 | `WorkflowSlotLayoutBehavior.SyncNow` |
| 5 | **小地图不订阅节点**：`WorkflowMinimapOverlay` 只收表面推来的 scroll/content/viewport，节点移动的重画由表面在 `WorkflowNodeAttachment.AnchorChanged` 上补（`SurfaceState.OnNodeAnchorChanged`）。其余家的小地图自己订阅模型。 | `WorkflowMinimapOverlay.ComputeLayout`、`WorkflowSurfaceBehavior.HookView` |
| 6 | **`Replace` 由共享的 Core 池处理**：`Src/Core/VeloxDev.Core/WorkflowSystem/ViewHosting/ViewPool.cs` 的 `OnCollectionChanged` 显式列了 `Replace`（先 Remove 再 Add）。⇒ 「原地替换 `VisibleItems` 元素」会刷新视图；六家都走 Core 池。 | `ViewPool.cs` 的 `case NotifyCollectionChangedAction.Replace` |
| 7 | **Ctrl 判定是精确相等**：消息过滤器先判 `Control.ModifierKeys == Keys.Control`、再看 `GetZoomEnabled(host)`。与 WPF 同形，另四家用 `HasFlag`。⇒ **Ctrl+Shift+滚轮在这家不缩放**（走汇报支、按默认滚处理）；这是七家不一致的地方，代码里没写是刻意还是遗漏。 | `WorkflowSurfaceBehavior.cs` 的 `PreFilterMessage`（`zoomGesture`） |

---

## 四、坑（带依据）

### 4.1 `PointerPressSourceName` 解析的是空白按下源，和画布不能双挂

`EnsureParts` 用 `FindNamed` 解析它，`HookPressSource` 只在 `!ReferenceEquals(pressSource, canvas)` 时订阅同一组 `MouseDown`/`MouseMove`/`MouseUp`/`MouseCaptureChanged`/`MouseEnter`/`MouseLeave`；与画布是同一控件时不挂第二份。未设或名字找不到 ⇒ 不订阅，此时只有 `PART_Canvas` 上的按下能启动平移。两个 demo 与模板都写成 `PART_Surface`（表面的容器 `Panel`）。

### 4.2 `WorkflowCanvasTransformBehavior.GetTransform` 没有消费者（注释描述的是「能力」，不是现状）

成员注释写着：「自绘画布的宿主可以在它的 `OnPaint` 里读 `GetTransform` 来平移绘制原点」。**仓库里没有任何地方读它** —— `grep GetTransform` 在这家只命中定义本身。表面只在 `WorkflowSurfaceBehavior.ApplyPanCore` 末尾调 `WorkflowCanvasTransformBehavior.Apply(host, content)`；节点与连线靠 `IWorkflowSurfaceNodeView.ApplySurfacePosition` 和 `WorkflowLinkLayer` 的项目偏移落位，不读它。要在自家宿主上用它可以，但目前没人这么用。

### 4.3 坐标换算的入口

`WorkflowSlotLayoutBehavior` 的坐标换算走 `ResolveCoordinateHost` + `PointToClient`（见 §4.4）；没有按名字设置布局属性名的开关。`ResolveCoordinateHost` 的顺序：`CoordinateHostName` 在 `parentHost` 子树里找 → `WorkflowSurfaceBehavior.FindSurfaceCanvas`（表面行为手里的 `PART_Canvas`）→ `CoordinateHostType`（默认 `typeof(Panel)`，沿祖先链上溯）→ `parentHost`。`WorkflowNodeViewBehavior.Attach` 把两个行为的 `CoordinateHostType` 都设成 `VeloxDev.WinForms.Xaml.Controls.Canvas`。

### 4.4 插槽锚点**必须**用 `SlotAnchorFromCanvasLocal`，用 `SlotAnchorFromVisualCenter` 会系统性偏移

坐标宿主存在时，路径是 `slotControl.PointToClient(screenPoint)` → `SlotAnchorFromCanvasLocal`（`WorkflowSlotLayoutBehavior.cs` 的 `SyncSlot`），注释写明理由：**`PointToClient` 得到的已经是画布客户区坐标（节点的 `Location` 里已经含 pan + `ActualOffset`），所以不能再减一次偏移；用 `SlotAnchorFromVisualCenter` 会把每条连线整体平移 `-ActualOffset`。** `ResolveCoordinateHost` 永不返回 null（兜底是 `parentHost`），所以 `SlotAnchorFromNode` 分支不可达。

⇒ 这正是 `WorkflowSurfaceMath`（`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Math/WorkflowSurfaceMath.cs` 的 `SlotAnchorFromCanvasLocal` 注释）所说「选错是静默的偏移 bug」。七家在这条上分三派：用 `SlotAnchorFromCanvasLocal` 的是这家、WinUI、MAUI；用 `SlotAnchorFromVisualCenter` 的是 WPF、Avalonia、Jalium；Razor 直接 `new Anchor(...)`。三派不是互斥：除 Razor 外，每家都还调了另外一两个（作退路或用在别的角色上）。

### 4.5 接缝是反射与名字，改名即静默失效

| 反射什么 | 位置 | 失效表现 |
|---|---|---|
| 树的 `ViewModel` / `DataContext` / `BindingContext`，再退 `Tag`（`ResolveValue` 按属性名反射） | `WorkflowSurfaceBehavior.ResolveTree` | 改名 ⇒ 认不到树，缩放/推 offset 全不做，界面照常显示但不跟手 |
| 节点/插槽上的同名属性 | `WorkflowSlotLayoutBehavior.ResolveNode` / `ResolveSlot` / `ResolveDataSource`、`ViewContainer.ApplyBinding` | 认不到 ⇒ 槽不测、锚点停 NaN，`WorkflowSlotUpdateGate` 把连线藏掉 |
| 视图类型名（`NodeViewType` / `LinkViewType`） | `SurfaceTemplateSelector.ResolveType` | 找不到抛 `InvalidOperationException` |
| 宿主里的部件名 | `FindNamed` / `FindControlByName` | 找不到静默跳过，不抛 |

⇒ **改宿主控件或模板的成员名/部件名时，这里一定一起改；除视图类型名会抛以外，其余都静默失效。**

**视口往返**：`Refresh` 依次 `CaptureViewportRestore`（换树时用 `ViewportRestoreScroll` 取待恢复值）→ `ApplyPan` → `PersistViewport`（`layout.ViewportOffset = ViewportOffsetFromScroll(-PanOffset.X, -PanOffset.Y, layout)`）→ `QueueViewportRestore`（`BeginInvoke`；`!host.IsHandleCreated` 时不排队，等 `HandleCreated` 后的 `Refresh` 再来）。`SyncXamlScrollViewer` 写的是 `ScrollOffset = -PanOffset`，**不能再减一次 `ActualOffset`**：那会让画布向左/上扩展后 thumb 被钉回 0。见 [../extension.md](../extension.md) §3.9-10。

### 4.6 应用级消息过滤器的两个副作用

`WorkflowSurfaceBehavior.Attach` 里挂的 `state` 是**每个已启用控件一个**（`EnsureMessageFilter` → `Application.AddMessageFilter(state)`），而 `WorkflowSlotConnectionBehavior` 的 `_messageFilter` 是**进程唯一**。⇒ 同时启用多个工作流表面时：滚轮过滤器会各收一份（先按光标底下的控件认领、再退回消息目标，`ResolveSurfaceHost` / `PreFilterMessage`），连线只认最后 `EnsureMessageFilter` 那次挂上的那一个，而它的归属靠静态 `_activeConnection` 决定。**多表面同时拖连线不是被设计覆盖的场景。**

### 4.7 拖拽期同步重绘是必需的，删掉就出鬼影

见 §2.7。`RedrawTree` 是**递归**的，注释说明为什么必须递归：「卡片背景画完之后，它内部那些透明子控件（标题栏、输出行面板、插槽视图）的重绘还排在消息循环里，卡片移动后会看到旧背景残影，表现为输出行上下透明缝隙里的条状闪烁」。⇒ 只 `host.Update()` 不 `RedrawTree`，卡片的子控件仍然拖影。

### 4.8 子控件数超过 100 就换渲染策略

见 §2.6：`CountDescendants(top) > CompositedMaxControlCount`（100）时不上 `WS_EX_COMPOSITED`（`NativeWindowStyleHelper.ApplyCompositedToTopLevel` 里的判定）。⇒ 在小图上验收通过的效果，在大图上可能不同；这是**已知的阈值**，不是随机闪烁。

### 4.9 连线交互整个落在画布的指针处理里

**两家 demo 的连线都不物化视图**：`ViewLinkLayer.IsActive => true`，`WorkflowLinkLayer` 在 `PART_Canvas` 的 `Paint` 里一趟画完所有可见线并 `PublishCurve`，所以「悬停高亮 / Delete 删除 / 右键菜单」的平台输入都只能落在同一块画布上。命中由 Core 的输入路由（`WorkflowInput` + `HitTestVisibleLinks`）裁决，高亮与删除是宿主的；适配器的表面行为只做平台那一半：把指针/按键翻译成标准输入事件转发进去、命中时给画布取焦点、右键时弹菜单、菜单的目标线离树时把菜单收起来。

| 事 | 落点 | 依据 |
|---|---|---|
| 命中测试 | `SurfaceState.RoutePointer` / `RouteHoverFromCursor` 用 `canvas.PointToClient(Cursor.Position)` 得到画布局部坐标；`ResolveTarget` 认不到组件时调 `tree.HitTestVisibleLinks(x, y, input.HitRadius)`。曲线由 `WorkflowLinkLayer.EnsureGeometry` 在画布局部系里 `PublishCurve`，**判的就是绘制用的那条曲线** | `WorkflowSurfaceBehavior` 的 `RoutePointer` / `ResolveTarget`；`WorkflowLinkLayer.EnsureGeometry` 的 `PublishCurve`；`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Interaction/LinkHitTestEx.cs` 的 `HitTestVisibleLinks` |
| 命中半径 | `LinkHitRadius = 6f`（≈ 最外圈辉光管壁的半宽 5.5px），建输入路由时写进 `input.HitRadius` | `Examples/Workflow/WinForms/Demo/Views/Workflow/WorkflowCanvas.xaml.cs` 的 `LinkHitRadius`、`input.HitRadius = LinkHitRadius` |
| 选中即取焦点 | `RoutePointer` 在 `input.HoveredLink is not null` 时 `FocusCanvas`；上色由 `WorkflowLinkLayer.OnPaint` 读 `WorkflowInput.For(tree).HoveredLink` 自己画（`HoveredLineColor` / `HoveredThickness`） | `WorkflowSurfaceBehavior` 的 `RoutePointer` / `FocusCanvas`、`WorkflowLinkLayer.OnPaint` |
| 删除 | 走连线的 `DeleteCommand`，**不是**摘控件：Delete 键由宿主在 `IInputEvents.KeyDown` 的处理器里执行 | `WorkflowCanvas.xaml.cs` 的 `OnInputKeyDown`、`Examples/Workflow/WinForms Trimmed/Demo/Views/Workflow/TreeView.xaml.cs` 的 `OnKeyDown` |
| 右键菜单 | 条目由 `WorkflowSurfaceBehavior.SetLinkMenuBuilder` 给出；适配器从自己的 `PointerPressed(Right, link)` 弹菜单（`ShowLinkMenu`），菜单一开置 `IsSuspended`、收起恢复；**不挂 `Control.ContextMenuStrip`**。那条线离开可见集时树发 `LinkRemoved`，适配器收尾 | `WorkflowSurfaceBehavior` 的 `AttachLinkInput` / `ShowLinkMenu` |

结论：

1. **`LinkHitRadius = 6f`，带宽 ≈ ±5.5px，不是线体那 2px**。命中判定全在 Core：`LinkHitTestEx.HitTest` 只问「这条线发布过曲线没有、点在不在曲线的半径内」（默认半径 `DefaultHitRadius = 6d`）；**画出来的每一层描边都是可命中内容**，悬停命中的是**最外那圈辉光管壁**（`WorkflowLinkLayer` 的 `Glow` 画 `thickness + 9`：线宽 2f，半宽 5.5px）。7px 落在这圈之内：七家一致，且都等于「只有画出来的部分能命中」。
2. **虚拟连线与端点未量出的线不参与命中**：前者由 `LinkHitTestEx.HitTestVisibleLinks` 显式跳过 `tree.VirtualLink`；后者在 `WorkflowLinkLayer.EnsureGeometry` 里端点不 ready 时 `PublishCurve(null)`，`Contains` 自然答否。
3. **焦点必须与「上色」同一拍发生**：`RoutePointer` 在悬停命中时 `FocusCanvas`，层的下一帧从 `HoveredLink` 上色。全功能 demo 的画布靠 `ControlStyles.Selectable` 才获焦、靠 `TabStop = false` 不进制表位（`WorkflowCanvas` 构造函数）。「被点击才给焦点」会让悬停变红但 Delete 要先点一下（见 `adapters/avalonia.md`）。
4. **输入路由的 target 由消息过滤器统一解析**：节点/插槽是子窗口，表面根收不到它们的 `MouseMove`；`WM_MOUSEMOVE` 在 `IMessageFilter` 里翻译后 `Route`，`ResolveTarget` **Tag 优先**（继承的 `DataContext` 可能是树）。节点/插槽行为把每个按钮的 press/release 都路由；离开表面由 `pointerWatch` 补 `PointerExited`，target 传前一个 `PointerTarget`。
5. **已知代价：连线被卡片/浮层窗口盖住的那一段不可悬停**。指针落在卡片（或小地图/HUD）的真窗口上时画布收不到 `MouseMove`，只有画在空白画布上的那段可命中。要全段命中就得回到每线一个窗口，而那种形状要付 §2.1 的 z 序与 §2.3 的透明代价。

### 4.10 `new Region()` 是**无限**区域，不是空的

`WorkflowLinkAttachment.ApplyRegion(null)`（连线这一帧什么都不画）必须用 `new Region()` + `MakeEmpty()`：GDI+ 的默认构造给出的是**无限**区域（`IsInfinite=True`，`IsEmpty=False`）。`Control.Region` 就是 `SetWindowRgn`，无限区域 = **不做任何裁剪**，不透明子窗口整块露出、被 `BackColor`（= 表面底色 `#1E1E1E`）填满，表现为没有网格线的深色方框。

**触发路径**：`RebuildGeometry` 的四条早退 —— 不可见（`_canRender=false`，橡皮筋收工时）、`WorkflowSlotUpdateGate.IsLinkRenderReady` 不过（端点还没量出来）、两端为 null、`IsDrawable` 为假（两端落在同一像素：**连线手势的第一帧**）。表面不池化连线视图，所以这条只在宿主自己实例化 `LinkView` 时走到；实测一次 49×33 的方框挂了 **1.8 秒**（直到几何再次可画）。

**区域与 `Location`/`Size` 的顺序**：`RebuildGeometry` 里 `ApplyRegion` 必须在写 `target.Location` / `target.Size` 之前 —— 反过来的话，`SetWindowPos` 之后、区域更新之前那一瞬，新露出来的矩形会先按 `BackColor` 画一次。

**验证**：在 `ApplyRegion` 里打 `next.IsEmpty(g)` / `IsInfinite(g)`，`path=NULL` 时应为 `empty=True infinite=False`。

> 这条只在本家有：`Control.Region` 是 WinForms 的窗口裁剪机制，别家（保留模式的 `Clip`/`Bounds`、MAUI 的 `Path` 布局槽裁剪）没有「无限 vs 空」这个二选一，但都有各自的「画不出来时留下上一次的盒子」问题（MAUI 那条见 `adapters/maui.md` §四·12）。

### 4.11 连线菜单：条目是宿主策略，适配器只管弹出与收尾

`SetLinkMenuBuilder(host, Action<ContextMenuStrip, IWorkflowLinkViewModel>)` 是填适配器那份菜单的入口；模板的 tree 不给条目，两个 demo 各加一项 `Delete` 执行 `link.DeleteCommand`。适配器在 `AttachLinkInput` 里订阅输入路由的 `PointerPressed` 与树的 `LinkRemoved`：右键命中连线且 `e.Handle.PreventDefault` 为假时 `ShowLinkMenu`，开菜单置 `WorkflowInput.IsSuspended = true`，`Closed` 恢复；`LinkRemoved` 命中当前 `MenuLink` 时关菜单。

⚠ **订阅顺序**：`ShowLinkMenu` 是适配器自己的 `PointerPressed` 处理器，它在 Route 的过程中读 `PreventDefault`；宿主之后订阅的处理器再置 `PreventDefault` 已经晚于这次检查，拦不住菜单。空白按下的平移检查发生在 `Route` 返回之后（`OnCanvasMouseDown`），那里任何处理器都能否决。

（校验脚本入口：`Src/Test/Harness/verify-workflow-item-templates-all.ps1`，七家平台通吃，`-Platform <name>` 选一家、`-Strict` 严格模式；两个按平台命名的脚本是薄转发。）

### 4.12 槽锚点是**客户区**系，`node.Anchor` 是**模型**系 —— 判端口方向必须先把端点搬过去

`WorkflowSlotLayoutBehavior.SyncSlot` 的 `SlotAnchorFromCanvasLocal` 分支把 `slot.Anchor` 写进**画布客户区**坐标，而 `node.Anchor` / `node.Size` 是**模型**系，两者差一个表面投影（`state.PanOffset + VisualContentOffset`，本家实测约 (356,296)）。`LinkCurve.PortOutward` 判「这个口贴着节点哪条边」是拿端点比节点矩形，所以**端点在哪个系，就必须用那个系的节点矩形** —— 不搬的后果：一个在卡片**左缘**的输入口会被判成**下边**，反向连线（目标在源左边）该往左翻出去的那一端就不翻了。

`WorkflowLinkAttachment.BuildCurve` 里先减 `ox/oy`（`surfacePan + surfaceContent`）再调用、算完把四个点加回来（`WorkflowLinkAttachment.cs` 的 `BuildCurve`），发布的那条曲线用同一组点（`LinkCurve.FromCubic`），所以画与命中仍是同一条。

投影由 `WorkflowNodeAttachment.ApplySurfacePosition` / `WorkflowLinkAttachment.ApplySurfacePosition` 落下去，**两处都要发** —— `ApplyPanCore` 的池化视图循环，与 `ApplyViewProjection`（`ControlAdded` / `VisibleChanged`；池回收只隐藏视图，再进入可见区不触发 `ControlAdded`）。只发前者的话新视图会留着默认的 (0,0)。

**单层绘制不走这条**：`WorkflowLinkLayer.ResolveModel` 优先用 `WorkflowSlotLayoutBehavior.TryGetLocalAnchor` 的节点内局部锚点，再用 `node.Anchor + 局部偏移`，天然与模型同系；取不到局部锚点才退回 `slot.Anchor - (node.Anchor + ox)`。判据（不依赖截图）：在 `BuildCurve` 里临时把两套坐标算出的法线都写进文件，对比 `canvasNormals` / `modelNormals` —— 修前 `(1,0)(0,1)`、修后 `(1,0)(-1,0)`。

### 4.13 池换绑时，卡片里的事件回调必须捕获自己的局部控件

`WorkflowNodeCard.BuildLayout` 先 `ResetRefs()` 把 `_scriptBox` 等字段置空，再 `_bodyPanel.Controls.Clear()`；被摘掉的旧视口仍可能收到一次 `VisibleChanged` → `Layout`。因此卡片内的布局/绘制回调捕获创建时的局部控件并检查 `IsDisposed`，不读会被 `ResetRefs` 清空的字段；`BuildScriptEditor` 的 `viewport.Layout` 按此写。

### 4.14 `SlotRowsPanel.ApplyScale` 不能按自己记住的 collapse 早退

缩放后 `WorkflowNodeRowsBehavior.Rebuild` 会新建输入视图与行；如果面板因为自身 `collapse`/`baseSlotSize` 已经是当前值就 `return`，这些新控件从未收到缩放，停在 18×18，表现为同一卡片里部分端口大、部分端口小。每次 `ApplyScale` 都要把因子传播给所有行并 `PerformLayout`；行自己的 `appliedScale` 去重，不会重复建字体。

### 4.15 缩放换字体时不能立即 Dispose 旧 Font

`SlotRowPanel.ApplyScale` / `WorkflowNodeTitleBehavior.ApplyCollapse` 给 Label 换字体后，旧 Font 若当场 `Dispose`，而 Label 正在用这份字体 `MeasureString`，GDI+ 抛 `ArgumentException: Parameter is not valid.`（`Label.GetPreferredSizeCore` → `Graphics.MeasureStringInternal`），这次绘制会留下红框和红色乱线。旧字体只放进 `retiredFonts`，等行/标题销毁（`Detach`）后统一释放。

### 4.16 悬停高亮需要宿主推帧

`WorkflowLinkLayer.OnPaint` 只在被绘制时读 `WorkflowInput.HoveredLink`；指针换目标本身不会让画布 `Invalidate`。宿主订 `IInputEvents` 的 `PointerMoved` / `PointerEntered` / `PointerExited` 并调用 `surfaceCanvas.Invalidate()`，`HoveredLineColor` / `HoveredThickness` 才会在悬停时生效（两个 demo 的 `TreeView` / `WorkflowCanvas` 都这么做）。

---

## 四点五、画布滚动与表面级按键

**滚轮被表面整笔接管**：`PART_ScrollViewer` 是 `v:ScrollViewer`，内容由 `PanOffset` 落位，滚轮在应用级 `IMessageFilter` 里接（画布自己的 `MouseWheel` 永远不触发：`WM_MOUSEWHEEL` 发给**指针底下**的那个窗口，限定条件见 §2.2）。形状是：路由 → 没人 `PreventDefault` 就 `state.Scroller.ScrollBy` 默认竖滚 → **吞掉消息**（`m.Result = IntPtr.Zero; return true`）。容器本身不滚，消息又被吞掉，所以泄漏 0。

- **步长** `SystemInformation.MouseWheelScrollLines`（默认 3）× 16 = **48 px/格**，与 WPF 同源；`-1`（按页滚）退回视口尺寸。
- **`ScrollBy` 就是平移画布** —— 走 `SurfaceScroller.ScrollBy` → `SetScrollOffset` → `ApplyPan`（小地图拖拽用的同一条路），两个轴都走它；没有独立的滚动偏移。
- **焦点**：按下取焦也在同一个消息过滤器里做（`WM_*BUTTONDOWN` 分支 → `FocusSurfaceForPress`；命中在画布子树内且不是 `TextBoxBase` 才收）。实测点**节点卡片**（不只是空白画布）也能让 `节点 4/4 → 3/3`。
- **键**：`Keys.ShiftKey`(16) / `Keys.ControlKey`(17) / `Keys.Menu`(18) 这些**通用**形式才会到达 `KeyCode`；**侧别形式（`Keys.LShiftKey` 160 等）实测从不出现**。所以 `ToKey` 把八个侧别键照常点名，另外把通用形式按「通用 → 左」映射（`Keys.ShiftKey → LeftShift`）。这几个值都在三段算术区间之外，不会与字母/数字/功能键的差值映射抢。

### 四点五·补、滚轮闸门、到边与横向滚轮

**① 闸门是「放行整笔」**：`InnerScrollableHasRoom(under, host, dx, dy)` 从指针底下沿父链走到表面宿主**之前**，遇到能往该方向滚的 `ScrollableControl` 或 `TextBoxBase` 就 **`return false`**。不显式放行，被吞的消息一点都到不了内层。实测：文本框 `vscr 0→3→6→9→12`、画布 HUD 恒定不动。

**② ⚠ 到边那一格必须由我们自己接 —— 与 Avalonia/Jalium 相反。** `EDIT` 到边仍吞掉 `WM_MOUSEWHEEL`，原生链走不到画布；若放过会掉进**两边都不接的空档**。实现取「到边 ⇒ 闸门返回 false ⇒ 我们的路由 + 默认滚」，步长稳定 48。

**③ 默认滚与平移/小地图走同一条路**：`SurfaceScroller.ScrollBy` → `SetScrollOffset` → `ApplyPan`；`SurfaceScroller.CenterOn` 用 `WorkflowSurfaceMath.CenterScroll` 并加 `VisualContentOffset - ActualOffset` 的标尺带，否则定位固定往左上偏一条标尺。滚轮与拖画布/小地图同语义（越界撑出去）。HUD 在画布 `Paint` 里更新（`WorkflowCanvas.xaml.cs` 的 `surfaceCanvas.Paint += _infoOverlay.UpdateText`）。

**④ demo 的 `WorkflowCanvas.Dispose` 调 `SetIsEnabled(this, false)`**（放在 `DetachSession` 之前）。`DetachSession` 经 `SetWorkflowTree(null)` → `DetachTree` → `ClearScroller` 已摘掉树路由上的 `Scroller`；会泄漏的是**应用级过滤器**（`Application` 对它是**强引用**）。

**⑤ 横向滚轮也接在过滤器里**：`WM_MOUSEHWHEEL(0x020E)` 与 `WM_MOUSEWHEEL(0x020A)` 共用同一套闸门/路由/裁决/默认滚；**倾斜滚轮不进缩放支**。
- ⚠ **`EDIT` 不认 `WM_MOUSEHWHEEL`** ⇒ 闸门对 `TextBoxBase` **只判竖轴**（否则指针压在文本框上时横滚会掉进两边都不接的空档）。
- ⚠ **方向**：`deltaX > 0` 使滚动偏移**减小**（画面右移），这是仓库七家共享的 `dx = -(deltaX/Notch)*step` 约定，与操作系统「向右倾斜 = 向右滚」**相反**；**这是横跨七家的语义问题**，待定。

**⑥ Shift 抬起**：`Keys.ShiftKey(16)` 在 `WorkflowSurfaceBehavior.ToKey` 里映射到 `LeftShift`；实测 `keyup:Shift` 后一格回到竖滚（`SCROLLBY in=(0,-120) step=(0,48)`）。

**回归**：中部 48/格；Ctrl+滚轮缩放 `121% / Scale 0.83`。

## 五、外部参照

- 坐标换算与缩放数学：`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Math/WorkflowSurfaceMath.cs`（七家共用）。
- 滚轮方向、缩放序列、`EnsureNegativeCover` 的时序：`extension.md` §3.9 第 6/7 条（七家一致；这家 `delta > 0 ? 1/1.1 : 1.1`，`WorkflowSurfaceBehavior` 的 `QueueZoom` 调用与 `ApplyZoomFactor`）。
- WPF 那家的对照结论（透明分层、路由事件、`UserControl` 边界）：`memory/modules/WorkflowSystem/adapters/wpf.md`。
