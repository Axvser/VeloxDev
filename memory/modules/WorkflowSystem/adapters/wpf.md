# WorkflowSystem — WPF

> **读法**：契约（七个视图角色、附着属性、注册位置、联动清单）在 `memory/modules/WorkflowSystem/extension.md` §3.9 与 §4.3，
> 本文不重复；人面向的「怎么搭一个 WPF 工作流视图」在 `skills/veloxdev-create-workflow/references/gui/wpf.md`，
> 逐角色职责表在 `skills/veloxdev-create-workflow/references/new-adapter.md` / `references/view-layer.md`。
> 本文只写这家的**硬限制、刻意背离、该家特有的坑**。代码在 `Src/Adapters/VeloxDev.WPF/Attached/Workflow/`。
> **路径写法**：下文的裸文件名（`WorkflowSurfaceBehavior.cs:113`、`ViewManager.cs:125` 等）都相对 `Src/Adapters/VeloxDev.WPF/Attached/Workflow/`；引用 Core 或别家时一律写全路径。

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
| 网格装饰器 / 小地图 | `WorkflowMinimapOverlay`（装饰器由模板/demo 提供并被 cast 成 `IWorkflowGridDecorator`，`WorkflowSurfaceBehavior.cs:604-614`） | |

**为什么这家没有第八个角色**：WPF 的渲染层不需要额外托管——连线视图自己 `OnRender` 画（`Examples/Workflow/WPF Trimmed/Demo/Views/Workflow/LinkView.xaml.cs`）。别家的额外文件都不是契约要求，是各家的平台补偿：MAUI 用 `WorkflowLinkOverlay` 换掉 `WorkflowCanvasTransformBehavior`（它不需要一个独立的变换载体，但需要一个链接层）；Avalonia 多一个 `PlatformDetection.cs`；WinForms 多一个 `NativeWindowStyleHelper.cs`；Jalium 多三个（`WorkflowGridDecorator` 自带装饰器、`WorkflowTreeView`、`IWorkflowTemplateSelector` —— 最后一个别家也有同形的：WinForms 在 `Src/Adapters/VeloxDev.WinForms/Attached/Workflow/ViewManager.cs:14-22` 里是一个连注释都一样的接口，见 `adapters/jalium.md` §1.1）；Razor 的装饰器也在适配器里，且每个行为都是 `.razor` + `.razor.cs` 一对。

**唯一与 WPF 逐文件同构的是 WinUI**（8 个文件、同名同分法）。这条的实际用处：**想把 WPF 的适配器结构照搬到某一家之前，先确认目标是 WinUI；对另外五家都搬不过去。**

---

## 二、平台硬限制，与由此产生的做法

> 这一节是全文最有价值的部分：它决定别家能照抄什么、不能照抄什么。

### 2.1 一切以 `UserControl` 为界；不是就静默无效

宿主行为直接 cast `UserControl`（`WorkflowSurfaceBehavior.cs:113` 是附着属性回调，`:256` 是状态取用），或从事件/视觉源往上找最近的 UserControl（`:294`、`:386`、`:418`、`:494`）；插槽布局同理（`WorkflowSlotLayoutBehavior.cs:78`、`:145`）。

⇒ 宿主根元素换成 `Border`/`Grid`，**整条行为链静默失效，不抛异常**。这就是 demo 里 Surface / NodeView / SlotView 全是 `UserControl` 的原因，也是 `FindName` 能工作的前提（见 2.2）。

**插槽连接行为的要求更宽一点**：只要 `Control` 就够（`WorkflowSlotConnectionBehavior.cs:21`）；而 `WorkflowNodeDragBehavior` 只要求 `UIElement`（`WorkflowNodeDragBehavior.cs:55`）。⇒ 三个行为三个门槛，**不要按其中一个去推其余两个**。

### 2.2 XAML 名字作用域是 UserControl 级，且不跨 `DataTemplate`

- 画布宿主的部件**全部靠字面名字找**：名字存在附着属性里，解析一律 `control.FindName(name)`（`WorkflowSurfaceBehavior.cs:182-231`）。⇒ `PART_ScrollViewer` 这些名字必须与宿主在**同一个 UserControl 的 XAML** 里。
- `DataTemplate` 里的元素**不在**这个作用域。所以节点模板内部的部件只能靠 `ItemsControl.ItemContainerGenerator.ContainerFromIndex(i)` 拿容器再下钻视觉树（`WorkflowSlotLayoutBehavior.cs:328-333`，下钻在 `:413-423` 的 `FindDescendantWithSlotDataContext`）—— **同一个 `FindName` 在这里永远返回 null**。
- 反过来：`Window.FindName` 看不进 `UserControl` 的作用域。小地图必须自己沿视觉树上溯找到 `UserControl` 再 `uc.FindName`（`WorkflowMinimapOverlay.cs:185-204`，注释 `:189-191` 明写这条限制）。

### 2.3 `ScrollViewer` 的 extent 是懒的：读之前必须强制 layout

这家最多次出现的舞蹈是 `ApplyLayout` + 三连 `UpdateLayout()`，然后才读最大值：

```
ApplyLayout(host, state);
state.Canvas?.UpdateLayout();
sv.UpdateLayout();
host.UpdateLayout();
maxH = GetHorizontalScrollMaximum(sv);   // WorkflowSurfaceMath.ScrollMax(ExtentWidth, ViewportWidth)
```

出现三处：缩放 `WorkflowSurfaceBehavior.cs:332-338`、被夹后重读 `:348-353`、平移越边时 `:535-539`。注释 `:329-331` 写明了理由（「让 ScrollViewer 先认下可能刚被自动扩过的 extent，再读 max，否则枢轴落偏、下一 tick 再修一次漂移」）。

**这条不能照抄到别家**：它依赖 WPF 的 layout 是同步可推进的。MAUI 的原生 extent 是**异步**重测的，那边必须 `await Task.Yield()` 两次（`Src/Adapters/VeloxDev.MAUI/Attached/Workflow/WorkflowSurfaceBehavior.cs` 的注释）—— 在 MAUI 上写这个三连是无效动作。

### 2.4 鼠标捕获的两处硬要求

- **`MouseUp` 必须用 `handledEventsToo: true` 挂**：`control.AddHandler(UIElement.MouseUpEvent, MouseUpHandler, true)`（`WorkflowSurfaceBehavior.cs:137`）。否则内部控件把事件标记为已处理时收不到释放，`IsPanning` 永久为真。
- **`Mouse.Capture(source)` + `LostMouseCapture` 复位**：捕获在 `:407`，拖拽在 `:501-558`；拖动途中左键松开也会走 `:508-517` 那条自愈分支。节点拖拽同理：`Mouse.Capture(control)`（`WorkflowNodeDragBehavior.cs:103`）、`LostMouseCapture` 复位（`:139-148`）。

### 2.5 `InvalidateVisual` 只能在 UI 线程，而属性通知可能来自别的线程

小地图的每个 DP 变更回调都转 `MarkDirty()`（`WorkflowMinimapOverlay.cs:275-302`），`MarkDirty` 自己判线程再分发（`:304-316`）：

```
if (Dispatcher.CheckAccess()) InvalidateVisual();
else Dispatcher.BeginInvoke(InvalidateVisual);
```

理由在注释 `:309-311`：节点的 `PropertyChanged` 可能从 MonoBehaviourManager 的循环线程到达（`BroadcastVisibleItemLayout` 那条路）。**这是 Core 驱动的、与平台无关的原因** —— 任何订阅节点 `PropertyChanged` 去做渲染的适配器都要处理它，只是各家的分发 API 不同。

### 2.6 立即模式渲染：小地图自己画，也因此要自己解决命中测试

- 画布全部靠 `OnRender(DrawingContext)` 画（`WorkflowMinimapOverlay.cs:442` 起），里面显式铺一层透明矩形（注释 `:444`、调用 `:447` 的 `dc.DrawRectangle(Brushes.Transparent, null, new Rect(sz))`）——**透明背景不是为了看，是为了有内容可命中**。
- 同时它还覆写了 `HitTestCore`，让整个元素在其 `RenderSize` 内都可命中（`:177-183`）。两件事都做了一遍。
- 交互靠自己覆写 `OnMouseLeftButtonDown/OnMouseMove/OnMouseLeftButtonUp/OnLostMouseCapture`（`:368-401`）。

### 2.7 插槽布局的触发源必须同时挂两个「布局相关」事件

`Loaded/Unloaded/DataContextChanged/IsVisibleChanged/LayoutUpdated/SizeChanged` 全挂（`WorkflowSlotLayoutBehavior.cs:100-107`），原因是注释 `:178-180` 写的：**`LayoutUpdated` 会在 arrange 途中触发**（子元素还没定），而 `SizeChanged` 只在节点尺寸最终定型后触发一次。只挂一个会在缩放折叠时读到中间态。实际同步还要再经一次 `Dispatcher.BeginInvoke(..., DispatcherPriority.Render)` 排队（`ScheduleSync`，`:220-237`）。

⇒ **别家照抄时最容易丢的就是这一对**：MAUI 那条已知教训正是「托管 `SizeChanged` 是 arrange 途中而非 post-arrange，必须改挂原生 `LayoutUpdated`」（背景见 `memory/workflow-zoom-slot-anchor-sync.md`）—— WPF 这里两个都挂了，所以它不踩那个坑。

### 2.8 分批建视图的前提是「有优先级队列」

`ViewManager` 每次只造 3 个视图，然后 `Dispatcher.BeginInvoke(ProcessNextBatch, DispatcherPriority.Background)`（`ViewManager.cs:115-128`，`batchSize = 3` 在 `:125`），**再排下一批**。`Background` 的语义是「只在更紧急的消息处理完之后跑」，所以创建被摊进消息泵的空隙里。

这条**只在有 `DispatcherPriority` 的三家成立**。MAUI 的等价物用的是 16ms 一次的一次性定时器，它的注释把理由写得很直白：`// MAUI lacks WPF's DispatcherPriority.Background`（`Src/Adapters/VeloxDev.MAUI/Attached/Workflow/ViewManager.cs:134-135`，`BatchSize = 8` 在 `:23`）。WinForms 与 Jalium 干脆没有分批。

---

## 三、与其它六家的刻意背离

1. **这家用原生 `PreviewMouseWheel` 拿滚轮，因此可以真的吃掉事件**（`WorkflowSurfaceBehavior.cs` 的 `OnZoomPreviewMouseWheel`，末尾 `e.Handled = true` 在 `:376`）。Avalonia 与 WinUI **没有** `PreviewMouseWheel`：Avalonia 在 `ScrollViewer` 上用 `RoutingStrategies.Tunnel` 加 `AddHandler`（`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/WorkflowSurfaceBehavior.cs:241-242` 的注释），WinUI 在 `ScrollViewer` 上 `AddHandler(..., handledEventsToo: true)`（`Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:278-281`，并明说「Ctrl+滚轮可能会先滚一丝」）。⇒ **要接一家新平台，先确认它有没有 preview/tunnel 阶段；没有就得付「先滚一丝」的代价，这不是 bug。**

2. **空白判定里多一道按类名字符串识别连线的步骤**：`IsWorkflowLinkVisual` 判 `DataContext is IWorkflowLinkViewModel` **或** 类型名等于 `"BezierCurveView"`/`"PolylineCurveView"`（`WorkflowSurfaceBehavior.cs:678-682`，用在 `:659` 与 `:661`）。七家里只有 WPF、Avalonia（`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/WorkflowSurfaceBehavior.cs:605-606`）、WinUI（`Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:740-741`）有这一步；Jalium 有同名的 `IsSurfaceBlankInteraction` 但**没有**这一步（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowSurfaceBehavior.cs:634-648`），MAUI 则完全不用空白判定 —— 它用 `PanGestureRecognizer` 平移（`Src/Adapters/VeloxDev.MAUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:323-325`）。⇒ 这三家是靠**用户给连线视图起的类名**来识别连线的，换名字就静默失效（见坑 1）。

3. **网格装饰器不在适配器里**：WPF 只按名字找它、cast 成 `IWorkflowGridDecorator` 并回填 `ScrollOffset*`/`ContentOffset*`（`WorkflowSurfaceBehavior.cs:597-615`），实现体由模板与 demo 提供（`Src/Templates/VeloxDev.WPF.Templates/working/content/workflow-grid-decorator/TemplateClass.cs`）。Jalium（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowGridDecorator.cs`）与 Razor（`Src/Adapters/VeloxDev.Razor/Attached/Workflow/WorkflowGridDecorator.razor`）把自家装饰器放进了适配器 —— **那是它们的背离，不是 WPF 少写了一个文件**。

4. **小地图的导航语义是「点哪哪成中心」**，不是「抓住视口块拖」。代码注释明写这是对齐 Jalium（`WorkflowMinimapOverlay.cs:373-374` 的 `// Match the Jalium adapter: the clicked point always becomes the viewport center`），Avalonia（`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/WorkflowMinimapOverlay.cs:453-454`）、WinUI（`Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowMinimapOverlay.cs:432-433`）、MAUI（`Src/Adapters/VeloxDev.MAUI/Attached/Workflow/WorkflowMinimapOverlay.cs:536`）与 Razor 的 JS（`Src/Adapters/VeloxDev.Razor/wwwroot/veloxdev.workflow.js:1193`）都是同一句。⇒ 这是**五家 + Razor JS 的一致行为**，不是 WPF 单独的选择；WPF 只是把「按下即中心」写成 `OnMouseLeftButtonDown` 里立刻 `NavigateToWorld` 再置 `_isDragging`（`:368-379`），所以按下的一瞬视口就跳了。**唯一与之冲突的是一句注释**，见坑 3。

5. **`Move` / `Replace` 两种集合变更不处理**：`ViewManager` 的 `switch` 只列 `Add`/`Remove`/`Reset`（`ViewManager.cs:59`/`:71`/`:82`）。WPF、Avalonia、WinUI、MAUI 四家都这样；**Jalium（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/ViewManager.cs:110`）与 WinForms（`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/ViewManager.cs:126`）处理 `Replace`**。⇒ 若你在 WPF 上依赖「原地替换 `VisibleItems` 里的元素」来刷新视图，**不会刷新，也不会报错**；要走 `Reset` 或先删后加。

6. **`WorkflowCanvasTransformBehavior.Transform` 的变更回调是故意空的**（`WorkflowCanvasTransformBehavior.cs:25-30`，注释：「这个属性只是通知载体；节点与连线视图各自在 XAML 里把自己的 `RenderTransform` 绑到它；宿主自身绝不能收到渲染变换」）。⇒ 别在这里补逻辑；它是被 `Apply`（`:22-23`）写、被 XAML 绑定读的单向值通道。

---

## 四、坑（带依据）

1. **`IsWorkflowLinkVisual` 靠类名白名单，改名就静默失效 —— 而且两个 demo 只有一个吃得到这条。** 那两个名字在 `Examples/Workflow/WPF/Demo/Views/Workflow/` 下确实存在（`BezierCurveView.xaml(.cs)`、`PolylineCurveView.xaml(.cs)`），所以**完整版 demo 里这条路是活的**；但 `Examples/Workflow/WPF Trimmed/Demo` 的连线视图叫 `LinkView` 且构造里 `IsHitTestVisible = false`（`Examples/Workflow/WPF Trimmed/Demo/Views/Workflow/LinkView.xaml.cs:20`）⇒ **裁剪版 demo 里这条分支永远不中**。看到「某处代码在 demo 里没被走到」时，先确认你说的是哪个 demo。

2. **`WorkflowMinimapOverlay.RulerThickness` 这个 DP 在本文件里从未被读**：DP 注册在 `:63-65`，CLR 访问器在 `:139`，而 `RulerBand` 硬编码返回 0（`:132`）。真正把标尺厚度接上的是**网格装饰器**（`Src/Templates/VeloxDev.WPF.Templates/working/content/workflow-grid-decorator/TemplateClass.cs:134` 的 `RulerBand => RulerThickness`）。⇒ 想让小地图也避让标尺，得先在这里把 `:132` 换成读 DPs，而不是设一个没人读的 `RulerThickness`。

3. **【注释与代码不符，跨文件】** `Src/Adapters/VeloxDev.Razor/Attached/Workflow/WorkflowMinimapOverlay.razor.cs:23-24` 的 XML 注释写着「Navigation is grab-the-block (**matching the other adapters**)」，但 Razor 自己的 JS 注释写着 `// MINIMAP — always-center navigation (matching the Jalium adapter)`（`Src/Adapters/VeloxDev.Razor/wwwroot/veloxdev.workflow.js:1193`），且 WPF/Avalonia/WinUI/MAUI 四家的 C# 注释全是 always-center。⇒ **错的是 Razor 的 C# 注释**，代码是 always-center。WPF 这边（`:373-374`）的注释与代码一致，可以信。这正是「源码注释与代码矛盾时要显式标出」的样本：注释写的是「和别人一样」，而它说反了。

4. **Ctrl 判定用精确相等，不是 `HasFlag`**：`Keyboard.Modifiers != ModifierKeys.Control`（`WorkflowSurfaceBehavior.cs:300`，WinForms 同形：`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowSurfaceBehavior.cs:42` 的 `Control.ModifierKeys != Keys.Control`）；另外四家用 `HasFlag`：`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/WorkflowSurfaceBehavior.cs:274`、`Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:313`、`Src/Adapters/VeloxDev.MAUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:467`、`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowSurfaceBehavior.cs:322`。⇒ **Ctrl+Shift+滚轮在 WPF 与 WinForms 上不缩放，在另外四家上缩放**。代码与注释里**没有**记录这是刻意还是遗漏 —— 别猜，按现状保留或统一都可以，但别以为七家一致。

5. **每个滚动 tick 都会全量重解析部件、并新建一个 `TranslateTransform`**：`ScrollViewer.ScrollChanged` → `Refresh(host)`（`:487-499`）→ `ResolveNamedControls`（`:182-231`，先全部退订再逐个 `FindName` 重找重订）+ `ApplyLayout`（`:560-575`，`:567` 处 `new TranslateTransform(...)`，`WorkflowCanvasTransformBehavior.Apply` 写进去）。⇒ 滚动期间每个节点/连线模板绑定的那个变换实例都在被替换。**知道就好**：要在这里加缓存或改绑定时，先确认这个刷新不是必需的。

6. **节点拖拽的落点判定依赖 `DataContext`，坐标宿主按名字或按类型找**：节点从 `control.DataContext` 起、取不到再沿祖先找 `IWorkflowNodeViewModel`（`WorkflowNodeDragBehavior.cs:176-186`）；坐标宿主先按名字，再按 `CoordinateHostType`（默认 `typeof(Canvas)`，`:150-162`）。⇒ **节点视图的 DataContext 必须是节点 VM**（或祖先上有），而坐标宿主默认是 `Canvas` —— 换成 `Grid` 必须显式给 `CoordinateHostType`。

7. **插槽锚点用 `TranslatePoint` 换算到坐标宿主，再交给 Core 的换算函数**：`control.TranslatePoint(节点中心, coordinateHost)` 后调 `WorkflowSurfaceMath.SlotAnchorFromVisualCenter`（`WorkflowSlotLayoutBehavior.cs:350-363`，注释 `:350-352` 解释了为什么是「世界 + ActualOffset」）。坐标宿主取不到时退回 `SlotAnchorFromNode`（`:362-363`）。⇒ 两种坐标系数值上不同源，**混用会得到系统性偏移**，选哪个取决于你测到的是哪个坐标系。

8. **`ViewManager` 的池按运行时类型分桶，隐藏的视图留在 `panel.Children` 里不清树**：池是 `Dictionary<Type, Queue<FrameworkElement>>`（`ViewManager.cs:16`），复用前 `Visibility.Collapsed` + `DataContext = null`（`:184-185`、`:205-206`）。`DataTemplate` 查找是三级回退 + 缓存（`:165` 调 `FindDataTemplate`，`:228` 起：`TemplateSelector` → 视觉树 `Resources` → `Application.Resources`）。⇒ 「视图消失了但仍在树上」是设计；**不要用 `panel.Children.Count` 表示可见视图数**，用 `VisibleItems`。

9. **`ViewPool` 的管理器挂在 `ConditionalWeakTable<Panel, ViewManager>` 上，并在 `Panel.Unloaded` 时清理**（`ViewPool.cs:38`、`:53-61`）。⇒ 面板一旦被卸载，池与所有复用视图一起作废 —— 把工作流面板放进一个会被反复卸载/重建的容器（切换 Tab、`ContentControl` 换模板）时，每次都要重新付一遍建视图的成本。

10. **平移越边会主动扩张画布，因此平移中必须重读两次 max 并把 `PanStartOffset` 重设**（`WorkflowSurfaceBehavior.cs:531-547`）：`layoutChanged` 时先 `ApplyLayout` + 三连 `UpdateLayout` 再重读 max，并用**实际落地的偏移**（而非期望偏移）重置 `PanStart`/`PanStartOffset`。少这一步的表现是拖到边缘后指针「甩开」画布。这条的数学在 Core（`WorkflowSurfaceMath.ClampScrollOffset` 的 `extendRatio`，`:527-530` 传的是 `DefaultPanExtendRatio`）。

11. **`UpdateGridDecorator` 必须把 `RulerBand` 转发给虚拟化内缩**（`:597-615`，`:613` 的 `viewModel.SetVirtualizeInset(left: decorator.RulerBand, top: decorator.RulerBand)`），否则浮在顶/左边的标尺下面那些节点会被判为不可见。这是 extension.md §3.8 那条「`RulerBand` 要转发」在这家的具体落点，**照抄时最容易只抄 `ScrollOffset*` 三个赋值而漏掉这一行**。

12. **`UpdateVisibleRegion` 同时写 `Viewport` 和 `Layout.ViewportOffset`**（`:577-595`，后者在 `:594`）—— 后者是为了序列化往返保留视口位置。⇒ 只写 `Viewport` 的话视图是对的，但保存再加载会丢掉「上次看到哪」。

13. **悬停连线会让画布自己滚一段 —— 是「取焦点」带来的 WPF 默认行为，不是本仓库的代码。** 连线视图在指针进入、或指针落到线身上时 `Focus()`（`Examples/Workflow/WPF/Demo/Views/Workflow/PolylineCurveView.xaml.cs:102`、`:479`；焦点是 `OnKeyDown` 的 Delete 需要的，`:483`）；WPF 的 `FrameworkElement` 在获得焦点时替它请求 `RequestBringIntoView`，`ScrollContentPresenter` 的类处理照办 ⇒ `ScrollViewer` 偏移跳变，**与按键无关**（实测 `left=Released`）。跳多远由当时的偏移与 extent 决定，不是固定值：连线视图的尺寸绑的是祖先 `Canvas`（`Examples/Workflow/WPF/Demo/Views/Workflow/WorkflowView.xaml:68-69`）⇒ 它的包围盒就是整块画布。

    实测（完整版 demo，extent 2400×850、viewport 969×723）：纯悬停扫过线身 4 次，偏移跳 **28 / 412.8 / 362 / 502.1 px**；每跳一次 extent 还被撑大（**850 → 1293 → 1487**，平移越边扩张的连带效应），所以画布会越跳越大。修法是**在发源地吃掉这条请求**（`:108` 的 `AddHandler(RequestBringIntoViewEvent, … e.Handled = true)`）—— 保留焦点、只拦滚动；**不要改成 `Focusable = false`**，那会连带废掉 Delete 键。修后同一把尺子复测：纯悬停滚动 **4 → 0**，由连线焦点引起的二次抛出 **4 → 0**（另有 1 次 `REQ target=ScrollViewer` 是同一轮里人按鼠标那下带来的，`left=Pressed`，与连线无关）。

    **量这条时的两个坑**：(a) 先用 `ScrollTo*` 把偏移预设到别处再悬停 ⇒ 症状被掩盖（它依赖当时的偏移），要照真实用法从启动状态扫；(b) 合成光标（`SetCursorPos`）**必须**先 `SetWindowPos(HWND_TOPMOST)` + `SetForegroundWindow` 把窗口推到最前，否则一次都命不中、日志里连 `over ->` 都没有 —— 我第一次就是这样量到「0 次」的。

    **别家（未核）**：WinUI 的 `ScrollViewer.BringIntoViewOnFocusChange` 默认同为 `true`，而这家的连线视图也在悬停时 `Focus(FocusState.Pointer)`（`Examples/Workflow/WinUI/Demo/Views/Workflow/PolylineCurveView.xaml.cs:169`）⇒ 同一症状在 WinUI 上可能存在，实测前别断言没有。Avalonia 无此默认行为，同形的 `Focus()`（`Examples/Workflow/Avalonia/Demo/Views/Workflow/PolylineCurveView.axaml.cs:471`）未见此症状。

---

## 五、这份文件没写的东西

- 七个角色各自要暴露什么成员、附着属性叫什么名字、`PART_*` 命名约定 —— 在 `memory/modules/WorkflowSystem/extension.md` §3.9 与 `skills/veloxdev-create-workflow/references/view-layer.md`。
- 怎么在 WPF 上从零搭一个工作流视图（XAML 片段、绑定写法、demo 位置）—— 在 `skills/veloxdev-create-workflow/references/gui/wpf.md`（其中「本适配器**没有**连线交互 helper」那条也以那份为准）。
- 坐标换算与缩放的数学 —— 在 `WorkflowSurfaceMath`（`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Math/WorkflowSurfaceMath.cs`），七家共用，不在本文。
- 节点拖拽需要真 `Background` 这类 WPF 命中测试通则 —— 见 `memory/workflow-node-drag-hit-test.md`。
