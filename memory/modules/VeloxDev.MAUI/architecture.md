# VeloxDev.MAUI — 架构

> 代码：`Src/Adapters/VeloxDev.MAUI/`。**31 个 .cs、8052 行**（`Attached/Workflow/` 9 个 6723 行，最大的是 `WorkflowSurfaceBehavior.cs` 2012、`WorkflowLinkOverlay.cs` 1471、`WorkflowMinimapOverlay.cs` 816；`PlatformAdapters/` 本级 9 个 807 行（最大 `ThemeValueConverters.cs` 373、`Transition.cs` 250）；`PlatformAdapters/Samplers/` 12 个 519 行；顶层 `GlobalUsings.cs` 3 行）。
> **计数写法**：`git ls-files 'Src/Adapters/VeloxDev.MAUI/*.cs' 'Src/Adapters/VeloxDev.MAUI/**/*.cs'`。只写 `'.../**/*.cs'` 会得到 **30** —— 这条 pathspec 只匹配**子目录里**的 `.cs`，本级文件一个都不算（漏掉 `GlobalUsings.cs`；同理 `PlatformAdapters/**/*.cs` 只有 12 个采样器，`PlatformAdapters/` 本级那 9 个要另写 `PlatformAdapters/*.cs`）。七家适配器每家都正好漏这 1 个。
>
> 本文只写「读完这 31 个文件才知道的东西」。类型清单、成员表、继承树请看 IDE。
>
> **本模块没有 `adapters/` 子目录，也不该有**：模块名本身就是一个平台，不存在平台轴。它在三条轴上的平台差异分别落在
> `memory/modules/WorkflowSystem/adapters/maui.md`、`memory/modules/TransitionSystem/adapters/maui.md`、`memory/modules/DynamicTheme/`，本文**指路不抄**。

---

## 一、一个项目、三条轴、三份契约

这个项目叫「MAUI 适配器」，但它实际同时实现了三个 Core 模块的平台侧契约：

| 轴 | Core 契约 | 本项目落点 | 平台差异记在哪 |
|---|---|---|---|
| WorkflowSystem | 七个视图角色 + 模型事件桥 | `Attached/Workflow/`（9 文件） | `memory/modules/WorkflowSystem/adapters/maui.md` |
| TransitionSystem | 宿主 / 解释器 / 调度器 / 帧 pacer / 采样器 | `PlatformAdapters/`（9 文件 + `Samplers/` 12 个） | `memory/modules/TransitionSystem/adapters/maui.md` |
| DynamicTheme | `IThemeValueConverter` | `PlatformAdapters/ThemeValueConverters.cs` | `memory/modules/DynamicTheme/architecture.md` |

**目录名与轴不对齐，别按目录推契约**：`Attached/` 只有 WorkflowSystem 一条轴；TransitionSystem 与 DynamicTheme 混在 `PlatformAdapters/` 里。这与 WPF 同形，是七家共有的排布。

### 1.1 三条轴的命名空间都寄生在 Core 上，而 Attached 那一侧靠**嵌套**白拿 Core 的类型

三条轴各自占用 Core 那一条命名空间：`VeloxDev.TransitionSystem`、`VeloxDev.WorkflowSystem.AttachedBehaviors`、`VeloxDev.DynamicTheme`。采样器是唯一例外，在 `VeloxDev.Adapters.NativeSamplers`。

**`VeloxDev.WorkflowSystem.AttachedBehaviors` 不是随便挑的名字 —— 它嵌套在 Core 的 `VeloxDev.WorkflowSystem` 里面**，于是这 9 个文件不写 `using VeloxDev.WorkflowSystem;` 也能直接用 `IWorkflowTreeViewModel`、`Viewport`、`WorkflowSurfaceMath`（全都声明在 `VeloxDev.WorkflowSystem`）。证据是 `WorkflowMinimapOverlay.cs`：它在 `:21` 实现 `IWorkflowMinimapOverlay`、在 `:53` 用 `typeof(IWorkflowTreeViewModel)`、在 `:64` 写 `IWorkflowTreeViewModel?`，**三处都是裸名**，而该文件的 using 里**根本没有 `using VeloxDev.WorkflowSystem;` 那一行** —— 它只有输入家族要求的两个别名（`Wf` `:4`、`WfAnchor` `:13`，见 [input-alias-specifications.md](../../specifications/input-alias-specifications.md)）。别名不引入命名空间，所以那几个裸名确实是靠命名空间嵌套解析的。

推论（改这 9 个文件的头部之前先看这一条）：

- **那 6 个文件里的 `using VeloxDev.WorkflowSystem;` 是多余的**（`ViewManager.cs`、`WorkflowLinkOverlay.cs`、`WorkflowNodeDragBehavior.cs`、`WorkflowSlotConnectionBehavior.cs`、`WorkflowSlotLayoutBehavior.cs`、`WorkflowSurfaceBehavior.cs`），删掉不影响编译。真正**必需**的是 `WorkflowSurfaceBehavior.cs` 的 `using VeloxDev.WorkflowSystem.StandardEx;` —— StandardEx 是**子**命名空间，不在向上查找的链上，扩展方法必须显式引入（`StandardSetAnchor` 族在 `Src/Core/VeloxDev.Core/WorkflowSystem/StandardEx/WorkflowNodeEx.cs`）。`WorkflowEvents.cs` 另需显式 `using VeloxDev.WorkflowSystem;`（`:2`）——它没有走别名那条路，而 `WorkflowMinimapOverlay.cs` 连这一行也没有，靠的是 `Wf` 别名加命名空间嵌套（见上段）。
- 反过来说：**把 Attached 文件挪到别的命名空间（比如为省 CS0433 而改名）会一次性打断所有对 Core 类型的引用**，而且报的是一整串「找不到类型」，看不出是命名空间的事。

### 1.2 「同名类型」在本模块是硬约束，不是风格

`ViewManager` / `ViewPool` / `WorkflowSurfaceBehavior` / `WorkflowMinimapOverlay` 这些类型名在**七家适配器里逐家各有一份**，且都在 `VeloxDev.WorkflowSystem.AttachedBehaviors` 这一个命名空间下（全仓 `git grep -l 'namespace VeloxDev.WorkflowSystem.AttachedBehaviors'` 命中大量 `.cs`，全部在 `Src/Adapters/*/Attached/`，Core 命中 0 次，其中本家贡献 9 个）。后果：

1. **宿主 XAML 必须写 `assembly=`**：`xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.MAUI"`（`Examples/Workflow/MAUI Trimmed/Demo/Controls/Workflow/TreeView.xaml`）。写成 `assembly=VeloxDev.Core` 一个行为类型都找不到。
2. **同时引用两家适配器的 C# 项目会 CS0433**。代价的真实样貌见 `Examples/Transition/AUTO TEST/Samplers/MauiEntries.cs:55`：它按程序集 `GetType($"VeloxDev.Adapters.NativeSamplers.{samplerName}", throwOnError: true)` 反射取类型。
3. **给某个公开类型改名 = 跨七家的决定**，且漏改的项目只在「同时引两家」时才炸 —— 见 `extension.md` §四。

### 1.3 不解决什么（常被误以为在这里）

| 不在模块内 | 实际归谁 |
|---|---|
| 七个角色的职责、附着属性名、注册位置 | `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3 |
| 缩放/平移/虚拟化/槽位锚点的**数学** | Core `WorkflowSurfaceMath`、`WorkflowSlotUpdateGate` 等 —— 本家只**调用**（`WorkflowSurfaceBehavior.cs` 多处） |
| 契约需要哪些成员、`FindOrCreate` 为什么必须用 | `memory/modules/TransitionSystem/extension.md` |
| 「主题是什么」「切换什么时候发生」 | DynamicTheme 模块 |
| 生成器 / 分析器 | **本项目不引用分析器包**（`VeloxDev.MAUI.csproj` 的引用项只有 `Microsoft.Maui.Controls`、`VeloxDev.Core`、`Microsoft.WindowsAppSDK`，`:41-52`），自己一行 `[VeloxProperty]` 都没有 |
| 装饰器（网格/标尺） | 本家**不提供**实现，由模板/demo 写（`Examples/Workflow/MAUI Trimmed/Demo/Controls/Workflow/WorkflowGridDecorator.cs`）。本家只**喂**它，见 §三·3 |
| `dotnet new` 模板包 | `Src/Templates/VeloxDev.MAUI.Templates/working/VeloxDev.MAUI.Templates.csproj` 是**纯内容包**（`IncludeBuildOutput=false`、`EnableDefaultCompileItems=false`、无任何 `ProjectReference`）⇒ 装模板**不会**带来 `VeloxDev.MAUI` 依赖，宿主必须自己引。它还把 `skills/veloxdev-workflow-item-templates/references/**` 打进包（`:27`）—— 该 glob 相对 `working/` 算下来是 `Src/Templates/skills/…`，**仓库里不存在，是死的**（与 WinUI 那条恒不匹配的同名 glob 一样） |

---

## 二、`PlatformAdapters/`：注册与工厂

### 2.1 三条注册路径，只有一条需要宿主动手

| 要注册的东西 | 注册点 | 什么时候真的发生 |
|---|---|---|
| 12 个采样器 | `Interpolator` 的**静态构造**（`PlatformAdapters/Interpolator.cs:8-22`；12 条登记在 `:10-21`） | 第一次**构造** `Transition<T>` 时：Core 的字段初始化 `protected TInterpolatorCore interpolator = new();`（`Src/Core/VeloxDev.Core/TransitionSystem/Effects/Transition.cs:296`，约束 `:270` 的 `new()`） |
| 采样器所在的宿主/解释器/优先级 | `Transition<T>` 的类型实参（`PlatformAdapters/Transition.cs:11-19`）与 `TransitionScheduler`（`TransitionScheduler.cs:3`） | 同上，全部编译期写死 |
| DynamicTheme 的调度器工厂 | `Interpolator.CreateScheduler`（`PlatformAdapters/Interpolator.cs:25-28`） | **宿主必须显式调** `ThemeManager.SetPlatformInterpolator(new Interpolator())` |

**没有程序集级入口，也没有 `Initialize()`**：全模块 grep `ModuleInitializer` / `[assembly:` 零命中，唯一的静态构造就是 `Interpolator.cs:8`。⇒ 只用 `Transition<T>` 的宿主什么都不用做；而只走 DynamicTheme 的宿主漏掉 `SetPlatformInterpolator` 会**静默瞬切**（机制与各家的差异见 TransitionSystem 记忆）。

**本家的第二条「宿主接不上」比别家更彻底**：`Examples/Transition/MAUI/Demo/MauiProgram.cs` 与 `Examples/Workflow/MAUI Trimmed/Demo/MauiProgram.cs` 都没有 `SetPlatformInterpolator`（grep 全仓 `SetPlatformInterpolator` 只命中 Avalonia/WPF 的 demo）—— 因为**本仓库根本没有 MAUI 的主题 demo**（`Examples/Theme/` 只有 Avalonia 与 WPF）。

工厂写的是 `FindOrCreate` 而不是 `new`（`Interpolator.cs:27`）—— 只有它会把调度器**按 target 归档**，这是之后 `Pause`/`Seek`/`Exit` 找得回动画的唯一原因（契约在 `TransitionSystem/extension.md`）。

### 2.2 12 条注册是本模块的宿主形状决定的，不是抄来的

`Interpolator.cs:10-21` 的 12 个键里有 **6 个是本家独有的类型对**：MAUI 同时提供单精度的 `Microsoft.Maui.Graphics.PointF/SizeF/RectF` 与 Controls 的 `Point/Size/Rect/Thickness`，所以别家各一份的采样器在这里成对注册（`Point:PointF`、`Size:SizeF`、`Rect:RectF`），另加 `ShadowSampler`。这条的展开在 `TransitionSystem/adapters/maui.md` §一/§三·3，本文不重复。

**`Interpolator.cs` 与 12 个采样器**一个都不 `using System.Drawing`，所以两个文件里的裸名 `PointF`/`SizeF`/`RectF` 一律绑到 MAUI 一侧，与注册键一致（`RectFSampler` 不 import `System.Drawing`，见 §五·1）。

### 2.3 `Property(...)` 重载表由本家自己写，Core 一个都没有

Core 的 `TransitionCore` **不提供** `Property(...)`。所以每个适配器各自手抄一份闭合的重载清单，本家是 `PlatformAdapters/Transition.cs:35-242`：

- `:35` 一个泛型 `Property<TValue>` 兜底；
- 走 MAUI 类型的若干条（`Brush?`/`Transform?`/`Point`/`PointF`/`CornerRadius`/`Thickness`/`Color?`/`Size`/`SizeF`/`Rect`/`RectF`/`Shadow?`/`int`/`double`/`float`/`decimal`）；
- 走 `System.Drawing.*` 的一组，**全部逐字全限定**；
- 走 `System.Numerics` 的 4 个，在 `#if !NETSTANDARD2_0` 里。

**唯一语义不同的重载是 `Transform`（`:50`）**：单个 transform 直接赋值以保留运行时类型，多个才包 `TransformGroup`（理由写在 `:54-55` 的注释：包成 group 会改变运行时类型，破坏 `((TranslateTransform)x.RenderTransform).X` 这类嵌套路径）。其余都是「`state.SetValue` + 可选 `state.SetOptions`」两行。

⇒ **加一个新可动画类型要动两处**：`Samplers/` 一个类 + `Interpolator.cs` 一行注册 +（若要表达式的编译期重载）`Transition.cs` 一段。步骤见 `extension.md` §三·1。

---

## 三、`Attached/Workflow/`：每个角色的入口与挂载方式

### 3.1 八种挂法，三道门槛

| 角色 | 怎么挂上去 | 宿主类型门槛 | 每元素状态存哪 | 需要谁的 `DataContext` |
|---|---|---|---|---|
| 画布宿主 `WorkflowSurfaceBehavior` | `IsEnabled="True"` + 5 个 `*Name` + `ZoomEnabled` + `LinkMenuKey`（`:92`-`:150`） | **`ContentView`**（`:610`） | 私有附着 DP `State`（`:152`），装 `SurfaceState`（`:11-90`） | 宿主自己，且沿宿主→Canvas→ScrollViewer→GridDecorator→PointerPressSource 逐级试探（`ResolveTreeViewModel` `:1608`） |
| 插槽布局 `WorkflowSlotLayoutBehavior` | `IsEnabled="True"` + `SlotNames`/`SlotEnumeratorNames`/`CoordinateHost*` | **`ContentView`**（`:85`） | 私有附着 DP `State`（`:65`） | 被挂的那个 `ContentView` 自己 |
| 节点拖拽 `WorkflowNodeDragBehavior` | `IsEnabled="True"` + `CoordinateHost*`（`:43-53`） | **`View`**（`:75`）——最宽 | 私有附着 DP `State`（`:55`） | 自己或祖先（`ResolveCoordinateHost` `:363-388` 的 `FindByName` → 祖先类型回退） |
| 插槽连接 `WorkflowSlotConnectionBehavior` | `IsEnabled="True"`（`:36`） | **`View`**（`:57`） | 私有附着 DP `State`（`:43`） | 自己；坐标宿主与画布**靠遍历祖先现找**，见 §3.2 |
| 视图池 `ViewPool` | 赋 `ItemsSource`（`:12-17`）+ `TemplateSelector`（`:19-24`） | **`Layout`**（`:42`/`:60` 的 `is not Layout`） | `ConditionalWeakTable<Layout, ViewManager>`（`:38`） | 不读；它是**写** DC 的那一方 |
| 小地图 `WorkflowMinimapOverlay` | 不用挂：**继承它** + 赋 `WorkflowTree` | 继承（`GraphicsView, IDrawable, IWorkflowMinimapOverlay`；`public class`，全模块唯一非 `sealed`） | 实例字段（它本身就是元素） | 不读，它自己订阅 Core 模型（`:286` 起） |
| 链接层 `WorkflowLinkOverlay` | 不用挂：XAML 里**绑**它的偏移 DP | 元素本身（`GraphicsView` 子类） | 实例字段 | 不读 |
| 模型事件桥 `WorkflowEvents` | 绑 `Node`/`Slot`/`Tree` 到 sink | `VisualElement` | `ConditionalWeakTable` | 订阅跟随 `BindingContext`（转发在 Core `WorkflowEventRelay`） |

**`IsEnabled` 默认 false**，四个开关注册时给的都是 `false`，必须显式打开。`ViewPool` **没有** `IsEnabled`（靠赋值触发，`OnTemplateSelectorChanged` `:58-67` 永远是 Cleanup → Ensure 两步，不看开关）。

每个 `OnIsEnabledChanged` 都是**先 `Detach` 再 `Attach`**（`WorkflowSurfaceBehavior.cs:608-622`，`Attach` `:624` 的第一行就是 `Detach`），所以重复置 `True` 不会叠加订阅；`Detach` 里 `ClearValue(StateProperty)`（`:658`）顺带丢状态。

### 3.2 坐标宿主有两套约定，唯独连接行为不属于任何一套

另两个行为用 `CoordinateHostName` + `CoordinateHostType` 附着属性（`WorkflowSlotLayoutBehavior.cs`、`WorkflowNodeDragBehavior.cs:43-53`），解析方式是「先 `FindByName`，再沿祖先找第一个可赋成 `hostType` 的，默认 `AbsoluteLayout`」（`WorkflowNodeDragBehavior.cs:363-388`）。

**`WorkflowSlotConnectionBehavior` 没有任何 `CoordinateHost*` 属性**（它的 DP 只有 `IsEnabled`（`:36`）和私有的 `State`（`:43`））。它自己走两条固定规则：

- 坐标宿主 = **最近的祖先 `AbsoluteLayout`**（`FindCoordinateHost` `:486-497`）；
- 画布 = **最近的祖先 `ContentView` 且 `WorkflowSurfaceBehavior.GetIsEnabled` 为真**（`FindSurface` `:499-510`）。

⇒ **给它挂 `CoordinateHostName` 是静默无效的**（那是个不存在的附着属性，XAML 里根本不解析）；反过来，把插槽放在非 `AbsoluteLayout` 的宿主里、又没让 `CoordinateHost` 指对，另两个行为能救、这一个救不回来。见 `extension.md` §二·3。

### 3.3 两个 overlay 的**不对称**：一个被推、一个自己拉

- `WorkflowMinimapOverlay` **实现 `IWorkflowMinimapOverlay`**（`:21`），由宿主**推**数据：`UpdateMinimapOverlay`（`WorkflowSurfaceBehavior.cs:1691-1705`）写 4 个偏移 + `ViewportWidth/Height` + `WorkflowTree`。
- `WorkflowLinkOverlay` **不实现 `IWorkflowGridDecorator`，也不实现任何 Core 接口**（`WorkflowLinkOverlay.cs:54` 就是 `GraphicsView`）。它的偏移/标尺 DP 与网格装饰器**同名同义**（`ScrollOffsetX/Y`、`ContentOffsetX/Y`、`RulerThickness`，`:64-77`），另加 `WorkflowTree`（`:60`）、`InteractionSource`（`:91`）、`LinkLineColor`/`VirtualLineColor`/`StrokeWidth`/`LinkFlowEnabled`（`:79-89`）。**悬停高亮不在这一层**（2026-10-05 起）：overlay 只画静息线，演示里由 demo 自己叠的 `LinkHighlightLayer` 沿 `ILinkHitTestable.Curve` 画，在 XAML 里是**从 GridDecorator 实例上绑过来**的（`Examples/Workflow/MAUI Trimmed/Demo/Controls/Workflow/TreeView.xaml` 的 `Source={x:Reference PART_GridDecorator}`）。

⇒ **网格装饰器上是这层唯一的偏移源**。想「让链接层自己从宿主拿偏移」得先给它加接口实现，现在没有这条路。反过来，只喂装饰器不喂链接层 = 链接层停在 0 偏移上（因为它默认值全是 `0d`）。

### 3.4 `Refresh` 是唯一的重驱动入口，被两道闸挡着

`WorkflowSurfaceBehavior.Refresh(ContentView)`（`:179-214`）重解析命名部件 → `ApplyLayout` → `UpdateVisibleRegion`。两道闸在**最前面**：

1. `if (!GetIsEnabled(host)) return;` —— 没打开开关时调它什么也不做、不抛。
2. `if (state.IsRefreshing) return;`（`:198`）—— 重入守卫（注释在 `:196-197`：每次画布扩张级联多次 Refresh，正反馈减速螺旋）。

插槽布局那边同名的 `Refresh` 只是 `=> ScheduleSync(control)`（`WorkflowSlotLayoutBehavior.cs:81`），语义是**合并一次的 16ms 延后**，与上面的「立刻做完」不是一回事 —— 两条同名 API 别互推。

### 3.5 谁写 Core 模型：只有一处

写 Core 模型状态的是 `ApplyVisibleRegion`（`:1637-1673`）：写 `viewModel.GetHelper().Viewport = new Viewport(...)` 与 `viewModel.Layout.ViewportOffset`。它上游是 `UpdateVisibleRegion`（`:1615-1635`），用 `MainThread.BeginInvokeOnMainThread` 把一帧内的多次请求**合并成一次**（守卫 `IsVisibleRegionUpdateQueued` `:1617`，回调里还要再验一次状态身份）。

2026-10-03 起**写进去的是世界坐标**；同轮接上了挂树/加载/绑定恢复：`CaptureViewportRestore`（`:555`）在三处入口读取存档，`RequestViewportRestore`（`:569`）与 `TryGetViewport`（`:590`）是配套的公开入口（`WorkflowSystem/extension.md` §3.9-10）。

装饰器/小地图的偏移是**另一条路**：`ApplyLayout`（`:1198` 起）在**每次** Refresh 里直接从 `ScrollViewer.ScrollX/Y` 写一遍，而 `ApplyVisibleRegion` 又写第二遍（:1675`/`:1691`）。第二次写是免费的（两个 overlay 都合并重绘，注释 `:1223` 附近说明），第一遍保证**平移途中**网格与内容同帧一致。

---

## 四、不变量：谁拥有状态，什么必须成对

1. **每个附着行为的每元素状态只住在私有附着 DP `State` 里**，`Detach` 时 `ClearValue`。没有全局注册表（唯一的例外是 `ViewPool` 的 `ConditionalWeakTable`，`:38`，因为它的状态天然按 `Layout` 归档）。
2. **两处进程级静态是刻意的**：`WorkflowNodeDragBehavior.IsDraggingNode`（`:14`）与 `WorkflowSlotConnectionBehavior._activeConnection` / `IsDraggingConnection`（`:32`/`:34`）。它们表达的是「全局同一时刻只允许一次节点拖拽 / 一次连线拖拽」，不是缓存。`IsDraggingNode` 被**平移逻辑**读（`WorkflowSurfaceBehavior.cs:1257`/`:1373`/`:1411`：拖节点时把平移锚点贴到当前位置，避免拖拽结束后平移跳一段），所以删掉会连累平移。跨行为的读法只有这几处，别扩大。
3. **`Attach` 必须先 `Detach`**；`Detach` 必须可重入（先 `-=` 再清状态）。所有 `OnIsEnabledChanged` 都依赖这一点。
4. **视图被隐藏时留在 `_layout.Children` 里**：只 `IsVisible=false` + `ZIndex=-100`（`ViewManager.cs:288-289`、`:309-310`，移除会触发昂贵的 MAUI 重排）⇒ 遍历子元素时**不能假设「看不见 = 不在」**。
5. **窗口增长方向是单向的**：`ApplyLayout` 里 `WidthRequest/HeightRequest` 只增不减（`:1215-1220` 的两个 `if` 都带 `<` 判定），尺寸还有个 `Math.Max(1, ...)` 下限。
6. **`ApplyVisibleRegion` 的 NaN 提前返回是硬前提**（`:1652-1654`）。`NaN <= 0` 在 C# 里是 `false`，Core 的守卫抓不到，一旦放进去会让 `Viewport → Virtualize → 空间索引` 把**整批 VisibleItems 清空**（注释 `:1645` 明写「links permanently disappear」）。

---

## 五、陷阱（带依据）

1. **注册键与采样器实现必须是同一个 `RectF` —— 这个名字在不同 using 下会解到不同类型。**
   - `PlatformAdapters/Interpolator.cs:20` 的 `RegisterInterpolator(typeof(RectF), new RectFSampler());`：该文件只 `using` 了 `Microsoft.Maui.Controls.Shapes`（`:1`）与 `VeloxDev.Adapters.NativeSamplers`（`:2`），**没有 `System.Drawing`**；而 `System.Drawing` 里**没有叫 `RectF` 的类型**（它叫 `RectangleF`）。MAUI 的隐式 using 里有 `global using Microsoft.Maui.Graphics;`（生成物 `obj/.../VeloxDev.MAUI.GlobalUsings.g.cs`）⇒ 这个 `RectF` 只能是 `Microsoft.Maui.Graphics.RectF`。
   - 采样器那侧（`Samplers/RectFSampler.cs`）一旦 `using System.Drawing;`，就会解成 `System.Drawing.RectangleF` —— 与键解到的不是同一个 `Type`（本模块的采样器默认都不 import `System.Drawing`）。
   - **后果**：声明为 `Microsoft.Maui.Graphics.RectF` 的属性走注册表解析（精确类型命中，Core `Sampling/Interpolator.cs:51-91`；`RectF` 是 struct，基类链走到 `ValueType`/`object` 就没了，没有别的回退），**第一帧抛 `InvalidCastException`**；`SamplerSet.ApplyCore` 在 `Src/Core/VeloxDev.Core/TransitionSystem/Sampling/SamplerSet.cs:136` 接住，`:139` 报一条 `ErrorStage.Sampling` 诊断，`:140` `CancelQuietly()`，`:141` 返回 —— **整条 run 被取消，不是静默降级**。
   - **不受这条键影响的**：`System.Drawing.RectangleF` 由 **Core** 注册（`Src/Core/VeloxDev.Core/TransitionSystem/Sampling/Interpolator.cs:23` 的 `RectangleFSampler`），根本不经过这条键；`Microsoft.Maui.Graphics.Rect` 走 `:19` 的 `RectSampler`，是对的。
   - **对齐方式**：名字 / 键 / 实现三处都解 `Microsoft.Maui.Graphics.RectF`（改采样器体，不留 `System.Drawing` 的 using）。**不能**走另一条路（把键改成 `typeof(System.Drawing.RectangleF)`）：那会经 `AddOrUpdate` **顶掉 Core 的 `RectangleFSampler`**（安装是 last-writer-wins，Core `Sampling/Interpolator.cs:94-100`）。**同名从来不是问题**（键是 `Type`，`Sampling/Interpolator.cs:40`），同一个 `Type` 才是。
   - **测试为什么抓不到**：`SamplerCoverageTests` 对的是**采样器类型**集合，不是 `(键, 采样器)` 配对。这类断言在 `Examples/Transition/AUTO TEST/Samplers/SamplerKeyTests.cs`：它拿真实注册表问「条目声明的类型解析到谁」，`EveryEntry_ValueTypeResolvesToTheSamplerItNames`（`:54`）比采样器类型、`EveryEntry_SamplerTheRegistryResolves_AcceptsAValueOfThatKey`（`:98`）跑一帧看接不接得住。
   - 与 `memory/modules/TransitionSystem/adapters/maui.md` §四·1 的结论一致，**不冲突**。

2. **`ViewManager.TryFindTemplateByResourceKey` 是死代码**（`ViewManager.cs:472-503`）。`:475` 写死 `var resourceKey = (string?)null;`，`:477` 立刻 `if (resourceKey is null) return false;` ⇒ 后面遍历 `_layout` 祖先与 `Application.Current.Resources` 的查找**永远不可达**。所以 `FindDataTemplate`（`:422`）只有「`TemplateSelector` 选中」这一条活路 —— 与 `ViewPool.EnsureManager` 要求 `GetTemplateSelector(layout) is not null`（`ViewPool.cs:96`）正好印证。⇒ **不要以为放一个 `DataTemplate` 到 `Resources` 里就能被选中**。

3. **`WorkflowMinimapOverlay.RulerBand => 0`**（`:63`），而宿主只把**网格装饰器**的 `RulerBand` 转发给虚拟化 inset（`WorkflowSurfaceBehavior.cs:1688` 的 `SetVirtualizeInset(left: decorator.RulerBand, top: ...)`）。⇒ 小地图自己的标尺带永远不参与 inset；这条与 WPF 同结论（`WorkflowSystem/adapters/wpf.md` 坑 2）。

4. **`#if WINDOWS` 在本家会编出两个不同的程序集**。csproj 的 TFM 是 `net10.0;net10.0-windows10.0.19041.0`（`:8`），**七家里只有本家是「平台中立 + windows」一对**（WinUI 是两个 `-windows`；WPF/WinForms 是 `netframework4.6.1;net5.0-windows;netcoreapp3.0`；Avalonia `netstandard2.0;net6.0`；其余单一 TFM）。所以 **44 处 `#if WINDOWS`**（Surface 13、Minimap 11、LinkOverlay 6、NodeDrag 7、SlotLayout 5、SlotConnection 2）在两个 TFM 下是**两份不同的实现**，而它们在 `PlatformAdapters/` 里是 **0 处**（Transition 轴只有与 TFM 无关的 `#if !NETSTANDARD2_0`）。

   **顺带一条宿主侧的事实**：三个 MAUI demo 的 Windows TFM 是**条件的** —— `Examples/Workflow/MAUI Trimmed/Demo/Demo.csproj` 用 `Condition="$([MSBuild]::IsOSPlatform('windows'))"` 才把 `net10.0-windows10.0.19041.0` 追加进去。⇒ **在非 Windows 机器上编译，这 44 个分支一个都不参与编译**，出了错也看不见。反过来 Windows 上跑起来的那份 exe 路径在 `Examples/Transition/AUTO TEST/Drivers/MauiDemoDriver.cs`。

5. **`Microsoft.Maui.Graphics.RectF` 与 `System.Drawing.RectangleF` 在本模块里共存**，凡是新写采样器都要先问「键是哪一侧」。反面教材就是坑 1；正面做法是 `PointFSampler.cs`（不 import `System.Drawing`），而 `Transition.cs` 里那组 `System.Drawing.*` 重载是**逐字全限定**写的 —— 这不是啰嗦，是唯一能让两类同名类型在一个文件里共存的办法。**绝不要在 `Interpolator.cs` 顶部加 `using System.Drawing;`**：`:13` 的 `PointF` 与 `:18` 的 `SizeF` 会静默改指 `System.Drawing`，变成注册 Core 的类型。

6. **`GlobalUsings.cs` 不传给消费者**。它只有 3 行（`VeloxDev.TransitionSystem`、`...Abstractions`、`VeloxDev.Threading`，七家逐字相同），是编译期的；宿主要用 `Transition<T>` 仍得自己写 `using VeloxDev.TransitionSystem;`。同理它**不含** `VeloxDev.WorkflowSystem` —— 但见 §1.1，本家 Attached 文件大多不需要那一行。

7. **`obj/` 里有陈旧产物，别拿它当事实**。`obj/Debug/net10.0-windows10.0.17763.0/` 与 `obj/Debug/*.nuspec`（6.0.0 / 6.0.4 / 6.0.82 …）都还在；那个 17763 目录里的 `VeloxDev.MAUI.GlobalUsings.g.cs` 只有 8 行、**没有 MAUI 的隐式 using**，是旧工程状态的残留。当前声明过的 TFM 目录（`net10.0`、`net10.0-windows10.0.19041.0`）里的是 26 行。别按 `obj/` 推当前 TFM 或当前 API。

8. **模板选择器对同一个 VM 类型只会被问一次**：`ViewManager` 把结果按 `Type` 缓存（`ViewManager.cs:16` 的 `_templateMap`，读 `:425` 短路，写 `:454`/`:462`），而这个字典**在文件里没有任何一处清空**。它的寿命等于一次挂载周期 —— `ViewPool.CleanupManager`（`:78-87`）在 `Layout.Handler` 变 null 时 `Managers.Remove(layout)`（触发点 `:71-75`），管理器随之丢弃、缓存才跟着没。⇒ selector 里写「按状态换模板」的逻辑在一次挂载里只生效一次；要状态可变就得让 `ItemsSource` 的元素换新实例、或重建宿主。

9. **`ViewManager` 现在会「探测」它没法物化的 item**：`CanMaterialize`（`:420`）拿 `FindDataTemplate` 试一次，选择器抛异常/返回 null 的 item 被跳过而不是让整个批次炸（这就是 2026-10-04「selector-probing」那条提交）。⇒ 选择器「按 item 类型分派、不认识的返回 null」是受支持的写法；但**跳过是静默的**，item 永远不出现也不报错。

---

## 六、`GlobalUsings.cs` 与 `VeloxDev.MAUI.csproj`

**`GlobalUsings.cs`** 见 §五·6。

**csproj 里影响代码本身的条件**（`VeloxDev.MAUI.csproj`）：

| 事实 | 行 | 后果 |
|---|---|---|
| `<TargetFrameworks>net10.0;net10.0-windows10.0.19041.0` | `:8` | 七家里唯一的「中立 + windows」对；Windows TFM 必须 ≥ 10.0.19041.0，**低了会静默退回平台中立程序集**，`#if WINDOWS` 需要的 Windows 专属类型全没有（理由写在 `:4-7` 的注释里） |
| `<MauiVersion>10.0.20` | `:34` | 注释 `:32-33` 记着：`10.0.0` 那版**没有 Windows TFM 资源**，于是 `#if WINDOWS` 的代码从来没被编进库里。改这个值 = 改整个 Windows 分支能不能编译 |
| `<NoWarn>CA1416</NoWarn>` | `:17` | 只压平台兼容性告警一项 |
| `SupportedOSPlatformVersion`/`TargetPlatformMinVersion` 的 `Condition` 覆盖 ios / maccatalyst / android / windows / tizen | `:19-24` | **这些平台标识符有一半不在 `TargetFrameworks` 里**（没有 android/ios TFM），条件不成立即为死条件。`:22`/`:23` 的 windows min-version 是 `10.0.17763.0`，**低于** TFM 的 19041 ⇒ 它会把输出目录名改成 `net10.0-windows10.0.17763.0`（`obj/` 里那几条就是这么来的，见 §五·7）。这是从 MAUI 模板原样带下来的 |
| `<SingleProject>true</SingleProject>` / `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` | `:14` / `:25` | `AllowUnsafeBlocks` 在本模块**无消费者**：31 个 .cs 里 `unsafe` 零命中。模板残留 |
| Debug → `ProjectReference`（`:43`）／非 Debug → `PackageReference`（`:44`） | — | 与生成器那套双轨同形；包里唯一的依赖是 `VeloxDev.Core` 10.0.0 |
| `Microsoft.WindowsAppSDK` 只在 windows TFM 下引用（`:47-52`，现 `1.7.250909003`） | — | 注释写明：app 从 MAUI SDK 拿到它，**类库必须自己显式引用**。这是本家 `#if WINDOWS` 里能写 `Microsoft.UI.Xaml.*` 的前提 |
| `GeneratePackageOnBuild`（`:26`）+ `<Version>10.0.0`（`:28`） | — | **不生成 XML 文档**（全仓只有 Core、Jalium、WPF 三个项目开 `GenerateDocumentationFile`）⇒ 本模块的 XML 注释不进任何 `.xml`，注释纯给人看 |
| `<None Include="..\..\..\Assets\veloxdev-logo.png" Pack="true">`（`:38`） | — | 三级上跳即仓库根的 `Assets/veloxdev-logo.png`（存在） |

---

## 七、入口：我要改 X 先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 画布平移 / 缩放 / 捏合 / Windows 的 Ctrl+滚轮 / 平移到边时的扩张 | `Attached/Workflow/WorkflowSurfaceBehavior.cs`（数学在 Core `WorkflowSurfaceMath`） |
| 可见区、装饰器与小地图的喂值、`Viewport` 写回 | 同上，`UpdateVisibleRegion` `:1615` 与 `ApplyVisibleRegion` `:1637` |
| 连线右键菜单 / 视口恢复 | 同上（`WireLinkMenu` `:219`、`RequestViewportRestore` `:569`） |
| 节点拖拽的落点与坐标宿主 | `Attached/Workflow/WorkflowNodeDragBehavior.cs` |
| 插槽锚点写回、重同步信号源 | `Attached/Workflow/WorkflowSlotLayoutBehavior.cs` |
| 插槽两阶段连接命令 | `Attached/Workflow/WorkflowSlotConnectionBehavior.cs` |
| 视图池、模板查找、分批 | `Attached/Workflow/ViewManager.cs`（挂点 `ViewPool.cs`） |
| 小地图外观与导航 | `Attached/Workflow/WorkflowMinimapOverlay.cs` |
| 链接层绘制、裁剪、光带 | `Attached/Workflow/WorkflowLinkOverlay.cs` |
| 把模型事件接给宿主的 sink | `Attached/Workflow/WorkflowEvents.cs` |
| 某个 MAUI 类型可动画 | `PlatformAdapters/Samplers/` + `PlatformAdapters/Interpolator.cs:10-21` + `Transition.cs:35-242` |
| 线程、优先级、pacer、调度器 | `PlatformAdapters/UIThreadInspector.cs`、`TransitionInterpreter.cs` —— 差异与坑见 `TransitionSystem/adapters/maui.md` |
| 主题字符串 → 值 | `PlatformAdapters/ThemeValueConverters.cs`（**仓库内无消费者**，见 §八） |
| 包结构、TFM、双轨引用、MAUI 版本钉法 | `VeloxDev.MAUI.csproj` |
| **宿主怎么把这些接起来（最小可读样本）** | `Examples/Workflow/MAUI Trimmed/Demo/Controls/Workflow/TreeView.xaml`（附着属性 + 链接层绑定一次看全）；节点/插槽侧看同目录的 `NodeView.xaml`、`SlotView.xaml` |

---

## 八、这份文件没写的东西（以及为什么）

- **七个角色各自要暴露什么成员、`PART_*` 约定、七角色职责表** —— `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3 与 `skills/veloxdev-create-workflow/references/view-layer.md`。
- **本家的平台硬限制、与别家的刻意背离、逐条坑**（16k 纹理上限、托管 `SizeChanged` 是 arrange 途中、原生 extent 异步重测的两条相反写法则、Windows 上把原生 `ScrollViewer` 降级成被动容器…）—— `memory/modules/WorkflowSystem/adapters/maui.md`。
- **TransitionSystem 侧的 12 条注册逐条分析、`IsRepeating` 与 WinUI 的正面对立、`IsAlive` 用窗口数** —— `memory/modules/TransitionSystem/adapters/maui.md`。
- **主题切换的完整流向** —— DynamicTheme 模块。本家这边只有一条可写的事实，写在下面。
- **`ThemeValueConverters.cs` 在本仓库里没有任何消费者。** 文件里是 `VeloxDev.DynamicTheme` 下的 7 个 `IThemeValueConverter` 实现（`DoubleConverter` `:9`、`PointConverter` `:43`、`ThicknessConverter` `:74`、`CornerRadiusConverter` `:109`、`ColorConverter` `:141`、`BrushConverter` `:180`、`ObjectConverter` `:286`，加一个 `ThemeResourceLookup` 辅助 `:339`），但**全仓没有任何 .cs / .xaml 引用它们**，`Examples/Theme/` 下也**没有 MAUI 主题 demo**（只有 Avalonia 与 WPF）。⇒ 改这里**跑不出任何症状**，也别把它当成 Transition/Workflow 契约的一部分（`TransitionSystem/adapters/maui.md` §四·4 同结论）。
- **人面向的「怎么用这套模板搭一个 MAUI 工作流视图」** —— `skills/veloxdev-create-workflow/references/gui/maui.md`（`Docs/` 那套文档站点**不在本仓库**）。
- **本家的验证线在哪**：Transition 轴有自动验收（`Examples/Transition/AUTO TEST/`，含 `Drivers/MauiDemoDriver.cs` 与 `Conformance/MauiConformance.cs`、`Samplers/MauiEntries.cs`）；**Workflow 轴在本仓库没有任何自动验收** —— `Examples/Workflow/` 下不存在 `AUTO TEST` 目录，唯一的执行样本是 `Examples/Workflow/MAUI Trimmed/Demo/` 本身。
