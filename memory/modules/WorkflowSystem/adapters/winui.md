# WorkflowSystem — WinUI 适配器

> 连线交互规则见 [WorkflowSystem/architecture.md §3.6](../architecture.md)：输入是标准输入
> （`WorkflowInput.For(tree).Route(...)` + `IInputEvents`），命中归 Core 的共享曲线判定，外观、删除与菜单的接线归宿主/适配器。
>
> 契约与注册位置在 [`../extension.md`](../extension.md) §3.9；七角色职责表与 `PART_*` 命名在
> `skills/veloxdev-create-workflow/references/view-layer.md`；本家的写法说明在
> `skills/veloxdev-create-workflow/references/gui/winui.md`。本文只写这一家的**形状**：
> 被哪些平台事实逼成现在这样、哪里和另外六家不一样、改哪里会踩什么。

代码：`Src/Adapters/VeloxDev.WinUI/Attached/Workflow/`。

**参考实现是 `Examples/Workflow/WinUI Trimmed/`（offset frame）**；完整版用另一条腿：**每条线自己的包围盒**
（见 §四·P3）。两者都避免按画布尺寸裁线，别把任何一边的盒子绑回画布 `ActualWidth/Height`。

---

## 一、这一家与契约的接法（只说需要额外知道的部分）

七个角色都落在适配器里，与别家同名（`WorkflowSurfaceBehavior` / `WorkflowCanvasTransformBehavior` /
`ViewManager`+`ViewPool` / `WorkflowNodeDragBehavior` / `WorkflowSlotConnectionBehavior` /
`WorkflowSlotLayoutBehavior` / `WorkflowMinimapOverlay`）。附着属性名与 `PART_*` 约定与 WPF 一致，
不要在这里重抄一遍。契约之外这一家多出来的是：

**多一个公开入口 `WorkflowSurfaceBehavior.Refresh(UserControl host)`**（`Attached/Workflow/WorkflowSurfaceBehavior.cs`）。
原因是这一家解析 `PART_*` 唯一的路子是**名称域查找**（`FindName`，`ResolveNamedControls`），而名称域挂在 `UserControl` 上、
不在可视树上 —— 宿主换树或重新套模板后，必须有人再调一次 `Refresh` 重新解析。别家能靠可视树遍历自愈，
所以别家没有这个成员。**接新平台时如果你能在可视树上按名字找，就不需要抄它。**

契约要求「`Viewport` 是画布局部坐标、且只有适配器写」：这一家写在 `WorkflowSurfaceBehavior.ApplyVisibleRegion`
（`ApplyVisibleRegion`，同时写 `Layout.ViewportOffset`），触发链路是 `UpdateVisibleRegion` → `ApplyVisibleRegion`。
2026-10-03 起挂树时也恢复：`CaptureViewportRestore` 在 `Refresh` 里取值（持久化是延后的，所以读模型即可），
恢复借它既有的 `DispatcherQueue` Low 队列 —— 而且**跳过那一轮的 `ApplyVisibleRegion`**，因为 `ChangeView`
是异步的，此刻读到的还是旧偏移，写回去就抹了存档位置；让落地后的 `ViewChanged → Refresh` 去写。
见 [../extension.md](../extension.md) §3.9-10。
契约要求「连线视图首行过渲染就绪门」：那行在视图侧。本家两个 demo 现在**都过了这道门**，只是形状不同 ——
`Examples/Workflow/WinUI/Demo/Views/Workflow/PolylineCurveView.xaml.cs` 把它收在一个 `RenderReady` 属性里
（`CanRender && 四端点非 NaN`，2026-09 那次修链接「新连线有时不出现」时加的）；
`Examples/Workflow/WinUI Trimmed/Demo/Views/Workflow/LinkView.xaml.cs` 没有独立属性，在 `PublishCurve` 里现算同一个合取。
按渲染就绪门改哪一份，先核这一句 —— 两份现在都齐，别再以为 Trimmed 缺 NaN 那半。

---

## 二、平台硬限制（决定了别家能照抄什么）

**L1 · 没有 Preview / 隧道阶段，且指针事件在到达自定义处理器前已被框架标记 handled。**
滚轮因此只能挂在**画布**上、走冒泡、注册 `handledEventsToo: true`
（`WorkflowSurfaceBehavior.HookWheel`，理由自陈在它上方的注释）：缩放与普通滚轮的汇报现在共用同一支 `OnSurfaceWheel` ——
`Ctrl` 且开着缩放走缩放，其余只路由、不置 `Handled`。代价写在同一条注释里：
**一格 Ctrl+滚轮可能先被原生滚动吃掉一点**；普通滚轮则是位置与目标都取自**滚动之后**（与 Ctrl 那条同一取舍）。
2026-10-06 实测把普通滚轮从宿主收回来：宿主上的 `control.PointerWheelChanged += …` 与
`control.AddHandler(UIElement.PointerWheelChangedEvent, …, true)` **都是两格 0 行** —— 这家从不把滚轮交给宿主；
并进滚动容器这一支后 **3/3**（2026-10-11 又改挂到画布内容，见 §三点五）。挂接也不再跟 `ZoomEnabled` 走（`HookWheel`/`state.WheelHandler` 取代了
`HookZoom`/`state.ZoomHandler`：解析到画布就装，卸载在 `UnhookWheel`，只改开关时 `OnZoomEnabledChanged` 补一次）。
插槽连线的处理方式同源：冒泡 `PointerPressed`/`PointerReleased`，
不置 `e.Handled`，用下降沿 `ReleasePointerCaptures()` 代替「抢在子元素之前」
（`WorkflowSlotConnectionBehavior.cs` 的 `OnPointerPressed`）。这家**没有**隧道相，所以四个手势的否决只能由组件自己路由这一笔按下（见 L9）。对照：WPF 走隧道 `PreviewMouseLeftButtonDown` 并置 `Handled`
（`Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowSlotConnectionBehavior.cs` 的 `OnPointerPressed` / `OnPointerReleased`）。

**L2 · 没有公共 `OnRender`。** 小地图只能是**保留式元素**：`WorkflowMinimapOverlay : Canvas`
（`WorkflowMinimapOverlay.cs` 的类声明），`RebuildShapes` 复用 `Rectangle` 池并**原地改** `Width/Height/SetLeft/SetTop`。
重绘不是去抖而是**节流**：`ScheduleRebuild` 配一个 ctor 里建的 16 ms `DispatcherQueueTimer`，
注释明说去抖会让连续平移期间「只画一次」；`Tick` 里吞 `COMException`（构造函数的 `catch`）。
**别家有 `OnRender`/`Render` 就别抄这套** —— WPF/Avalonia/Jalium 覆写 `OnRender`/`Render`，
MAUI 走 `GraphicsView`/`IDrawable`（各适配器的 `…/WorkflowMinimapOverlay.cs`）。

**L3 · `LayoutUpdated` 是逐元素的，且画布在节点子树布局之后才重排自己的子元素。**
所以插槽测量必须**挂两级**：节点自己的和坐标宿主（`PART_Canvas`）的
（`WorkflowSlotLayoutBehavior.AttachCoordinateHost`，理由注释在 `LayoutState.CoordinateHost`）。
**WPF 免疫**：它的 `LayoutUpdated` 是整棵树一次 layout pass、天然 post-arrange（同注释）。
WinUI 是七家里唯一需要挂两级的。
同源的三个附带事实：`LayoutUpdated` 的 `sender` 恒为 `null`，只能用闭包捕获控件（`Attach` 里的注释）；
`SizeChanged` 与之并行挂（`OnSizeChanged`）；两者都**同步**调 `Sync`，异步 hop 会晚一帧（`ScheduleSync`）。

**L4 · 名称域查找只有 `FindName` 一条路。** 见 §一 的 `Refresh(UserControl)`。
`PART_*` 必须真的落在 `UserControl` 的 `x:Name` 域里；「模板不生成 `x:Name="Root"`」的坑
（`skills/.../gui/winui.md` 的「The template does not emit `x:Name="Root"`」那段；demo 侧对照是 `Examples/Workflow/WinUI/Demo/Views/Workflow/TreeView.xaml` 根元素的 `x:Name="Root"` —— 有 `x:Name="Root"` 的是**非 Trimmed** 那个 demo）就是这条限制的另一面。

**L5 · 画布平移只能用合成变换。** `state.Canvas.Translation = new Vector3(ActualOffset.H, ActualOffset.V, 0f)`
（`WorkflowSurfaceBehavior.ApplyLayout`），注释写明「WinUI 的子元素（不像 WPF）不会把 `RenderTransform`
绑到 `CanvasTransformBehavior.Transform`」。这一条牵出本家整套 **offset frame**（连线几何里烘进 `+offset`、
元素定位到 `-offset`、`Clip` 三处清空）：做法与理由见 `skills/.../gui/winui.md` 的「Adding your own content to a node」与「Links — the offset frame」两节，此处不复述。
它的**推论**必须记住：因为合成平移已经把画布平移反掉了，槽位锚点要用 `SlotAnchorFromCanvasLocal`（恒等形式），
用视觉中心形式会**二次减去** `ActualOffset`，表现为「每条连线整体偏一个常量」
（`WorkflowSlotLayoutBehavior.SyncSlot` 的注释与分支；`skills/.../gui/winui.md` 的「`Clip` is cleared in three places」那条）。
同理，指针世界坐标要**手动补偿**画布渲染平移（`WorkflowSurfaceBehavior.ToCanvasLocalAnchor`，注释自陈
「少减一段不会报错，只会在平移/缩放后命中另一条线」）。

**L6 · `DispatcherQueuePriority` 只有 Low / Normal / High 三档，没有 `Render`。** 于是「延后做、别挡当前帧」
这件事全靠**选档位 + 落地前重校验**这两招，七家里这套写得最密的也是本家：

| 延后的事 | 档位 | 重校验 |
|---|---|---|
| 可见区域/虚拟化重算（`UpdateVisibleRegion`） | `Low`（`UpdateVisibleRegion` 的 `TryEnqueue`） | `ReferenceEquals(currentState, state)` |
| 一批视图的物化（`ScheduleNextBatchRender`） | `Low`（`ViewManager.ScheduleNextBatchRender`） | 批次上限 3 |
| 槽位同步（`ScheduleSync`） | `Normal`（`WorkflowSlotLayoutBehavior.ScheduleSync`） | `Syncing` 守卫（`WorkflowSlotLayoutBehavior.Sync`） |
| 小地图在 `ScrollViewer` 尺寸变化后的重算 | `Low`（`WorkflowMinimapOverlay.OnScrollViewerResized`） | —— |

**这张表是这一家最值得抄的东西**：在任何「异步重算 + 下一帧才生效」的宿主上，
「排到低优先级」与「落地时确认自己还是当前那份状态」必须成对出现。

**这张表有一个代价，记在这里免得下次再推一遍**：`Low` 只在 Normal 队列空的时候才跑，所以**任何一直不停、
每帧都在干活的动画都会把上表全部饿住**（TransitionSystem 对可见性一无所知，见 §四·P8；每个动画还各占一个
自己重装填的 `DispatcherQueueTimer`，没有任何合并）。症状因此不是「画错了」，而是**延迟**：画布上节点/连线
物化慢一拍、可见区域重算落在后面、新连线不出现 —— 而且**「迟迟不出现」和「一直看不到」是同一个开关**，
只是排队时长不同。
**这一条是推断，不是实测**（2026-10-01 有过一轮把它当成因的改动，改完用户说更卡，已全部回退）。
要把它坐实只需一件事：给 `ScheduleNextBatchRender` 与 `ProcessNextBatch` 各打一个带时间戳的日志，
量「排进去到跑起来」隔了多久。别在没量到之前再按它改代码。

**L7 · WinRT 控件只能在有 XAML 运行时、且在 UI 线程上构造。** 小地图 ctor 里
`DispatcherQueue.GetForCurrentThread()?.CreateTimer()` 用 `?.` + `catch (COMException)` 兜住
（构造函数）：在非 UI 线程构造就是**永不重绘且不报错**；
在纯数据进程里构造则是类激活失败（同 TransitionSystem 那侧，见
`Examples/Transition/AUTO TEST/Samplers/UnreachableSamplers.cs` 里 MAUI `BindableObject` 那几条 —— `BrushSampler` / `ShadowSampler` / `TransformSampler`）。

**L8 · 命中测试全走 `DataContext` 契约类型，只剩一个 WinUI 内部类型靠比名字符串。** `IsSurfaceBlankInteraction`
对节点/插槽用 `DataContext is IWorkflowNodeViewModel or IWorkflowSlotViewModel`（`IsWorkflowNodeOrSlotVisual`），
对连线视图用 `DataContext is IWorkflowLinkViewModel`（`IsWorkflowLinkVisual`）—— 都是**契约类型，可靠**，演示重命名视图类型不再影响判定。
唯一的字符串比较留给 `ScrollContentPresenter`（WinUI 内部类型，适配器引不到）：`string.Equals(x.GetType().Name, "ScrollContentPresenter", StringComparison.Ordinal)`（`IsSurfaceBlankInteraction`）。
**推论**：靠 `GetType().Name` 认 `BezierCurveView`/`PolylineCurveView` 的兜底没有（`IsWorkflowLinkVisual` 上方的注释：原先把类名当兜底是多余的、还把这条路径变成字符串类型化），
所以这条不再是「重命名演示里的连线视图类型会静默改变手势」的来源。路由的 `Target` 也用同一套 DataContext 判定（`ResolveTarget`）—— 见 L9。

**L9 · 四个自带手势的否决：组件自己路由这一笔按下，`PressRouted` 保证一笔只路由一次。**
这家没有隧道相，节点卡片与插槽比表面先跑，所以句柄只能由组件自己交回路由：`RouteComponentPress(control, component, e)` 的 `Target` 就是组件本身（`WorkflowSurfaceBehavior.RouteComponentPress`），节点拖动（`WorkflowNodeDragBehavior.cs` 的 `OnPointerPressed`）与插槽连接（`WorkflowSlotConnectionBehavior.cs` 的 `OnPointerPressed`）起手势前读它的 `PreventDefault`；表面平移那处先看 `SurfaceState.PressRouted`（`SurfaceState` 的字段），没被路由过就自己路由并置位（`OnPointerPressed`），Ctrl+滚轮同样路由（`OnSurfaceWheel` 的缩放分支）。**卡片内的 `ButtonBase` / `TextBox` / `RichEditBox` / `PasswordBox` / `ComboBox` / `ToggleSwitch` / `RangeBase` 不能起拖动**：它们自己要吃下这一笔；`WorkflowNodeDragBehavior.OnPointerPressed` 若仍 `CapturePointer` 并置 `Handled`，子控件收不到 Release，Compile/Run 等按钮表现为「点了没反应」。`IsInteractiveSource` 从 `OriginalSource` 向上找这些基类并直接返回。
**坑**：不能用 `e.Handled` 代替这个标记 —— 表面的按下处理器是 `handledEventsToo: true` 挂的（`Attach` 里的 `AddHandler`），无视 `Handled` 照跑；标记只由冒泡链上最后的连线处理器消费并清除（`OnLinkPointerPressed`），所以那个处理器必须保持 `handledEventsToo: true`。

---

## 三、刻意背离（与另外六家不一样，以及为什么）

**D1 · 画布平移用合成 `Translation`，不是共享 `TranslateTransform` 的模板绑定。**
WPF/Avalonia 的做法是「适配器设一个附着属性，节点与连线视图在 XAML 里把 `RenderTransform` 绑上去」
（`Examples/Workflow/WPF/Demo/Views/Workflow/WorkflowView.xaml`、`Examples/Workflow/Avalonia/Demo/Views/Workflow/WorkflowView.axaml` 的节点模板，
`Src/Templates/VeloxDev.WPF.Templates/working/content/workflow-tree-view/TemplateClass.xaml` 同）。
**这里的做法和其他家不一样，因为** WinUI 的子元素不做那个绑定（`WorkflowSurfaceBehavior.ApplyLayout` 的注释），
平移由画布自己的合成变换承担。
> ⚠ **本条带一处注释与代码不符，见 §四·P1。**

**D2 · 节点几何是「模板绑定 + 适配器命令式再写一遍」两套并存。**
示例模板里节点视图直接绑 `Canvas.Left/Top/ZIndex` 到 `Anchor`（`Examples/Workflow/WinUI/Demo/Views/Workflow/TreeView.xaml` 的 `NodeTemplate`），
同时 `ViewManager.ApplyLayout` 又命令式写一遍同样的三个附着属性、外加 `Width`/`Height`
（`ViewManager.ApplyLayout`）。WPF/Avalonia 的 `ViewManager` **只写 `Visibility` + `DataContext`**，
几何全交给模板（WPF/Avalonia 的 `ViewManager` 里 `AddOrReuseView` / `HideViewFor` / `ResetAllViews`）。
**这里的做法和其他家不一样，因为**这一家的池化宿主是挂在 `PART_Canvas` 上的 `behaviors:ViewPool`，
`ViewManager` 把模板视图**直接** `Children.Add` 到 Canvas（`ViewManager.AddOrReuseView`），几何因此也由适配器在挂树后亲手写一遍 ——
模板上那三条绑定与它并存、取同一个 `Anchor`，互为保险。
（这里不用 `CanvasItemsControl` 抄 Left/Top —— `Examples/Workflow/WinUI/Demo/Views/Workflow/CanvasItemsControl.cs` 不在源码树里，
别去找，它只剩 `obj/` 里的生成物。）
`Width/Height` 则是本家必需的（见 D3）。

**D3 · `Size` 为 `(0,0)` 时写 `Width/Height = double.NaN`，而不是 0。**
`ViewManager.ApplyLayout` 及注释：「还没测量过，用 NaN 让平台自己定尺寸」。
**这里的做法和其他家不一样，因为** WinUI 里 `Width = 0` 是「真的宽 0」，没有别家那种
「0 表示未设」的约定（`Layout.ActualSize` 的后续写入依赖这一点）。

**D4 · 缩放后**同步**重跑一次虚拟化，并强制三次 `UpdateLayout()`。**
`WorkflowSurfaceBehavior.OnSurfaceWheel`：`EnsureNegativeCover` 之后 `Canvas.UpdateLayout()` → `sv.UpdateLayout()`
→ `host.UpdateLayout()`，注释说 `ChangeView` 会按**陈旧的 extent** 夹取滚动位置；
紧接着 `VirtualizeAtScroll(...)`，世界原点那条分支也再跑一次。
**这里的做法和其他家不一样，因为**本家池化视图的几何更新排在低优先级（L6 的表），
不强制重排就会「池化视图滞后缩放约 100 ms」—— 那句现象写在 `skills/.../gui/winui.md` 的「Pooled views can lag a zoom by ~100 ms」那条。

**D5 · 网格装饰器**不在适配器里**，但它的虚拟化接线**在适配器里**。**
适配器目录里没有 `WorkflowGridDecorator`，装饰器是示例/模板产物
（`Examples/Workflow/WinUI/Demo/Views/Workflow/WorkflowGridDecorator.cs` 的 `WorkflowGridDecorator`）；
Razor 与 WinForms 则把它放进了适配器
（`Src/Adapters/VeloxDev.Razor/Attached/Workflow/WorkflowGridDecorator.razor.cs`、`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowGridDecorator.cs`）。
但契约要求的 `RulerBand → SetVirtualizeInset` 转发**由适配器做**
（`WorkflowSurfaceBehavior.UpdateGridDecorator`：`SetVirtualizeInset(left: decorator.RulerBand, top: decorator.RulerBand)`）。
**这里的做法和其他家不一样，因为**这一家按名字找装饰器、拿不到就不装 inset；
新接平台时不要因为「装饰器不归我」而把 inset 那两行一起搬出去。

**D6 · 小地图指针按下**总是重新居中**，没有抓取锚点。**
`WorkflowMinimapOverlay.OnPointerPressedHandler`，注释写「与 Jalium 家一致…」。

**D7 · 小地图推给重绘的视口尺寸取 `ActualWidth/ActualHeight`，不是 `ViewportWidth/Height`**
（本家小地图自身 resize 时读 `_scrollViewer.ActualWidth/Height`：`WorkflowMinimapOverlay.OnScrollViewerResized`；适配器把同一对值推给它：`WorkflowSurfaceBehavior.UpdateMinimapOverlay`）。
**这里的做法和其他家不一样，因为**本家小地图自己是画布元素、尺寸即视口尺寸，
读它比读契约属性少一次跨对象依赖。

---

## 三点五、画布滚动与表面级按键（2026-10-11，探针实测）

这家是**「没有预览相」到底要怎么接**的答案所在，结论与旧笔记相反：

- **挂滚动容器本身会漏，挂滚动容器的内容就不漏。** 没有隧道相时，`handledEventsToo` 也救不回来：`Handled` 在 `ScrollContentPresenter` 已经滚完之后才跑（实测一格漏 **74 DIP**，且改前若照旧挂容器，接管后会是 148 = 原生 + 自己那份）。改挂 `state.Canvas`（画布）之后，它在冒泡路径上**早于 presenter**，`Handled` 真的挡得住 —— 实测一格 **74**、四格 **296**，与基线逐字相同，**泄漏 0**。旧笔记那条「没有隧道相就得付先滚一丝的代价」**作废**（`wpf.md` §三·1 已同步）。
- **步长** 这家自己的 `ScrollViewer` 一格 **74 DIP**（线性：1 格 74、4 格 296）。
- **基线**：普通滚轮竖滚 74；**Shift+滚轮也竖滚**（这家原生忽略 Shift，与 WPF/Jalium/MAUI/WinForms 同；只有 Avalonia 与浏览器原生横滚）。
- **`VirtualKey` 实测**（反射）：`LeftShift=160 / RightShift=161 / LeftControl=162 / RightControl=163 / LeftMenu=164 / RightMenu=165 / LeftWindows=91 / RightWindows=92`，而 A–Z `65–90`、Number0–9 `48–57`、F1–F12 `112–123` ⇒ 八个修饰键都在算术区间外。⚠ **这家把 Alt 拼作 `Menu`**，所以是 `LeftMenu → LeftAlt`。
- **焦点**：这家没有可设的 `Focusable`，用 `host.Focus(FocusState.Pointer)`；按下就收焦点（挂在 `handledEventsToo` 的那个按下处理器顶端，空白画布/节点/插槽/连线都看得到；可编辑控件与按钮由 `OwnsItsFocus` 排除），`RoutePointer` 里那条「悬停连线才收焦点」原样保留。实测点空白画布后 Ctrl+Z 生效（`节点 4/4 → 3/3`），Ctrl+Y 回到 `4/4`。
- **键**：去掉 `HoveredLink is null` 门，target 改用 `input.PointerTarget`，Delete 仍是唯一被吞的键。
- **一处刻意保留的齐一性**：Ctrl 那一支里，订阅方若置 `PreventDefault` 就跳过缩放且**不置 `Handled`**，于是原生滚动仍可能发生 —— 这与 WPF/Avalonia 参考实现逐字相同，是为了「七家同形」而没有为这家特判。
- **宿主级按下处理器只处理画布区域**（2026-10-30 起）：完整 Demo 把侧栏与画布装在同一个 `UserControl` 里，而 `OnLinkPointerPressed` 挂在宿主上、`handledEventsToo`。它在按下时无条件 `host.Focus(FocusState.Pointer)`（为了让 Ctrl+Z 有落点），于是侧栏元素列表里的按钮在**真实慢点击**（按住 100–150ms）时会失焦：焦点跳到画布里的 SEED `TextBox`，按钮的 `Click` 不落地 —— 表现为「点 Locate 只让输入栏获得焦点、HUD 不动」。同一刻 down+up 的合成点击不会触发这个竞态。现在处理器开头用 `IsWithinScrollViewer(e.OriginalSource, state.ScrollViewer)` 判门：不在画布滚动容器里的按下直接 return，焦点与路由都留给侧栏自己；画布/节点/插槽/连线的按下（都在容器内）不受影响。
  **卡片内的按钮过不了那道门，要另一条判据**（2026-10-30 起）：节点卡上的 Compile/Run/Stop/Close 本来就在滚动容器里，`IsWithinScrollViewer` 放行后照样被 `surfaceViewer.Focus(FocusState.Pointer)` 收走焦点，`Button.Click` 不落地 —— 真实鼠标点 Compile 后 `Run` 永不高亮、节点不出现 `#N`；同一按钮用 UI Automation 的 `Invoke` 却能编译（`Invoke` 不走指针与焦点，正好是判别这两种原因的探针）。现在焦点判断是 `WorkflowSurfaceBehavior.OwnsItsFocus`：从 `OriginalSource` 向上找 `TextBox`/`RichEditBox`/`PasswordBox`/`ButtonBase`/`ComboBox`/`ToggleSwitch`/`RangeBase`，命中就跳过 Focus；空白画布、卡片面、插槽、连线仍收焦点。
- **视图池按类型复用，TwoWay 选择器会把复位写回新节点**（2026-10-30 起）：`ViewManager.HideViewFor` 池化时把 `DataContext` 置空、`AddOrReuseView` 复用时再赋新 VM；ComboBox 的 `SelectedItem` 在 `ItemsSource` 换引用（`EnumValues`/`CompileModeOptions` 每次 getter 都是新数组）时复位并回写 null —— 绑到非空枚举会变成 `default` = `Static`，绑到 `SelectedValue` 直接清空。表现：Enum Selector 卡滚出再滚回视口后 COMPILE MODE 自己变 Static、SELECTED METHOD 变空；此后再编译，Static 拿不到分支键，整条下游 `Order = -1` 不执行（节点仍拿得到 `#N`，所以只看徽标会以为编译正常）。修法在共享 VM `EnumSelectorNodeViewModel`：`SelectedValue` 忽略 null 并重推；`CompileMode` 的 UI 面走字符串 `CompileModeName`/`CompileModeNames`（WinUI 的 `EnumSelectorNodeView.xaml`），空值同样忽略。WPF 的 `VirtualizingStackPanel` 也是 `Recycling`，同一条判据对它可能成立，但未实测。
  **焦点不能再落在宿主上**（同一次修复）：`host.Focus(FocusState.Pointer)` 会沿焦点链落到节点卡的第一个 `TextBox`（完整 Demo 的 SEED 输入框），`ScrollViewer` 随后 `BringIntoView` 把它滚进视口 —— 这就是「Locate 几下再进画布 / 大幅度拖动后画布闪回」的另一半根因。现在把 `PART_ScrollViewer.IsTabStop = true`（`ResolveNamedControls`）并把两处 `host.Focus` 改成 `viewer.Focus(FocusState.Pointer)`（按下处理器与 `RoutePointer` 的悬停连线收焦点）；键仍从滚动容器冒泡到宿主的 KeyDown。Trimmed Demo 没有可聚焦的节点输入，因此从来看不到这一条。
- **滚动写入只有一个入口，读位置只认逻辑目标**（2026-10-30 起）：`RequestScroll(state, x, y)` 先记 `PendingScrollX/Y`（NaN = 该轴在途）→ `ApplyLayout` 用 `ActualOffset − (pending − 实际滚动)` 写 `Canvas.Translation` → 再 `ChangeView`。`OnViewChanged` 落地后清 pending，Translate 回到 `ActualOffset`；`OnDataContextChanged`、滚动容器换实例、目标落到可滚范围外都会清。所有「读当前位置」的地方（平移起点、`ToCanvasLocalAnchor`、可见区、grid/minimap、滚轮/缩放起点）都走 `EffectiveScrollX/Y`，绝不能在 ChangeView 未落地时读 `HorizontalOffset`。**理由**：ChangeView 异步，负向扩画布又同时改 `ActualOffset`（合成变换当帧生效），不补偿就会「内容先平移、再被滚动拉回」——这就是非 Trimmed 大幅拖动/Locate 后的闪回；补偿让「逻辑滚动位置」成为唯一真相，落的滚只是为了追上来。小地图的拖拽导航也必须走同一个入口（`WorkflowSurfaceBehavior.TryRequestScroll(this, x, y)`）；它若自己 `ChangeView`，会绕过在途目标，把画面钉在旧位置。在途目标另有一个 **150ms 的一次性定时器**兜底：ChangeView 被夹取/被忽略、或用户在途中自己拖了滚动条时，定时器清掉补偿并 `Refresh`，避免画面永远挂在到不了的位置。
- **平移越过画布边缘时，先让布局认下新 extent，再读 `ExtentWidth/Height`**（2026-10-29 起）：`ClampScrollOffset` 只改模型（`Negative/PositiveOffset` → `ActualSize`/`ActualOffset`），`ScrollViewer` 的范围要到下一次布局才更新。`OnCanvasPanMoved` 原在 `ApplyLayout` 后立刻读 max 并 `ChangeView`，读到的是旧范围，越界那一截被夹短，松手后画面停在旧边界（非 Trimmed Demo 的 3600×1000 画布最容易碰到；Trimmed 画布大，普通拖动到不了边）。修法与 `CenterOn` 同形：`ApplyLayout` → `Canvas.UpdateLayout()` → `ScrollViewer.UpdateLayout()` → `host.UpdateLayout()` → 再读 max。滚轮与定位那两路本来就有这套三连。

### 三点六、「内外一起滚」的 A/B 实测（2026-10-11 探针）

指针压在卡片内层可滚区上滚一格，两组对照：

| | 内层 `ScrollViewer` | 画布 |
|---|---|---|
| **有拦截**（默认适配器） | V `0 → 30` | V `0 → 74` |
| **bypass**（适配器在路由前 return） | V `0 → 30` | **完全不动**（无 `OUTER-VIEW`） |

入口那行是硬证据：**`handledEnter=True`** —— 内层 `ScrollViewer` 早已滚过并置了 `Handled`，而 `handledEventsToo` 让我们照样跑到，再**无条件** `e.Handled=true` + `ScrollBy(0, delta)`。

⇒ **外层的 74 是适配器额外加上去的那一滚**，缺陷 ① 的根因就是那条无条件 `ScrollBy`（叠在内层自己的滚动之上）。微软文档「滚轮按惯性操纵处理、链式不生效」在本例**成立** —— bypass 组外层 P 不动，正是因为内层处理完不会去驱动外层。

同批实测的两条附带结论：

- ⚠ **默认 demo 数据下卡片全都不溢出**（每张卡 `scrollableH=0`）⇒ 缺陷 ① 在默认数据下**不可见**，验它必须先灌数据（WPF 与 WinUI 两家独立实测一致）。
- **「两轴齐写」没有观测到副作用**：先把 H 平移到 60 再竖滚一格，H **精确保持 60**，没有出现「未动那轴被陈旧 max 夹一下」。⇒ 代码确实违背 `IWorkflowSurfaceScroller` 文档的「零增量不动那一轴」，但**症状未复现** —— 修它的理由写「违反已写下的契约 + 多余写回」，不要写「它导致抖动」。
- **真正的横向/倾斜滚轮**：2026-10-11 已可测（`agent-ui-harness.ps1` 新增 `hwheel:` 动作 = `MOUSEEVENTF_HWHEEL`），且在这家**已测**（见 §三点七）。

### 三点七、闸门的判据在本家必须「叠加」—— 实测把问法定死了（2026-10-11）

这家与 Jalium/Avalonia 的处理器挂点**不同**：它挂在**画布**上、带 `handledEventsToo`，所以轮到我们时**内层 `ScrollViewer` 已经跑过了**。「内层还有没有余量」在这种时序下是**错**的问法 —— 而「只判入口 `e.Handled`」也是错的：

**实测：内层到边那一格照样置 `Handled`。**
```
WHEEL handled=True delta=-120 | INNER off=(0,12) max=(0,12)   ← 到边，off 一动不动，仍 True
WHEEL handled=True delta=-120 | INNER off=(0,12) max=(0,12)   ← 仍然 True
```
⇒ **`if (e.Handled) return;` 单独不够**（会吞掉到边那一格，画布再也不接）。

**成立的判据是叠加**：内层已经跑过，入口读到的 offset 是它滚完之后的；**只要它沿这一笔方向仍有余量，这一笔就确实归它**（它已消费）；只有它停在边界（`max` 或 `0`）时才交回画布 —— 那时它确实没动，不会重复滚。

**A/B 实测**（指针压卡片输入口条）：

| | 内层 | 画布 `视口(画布)` |
|---|---|---|
| 改前 | `off 0→9→18→27` | **同时** `0→74→148→222`（内外一起滚） |
| 改后·内层有余量 | `off` 继续动 | **恒 `0,0`，完全不动** |
| 改后·内层到边那格 | `off` 停在 max | `0,74`，此后每格 +74 |

**「谁滚的」**：这家**没有原生滚动链**，所以每一格画布滚动都必然是适配器自己的 `ScrollBy`（trace 里 `OUTER` 逐格 +74 只出现在闸门返回 false 的那几格，与内层余量一一对应）。

**边缘**：改前滚 10 格后卡在 `546.67`（画布 `3600×1000`）；改后第 8 格起撑大 ⇒ `3600×1150`（+150 = 0.15×1000）。中部步长仍是 **74**（3 格 = 222，不是 148 —— 双重滚动没有回来）。

**N2（`DeltaX` 硬编码 0）已修且已验**（用新的 `hwheel:` 动作 A/B）：改前 `hwheel` 两次 ⇒ 画布**竖**滚 `V 0→74→148`、`H=0`；改后 ⇒ `H 0→74→148`、`V=0`。`Ctrl+横滚` 未测（新代码让它不进缩放分支、当普通横滚处理 —— 这是设计选择，非实测结论）。

⚠ **附带观察**：卡片内层 `ScrollViewer` 的滚轮步长**不是常量** —— 视口 `vp=60` 时实测 **9 DIP/格**，而先前探针在高视口下量到 **30/格**。别把「内层一格走多少」当成固定值去对账。

## 四、坑（改这里时最容易踩的，附依据）

**P1 · `WorkflowCanvasTransformBehavior` 的 `OnTransformChanged` 注释与代码不符 —— 这是本家最该抓的一条。**
注释写「节点与连线视图在 XAML 里把自己的 `RenderTransform` 绑到这个附着属性」，
但全仓库搜 `WorkflowCanvasTransformBehavior`：WinUI 侧**只有** `WorkflowSurfaceBehavior.ApplyLayout` 自己经 `Apply` 写它，
没有任何 WinUI 的 `.xaml`（适配器、模板、两个 demo 都算）读过它；
真正那样绑的只有 WPF/Avalonia 的 demo 与 WPF 模板（见 §三·D1）。
这句注释显然是从 WPF 版本抄过来的。**后果**：下一个 agent 读它就会去模板里找那个绑定、找不到，
或者「补上」这个绑定，从而与 `Canvas.Translation` 的平移**叠加**成双倍位移。
`OnTransformChanged` 是故意空的（自陈「只是通知载体，宿主本身不能收渲染变换」），这一半是对的。

**P2 · `ViewManager.SubscribeToLayoutChanges` 的延迟闭包不重校验自己还是当前状态。**
它在跨线程时 `TryEnqueue(DispatcherQueuePriority.Low, () => ApplyLayout(view, viewModel))`，
闭包捕获 `view`/`viewModel` 后**没有**确认这一对还在活跃。
对照 `WorkflowSurfaceBehavior.UpdateVisibleRegion`，那里排 Low 之前与落地之后都做了 `ReferenceEquals` 重校验。
**后果**：一帧内视图被回收并复用给另一个 ViewModel 时，旧几何会写到新宿主上 —— 且不报错。
改这里时照 L6 的表补一条重校验。

**P3 · 连线视图的盒子绝不能绑画布尺寸；完整版现在用「每条线的包围盒」。**
完整版曾经把 `PolylineCurveView` 的 `Width/Height` 绑到画布 `ActualWidth/ActualHeight`（`TreeView.xaml` 的 `LinkTemplate`），
于是曲线伸出画布（节点的锚点/尺寸不必落在 `OriginSize` 内）或缩放/长大的一帧里尺寸落后时，线被裁在画布边
——2026-10-30 修：`PolylineCurveView.RefreshGeometry` 取四次贝塞尔四个点的 min/max（加 `BoxPadding` 覆盖光晕/圆头），
把视图 `Width/Height` 设为这个包围盒、`RenderTransform` 平移回包围盒原点，几何与合成线段都写成「原始坐标 − 原点」；
元素和 `ShapeVisual` 的 Size 因此都装得下整条线，且与画布尺寸无关。模板里的 `Width/Height` 绑定已删除。
**另一条腿**：Trimmed 的 `LinkView.UpdatePath` 用 offset frame（`Canvas.SetLeft(−ActualOffset)`、宽高 `ActualSize`、几何烘 `+offset`），
适用于「画布尺寸能覆盖内容」的模板。两条路都行，共同点是**不要按画布尺寸裁线**；skill `gui/winui.md` 的
「Do not bind the link view's `Width`/`Height` to the canvas element.」仍成立。

**P4 · `Sync` 的 `Syncing` 守卫是 try/finally —— 这是有意的，别简化成 try/catch。**
`WorkflowSlotLayoutBehavior.Sync`，注释就在 `state.Syncing = true` 上方：`TransformToVisual` 抛异常时不能让守卫留在
已置位状态，否则槽位同步**永久停止且不报错**。同族的 `SyncSlotEnumerator` 用 `ActualWidth <= 0`
判断「还没测量」并排 Low 重试 —— 这两个守卫一起保证「测量失败 ≠ 停止同步」。

**P5 · 名称解析失败是静默的。** `FindName` 找不到就返回 `null`，行为退化成「没有缩放 / 没有小地图」，
不抛异常（`ResolveNamedControls`）。所以改模板布局后一定要调 `Refresh(UserControl)`（`Refresh`）。

**P6 · 小地图在非 UI 线程构造 = 永不重绘且不报错**（§二·L7）。定时器建不出来时没有任何补偿路径。

**P8 · 入队被拒 = 永久失效，因为旗标是在回调里清的**（2026-09-27 修了两处）。`ViewManager.ScheduleNextBatchRender`
与 `WorkflowSurfaceBehavior.UpdateVisibleRegion` 都是「先置旗标 → `TryEnqueue` → 在回调里清旗标」，
而后者的返回值被丢掉了：队列一旦拒绝（正在关闭），回调永远不跑 ⇒ 旗标永远立着 ⇒ 之后每次调用都在第一行返回
⇒ **画布一个视图都不会再加**。**症状很好认：小地图照旧画（它读的是模型，不是这批视图），画布上节点/连线却没了。**
现在「没有入队成功就立刻复位旗标 + `Debug.WriteLine`」。同族还有两处同样丢返回值、但没有旗标（
`WorkflowSlotLayoutBehavior.ScheduleSync` 与 `SyncSlotEnumerator` 的重试入队、`WorkflowMinimapOverlay.OnScrollViewerResized`）：它们只丢一次更新，下一次事件会补上。
**这条与本文件 P4 是同一个教训**（「已置位状态……永久停止且不报错」），该家自己的正确范形在 `SyncSlotEnumerator`：
用可判定的条件（`ActualWidth <= 0`）判断「还没测量」并**排 Low 重试**。

**这一症状的成因在 Core 的空间索引，不在本家**：**只要 Agent 对话进行中，节点/连线就可能概率消失**，
可见元素数量也未必实时同步，且**重入 Viewport 也救不回来**。
`Insert` 时边界为空的条目只登记、不进网格，之后只靠 `PropertyChanged`
补进去，而那个事件可能压根不来（视图没测量）或正好落在别人那一趟里被延后 ⇒ 它**不在任何格子里** ⇒ 任何视口变化都
查不到它（「救不回来」这句正是这条的指纹）。修在查询侧（`SpatialGridHashMap.Query` 先 `EnsureIndexed()`），见
[`WorkflowSystem/architecture.md`](../architecture.md) §3.5 那条新规则与判别测试 `SpatialIndexFreshnessTests`。
P8 那两处修复**仍然成立**（确实存在的一类永久失效），只是不是这一幕的原因。

**教训（写给下一个遇到同类报告的人）**：症状说「消失」时，先分辨**更新丢了**（会随下一次事件恢复）与**条目不可达**
（任何操作都救不回来）—— 后者说明索引/容器里根本没有它，要往「谁负责把它放进去、那一步什么时候会跑」查，而不是
往刷新频率或优先级查。当时我按「被饿着的 Low 批次」查了两轮，方向就错了。

**P7 · 空白处按下的判定现在只对 `ScrollContentPresenter` 一个类型名字符串敏感**（§二·L8）。节点/插槽/连线都走 `DataContext` 契约类型，
演示重命名连线视图类型（`BezierCurveView`/`PolylineCurveView`）不再影响手势归属。

**P8 · 保留模式下「写进去的几何 = 画出来的东西」，所以连线视图必须在每个入口都重算一遍渲染状态。**
本家没有 `OnRender`，`PolylineCurveView` 的整幅画面由两类元素承载：3 条静息线各是一个 `Path`
（`BezierSegment`），彗星的 24 段（16 条尾 + 8 条光晕）已搬进合成层、是 `CompositionSpriteShape`
（见 §四·P9）—— 它们的几何只由「依赖属性变更」这一个入口驱动重写。
Avalonia/WPF/Jalium 的同一份视图是每帧重算的（`Render`/`OnRender`），所以那边
`_length <= 0` 这类守卫即使被 NaN 穿过，最多错一帧；**这边错的是永远** —— 写进去什么就一直画什么。
同一个视图还有两个不触发 `Loaded`/`Unloaded` 的入口必须自己挂：`DataContextChanged`
（池化视图改绑/回收，`ViewManager.HideViewFor` 只改 `Visibility` + `DataContext`，从不摘树 —— 见 §六），
以及「本帧没有属性变化」这件事本身。
两条硬规则：**几何里不许出现 NaN**（未测量的 slot 锚点就是 NaN，`WorkflowSlotUpdateGate` 的约定；
NaN 参与的比较全是 false，`_length <= 0`、`lo >= _length` 这类守卫一条都拦不住，而 NaN 点画不出任何东西）；
**「停周期」不能只挂在 `Unloaded` 上**（池化视图永远等不到它，旧链接的 `Transition` 会一直按帧写那 24 条合成段，
这正是「整体有一点点不流畅」的来源）。
改这一份视图时按这两条自查：`Examples/Workflow/WinUI/Demo/Views/Workflow/PolylineCurveView.xaml.cs` 的
`Refresh()` 是几何/显隐/周期重算的入口（颜色/粗细/高亮走 `OnChanged` → `UpdateRestingLine` + `RedrawComet`，不经过它），
`RenderReady` 是唯一门。参照写法在 Trimmed 的
`Examples/Workflow/WinUI Trimmed/Demo/Views/Workflow/LinkView.xaml.cs`：它挂了 `DataContextChanged`
（订阅在构造函数、处理器是 `OnDataContextChanged`，注释明写池化复用与「hide 会先给一个 null 的 DataContext」），
发布曲线时也现算同一个 `CanRender && 四端点非 NaN` 合取（`PublishCurve`）。
**`SlotView` 核过了：确实漏，且至今未修。** 它就是上面「停周期不能只挂在 `Unloaded` 上」的第二个受害者 ——
非 Trimmed 的 `Examples/Workflow/WinUI/Demo/Views/Workflow/SlotView.xaml.cs` 在 `Loaded` 里起波纹周期、
只在 `Unloaded` 里停，而池化节点视图永远等不到 `Unloaded` ⇒ **每个曾物化过的节点都把它的端口波纹一直画下去**
（每帧约 16 次属性写入，写进一个 `Collapsed` 的元素；每个 SlotView 还各占一个 60 fps 的 `DispatcherQueueTimer`）。
Trimmed 的 `SlotView` 与 `Src/Templates/VeloxDev.WinUI.Templates/` 的那份**都没有周期**，所以这一条只属于非 Trimmed 这个 demo。

**这一条仍然开着，而且真正该修的不是「周期没停」，是「每帧写布局属性」。** 2026-10-01 实测（见下节）把这条
从「怀疑」推到了「确诊」，但确诊的东西不是泄漏本身 —— 见下面「每帧写布局属性」那一节。

**「端口自己被折叠」那一路不用单独管**：`TimerNodeView` / `EnumSelectorNodeView` 的端口用
`Visibility="{Binding DataContext.HasInputSlot, ElementName=Root, …}"` 收起，而 `HasInputSlot => _inputSlot is not null`
（`TimerNodeViewModel.HasInputSlot`、`EnumSelectorNodeViewModel.HasInputSlot`）—— 折叠与 `InputSlot is null` 是同一件事。
**祖辈（节点视图）被折叠对子元素完全不可观察**：`Visibility` 不继承、`IsLoaded` 仍为 true、
`EffectiveViewportChanged` 在祖辈 `Collapsed` 时根本不触发 —— 所以「被回收」这件事只有 `DataContext` 一个入口。

### P9 · 每帧写「布局属性」＝ 每帧让整块画布重排一次（2026-10-01 实测）

**这是这一家所有「卡顿」报告的第一嫌疑人，先量它再谈别的。** XAML 里改 `Shape` 的几何（`PathGeometry`
的点）或 `Width/Height`/`StrokeThickness`，会让该元素的**测量**失效，而失效会一路向上冒到画布
⇒ 整个 `PART_Canvas` 重排一次。视图是不是喂给池、是不是 `Collapsed`，都拦不住 —— 只要它还挂在树上。

**实测方法**（需要时照这个重建探针）：`PART_Canvas.LayoutUpdated` 计数 + 一个 1s 的
`DispatcherQueueTimer` 把计数写进 `%TEMP%` 的日志文件；demo 里两处动画各加一行计数。
用文件而不是 `Debug.WriteLine`，是为了能在**不挂调试器**的情况下启动、跑十几秒、再杀掉读文件。

**实测结果（空闲、鼠标不动，画布重排次数/秒）：**

| 状态 | 重排/秒 |
|---|---|
| 原始（两条动画都跑） | ~60 |
| 只关彗星 | ~60（分量来自 SlotView） |
| 只关波纹 | ~60（分量来自彗星） |
| 两条都关 | **0** |

⇒ 两条动画**各自单独**就足以把画布按帧重排。**每一个几何重画换一次整块画布的重排**，所以彗星那条的
曲线是：FPS 60 → ~65、30 → ~75、15 → ~65、2 → ~6、1 → ~4、关掉 → 0（高 FPS 段被帧率钉在 ~60 天花板）。

**改法（两处，都已落地）：**

1. **`SlotView`：`Width/Height` → `ScaleTransform`。** 官方那份性能指南点名的就是这一条
   （「animate `ScaleTransform.ScaleX`/`ScaleY` instead of the `Width` and `Height` of an object」）。
   四个圆按基准尺寸建一次，每帧只写 `ScaleX/ScaleY` 与 `Opacity` —— 都在「独立动画」清单上。
   **实测：~930 次/秒的端口重画，重排从 60–108/s 降到 0。**
2. **`PolylineCurveView` 的彗星：从 XAML `Path` 搬到合成层。** 24 段改成
   `ShapeVisual` + 24 个 `CompositionSpriteShape`，几何用 `CompositionLineGeometry` 的
   `Start`/`End`（每帧写两个 `Vector2`）。**合成对象不参与 XAML 布局**，所以这条路径一次重排都不会有。
   实测：两条动画全开、彗星仍以 60 fps 重画，**重排 0–3/s**。

**搬运时踩到/需要知道的四件事：**

- **`CompositionPathGeometry` + `TrimStart`/`TrimEnd` 在这条路上走不通**，别照着别人 UWP 的写法直接抄：
  它要 `CompositionPath`，而 `CompositionPath` 要一个 `IGeometrySource2D` —— C# 里只有 Win2D 的
  `CanvasGeometry` 能提供，不引 Win2D 就得写一个实现 `IGeometrySource2DInterop` 的 C++/WinRT 类。
  **`CompositionLineGeometry` 是纯 C# 可达的那一个**，而且它和原来的画法一一对应（原来那 24 段本来
  就是直线段）。代价是每段只有一条直线（原来是「起-中-末」三点），段足够短时看不出差别。
- **子视觉与 XAML 内容谁在上不好赌**：多插一层什么都不画的末位 `Grid` 当载体，用它的子视觉把顺序钉死。
- **合成层里没有 `Visibility`**（那是布局概念）。出窗的段改成把画刷 `Color` 写成透明。
- **`ShapeVisual.Size` 要跟着视图尺寸走**，否则线段会被裁掉；`SizeChanged` 里同步一次。

**由此得到的通用判据**：这一家任何「每帧都在动」的东西，先问一句「它写的是不是布局属性」。
是 → 它在按帧重排整个画布，任何微优化都救不了（实测过：把 24 次恒定 `StrokeThickness` 写入挪出逐帧路径
不减重排；把每条 `Path` 的 Width/Height 钉死也不减 —— `Shape` 只要几何变了就无条件失效测量）。
不是 → 它可能是免费的（`SlotView` 改完之后就是这样）。

**`PolylineCurveView` 不再自己跑周期**：WPF / Avalonia / WinUI 共用 `Examples/Workflow/Common/Lib` 的
`WorkflowFlowClock`，每棵树一条 30fps 的周期，所有 LinkView 订同一个 `LateUpdate`；视图 tick 只把
`BandHead` / `BandIntensity` 抄进字段并 `RedrawComet()` 一次。这样一棵树无论多少链接都只有一个帧调度器，
且两个标量在同一帧一起落地。动画仍保留两个标量，与另外六家一致。

**教训（写给下一个拿到「卡顿」报告的人）**：这一轮前面三次全是**没有一次实测**的推断，方向全错，
其中一次还把 demo 改得更卡。官方那套「先量基线 → 选一项改 → 再量」在这里不是流程洁癖：
症状是「延迟」时**先数一遍『现在有多少东西每帧在写布局属性』**，一个探针十几行、一次运行十几秒，
比再回退一版便宜得多。

**P10 · `OnLinkKeyDown` 现在只吞 `Delete`，但别把它读成「画布不再丢键盘」。** 先前无条件 `e.Handled = true`
（指针停在连线上时方向键、翻页键、空格一并被吞），2026-10-06 收窄成只在 `Windows.System.VirtualKey.Delete` 上置
（`WorkflowSurfaceBehavior.OnLinkKeyDown`，置位在 `e.Key == Windows.System.VirtualKey.Delete`；与 Avalonia 的「非 Delete 直接 `return`」
（`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/WorkflowSurfaceBehavior.cs` 的 `OnLinkKeyDown`）、Jalium 的「只吞 Delete」同形）。
**实测（悬停连线上按 PageDown）**：改前与改后视口都从 `0,0` 滚到 `0,437` —— `ScrollViewer` 是宿主的**下代**，
冒泡相里它先处理，宿主再置 `Handled` 追不上它；被吞的从来只是继续往**宿主之上**（窗口级处理器）的那一段。
所以这是一笔对齐（不再替宿主的窗口级键盘做决定），**不是**键盘不可用的修复。

---

## 五、连线交互落点（命中在 Core，右键菜单接线在适配器）

**2026-10-03 起右键菜单的接线不再在表面 code-behind，而在适配器 `WorkflowSurfaceBehavior` 的
`WireLinkMenu` / `ShowLinkMenu` / `UnwireLinkMenu`（挂附着属性 `LinkMenuKey`）** —— 模板与两个 demo 的
code-behind 因此只剩 `InitializeComponent()`（Trimmed / 模板）或自身设置（非 Trimmed），
菜单条目仍由用户在该表面的 `<UserControl.Resources>` 里声明。下面的描述据此写成。

**命中在 Core，不在元素上。** 连线视图本身**整块不可命中**：`PolylineCurveView` 在
`UpdateInteractivity` 里恒置 `IsHitTestVisible = false`，三条静息线各自的 `Path` 也恒为 `false`（`RestingStroke` 那处 `IsHitTestVisible = false`）—— 理由自陈在类里与 `UpdateInteractivity` 上方的注释：它是整块画布大小，
一旦可命中就会把画布手势全吃掉。判定改由视图把画出的曲线**发布**给 Core
（`_boundLink?.PublishCurve(RenderReady ? _curve : null, this)`，`PublishCurve`），适配器 `RoutePointer` 先按 `ResolveTarget` 从来源视觉的 DataContext 认节点/插槽（路由的 `Target` 已不只连线，见 §二·L9），都不认才落到 `tree.HitTestVisibleLinks` 做几何判定（`Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowSurfaceBehavior.cs` 的 `RoutePointer`、`ResolveTarget`），
半径 `LinkHitTestEx.DefaultHitRadius = 6d`（`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Interaction/LinkHitTestEx.cs` 的 `DefaultHitRadius`，
画布单位、**不随缩放放大**，见 `HitTest` 的 remarks）。带宽因此 ≈±6px —— 与「把 halo 描边设成可命中」那套的 ±5.5px
同量级，但机制完全不同。**输入仍要够得着宿主**：`PART_Canvas` 自身 `Background="Transparent"`
（`TreeView.xaml` 的 `PART_Canvas`）提供元素级命中面，画布手势与连线判定都靠它把指针事件冒泡到 `UserControl`；
行为侧 `PointerMoved += OnPointerMoved`（`WorkflowSurfaceBehavior.Attach`）、按下走
`AddHandler(..., handledEventsToo: true)`（`Attach` 里的 `PointerPressedEvent` 注册）。

| 事 | 代码落点 | 依据 |
|---|---|---|
| 命中 | Core 几何判定 `HitTestVisibleLinks`（半径 6，跳过橡皮筋与被节点卡盖住的线）；视图只发布曲线、全程不可命中 | `WorkflowSurfaceBehavior` 的 `RoutePointer` / `ResolveTarget`、`LinkHitTestEx.HitTest` / `HitTestVisibleLinks`；`PolylineCurveView.xaml.cs` 的 `UpdateInteractivity` / `PublishCurve` |
| 选中即取焦点 | 归适配器：`OnPointerMoved`（经 `RoutePointer`）／`OnLinkPointerPressed` 里各 `Focus(FocusState.Pointer)` 一次（悬停到线上即取，不用先点一下） | `WorkflowSurfaceBehavior.RoutePointer` 的 `host.Focus(FocusState.Pointer)`、`OnLinkPointerPressed` 里同一次调用 |
| 右键菜单 | **接线在适配器 `WorkflowSurfaceBehavior.WireLinkMenu`**：表面按 `LinkMenuKey` 解析 `x:Key="LinkContextMenu"` 资源 `MenuFlyout`、订输入面 `Input.PointerPressed` 与树的 `LinkRemoved`；弹出时画布坐标转锚点坐标、逐条喂 `DataContext`、`ShowAt(PART_SurfaceBorder, …)`，`Opened`/`Closed` 订在 `MenuFlyout` 上报回输入路由（置/放 `IsSuspended`）。**菜单指着的那条线一离开 `tree.Links`（Agent、Undo、别处删都算）树就报 `LinkRemoved`，表面只做 `state.LinkMenu?.Hide()` 收自己的弹窗，靠 `ReferenceEquals(state.MenuLink, link)` 认领** | `WorkflowSurfaceBehavior` 的 `WireLinkMenu` / `ShowLinkMenu` / `UnwireLinkMenu` / `FindResource`；资源仍在 XAML：`TreeView.xaml`（非 Trimmed / Trimmed）与模板的 `MenuFlyout x:Key="LinkContextMenu"` |
| 删除 | 菜单项 `Command="{Binding DeleteCommand}"`（条目 DataContext 由适配器在 `ShowAt` 前逐条喂成被按的连线）；Delete 键由**宿主**执行（库只把 `KeyDown` 路由过来） | `TreeView.xaml` 的 `MenuFlyoutItem`；`WorkflowSurfaceBehavior.OnLinkKeyDown`；`Examples/Workflow/WinUI/Demo/Views/Workflow/TreeView.xaml.cs` 的 `KeyDown` 处理器 |

四条要记住的：

1. **视图整块不可命中是有意的，别再给它加元素级命中面。** 给连线视图加 `Background` 仍然是错的：它是**整块画布大小**（§四·P3），加了背景就会把画布平移整个吃掉 —— 这正是现在恒置 `IsHitTestVisible = false` 的原因（理由自陈在 `UpdateInteractivity` 上方的注释）。`SlotView.xaml` 那句「命中面是控件自己的 `Background`」是给**小控件**的规矩，别照抄到连线视图上；命中落在 Core 的曲线判定上。
2. **元素命中那条路的坑（现已被 Core 几何判定取代）**：根 `UserControl` 没有 `Background` 时，画出来的内容全是 `Path` 且逐个 `IsHitTestVisible = false`，容器 `Grid` 也无背景 ⇒ 元素命中面为空，`PointerEntered`/`PointerMoved`/`RightTapped` 一个都不会派发。**实测（2026-09-26，SendInput + 闭环伺服取点）**：指针停在线体正上方（48×48 邻域内体色像素 120–186）线体仍是静息青色；从窗口外跳进来再压线体也没有高亮（`PointerEntered` 是无条件置高亮的，所以没高亮就是没派发）；同一窗口里空画布左键拖动照常平移 —— 窗口收得到输入，是**这个视图**收不到。判别法仍有用：**一旦又回到元素命中，「窗口收得到、这个视图收不到」照旧成立。**
3. **右键入口是表面的 `PointerPressed`（右键），不是 `RightTapped`**：`WorkflowSurfaceBehavior` 冒泡转发按下（`AddHandler(PointerPressedEvent, handledEventsToo: true)`，`Attach`），`ShowLinkMenu` 订在输入面的 `Input.PointerPressed` 上、判 `e.Button == Right` 与 `e.Target is IWorkflowLinkViewModel`。**否决菜单在链上更靠前的一级（连线自己）**：在同一条 `Input.PointerPressed` 上置 `e.Handle.PreventDefault`，`ShowLinkMenu` 读它（`WorkflowSurfaceBehavior.ShowLinkMenu`）。
4. **WinUI 特有的四件（现在都落在适配器里）**：(a) **`MenuFlyout` 继承 `FlyoutBase` → `DependencyObject`，不是 `FrameworkElement`，因而没有 `DataContext`** —— 资源里的菜单不在可视树上，绑定拿不到上下文，所以 `ShowLinkMenu` 在 `ShowAt` 之前**逐条**把这条连线喂给条目（`foreach (var item in state.LinkMenu.Items) if (item is FrameworkElement element) element.DataContext = e.Link;`，`WorkflowSurfaceBehavior.ShowLinkMenu`）；用户只要在 `MenuFlyoutItem` 上写 `Command="{Binding …}"` 就行（`TreeView.xaml` 的 `MenuFlyoutItem`）—— **条目绑命令**。(b) **资源 `MenuFlyout` 仍要自己给 `XamlRoot`**：它不属于任何元素，只有 `ShowAt(element)` 的 `element` 在树上，所以在 `ShowAt` 之前写一次 `state.LinkMenu.XamlRoot = host.XamlRoot`（`ShowLinkMenu`）；`MenuFlyout.Closed` 是**唯一的收起通知**，订在 `WireLinkMenu` 里报 `Closed`。(c) **弹窗的寿命由树的 `LinkRemoved` 判、表面只收自己的弹窗**：菜单指着的那条线一离开 `tree.Links`（Agent、Undo、别处删都算），树就报 `LinkRemoved`；表面只在 `ReferenceEquals(state.MenuLink, link)` 时对自己的 `MenuFlyout` 调 `Hide()`（`WireLinkMenu` 里的 `MenuLinkRemoved` 闭包），收起照常走上面那条 `Closed` 报回输入路由、`IsSuspended` 由此放开。**「树」与「打开的菜单」同时知道的只有表面，所以「该不该收」由表面按 `MenuLink` 比对判。** (d) **WinUI 没有 `TryFindResource`**：`FindResource` 从宿主沿父链逐级查 `Resources`、最后落到 `Application.Current.Resources`（`WorkflowSurfaceBehavior.FindResource`）；菜单资源声明在该表面自己的 `<UserControl.Resources>` 里，第一站就是宿主本身，所以这条查找对模板产物同样成立。

**这家没有「悬停取焦点 ⇒ 画布跳一段」这条代价**（即 Avalonia/WPF 那个 `ScrollViewer.BringIntoViewOnFocusChange` 症状）。焦点现在由适配器给：悬停到线上时 `OnPointerMoved`（经 `RoutePointer`）对宿主 `UserControl` 做一次 `Focus(FocusState.Pointer)`（`WorkflowSurfaceBehavior.RoutePointer`；按下时在 `OnLinkPointerPressed` 再来一次）。**实测（2026-09-26）**：把视口滚到非零偏移（`视口(画布) 1998, 421`）后，让指针**走**到线上（伺服逐步逼近）→ 同一点由体色（105 像素）变暖色（306）= 高亮，随后 `VK_DELETE` 把那条线删掉（浮层「元素 节点 8/11 · 连线 6/10」，总数由 11 降 10）= `Focus(FocusState.Pointer)` 确实拿到了焦点，而 `视口(画布)` 前后都是 **1998, 421**（视口用 UI Automation 读浮层文本得到，不依赖哪个窗口在最前）。⇒ WinUI 这条路径对「指针焦点 + 比视口大的元素」没有实际动作；**没查到官方文档里的明确条件**（测的时候这台机器取不到 learn.microsoft.com），所以只留实测结论。连线视图如今 `IsTabStop = false`（`PolylineCurveView.xaml.cs` 的 `UpdateInteractivity`），Tab 聚焦不到它、焦点只会落在宿主上；若日后有人改这条路径，可用的单行防线仍是 `ScrollViewer.SetBringIntoViewOnFocusChange(…)`（别把整块画布的自动滚进视口关掉）。
**虚拟连接（橡皮筋）的可见性：先分清「被卡挡住」与「没画出来」**（2026-09-26 实测）。

- **「被卡挡住」是合法的**：橡皮筋在连线层，而连线层在节点卡之下（非 Trimmed `Canvas.SetZIndex(this, -100)`，Trimmed 模板 `Canvas.ZIndex="-1"`）⇒ 指针还压在卡上时它本来就被挡住，「越过一段距离才看到」的距离 = 从插槽到**卡外**。实测（非 Trimmed，输出插槽在卡右边缘）：向上/向下拖第 20px 就看到；向卡内拖到 650px 看不到；卡外开阔处另有正面对照。
- **「没画出来」是缺陷（`LinkView.UpdateLayoutSubscription()` 从 `Sender/Receiver.Parent.Parent` 找承载树）**：`LinkView.UpdateLayoutSubscription()` 只从 `Sender/Receiver.Parent.Parent` 找承载树，而虚拟连线两端**故意没有父节点**（`IsVirtualLink` 就是按这个判的）⇒ `_layout` 恒为 null ⇒ `UpdatePath` 的 `if (w > 0 && h > 0) { Width = w; Height = h; }` 从不生效 ⇒ 视图盒子退化成 `actualH=0`（探针实测，正常应为 1340）⇒ 保留模式的 `Path` 被元素边界剪掉 ⇒ **橡皮筋从按下第一帧起就不可能可见**，与象限、与拖拽距离都无关（修前 20/60/150/300/600/900/1200px 每一档整幅画布都是 **0** 像素变化）。
- **修法**：`tree ??= FindHostTree();` —— 沿宿主链（`Parent as FrameworkElement` → `DataContext is IWorkflowTreeViewModel`）找承载它的树，画布及其后代继承同一份 DataContext。修后同一套测量：`actualH=1340`、20px 拖拽 **30** 像素、150px **346** 像素（bbox 148×4 = 那条 2px 虚线）、连跑两遍读数一致；同轮对照（同二进制同输入）非 Trimmed 的 20px 是 **9281** 像素。
- **同一段代码也在 item 模板里**（`Src/Templates/VeloxDev.WinUI.Templates/working/content/workflow-link-view/TemplateClass.xaml.cs` 的 `UpdateLayoutSubscription`）⇒ 生成出来的项目同样起不了橡皮筋，已同步修（模板内容在本仓库没有编译校验，这一份只有静态依据 + 同形代码在 demo 侧的运行时验证）。七家的**连线视图**代码里 grep `Sender?.Parent?.Parent` 只命中这两处（Core 里另有两处、语义不同：判定虚拟连线的 `WorkflowLinkEx.StandardDelete` 的 `component.Sender?.Parent?.Parent is null` 与找承载组件的 `ComponentPatcher.ResolveTree` 的 `link.Sender?.Parent?.Parent`）⇒ 六个 Trimmed 里**只有这家**是这个成因（另外五家未实测）。
- **判定：既有**（视图侧文件这几天没被改过）。行为侧也已排除：按下到达行为、`Channel` 是默认的 `MultipleBoth`、`canBeSender=True`、插槽挂在树上、`StandardSendConnection` 走到了写锚点的 step 3 ⇒ **不是**行为/demo 配置的问题。
- **中途两次被日志带偏，记下来免得重走**：`VirtualLink.IsVisible=False` 那条其实是**拖拽结束时的 Reset**；「样例插槽不配 channel ⇒ 起不了连线」也被探针排除（默认 `MultipleBoth` 宽松，模板不配同样无碍）。
- **量这块的三条陷阱**：(1) Trimmed 的线是**白色** `#DDFFFFFF`（`LinkTemplate` 对所有线都写它，「是不是虚拟」由 `LinkView` 从数据上下文推，模板从不绑 `IsVirtual` DP）⇒ 拿青色筛必然一个都找不到；(2) 插槽拖拽**同时平移画布**（`WorkflowSlotConnectionBehavior` 不置 `Handled`，实测一次 260px 拖拽改了 7154 像素）⇒ 拖拽途中的像素判定必须扣掉平移；(3) `WindowFromPoint` 要按**进程号**比而不是句柄（XAML 在 `InputSiteWindowClass` 子窗口里），否则每一步移动都被守卫拒掉、量出「什么都没发生」的假象。
- **「白线画在白卡上」不是成因**：卡底色与连线默认色确实都是 `#DDFFFFFF`（卡底实测 (225,225,225)，白线压上去合成 (251,251,251)、差约 25 级 —— 低对比但可分），而非 Trimmed 那条是青色（高对比）。
- 顺带记虚拟连线的判定边界：`Slot.Parent` 是 `IWorkflowNodeViewModel?`、插槽脱离节点时置 `null`（`SlotEnumerator` 里 `slot.Parent = null;`）⇒「两端都无父节点」= 虚拟连线；正常连线两端都挂在节点上、不会误判，只有「两端都脱挂的孤儿连线」会被画成虚线（本 demo 到不了）。
**「白线画在白卡上」这条机制本身**：调色板里卡的底色与连线默认色都是 `#DDFFFFFF`，实测 Trimmed 样张的卡底色是 **(225,225,225)**（= 0.867·255 + 0.133·30，正是 `#DDFFFFFF` 压在那块深色画布上的值）⇒ 若橡皮筋真画在卡上，它的合成色是 (251,251,251)，与卡底**只差约 25 级**（在深色画布上则差约 195 级）—— 低对比成立，但**不是**「完全不可分」，而且**非 Trimmed 那家的橡皮筋是青色**（高对比），所以这条不是用户那次报告的观察到的成因；可观测的成因仍是上一条的**卡遮挡**。
**两个会让测量得出假结论的陷阱（我都踩过）**：
1. **Trimmed demo 的线是白色、不是青色**：它的 `LinkTemplate` 给每条线都写 `LineColor="#DDFFFFFF"`，而「是不是虚拟」由 `LinkView.IsVirtualLink` 从数据上下文推出来 —— `IsVirtual || DataContext is IWorkflowLinkViewModel { Sender.Parent: null, Receiver.Parent: null }`（模板**从不**绑 `IsVirtual` 那个 DP）⇒ 拿青色去筛 Trimmed 的线必然**一个像素都找不到**，会误判成「根本没画」—— 要用白色/全区域差分。顺带记该判据的边界：`Parent` 是 `IWorkflowNodeViewModel?`，插槽脱离节点时被置 `null`（`SlotEnumerator` 里 `slot.Parent = null;`）⇒「两端都没有父节点」= 虚拟连线，**正常**连线两端都挂在节点上、不会被误判；只有「两个端点都脱挂的孤儿连线」会被画成虚线（本 demo 到不了：样张 0 条连线、也没有删节点的入口）。
2. **插槽拖拽同时会平移画布**：`WorkflowSlotConnectionBehavior` 冒泡指针事件且**不置 `e.Handled`**（§二·L1 已记）⇒ 从插槽往外拖时画布也在跟着平移（实测：一次 260px 的插槽拖拽让整幅画布变了 7154 个像素）。所以任何「拖拽过程中」的像素测量都必须先扣掉平移，否则「看不到」既可能是被卡挡住、也可能是被平移带走。
3. 用 `WindowFromPoint` 断言「这个点属于我的窗口」时要**比较进程号**，不能比较窗口句柄：WinUI 把 XAML 内容放在一个子窗口（`InputSiteWindowClass`）里，`WindowFromPoint` 往往返回那个子窗口而不是外层框架窗口 —— 按句柄比较会把**每一次移动都拒掉**，量出来是「什么都没发生」的假象。

**两个量这块时的坑**：(a) 指针必须**走**到线上（多步小位移），一步瞬移（`SetCursorPos`/单次绝对移动）不会派发 `PointerEntered`/`PointerMoved`，测出来会像「悬停坏了」；(b) `MoveWindow` 对这家窗口**不生效**，要挪窗口/改尺寸得用 `SetWindowPos(hwnd, None, x, y, w, h, SWP_NOZORDER|SWP_NOACTIVATE)`。

**这套交互的实测（2026-09-26，同一套「先断言再动作」的脚本）**：悬停 → 同一点由体色（≈120 像素）变高亮暖色（≈340）、线体加粗成 OrangeRed；`VK_DELETE` → 悬停那条被删（左栏「可见组件数」15 → 14、两端端口变灰）；右键 → `MenuFlyout` 只有一项（当时文案「删除连线」，现为 `Delete`）→ 点它 → 该线消失（16 → 15；浮层「连线 N/M」总数 12 → 10）。**回归同样实测通过**：空画布左键拖仍平移（视口原点 0,0 → 230,58，且按下点 48×48 内体色为 0 = 确实在空白处）、节点拖拽仍生效（卡片右移 220px 而视口原点不变）、端口拖动仍出橡胶带（空处释放即取消、连线总数不变）、Ctrl+滚轮仍缩放（Scale 1.00 → 0.75）。

---

## 六、核不到的东西（写下来免得下一个人重找）

- 历史记录里的「**WinUI 上池化视图可能在离树状态下收到属性变更**」在 WinUI 侧**不成立**：
  池化视图从不离开视觉树 —— `ViewManager.HideViewFor` 只做 `Visibility = Collapsed` + `DataContext = null` +
  退订 + 入池，从池里取回时也不再 `Children.Add`。
  离它最近的机制是 §四·P2 那个不重校验的延迟闭包，与「离树」无关。
- `WorkflowCanvasTransformBehavior` 那句错注释的具体来历无法考证（代码与注释里都没有记录），
  只能确证**现状**与注释不符（§四·P1）。
