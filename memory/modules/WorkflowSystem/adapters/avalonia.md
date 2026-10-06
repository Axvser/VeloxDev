# WorkflowSystem — Avalonia

> 连线交互规则见 [WorkflowSystem/architecture.md §3.6](../architecture.md)：输入是标准输入
> （`WorkflowInput.For(tree).Route(...)` + `IInputEvents`），命中归 Core 的共享曲线判定，外观、删除与菜单的接线归宿主/适配器。
>
> 代码：`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/`（10 个类）。
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
| 画布宿主 | `WorkflowSurfaceBehavior`（`sealed class : AvaloniaObject`，1038 行） | 附着 9 个属性（8 公开 + 1 私有 `State`），命名控件靠 `control.FindControl<T>(name)` 解析 |
| 画布变换 | `WorkflowCanvasTransformBehavior`（**`sealed class : AvaloniaObject`**） | WPF/WinUI 的同名类是 `static class`，这家不行 —— 理由在 §二.1 |
| 视图池 | `ViewPool`（`sealed class : AvaloniaObject`）+ `ViewManager`（普通 `sealed class`） | `ViewPool` 要注册附着属性所以继承 `AvaloniaObject`；`ViewManager` 不是行为类、没有附着属性，所以不继承 |
| 节点拖拽 | `WorkflowNodeDragBehavior`（`sealed class : AvaloniaObject`） | 只认左键；位移在**坐标宿主空间**里算，不是 Canvas 空间 |
| 插槽连接 | `WorkflowSlotConnectionBehavior`（`sealed class : AvaloniaObject`，52 行） | 按下发送、同元素上松开接收；按下后**主动释放指针捕获**，见 §三.3 |
| 插槽布局 | `WorkflowSlotLayoutBehavior`（`sealed class : AvaloniaObject`，342 行） | **三条写回路径**，见 §三.5 |
| 网格装饰器 / 小地图 | `WorkflowMinimapOverlay`（`class Control, IWorkflowMinimapOverlay`，589 行）；**网格装饰器适配器不自带** | 自绘走 `public override void Render(DrawingContext)`；`RulerBand => 0`（`:137`，小地图不占标尺带）；装饰器由 demo 提供（`Examples/Workflow/Avalonia Trimmed/Demo/Demo/Views/Workflow/WorkflowGridDecorator.cs`，`sealed class : Panel, IWorkflowGridDecorator`，`RulerBand => RulerThickness`），与 WPF 同形 |
| 平台探测 | `PlatformDetection`（`internal static class`，20 行） | **零调用者，死代码**，见 §四.1 |

---

## 二、平台硬限制（本篇最有价值的部分）

### 1. 附着属性只能走泛型 `AvaloniaProperty.RegisterAttached<TOwner, THost, TValue>` —— 于是行为类**不能**是 `static class`

这家的注册签名把所有者类型放在**类型参数**里（`WorkflowSurfaceBehavior.cs:60-82`、`ViewPool.cs:22-27` 全是这个形状）。而 C# 不允许静态类作类型参数（CS0718），所以这家的行为类必须是可实例化的类；仓库里的做法统一是 **`public sealed class X : AvaloniaObject`**。6 个行为类无一例外：

- `WorkflowSurfaceBehavior`（`WorkflowSurfaceBehavior.cs:21`）
- `WorkflowCanvasTransformBehavior`（`:11`）
- `ViewPool`（`ViewPool.cs:13`）
- `WorkflowNodeDragBehavior`（`:13`）
- `WorkflowSlotConnectionBehavior`（`:10`）
- `WorkflowSlotLayoutBehavior`（`:13`）

**这是接新平台时第一处不能照抄 WPF 的地方**：`WorkflowCanvasTransformBehavior` 的**方法体逐字相同**（两个文件都是「属性只是通知载体」+ 空回调），唯一的差别就是这一层外壳 —— WPF 用 `DependencyProperty.RegisterAttached("Transform", typeof(Transform), typeof(X), metadata)`（WPF `WorkflowCanvasTransformBehavior.cs:12-16`），Avalonia 用 `AvaloniaProperty.RegisterAttached<WorkflowCanvasTransformBehavior, Control, ITransform?>("Transform")`（Avalonia `:13-14`）。
- 补充：Avalonia 也有收 `System.Type` 所有者的两参重载（`RegisterAttached<THost, TValue>(string, Type ownerType, …)`），所以静态类**理论上有路可走**；仓库一律走泛型那条，于是「类必须继承 `AvaloniaObject`」成了家规。

### 2. 属性变化回调挂在**静态构造**里，签名是 `(THost, AvaloniaPropertyChangedEventArgs)`

```csharp
static WorkflowSlotConnectionBehavior() { IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged); }
```
（`WorkflowSlotConnectionBehavior.cs:15-18`、`ViewPool.cs:15-19`、`ViewPool.cs:17-18`。）WPF 是在 `PropertyMetadata` 里传回调、签名 `(DependencyObject, DependencyPropertyChangedEventArgs)`。**回调的第一个参数是宿主控件而不是属性所有者**，所以每个回调里都要自己再取一次 `State` 附着属性（例如 `WorkflowSurfaceBehavior.cs:570-585` 的 `OnZoomEnabledChanged`）—— 这是「属性所有者是静态类、状态必须挂在被附着的控件上」这条约束的直接后果。

### 3. `PreviewMouseWheel` **不存在** —— 滚轮缩放只能靠隧道路由

`WorkflowSurfaceBehavior.cs:587-588` 的注释明写：「Avalonia 没有 PreviewMouseWheel，所以滚轮在 ScrollViewer 上做隧道：它在控件滚动前触发并标记已处理，普通滚轮滚动因此不受影响。」实现是
`state.ScrollViewer.AddHandler(InputElement.PointerWheelChangedEvent, handler, RoutingStrategies.Tunnel)`（`:594`）。
WPF 的对应写法是 `state.ScrollViewer.PreviewMouseWheel += …`（WPF `WorkflowSurfaceBehavior.cs:527`）。

**后果**：这家的滚轮处理器挂在 **ScrollViewer** 上（而不是宿主），且必须是 `Tunnel` —— 先于 ScrollViewer 自己滚动触发、并置 `e.Handled = true`（`:679`），普通滚轮滚动才不受影响。照抄 WPF 的 `PreviewMouseWheel` 在这家编译不过；照抄成 `PointerWheelChanged` 的**冒泡**注册则会被 ScrollViewer 先消费掉。

### 4. `GestureRecognizerCollection` 只公开 `Add` —— 删内置手势识别器必须反射

`WorkflowSurfaceBehavior.cs:842-843`（注释）+ `:837-859`（实现）：ScrollContentPresenter 上的 `ScrollGestureRecognizer` 会在拖动中途抢走指针捕获（触摸平台必现），而这家的 `GestureRecognizers` 是 `IReadOnlyCollection`，**没有 Remove**。于是代码从 `ScrollContentPresenter` 上拿到识别器集合，反射取私有字段 `_recognizers`（`List<GestureRecognizer>`）、`RemoveAll(ScrollGestureRecognizer)`，然后**立刻退订** `LayoutUpdated`（`:857`），避免每帧重扫。

- 这是这家最脆的一处：字段名 `_recognizers` 是私有的，Avalonia 改字段名就静默失效（不会抛，只会回到「拖动被抢捕获」的老症状）。
- 这也是 `PlatformDetection`（§四.1）想解决的问题 —— 但那条路（Tunnel 注册）**没有实现**。

### 5. 自绘入口是 `Render(DrawingContext)` + `AffectsRender<T>(…)`；Avalonia 没有 `OnRender`

实测：`Avalonia.Visual` 上**不存在** `OnRender`，`Render` 是 `public virtual`。所以小地图是
`public override void Render(DrawingContext context)`（`WorkflowMinimapOverlay.cs:525`），用 `context.PushClip` / `FillRectangle(brush, rect, cornerRadius)` / `DrawRectangle(null, new Pen(…), rect, cr)` 画（`:546-548` 起，`PushClip` 在 `:558`）；重绘靠静态构造里登记 `AffectsRender<WorkflowMinimapOverlay>(…)` 列出全部相关属性（`:171-184`，约 25 个），属性一变自动 `InvalidateVisual`。WPF 那份是 `FrameworkElement` + `OnRender` + `DrawingContext`。

- **改这家的自绘时不要去找 `OnRender`**，也不要在属性 setter 里手写 `InvalidateVisual`：属性已在 `AffectsRender` 名单里就够；名单外的属性（例如只在 `Render` 内部读的派生值）才需要手动 `InvalidateVisual`。
- 小地图的 `InstanceMarkDirty`（`:374-385`）是例外：它可能在**非 UI 线程**被调用（注释：`OnNodePropChanged` 可能来自 `TickManager` 的循环线程），所以先 `Dispatcher.UIThread.CheckAccess()`，不行才 `Post(InvalidateVisual)`。`InvalidateVisual` 要求 UI 线程，这是平台硬约束。

### 6. 布局 pass 走**渲染循环**，不在 dispatcher 优先队列上 —— 两个后果

IL 实测（11.1.0）：`LayoutManager.QueueLayoutPass()` → `MediaContext.BeginInvokeOnRender(action)` → `MediaContext.ScheduleRender(…)` → `Dispatcher.InvokeAsync(…, DispatcherPriority.Render)`（`ScheduleRender` 的 IL 里读的就是 `DispatcherPriority.Render` 与 `Input`）。而 `Layoutable.UpdateLayout()` 的 IL 只有一句 `ILayoutManager.ExecuteLayoutPass()`，**`ILayoutManager` 上没有任何「只重排这个控件」的重载**。

后果一 —— **一次 `UpdateLayout()` 就够，不必像 WPF 那样连调三处**。WPF 的缩放提交里写的是
`state.Canvas?.UpdateLayout(); sv.UpdateLayout(); host.UpdateLayout();`（WPF `:579-581`、`:594-596`，各两处），Avalonia 只写 `ApplyLayout(host, state); sv.UpdateLayout();`（`:644-645`、`:657-658`）。因为 Avalonia 这一次调用执行的是整棵树的布局 pass，等价于 WPF 那三句。

后果二 —— **`LayoutUpdated` 里的同步写回与 `Dispatcher.Post(…, Render)` 的延迟写回处在同一优先级带**。这正是 `WorkflowSlotLayoutBehavior` 需要「两条路并存」的原因（§三.5）：延后到 `Render` 的那条不保证赶在本帧渲染之前。

### 7. `Layoutable.LayoutUpdated` 是**整棵树、arrange 之后**的事件（与 WPF 同族，**不是** WinUI 那种逐元素语义）

IL 实测：`Layoutable.OnAttachedToVisualTreeCore` 在挂树时订阅的是 `ILayoutManager.LayoutUpdated`（整棵树那条），再由每个 `Layoutable` 的私有处理器转发成自己的 `LayoutUpdated`；`LayoutManager.ExecuteLayoutPass` 的 IL 里，`InnerLayoutPass`（measure + arrange）跑完之后才 `EventHandler.Invoke` 那条事件。

**这条要紧，因为代码注释把它归错了源头**：`WorkflowSlotLayoutBehavior.cs:128-129` 写着「布局后同步回写（与 WinUI 家一致）：在渲染前读插槽中心，保证连线端点锚点与节点折叠同帧刷新，而不是迟一个 Dispatcher.Post(Render) 节拍（插槽漂移/闪烁的来源）」。而 WinUI 那边的注释（`Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowSlotLayoutBehavior.cs:21-23`）说的是**相反**的事：WinUI 的 `LayoutUpdated` 是**逐元素**的，可能在节点还没移到新位置时就读到旧的 Canvas 坐标，**WPF 免疫恰恰因为 WPF 的 `LayoutUpdated` 是整棵树一次**。
Avalonia 与 WPF 同族 → **同步写在 Avalonia 上成立的理由是 WPF 那条**，与「照 WinUI 抄」无关。抄形状可以，别抄错了理由；将来若有人按 WinUI 那段注释去「修正」Avalonia 的实现（例如改用坐标宿主的 `LayoutUpdated`），那是把 WPF 族当成 WinUI 族在改。

### 8. 命名控件解析：`FindControl<T>(name)`，以及为什么小地图要自己走视觉树

宿主侧用 `control.FindControl<T>(name)`（`WorkflowSurfaceBehavior.cs:539-549`）—— 它是沿宿主名字域往下找，够用。
小地图**不在**宿主的名字域里，且要能反查自己的 ScrollViewer，所以走「先从 `GetVisualParent()` 上溯到根、再从根往下按名字找」（`WorkflowMinimapOverlay.cs:205-208`）。该处的注释还带一条**版本兼容约束**：

> Avalonia 12 移除了 `GetVisualRoot()`、`e.Root` 的返回类型也变了（11 是 `IRenderRoot`，12 是 `Visual`），因此用两版签名一致的 `GetVisualParent()` 向上走。

**这家锁的是 11.1.0，但这段代码是刻意同时兼容 12 的** —— 改这里时不要用任一版本独有的 API「简化」它。

### 9. 附着属性里塞对象当「每宿主状态」是这家的通用手法

`State`（`SurfaceState` / `LayoutState` / `DragState`）都是各自的**私有附着属性**（`WorkflowSurfaceBehavior.cs:81`、`WorkflowSlotLayoutBehavior.cs:39`、`WorkflowNodeDragBehavior.cs:31`），因为行为类自身无实例、状态必须寄存在被附着的控件上。WPF 同形（WPF `WorkflowSurfaceBehavior.cs:113` 的 `StateProperty`）。**新写一家时跟着抄这个手法**，不要改用 `ConditionalWeakTable`（`ViewPool.cs:73` 那种用法是给「不需要在 XAML 里读到的管理器」用的，不是给行为状态用的）。

---

## 三、与其它家的刻意背离

### 1. 平移接受**左键或中键** —— 与 WinUI 同款，与 WPF / Jalium 相反

`WorkflowSurfaceBehavior.cs:989-994`：`properties.IsLeftButtonPressed || properties.IsMiddleButtonPressed`，配合 `:996-1000` 的 `IsPanStillActive`（拖动中任一键按住都算）。逐家核对的结果是**四家两派**：

| 认左键 + 中键 | 只认左键 |
|---|---|
| Avalonia（`:989-994`）、WinUI（`VeloxDev.WinUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:1138-1149`，逐字同形） | WPF（`if (e.ChangedButton != MouseButton.Left) return;`，`:635-638`） |

**要注意的不是「Avalonia 特别」，而是「WPF 不是唯一参照」**：这家的形态与 WinUI 一致，代码与注释都没写理由（CAD/地图类界面把中键当平移是惯例）。副作用是：中键平移到一半、左键随便点一下，`IsPanStillActive` 仍为真，平移不会中断。**别按 WPF 那份去「修正」成只认左键** —— 先决定要哪一派。

### 2. 按下走**冒泡**、释放**不判空白也不判 `IsVisible`** —— 于是「什么算空白」只能在数据上下文层做

- 按下：`state.PointerPressSource.PointerPressed += OnPointerPressed;`（`:506`，默认冒泡）。WPF 是 `state.PointerPressSource.PreviewMouseDown += OnPointerPressed`（WPF `:470`，隧道路由）。Avalonia 要等价写法得用 `AddHandler(InputElement.PointerPressedEvent, handler, RoutingStrategies.Tunnel)` —— 这家**没有**这么做（全目录里唯一的 `Tunnel` 用例是 §二.3 的滚轮，实测 grep：`Tunnel` 只出现在 `WorkflowSurfaceBehavior.cs:594`）。
- 释放：`:804-827`。先处理平移收尾，然后**无条件**执行 `viewModel.VirtualLink.Sender.State &= ~SlotState.PreviewSender;` 与 `viewModel.ResetVirtualLinkCommand.Execute(null)`。WPF 的同位置有两道门：`!viewModel.VirtualLink.IsVisible` 直接返回、且必须 `IsSurfaceBlankInteraction(originalSource, state)` 为真（WPF `:667-678`）。
- 这一对的后果是**互补的**：因为按下/释放都不 `Handled`、释放也不筛来源，这家必须把「命中节点/插槽/连线」的判定放进 `IsSurfaceBlankInteraction`（`:1002-1026`：先排 `DataContext is IWorkflowNodeViewModel or IWorkflowSlotViewModel` 的视觉元素及其祖先，再把连线视觉当成合法空白，最后按引用匹配 `Canvas` / `ScrollViewer` / `PointerPressSource` / `GridDecorator`，并额外认类名 `"ScrollContentPresenter"`）。
- **未验证**：释放时无条件 `ResetVirtualLinkCommand` 是否会在「连线进行中于空白处松开」之外造成可观察的副作用，我没有跑起来验证，只按代码读出来。要动这块的话，先按 WPF 的两道门补，再看是否需要保留无条件路径。

### 3. 插槽连接：按下后**主动释放指针捕获**，且两个处理器都**不置 `Handled`**

`WorkflowSlotConnectionBehavior.cs:36-50`：
```csharp
slot.SendConnectionCommand.Execute(null);
e.Pointer.Capture(null);      // 按下
…
slot.ReceiveConnectionCommand.Execute(null);   // 松开
```
WPF 那份是 `PreviewMouseLeftButtonDown/Up` + 两个处理器都 `e.Handled = true`、**不碰捕获**（WPF `WorkflowSlotConnectionBehavior.cs:39-59`）。

**这里的做法和其他家不一样，因为 Avalonia 的指针捕获会把后续指针事件全部钉在按下的那个插槽上** —— 不主动放开，松开的那个事件永远到不了接收方的插槽，两阶段 `SendConnection`/`ReceiveConnection` 的第二阶段就永远不触发。这也是为什么这家不能在按下时置 `Handled`：事件要继续冒泡（配合 §三.2 的空白判定），否则「点空白取消连线」这类路径会断。

### 4. 插槽布局有**三条**写回路径，别家只有一到两条

`WorkflowSlotLayoutBehavior`：

1. **同步**：`LayoutUpdated` → 直接 `Sync(control)`（`:126-132`）。
2. **异步**：`Visual.BoundsProperty` 变化 → `ScheduleSync`（`:134-138`）。
3. **异步**：DataContext 的 `INotifyPropertyChanged` 且属性名在 `SlotPropertyNames` 里 → `ScheduleSync`（`:140-155`）。

`ScheduleSync`（`:175-189`）用 `state.SyncPending` 合并、`Dispatcher.UIThread.Post(…, DispatcherPriority.Render)` 下发。**三条并存的理由是平台决定的**：布局 pass 在渲染循环上、与 `Render` 优先级同一带（§二.6），所以「延后到 Render」这条路没法保证赶在本帧渲染前，必须再留一条同步的路；而 `Bounds` 与 ViewModel 属性变化又不是每次都有布局 pass，所以两条异步路也不能省。

- 上一家（WPF）是 `LayoutUpdated` + `SizeChanged` + `PropertyChanged`（WPF `:104-105`、`:171`）；这家把 `SizeChanged` 换成了 `BoundsProperty` 的类处理器，**因为 Avalonia 的 `Visual.Bounds` 是 `Rect` 且以 `AvaloniaProperty` 形式存在**（实测：`Visual.Bounds` 是 `Avalonia.Rect`），所以能直接挂在属性变化上，不必等 `SizeChanged` 事件。
- 这家**没有** WinUI 那种「额外订阅坐标宿主的 `LayoutUpdated`」的做法（WinUI `:156`）—— 与 §二.7 同因：这家的 `LayoutUpdated` 本来就是整棵树一次。

### 5. 插槽名匹配是**宽松匹配**：控制名 + 去 `PART_` 前缀形式 + 三个字面量兜底

`:210-231`：`SlotPropertyNames` 除 `Anchor` / `Size` 外，收下 `SlotNames` / `SlotEnumeratorNames` 里每个控制名、每个以 `PART_` 开头的名字**去掉前缀后的形式**，另外**无条件**加三个字面量 `"InputSlot"` / `"OutputSlot"` / `"OutputSlots"`。注释写的理由是「Control names (e.g. `"PART_OutputSlots"`) differ from ViewModel property names (`"OutputSlots"`)」。

**这里的做法和其他家不一样，因为这家把「XAML 里的控件名」与「ViewModel 的属性名」这两个命名空间在适配器里对齐**，代价是那三个字面量兜底会对所有节点生效（包括根本没用这些名字的节点）。改这里的注意：兜底名字加进去就等于给全仓库的节点都开了一个属性名触发器，不要为了某一个 demo 再加一个。

### 6. 模板查找的第三级是 `Application.Current.DataTemplates`

`ViewManager.FindDataTemplate`（`:232-270`）顺序：`_templateMap`（按 ViewModel 类型缓存）→ `TemplateSelector.Match` → 面板自己的 `DataTemplates` → **沿 `Parent` 链往上逐个控件的 `DataTemplates`** → `Application.Current.DataTemplates`。
WPF 那份的第三级是**扫 `Application.Current.Resources`** 找 `DataType` 匹配的 `DataTemplate`（WPF `ViewManager.cs:266-268`）。

**这里的做法和其他家不一样，因为 Avalonia 有 `Application.DataTemplates` 这个有序集合，而 WPF 没有**（WPF 的隐式模板走资源查找）。所以这家的「Self > Parent > App」三级是平台提供的结构，不是自创层级。

### 7. 视图池 batching 与 WPF **相同**（记下来免得重复查）

`batchSize = 3` + `DispatcherPriority.Background`（`ViewManager.cs:119`、`:125`）与 WPF 逐字一致（WPF `:119`、`:125`）；WinUI 是 3 + `DispatcherQueuePriority.Low`，MAUI 是 8 + 自己的 `IDispatcherTimer`。
另：`ViewPool` 的清理只在 `DetachedFromVisualTree`（`ViewPool.cs:53`、`:75-82`），WPF 用 `Unloaded` —— **同形，不是本家差异**；管理器放 `ConditionalWeakTable`（`:73`）两家也一样。

---

## 四、改这里最容易踩的坑

1. **`PlatformDetection.cs` 是死代码，而且它的注释描述的是一条不存在的路径。** 全类 20 行，`IsTouchPlatform` 只有定义没有调用者（`Src/` 与 `Examples/` 全仓库 grep 零命中）。它的 XML 注释写着「On these platforms, PointerPressed handlers **must be registered with Tunnel routing** to pre-empt the ScrollViewer gesture recognizer」—— 实际做法是 §二.4 的**反射删除识别器**，全目录唯一的 `Tunnel` 注册是滚轮（`WorkflowSurfaceBehavior.cs:594`）。
2. **`IsSurfaceBlankInteraction` 会把 ScrollViewer 内部的一切都算成空白**（`:1021-1025` 的祖先判定里有 `ReferenceEquals(x, state.ScrollViewer)`）。WPF 那份在此之后**显式排除**滚动条（WPF `:1135-1137`：`source is ScrollBar || ancestors.Any(x => x is ScrollBar)`），Avalonia 没有这段。
   - **未验证**：Avalonia 里滚动条（Thumb）按下时是否会把 `PointerPressed` 标成已处理、从而根本到不了 `OnPointerPressed`（这家是 `+=` 注册、默认不接收已处理事件）。若会，这条就不会触发；若不会，点击滚动条会**同时**启动一次画布平移。**下一个动这块的人应该先用一次实测把它定下来**，别按 WPF 的结论直接补排除。
3. **拖拽手柄必须有不透明背景，否则收不到指针事件。** `Examples/Workflow/Avalonia Trimmed/Demo/Demo/Views/Workflow/NodeView.axaml` 里拖拽用的 Grid 带 `Background="Transparent"`（`:27`）；插槽视图 `Examples/Workflow/Avalonia Trimmed/Demo/Demo/Views/Workflow/SlotView.axaml:5` 用 `Background="#01000000"`（近透明，仍参与命中测试）。空背景的 `Grid`/`Panel` 在 Avalonia 里不做命中测试 —— 这与 `memory/modules/WorkflowSystem/…` 里「XAML node drag header needs Background="Transparent"」是同一件事，属平台硬限制，不是 demo 的随手写法。
4. **`IsScrollInertiaEnabled="False"`**（demo 的 `PART_ScrollViewer`，`Examples/Workflow/Avalonia Trimmed/Demo/Demo/Views/Workflow/TreeView.axaml:53`）：这家自建了完整的平移逻辑（`WorkflowSurfaceBehavior` 的 `IsPanning`/`PanStartOffset`），滚动惯性会与它抢同一组指针事件。抄 demo 时别把这一行删了。
5. **`_templateMap` 按 ViewModel 类型永久缓存模板**（`ViewManager.cs:235-241`），后来才加进 `Application.DataTemplates` 的模板不会被重新解析；同类型换模板（例如主题切换换掉 DataTemplate）也不会生效。这不是 bug 而是缓存策略，但改模板相关行为时要知道它在那儿。
6. **`WorkflowSurfaceBehavior` 的刷新是 `ScrollChanged` 驱动的**：`OnScrollChanged`（`:861-869`）→ `Refresh(host)`（`:136`）→ `UpdateVisibleRegion`（`:937-953`），而 `UpdateVisibleRegion` 每次都会写 `viewModel.Layout.ViewportOffset`（`:952`）。**`Viewport` 是画布局部坐标、只有适配器写它**这条契约（`extension.md` §3.9-3）在这家由这一处落地；不要在别处再写一次 `Viewport`。
   2026-10-03 起 `Refresh` 里还多了一对：`CaptureViewportRestore`（在 `UpdateVisibleRegion` **之前**取
   `Layout.ViewportOffset`）+ `QueueViewportRestore`（末尾 `Dispatcher.UIThread.Post(…, DispatcherPriority.Loaded)`，
   滚到 `ViewportRestoreScroll` 并按 `ClampValue` 夹到 `GetHorizontalScrollMaximum`）。宿主不再自己滚，
   也不要再自己写 `ViewportOffset` 恢复 —— 见 [../extension.md](../extension.md) §3.9-10。
7. **`ApplyLayout` 每次都新建 `TransformGroup` + `TranslateTransform`**（`:916-935`），并先设 `Canvas.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative)`（`:921`）。缩放/平移期间这是每帧一次的分配 —— 与 WPF 同形，属于已知代价；若要优化，注意 `RenderTransformOrigin` 必须保持 `(0,0)`，否则 `ActualOffset` 的语义就变了。
8. **`TranslatePoint` 在这家返回 `Point?`**（未挂到同一视觉树根时为 null）。所有测量点都要处理 null：`WorkflowSlotLayoutBehavior.cs:277-283`（有坐标宿主时）与 `:286-293`（回退到 `SlotAnchorFromNode`）。**别把这两条回退路径合成一条**：前者用 `SlotAnchorFromVisualCenter` + 宿主 `CanvasLayout`，后者用 `SlotAnchorFromNode`，坐标系不同（`extension.md` §3.9-5 要求按测量到的坐标系三选一）。
9. **`SyncSlot` 在 `Bounds` 未测量时直接返回**（`:272`：`control.Bounds.Width <= 0 || control.Bounds.Height <= 0`）。这是这家版的「NaN 锚点 = 未测量」门（Core 那边是 `WorkflowSlotUpdateGate`）；**不要**在这里改成「用 0 兜底」，那会让连线先在节点原点画一帧再跳走。

10. **池建视图时传的是 VM，不是 `null` —— 传 `null` 会让自定义 `IDataTemplate` 选择器整个失效。** 那一行现在是 `template?.Build(viewModel)`（`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/ViewManager.cs:170`，2026-09-25 从 `Build(null)` 改来）。原因：Avalonia 的 `IDataTemplate` 是「既选又建」—— `Match` 挑出的若是个**选择器**，轮到 `Build` 时才是它挑内层模板的时候；传 `null` 它无从下手（`workflow-template-selector` 的 `SelectTemplate(null)` 抛 `InvalidOperationException`）⇒ **视图一个都不建、画布空着、不报错**。实测（Avalonia Trimmed demo，装记录仪）：`Build(null)` 时 5 次 Build 全失败（1 条连线 + 4 个节点，正好对应基线的 4 张卡），截图是空画布；改成传 VM 后日志为 `Build NodeViewModel -> NodeView`×4 与 `Build LinkDefaultViewModel -> LinkView`，卡片回来。**别把它「简化」回 `Build(null)`** —— 其余三家（WPF/WinUI/MAUI）没有这个问题，因为它们的 `DataTemplateSelector` 只负责「选」，建由适配器 `LoadContent()`/`CreateContent()` 做。

11. **Android 头那一行 `AndroidEnableProfiledAot` 会把整个解决方案的 Debug 构建挡在门外。**
   `Examples/Workflow/Avalonia Trimmed/Demo/Demo.Android/Demo.Android.csproj:11` 一旦无条件设为 `true`，就让 Android SDK 在**求值阶段**就打开 AOT（`Microsoft.Android.Sdk.Windows/36.1.69/targets/Microsoft.Android.Sdk.Aot.targets:24-30` 那段以 `'$(AotAssemblies)' == 'true'` 为条件的 `ImportGroup`），于是去解析 `Microsoft.NET.Runtime.MonoAOTCompiler.Task` 与 `Microsoft.NETCore.App.Runtime.AOT.Cross.android-*`；工作负载不全的机器上解不出来，**`dotnet build VeloxDev.slnx` 连 MSBuild 求值都过不去**（错误挂在 `Microsoft.Android.Sdk.Aot.targets` 上，看着像环境问题，其实是这一行）。**全仓只有这一处设了它**（`grep AndroidEnableProfiledAot` 只有这一行）。现在带上 `Condition="'$(Configuration)' == 'Release'"` —— Debug 不需要 AOT。
   判据：改前 `dotnet clean VeloxDev.slnx -c Debug` 4 个 error、`dotnet build … -p:AotAssemblies=false` 才能过（还带 3 条 `XA1029`）；改后 `dotnet clean` 与 `dotnet build VeloxDev.slnx -c Debug` 都是 **0 错误 0 警告**。
   另注：**`dotnet build VeloxDev.slnx -t:Rebuild` 在解决方案上不安全** —— 那是「每个项目各自 Clean+Build」，引用项目的 clean 会与依赖方的 build 抢文件（实测 25 个 error：`CS0006 找不到元数据文件`、`MSB3030 复制失败`、WinUI 的 `XamlCompiler.exe` 退出、NuGet 打包找不到 xml）。VS 的「重新生成解决方案」是整解先 clean 再 build，CLI 的等价写法是 `dotnet clean` + `dotnet build` 两步。

---

## 五、非 Trimmed demo 的连线：视图只画，命中几何在 Core，悬停高亮与删除归宿主

两个自绘连线视图各有一份，**都要改** —— demo 的 `WorkflowView.axaml` 按 `LinkViewModel.UsePolyline`（默认 `true`，字段在 `Examples/Workflow/Common/Lib/ViewModels/Workflow/LinkViewModel.cs:14`）在两者之间切换，不是死代码。**默认显示的是 `PolylineCurveView`**（带流动光带那个），`BezierCurveView` 是关掉光带的对照。

**这两个视图现在都不碰指针命中、也不自己转发指针事件**：命中的几何由 Core 的 `LinkHitTestEx` / `LinkCurve` 判，事件由 Core 的 `WorkflowInput` 路由（用 `WorkflowInput.For(tree)` 取，一棵树一个），视图只做两件事 —— 画，以及把画出来的曲线发布给命中契约（另订自己 helper 的 `Input` 决定亮不亮，见下表）。两者都把 `LinkCurve.BuildLinkCubic(DataContext as IWorkflowLinkViewModel, StartLeft, StartTop, EndLeft, EndTop, PullMinimum)` 交给 `link.PublishCurve(_curve, this)`（`PolylineCurveView.axaml.cs`、`BezierCurveView.axaml.cs` 的 `RefreshGeometry`；`PullMinimum = 40`），发布出去的那条就是命中用的唯一几何。**区别只在有没有流动光带，不在曲线形状**：`BezierCurveView` 绘制时另用一份 `Controls()` 拉控制点，那一份也来自 Core 的 `LinkCurve.LinkCurvePoints` —— 两处若各推一遍几何，弯的地方命中就会对不上指针（`PolylineCurveView` 直接画 `_curve` 的采样，没有第二份）。控制点沿每个口自己那条边的外法线拉，不是写死水平，理由见 [`architecture.md` §3.4](../architecture.md)。

| 事 | 现在归谁 | 锚点 |
|---|---|---|
| 命中 | Core：沿发布曲线的采样段逐段判点到线段距离，`DefaultHitRadius = 6d` | `LinkHitTestEx.cs:18`、`LinkCurve.cs:318-331`、`LinkHelper.cs:46-47`；`HitTestVisibleLinks`（`LinkHitTestEx.cs:76-93`）从 `VisibleItems` **末尾往前**、跳过 `VirtualLink`、被节点卡盖住的不算 |
| 右键菜单 | **适配器**订树 helper 的 `Input.PointerPressed`，只有右键落在连线上才弹（自己判 `e.Button == Right` 与 `e.Target is IWorkflowLinkViewModel`），按宿主根元素上的 `LinkMenuKey` 取出声明的菜单实例、定位并弹出；菜单指着的那条线离树时树 helper 的 `LinkRemoved` 报到适配器，适配器只收自己那份弹窗 | 适配器 `WorkflowSurfaceBehavior.cs` 的 `WireLinkMenu`（`:153-217`）/ `ShowLinkMenu`（`:254-279`）；模板与两个 demo 的 code-behind 都不含这段 |
| 悬停高亮 / 取焦点 | 高亮是这本 demo 自己的：连线视图订**自己** helper 的 `Input.PointerEntered/Exited`，写自己的 `IsHighlighted`；适配器 `FocusHoveredLink` 把键盘焦点交给画线的控件，不可聚焦时退回宿主 | `PolylineCurveView.axaml.cs:211-235`；`WorkflowSurfaceBehavior.cs:345-358`（宿主 `Focusable = true` 在 `:424`） |
| 删除 | 归宿主：**demo 的 code-behind** 订树 helper 的 `Input.KeyDown`，`Delete` 落在指针停着的那条线上就执行 `link.DeleteCommand`；菜单项绑的也是它 | `WorkflowView.axaml.cs:652-663`；`WorkflowView.axaml:37` |

三条结论：

1. **命中面是画出来的那圈描边，不是整块画布框**（实测 2026-09-26，SendInput 从窗口外跳到「离线约 19px 的空画布」上：线体保持静息青色、`PointerEntered` 不触发；压到线上才高亮）。现在这条由 Core 落实：`HitTest` 先要求 `link.IsVisible`、再要求有一条已发布曲线，且点落在其半径带内才算命中（`LinkHitTestEx.cs:54-58`）；视图的 `PublishCurve()` 在 `!IsVisible` 时直接返回、不再发布（`PolylineCurveView.axaml.cs:295-303`），池化换绑时旧链接的曲线由 `old.PublishCurve(null)` 撤掉（`:252`），空白处因此不会命中。半径 6 与框架给的带宽同量级（最外那圈辉光是本体 + 9px，半宽 ≈ 5.5px），既不放宽也不收窄实际命中面。**别按「整块画布都会命中」这条错读去改这层逻辑**。
2. **菜单项绑命令是刻意的**：菜单是 XAML 里的声明资源 `WorkflowTreeMenu`（`WorkflowView.axaml:36-38`，模板里同键），宿主根元素用 `behaviors:WorkflowSurfaceBehavior.LinkMenuKey="WorkflowTreeMenu"` 指出它（键而非实例：该属性挂在宿主根元素上，`{StaticResource}` 会在定义它的资源字典之前解析）；适配器弹出前把菜单的 `DataContext` 设成那条连线，条目写 `Command="{ReflectionBinding DeleteCommand}"`。**必须用 `{ReflectionBinding}` 而非 `{Binding}`** —— 这家的 `AvaloniaUseCompiledBindingsByDefault=true`（`Examples/Workflow/Avalonia/Demo/Demo.csproj:8`），而资源里的 `ContextMenu` 没有 `x:DataType` 作用域，编译绑定在此无从下手。删除归宿主：demo 的 code-behind 订树 helper 的 `Input.KeyDown` 执行 `link.DeleteCommand`（`WorkflowView.axaml.cs:652-663`），菜单只负责发命令。
3. **弹菜单不再把高亮弄掉**：popup 把指针从视图上拿走，仍会触发 `OnPointerExited` → 适配器照发 `PointerExitedEventArgs`（`WorkflowSurfaceBehavior.cs:752-758`），但菜单开着时输入路由的 `WorkflowInput.IsSuspended` 为真，Core 的 `ApplyDefault` 直接返回、不改指针目标（`WorkflowInput.cs:201-203`），这条线的选中/高亮留着。`IsSuspended` 由**适配器**把菜单的 `Opened`/`Closed` 报回输入路由来收放（`WorkflowSurfaceBehavior.cs` 的 `WireLinkMenu`，2026-10-03 起整条接线都在 `LinkMenuKey` 之后），宿主、模板与适配器都不再自己记账（适配器侧没有 `IsSuspended` 守卫）。**菜单不许比它针对的线活得久**这条判据由树既有的 `LinkRemoved` 提供：菜单开着时那条线从 `tree.Links` 离树（Delete 键 / Agent / Undo 任何删除路径），**树 helper 的 `LinkRemoved`** 报到适配器（输入路由不自行放开 `IsSuspended`）；适配器收到后只 `state.LinkMenu?.Close()` 收自己的弹窗，`Closed` 照常报回、挂起随之释放 —— 判据与执行分开，适配器不再自己比菜单之外的东西。**别在视图的 `OnPointerExited` 里按「菜单是否打开」跳过取消** —— 那会把某条线的高亮永久留在画布上（`Closed` 后没有配对的 `Entered`）；这条由 `WorkflowInput.IsSuspended` 统一兜住，不要退回视图级开关。

**实测（2026-09-26，SendInput + 闭环伺服取点，每一步先断言）**：指针经伺服落在线体上（48×48 邻域内体色像素 ≈25–160 → 同一点变暖色 ≈340 = 高亮，说明框架认的是「画出来的描边」而不是整块画布框）→ 合成右键 → **原生 `ContextMenu` 弹出，只有一项**（当时标题是「删除连线」，2026-10-03 起英文 `Delete`，`WorkflowView.axaml:37`）→ 合成左键点该项 → 那条线消失（两端端口由白/绿变灰）。`hitRadius = 6.0` 与框架给的带宽同量级（最外那圈辉光是本体 + 9px，半宽 ≈ 5.5px），既不放宽也不收窄实际命中面；它对右键这条路径是活的判据。

**悬停取焦点的连带代价 = `ScrollViewer` 的 `BringIntoViewOnFocusChange`（默认 `true`）。** 连线视图是整块画布大小，于是「焦点一落到它身上，画布就跳一段」—— 用户报的就是这个（与 WPF 那次同源，触发点是适配器悬停路径里的 `target.Focus()`，`WorkflowSurfaceBehavior.cs:357`）。拦法：**在发源地吃掉这条请求**，两个连线视图的构造函数里各写一次 `AddHandler(RequestBringIntoViewEvent, (_, e) => e.Handled = true);`（`PolylineCurveView.axaml.cs:60`、`BezierCurveView.axaml.cs:38`）—— 作用域刻意收在连线视图上，节点卡里输入框被聚焦时照样滚进视口。**不要**改成 `protected override void OnRequestBringIntoView(...)`：那个符号不是可继承的虚方法，编译报 `CS0115`。**实测（2026-09-26，同一套断言链）**：先把画布滚到非零偏移 `视口(画布) 210, 42`（并断言按下点无线体），再悬停一条线 —— 同一点由体色变暖色（高亮）、随后 `VK_DELETE` 把那条线删掉（浮层「连线 N/M」总数 12 → 11）= 焦点确实拿到了，而 `视口(画布)` 前后都是 **210, 42**（离悬停点较远的画布区域 0 个像素变化）；指针移开到空白处后仍是 210, 42。

**给别人量这块时的两个坑**：(a) **一份没关掉的菜单会吞掉之后的全部悬停** —— 菜单开着时 `WorkflowInput.IsSuspended` 挂着，适配器照发 Enter/Move、Core 的 `ApplyDefault` 却一律早退（`WorkflowInput.cs:201-203`），量出来像是「悬停坏了」；下一次测量前必须先把菜单关掉（`Esc`）并断言它真的关了。(b) **Avalonia 的 UIA 树里看不到 `ContextMenu` 的菜单项**（`AutomationElement.RootElement` 下按名字找不到 `Delete`），所以「菜单有没有弹」在这家只能靠像素/肉眼判，别把 UIA 查不到当成没弹。
