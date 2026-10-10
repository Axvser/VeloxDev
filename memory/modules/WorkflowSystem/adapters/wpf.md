# WorkflowSystem — WPF

> 连线交互规则见 [WorkflowSystem/architecture.md §3.6](../architecture.md)：输入是标准输入
> （`WorkflowInput.For(tree).Route(...)` + `IInputEvents`），指针底下是谁（节点 / 插槽 / 连线）由适配器从视图的数据上下文解析、认不到才回退 Core 的共享曲线判定，外观、删除与菜单的接线归宿主/适配器。
>
> **读法**：契约（七个视图角色、附着属性、注册位置、联动清单）在 `memory/modules/WorkflowSystem/extension.md` §3.9 与 §4.3，
> 本文不重复；人面向的「怎么搭一个 WPF 工作流视图」在 `skills/veloxdev-create-workflow/references/gui/wpf.md`，
> 逐角色职责表在 `skills/veloxdev-create-workflow/references/new-adapter.md` / `references/view-layer.md`。
> 本文只写这家的**硬限制、刻意背离、该家特有的坑**。代码在 `Src/Adapters/VeloxDev.WPF/Attached/Workflow/`。
> **路径写法**：下文的裸文件名（`WorkflowSurfaceBehavior.cs:323`、`ViewManager.cs:125` 等）都相对 `Src/Adapters/VeloxDev.WPF/Attached/Workflow/`；引用 Core 或别家时一律写全路径。

---

## 一、这家要写什么，为什么是这些

WPF 把七个视图角色**一对一**落成 8 个实现（视图池拆成「挂点」与「管理器」两个文件），**没有任何一个角色被合并、也没有多出契约外的角色**：

| 角色 | 这家的实现 | 为什么是它 |
|---|---|---|
| 画布宿主 | `WorkflowSurfaceBehavior` | 附着属性入口，也是唯一持有 `SurfaceState` 的地方 |
| 画布变换 | `WorkflowCanvasTransformBehavior` | 只是一个**值载体**（见坑 6） |
| 视图池 | `ViewPool`（挂点）+ `ViewManager`（管理器） | 挂点在 XAML 上要能写，管理器要能按 `Panel` 存状态 |
| 节点拖拽 | `WorkflowNodeDragBehavior` | |
| 插槽连接 | `WorkflowSlotConnectionBehavior` | 只做两阶段命令派发，不做规则 |
| 插槽布局 | `WorkflowSlotLayoutBehavior` | 测量 + 写 `slot.Anchor` |
| 网格装饰器 / 小地图 | `WorkflowMinimapOverlay`（装饰器由模板/demo 提供并被 cast 成 `IWorkflowGridDecorator`，`WorkflowSurfaceBehavior.cs:1162-1179`） | |

**为什么这家没有第八个角色**：WPF 的渲染层不需要额外托管——连线视图自己 `OnRender` 画（`Examples/Workflow/WPF Trimmed/Demo/Views/Workflow/LinkView.xaml.cs`）。别家的额外文件都不是契约要求，是各家的平台补偿：MAUI 用 `WorkflowLinkOverlay` 换掉 `WorkflowCanvasTransformBehavior`（它不需要一个独立的变换载体，但需要一个链接层）；Avalonia 多一个 `PlatformDetection.cs`；WinForms 多一个 `NativeWindowStyleHelper.cs`（外加 `IWorkflowTemplateSelector` / `WorkflowTreeView` 等一整套基类）；Jalium 用 `WorkflowLinkBounds` 换掉 `WorkflowCanvasTransformBehavior`（渲染器按 `RenderSize` 盒裁剪的补偿，见 `adapters/jalium.md` §2.1；变换直接发在 `WorkflowSurfaceBehavior` 上）；Razor 的装饰器也在适配器里，且每个行为都是 `.razor` + `.razor.cs` 一对。

**唯一与 WPF 逐文件同构的是 WinUI**（8 个文件、同名同分法）。这条的实际用处：**想把 WPF 的适配器结构照搬到某一家之前，先确认目标是 WinUI；对另外五家都搬不过去。**

---

## 二、平台硬限制，与由此产生的做法

> 这一节是全文最有价值的部分：它决定别家能照抄什么、不能照抄什么。

### 2.1 一切以 `UserControl` 为界；不是就静默无效

宿主行为直接 cast `UserControl`（`WorkflowSurfaceBehavior.cs:350` 是附着属性回调，`:407` 是状态取用），或从事件/视觉源往上找最近的 UserControl（`:552`、`:651`、`:690`、`:1060`）；插槽布局同理（`WorkflowSlotLayoutBehavior.cs:78`、`:145`）。

⇒ 宿主根元素换成 `Border`/`Grid`，**整条行为链静默失效，不抛异常**。这就是 demo 里 Surface / NodeView / SlotView 全是 `UserControl` 的原因，也是 `FindName` 能工作的前提（见 2.2）。

**插槽连接行为的要求更宽一点**：只要 `Control` 就够（`WorkflowSlotConnectionBehavior.cs:24`）；而 `WorkflowNodeDragBehavior` 只要求 `UIElement`（`WorkflowNodeDragBehavior.cs:55`）。⇒ 三个行为三个门槛，**不要按其中一个去推其余两个**。

### 2.2 XAML 名字作用域是 UserControl 级，且不跨 `DataTemplate`

- 画布宿主的部件**全部靠字面名字找**：名字存在附着属性里，解析一律 `control.FindName(name)`（`WorkflowSurfaceBehavior.cs:440-489`）。⇒ `PART_ScrollViewer` 这些名字必须与宿主在**同一个 UserControl 的 XAML** 里。
- `DataTemplate` 里的元素**不在**这个作用域。所以节点模板内部的部件只能靠 `ItemsControl.ItemContainerGenerator.ContainerFromIndex(i)` 拿容器再下钻视觉树（`WorkflowSlotLayoutBehavior.cs:321-326`，下钻在 `:405-415` 的 `FindDescendantWithSlotDataContext`）—— **同一个 `FindName` 在这里永远返回 null**。
- 反过来：`Window.FindName` 看不进 `UserControl` 的作用域。小地图必须自己沿视觉树上溯找到 `UserControl` 再 `uc.FindName`（`WorkflowMinimapOverlay.cs:185-202`，注释 `:189` 明写这条限制）。

### 2.3 `ScrollViewer` 的 extent 是懒的：读之前必须强制 layout

这家最多次出现的舞蹈是 `ApplyLayout` + 三连 `UpdateLayout()`，然后才读最大值：

```
ApplyLayout(host, state);
state.Canvas?.UpdateLayout();
sv.UpdateLayout();
host.UpdateLayout();
maxH = GetHorizontalScrollMaximum(sv);   // WorkflowSurfaceMath.ScrollMax(ExtentWidth, ViewportWidth)
```

出现三处：缩放 `WorkflowSurfaceBehavior.cs:600-603`、被夹后重读 `:615-620`、平移越边时 `:1100-1103`。注释 `:599` 写明了理由（「让 ScrollViewer 先认下可能刚被自动扩过的 extent，再读 max，否则枢轴落偏、下一 tick 再修一次漂移」）。

**这条不能照抄到别家**：它依赖 WPF 的 layout 是同步可推进的。MAUI 的原生 extent 是**异步**重测的，那边必须 `await Task.Yield()` 两次（`Src/Adapters/VeloxDev.MAUI/Attached/Workflow/WorkflowSurfaceBehavior.cs` 的注释）—— 在 MAUI 上写这个三连是无效动作。

### 2.4 鼠标捕获的两处硬要求

- **`MouseUp` 必须用 `handledEventsToo: true` 挂**：`control.AddHandler(UIElement.MouseUpEvent, MouseUpHandler, true)`（`WorkflowSurfaceBehavior.cs:387`）。否则内部控件把事件标记为已处理时收不到释放，`IsPanning` 永久为真。
- **`Mouse.Capture(source)` + `LostMouseCapture` 复位**：捕获在 `:679`，拖拽在 `:1067-1123`；拖动途中左键松开也会走 `:1074-1083` 那条自愈分支。节点拖拽同理：`Mouse.Capture(control)`（`WorkflowNodeDragBehavior.cs:103`）、`LostMouseCapture` 复位（`:139-148`）。

### 2.5 `InvalidateVisual` 只能在 UI 线程，而属性通知可能来自别的线程

小地图的每个 DP 变更回调都转 `MarkDirty()`（`WorkflowMinimapOverlay.cs:271-298`），`MarkDirty` 自己判线程再分发（`:300-310`）：

```
if (Dispatcher.CheckAccess()) InvalidateVisual();
else Dispatcher.BeginInvoke(InvalidateVisual);
```

理由在注释 `:305`：节点的 `PropertyChanged` 可能从 TickManager 的循环线程到达（`BroadcastVisibleItemLayout` 那条路）。**这是 Core 驱动的、与平台无关的原因** —— 任何订阅节点 `PropertyChanged` 去做渲染的适配器都要处理它，只是各家的分发 API 不同。

### 2.6 立即模式渲染：小地图自己画，也因此要自己解决命中测试

- 画布全部靠 `OnRender(DrawingContext)` 画（`WorkflowMinimapOverlay.cs:435` 起），里面显式铺一层透明矩形（注释 `:437`、调用 `:440` 的 `dc.DrawRectangle(Brushes.Transparent, null, new Rect(sz))`）——**透明背景不是为了看，是为了有内容可命中**。
- 同时它还覆写了 `HitTestCore`，让整个元素在其 `RenderSize` 内都可命中（`:177-183`）。两件事都做了一遍。
- 交互靠自己覆写 `OnMouseLeftButtonDown/OnMouseMove/OnMouseLeftButtonUp/OnLostMouseCapture`（`:362-394`）。

### 2.7 插槽布局的触发源必须同时挂两个「布局相关」事件

`Loaded/Unloaded/DataContextChanged/IsVisibleChanged/LayoutUpdated/SizeChanged` 全挂（`WorkflowSlotLayoutBehavior.cs:100-107`），原因是注释 `:175-176` 写的：**`LayoutUpdated` 会在 arrange 途中触发**（子元素还没定），而 `SizeChanged` 只在节点尺寸最终定型后触发一次。只挂一个会在缩放折叠时读到中间态。实际同步还要再经一次 `Dispatcher.BeginInvoke(..., DispatcherPriority.Render)` 排队（`ScheduleSync`，`:215-232`）。

⇒ **别家照抄时最容易丢的就是这一对**：MAUI 那条已知教训正是「托管 `SizeChanged` 是 arrange 途中而非 post-arrange，必须改挂原生 `LayoutUpdated`」（背景见 `memory/modules/WorkflowSystem/adapters/maui.md` §5）—— WPF 这里两个都挂了，所以它不踩那个坑。

### 2.8 分批建视图的前提是「有优先级队列」

`ViewManager` 每次只造 3 个视图，然后 `Dispatcher.BeginInvoke(ProcessNextBatch, DispatcherPriority.Background)`（`ViewManager.cs:115-128`，`batchSize = 3` 在 `:125`），**再排下一批**。`Background` 的语义是「只在更紧急的消息处理完之后跑」，所以创建被摊进消息泵的空隙里。

这条**只在有 `DispatcherPriority` 的三家成立**。MAUI 的等价物用的是 16ms 一次的一次性定时器，它的注释把理由写得很直白：`// MAUI 没有 WPF 的 DispatcherPriority.Background`（`Src/Adapters/VeloxDev.MAUI/Attached/Workflow/ViewManager.cs:145`，`BatchSize = 8` 在 `:24`）。WinForms 干脆没有分批（Jalium 的 `ViewManager` 现在也按 `DispatcherPriority.Background` 三个一批，与 WPF 同）;

### 2.9 自带手势的否决：按下在宿主隧道相路由一次，句柄存表面，节点/插槽只读

句柄的产生与存放都在宿主行为里：`PreviewMouseDown` 的 `OnLinkPointerPressed`（`WorkflowSurfaceBehavior.cs:783`，挂在 `:378`）把这一笔路由、句柄写进 `SurfaceState.PressHandle`（字段在 `:46`、写入点 `:802`）；画布平移读它（`OnPointerPressed`，读在 `:671`；它挂在具名按下源的 `PreviewMouseDown` 上，`:476`），Ctrl+滚轮先路由再决定缩不缩（`:565-577`）。节点拖拽与插槽连接只**读** `WorkflowSurfaceBehavior.GetPressHandle(control)`（`:903-908`；`WorkflowNodeDragBehavior.cs:104`、`WorkflowSlotConnectionBehavior.cs:52`）—— 宿主的隧道处理器比它们先跑，所以这两个处理器里绝不能再路由一次。**取句柄要传控件、不能传 slot 模型**：`IWorkflowSlotViewModel` 不派生自 `DependencyObject`，`slot as DependencyObject` 恒为 null，那道检查会静默永不生效（`WorkflowSlotConnectionBehavior.cs:46-51` 的注释）。

---

## 三、与其它六家的刻意背离

1. **这家用原生 `PreviewMouseWheel` 拿滚轮，因此可以真的吃掉事件**：缩放那一支 `OnZoomPreviewMouseWheel` 挂在 `ScrollViewer` 的预览相上（`WorkflowSurfaceBehavior.cs:533` 由 `HookZoom` 装、受 `ZoomEnabled` 门控，末尾 `e.Handled = true` 在 `:641`）；**普通（非 Ctrl）滚轮是另一支** `OnLinkPointerWheel`，挂在**宿主的预览相**上（`:384` 装、`:402` 卸，处理器 `:833`；2026-10-11 起**置 `Handled`** —— 普通滚轮整笔改由适配器执行，见 [architecture.md §3.6](../architecture.md)）。普通这一支的相是 2026-10-06 换的：先前挂宿主的**冒泡**相 `MouseWheel`，实测两格滚轮 **0 行**到达 —— `ScrollViewer` 是宿主的**下代**，冒泡相里它先吃掉并标记 handled；改到预览相后 **2/2**。Avalonia 与 WinUI **没有** `PreviewMouseWheel`：Avalonia 的普通滚轮也挂在宿主的隧道相上（`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/WorkflowSurfaceBehavior.cs:467-469`），它的缩放那一支另挂 `ScrollViewer`（同文件 `:633`）。
⇒ **接一家新平台先确认它有没有 preview/tunnel 阶段**；**没有隧道相时的解法不是「忍一丝」**（2026-10-11 起更正）：挂**滚动容器本身**确实会漏 —— `Handled` 在 `ScrollContentPresenter` 已经滚完之后才跑（WinUI 实测一格漏 74 DIP，且 `handledEventsToo` 也救不回来）；但挂**滚动容器的内容**（画布）就**一点不漏**，它在冒泡路径上早于 presenter，`Handled` 真的挡得住。WinUI 改挂画布后实测一格 74、与基线逐字相同（改前若照旧挂容器会是 148）。

2. **空白判定里多一道按类名字符串识别连线的步骤**：`IsWorkflowLinkVisual` 判 `DataContext is IWorkflowLinkViewModel` **或** 类型名等于 `"BezierCurveView"`/`"PolylineCurveView"`（`WorkflowSurfaceBehavior.cs:1242-1246`，用在 `:1223`）。七家里只有 WPF 与 Avalonia（`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/WorkflowSurfaceBehavior.cs:1031-1035`）有这后一道类名判定；WinUI 的同名方法只判 `DataContext`（`Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:1189-1190`），Jalium 有独立的表面行为（`WorkflowSurfaceBehavior`）但它的空白判定只判 `DataContext`、没有按类名的白名单（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowSurfaceBehavior.cs` 的 `IsNodeOrSlotVisual`），MAUI 完全不用空白判定（无 `IsSurfaceBlank*`）—— 它在 Windows 上走原生指针事件平移，其余平台才用 `PanGestureRecognizer`（`Src/Adapters/VeloxDev.MAUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:740-753`）。⇒ 类名白名单只有 WPF 与 Avalonia 吃，换名字就静默失效（WPF 上的生效前提另见坑 1）。

3. **网格装饰器不在适配器里**：WPF 只按名字找它、cast 成 `IWorkflowGridDecorator` 并回填 `ScrollOffset*`/`ContentOffset*`（`WorkflowSurfaceBehavior.cs:1162-1179`），实现体由模板与 demo 提供（`Src/Templates/VeloxDev.WPF.Templates/working/content/workflow-grid-decorator/TemplateClass.cs`）。只有 Razor（`Src/Adapters/VeloxDev.Razor/Attached/Workflow/WorkflowGridDecorator.razor`）与 WinForms（`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowGridDecorator.cs`）把自家装饰器放进了适配器 —— **那是它们的背离，不是 WPF 少写了一个文件**。

4. **小地图的导航语义是「点哪哪成中心」**，不是「抓住视口块拖」。代码注释明写这是对齐 Jalium（`WorkflowMinimapOverlay.cs:367` 的 `// 与 Jalium 家一致：点击点一律成为视口中心 —— 指示块上没有抓取锚点，按在哪里都重新居中。`），Avalonia（`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/WorkflowMinimapOverlay.cs:450`）、WinUI（`Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowMinimapOverlay.cs:424`）、MAUI（`Src/Adapters/VeloxDev.MAUI/Attached/Workflow/WorkflowMinimapOverlay.cs:515`）与 Razor 的 JS（`Src/Adapters/VeloxDev.Razor/wwwroot/veloxdev.workflow.js:1221`）都是同一句。⇒ 这是**五家 + Razor JS 的一致行为**，不是 WPF 单独的选择；WPF 只是把「按下即中心」写成 `OnMouseLeftButtonDown` 里立刻 `NavigateToWorld` 再置 `_isDragging`（`:362-373`），所以按下的一瞬视口就跳了。**唯一与之冲突的是一句注释**，见坑 3。

5. **`Move` / `Replace` 两种集合变更不处理**：`ViewManager` 的 `switch` 只列 `Add`/`Remove`/`Reset`（`ViewManager.cs:59`/`:71`/`:82`）。WPF、Avalonia、WinUI、MAUI 四家都这样；**Jalium（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/ViewManager.cs:105`）与 WinForms（`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/ViewManager.cs:126`）处理 `Replace`**。⇒ 若你在 WPF 上依赖「原地替换 `VisibleItems` 里的元素」来刷新视图，**不会刷新，也不会报错**；要走 `Reset` 或先删后加。

6. **`WorkflowCanvasTransformBehavior.Transform` 的变更回调是故意空的**（`WorkflowCanvasTransformBehavior.cs:25-30`，注释：「这个属性只是通知载体；节点与连线视图各自在 XAML 里把自己的 `RenderTransform` 绑到它；宿主自身绝不能收到渲染变换」）。⇒ 别在这里补逻辑；它是被 `Apply`（`:22-23`）写、被 XAML 绑定读的单向值通道。

---

## 三点五、画布滚动与表面级按键（2026-10-11，探针实测）

这家是七家里第一个落地的（其余六家照它的形状铺开），数字都是探针量的：

- **普通滚轮整笔归适配器**：`OnLinkPointerWheel` 路由之后**无条件 `e.Handled = true`**，再按裁决决定这一笔——没人 `PreventDefault` 就自己竖滚。**宿主预览相上置 `Handled` 确实吃得住**：实测一格走 **48**，不是 96（若 `ScrollViewer` 也滚了就是双倍）。这条是 §三·1 那个「挂预览相」结论的直接后果。
- **步长** `SystemParameters.WheelScrollLines`（默认 3）× **16** = 48/格 —— 与 WPF 自己 `ScrollViewer` 在同档位下的步长一致（实测逐字相同）。`WheelScrollLines` 为 `-1`（按页滚）时退回视口高度。
- **键的 target 从「悬停的那条线」改成 `input.PointerTarget`**：树级的键（Ctrl+Z/Ctrl+Y）在指针位于空白画布或节点上时也到得了树。删掉的门是 `if (input.HoveredLink is null) return;`。Delete 仍然只吞它自己那一手。
- **焦点**：按下时把焦点交给宿主（先前只在**悬停到连线**时收，点空白画布收不到键）。落在 `TextBoxBase` 里的按下**不抢** —— 那里的 Ctrl+Z 撤的是文字。实测：点空白画布后 Ctrl+Z 生效（`节点 4/4 → 3/3 → 2/2 → 1/1 → 0/0`），Ctrl+Y 回到 `4/4`。
- **`ToKey` 补八个修饰键**：WPF 的 `Key` 实测 `LWin=70 / RWin=71 / LeftShift=116 … RightAlt=121`，而三段算术区间是 A–Z `44–69`、D0–D9 `34–43`、F1–F12 `90–101` ⇒ 修饰键都在区间外，不会被那三条 `if` 抢走。

## 四、坑（带依据）

1. **`IsWorkflowLinkVisual` 的类名白名单在 WPF 侧对一个 WPF 不存在的名字生效。** 白名单字符串是 `"BezierCurveView"`/`"PolylineCurveView"`（`WorkflowSurfaceBehavior.cs:1245-1246`），但 WPF 树下没有 `BezierCurveView` —— 那是 Avalonia demo 的类名（`Examples/Workflow/Avalonia/Demo/Views/Workflow/BezierCurveView.axaml.cs:21`）；完整版 demo 的连线视图是 `PolylineCurveView`（`Examples/Workflow/WPF/Demo/Views/Workflow/PolylineCurveView.xaml.cs:58`），模板产物是 `TemplateClass`（`Src/Templates/VeloxDev.WPF.Templates/working/content/workflow-link-view/TemplateClass.xaml.cs:15`），裁剪版 demo 是 `LinkView`。而这三个视图的构造里都设了 `IsHitTestVisible = false`（`PolylineCurveView.xaml.cs:98`、`LinkView.xaml.cs:29`、模板 `TemplateClass.xaml.cs:28`），连线视图因此不会成为指针事件的 `OriginalSource` —— 指针落在连线上时命中的是画布，`IsWorkflowLinkVisual` 在 WPF 的两个 demo 与模板里恒为 false。⇒ 改类名不会影响 WPF 的行为；这条分支是给「用户自己写的、可命中的连线视图」留的后备。（此结论由 `IsHitTestVisible = false` 的 WPF 命中测试语义推出，未跑运行时探针。）

2. **`WorkflowMinimapOverlay.RulerThickness` 这个 DP 在本文件里从未被读**：DP 注册在 `:63-65`，CLR 访问器在 `:139`，而 `RulerBand` 硬编码返回 0（`:132`）。真正把标尺厚度接上的是**网格装饰器**（`Src/Templates/VeloxDev.WPF.Templates/working/content/workflow-grid-decorator/TemplateClass.cs:134` 的 `RulerBand => RulerThickness`）。⇒ 想让小地图也避让标尺，得先在这里把 `:132` 换成读 DPs，而不是设一个没人读的 `RulerThickness`。

3. **【注释与代码不符，跨文件】** `Src/Adapters/VeloxDev.Razor/Attached/Workflow/WorkflowMinimapOverlay.razor.cs:24-25` 的 XML 注释写着「Navigation is grab-the-block (**matching the other adapters**)」，但 Razor 自己的 JS 注释写着 `// MINIMAP — always-center navigation (matching the Jalium adapter)`（`Src/Adapters/VeloxDev.Razor/wwwroot/veloxdev.workflow.js:1221`），且 WPF/Avalonia/WinUI/MAUI 四家的 C# 注释全是 always-center。⇒ **错的是 Razor 的 C# 注释**，代码是 always-center。WPF 这边（`:367`）的注释与代码一致，可以信。这正是「源码注释与代码矛盾时要显式标出」的样本：注释写的是「和别人一样」，而它说反了。

4. **Ctrl 判定用精确相等，不是 `HasFlag`**：`Keyboard.Modifiers != ModifierKeys.Control`（`WorkflowSurfaceBehavior.cs:558`，WinForms 同形：`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowSurfaceBehavior.cs:50` 的 `Control.ModifierKeys != Keys.Control`）；另外三家用 `HasFlag`：`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/WorkflowSurfaceBehavior.cs:657`、`Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:560`、`Src/Adapters/VeloxDev.MAUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:902`。⇒ **Ctrl+Shift+滚轮在 WPF 与 WinForms 上不缩放，在这三家上缩放**；Jalium 的表面行为在 `ScrollViewer` 预览相上按 `e.KeyboardModifiers != ModifierKeys.Control` 处理（精确相等，与 WPF/WinForms 同侧，`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowSurfaceBehavior.cs:566`）；它的非 Trimmed demo 另有两处：窗口级 `Keyboard.Modifiers == ModifierKeys.Control`（精确相等，`Examples/Workflow/Jalium/Demo/MainWindow.cs:460`）与画布内 `NodeEditorSurface.OnZoomMouseWheel` 的 `HasFlag`（`Examples/Workflow/Jalium/Demo/Views/Workflow/NodeEditorSurface.cs:369`）。代码与注释里**没有**记录这是刻意还是遗漏 —— 别猜，按现状保留或统一都可以，但别以为七家一致。

5. **每个滚动 tick 都会全量重解析部件、并新建一个 `TranslateTransform`**：`ScrollViewer.ScrollChanged` → `Refresh(host)`（`:1053-1065`）→ `ResolveNamedControls`（`:440-489`，先全部退订再逐个 `FindName` 重找重订）+ `ApplyLayout`（`:1125-1140`，`:1132` 处 `new TranslateTransform(...)`，`WorkflowCanvasTransformBehavior.Apply` 写进去）。⇒ 滚动期间每个节点/连线模板绑定的那个变换实例都在被替换。**知道就好**：要在这里加缓存或改绑定时，先确认这个刷新不是必需的。

6. **节点拖拽的落点判定依赖 `DataContext`，坐标宿主按名字或按类型找**：节点从 `control.DataContext` 起、取不到再沿祖先找 `IWorkflowNodeViewModel`（`WorkflowNodeDragBehavior.cs:176-186`）；坐标宿主先按名字，再按 `CoordinateHostType`（默认 `typeof(Canvas)`，`:150-162`）。⇒ **节点视图的 DataContext 必须是节点 VM**（或祖先上有），而坐标宿主默认是 `Canvas` —— 换成 `Grid` 必须显式给 `CoordinateHostType`。

7. **插槽锚点用 `TranslatePoint` 换算到坐标宿主，再交给 Core 的换算函数**：`control.TranslatePoint(节点中心, coordinateHost)` 后调 `WorkflowSurfaceMath.SlotAnchorFromVisualCenter`（`WorkflowSlotLayoutBehavior.cs:343-356`，注释 `:343` 解释了为什么是「世界 + ActualOffset」）。坐标宿主取不到时退回 `SlotAnchorFromNode`（`:354-355`）。⇒ 两种坐标系数值上不同源，**混用会得到系统性偏移**，选哪个取决于你测到的是哪个坐标系。

8. **`ViewManager` 的池按运行时类型分桶，隐藏的视图留在 `panel.Children` 里不清树**：池是 `Dictionary<Type, Queue<FrameworkElement>>`（`ViewManager.cs:16`），复用前 `Visibility.Collapsed` + `DataContext = null`（`:184-185`、`:205-206`）。`DataTemplate` 查找是三级回退 + 缓存（`:165` 调 `FindDataTemplate`，`:228` 起：`TemplateSelector` → 视觉树 `Resources` → `Application.Resources`）。⇒ 「视图消失了但仍在树上」是设计；**不要用 `panel.Children.Count` 表示可见视图数**，用 `VisibleItems`。

9. **`ViewPool` 的管理器挂在 `ConditionalWeakTable<Panel, ViewManager>` 上，并在 `Panel.Unloaded` 时清理**（`ViewPool.cs:38`、`:53-61`）。⇒ 面板一旦被卸载，池与所有复用视图一起作废 —— 把工作流面板放进一个会被反复卸载/重建的容器（切换 Tab、`ContentControl` 换模板）时，每次都要重新付一遍建视图的成本。

10. **平移越边会主动扩张画布，因此平移中必须重读两次 max 并把 `PanStartOffset` 重设**（`WorkflowSurfaceBehavior.cs:1098-1112`）：`layoutChanged` 时先 `ApplyLayout` + 三连 `UpdateLayout` 再重读 max，并用**实际落地的偏移**（而非期望偏移）重置 `PanStart`/`PanStartOffset`。少这一步的表现是拖到边缘后指针「甩开」画布。这条的数学在 Core（`WorkflowSurfaceMath.ClampScrollOffset` 的 `extendRatio`，`:1092-1095` 传的是 `DefaultPanExtendRatio`）。

11. **`UpdateGridDecorator` 必须把 `RulerBand` 转发给虚拟化内缩**（`:1162-1179`，`:1177` 的 `viewModel.SetVirtualizeInset(left: decorator.RulerBand, top: decorator.RulerBand)`），否则浮在顶/左边的标尺下面那些节点会被判为不可见。这是 extension.md §3.8 那条「`RulerBand` 要转发」在这家的具体落点，**照抄时最容易只抄 `ScrollOffset*` 三个赋值而漏掉这一行**。

12. **`UpdateVisibleRegion` 同时写 `Viewport` 和 `Layout.ViewportOffset`**（`:1142-1160`，后者在 `:1159`）—— 后者是为了序列化往返保留视口位置。⇒ 只写 `Viewport` 的话视图是对的，但保存再加载会丢掉「上次看到哪」。
    2026-10-03 起另一半也在这家：`Refresh` 里 `CaptureViewportRestore`（在 `UpdateVisibleRegion` **之前**取值）+
    `QueueViewportRestore`（末尾排 `Dispatcher.BeginInvoke(…, DispatcherPriority.Loaded)`），滚到
    `ViewportRestoreScroll` 并按 `ClampValue` 夹到 `GetHorizontalScrollMaximum`。宿主不再自己滚。见 [../extension.md](../extension.md) §3.9-10。

13. **悬停连线会让画布自己滚一段 —— 是「取焦点」带来的 WPF 默认行为，不是本仓库的代码。** 悬停取焦点的落点现在在适配器：`WorkflowSurfaceBehavior.FocusHoveredLink`（`WorkflowSurfaceBehavior.cs:966-980`，调用点 `:778`、`:864`）在指针进入/移动/离开/按下时把焦点交给画线的那台控件 —— 视图 `Focusable = true`（`Examples/Workflow/WPF/Demo/Views/Workflow/PolylineCurveView.xaml.cs:99`），它不可聚焦时才退回宿主；焦点是 Delete 能冒泡到宿主的 `OnLinkKeyDown`（`WorkflowSurfaceBehavior.cs:984-1012`）需要的。WPF 的 `FrameworkElement` 在获得焦点时替它请求 `RequestBringIntoView`，`ScrollContentPresenter` 的类处理照办 ⇒ `ScrollViewer` 偏移跳变，**与按键无关**（实测 `left=Released`）。跳多远由当时的偏移与 extent 决定，不是固定值：连线视图的尺寸绑的是祖先 `Canvas`（`Examples/Workflow/WPF/Demo/Views/Workflow/WorkflowView.xaml:69-70`）⇒ 它的包围盒就是整块画布。

    实测（完整版 demo，extent 2400×850、viewport 969×723）：纯悬停扫过线身 4 次，偏移跳 **28 / 412.8 / 362 / 502.1 px**；每跳一次 extent 还被撑大（**850 → 1293 → 1487**，平移越边扩张的连带效应），所以画布会越跳越大。修法是**在发源地吃掉这条请求**（`Examples/Workflow/WPF/Demo/Views/Workflow/PolylineCurveView.xaml.cs:117` 的 `AddHandler(RequestBringIntoViewEvent, … e.Handled = true)`）—— 保留焦点、只拦滚动。修后同一把尺子复测：纯悬停滚动 **4 → 0**，由连线焦点引起的二次抛出 **4 → 0**（另有 1 次 `REQ target=ScrollViewer` 是同一轮里人按鼠标那下带来的，`left=Pressed`，与连线无关）。

    **量这条时的两个坑**：(a) 先用 `ScrollTo*` 把偏移预设到别处再悬停 ⇒ 症状被掩盖（它依赖当时的偏移），要照真实用法从启动状态扫；(b) 合成光标（`SetCursorPos`）**必须**先 `SetWindowPos(HWND_TOPMOST)` + `SetForegroundWindow` 把窗口推到最前，否则一次都命不中、日志里连 `over ->` 都没有 —— 我第一次就是这样量到「0 次」的。

    **别家（未核）**：WinUI 的 `ScrollViewer.BringIntoViewOnFocusChange` 默认同为 `true`，而这家也有同形的悬停取焦点 —— 由适配器做，取的是宿主 `UserControl` 而非连线视图（`host.Focus(FocusState.Pointer)`，`Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:798`、`:847`）⇒ 同一症状在 WinUI 上可能存在，实测前别断言没有。Avalonia 无此默认行为，同形的取焦点也在适配器（`FocusHoveredLink`，`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/WorkflowSurfaceBehavior.cs:345-358`），未见此症状。

14. **`OnLinkKeyDown` 现在只吞 `Delete`，但别把它读成「画布不再丢键盘」。** 先前无条件 `e.Handled = true`（指针停在连线上时方向键、翻页键、空格一并被吞），2026-10-06 收窄成只在 `PlatformInput.Key.Delete` 上置（`WorkflowSurfaceBehavior.cs:984-1012`，置位在 `:1008-1011`；与 Avalonia 的「非 Delete 直接 `return`」（`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/WorkflowSurfaceBehavior.cs:850-851,861`）、Jalium 的「只吞 Delete」同形）。**实测（完整版 demo，悬停连线上按 PageDown）**：改前与改后视口都从 `0,0` 滚到 `0,437` —— `ScrollViewer` 是宿主的**下代**，冒泡相里它先处理，宿主再置 `Handled` 追不上它；被吞的从来只是继续往**宿主之上**（窗口级处理器）的那一段。所以这是一笔对齐（不再替宿主的窗口级键盘做决定），**不是**「画布对键盘整段无响应」的修复。

---

## 五、非 Trimmed demo 的连线交互落点（含右键菜单）

现在连线交互不再挤在连线视图里：视图只画线（构造里 `IsHitTestVisible = false`，`Examples/Workflow/WPF/Demo/Views/Workflow/PolylineCurveView.xaml.cs:98`），**指针底下是谁由适配器解析**（节点 / 插槽取视图的数据上下文，认不到才回退 Core 的共享曲线判定 `tree.HitTestVisibleLinks`），**高亮与删除归宿主/demo**，指针与按键的转发归适配器，菜单的**接线**也归适配器。模板与两个 demo 只剩一行附着属性 `behaviors:WorkflowSurfaceBehavior.LinkMenuKey="LinkContextMenu"`（`Src/Templates/VeloxDev.WPF.Templates/working/content/workflow-tree-view/TemplateClass.xaml:15`、`Examples/Workflow/WPF Trimmed/Demo/Views/Workflow/TreeView.xaml:14`、`Examples/Workflow/WPF/Demo/Views/Workflow/WorkflowView.xaml:19`）与那个带键资源本身（条目由用户增删）；模板与 Trimmed 的 `TreeView.xaml.cs` 现在只剩 `InitializeComponent()`（`TemplateClass.xaml.cs`、`TreeView.xaml.cs` 全文仅构造函数），完整版 `WorkflowView.xaml.cs` 仍保留 Load/Save/MCP 等宿主代码，但已无一行连线交互。所以本节对模板与两个 demo 都成立。

> 2026-10-03：接线在**适配器**的 `WireLinkMenu`（模板与 demo 的 code-behind 没有一行连线事件代码），与 WinUI/MAUI 已是同一形状（它们的模板同样只带 `LinkMenuKey`）。

| 事 | 落点 | 依据 |
|---|---|---|
| 命中 | 适配器先从命中元素的数据上下文解析出节点/插槽，解析不到才把这笔交给 Core 的共享曲线判定（`WorkflowSurfaceBehavior.RoutePointer` → `ResolveTarget`，回退到 `tree.HitTestVisibleLinks(x, y, input.HitRadius)`）；曲线判定被测的是视图发布的 `LinkCurve`（`PolylineCurveView.xaml.cs:347` 的 `link.PublishCurve(_curve, this)`） | `Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowSurfaceBehavior.cs:854-866`（`RoutePointer`）、`:881-900`（`ResolveTarget`，回退在 `:899`）；`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Interaction/LinkHitTestEx.cs:54-93`；半径 `LinkHitTestEx.cs:18` 的 `DefaultHitRadius = 6d` |
| 高亮 | 宿主/demo：连线视图订**自己** helper 的 `Input.PointerEntered` / `PointerExited`（`PolylineCurveView.xaml.cs:229-230`），据 `HoveredLink` 写自己的 `IsHighlighted`（`:242` / `:244`，读它换色见 `:395-396`）；Core 里没有 `ILinkHighlight` / `AutoHighlight` | `Examples/Workflow/WPF/Demo/Views/Workflow/PolylineCurveView.xaml.cs:229-230,242-244,395-396` |
| 悬停取焦点 | 适配器：`WorkflowSurfaceBehavior.FocusHoveredLink` 把焦点交给画线的那台控件，不可聚焦才退回宿主 | `WorkflowSurfaceBehavior.cs:966-980`；调用点 `:778`、`:864` |
| 右键菜单 | 适配器（表面）：按 `LinkMenuKey` 解析带键资源、订输入面 `Input.PointerPressed` 与树的 `LinkRemoved`、设 `DataContext` 后自己定位弹出 | `Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowSurfaceBehavior.cs:166`（`WireLinkMenu`，`Refresh` 在 `:157` 调）、`:267`（`ShowLinkMenu`）、`:232`（`UnwireLinkMenu`，`Detach` 在 `:410` 调）；`LinkMenuKeyProperty` 在 `:111` |
| 删除 | 菜单项绑 `Command="{Binding DeleteCommand}"`；Delete 键由**宿主**执行（库只把 `KeyDown` 路由过来），demo 订树 helper 的 `Input.KeyDown` 执行 `link.DeleteCommand` | `WorkflowView.xaml:95`；`Examples/Workflow/WPF/Demo/Views/Workflow/WorkflowView.xaml.cs:611-622` |

**四条结论：**

1. **菜单项现在是绑命令的，安全来自「每次弹出前重设菜单的 `DataContext`」。** 菜单是表面的**声明式带键资源**（`WorkflowView.xaml:94-96`），不挂在会被池化改绑的连线视图上，所以「独立视觉树 `DataContext` 不跟过来、绑定指向旧 VM」在这条路上不存在：适配器每次弹出前 `state.LinkMenu.DataContext = link`（`WorkflowSurfaceBehavior.cs:281`），条目因此总绑到「这次右键的那条」。增删条目只改 XAML。
2. **菜单是 `UserControl.Resources` 里的带键 `ContextMenu`，不是 `<Border.ContextMenu>`。** 后者会让 WPF 的 `ContextMenuService` 在任意右键自行弹出、绕开适配器；带键资源没人替它开，只有适配器在 `ShowLinkMenu` 里 `state.LinkMenu.IsOpen = true`（`WorkflowSurfaceBehavior.cs:302`）—— 那条「用 WPF 自带开启时机」的老路连同它的 `ContextMenuOpening` 取消一起消失了。**空白处仍不开菜单，但换了两道闸**：连线视图 `IsHitTestVisible = false`（`PolylineCurveView.xaml.cs:98`），WPF 不会从它身上弹；适配器读到 `e.Target is not IWorkflowLinkViewModel` 直接返回（`WorkflowSurfaceBehavior.cs:271`）—— 这笔按下的 Target 现在也可能是节点/插槽（见 §2.9），同样被这道闸挡掉。否决在链上更靠前的一级（连线自己）订同一个 `PointerPressed` 并置 `e.Handle.PreventDefault`，`ShowLinkMenu` 读它（`WorkflowSurfaceBehavior.cs:276`）。**命中面仍是画出来的描边、不是视图的整块框**：视图的 `Width/Height` 绑的是 `Canvas.ActualWidth/Height`（`WorkflowView.xaml:69-70`），但命中由 Core 对发布曲线判距（半径见末段），离线约 20px 的空画布照样没有菜单 —— 视图框一旦变成命中面（例如把 `HitTest` 换成包围盒），这里先坏。
3. **定位用 `PlacementMode.AbsolutePoint`，不是 `MousePoint`。** 画布坐标先经 `WorkflowSurfaceMath.ToScreen`（world + ActualOffset）回到画布局部（`WorkflowSurfaceBehavior.cs:285`；`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Math/WorkflowSurfaceMath.cs:47-48`），再由 `PART_Canvas.PointToScreen` 换设备像素（`:286`）；`AbsolutePoint` 吃 DIP，最后按设备比例除回去（`:300-301`）。**这家多目标 `netframework4.6.1`，它没有 `VisualTreeHelper.GetDpi`**，所以适配器用 `#if NETFRAMEWORK` 退回 `PresentationSource…CompositionTarget.TransformToDevice`（`:288-296`），现代 TFM 才走 `GetDpi` —— 两条给的是同一个缩放，别把 `#if` 当成死代码删掉。二者会分开：`MousePoint` 拿的是屏幕点，而这里要的是「画布上那个世界点」，缩放/高 DPI 下不是同一个位置。
4. **菜单开合由表面自己记账，`IsSuspended` 仍归 Core 的输入路由。** `WireLinkMenu` 把这份菜单的 `Opened`/`Closed` 接到表面自己的两个处理器（`WorkflowSurfaceBehavior.cs:190-201`）：`Opened` 置 `input.IsSuspended = true`、`Closed` 复位并清 `MenuLink` —— 模板/demo 都不再自己记账。**「菜单不能比它指着的那条线活得久」由树既有的 `LinkRemoved` 实现**：菜单针对的那条连线一旦离开 `tree.Links`（Delete 键，或 Agent/Undo 等任何删除路径），树报 `LinkRemoved`；表面**仅当 `ReferenceEquals(state.MenuLink, link)`** 时 `state.LinkMenu.IsOpen = false`（`WorkflowSurfaceBehavior.cs:222-227`），关掉照常触发 `Closed` 释放挂起。Core 不替适配器关 popup，输入路由也不自行释放 `IsSuspended` —— 检测归树的 `LinkRemoved`，关窗归适配器。挂起期间 `WorkflowInput.Route` 不再改指针目标，`Exited` 一并被挡（`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Events/WorkflowInput.cs:203`的 `if (IsSuspended) return;`）—— 面弹菜单确实把指针从画布上拿走（`Exited` 照发，`WorkflowSurfaceBehavior.cs:764`），但悬停目标不会因此被清（`Src/Core/VeloxDev.Core.Test/WorkflowSystem/MenuDismissTests.cs` 的 `Route_Suspended_WaitsOutTheMenusOwnPointerTraffic` 已把它钉成测试）。适配器也不再自己挡：`OnLinkPointerExited` 现在无条件发 `Exited`（`WorkflowSurfaceBehavior.cs:764`）。**别再回到平台侧在 `MouseLeave` 里按「菜单是否打开」跳过取消悬停的老路** —— 那样 popup 关闭后没有配对的 `Entered`，悬停会永久留在画布上；复位现在有唯一负责人（表面把 `Closed` 报给输入路由）。

**实测（2026-09-26，SendInput + 闭环伺服取点，每一步先断言）**：指针经伺服落在线体上（48×48 邻域内体色像素 ≈160，佐证命中面是「画出来的描边」；随后同一点变暖色 ≈280 = 高亮）→ 合成右键 → **原生 `ContextMenu` 弹出，只有「Delete」一项**（菜单左上角就是鼠标点）→ 合成左键点该项 → **那条线消失**：两端端口由橙/绿变灰、左栏「可见组件数（Node / Link）」（`WorkflowView.xaml:162`）16 → 15。也就是说这几件事在这家**都真的跑得通**，且「命中的是人画出来的描边」这一点由伺服日志本身佐证。

`hitRadius = 6.0` 现在是 Core 的 `LinkHitTestEx.DefaultHitRadius = 6d`（`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Interaction/LinkHitTestEx.cs:18`），`WorkflowInput.HitRadius` 以它作默认（`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Events/WorkflowInput.cs:71`）。它与框架给的带宽同量级（最外那圈辉光是本体 + 9px，半宽 ≈ 5.5px），所以既不放大也不缩小实际命中范围；它对**右键**这条路径仍是活的判据 —— 适配器 `RoutePointer` 先解析节点/插槽、解析不到才经 `ResolveTarget` 用 `HitTestVisibleLinks`（`WorkflowSurfaceBehavior.cs:899`）；三者都认不到时 `e.Target` 才为 null，适配器据此不弹（`WorkflowSurfaceBehavior.cs:271`）。

---

## 六、这份文件没写的东西

- 七个角色各自要暴露什么成员、附着属性叫什么名字、`PART_*` 命名约定 —— 在 `memory/modules/WorkflowSystem/extension.md` §3.9 与 `skills/veloxdev-create-workflow/references/view-layer.md`。
- 怎么在 WPF 上从零搭一个工作流视图（XAML 片段、绑定写法、demo 位置）—— 在 `skills/veloxdev-create-workflow/references/gui/wpf.md`（其中「本适配器**没有**连线交互 helper」那条也以那份为准）。
- 坐标换算与缩放的数学 —— 在 `WorkflowSurfaceMath`（`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Math/WorkflowSurfaceMath.cs`），七家共用，不在本文。
- 节点拖拽需要真 `Background` 这类 WPF 命中测试通则 —— 见模板给拖拽头铺的 `Background="Transparent"`（`Src/Templates/VeloxDev.WPF.Templates/working/content/workflow-node-view/TemplateClass.xaml:29-31`）。
