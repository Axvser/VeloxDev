# WorkflowSystem — Avalonia

> 代码：`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/`（9 个类）。
> 契约、注册位置、七角色职责表在 `memory/modules/WorkflowSystem/extension.md` §3.9 与
> `skills/veloxdev-create-workflow/references/view-layer.md`；「怎么搭一个工作流界面」在
> `skills/veloxdev-create-workflow/references/gui/avalonia.md`。
> 本文只写：这家必须实现哪些成员、这家的平台硬限制、与其它六家的刻意背离、改这里最容易踩的坑。
>
> **平台事实用 Avalonia 11.1.0（仓库锁定版本，`veloxdev/…csproj` 的 `AvaloniaVersion = 11.1.0`）
> 的运行时反射 / IL 实测过**，不是从 WPF 类推的。类推会推错的地方都标了「实测」。

---

## 一、七个视图角色在这家的形状（只写「为什么是这个形状」）

| 角色 | 这家的类 | 形状要点（为什么） |
|---|---|---|
| 画布宿主 | `WorkflowSurfaceBehavior`（`sealed class : AvaloniaObject`，609 行） | 附着 8 个属性（7 公开 + 1 私有 `State`），命名控件靠 `control.FindControl<T>(name)` 解析 |
| 画布变换 | `WorkflowCanvasTransformBehavior`（**`sealed class : AvaloniaObject`**） | WPF/WinUI/Jalium 的同名类是 `static class`，这家不行 —— 理由在 §二.1 |
| 视图池 | `ViewPool`（`sealed class : AvaloniaObject`）+ `ViewManager`（普通 `sealed class`） | 池化器不是行为类、没有附着属性，所以不继承 `AvaloniaObject` |
| 节点拖拽 | `WorkflowNodeDragBehavior`（`sealed class : AvaloniaObject`） | 只认左键；位移在**坐标宿主空间**里算，不是 Canvas 空间 |
| 插槽连接 | `WorkflowSlotConnectionBehavior`（`sealed class : AvaloniaObject`，50 行） | 按下发送、同元素上松开接收；按下后**主动释放指针捕获**，见 §三.3 |
| 插槽布局 | `WorkflowSlotLayoutBehavior`（`sealed class : AvaloniaObject`，346 行） | **三条写回路径**，见 §三.5 |
| 网格装饰器 / 小地图 | `WorkflowMinimapOverlay`（`class Control, IWorkflowMinimapOverlay`，593 行）；**网格装饰器适配器不自带** | 自绘走 `public override void Render(DrawingContext)`；`RulerBand => 0`（`:135`，小地图不占标尺带）；装饰器由 demo 提供（`Examples/Workflow/Avalonia Trimmed/Demo/Demo/Views/Workflow/WorkflowGridDecorator.cs`，`sealed class : Panel, IWorkflowGridDecorator`，`RulerBand => RulerThickness`），与 WPF 同形 |
| 平台探测 | `PlatformDetection`（`internal static class`，20 行） | **零调用者，死代码**，见 §四.1 |

---

## 二、平台硬限制（本篇最有价值的部分）

### 1. 附着属性只能走泛型 `AvaloniaProperty.RegisterAttached<TOwner, THost, TValue>` —— 于是行为类**不能**是 `static class`

这家的注册签名把所有者类型放在**类型参数**里（`WorkflowSurfaceBehavior.cs:33-54`、`ViewPool.cs:22-27` 全是这个形状）。而 C# 不允许静态类作类型参数（CS0718），所以这家的行为类必须是可实例化的类；仓库里的做法统一是 **`public sealed class X : AvaloniaObject`**。6 个行为类无一例外：

- `WorkflowSurfaceBehavior`（`WorkflowSurfaceBehavior.cs:17`）
- `WorkflowCanvasTransformBehavior`（`:11`）
- `ViewPool`（`ViewPool.cs:13`）
- `WorkflowNodeDragBehavior`（`:11`）
- `WorkflowSlotConnectionBehavior`（`:8`）
- `WorkflowSlotLayoutBehavior`（`:13`）

**这是接新平台时第一处不能照抄 WPF 的地方**：`WorkflowCanvasTransformBehavior` 的**方法体逐字相同**（两个文件都是「属性只是通知载体」+ 空回调），唯一的差别就是这一层外壳 —— WPF 用 `DependencyProperty.RegisterAttached("Transform", typeof(Transform), typeof(X), metadata)`（WPF `WorkflowCanvasTransformBehavior.cs:10-16`），Avalonia 用 `AvaloniaProperty.RegisterAttached<WorkflowCanvasTransformBehavior, Control, ITransform?>("Transform")`（Avalonia `:11-14`）。
- 补充：Avalonia 也有收 `System.Type` 所有者的两参重载（`RegisterAttached<THost, TValue>(string, Type ownerType, …)`），所以静态类**理论上有路可走**；仓库一律走泛型那条，于是「类必须继承 `AvaloniaObject`」成了家规。

### 2. 属性变化回调挂在**静态构造**里，签名是 `(THost, AvaloniaPropertyChangedEventArgs)`

```csharp
static WorkflowSlotConnectionBehavior() { IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged); }
```
（`WorkflowSlotConnectionBehavior.cs:13-16`、`ViewPool.cs:15-19`、`ViewPool.cs:17-18`。）WPF 是在 `PropertyMetadata` 里传回调、签名 `(DependencyObject, DependencyPropertyChangedEventArgs)`。**回调的第一个参数是宿主控件而不是属性所有者**，所以每个回调里都要自己再取一次 `State` 附着属性（例如 `WorkflowSurfaceBehavior.cs:245-249` 的 `OnZoomEnabledChanged`）—— 这是「属性所有者是静态类、状态必须挂在被附着的控件上」这条约束的直接后果。

### 3. `PreviewMouseWheel` **不存在** —— 滚轮缩放只能靠隧道路由

`WorkflowSurfaceBehavior.cs:241-249` 的注释明写：「Avalonia has no PreviewMouseWheel, so the wheel is tunneled on the ScrollViewer: it fires before the control scrolls and marks the event handled, keeping plain wheel scrolling intact.」实现是
`state.ScrollViewer.AddHandler(InputElement.PointerWheelChangedEvent, handler, RoutingStrategies.Tunnel)`（`:248`）。
WPF 的对应写法是 `state.ScrollViewer.PreviewMouseWheel += …`（WPF `WorkflowSurfaceBehavior.cs:218` 附近）。

**后果**：这家的滚轮处理器挂在 **ScrollViewer** 上（而不是宿主），且必须是 `Tunnel` —— 先于 ScrollViewer 自己滚动触发、并置 `e.Handled = true`（`:339`），普通滚轮滚动才不受影响。照抄 WPF 的 `PreviewMouseWheel` 在这家编译不过；照抄成 `PointerWheelChanged` 的**冒泡**注册则会被 ScrollViewer 先消费掉。

### 4. `GestureRecognizerCollection` 只公开 `Add` —— 删内置手势识别器必须反射

`WorkflowSurfaceBehavior.cs:189-197`（注释）+ `:406-429`（实现）：ScrollContentPresenter 上的 `ScrollGestureRecognizer` 会在拖动中途抢走指针捕获（触摸平台必现），而这家的 `GestureRecognizers` 是 `IReadOnlyCollection`，**没有 Remove**。于是代码从 `ScrollContentPresenter` 上拿到识别器集合，反射取私有字段 `_recognizers`（`List<GestureRecognizer>`）、`RemoveAll(ScrollGestureRecognizer)`，然后**立刻退订** `LayoutUpdated`（`:427`），避免每帧重扫。

- 这是这家最脆的一处：字段名 `_recognizers` 是私有的，Avalonia 改字段名就静默失效（不会抛，只会回到「拖动被抢捕获」的老症状）。
- 这也是 `PlatformDetection`（§四.1）想解决的问题 —— 但那条路（Tunnel 注册）**没有实现**。

### 5. 自绘入口是 `Render(DrawingContext)` + `AffectsRender<T>(…)`；Avalonia 没有 `OnRender`

实测：`Avalonia.Visual` 上**不存在** `OnRender`，`Render` 是 `public virtual`。所以小地图是
`public override void Render(DrawingContext context)`（`WorkflowMinimapOverlay.cs:529`），用 `context.PushClip` / `FillRectangle(brush, rect, cornerRadius)` / `DrawRectangle(null, new Pen(…), rect, cr)` 画（`:549-551` 起）；重绘靠静态构造里登记 `AffectsRender<WorkflowMinimapOverlay>(…)` 列出全部相关属性（`:169-183`，约 25 个），属性一变自动 `InvalidateVisual`。WPF 那份是 `FrameworkElement` + `OnRender` + `DrawingContext`。

- **改这家的自绘时不要去找 `OnRender`**，也不要在属性 setter 里手写 `InvalidateVisual`：属性已在 `AffectsRender` 名单里就够；名单外的属性（例如只在 `Render` 内部读的派生值）才需要手动 `InvalidateVisual`。
- 小地图的 `InstanceMarkDirty`（`:376-387`）是例外：它可能在**非 UI 线程**被调用（注释：`OnNodePropChanged` 可能来自 `MonoBehaviourManager` 的循环线程），所以先 `Dispatcher.UIThread.CheckAccess()`，不行才 `Post(InvalidateVisual)`。`InvalidateVisual` 要求 UI 线程，这是平台硬约束。

### 6. 布局 pass 走**渲染循环**，不在 dispatcher 优先队列上 —— 两个后果

IL 实测（11.1.0）：`LayoutManager.QueueLayoutPass()` → `MediaContext.BeginInvokeOnRender(action)` → `MediaContext.ScheduleRender(…)` → `Dispatcher.InvokeAsync(…, DispatcherPriority.Render)`（`ScheduleRender` 的 IL 里读的就是 `DispatcherPriority.Render` 与 `Input`）。而 `Layoutable.UpdateLayout()` 的 IL 只有一句 `ILayoutManager.ExecuteLayoutPass()`，**`ILayoutManager` 上没有任何「只重排这个控件」的重载**。

后果一 —— **一次 `UpdateLayout()` 就够，不必像 WPF 那样连调三处**。WPF 的缩放提交里写的是
`state.Canvas?.UpdateLayout(); sv.UpdateLayout(); host.UpdateLayout();`（WPF `:322-324`、`:342-346`，各两处），Avalonia 只写 `ApplyLayout(host, state); sv.UpdateLayout();`（`:297-298`、`:317-318`）。因为 Avalonia 这一次调用执行的是整棵树的布局 pass，等价于 WPF 那三句。

后果二 —— **`LayoutUpdated` 里的同步写回与 `Dispatcher.Post(…, Render)` 的延迟写回处在同一优先级带**。这正是 `WorkflowSlotLayoutBehavior` 需要「两条路并存」的原因（§三.5）：延后到 `Render` 的那条不保证赶在本帧渲染之前。

### 7. `Layoutable.LayoutUpdated` 是**整棵树、arrange 之后**的事件（与 WPF 同族，**不是** WinUI 那种逐元素语义）

IL 实测：`Layoutable.OnAttachedToVisualTreeCore` 在挂树时订阅的是 `ILayoutManager.LayoutUpdated`（整棵树那条），再由每个 `Layoutable` 的私有处理器转发成自己的 `LayoutUpdated`；`LayoutManager.ExecuteLayoutPass` 的 IL 里，`InnerLayoutPass`（measure + arrange）跑完之后才 `EventHandler.Invoke` 那条事件。

**这条要紧，因为代码注释把它归错了源头**：`WorkflowSlotLayoutBehavior.cs:128-131` 写着「Synchronous post-layout write-back (**mirrors the WinUI adapter**). …guarantees the link-endpoint anchors are refreshed in the same frame…」。而 WinUI 那边的注释（`Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowSlotLayoutBehavior.cs:22-27`）说的是**相反**的事：WinUI 的 `LayoutUpdated` 是**逐元素**的，可能在节点还没移到新位置时就读到旧的 Canvas 坐标，**WPF 免疫恰恰因为 WPF 的 `LayoutUpdated` 是整棵树一次**。
Avalonia 与 WPF 同族 → **同步写在 Avalonia 上成立的理由是 WPF 那条**，与「照 WinUI 抄」无关。抄形状可以，别抄错了理由；将来若有人按 WinUI 那段注释去「修正」Avalonia 的实现（例如改用坐标宿主的 `LayoutUpdated`），那是把 WPF 族当成 WinUI 族在改。

### 8. 命名控件解析：`FindControl<T>(name)`，以及为什么小地图要自己走视觉树

宿主侧用 `control.FindControl<T>(name)`（`WorkflowSurfaceBehavior.cs:168-184`）—— 它是沿宿主名字域往下找，够用。
小地图**不在**宿主的名字域里，且要能反查自己的 ScrollViewer，所以走「先从 `GetVisualParent()` 上溯到根、再从根往下按名字找」（`WorkflowMinimapOverlay.cs:198-210`）。该处的注释还带一条**版本兼容约束**：

> `VisualExtensions.GetVisualRoot()` was removed in Avalonia 12 and `e.Root`'s return type changed (`IRenderRoot` in 11, `Visual` in 12), so walk up via `GetVisualParent()` whose signature is identical in both 11.x and 12.x.

**这家锁的是 11.1.0，但这段代码是刻意同时兼容 12 的** —— 改这里时不要用任一版本独有的 API「简化」它。

### 9. 附着属性里塞对象当「每宿主状态」是这家的通用手法

`State`（`SurfaceState` / `LayoutState` / `DragState`）都是各自的**私有附着属性**（`WorkflowSurfaceBehavior.cs:54`、`WorkflowSlotLayoutBehavior.cs:39`、`WorkflowNodeDragBehavior.cs:30`），因为行为类自身无实例、状态必须寄存在被附着的控件上。WPF 同形（WPF `WorkflowSurfaceBehavior.cs:70` 的 `StateProperty`）。**新写一家时跟着抄这个手法**，不要改用 `ConditionalWeakTable`（`ViewPool.cs:73` 那种用法是给「不需要在 XAML 里读到的管理器」用的，不是给行为状态用的）。

---

## 三、与其它家的刻意背离

### 1. 平移接受**左键或中键** —— 与 WinUI 同款，与 WPF / Jalium 相反

`WorkflowSurfaceBehavior.cs:560-565`：`properties.IsLeftButtonPressed || properties.IsMiddleButtonPressed`，配合 `:567-571` 的 `IsPanStillActive`（拖动中任一键按住都算）。逐家核对的结果是**四家两派**：

| 认左键 + 中键 | 只认左键 |
|---|---|
| Avalonia（`:560-565`）、WinUI（`VeloxDev.WinUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:689-697`，逐字同形） | WPF（`if (e.ChangedButton != MouseButton.Left) return;`，`:392-395`）、Jalium（`VeloxDev.Jalium/…WorkflowSurfaceBehavior.cs:417`） |

**要注意的不是「Avalonia 特别」，而是「WPF 不是唯一参照」**：这家的形态与 WinUI 一致，代码与注释都没写理由（CAD/地图类界面把中键当平移是惯例）。副作用是：中键平移到一半、左键随便点一下，`IsPanStillActive` 仍为真，平移不会中断。**别按 WPF 那份去「修正」成只认左键** —— 先决定要哪一派。

### 2. 按下走**冒泡**、释放**不判空白也不判 `IsVisible`** —— 于是「什么算空白」只能在数据上下文层做

- 按下：`state.PointerPressSource.PointerPressed += OnPointerPressed;`（`:186-187`，默认冒泡）。WPF 是 `state.PointerPressSource.PreviewMouseDown += OnPointerPressed`（WPF `:218`，隧道路由）。Avalonia 要等价写法得用 `AddHandler(InputElement.PointerPressedEvent, handler, RoutingStrategies.Tunnel)` —— 这家**没有**这么做（全目录里唯一的 `Tunnel` 用例是 §二.3 的滚轮，实测 grep：`Tunnel` 只出现在 `WorkflowSurfaceBehavior.cs:248`）。
- 释放：`:378-396`。先处理平移收尾，然后**无条件**执行 `viewModel.VirtualLink.Sender.State &= ~SlotState.PreviewSender;` 与 `viewModel.ResetVirtualLinkCommand.Execute(null)`。WPF 的同位置有两道门：`!viewModel.VirtualLink.IsVisible` 直接返回、且必须 `IsSurfaceBlankInteraction(originalSource, state)` 为真（WPF `:424-437`）。
- 这一对的后果是**互补的**：因为按下/释放都不 `Handled`、释放也不筛来源，这家必须把「命中节点/插槽/连线」的判定放进 `IsSurfaceBlankInteraction`（`:573-598`：先排 `DataContext is IWorkflowNodeViewModel or IWorkflowSlotViewModel` 的视觉元素及其祖先，再把连线视觉当成合法空白，最后按引用匹配 `Canvas` / `ScrollViewer` / `PointerPressSource` / `GridDecorator`，并额外认类名 `"ScrollContentPresenter"`）。
- **未验证**：释放时无条件 `ResetVirtualLinkCommand` 是否会在「连线进行中于空白处松开」之外造成可观察的副作用，我没有跑起来验证，只按代码读出来。要动这块的话，先按 WPF 的两道门补，再看是否需要保留无条件路径。

### 3. 插槽连接：按下后**主动释放指针捕获**，且两个处理器都**不置 `Handled`**

`WorkflowSlotConnectionBehavior.cs:34-49`：
```csharp
slot.SendConnectionCommand.Execute(null);
e.Pointer.Capture(null);      // 按下
…
slot.ReceiveConnectionCommand.Execute(null);   // 松开
```
WPF 那份是 `PreviewMouseLeftButtonDown/Up` + 两个处理器都 `e.Handled = true`、**不碰捕获**（WPF `WorkflowSlotConnectionBehavior.cs:36-56`）。

**这里的做法和其他家不一样，因为 Avalonia 的指针捕获会把后续指针事件全部钉在按下的那个插槽上** —— 不主动放开，松开的那个事件永远到不了接收方的插槽，两阶段 `SendConnection`/`ReceiveConnection` 的第二阶段就永远不触发。这也是为什么这家不能在按下时置 `Handled`：事件要继续冒泡（配合 §三.2 的空白判定），否则「点空白取消连线」这类路径会断。

### 4. 插槽布局有**三条**写回路径，别家只有一到两条

`WorkflowSlotLayoutBehavior`：

1. **同步**：`LayoutUpdated` → 直接 `Sync(control)`（`:126-134`）。
2. **异步**：`Visual.BoundsProperty` 变化 → `ScheduleSync`（`:136-140`）。
3. **异步**：DataContext 的 `INotifyPropertyChanged` 且属性名在 `SlotPropertyNames` 里 → `ScheduleSync`（`:142-157`）。

`ScheduleSync`（`:177-191`）用 `state.SyncPending` 合并、`Dispatcher.UIThread.Post(…, DispatcherPriority.Render)` 下发。**三条并存的理由是平台决定的**：布局 pass 在渲染循环上、与 `Render` 优先级同一带（§二.6），所以「延后到 Render」这条路没法保证赶在本帧渲染前，必须再留一条同步的路；而 `Bounds` 与 ViewModel 属性变化又不是每次都有布局 pass，所以两条异步路也不能省。

- 上一家（WPF）是 `LayoutUpdated` + `SizeChanged` + `PropertyChanged`（WPF `:104-105`、`:174`）；这家把 `SizeChanged` 换成了 `BoundsProperty` 的类处理器，**因为 Avalonia 的 `Visual.Bounds` 是 `Rect` 且以 `AvaloniaProperty` 形式存在**（实测：`Visual.Bounds` 是 `Avalonia.Rect`），所以能直接挂在属性变化上，不必等 `SizeChanged` 事件。
- 这家**没有** WinUI 那种「额外订阅坐标宿主的 `LayoutUpdated`」的做法（WinUI `:162`）—— 与 §二.7 同因：这家的 `LayoutUpdated` 本来就是整棵树一次。

### 5. 插槽名匹配是**宽松匹配**：控制名 + 去 `PART_` 前缀形式 + 三个字面量兜底

`:212-235`：`SlotPropertyNames` 除 `Anchor` / `Size` 外，收下 `SlotNames` / `SlotEnumeratorNames` 里每个控制名、每个以 `PART_` 开头的名字**去掉前缀后的形式**，另外**无条件**加三个字面量 `"InputSlot"` / `"OutputSlot"` / `"OutputSlots"`。注释写的理由是「Control names (e.g. `"PART_OutputSlots"`) differ from ViewModel property names (`"OutputSlots"`)」。

**这里的做法和其他家不一样，因为这家把「XAML 里的控件名」与「ViewModel 的属性名」这两个命名空间在适配器里对齐**，代价是那三个字面量兜底会对所有节点生效（包括根本没用这些名字的节点）。改这里的注意：兜底名字加进去就等于给全仓库的节点都开了一个属性名触发器，不要为了某一个 demo 再加一个。

### 6. 模板查找的第三级是 `Application.Current.DataTemplates`

`ViewManager.FindDataTemplate`（`:230-268`）顺序：`_templateMap`（按 ViewModel 类型缓存）→ `TemplateSelector.Match` → 面板自己的 `DataTemplates` → **沿 `Parent` 链往上逐个控件的 `DataTemplates`** → `Application.Current.DataTemplates`。
WPF 那份的第三级是**扫 `Application.Current.Resources`** 找 `DataType` 匹配的 `DataTemplate`（WPF `ViewManager.cs:266-268`）。

**这里的做法和其他家不一样，因为 Avalonia 有 `Application.DataTemplates` 这个有序集合，而 WPF 没有**（WPF 的隐式模板走资源查找）。所以这家的「Self > Parent > App」三级是平台提供的结构，不是自创层级。

### 7. 视图池 batching 与 WPF **相同**（记下来免得重复查）

`batchSize = 3` + `DispatcherPriority.Background`（`ViewManager.cs:119`、`:125`）与 WPF 逐字一致（WPF `:119`、`:125`）；WinUI 是 3 + `DispatcherQueuePriority.Low`，MAUI 是 8 + 自己的 `IDispatcherTimer`。
另：`ViewPool` 的清理只在 `DetachedFromVisualTree`（`ViewPool.cs:53`、`:75-82`），WPF 用 `Unloaded` —— **同形，不是本家差异**；管理器放 `ConditionalWeakTable`（`:73`）两家也一样。

---

## 四、改这里最容易踩的坑

1. **`PlatformDetection.cs` 是死代码，而且它的注释描述的是一条不存在的路径。** 全类 20 行，`IsTouchPlatform` 只有定义没有调用者（`Src/` 与 `Examples/` 全仓库 grep 零命中）。它的 XML 注释写着「On these platforms, PointerPressed handlers **must be registered with Tunnel routing** to pre-empt the ScrollViewer gesture recognizer」—— 实际做法是 §二.4 的**反射删除识别器**，全目录唯一的 `Tunnel` 注册是滚轮（`WorkflowSurfaceBehavior.cs:248`）。**不要照这条注释去改实现**；要么删掉这个类，要么把注释改成描述真实机制。
2. **`IsSurfaceBlankInteraction` 会把 ScrollViewer 内部的一切都算成空白**（`:586-598` 的祖先判定里有 `ReferenceEquals(x, state.ScrollViewer)`）。WPF 那份在此之后**显式排除**滚动条（WPF `:652-657`：`source is ScrollBar || ancestors.Any(x => x is ScrollBar)`），Avalonia 没有这段。
   - **未验证**：Avalonia 里滚动条（Thumb）按下时是否会把 `PointerPressed` 标成已处理、从而根本到不了 `OnPointerPressed`（这家是 `+=` 注册、默认不接收已处理事件）。若会，这条就不会触发；若不会，点击滚动条会**同时**启动一次画布平移。**下一个动这块的人应该先用一次实测把它定下来**，别按 WPF 的结论直接补排除。
3. **拖拽手柄必须有不透明背景，否则收不到指针事件。** `Examples/Workflow/Avalonia Trimmed/Demo/Demo/Views/Workflow/NodeView.axaml` 里拖拽用的 Grid 带 `Background="Transparent"`；插槽视图 `SlotView.axaml` 用 `Background="#01000000"`（近透明，仍参与命中测试）。空背景的 `Grid`/`Panel` 在 Avalonia 里不做命中测试 —— 这与 `memory/modules/WorkflowSystem/…` 里「XAML node drag header needs Background="Transparent"」是同一件事，属平台硬限制，不是 demo 的随手写法。
4. **`IsScrollInertiaEnabled="False"`**（demo 的 `PART_ScrollViewer`，`TreeView.axaml:41`）：这家自建了完整的平移逻辑（`WorkflowSurfaceBehavior` 的 `IsPanning`/`PanStartOffset`），滚动惯性会与它抢同一组指针事件。抄 demo 时别把这一行删了。
5. **`_templateMap` 按 ViewModel 类型永久缓存模板**（`ViewManager.cs:230-233`），后来才加进 `Application.DataTemplates` 的模板不会被重新解析；同类型换模板（例如主题切换换掉 DataTemplate）也不会生效。这不是 bug 而是缓存策略，但改模板相关行为时要知道它在那儿。
6. **`WorkflowSurfaceBehavior` 的刷新是 `ScrollChanged` 驱动的**：`OnScrollChanged`（`:431-439`）→ `Refresh(host)`（`:90`）→ `UpdateVisibleRegion`（`:507-523`），而 `UpdateVisibleRegion` 每次都会写 `viewModel.Layout.ViewportOffset`（`:522`）。**`Viewport` 是画布局部坐标、只有适配器写它**这条契约（`extension.md` §3.9-3）在这家由这一处落地；不要在别处再写一次 `Viewport`。
7. **`ApplyLayout` 每次都新建 `TransformGroup` + `TranslateTransform`**（`:491-500`），并先设 `Canvas.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative)`（`:491`）。缩放/平移期间这是每帧一次的分配 —— 与 WPF 同形，属于已知代价；若要优化，注意 `RenderTransformOrigin` 必须保持 `(0,0)`，否则 `ActualOffset` 的语义就变了。
8. **`TranslatePoint` 在这家返回 `Point?`**（未挂到同一视觉树根时为 null）。所有测量点都要处理 null：`WorkflowSlotLayoutBehavior.cs:281-287`（有坐标宿主时）与 `:290-297`（回退到 `SlotAnchorFromNode`）。**别把这两条回退路径合成一条**：前者用 `SlotAnchorFromVisualCenter` + 宿主 `CanvasLayout`，后者用 `SlotAnchorFromNode`，坐标系不同（`extension.md` §3.9-5 要求按测量到的坐标系三选一）。
9. **`SyncSlot` 在 `Bounds` 未测量时直接返回**（`:276`：`control.Bounds.Width <= 0 || control.Bounds.Height <= 0`）。这是这家版的「NaN 锚点 = 未测量」门（Core 那边是 `WorkflowSlotUpdateGate`）；**不要**在这里改成「用 0 兜底」，那会让连线先在节点原点画一帧再跳走。

10. **池建视图时传的是 VM，不是 `null` —— 传 `null` 会让自定义 `IDataTemplate` 选择器整个失效。** 那一行现在是 `template?.Build(viewModel)`（`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/ViewManager.cs:168`，2026-09-25 从 `Build(null)` 改来）。原因：Avalonia 的 `IDataTemplate` 是「既选又建」—— `Match` 挑出的若是个**选择器**，轮到 `Build` 时才是它挑内层模板的时候；传 `null` 它无从下手（`workflow-template-selector` 的 `SelectTemplate(null)` 抛 `InvalidOperationException`）⇒ **视图一个都不建、画布空着、不报错**。实测（Avalonia Trimmed demo，装记录仪）：`Build(null)` 时 5 次 Build 全失败（1 条连线 + 4 个节点，正好对应基线的 4 张卡），截图是空画布；改成传 VM 后日志为 `Build NodeViewModel -> NodeView`×4 与 `Build LinkDefaultViewModel -> LinkView`，卡片回来。**别把它「简化」回 `Build(null)`** —— 其余三家（WPF/WinUI/MAUI）没有这个问题，因为它们的 `DataTemplateSelector` 只负责「选」，建由适配器 `LoadContent()`/`CreateContent()` 做。
