# WorkflowSystem — WinForms

> **另：连线的那个基类已换成附加助手**（2026-10-04）—— 这一角色现在由用户自己的控件 + `WorkflowLinkAttachment.Attach(this)` 组成，
> 下文凡是 `WorkflowLinkView` 的类名与行号，按 `WorkflowLinkAttachment` 读；机制（自盒化 / 雕窗口区域 / 端点订阅 / 命中发布）没变。
> **另（2026-10-04）：slot / node 的基类也换成了附加助手** —— `WorkflowSlotAttachment` / `WorkflowNodeAttachment`；
> 树（`WorkflowTreeView`）仍是基类（它是引擎不是视图），但 WinForms 那家的表面行为是**按对象**交部件（不按名字找）。
> 下文凡提 `WorkflowSlotView` / `WorkflowNodeView` 的类名，按对应的 `*Attachment` 读。
>
> 连线交互规则见 [WorkflowSystem/architecture.md §3.6](../architecture.md)：输入是标准输入
> （`WorkflowInput.For(tree).Route(...)` + `IInputEvents`），指针底下是谁（节点 / 插槽 / 连线）由适配器解析、认不到才回退 Core 的共享曲线判定，外观、删除与菜单的接线归宿主/适配器。
>
> **读法**：契约（七个视图角色、注册位置、联动清单）在 `memory/modules/WorkflowSystem/extension.md` §3.9 与 §4.3，
> 本文不重复；人面向的「怎么搭一个 WinForms 工作流视图」在 `skills/veloxdev-create-workflow/references/gui/winforms.md`，
> 逐角色职责表在 `skills/veloxdev-create-workflow/references/new-adapter.md` / `references/view-layer.md`。
> 本文只写这家的**硬限制、刻意背离、该家特有的坑**。
> **路径写法**：下文的裸文件名（`WorkflowSurfaceBehavior.cs:422` 等）都相对 `Src/Adapters/VeloxDev.WinForms/Attached/Workflow/`；引用 Core、别家、模板、demo 时一律写全路径。

---

## 一、这家要写什么，为什么是这些

七个视图角色在适配器里各有实现文件（视图池由 `ViewPool` + `ViewManager` 两个文件承担），外加一个**契约外的平台补偿文件**：

| 角色 | 这家的实现 | 这家特有的形状 |
|---|---|---|
| 画布宿主 | `WorkflowSurfaceBehavior`（`sealed class` + 静态 `Get/Set` + `ConditionalWeakTable<Control, SurfaceState>`，`WorkflowSurfaceBehavior.cs:15`/`:172`）；画布控件本体也是适配器里的 `WorkflowTreeView`（`WorkflowTreeView.cs:37`） | 同时是**滚轮汇报/缩放的 `IMessageFilter` 宿主**（`SurfaceState : IMessageFilter`，`:17`）；过滤器跟着 `IsEnabled` 挂、看得见**每一笔** `WM_MOUSEWHEEL`（`EnsureMessageFilter` `:39-48`／调用 `:209`，`_filterAdded` 守卫 `:37`） |
| 画布变换 | `WorkflowCanvasTransformBehavior`（`static class`，CWT 存一个 `Offset`，`.cs:22`/`:29`） | 只是一个**值载体**，而且是结构体不是变换对象 |
| 视图池 | `ViewPool`（挂点）+ `ViewManager`（管理器 + `IWorkflowTemplateSelector` 接口，`ViewManager.cs:14-20`/`:28`） | 用**自定义接口**代替 `DataTemplateSelector`（没有 XAML，没有 `DataTemplate`） |
| 节点拖拽 | `WorkflowNodeDragBehavior`（`WorkflowNodeDragBehavior.cs:15`） | 递归挂**整棵控件树**的 `MouseDown`（`:312-343`），不是一次命中测试 |
| 插槽连接 | `WorkflowSlotConnectionBehavior`（`.cs:13`） | 应用级 `IMessageFilter` + `WindowFromPoint`（`:31-32`、`:331`、`:375-379`） |
| 插槽布局 | `WorkflowSlotLayoutBehavior`（`.cs:15`） | 是七家里唯一额外提供**同步重测**入口 `SyncNow` 的（`:288`） |
| 网格装饰器 / 小地图 | **适配器自带实现类**：`WorkflowGridDecorator`（`.cs:23`，`Panel` + `IWorkflowGridDecorator`）与 `WorkflowMinimapOverlay`（`.cs:23`，`Panel` + `IWorkflowMinimapOverlay` + `IWorkflowMinimapScrollSource`）；模板与 `WinForms Trimmed` 派生一层只改配色，全功能 demo 另有自己的实现（`Examples/Workflow/WinForms/Demo/Views/MinimapOverlay.cs:19`） | 七家现在一致 —— 适配器各带一个实现类；这家两个都是 `Panel` 子类自绘 |
| （契约外）平台补偿 | `NativeWindowStyleHelper`（`internal static`，`.cs:19`） | 只有这家有 —— Win32 窗口样式是这家的渲染前提，见 §2.6 |

**装饰器/小地图现在是适配器里的实现类**：`WorkflowSurfaceBehavior.Refresh` 用 `state.GridDecorator is IWorkflowGridDecorator decorator`（`:389`）与 `state.MinimapOverlay is IWorkflowMinimapOverlay minimap`（`:400`）把偏移推进去。两个部件都是宿主**按对象**交进来的（`SetGridDecorator` / `SetMinimapOverlay`），所以它要的仍是**已经存在**的控件，而不是自己创建、也不再按名字找。

⚠ **这家的 `WorkflowMinimapOverlay` 不订阅节点**（与 Jalium / WPF 等的同名控件相反），两个后果都要先知道再改它：

- **内容不能缓存**：没有「节点动过」的信号，缓存会按旧位置反解点击。所以它每次绘制走一趟把包围盒与缩略框一起收齐（`ComputeLayout(List<(double,double,double,double)>? thumbnails = null)`，绘制传 `_nodeRects`），每次拖拽移动再走一趟 —— 后者与其余六家等价（那几家的视口是 DP，一变就整份重算）。
- **纯节点移动不会重画它**：适配器这份控件自己不订阅节点 —— 给它 `Invalidate()` 的只有 `WorkflowTreeView.ApplyPan`（`WorkflowTreeView.cs:811`）与几个配色 setter，而 `ApplyPan` 只跟着平移/缩放/尺寸走。于是单独用适配器这份控件时，「拖动节点但没触发平移」缩略图会停在旧位置；模板与 `WinForms Trimmed` 各自在 `CreateNodeView` 里补了一座 `view.Attachment.AnchorChanged → PART_MinimapOverlay.Invalidate()` 的桥（`Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-tree-view/TemplateClass.cs:32-38`），那两家因此没有这个毛病。**要给这家加缓存，必须同时补上订阅**，否则缩略图会彻底冻住。`WorkflowTreeView` 在构造时把 `PART_Canvas` 同时交给网格装饰器（`WorkflowTreeView.cs:383`），并把 `MinimapOverlay` 属性挂成 `PART_MinimapOverlay`（`WorkflowTreeView.cs:105-136`）。模板与 `WinForms Trimmed` 各自派生一层只改配色（`Src/Templates/VeloxDev.WinForms.Templates/working/content/` 下 7 个模板包一个不少，`workflow-grid-decorator` / `workflow-minimap-overlay` 已是薄子类）。

---

## 二、平台硬限制，与由此产生的做法

> 这一节是全文最有价值的部分：它决定别家能照抄什么、不能照抄什么。

### 2.1 没有附加属性、没有 `DataContext`、没有 `DataTemplate` ⇒ 三个替代物

- **静态 `Get/Set` + `ConditionalWeakTable`**：每个 Behavior 都是 `sealed class`（或 `static class`）+ 一对 `GetXxx/SetXxx(Control, …)`，状态放 CWT（`WorkflowSurfaceBehavior.cs:122`、`WorkflowSlotLayoutBehavior.cs:33`、`WorkflowNodeDragBehavior.cs` 的 `DragState`、`ViewPool.cs:20` 的 `PoolState`）。⇒ **`ConditionalWeakTable` 不可枚举**，需要「遍历所有已挂载控件」时得另存一个普通集合。
- **没有 `DataContext` ⇒ 反射填上下文**：`ViewManager.ApplyContext`（节点/插槽/连线的视图先分派给各自的 `*Attachment`，其余角色走下面这条）先 `view.Tag = item`，再对 `"ViewModel"` / `"DataContext"` / `"BindingContext"` 三个属性名做反射 `SetValue`（`ViewManager.cs:223-264`）。⇒ **视图控件想拿到 VM，要么读 `Tag`，要么有那三个名字之一的属性**；其它名字静默拿不到（不报错）。
- **没有 `DataTemplate` ⇒ `IWorkflowTemplateSelector`**：一个自定义接口，`Control CreateView(object item)`（`ViewManager.cs:14-20`）。契约要求由用户实现并把选择器交给 `ViewManager.SetTemplateSelector`（`:48-51`）。
- **池不安排 z 序**：`ViewManager.AddItem` 在 `Controls.Add` 之后无条件 `view.BringToFront()`（`ViewManager.cs:171`），`Controls[i]` 的序号 0 是最前面（`Add` 追加到末尾 = 最后面）。⇒ **凡是进池的视图，`Controls` 的顺序只反映「谁最后被物化」**；需要"永远待在后面"的视图（如 Trimmed 那家的连线 —— 非 Trimmed 的 demo 根本不物化连线视图，它由画布代画，见 §4.9）只能由宿主在每次可见集变化后自己 `SendToBack` —— 别家靠 `Panel.ZIndex`，这家没有对应物。

### 2.2 没有路由/隧道事件 ⇒ `Application.AddMessageFilter`，而且是**进程级单例**

- 滚轮：应用级过滤器由 `SetIsEnabled` 挂（`EnsureMessageFilter`，`WorkflowSurfaceBehavior.cs:39-48`／调用 `:209`，`_filterAdded` 守卫 `:37`），**看得见每一笔 `WM_MOUSEWHEEL`** —— 注释 `:61-71` 写明理由：消息发给**焦点/光标下的那个窗口**，挂在元素上的处理器只在滚轮正好发给它时收得到，被子窗口盖住的地方一笔都到不了（节点卡片内部的 `AutoScroll` 面板会先吃掉滚轮）。`SetZoomEnabled` 只补一条 `element.MouseWheel` 直路（`:250`）。两枝都在 `PreFilterMessage`（`:72-141`）：非 Ctrl（或关掉了缩放）→ `RouteWheel` 汇报 + `return false` 放行（`:95-100`）；Ctrl 且 `ZoomEnabled` → 缩放路径，可被否决并吞消息（`:102-108`）。
  ⚠ 实测：三格滚轮 = 三行汇报，**但 demo 的视口不动** —— `WM_MOUSEWHEEL` 发给**焦点控件**（demo 里是 SEED 文本框），不是画布；这是平台既有行为，本层不碰它（所以「汇报到了」与「滚了」是两件事，别把后者当成前者失败的证据）。
- 插槽连接：`_messageFilter` 与 `_activeConnection` 都是**静态字段**（`WorkflowSlotConnectionBehavior.cs:31-32`），即**整个进程同时只有一条在拖的连线**；`EnsureMessageFilter` 惰性挂、`DetachMessageFilter` 摘下（`:170-189`）。过滤器自己监听 `WM_MOUSEMOVE`/`WM_LBUTTONUP` 并用 `NativeMethods.WindowFromPoint`（`:338`、`:347-371`）做命中测试。
- ⇒ **这条的代价必须记住**：`IMessageFilter` 是应用级的，所以 **(a)** 多个工作流表面同时开着时，靠 `ResolveSurfaceHost` 认领这一笔归谁，而不是靠事件源 —— 先按**光标底下**的控件（`GetControlAtScreenPoint` + `WindowFromPoint` P/Invoke，`WorkflowSurfaceBehavior.cs:162-166`；认领点 `:80-81`），认不到才退回消息目标 `m.HWnd`（`:143-146`）；判据是**启用**着的表面（`state.IsEnabled`，不再是「开着缩放」），因为关掉缩放的宿主也要收到滚轮汇报；**(b)** 任何全局过滤器链上的异常都会影响整个应用的消息泵。

### 2.3 子控件永远画在父控件的 `OnPaintBackground` 之上 ⇒ **透明分层在这家不可用**

这条是这家的渲染总纲，写在适配器里（模板只派生配色）：

- `WorkflowTreeView.cs:1164-1170`（标尺浮层 `RulerOverlayForm` 的说明，类在 `:1171`）：「**WinForms 子控件总是画在父控件的 `OnPaintBackground` 之上，所以画在那里的半透明标尺带永远压不暗从它下面滚过的、不透明的节点卡片 —— 卡片把带子盖住了。**」唯一能做这件事的机制是 `WS_EX_LAYERED` 顶层弹窗 + `UpdateLayeredWindow` 逐像素 alpha（还带 `WM_NCHITTEST → HTTRANSPARENT` 让下面的平移与拖拽继续可用）。
- 节点卡片照这个结论写：整卡用**不透明**填充（适配器 `WorkflowNodeAttachment.cs:72-74` 强制 A=255），注释明写「**no `SupportsTransparentBackColor` anywhere**」（`Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-node-view/TemplateClass.cs:415-435`，理由还包含「不透明填充把双缓冲彻底擦干净，`SetSelector` 重建后的旧行标签不会以鬼影透出来」）。
- 插槽视图同理：`BackColor = Opaque(slotBackground)`（A 强制 255，适配器 `WorkflowSlotAttachment.cs:88-91`，`Opaque` 本体在 `:251-257`），注释说明**必须 A=255**，因为 `Control.BackColor` 在 A≠255 且未声明 `SupportsTransparentBackColor` 时抛；而模板视图的 `OnPaintBackground` 里 `Clear(Parent?.BackColor ?? BackColor)`（`Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-slot-view/TemplateClass.cs:52-57`）。
- ⇒ **要接一家新平台的人从这里能抄到的是「结论」，不是做法**：WPF/Avalonia/WinUI/MAUI 的透明叠加（半透明标尺、发光描边、阴影）在这家一律要换成**不透明绘制**或**顶层分层窗口**，没有第三条路。

### 2.4 平移有两套模型，宿主各异（`AutoScroll` vs 有符号 pan）

`ResolveScrollOffset` / `ApplyScrollOffset`（`WorkflowSurfaceBehavior.cs:491-519`、`:531-566`）按宿主分两支：

- **`AutoScroll` 宿主**（完整版 demo）：`AutoScrollPosition` 的 **getter 是负的**，而 setter 把参数取反（`getter = −setter`）。捕获写的是 `-(pan.X + AutoScrollPosition.X)`（`:505`），应用写的是 `AutoScrollPosition = (x + pan.X, y + pan.Y)`（`:538`），注释 `:535-536` 把验算过程写全了。
- **有符号 pan 宿主**（模板 / Trimmed demo）：画布固定，节点放在 `node.Anchor + PanOffset`，所以有效滚动 = `-PanOffset`（`:508-514`）；应用时**不直接写画布属性**，而是反射找宿主的私有方法 `OnMinimapScrollRequested(double, double)` 调它（`:543-565`，注释 `:528-529` 说明理由：「直接写画布 `PanOffset` 会被 `Layout` 属性变更排下的延迟 `ApplyPan` 覆盖掉」）。
- 平移本身（拖画布、`_panOffset`、`ApplyPan`）在**适配器基类 `WorkflowTreeView`**（模板与 Trimmed 都从它派生，`WorkflowTreeView.cs:412` 的 `OnMinimapScrollRequested`、`:811` 的 `ApplyPan`），完整版 demo 则在自绘画布 `Examples/Workflow/WinForms/Demo/Controls/WorkflowCanvas.cs`。适配器只读它、只在缩放的枢轴补偿时写它。
- **`AutoScroll` 宿主的滚动范围被钳在 ≥ 0**，因此缩放枢轴只在其内可达、超出即被钳（`:502` 的注释：「the scroll range is clamped >= 0, so the pivot can only be reached within it (overscroll clamps)」）。
- 反射找 `PanOffset` 属性或私有 `_panOffset` 字段的代码在 `ResolvePanOffset`（`:582-604`），注释明写「完整版 demo 的自绘画布把 pan 放在私有 `_panOffset` 字段里，适配器叫不出那个嵌套类型的名字」。⇒ **这条是这家的核心脆弱点**：宿主换成员名就静默失效（见 §4.5）。

### 2.5 `AutoScrollMinSize` 会重新打开 `AutoScroll` ⇒ 手工平移的宿主绝不能碰它

`WorkflowTreeView.cs:951-952` 的注释（模板与 Trimmed 都继承它）：「**`AutoScrollMinSize` 是刻意不设的 —— 赋值会调 `AdjustScrollbars`，它会重新启用 `AutoScroll`，与手工平移打架。**」⇒ 选了一种平移模型之后，**另一种模型的开关属性连赋值都不能碰**。完整版 demo 走的是 `AutoScroll` 那一边，两个开关都在用（`Examples/Workflow/WinForms/Demo/Controls/WorkflowCanvas.cs:201` 的 `AutoScroll = true`，以及 `:1281`、`:1293` 的 `AutoScrollMinSize`）。

### 2.6 消除闪烁只能靠 Win32 窗口样式，而且句柄重建会丢掉

`NativeWindowStyleHelper`（`internal static`）：

- `WS_CLIPCHILDREN`（`.cs:23`）+ `WS_EX_COMPOSITED`（`:24`），用 `SetWindowLong`/`SetWindowLongPtr` 写（`:202`、`:213-214` 的 `SetLong` 分派），再 `SetWindowPos(..., SWP_FRAMECHANGED)` 逼系统重读样式（`:204-206`，注释说明不这么做运行时设的样式可能不生效）。
- **`RecreateHandle` 之后样式会丢**，所以挂在 `HandleCreated` 上重设（`:17` 的注释、`:74`、`:99`、`:145`）。
- **`CompositedMaxControlCount = 100`**：顶层窗口的子控件超过 100 个就**不上** `WS_EX_COMPOSITED`（`:34-36` 的理由 + `:133` 的判定），改回「`WS_CLIPCHILDREN` + 拖拽期同步重绘」。⇒ 大图与 demo 的观感不同是**设计取舍**，不是 bug；接新平台时这条「子窗口数量上限」是这家的独有约束，别家没有对应物。
- 触发点有三类：`WorkflowSurfaceBehavior.SetIsEnabled(element, true)` 里自动做（`:211-213`）；每个「交部件」的 setter 顺带给那个控件上 `WS_CLIPCHILDREN`（`SetScrollViewer` `:315-320`、`SetCanvas` `:330-335`、`SetGridDecorator` `:345-350`，统一走 `EnsureClipChildrenFor` `:778-786`；`SetMinimapOverlay` `:368-376` 是例外，只存对象、不上样式）；以及 `WorkflowNodeDragBehavior` 对节点卡片单独调 `EnsureClipChildren`（`WorkflowNodeDragBehavior.cs:131`）。⇒ **给画布/装饰器/小地图 `< 100` 个子控件、给节点卡片上 `WS_CLIPCHILDREN`，都不是可选的调优，是这家能不出闪烁的前提。**

### 2.7 没有合成器 ⇒ `Invalidate()` 只是排队，拖拽必须同步 `Update()`

`WorkflowNodeDragBehavior` 每次移动后：`host.Invalidate(); host.Update();`（`:216-224`，注释 `:216`、`:220`）再递归 `RedrawTree`（`:507-525`）。理由原话：「`Invalidate()` 只排队，`WM_PAINT` 只有消息循环空闲时才合并；拖拽期间鼠标消息高频到达，重绘一直被推迟，节点旧位置的卡片与旧连线来不及擦掉，留下拖影」。

配套地，`WorkflowSurfaceBehavior.Refresh` 默认**异步**失效，只在 `host.Capture`（平移/拖拽进行中）时才 `Update()`（`:477-484` 的注释 + `:480-484`）。⇒ **这家的刷新策略是「默认异步、手势中同步」，别家靠合成器自动解决这个问题。** 别把这里的 `Update()` 调用当成冗余删掉。

### 2.8 没有命中测试的「透明背景」问题，但有「哪个子控件被按住」的问题

WinForms 的每个控件都是真窗口，所以对**本身就是控件**的内容不存在 WPF 那种「无背景的 `Grid` 收不到命中」的问题。**这只到「内容有窗口」为止**：非 Trimmed 的 demo 把连线画在画布的 `OnPaint` 里，那条线没有窗口，于是 WPF 那个问题原样存在 —— 命中必须在画布的指针处理里手写（§4.9）；反过来，被不透明卡片或浮层的真窗口盖住的那一段连线，画布收不到鼠标消息，也就不可能被命中。

对控件内容，它比别家多一道工序：**递归遍历节点卡片的整棵控件树、逐个挂鼠标事件**（`HookControlTree`，`WorkflowNodeDragBehavior.cs:312-343`），并用排除表决定哪些子控件不算拖拽把手（`IsDragHandle`，`:371-376`）：

```
control is not TextBoxBase and not ComboBox and not ButtonBase and not CheckBox
    && ResolveSlot(control) is null
```

⇒ 在这家加一个新子控件（自定义按钮、可编辑标签）时，**必须回 `IsDragHandle` 加一条排除**，否则按住它会变成拖节点。

---

### 2.9 没有绑定引擎 ⇒ `WorkflowBindingExtensions`（2026-10-05 补）

这家没有 `DataContext`、也没有 WPF 那种绑定引擎：`DataBindings` 要求每个属性配一个 `BindingSource`，
而且对「模型在别的线程改了」一个字都不说。适配器包的 `WorkflowBindingExtensions` 补的就是这一小块：

- `Control.Bind(model, (view, m) => …)` —— 立刻应用一次，之后模型每报一次变更再应用一次；
- `Control.Bind(model, m => m.Anchor, (view, anchor) => …)` —— 先投影再比，投影值没变就不重放
  （跟一个属性走，不必写属性名字符串）；
- `Control.BindCollection(source, (view, e) => …)` —— 集合变更编组到控件线程。

三条共用的语义两条：**控件销毁即解订**（挂在 `Control.Disposed` 上，因为控件比模型活得短）；
跨线程编组复用 `ModelChangeRelay`（这家只有那一份「订阅 / 退订 / `InvokeRequired` 编排」，
**别再抄第二份** —— 它是容易写错的一类代码，这就是把它抽出来的理由）。

⚠ **它是给用户自己写的视图用的**。适配器自带的那几个视图走各自的助手（`WorkflowNodeAttachment` 等），
不用它；不要把这两条路合成一条。

### 2.10 卡片/插槽上的按下送不到画布，所以由组件自己路由

这家没有隧道相：指针落在**启用的子控件**（节点卡片 / 插槽）上时画布的 `MouseDown` 根本不触发，句柄只能由组件自己那次转发产出 —— `WorkflowNodeDragBehavior`（`WorkflowNodeDragBehavior.cs:169`）与 `WorkflowSlotConnectionBehavior`（`WorkflowSlotConnectionBehavior.cs:94`）调 `WorkflowSurfaceBehavior.RouteComponentPress(control, 组件模型, e.Button, 1)`（`WorkflowSurfaceBehavior.cs:532`）并以组件自身为目标，返回的句柄就地判 `PreventDefault`；目标解析沿命中控件与父链读 `ViewModel` / `DataContext` / `BindingContext` / `Tag`，认不到才回退共享曲线判定（`ResolveTarget`，`:553`）。**滚轮是另一条**：它在 Win32 消息过滤器里就被接住（`PreFilterMessage` 的 `:98`／`:104` 调 `RouteWheel`，定义 `:554-565`），画布永远看不到（画布上原来那条 `PART_Canvas.MouseWheel` 已删，留着会与过滤器双重路由），所以滚轮的路由与「Ctrl+缩放被否决即吞消息」（`:106-107` 的 `m.Result = IntPtr.Zero; return true;`）都只能在这一层做。适配器基类 `WorkflowTreeView` 的空白平移走 `OnCanvasMouseDown`（`WorkflowTreeView.cs:540`，句柄读取在 `:564`）；非 Trimmed demo 的自绘画布自持平移，同理自己路由那一笔（`Examples/Workflow/WinForms/Demo/Controls/WorkflowCanvas.cs`）。

---

## 三、与其它六家的刻意背离

| # | 这里的做法和其他家不一样，因为… | 依据 |
|---|---|---|
| 1 | **`SetIsEnabled` 顺手改 Win32 窗口样式（七家里唯一）**：启用画布宿主的同时给画布窗口上 `WS_CLIPCHILDREN`、给顶层窗体上 `WS_EX_COMPOSITED`。因为这家没有合成器，自绘画布与子窗口的重绘分离必然产生闪烁/鬼影，只能在窗口层解决。别家没有这一步，也没有对应的 hook 点。 | `WorkflowSurfaceBehavior.cs:212-213` |
| 2 | **滚轮走应用级 `IMessageFilter`：看得见每一笔滚轮（七家里唯一），且 Ctrl+缩放能真的把消息吃掉（七家里唯一）**：别家都用平台的路由/隧道/预览阶段（WPF `PreviewMouseWheel`、Avalonia `RoutingStrategies.Tunnel`、Jalium `Mouse.PreviewMouseWheelEvent`，见 `wpf.md` §三·1）；WinUI 没有预览相，得挂滚动容器的**内容**（画布）才不漏（挂容器本身一格漏 74 DIP），见 `wpf.md` §三·1。这家没有事件路由，只能抢在消息泵那一层（`WM_MOUSEWHEEL` 发给焦点/光标下的那个窗口，冒泡不到画布），于是分两支：**非 Ctrl（或关掉了缩放）只 `RouteWheel` 汇报、`return false` 放行**（`:95-100`，控件照常滚）；**Ctrl 且开着缩放**才走缩放、被否决即 `m.Result = IntPtr.Zero; return true; // swallow the message`（`:102-108`、`:140`）。设计意图写在 `:61-71`。 | `WorkflowSurfaceBehavior.cs:72-141`、`:96`、`:104-107`、`:140` |
| 3 | **插槽连接用消息过滤器 + `WindowFromPoint` + 静态单活动连接（七家里唯一）**：整文件 390 行；对照 WPF 是 72 行，只有 `PreviewMouseLeftButtonDown`（先读表面存的按下句柄，被否决就不起）→ `SendConnectionCommand`、`PreviewMouseLeftButtonUp` → `ReceiveConnectionCommand`（`Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowSlotConnectionBehavior.cs:39-70`）。这家的复杂度全部来自「按下与抬起可能落在不同的窗口上」，所以必须靠 `WindowFromPoint` 反查目标控件、并自己维护「谁在拖」。**别把 WPF 那种两行式实现当成通用形状往新平台上套。** | `WorkflowSlotConnectionBehavior.cs:31-32`、`:170-189`、`:338`、`:347-380` |
| 4 | **`WorkflowSlotLayoutBehavior.SyncNow(Control)` 只有这家有**：一个公开的**同步**重测入口。理由是延迟路径 `BeginInvoke` 会合并到消息循环，而消息循环排在强制同步重绘之后，所以缩放折叠/画布扩张期间连线会用旧端点画一帧。别家都不需要它 —— 它们的布局/渲染是同一趟流水线。**调用方**：适配器基类 `WorkflowTreeView.cs:852`（模板与 Trimmed 继承它，自动具备）、节点视图 `Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-node-view/TemplateClass.cs:320` 与 `Examples/Workflow/WinForms Trimmed/Demo/Views/Workflow/NodeView.cs:316`。 | `WorkflowSlotLayoutBehavior.cs:274-315` |
| 5 | **表面行为不再按名字找控件**：画布/装饰器/小地图都是宿主**按对象**交进来的（`SetScrollViewer` / `SetCanvas` / `SetGridDecorator` / `SetMinimapOverlay`），改名不再有影响。仍然靠名字找的是**插槽布局** —— `FindControlByName`（Ordinal 全树遍历，`WorkflowSlotLayoutBehavior.cs:652-663`）解析插槽名 / 坐标宿 / 父宿（`:538`、`:560`、`:603`、`:633`），不是 `FindName`/`GetTemplateChild`。`PART_*` 命名约定在这家**只是模板自己遵守的写法**，框架不强制任何前缀。⇒ 给这些具名控件改名，插槽测量会静默失效（不抛）。 | `WorkflowSlotLayoutBehavior.cs:652-663` |
| 6 | **`ViewManager` 处理 `NotifyCollectionChangedAction.Replace`**（`ViewManager.cs:126-142`）：与 Jalium 同（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/ViewManager.cs:105`），而 WPF/Avalonia/WinUI/MAUI 四家的 `switch` 只列 `Add`/`Remove`/`Reset`。⇒ 这家的「原地替换 `VisibleItems` 元素」是**会**刷新视图的。 | `ViewManager.cs:126-142` |
| 7 | **Ctrl 判定是精确相等**：消息过滤器先判 `Control.ModifierKeys != Keys.Control`、再看 `GetState(host).ZoomEnabled`（`WorkflowSurfaceBehavior.cs:96`），`element.MouseWheel` 直路里只看修饰键（`:266`）。与 WPF 同形，另四家用 `HasFlag`（四家的位置见 `wpf.md` §四·4）。⇒ **Ctrl+Shift+滚轮在这家不缩放**（走汇报支、消息放行）；这是七家不一致的地方，代码里没写是刻意还是遗漏，**别猜**。 | `WorkflowSurfaceBehavior.cs:96`、`:266` |

---

## 四、坑（带依据）

### 4.1 这家**没有** `PointerPressSourceName`（那是别家的 API）

这家根本没有定义 `GetPointerPressSourceName` / `SetPointerPressSourceName` —— `grep PointerPressSourceName` 在 `Src/Adapters/VeloxDev.WinForms/` 内一个命中都没有。表面行为改成**按对象**交部件（`SetScrollViewer` / `SetCanvas` / `SetGridDecorator` / `SetMinimapOverlay`），没有「按下指针时该由哪个具名控件启动平移」这个设置项。

其余四家（WPF `Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowSurfaceBehavior.cs:462`、Avalonia `Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/WorkflowSurfaceBehavior.cs:501`、WinUI `Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:463`、MAUI `Src/Adapters/VeloxDev.MAUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:707`）**都**在宿主行为里解析它并据此挂拖拽/平移（Razor 无此 API；Jalium 的表面行为也解析它，`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowSurfaceBehavior.cs:495`，模板里给的是 `PART_SurfaceBorder`）。⇒ **照着别家的模板写 `SetPointerPressSourceName(this, "PART_Canvas")` 在这家编不过**（方法不存在）。这家的平移/拖拽由 `WorkflowNodeDragBehavior` 的整树挂钩与宿主自己的 pan 逻辑承担。

### 4.2 `WorkflowCanvasTransformBehavior.GetTransform` 没有消费者（注释描述的是「能力」，不是现状）

成员注释写着：「自绘画布的宿主可以在它的 `OnPaint` 里读 `GetTransform` 来平移绘制原点」（`WorkflowCanvasTransformBehavior.cs:14-21`）。**仓库里没有任何地方读它** —— `grep GetTransform` 在这家只命中定义本身；模板与两个 demo 都不读，它们直接读自己的 `_panOffset`。⇒ 这是一条**只写不读的通知通道**（`Apply` 是唯一写者，`WorkflowSurfaceBehavior.cs:412`）。要在自家宿主上用它是可以的，但要知道**目前没人这么用过**，别以为「宿主要在 `OnPaint` 里读它」是既有约定。

### 4.3 `WorkflowSlotLayoutBehavior` 里有一条零调用者的死链

`GetActualOffset(Control, IWorkflowTreeViewModel?)`（`WorkflowSlotLayoutBehavior.cs:743-771`）**全仓库没有调用者**，而它依赖的三个访问器也只是为了喂它才存在：状态字段 `LayoutPropertyName`（`:28`）/`ActualOffsetPropertyName`（`:29`）与四个公开访问器 `Get/SetLayoutPropertyName`（`:210`/`:223`）、`Get/SetActualOffsetPropertyName`（`:237`/`:250`）。这六个成员在 `Src/` 与 `Examples/` 内的命中只有它们自己。⇒ **别以为可以通过设 `LayoutPropertyName` 来改行为**，也别在这里照着补功能；实际生效的坐标换算走的是 `ResolveCoordinateHost` + `PointToClient`（见 §4.4）。

### 4.4 插槽锚点**必须**用 `SlotAnchorFromCanvasLocal`，用 `SlotAnchorFromVisualCenter` 会系统性偏移

坐标宿主存在时，路径是 `slotControl.PointToClient(screenPoint)` → `SlotAnchorFromCanvasLocal`（`WorkflowSlotLayoutBehavior.cs:587-590`），注释 `:585-586` 写明理由：**`PointToClient` 得到的已经是画布客户区坐标（节点的 `Location` 里已经含 pan + `ActualOffset`），所以不能再减一次偏移；用 `SlotAnchorFromVisualCenter` 会把每条连线整体平移 `-ActualOffset`（只要设了 `NegativeOffset`，就是恒定的左上移位）。** 取不到坐标宿主时才退回 `SlotAnchorFromNode`（`:593-595`）。

⇒ 这正是 `WorkflowSurfaceMath`（`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Math/WorkflowSurfaceMath.cs:244-247`）注释所说「选错是静默的偏移 bug」。**七家在这条上分三派，照抄前先看你测到的是哪个坐标系**：用 `SlotAnchorFromCanvasLocal` 的是这家、WinUI（`Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowSlotLayoutBehavior.cs:390`）、MAUI（`Src/Adapters/VeloxDev.MAUI/Attached/Workflow/WorkflowSlotLayoutBehavior.cs:438`）；用 `SlotAnchorFromVisualCenter` 的是 WPF（`Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowSlotLayoutBehavior.cs:348`）、Avalonia（`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/WorkflowSlotLayoutBehavior.cs:280`）与 Jalium（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowSlotLayoutBehavior.cs:428`，取不到坐标宿主时退 `SlotAnchorFromNode`）；Razor 直接 `new Anchor(...)` 不经过这三个函数（`Src/Adapters/VeloxDev.Razor/Attached/Workflow/WorkflowSlotLayoutBehavior.razor.cs:83`）。**「分三派」说的是「哪一个是插槽测量的主路径」，不是互斥** —— 除 Razor 外，每家都还调了另外一两个（作退路或用在别的角色上），所以别按「这家只该出现这一个函数名」去搜。

### 4.5 反射是这家的主要接缝，改名即静默失效

三处反射都**不抛异常**，只返回 `null`/退化值：

| 反射什么 | 位置 | 失效表现 |
|---|---|---|
| 树的 `Viewport`/`Layout` 之类成员（`ResolveTree`） | `WorkflowSurfaceBehavior.cs:466-489` | 找不到树 ⇒ 缩放/推 offset 全不做，界面照常显示但不跟手 |
| 宿主的 `PanOffset` 属性或私有 `_panOffset` 字段 | `:582-604` | 找不到 ⇒ 退回 `ViewportOffset`（`:516-518`），表现为**枢轴漂移**（注释 `:498-499` 明说「退回 `ViewportOffset` 会双重减掉内容偏移，每格滚轮都让枢轴漂」） |
| 宿主的私有方法 `OnMinimapScrollRequested(double, double)` | `:543-565` | 找不到 ⇒ 缩放后不重新居中；找不到时异常被 `catch` 吞掉（`:558-560` 注释「尽力而为」） |

⇒ **改宿主控件（模板或 demo）的成员名时，这里一定一起改；而且不会有任何编译错误或运行时异常提醒你。**

**2026-10-03：这些名字现在有了一个真实来源。** 适配器包发了 `WorkflowTreeView`
（`Attached/Workflow/WorkflowTreeView.cs`），生成模板与 `WinForms Trimmed` 的树视图都从它派生 ——
`PanOffset`、`OnMinimapScrollRequested`、四个 `PART_*` 名不再靠用户代码碰巧起对名字，反射是对着包内类型解析的。
⇒ 那三处反射**可以**改成真实接口（尚未做）；在那之前，**基类上这些名字同样不能改名**，
而且改坏不会有编译错误，只会让卡片不跟手、缩放枢轴漂移。

**视口往返现在两半都齐**（2026-10-03 起）：`Refresh` 推 `helper.Viewport`，也持久化
`Layout.ViewportOffset`、并做挂树恢复。做法：
写回用 `WorkflowSurfaceMath.ViewportOffsetFromScroll`，**只在 `ResolveScrollOffset` 走真实 pan 来源时写**
（`out bool measured`）—— 上表那一行说的「退回 `ViewportOffset`」那条兜底不能用，写回去就是枢轴漂移那个坑；
恢复是 `CaptureViewportRestore`（`Refresh` 里、写 Viewport 之前）+ `QueueViewportRestore`（`BeginInvoke`；
**句柄还没创建时不清标记**，等既有的 `HandleCreated`/`InitialSync` 再走一次 `Refresh` 排队）。
见 [../extension.md](../extension.md) §3.9-10。

### 4.6 应用级消息过滤器的两个副作用

`WorkflowSurfaceBehavior.SetIsEnabled(true)` 里挂的 `state` 是**每个已启用控件一个**（`EnsureMessageFilter` → `Application.AddMessageFilter(state)`，`WorkflowSurfaceBehavior.cs:46`／调用 `:209`），而 `WorkflowSlotConnectionBehavior` 的 `_messageFilter` 是**进程唯一**（`WorkflowSlotConnectionBehavior.cs:31`）。⇒ 同时启用多个工作流表面时：滚轮过滤器会各收一份（先按光标底下的控件认领、再退回消息目标，`WorkflowSurfaceBehavior.cs:80-81`／`:143-146`），连线只认最后 `EnsureMessageFilter` 那次挂上的那一个，而它的归属靠静态 `_activeConnection` 决定（`WorkflowSlotConnectionBehavior.cs:31-32`）。**多表面同时拖连线不是被设计覆盖的场景。**

### 4.7 拖拽期同步重绘是必需的，删掉就出鬼影

见 §2.7。这里补一条容易误删的细节：`RedrawTree`（`:507-525`）是**递归**的，注释 `:220-221` 说明为什么必须递归：「卡片背景画完之后，它内部那些透明子控件（标题栏、输出行面板、插槽视图）的重绘还排在消息循环里，卡片移动后会看到旧背景残影，表现为输出行上下透明缝隙里的条状闪烁」。⇒ 只 `host.Update()` 不 `RedrawTree`，卡片的子控件仍然拖影。

### 4.8 子控件数超过 100 就换渲染策略

见 §2.6：`CountDescendants(top) > CompositedMaxControlCount`（100）时不上 `WS_EX_COMPOSITED`（`NativeWindowStyleHelper.cs:133-135`）。⇒ 在小图上验收通过的效果，在大图上可能不同；这是**已知的阈值**，不是随机闪烁。

### 4.9 非 Trimmed demo 的连线交互整个落在画布的指针处理里

这家的非 Trimmed demo **不物化连线视图**（`LinkView` 只是几何载体，不在控件树里，由画布 `OnPaint` 统一绘制），所以「悬停高亮 / Delete 删除 / 右键菜单」的平台输入都只能落在同一块画布上 —— 没有连线的窗口可挂。命中由 Core 的输入路由（`WorkflowInput` + `HitTestVisibleLinks`）裁决，高亮与删除是宿主的；画布只做平台那一半：把指针/按键翻译成标准输入事件转发进去、命中时给画布取焦点、右键时弹菜单、菜单的目标线离树时把菜单收起来 —— 后两件事各有 Core 的一个信号驱动（输入路由的 `PointerPressed(Right, link)` / 树的 `LinkRemoved`），画布只负责平台侧（`Examples/Workflow/WinForms/Demo/Controls/WorkflowCanvas.cs:980-1023`）。

| 事 | 落点 | 依据 |
|---|---|---|
| 命中测试 | 画布把指针翻成**世界坐标**（`ClientToWorld`）转发给输入路由，路由走 `tree.HitTestVisibleLinks` 对每条线 `PublishCurve` 上来的 `LinkCurve` 逐条测（曲线存进 Core 的 `LinkHelper`）；**判的就是绘制用的那条曲线**，所以线弯到哪命中面就到哪 | `WorkflowCanvas.cs:914-915`（`RoutePointer` 在 `:910-927`）、`:1258-1265`（`ClientToWorld`）、`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Interaction/LinkHitTestEx.cs:76-93`（`:57` 的 `Contains` 落到 `Src/Core/VeloxDev.Core/WorkflowSystem/Templates/Helpers/LinkHelper.cs:46`）、`Examples/Workflow/WinForms/Demo/Views/LinkView.cs:488-493` |
| 命中半径 | `LinkHitRadius = 6f`（≈ 最外圈辉光管壁的半宽 5.5px），建输入路由时写进 `input.HitRadius` | `WorkflowCanvas.cs:43`、`:878` |
| 选中即取焦点 | 输入路由的悬停结果一到就 `Focus()`（上色是画布写回渲染器的 `IsHighlighted`，取焦点是画布补的平台一半） | `WorkflowCanvas.cs:919-926`（`Focus()` 在 `:926`）、`:213`（`ControlStyles.Selectable`） |
| 删除 | 走连线的 `DeleteCommand`，**不是**摘控件：Delete 键由宿主自己在 `OnKeyDown` 里执行，菜单项在本地 `OnBuildLinkMenu` 里直接 `Execute` | `WorkflowCanvas.cs:1027-1030`、`:1058-1060` |
| 右键菜单 | 每次右键**现建**一个 `ContextMenuStrip`，条目由本地 `OnBuildLinkMenu` 填（只有「删除连线」一项）；本画布发布的指针是**世界坐标**，所以弹出位置须经 `WorldToClient` 落回客户区再 `PointToScreen`（适配器基类发布的却是**客户区坐标**，那边直接 `PointToScreen`、没有这个逆变换 —— 两家的坐标约定相反，别互相照抄）；菜单一开就把输入路由置 `IsSuspended`、收起时放开（宿主自己记账）；**不挂 `Control.ContextMenuStrip`**（挂上去会变成画布任意处右键都弹）。**菜单不得比它作用的那条线活得久**：那条线离开 `tree.Links`（Delete 键 / Agent / Undo 任一删除路径）时树发 `LinkRemoved`，宿主用记录菜单目标线的字段（本家 `_menuLink`）认领是不是自己这份菜单，认领了才 `_linkMenu?.Close()` —— 收起照常放开 `IsSuspended`，输入路由不代关弹窗；这一对接线在两处：适配器基类 `AttachLinkInput`/`DetachLinkInput`（模板产物与 Trimmed 继承它，自动具备）与这块自绘画布各自的 attach/detach 对 | `WorkflowCanvas.cs:980-1016`、`:1019-1023`、`:1268-1274`、`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowTreeView.cs:738-754`、`:756-772`、`:776-811` |

四条要记住的结论：

1. **`LinkHitRadius = 6f`，带宽 ≈ ±5.5px，不是线体那 2px**。命中判定现在全在 Core：`LinkHitTestEx.HitTest` 只问「这条线发布过曲线没有、点在不在曲线的半径内」（默认半径 `DefaultHitRadius = 6d`）；**画出来的每一层描边都是可命中内容**，悬停命中的是**最外那圈辉光管壁**（本家 `LinkView.Render` 的 `thickness + 9`：`LineThickness = 2f`，半宽 5.5px）。本家画的正是同样两层辉光，所以 6px 落在这圈之内：七家一致，且都等于「只有画出来的部分能命中」。
2. **虚拟连线与端点未量出的线不参与命中**：前者是指针下的橡皮筋（永远贴在指针上，选中它没有意义），由 `LinkHitTestEx.HitTestVisibleLinks` 显式跳过 `tree.VirtualLink`；后者是端点没量到、曲线已被撤回（`LinkView.RefreshGeometry` 在端点不 finite 时 `PublishCurve(null)`），`Contains` 自然答否。判据在 `Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Interaction/LinkHitTestEx.cs:82-86` 与 `Examples/Workflow/WinForms/Demo/Views/LinkView.cs:482-494`。
3. **焦点必须与「上色」同一拍发生**：悬停命中与写回渲染器的高亮是同一次 `RoutePointer` 里的动作，画布在同处 `Focus()`（`Examples/Workflow/WinForms/Demo/Controls/WorkflowCanvas.cs:919-926`）；画布靠 `ControlStyles.Selectable` 才获焦、靠 `TabStop = false` 不进制表位。写成「被点击才给焦点」就会重演 Avalonia 那个 bug：悬停变红但 Delete 要先点一下（见 `adapters/avalonia.md`）。这一家没有 WPF 那种「拿到焦点就把自己滚进视口」的副作用 —— 平移在宿主手里，实测悬停前后 `_panOffset` 不变。
4. **已知代价：连线被卡片/浮层窗口盖住的那一段不可悬停**。指针落在卡片（或小地图/HUD）的真窗口上时画布收不到 `MouseMove`，只有画在空白画布上的那段可命中。这是「画布代画连线」这一形状的固有代价 —— 换成 Trimmed demo 那种「一条线一个窗口」的形状才有全段命中，而那种形状要付 §2.1 的 z 序与 §2.3 的透明代价。

### 4.11 基类上的两处 `protected virtual`（2026-10-03）

`WorkflowTreeView` 现在还有：`OnConnecting` / `OnConnected`（连接建立前后）、`OnBuildLinkMenu(menu, link)`
（填连线右键菜单，基类**什么都不加**、条目是宿主的策略，`WorkflowTreeView.cs:282-286`）。前者由基类用
`WorkflowEventRelay` 接模型事件、转发进钩子；后者的**弹出、定位、开合上报全在基类**
（`OnLinkPointerPressed`，`WorkflowTreeView.cs:776-811`），模板产物只重写 `OnBuildLinkMenu` 这一处来增删条目。

⚠ **基类订阅输入路由的 `PointerPressed` 与树的 `LinkRemoved` 就在 `AttachLinkInput` 里**
（`WorkflowTreeView.cs:738-754`；`Detach` 在 `DetachLinkInput` 里按相反方向卸，`:756-772`），
已无单独的菜单 attach 步骤，也不再依赖订阅先后：链上更靠前的一级（连线自己）在同一个事件的句柄上置
`e.Handle.PreventDefault` 就能否掉这一次，基类在 `OnLinkPointerPressed` 里读它（`WorkflowTreeView.cs:782`）。
空白画布上的平移同一笔否决：`OnCanvasMouseDown` 路由后读同一个句柄（`WorkflowTreeView.cs:564`）。
宿主想否决某一次，订同一个 `PointerPressed` 即可，与基类订阅的先后无关。

（校验脚本已泛化：入口是 `Src/Verification/verify-workflow-item-templates-all.ps1` —— 七家平台通吃，`-Platform <name>` 选一家、`-Strict` 严格模式；旧的 `verify-workflow-item-templates.ps1` / `verify-jalium-item-templates.ps1` 现在是薄转发，老调用照常可用。）

### 4.10 `new Region()` 是**无限**区域，不是空的 —— 用户报的「黑色盒子」就是它（2026-10-03 修）

**症状**：画布上偶尔出现一个**没有网格线的深色方框**，尺寸正好是某条连线**最后一次**的盒子（用户原话：「黑色的盒子，盒子疑似是某个时刻连线的盒子残留的」）。

**根因**：`WorkflowLinkAttachment.ApplyRegion(null)`（连线这一帧什么都不画）原来写的是 `new Region()`，而 GDI+ 的默认构造给出的是**无限**区域（隔离验证：`IsInfinite=True`，`IsEmpty=False`；要空必须 `MakeEmpty()`）。设置 `Control.Region` 就是 `SetWindowRgn` —— 无限区域 = **不做任何裁剪**，这扇不透明子窗口于是整块露出来，被 `BackColor`（= 表面底色 `#1E1E1E`）填满。底色与画布背景同色，所以看起来「背景还在、网格没了」，边界恰是盒子。

**触发路径**：`RebuildGeometry` 的四条早退都会走到这里 —— 不可见（`_canRender=false`，橡皮筋收工时）、`WorkflowSlotUpdateGate.IsLinkRenderReady` 不过（端点还没量出来）、两端为 null、`IsDrawable` 为假（两端落在同一像素：**连线手势的第一帧**）。池化视图带着上一次的盒子走到这条路上就露出来；实测一次 49×33 的方框在屏幕上挂了 **1.8 秒**（直到几何再次可画）。

**修法（`WorkflowLinkAttachment.cs:430-442` 的 `ApplyRegion`）**：

- 「什么都不画」用 `new Region()` + `MakeEmpty()`（`:432-436`）；
- 并且**先雕区域、再写 `Location`/`Size`**（`:403-409`）—— 反过来的话，`SetWindowPos` 之后、区域更新之前那一瞬，新露出来的矩形会先按 `BackColor` 画一次，每帧闪一下方框。

**验证判据（不依赖截图时机，两次都够用）**：临时探针在 `ApplyRegion` 里打 `next.IsEmpty(g)` / `IsInfinite(g)`。修完：`path=NULL` 的每一次都是 `empty=True infinite=False`（0 例反例），而同一条路径仍在正常触发（`size=49x33 screen=514,321 vis=True`）—— 也就是说状态还在，只是现在它不可见。隔离验证 `new Region()` 的语义用 PowerShell 三行就够（`IsInfinite` / `IsEmpty` 都要传一个 `Graphics`）。

> 这条只在本家有：`Control.Region` 是 WinForms 的窗口裁剪机制，别家（保留模式的 `Clip`/`Bounds`、MAUI 的 `Path` 布局槽裁剪）没有「无限 vs 空」这个二选一，但都有各自的「画不出来时留下上一次的盒子」问题（MAUI 那条见 `adapters/maui.md` §四·12）。

### 4.12 槽锚点是**客户区**系，`node.Anchor` 是**模型**系 —— 判端口方向必须先把端点搬过去

Trimmed 这条路上（`WorkflowSlotLayoutBehavior.cs:588` 的 `SlotAnchorFromCanvasLocal` 分支）`slot.Anchor`
写的是**画布客户区**坐标，而 `node.Anchor` / `node.Size` 是**模型**系，两者差一个表面投影
（`_panOffset + VisualContentOffset`，本家实测约 (356,296)）。`LinkCurve.PortOutward` 判「这个口贴着节点哪条边」
是拿端点比节点矩形，所以**端点在哪个系，就必须用那个系的节点矩形** —— 不搬的后果：一个在卡片**左缘**的输入口
会被判成**下边**，反向连线（目标在源左边）该往左翻出去的那一端就不翻了。

`WorkflowLinkAttachment.BuildCurve` 里先减 `ox/oy`（投影）再调用、算完把四个点加回来（`WorkflowLinkAttachment.cs` 的 `BuildCurve`），
发布的那条曲线用同一组点（`LinkCurve.FromCubic`），所以画与命中仍是同一条。
投影的来源：`WorkflowTreeView.VisualContentOffset` + `_panOffset`，**两处都要发** —— 摆位那一趟（子控件循环）
与 `ArrangeLinkViews`（池化的连线视图是在那一趟才挂上来的，只发前者的话新视图会留着默认的 (0,0)）。

**非 Trimmed demo 没有这个问题**：它的画布自己把 `slot.Anchor` 写成**世界坐标**（`WorkflowCanvas.cs:1096`
「live world-coordinate snapshot」），与 `node.Anchor` 同系，所以那边 `BuildLinkCubic(_link, _startLeft, …)` 直接就是对的。
判据（不依赖截图）：在 `BuildCurve` 里临时把两套坐标算出的法线都写进文件，对比
`canvasNormals` / `modelNormals` —— 修前 `(1,0)(0,1)`、修后 `(1,0)(-1,0)`。

---

## 四点五、画布滚动与表面级按键（2026-10-11，探针实测）

**这一家的滚轮基线是「什么都不做」** —— 不是「竖滚」，是动的都不动：`PART_ScrollViewer` 是 `AutoScroll=false` 的 `ScrollableControl`，根本没有偏移可移（实测基线 `视口(画布)` 滚前滚后都是 `0, 0`）。所以接管之后那个「默认竖滚」在这家是**新增能力**，不是保持既有行为 —— 读别家的数字时别把这家的当成同一种情形。

- **滚轮在应用级 `IMessageFilter` 里接**（这家没有路由事件，画布自己的 `MouseWheel` 永远不触发：`WM_MOUSEWHEEL` 发给焦点窗口）。现在的形状是：路由 → 没人 `PreventDefault` 就自己执行默认竖滚 → **吞掉消息**（`m.Result = IntPtr.Zero; return true`）。**泄漏 0**（容器本来就滚不动，消息又被吞了）。
- **步长** `SystemInformation.MouseWheelScrollLines`（默认 3）× 16 = **48 px/格**，与 WPF 同源；`-1`（按页滚）退回视口尺寸。
- **`ScrollBy` 就是平移画布** —— 走这家自己那套 `ResolveScrollOffset` / `ApplyScrollOffset`（小地图拖拽用的同一条路），两个轴都走它。别去找滚动偏移，这里没有。
- ⚠ **`ApplyScrollOffset` 的反射曾经永远找不到目标**：它用 `p.GetType()`（运行时是派生的 `Demo.Views.Workflow.TreeView`）找私有的 `OnMinimapScrollRequested`，而**私有成员不参与反射继承** ⇒ 基类 `WorkflowTreeView` 那个方法从来没被找到、`ScrollBy` 静默空转。2026-10-11 改成沿基类链走（`BindingFlags.DeclaredOnly`）。**这条同时修好了「缩放居中」那条路**（签名平移的宿主也吃这个修复）。
- **焦点**：这家没有附着属性，所以「按下就收焦点」也在同一个消息过滤器里做（`WM_*BUTTONDOWN` 分支，命中在画布子树内且不是 `TextBoxBase` 才收）。实测点**节点卡片**（不只是空白画布）也能让 `节点 4/4 → 3/3`。
- **键**：`Keys.ShiftKey`(16) / `Keys.ControlKey`(17) / `Keys.Menu`(18) 这些**通用**形式才会到达 `KeyCode`；**侧别形式（`Keys.LShiftKey` 160 等）实测从不出现**。所以 `ToKey` 把八个侧别键照常点名，另外把通用形式按「通用 → 左」映射（`Keys.ShiftKey → LeftShift`）。这几个值都在三段算术区间之外，不会与字母/数字/功能键的差值映射抢。
- **`OnZoomMouseWheel` 已删**：它不可达（消息过滤器先吞了所有滚轮），而且**不过路由** —— 订阅方的 `PreventDefault` 在它那里根本拦不住缩放，与另外六家不一致。缩放现在完全归消息过滤器。

## 五、这份文件没写的东西

- **七个角色各自要暴露什么成员、注册位置、`PART_*` 命名约定**：`memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3 与 `skills/veloxdev-create-workflow/references/view-layer.md`。
- **人面向的「怎么在 WinForms 上从零搭一个工作流视图」**：`skills/veloxdev-create-workflow/references/gui/winforms.md`（含 demo 与模板位置、事件挂法示例）。
- **坐标换算与缩放的数学**：`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Math/WorkflowSurfaceMath.cs`，七家共用，不在本文。
- **滚轮方向、缩放序列、`EnsureNegativeCover` 的时序**：`extension.md` §3.9 第 6/7 条（七家一致，这家也是 `delta > 0 ? 1/1.1 : 1.1`，`WorkflowSurfaceBehavior.cs:67` 与 `:211`）。
- **WPF 那家的对照结论**（透明分层、路由事件、`UserControl` 边界等如何影响别家）：`memory/modules/WorkflowSystem/adapters/wpf.md`。
