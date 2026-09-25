# WorkflowSystem — WinForms

> **读法**：契约（七个视图角色、注册位置、联动清单）在 `memory/modules/WorkflowSystem/extension.md` §3.9 与 §4.3，
> 本文不重复；人面向的「怎么搭一个 WinForms 工作流视图」在 `skills/veloxdev-create-workflow/references/gui/winforms.md`，
> 逐角色职责表在 `skills/veloxdev-create-workflow/references/new-adapter.md` / `references/view-layer.md`。
> 本文只写这家的**硬限制、刻意背离、该家特有的坑**。
> **路径写法**：下文的裸文件名（`WorkflowSurfaceBehavior.cs:422` 等）都相对 `Src/Adapters/VeloxDev.WinForms/Attached/Workflow/`；引用 Core、别家、模板、demo 时一律写全路径。

---

## 一、这家要写什么，为什么是这些

七个视图角色落成 8 个文件，外加一个**契约外的平台补偿文件**：

| 角色 | 这家的实现 | 这家特有的形状 |
|---|---|---|
| 画布宿主 | `WorkflowSurfaceBehavior`（`sealed class` + 静态 `Get/Set` + `ConditionalWeakTable<Control, SurfaceState>`，`WorkflowSurfaceBehavior.cs:13`/`:119`） | 同时是**滚轮缩放的 `IMessageFilter` 宿主**（`SurfaceState : IMessageFilter`，`:15`） |
| 画布变换 | `WorkflowCanvasTransformBehavior`（`static class`，CWT 存一个 `Offset`，`.cs:22`/`:29`） | 只是一个**值载体**，而且是结构体不是变换对象 |
| 视图池 | `ViewPool`（挂点）+ `ViewManager`（管理器 + `IWorkflowTemplateSelector` 接口，`ViewManager.cs:14-20`/`:28`） | 用**自定义接口**代替 `DataTemplateSelector`（没有 XAML，没有 `DataTemplate`） |
| 节点拖拽 | `WorkflowNodeDragBehavior`（`WorkflowNodeDragBehavior.cs:15`） | 递归挂**整棵控件树**的 `MouseDown`（`:320-351`），不是一次命中测试 |
| 插槽连接 | `WorkflowSlotConnectionBehavior`（`.cs:13`） | 应用级 `IMessageFilter` + `WindowFromPoint`（`:31-32`、`:340`、`:375`） |
| 插槽布局 | `WorkflowSlotLayoutBehavior`（`.cs:15`） | 是七家里唯一额外提供**同步重测**入口 `SyncNow` 的（`:288`） |
| 网格装饰器 / 小地图 | **适配器里没有实现类**：`WorkflowMinimapOverlay` 是个 `static class`（`.cs:23`），只做「订阅树 + 令控件失效」；装饰器与小地图控件本身由模板/demo 提供 | 六家都随适配器带一个实现 `IWorkflowMinimapOverlay` 的类，这家的实现类在 `Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-minimap-overlay/TemplateClass.cs:20` 与 `Examples/Workflow/WinForms/Demo/Views/MinimapOverlay.cs:19` |
| （契约外）平台补偿 | `NativeWindowStyleHelper`（`internal static`，`.cs:19`） | 只有这家有 —— Win32 窗口样式是这家的渲染前提，见 §2.6 |

**为什么装饰器/小地图不在这家适配器里**：适配器里**没有「创建控件」的钩子**（没有 `DataTemplate` 可以实例化，装饰器/小地图也没有像视图池那样的 `IWorkflowTemplateSelector`），它只能去找**已经存在**的控件：`WorkflowSurfaceBehavior.Refresh` 里 `FindControlByName(host, state.GridDecoratorName!) is IWorkflowGridDecorator decorator`（`:453-454`）与 `... is IWorkflowMinimapOverlay minimap`（`:466-467`）。那两个控件的实体因此落在 demo 与 `dotnet new` 模板里（`Src/Templates/VeloxDev.WinForms.Templates/working/content/` 下 7 个模板包一个不少，其中 `workflow-grid-decorator` / `workflow-minimap-overlay` 就是它们）。**别照着找 `WorkflowGridDecorator.cs` 这个适配器文件，它不存在。**

---

## 二、平台硬限制，与由此产生的做法

> 这一节是全文最有价值的部分：它决定别家能照抄什么、不能照抄什么。

### 2.1 没有附加属性、没有 `DataContext`、没有 `DataTemplate` ⇒ 三个替代物

- **静态 `Get/Set` + `ConditionalWeakTable`**：每个 Behavior 都是 `sealed class`（或 `static class`）+ 一对 `GetXxx/SetXxx(Control, …)`，状态放 CWT（`WorkflowSurfaceBehavior.cs:119`、`WorkflowSlotLayoutBehavior.cs:33`、`WorkflowNodeDragBehavior.cs` 的 `DragState`、`ViewPool.cs:20` 的 `PoolState`）。⇒ **`ConditionalWeakTable` 不可枚举**，所以任何需要「遍历所有已挂载控件」的地方都得另存一个普通列表：`WorkflowMinimapOverlay.cs:34-37` 明写这一点（`BoundControls` 普通 `List<Control>`，注释：「ConditionalWeakTable 在某些目标框架上不可枚举」）。
- **没有 `DataContext` ⇒ 反射填上下文**：`ViewManager.ApplyContext` 先 `view.Tag = item`，再对 `"ViewModel"` / `"DataContext"` / `"BindingContext"` 三个属性名做反射 `SetValue`（`ViewManager.cs:223-237`）。⇒ **视图控件想拿到 VM，要么读 `Tag`，要么有那三个名字之一的属性**；其它名字静默拿不到（不报错）。
- **没有 `DataTemplate` ⇒ `IWorkflowTemplateSelector`**：一个自定义接口，`Control CreateView(object item)`（`ViewManager.cs:14-20`）。契约要求由用户实现并把选择器交给 `ViewManager.SetTemplateSelector`（`:48-51`）。
- **池不安排 z 序**：`ViewManager.AddItem` 在 `Controls.Add` 之后无条件 `view.BringToFront()`（`ViewManager.cs:171`），`Controls[i]` 的序号 0 是最前面（`Add` 追加到末尾 = 最后面）。⇒ **凡是进池的视图，`Controls` 的顺序只反映「谁最后被物化」**；需要"永远待在后面"的视图（如这一家的连线）只能由宿主在每次可见集变化后自己 `SendToBack` —— 别家靠 `Panel.ZIndex`，这家没有对应物。

### 2.2 没有路由/隧道事件 ⇒ `Application.AddMessageFilter`，而且是**进程级单例**

- 滚轮缩放：`SetZoomEnabled` 加 `Application.AddMessageFilter(state)` 并同时挂 `MouseWheel`（`WorkflowSurfaceBehavior.cs:183-191`）。注释 `:186-189` 写明为什么必须用消息过滤器：**「挂在元素上的 `WndProc` 只能收到发给元素自己的滚轮消息，发给子窗口的滚轮永远到不了它」** —— 节点卡片内部的 `AutoScroll` 面板会先把滚轮吃掉。
- 插槽连接：`_messageFilter` 与 `_activeConnection` 都是**静态字段**（`WorkflowSlotConnectionBehavior.cs:31-32`），即**整个进程同时只有一条在拖的连线**；`EnsureMessageFilter` 惰性挂、`DetachMessageFilter` 摘下（`:163-182`）。过滤器自己监听 `WM_MOUSEMOVE`/`WM_LBUTTONUP` 并用 `NativeMethods.WindowFromPoint`（`:331`、`:375-379`）做命中测试。
- ⇒ **这条的代价必须记住**：`IMessageFilter` 是应用级的，所以 **(a)** 多个工作流表面同时开滚轮缩放时，是靠 `ResolveSurfaceHost(m.HWnd)` 从消息目标反查归谁（`:47-51`、`:100-116`），而不是靠事件源；**(b)** 任何全局过滤器链上的异常都会影响整个应用的消息泵。

### 2.3 子控件永远画在父控件的 `OnPaintBackground` 之上 ⇒ **透明分层在这家不可用**

这条是这家的渲染总纲，写在模板里而不是适配器里：

- `Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-tree-view/TemplateClass.cs:340-353`：「**WinForms 子控件总是画在父控件的 `OnPaintBackground` 之上，所以画在那里的半透明标尺带永远压不暗从它下面滚过的、不透明的节点卡片 —— 卡片把带子盖住了。**」唯一能做这件事的机制是 `WS_EX_LAYERED` 顶层弹窗 + `UpdateLayeredWindow` 逐像素 alpha（该文件里的 `RulerOverlayForm` 就是这么做的，还带 `WM_NCHITTEST → HTTRANSPARENT` 让下面的平移与拖拽继续可用）。
- 节点卡片照这个结论写：整卡用**不透明**填充，注释明写「**no `SupportsTransparentBackColor` anywhere**」（`workflow-node-view/TemplateClass.cs:716-725`，理由还包含「不透明填充把双缓冲彻底擦干净，`SetSelector` 重建后的旧行标签不会以鬼影透出来」）。
- 插槽视图同理：`BackColor = Color.FromArgb(255, …)`，注释说明**必须 A=255**，因为 `Control.BackColor` 在 A≠255 且未声明 `SupportsTransparentBackColor` 时抛（`workflow-slot-view/TemplateClass.cs:42-47`），而 `OnPaintBackground` 里 `Clear(Parent?.BackColor ?? …)`（`:96-101`）。
- ⇒ **要接一家新平台的人从这里能抄到的是「结论」，不是做法**：WPF/Avalonia/WinUI/MAUI 的透明叠加（半透明标尺、发光描边、阴影）在这家一律要换成**不透明绘制**或**顶层分层窗口**，没有第三条路。

### 2.4 平移有两套模型，而且**适配器自己不写平移**

`ResolveScrollOffset` / `ApplyScrollOffset`（`WorkflowSurfaceBehavior.cs:521-546`、`:558-598`）按宿主分两支：

- **`AutoScroll` 宿主**（完整版 demo）：`AutoScrollPosition` 的 **getter 是负的**，而 setter 把参数取反（`getter = −setter`）。捕获写的是 `-(pan.X + AutoScrollPosition.X)`（`:533`），应用写的是 `AutoScrollPosition = (x + pan.X, y + pan.Y)`（`:568`），注释 `:562-567` 把验算过程写全了。
- **有符号 pan 宿主**（模板 / Trimmed demo）：画布固定，节点放在 `node.Anchor + PanOffset`，所以有效滚动 = `-PanOffset`（`:536-542`）；应用时**不直接写画布属性**，而是反射找宿主的私有方法 `OnMinimapScrollRequested(double, double)` 调它（`:572-597`，注释说明理由：「直接写画布 `PanOffset` 会被 `Layout` 属性变更排下的延迟 `ApplyPan` 覆盖掉」）。
- 平移本身（拖画布、`_panOffset`、`ApplyPan`）**完全在宿主机**（`Examples/Workflow/WinForms/Demo/Controls/WorkflowCanvas.cs`、`Examples/Workflow/WinForms Trimmed/Demo/Views/Workflow/TreeView.cs`）。适配器只读它、只在缩放的枢轴补偿时写它。
- **`AutoScroll` 宿主的滚动范围被钳在 ≥ 0**，因此缩放枢轴只在其内可达、超出即被钳（`:530-531` 的注释：「the scroll range is clamped >= 0, so the pivot can only be reached within it (overscroll clamps)」）。
- 反射找 `PanOffset` 属性或私有 `_panOffset` 字段的代码在 `ResolvePanOffset`（`:614-637`），注释明写「完整版 demo 的自绘画布把 pan 放在私有 `_panOffset` 字段里，适配器叫不出那个嵌套类型的名字」。⇒ **这条是这家的核心脆弱点**：宿主换成员名就静默失效（见 §4.3）。

### 2.5 `AutoScrollMinSize` 会重新打开 `AutoScroll` ⇒ 手工平移的宿主绝不能碰它

`Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-tree-view/TemplateClass.cs:1040-1041` 的注释：「**`AutoScrollMinSize` 是刻意不设的 —— 赋值会调 `AdjustScrollbars`，它会重新启用 `AutoScroll`，与手工平移打架。**」⇒ 选了一种平移模型之后，**另一种模型的开关属性连赋值都不能碰**。完整版 demo 走的是 `AutoScroll` 那一边，两个开关都在用（`Examples/Workflow/WinForms/Demo/Controls/WorkflowCanvas.cs:955`、`:967`）。

### 2.6 消除闪烁只能靠 Win32 窗口样式，而且句柄重建会丢掉

`NativeWindowStyleHelper`（`internal static`）：

- `WS_CLIPCHILDREN`（`.cs:23`）+ `WS_EX_COMPOSITED`（`:24`），用 `SetWindowLong`/`SetWindowLongPtr` 写（`:53-57`、`:221`），再 `SetWindowPos(..., SWP_FRAMECHANGED)` 逼系统重读样式（`:210-213`，注释说明不这么做运行时设的样式可能不生效）。
- **`RecreateHandle` 之后样式会丢**，所以挂在 `HandleCreated` 上重设（`:17` 的注释、`:78-79`、`:103-104`、`:151-152`）。
- **`CompositedMaxControlCount = 100`**：顶层窗口的子控件超过 100 个就**不上** `WS_EX_COMPOSITED`（`:35-40` 的理由 + `:139` 的判定），改回「`WS_CLIPCHILDREN` + 拖拽期同步重绘」。⇒ 大图与 demo 的观感不同是**设计取舍**，不是 bug；接新平台时这条「子窗口数量上限」是这家的独有约束，别家没有对应物。
- 触发点有三类：`WorkflowSurfaceBehavior.SetIsEnabled(element, true)` 里自动做（`:146-153`）；每个「具名控件」的 setter 顺带给那个控件上 `WS_CLIPCHILDREN`（`SetScrollViewerName` `:268-276`、`SetCanvasName` `:295-303`、`SetGridDecoratorName` `:322-330`、`SetMinimapOverlayName` `:377-385`，统一走 `EnsureClipChildrenForName` `:642-656`）；以及 `WorkflowNodeDragBehavior` 对节点卡片单独调 `EnsureClipChildren`（`WorkflowNodeDragBehavior.cs:133`）。⇒ **给画布/装饰器/小地图 `< 100` 个子控件、给节点卡片上 `WS_CLIPCHILDREN`，都不是可选的调优，是这家能不出闪烁的前提。**

### 2.7 没有合成器 ⇒ `Invalidate()` 只是排队，拖拽必须同步 `Update()`

`WorkflowNodeDragBehavior` 每次移动后：`host.Invalidate(); host.Update();`（`:216-229`，注释 `:218-223`）再递归 `RedrawTree`（`:515-534`）。理由原话：「`Invalidate()` 只排队，`WM_PAINT` 只有消息循环空闲时才合并；拖拽期间鼠标消息高频到达，重绘一直被推迟，节点旧位置的卡片与旧连线来不及擦掉，留下拖影」。

配套地，`WorkflowSurfaceBehavior.Refresh` 默认**异步**失效，只在 `host.Capture`（平移/拖拽进行中）时才 `Update()`（`:483-493` 的注释 + `:489-492`）。⇒ **这家的刷新策略是「默认异步、手势中同步」，别家靠合成器自动解决这个问题。** 别把这里的 `Update()` 调用当成冗余删掉。

### 2.8 没有命中测试的「透明背景」问题，但有「哪个子控件被按住」的问题

WinForms 的每个控件都是真窗口，所以不存在 WPF 那种「无背景的 `Grid` 收不到命中」的问题（对照 `memory/workflow-node-drag-hit-test.md`）。反过来它多一道工序：**递归遍历节点卡片的整棵控件树、逐个挂鼠标事件**（`HookControlTree`，`WorkflowNodeDragBehavior.cs:320-351`），并用排除表决定哪些子控件不算拖拽把手（`IsDragHandle`，`:379-384`）：

```
control is not TextBoxBase and not ComboBox and not ButtonBase and not CheckBox
    && ResolveSlot(control) is null
```

⇒ 在这家加一个新子控件（自定义按钮、可编辑标签）时，**必须回 `IsDragHandle` 加一条排除**，否则按住它会变成拖节点。

---

## 三、与其它六家的刻意背离

| # | 这里的做法和其他家不一样，因为… | 依据 |
|---|---|---|
| 1 | **`SetIsEnabled` 顺手改 Win32 窗口样式（七家里唯一）**：启用画布宿主的同时给画布窗口上 `WS_CLIPCHILDREN`、给顶层窗体上 `WS_EX_COMPOSITED`。因为这家没有合成器，自绘画布与子窗口的重绘分离必然产生闪烁/鬼影，只能在窗口层解决。别家没有这一步，也没有对应的 hook 点。 | `WorkflowSurfaceBehavior.cs:146-153` |
| 2 | **Ctrl+滚轮走应用级 `IMessageFilter`，并且能真的把滚轮消息吃掉（七家里唯一）**：别家都用平台的路由/隧道/预览阶段（WPF `PreviewMouseWheel`、Avalonia `RoutingStrategies.Tunnel`、WinUI `AddHandler(handledEventsToo:true)`，见 `wpf.md` §三·1），其中 Avalonia/WinUI 两家拿不到 preview 阶段、会「先滚一丝」。这家没有事件路由，只能抢在消息泵那一层，于是**能彻底抑制滚动**：`m.Result = IntPtr.Zero; return true; // swallow the message: the target control never scrolls`（`:92-93`）。设计意图写在 `:36-38`。 | `WorkflowSurfaceBehavior.cs:40-51`、`:92-93`、`:183-191` |
| 3 | **插槽连接用消息过滤器 + `WindowFromPoint` + 静态单活动连接（七家里唯一）**：整文件 383 行；对照 WPF 是 58 行，只有 `PreviewMouseLeftButtonDown` → `SendConnectionCommand`、`PreviewMouseLeftButtonUp` → `ReceiveConnectionCommand`（`Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowSlotConnectionBehavior.cs:26-56`）。这家的复杂度全部来自「按下与抬起可能落在不同的窗口上」，所以必须靠 `WindowFromPoint` 反查目标控件、并自己维护「谁在拖」。**别把 WPF 那种两行式实现当成通用形状往新平台上套。** | `WorkflowSlotConnectionBehavior.cs:31-32`、`:163-182`、`:329-379` |
| 4 | **`WorkflowSlotLayoutBehavior.SyncNow(Control)` 只有这家有**：一个公开的**同步**重测入口。理由是延迟路径 `BeginInvoke` 会合并到消息循环，而消息循环排在强制同步重绘之后，所以缩放折叠/画布扩张期间连线会用旧端点画一帧。别家都不需要它 —— 它们的布局/渲染是同一趟流水线。**调用方在模板与 demo 里**：`Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-node-view/TemplateClass.cs:453`、`.../workflow-tree-view/TemplateClass.cs:934`、`Examples/Workflow/WinForms Trimmed/Demo/Views/Workflow/NodeView.cs:452`、`.../TreeView.cs:942`。 | `WorkflowSlotLayoutBehavior.cs:274-310` |
| 5 | **小地图/装饰器的实现不进适配器（七家里唯一）**：六家都随适配器带一个实现 `IWorkflowMinimapOverlay` 的类（WPF `WorkflowMinimapOverlay.cs:19`、Avalonia `:23`、WinUI `:21`、MAUI `:19`、Jalium `:15`、Razor `WorkflowMinimapOverlay.razor.cs:26`），**只有这家同名文件里是个 `static class`，不实现任何接口**（`.cs:23`）。可以确证的结构原因是：这家适配器**没有任何「创建控件」的钩子**（视图池那条路有 `IWorkflowTemplateSelector` 由用户提供，装饰器/小地图没有对应物），它只会 `FindControlByName` 去找**已经存在**的控件（`:453`、`:466`）⇒ 实现类只能由用户代码提供。**代码与注释里没有记录这个选择是有意的还是历史遗留，不要替它编理由。** | `WorkflowMinimapOverlay.cs:11-23`、`WorkflowSurfaceBehavior.cs:453-476` |
| 6 | **定位控件靠 `FindControlByName`（Ordinal 全树遍历），不是 `FindName`/`GetTemplateChild`**：`WorkflowSurfaceBehavior.cs:658` 起，用于画布、装饰器、小地图、坐标宿主（`:604`、`:453`、`:466`、`:575`）。`PART_*` 命名约定在这家**只是模板自己遵守的写法**，框架不强制任何前缀。⇒ 给宿主控件改名，一切静默失效（不抛）。 | `WorkflowSurfaceBehavior.cs:642-658` |
| 7 | **`ViewManager` 处理 `NotifyCollectionChangedAction.Replace`**（`ViewManager.cs:126-141`）：与 Jalium 同（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/ViewManager.cs:110`），而 WPF/Avalonia/WinUI/MAUI 四家的 `switch` 只列 `Add`/`Remove`/`Reset`。⇒ 这家的「原地替换 `VisibleItems` 元素」是**会**刷新视图的。 | `ViewManager.cs:116-142` |
| 8 | **Ctrl 判定是精确相等**：`Control.ModifierKeys != Keys.Control`（`WorkflowSurfaceBehavior.cs:42`）。与 WPF 同形，另四家用 `HasFlag`（四家的位置见 `wpf.md` §四·4）。⇒ **Ctrl+Shift+滚轮在这家不缩放**；这是七家不一致的地方，代码里没写是刻意还是遗漏，**别猜**。 | `WorkflowSurfaceBehavior.cs:42` |

---

## 四、坑（带依据）

### 4.1 `SetPointerPressSourceName` 是**只写不读**（与六家相反）

这家定义了 `GetPointerPressSourceName` / `SetPointerPressSourceName`（`WorkflowSurfaceBehavior.cs:336-357`），**四个地方在写**（`Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-tree-view/TemplateClass.cs:159`、`Examples/Workflow/WinForms/Demo/Controls/WorkflowCanvas.cs:183`、`Examples/Workflow/WinForms/Demo/Form1.cs:22`、`Examples/Workflow/WinForms Trimmed/Demo/Views/Workflow/TreeView.cs:162`），而**适配器里没有任何地方读它**（`GetPointerPressSourceName` 除了自身没有调用者；全仓库 `grep PointerPressSourceName` 在 `Src/Adapters/VeloxDev.WinForms/` 内只命中定义与 getter/setter）。

其余六家**都**在宿主行为里解析它并据此挂拖拽/平移（WPF `WorkflowSurfaceBehavior.cs:210`、Avalonia `:182`、WinUI `:219`、MAUI `:290`、Jalium `:214`；Razor 无此 API）。⇒ **照着别家的模板写 `SetPointerPressSourceName(this, "PART_Canvas")` 在这家不会产生任何效果**，也不会报错。这家的平移/拖拽改由 `WorkflowNodeDragBehavior` 的整树挂钩与宿主自己的 pan 逻辑承担。

### 4.2 `WorkflowCanvasTransformBehavior.GetTransform` 没有消费者（注释描述的是「能力」，不是现状）

成员注释写着：「自绘画布的宿主可以在它的 `OnPaint` 里读 `GetTransform` 来平移绘制原点」（`WorkflowCanvasTransformBehavior.cs:14-20`）。**仓库里没有任何地方读它** —— `grep GetTransform` 在这家只命中定义本身；模板与两个 demo 都不读，它们直接读自己的 `_panOffset`。⇒ 这是一条**只写不读的通知通道**（`Apply` 是唯一写者，`WorkflowSurfaceBehavior.cs:479`）。要在自家宿主上用它是可以的，但要知道**目前没人这么用过**，别以为「宿主要在 `OnPaint` 里读它」是既有约定。

### 4.3 `WorkflowSlotLayoutBehavior` 里有一条零调用者的死链

`GetActualOffset(Control, IWorkflowTreeViewModel)`（`WorkflowSlotLayoutBehavior.cs:758-786`）**全仓库没有调用者**，而它依赖的三个访问器也只是为了喂它才存在：状态字段 `LayoutPropertyName`（`:28`）/`ActualOffsetPropertyName`（`:29`）与四个公开访问器 `Get/SetLayoutPropertyName`（`:210`/`:223`）、`Get/SetActualOffsetPropertyName`（`:237`/`:250`）。这六个成员在 `Src/` 与 `Examples/` 内的命中只有它们自己。⇒ **别以为可以通过设 `LayoutPropertyName` 来改行为**，也别在这里照着补功能；实际生效的坐标换算走的是 `ResolveCoordinateHost` + `PointToClient`（见 §4.4）。

### 4.4 插槽锚点**必须**用 `SlotAnchorFromCanvasLocal`，用 `SlotAnchorFromVisualCenter` 会系统性偏移

坐标宿主存在时，路径是 `slotControl.PointToClient(screenPoint)` → `SlotAnchorFromCanvasLocal`（`WorkflowSlotLayoutBehavior.cs:602-604`），注释 `:597-601` 写明理由：**`PointToClient` 得到的已经是画布客户区坐标（节点的 `Location` 里已经含 pan + `ActualOffset`），所以不能再减一次偏移；用 `SlotAnchorFromVisualCenter` 会把每条连线整体平移 `-ActualOffset`（只要设了 `NegativeOffset`，就是恒定的左上移位）。** 取不到坐标宿主时才退回 `SlotAnchorFromNode`（`:608-610`）。

⇒ 这正是 `WorkflowSurfaceMath`（`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Math/WorkflowSurfaceMath.cs:207-213`）注释所说「选错是静默的偏移 bug」。**七家在这条上分三派，照抄前先看你测到的是哪个坐标系**：用 `SlotAnchorFromCanvasLocal` 的是这家、WinUI（`Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowSlotLayoutBehavior.cs:402`）、MAUI（`Src/Adapters/VeloxDev.MAUI/Attached/Workflow/WorkflowSlotLayoutBehavior.cs:465`）；用 `SlotAnchorFromVisualCenter` 的是 WPF（`Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowSlotLayoutBehavior.cs:356`）与 Avalonia（`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/WorkflowSlotLayoutBehavior.cs:284`）；Jalium 只走 `SlotAnchorFromNode` 一条（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowSlotLayoutBehavior.cs:357`），Razor 直接 `new Anchor(...)` 不经过这三个函数（`Src/Adapters/VeloxDev.Razor/Attached/Workflow/WorkflowSlotLayoutBehavior.razor.cs:80`）。**「分三派」说的是「哪一个是插槽测量的主路径」，不是互斥** —— 除 Jalium 与 Razor 外，每家都还调了另外一两个（作退路或用在别的角色上），所以别按「这家只该出现这一个函数名」去搜。

### 4.5 反射是这家的主要接缝，改名即静默失效

三处反射都**不抛异常**，只返回 `null`/退化值：

| 反射什么 | 位置 | 失效表现 |
|---|---|---|
| 树的 `Viewport`/`Layout` 之类成员（`ResolveTree`） | `WorkflowSurfaceBehavior.cs:496-519` | 找不到树 ⇒ 缩放/推 offset 全不做，界面照常显示但不跟手 |
| 宿主的 `PanOffset` 属性或私有 `_panOffset` 字段 | `:614-637` | 找不到 ⇒ 退回 `ViewportOffset`（`:544-545`），表现为**枢轴漂移**（注释 `:523-527` 明说「退回 `ViewportOffset` 会双重减掉内容偏移，每格滚轮都让枢轴漂」） |
| 宿主的私有方法 `OnMinimapScrollRequested(double, double)` | `:572-597` | 找不到 ⇒ 缩放后不重新居中；找不到时异常被 `catch` 吞掉（`:590-593` 注释「尽力而为」） |

⇒ **改宿主控件（模板或 demo）的成员名时，这里一定一起改；而且不会有任何编译错误或运行时异常提醒你。**

### 4.6 应用级消息过滤器的两个副作用

`WorkflowSurfaceBehavior.SetZoomEnabled` 里挂的 `state` 是**每个已启用控件一个**（`Application.AddMessageFilter(state)`，`:190`），而 `WorkflowSlotConnectionBehavior` 的 `_messageFilter` 是**进程唯一**（`:31`）。⇒ 同时启用多个工作流表面时：缩放的过滤器会各收一份（靠 `ResolveSurfaceHost(m.HWnd)` 归位，`:47-51`），连线只认最后 `EnsureMessageFilter` 那次挂上的那一个，而它的归属靠静态 `_activeConnection` 决定（`:31-32`）。**多表面同时拖连线不是被设计覆盖的场景。**

### 4.7 拖拽期同步重绘是必需的，删掉就出鬼影

见 §2.7。这里补一条容易误删的细节：`RedrawTree`（`:515-534`）是**递归**的，注释 `:225-228` 说明为什么必须递归：「卡片背景画完之后，它内部那些透明子控件（标题栏、输出行面板、插槽视图）的重绘还排在消息循环里，卡片移动后会看到旧背景残影，表现为输出行上下透明缝隙里的条状闪烁」。⇒ 只 `host.Update()` 不 `RedrawTree`，卡片的子控件仍然拖影。

### 4.8 子控件数超过 100 就换渲染策略

见 §2.6：`CountDescendants(top) > CompositedMaxControlCount`（100）时不上 `WS_EX_COMPOSITED`（`NativeWindowStyleHelper.cs:138-139`）。⇒ 在小图上验收通过的效果，在大图上可能不同；这是**已知的阈值**，不是随机闪烁。

---

## 五、这份文件没写的东西

- **七个角色各自要暴露什么成员、注册位置、`PART_*` 命名约定**：`memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3 与 `skills/veloxdev-create-workflow/references/view-layer.md`。
- **人面向的「怎么在 WinForms 上从零搭一个工作流视图」**：`skills/veloxdev-create-workflow/references/gui/winforms.md`（含 demo 与模板位置、事件挂法示例）。
- **坐标换算与缩放的数学**：`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Math/WorkflowSurfaceMath.cs`，七家共用，不在本文。
- **滚轮方向、缩放序列、`EnsureNegativeCover` 的时序**：`extension.md` §3.9 第 6/7 条（七家一致，这家也是 `delta > 0 ? 1/1.1 : 1.1`，`WorkflowSurfaceBehavior.cs:61` 与 `:213`）。
- **WPF 那家的对照结论**（透明分层、路由事件、`UserControl` 边界等如何影响别家）：`memory/modules/WorkflowSystem/adapters/wpf.md`。
