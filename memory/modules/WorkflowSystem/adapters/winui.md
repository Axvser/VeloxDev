# WorkflowSystem — WinUI 适配器

> 契约与注册位置在 [`../extension.md`](../extension.md) §3.9；七角色职责表与 `PART_*` 命名在
> `skills/veloxdev-create-workflow/references/view-layer.md`；本家的写法说明在
> `skills/veloxdev-create-workflow/references/gui/winui.md`。本文只写这一家的**形状**：
> 被哪些平台事实逼成现在这样、哪里和另外六家不一样、改哪里会踩什么。

代码：`Src/Adapters/VeloxDev.WinUI/Attached/Workflow/`。

**参考实现是 `Examples/Workflow/WinUI Trimmed/`，不是 `Examples/Workflow/WinUI/`** ——
后者还停在 offset frame 之前的老做法（见 §四·P3）。

---

## 一、这一家与契约的接法（只说需要额外知道的部分）

七个角色都落在适配器里，与别家同名（`WorkflowSurfaceBehavior` / `WorkflowCanvasTransformBehavior` /
`ViewManager`+`ViewPool` / `WorkflowNodeDragBehavior` / `WorkflowSlotConnectionBehavior` /
`WorkflowSlotLayoutBehavior` / `WorkflowMinimapOverlay`）。附着属性名与 `PART_*` 约定与 WPF 一致，
不要在这里重抄一遍。契约之外这一家多出来的是：

**多一个公开入口 `WorkflowSurfaceBehavior.Refresh(UserControl host)`**（`Attached/Workflow/WorkflowSurfaceBehavior.cs:106`）。
原因是这一家解析 `PART_*` 唯一的路子是**名称域查找**（`FindName`，`:191-239`），而名称域挂在 `UserControl` 上、
不在可视树上 —— 宿主换树或重新套模板后，必须有人再调一次 `Refresh` 重新解析。别家能靠可视树遍历自愈，
所以别家没有这个成员。**接新平台时如果你能在可视树上按名字找，就不需要抄它。**

契约要求「`Viewport` 是画布局部坐标、且只有适配器写」：这一家写在 `WorkflowSurfaceBehavior.ApplyVisibleRegion`
（`:607-625`，同时写 `Layout.ViewportOffset`），触发链路是 `UpdateVisibleRegion` → `ApplyVisibleRegion`。
契约要求「连线视图首行过渲染就绪门」：那行在视图侧。本家两个 demo 的实现不同 ——
`Examples/Workflow/WinUI Trimmed/Demo/Views/Workflow/LinkView.xaml.cs` 只有 `CanRender`（绑定到 `IsVisible`），
**没有 NaN 那半**；`Examples/Workflow/WinUI/Demo/Views/Workflow/PolylineCurveView.xaml.cs` 的 `RenderReady`
（`// RenderReady => CanRender && 四端点非 NaN`，2026-09 那次修链接「新连线有时不出现」时加的）两半都在。
按渲染就绪门改哪一份，先核这一句 —— 别以为 Trimmed 就是齐全的。

---

## 二、平台硬限制（决定了别家能照抄什么）

**L1 · 没有 Preview / 隧道阶段，且指针事件在到达自定义处理器前已被框架标记 handled。**
滚轮缩放因此挂在 `ScrollViewer` 上、走冒泡、注册 `handledEventsToo: true`
（`WorkflowSurfaceBehavior.cs:278-288`，理由自陈在 `:278-281`）；代价写在同一条注释里：
**一格 Ctrl+滚轮可能先被原生滚动吃掉一点**。插槽连线的处理方式同源：冒泡 `PointerPressed`/`PointerReleased`，
不置 `e.Handled`，用下降沿 `ReleasePointerCaptures()` 代替「抢在子元素之前」
（`WorkflowSlotConnectionBehavior.cs:37-49`）。对照：WPF 走隧道 `PreviewMouseLeftButtonDown` 并置 `Handled`
（`Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowSlotConnectionBehavior.cs:26-33,43-44`）。

**L2 · 没有公共 `OnRender`。** 小地图只能是**保留式元素**：`WorkflowMinimapOverlay : Canvas`
（`:21`），`RebuildShapes` 复用 `Rectangle` 池并**原地改** `Width/Height/SetLeft/SetTop`（`:487-600`）。
重绘不是去抖而是**节流**：`ScheduleRebuild`（`:337-354`）配一个 ctor 里建的 16 ms `DispatcherQueueTimer`（`:174-202`），
注释明说去抖会让连续平移期间「只画一次」（`:341-344`）；`Tick` 里吞 `COMException`（`:189-191`）。
**别家有 `OnRender`/`Render` 就别抄这套** —— WPF/Avalonia/Jalium 覆写 `OnRender`/`Render`，
MAUI 走 `GraphicsView`/`IDrawable`（各 `…/WorkflowMinimapOverlay.cs`，行号见 `../WorkflowSystem/extension.md` 的
`WorkflowSurfaceMath` 小节与各适配器目录）。

**L3 · `LayoutUpdated` 是逐元素的，且画布在节点子树布局之后才重排自己的子元素。**
所以插槽测量必须**挂两级**：节点自己的和坐标宿主（`PART_Canvas`）的
（`WorkflowSlotLayoutBehavior.cs:147-163`，理由注释 `:21-28`）。
**WPF 免疫**：它的 `LayoutUpdated` 是整棵树一次 layout pass、天然 post-arrange（同注释 `:26-28`）。
WinUI 是七家里唯一需要挂两级的。
同源的三个附带事实：`LayoutUpdated` 的 `sender` 恒为 `null`，只能用闭包捕获控件（`:108-110`）；
`SizeChanged` 与之并行挂（`:205-213`）；两者都**同步**调 `Sync`，异步 hop 会晚一帧（`:250-266`）。

**L4 · 名称域查找只有 `FindName` 一条路。** 见 §一 的 `Refresh(UserControl)`。
`PART_*` 必须真的落在 `UserControl` 的 `x:Name` 域里；「模板不生成 `x:Name="Root"`」的坑
（`skills/.../gui/winui.md:57`；demo 侧对照是 `Examples/Workflow/WinUI/Demo/Views/Workflow/TreeView.xaml:4` —— 有 `x:Name="Root"` 的是**非 Trimmed** 那个 demo）就是这条限制的另一面。

**L5 · 画布平移只能用合成变换。** `state.Canvas.Translation = new Vector3(ActualOffset.H, ActualOffset.V, 0f)`
（`WorkflowSurfaceBehavior.cs:570-578`），注释写明「WinUI 的子元素（不像 WPF）不会把 `RenderTransform`
绑到 `CanvasTransformBehavior.Transform`」。这一条牵出本家整套 **offset frame**（连线几何里烘进 `+offset`、
元素定位到 `-offset`、`Clip` 三处清空）：做法与理由见 `skills/.../gui/winui.md:27-47`，此处不复述。
它的**推论**必须记住：因为合成平移已经把画布平移反掉了，槽位锚点要用 `SlotAnchorFromCanvasLocal`（恒等形式），
用视觉中心形式会**二次减去** `ActualOffset`，表现为「每条连线整体偏一个常量」
（`WorkflowSlotLayoutBehavior.cs:390-410` 的注释与分支；`skills/.../gui/winui.md:45`）。
同理，指针世界坐标要**手动补偿**画布渲染平移（`WorkflowSurfaceBehavior.cs:450-468`，注释自陈
「WPF 靠 `GetPosition(canvas)` 白拿」）。

**L6 · `DispatcherQueuePriority` 只有 Low / Normal / High 三档，没有 `Render`。** 于是「延后做、别挡当前帧」
这件事全靠**选档位 + 落地前重校验**这两招，七家里这套写得最密的也是本家：

| 延后的事 | 档位 | 重校验 |
|---|---|---|
| 可见区域/虚拟化重算（`UpdateVisibleRegion`） | `Low`（`:584-605`） | `ReferenceEquals(currentState, state)`（`:596-600`） |
| 一批视图的物化（`ScheduleNextBatchRender`） | `Low`（`ViewManager.cs:110-119`） | 批次上限 3（`:124`） |
| 槽位同步（`ScheduleSync`） | `Normal`（`WorkflowSlotLayoutBehavior.cs:249-267`） | `Syncing` 守卫（`:269-332`） |
| 小地图在 `ScrollViewer` 尺寸变化后的重算 | `Low`（`WorkflowMinimapOverlay.cs:230-244`） | —— |

**这张表是这一家最值得抄的东西**：在任何「异步重算 + 下一帧才生效」的宿主上，
「排到低优先级」与「落地时确认自己还是当前那份状态」必须成对出现。

**L7 · WinRT 控件只能在有 XAML 运行时、且在 UI 线程上构造。** 小地图 ctor 里
`DispatcherQueue.GetForCurrentThread()?.CreateTimer()` 用 `?.` + `catch (COMException)` 兜住
（`:174-202`，吞异常在 `:189-191`）：在非 UI 线程构造就是**永不重绘且不报错**；
在纯数据进程里构造则是类激活失败（同 TransitionSystem 那侧，见
`Examples/Transition/AUTO TEST/Samplers/UnreachableSamplers.cs:26-31,40-50`）。

**L8 · 命中测试里认不出类型时只能比类型名字符串。** `IsSurfaceBlankInteraction`（`:700-741`）
对节点/插槽用 `DataContext is IWorkflowNodeViewModel or IWorkflowSlotViewModel`（`:734-735`，**契约类型，可靠**），
但对 `ScrollContentPresenter`（WinUI 内部类型，引不到）和演示里的 `BezierCurveView`/`PolylineCurveView`
（适配器不能引用 demo 类型）只能 `string.Equals(x.GetType().Name, "…")`（`:731,740-741`）。
**推论**：演示一旦重命名这两个连线视图类型，空白处按下就会静默改变行为（缩放/平移的手势来源变化），
没有任何编译期或运行期提示。

---

## 三、刻意背离（与另外六家不一样，以及为什么）

**D1 · 画布平移用合成 `Translation`，不是共享 `TranslateTransform` 的模板绑定。**
WPF/Avalonia 的做法是「适配器设一个附着属性，节点与连线视图在 XAML 里把 `RenderTransform` 绑上去」
（`Examples/Workflow/WPF/Demo/Views/Workflow/WorkflowView.xaml:27`、`Examples/Workflow/Avalonia/Demo/Views/Workflow/WorkflowView.axaml:30`，
`Src/Templates/VeloxDev.WPF.Templates/working/content/workflow-tree-view/TemplateClass.xaml:22` 同）。
**这里的做法和其他家不一样，因为** WinUI 的子元素不做那个绑定（`WorkflowSurfaceBehavior.cs:570-578` 的注释），
平移由画布自己的合成变换承担。
> ⚠ **本条带一处注释与代码不符，见 §四·P1。**

**D2 · 节点几何是「模板绑定 + 适配器命令式再写一遍」两套并存。**
示例模板里节点视图直接绑 `Canvas.Left/Top/ZIndex` 到 `Anchor`（`Examples/Workflow/WinUI/Demo/Views/Workflow/TreeView.xaml:25-27`），
同时 `ViewManager.ApplyLayout` 又命令式写一遍同样的三个附着属性、外加 `Width`/`Height`
（`ViewManager.cs:334-340`）。WPF/Avalonia 的 `ViewManager` **只写 `Visibility` + `DataContext`**，
几何全交给模板（`Src/Adapters/VeloxDev.WPF/Attached/Workflow/ViewManager.cs:149-177`、Avalonia 同文件 `:150-178`）。
**这里的做法和其他家不一样，因为** WinUI 的容器会把模板根的 `Canvas.Left/Top` 吞掉，
示例为此专门写了一个 `CanvasItemsControl` 把子元素的 Left/Top 抄到容器上 ——
那句平台断言写在 `Examples/Workflow/WinUI/Demo/Views/Workflow/CanvasItemsControl.cs:8-10,150-165`（**演示的注释，不是我实测的**）。
`ViewManager` 那三个附着属性是同一个问题的第二道保险；`Width/Height` 则是本家必需的（见 D3）。

**D3 · `Size` 为 `(0,0)` 时写 `Width/Height = double.NaN`，而不是 0。**
`ViewManager.cs:334-340` 及注释：「还没测量过，用 NaN 让平台自己定尺寸」。
**这里的做法和其他家不一样，因为** WinUI 里 `Width = 0` 是「真的宽 0」，没有别家那种
「0 表示未设」的约定（`Layout.ActualSize` 的后续写入依赖这一点）。

**D4 · 缩放后**同步**重跑一次虚拟化，并强制三次 `UpdateLayout()`。**
`WorkflowSurfaceBehavior.cs:336-344`：`EnsureNegativeCover` 之后 `Canvas.UpdateLayout()` → `sv.UpdateLayout()`
→ `host.UpdateLayout()`，注释说 `ChangeView` 会按**陈旧的 extent** 夹取滚动位置（`:338-340`）；
紧接着 `VirtualizeAtScroll(...)`（`:369-374`），世界原点那条分支也再跑一次（`:382-401`）。
**这里的做法和其他家不一样，因为**本家池化视图的几何更新排在低优先级（L6 的表），
不强制重排就会「池化视图滞后缩放约 100 ms」—— 那句现象写在 `skills/.../gui/winui.md:47`。

**D5 · 网格装饰器**不在适配器里**，但它的虚拟化接线**在适配器里**。**
适配器目录里没有 `WorkflowGridDecorator`，装饰器是示例/模板产物
（`Examples/Workflow/WinUI/Demo/Views/Workflow/WorkflowGridDecorator.cs:28`）；
Jalium 与 Razor 则把它放进了适配器（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowGridDecorator.cs:13`、
`Src/Adapters/VeloxDev.Razor/Attached/Workflow/WorkflowGridDecorator.razor.cs:14`）。
但契约要求的 `RulerBand → SetVirtualizeInset` 转发**由适配器做**
（`WorkflowSurfaceBehavior.cs:658`：`SetVirtualizeInset(left: decorator.RulerBand, top: decorator.RulerBand)`）。
**这里的做法和其他家不一样，因为**这一家按名字找装饰器、拿不到就不装 inset；
新接平台时不要因为「装饰器不归我」而把 inset 那两行一起搬出去。

**D6 · 小地图指针按下**总是重新居中**，没有抓取锚点。**
`WorkflowMinimapOverlay.cs:427-438`，注释写「Match the Jalium adapter … no grab-anchor」。

**D7 · 小地图推给重绘的视口尺寸取 `ActualWidth/ActualHeight`，不是 `ViewportWidth/Height`**
（`:661-679`）。**这里的做法和其他家不一样，因为**本家小地图自己是画布元素、尺寸即视口尺寸，
读它比读契约属性少一次跨对象依赖。

---

## 四、坑（改这里时最容易踩的，附依据）

**P1 · `WorkflowCanvasTransformBehavior.cs:28` 的注释与代码不符 —— 这是本家最该抓的一条。**
注释写「节点与连线视图在 XAML 里把自己的 `RenderTransform` 绑到这个附着属性」，
但全仓库搜 `WorkflowCanvasTransformBehavior`：WinUI 侧**只有** `WorkflowSurfaceBehavior.cs:578` 自己写它，
没有任何 WinUI 的 `.xaml`（适配器、模板、两个 demo 都算）读过它；
真正那样绑的只有 WPF/Avalonia 的 demo 与 WPF 模板（见 §三·D1 的行号）。
这句注释显然是从 WPF 版本抄过来的。**后果**：下一个 agent 读它就会去模板里找那个绑定、找不到，
或者「补上」这个绑定，从而与 `Canvas.Translation` 的平移**叠加**成双倍位移。
`OnTransformChanged` 是故意空的（`:25-30` 自陈「只是通知载体，宿主本身不能收渲染变换」），这一半是对的。

**P2 · `ViewManager.cs:298` 的延迟闭包不重校验自己还是当前状态。**
`SubscribeToLayoutChanges` 在跨线程时 `TryEnqueue(DispatcherQueuePriority.Low, () => ApplyLayout(view, viewModel))`，
闭包捕获 `view`/`viewModel` 后**没有**确认这一对还在活跃。
对照同目录 `WorkflowSurfaceBehavior.cs:596-600`，那里排 Low 之前与落地之后都做了 `ReferenceEquals` 重校验。
**后果**：一帧内视图被回收并复用给另一个 ViewModel 时，旧几何会写到新宿主上 —— 且不报错。
改这里时照 L6 的表补一条重校验。

**P3 · `Examples/Workflow/WinUI/`（非 Trimmed）是 offset frame 之前的老做法，不要照它抄。**
它的连线模板把视图的 `Width/Height` 绑到画布元素的 `ActualWidth/ActualHeight`
（`Examples/Workflow/WinUI/Demo/Views/Workflow/TreeView.xaml:64-65`），
`PolylineCurveView.xaml.cs` 全文没有 `ActualOffset`/`Canvas.SetLeft`/`ActualSize`
（grep 命中 0）—— 也就是说它整幅铺在画布上、靠尺寸而不是靠负偏移定位，
正是 `skills/.../gui/winui.md:43` 明确劝退的那种写法（「会重新引入 offset frame 想避免的裁剪」）。
**确证过的对照**：`Examples/Workflow/WinUI Trimmed/Demo/Views/Workflow/LinkView.xaml.cs:219-237` 才是 offset frame
（注释 `:219-220` 讲清 `[-ActualOffset, ActualSize - ActualOffset]` 与烘焙几何的关系，`:237` 是 `Canvas.SetLeft(this, -ox)`）。
skill 文档 `gui/winui.md:5` 指的参考实现就是这个 Trimmed 目录。

**P4 · `Sync` 的 `Syncing` 守卫是 try/finally —— 这是有意的，别简化成 try/catch。**
`WorkflowSlotLayoutBehavior.cs:269-332`，注释在 `:276-279`：`TransformToVisual` 抛异常时不能让守卫留在
已置位状态，否则槽位同步**永久停止且不报错**。同族的 `SyncSlotEnumerator` 用 `ActualWidth <= 0`
判断「还没测量」并排 Low 重试（`:347-388`）—— 这两个守卫一起保证「测量失败 ≠ 停止同步」。

**P5 · 名称解析失败是静默的。** `FindName` 找不到就返回 `null`，行为退化成「没有缩放 / 没有小地图」，
不抛异常（`WorkflowSurfaceBehavior.cs:191-239`）。所以改模板布局后一定要调 `Refresh(UserControl)`（`:106`）。

**P6 · 小地图在非 UI 线程构造 = 永不重绘且不报错**（§二·L7）。定时器建不出来时没有任何补偿路径。

**P7 · 空白处按下的判定对类型名字符串敏感**（§二·L8）。重命名演示里的 `BezierCurveView`/`PolylineCurveView`
会静默改变手势归属。

**P8 · 保留模式下「写进去的几何 = 画出来的东西」，所以连线视图必须在每个入口都重算一遍渲染状态。**
本家没有 `OnRender`，`PolylineCurveView` 的整幅画面（3 条静息线的 `BezierSegment` + 24 条彗星段的
`PathFigure`/`LineSegment`，共 27 个 `Path`）只由「依赖属性变更」这一个入口驱动重写。
Avalonia/WPF/Jalium 的同一份视图是每帧重算的（`Render`/`OnRender`），所以那边
`_length <= 0` 这类守卫即使被 NaN 穿过，最多错一帧；**这边错的是永远** —— 写进去什么就一直画什么。
同一个视图还有两个不触发 `Loaded`/`Unloaded` 的入口必须自己挂：`DataContextChanged`
（池化视图改绑/回收，`ViewManager.cs:182-203` 只改 `Visibility` + `DataContext`，从不摘树 —— 见 §五），
以及「本帧没有属性变化」这件事本身。
两条硬规则：**几何里不许出现 NaN**（未测量的 slot 锚点就是 NaN，`WorkflowSlotUpdateGate` 的约定；
NaN 参与的比较全是 false，`_length <= 0`、`lo >= _length` 这类守卫一条都拦不住，而 NaN 点画不出任何东西）；
**「停周期」不能只挂在 `Unloaded` 上**（池化视图永远等不到它，旧链接的 `Transition` 会一直按帧写那 27 个 `Path`，
这正是「整体有一点点不流畅」的来源）。
改这一份视图时按这两条自查：`Examples/Workflow/WinUI/Demo/Views/Workflow/PolylineCurveView.xaml.cs` 的
`Refresh()` 是唯一入口，`RenderReady` 是唯一门。参照写法在 Trimmed 的
`Examples/Workflow/WinUI Trimmed/Demo/Views/Workflow/LinkView.xaml.cs`：它挂了 `DataContextChanged`
（`:60,122-128`，注释明写池化复用与「hide 会先给一个 null 的 DataContext」）却**没有** NaN 门。
两个 demo 的 `SlotView` 同为保留模式改写、也各有周期，但**没核过**它们是否也漏了某个入口 —— 动到再看。

---

## 五、非 Trimmed demo 连线视图的三件事落点（含右键菜单）

**命中面 = 画出来的最外那圈描边。** 静息线有三层，现在只有最外层（halo，本体 + 9）拿
`hitTestable: true`（`PolylineCurveView.xaml.cs:153`、`:669`），内侧两层与彗星各段保持
`IsHitTestVisible = false`（`:154-155`、`:708`）。带宽因此 ≈±5.5px —— 与 Avalonia/WPF 由框架对
描边做命中测试得到的带宽同量级，且不超出画出来的范围。

| 事 | 代码落点 | 依据 |
|---|---|---|
| 命中 | 只有 halo 一层可命中；业务判定仍是 `HitTestLine`（沿弧长表判距，`hitRadius = 6.0`） | `:153`、`:808`（右键那一层）、`:738`（`OnHoverPointerMoved`） |
| 选中即取焦点 | `PointerEntered` 与 `OnRightTapped` 里各 `Focus(FocusState.Pointer)` 一次 | `:179`、`:763` |
| 右键菜单 | `RightTapped` → `MenuFlyout.ShowAt(this, new FlyoutShowOptions { Position = pt })` | `:183`（挂载）、`:763` |
| 删除 | 菜单项 `Click` → 读视图**当时的** `DataContext` 的 `DeleteCommand`；Delete 键走同一个 `DeleteLink()` | `:791`（建菜单）、`:799` |

四条要记住的：

1. **这家曾经一件都跑不到，原因本身就是这一节的坑**：修之前根 `UserControl` 没有 `Background`，画出来的内容全是 `Path` 且逐个 `IsHitTestVisible = false`（就是现在 `:669`、`:708` 那两处，改前恒为 `false`），容器 `Grid` 也无背景 ⇒ 命中面为空，`PointerEntered`/`PointerMoved`/`RightTapped` 一个都不会派发。**实测（2026-09-26，SendInput + 闭环伺服取点）**：指针停在线体正上方（48×48 邻域内体色像素 120–186）线体仍是静息青色；从窗口外跳进来再压线体也没有高亮（`PointerEntered` 是无条件置高亮的，所以没高亮就是没派发）；同一窗口里空画布左键拖动照常平移 —— 窗口收得到输入，是**这个视图**收不到。
2. **不要改成给视图加 `Background`**。这家的连线视图是**整块画布大小**（§四·P3），加背景会把画布平移整个吃掉；`SlotView.xaml:21,33` 那句「命中面是控件自己的 `Background`」是给**小控件**的规矩，别照抄到连线视图上。正确做法是让**描边自己**成为命中面：`Shape` 的 stroke 本身就是命中区域，且不占空白。彗星那几段刻意不参与命中 —— 它整段落在 halo 之内（不增面积），而它的几何每帧都在改写，可命中只会让命中面跟着光跑。
3. **`RightTapped` 是这家的右键入口**（`IsRightTapEnabled` 默认 `true`，无须设），**在右键抬起时才触发**；不要换成 `PointerPressed` + `IsRightButtonPressed`。**前提是视图可命中**（第 1 条）。
4. **代码建的 `MenuFlyout` 必须自己给 `XamlRoot`**：它不属于任何元素，只有 `ShowAt(element)` 的 `element` 在树上；在 `ShowAt` 之前写一次 `_menu.XamlRoot = XamlRoot`（视图此刻已上屏，拿到的就是它所在的那一棵）。菜单项同样**不绑命令** —— 池化视图会被改绑给另一条链接，`Click` 处理器在点击那一刻才读 `DataContext`。

**这家没有「悬停取焦点 ⇒ 画布跳一段」这条代价**（即 Avalonia/WPF 那个 `ScrollViewer.BringIntoViewOnFocusChange` 症状）。**实测（2026-09-26）**：把视口滚到非零偏移（`视口(画布) 1998, 421`）后，让指针**走**到线上（伺服逐步逼近）→ 同一点由体色（105 像素）变暖色（306）= 高亮，随后 `VK_DELETE` 把那条线删掉（浮层「元素 节点 8/11 · 连线 6/10」，总数由 11 降 10）= `Focus(FocusState.Pointer)` 确实拿到了焦点，而 `视口(画布)` 前后都是 **1998, 421**（视口用 UI Automation 读浮层文本得到，不依赖哪个窗口在最前）。⇒ WinUI 这条路径对「指针焦点 + 比视口大的元素」没有实际动作；**没查到官方文档里的明确条件**（测的时候这台机器取不到 learn.microsoft.com），所以只留实测结论。若日后有人改这条焦点路径（例如让键盘 Tab 也能聚焦连线视图），可用的单行防线是 `ScrollViewer.SetBringIntoViewOnFocusChange(this, false)`（作用于该视图，别把整块画布的自动滚进视口关掉）。
**两个量这块时的坑**：(a) 指针必须**走**到线上（多步小位移），一步瞬移（`SetCursorPos`/单次绝对移动）不会派发 `PointerEntered`/`PointerMoved`，测出来会像「悬停坏了」；(b) `MoveWindow` 对这家窗口**不生效**，要挪窗口/改尺寸得用 `SetWindowPos(hwnd, None, x, y, w, h, SWP_NOZORDER|SWP_NOACTIVATE)`。

**开了命中面之后的实测（2026-09-26，同一套「先断言再动作」的脚本）**：悬停 → 同一点由体色（≈120 像素）变高亮暖色（≈340）、线体加粗成 OrangeRed；`VK_DELETE` → 悬停那条被删（左栏「可见组件数」15 → 14、两端端口变灰）；右键 → `MenuFlyout` 只有「删除连线」一项 → 点它 → 该线消失（16 → 15；浮层「连线 N/M」总数 12 → 10）。**回归同样实测通过**：空画布左键拖仍平移（视口原点 0,0 → 230,58，且按下点 48×48 内体色为 0 = 确实在空白处）、节点拖拽仍生效（卡片右移 220px 而视口原点不变）、端口拖动仍出橡胶带（空处释放即取消、连线总数不变）、Ctrl+滚轮仍缩放（Scale 1.00 → 0.75）。

---

## 六、核不到的东西（写下来免得下一个人重找）

- 历史记录里的「**WinUI 上池化视图可能在离树状态下收到属性变更**」在 WinUI 侧**不成立**：
  池化视图从不离开视觉树 —— `ViewManager.HideViewFor` 只做 `Visibility = Collapsed` + `DataContext = null` +
  退订 + 入池（`ViewManager.cs:182-203`），从池里取回时也不再 `Children.Add`（`:150-180`）。
  离它最近的机制是 §四·P2 那个不重校验的延迟闭包，与「离树」无关。
- `WorkflowCanvasTransformBehavior` 那句错注释的具体来历无法考证（代码与注释里都没有记录），
  只能确证**现状**与注释不符（§四·P1）。
