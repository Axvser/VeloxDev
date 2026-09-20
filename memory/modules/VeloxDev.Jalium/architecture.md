# VeloxDev.Jalium — 架构

> 代码：`Src/Adapters/VeloxDev.Jalium/`。**29 个 .cs、3294 行**
> （`Attached/Workflow/` 11 个 2360 行，最大三个是 `WorkflowSurfaceBehavior.cs` 674、`WorkflowSlotLayoutBehavior.cs` 446、`WorkflowMinimapOverlay.cs` 305；
> `PlatformAdapters/` 8 个 300 行，最大是 `Transition.cs` 138；`PlatformAdapters/Samplers/` 9 个 631 行，最大是 `TransformSampler.cs` 276、`BrushSampler.cs` 131；顶层 `GlobalUsings.cs` 3 行）。
> **计数写法**：`git ls-files 'Src/Adapters/VeloxDev.Jalium/*.cs' 'Src/Adapters/VeloxDev.Jalium/**/*.cs'`。
> 只写 `'.../**/*.cs'` 会得到 **28** —— 这条 pathspec 只匹配**子目录里**的 `.cs`，该目录**本级**的 `GlobalUsings.cs` 一个都不算（同理 `PlatformAdapters/**/*.cs` 只有 9 个采样器，`PlatformAdapters/` 本级那 8 个文件要另写 `PlatformAdapters/*.cs`）。
>
> 本文只写「读完这 29 个文件才知道的东西」。类型清单、成员表、继承树请看 IDE。
>
> **本模块没有 `adapters/` 子目录，也不该有**：模块名本身就是一个平台，不存在平台轴。它在两条轴上的平台差异分别落在
> `memory/modules/TransitionSystem/adapters/jalium.md`、`memory/modules/WorkflowSystem/adapters/jalium.md`、`memory/modules/Templates/adapters/jalium.md`，本文**指路不抄**。

---

## 一、一个项目、两条轴（不是三条）

这个项目叫「Jalium 适配器」，但它只实现了 **两个** Core 模块的平台侧契约，彼此**在程序集内互不引用**：

| 轴 | Core 契约 | 本项目落点 | 平台差异记在哪 |
|---|---|---|---|
| TransitionSystem | 宿主 / 解释器 / 调度器 / 帧 pacer / 采样器 | `PlatformAdapters/`（8 个类型 + `Samplers/` 9 个） | `memory/modules/TransitionSystem/adapters/jalium.md` |
| WorkflowSystem | 七个视图角色 | `Attached/Workflow/`（11 文件） | `memory/modules/WorkflowSystem/adapters/jalium.md` |

**第三条轴（DynamicTheme）在这一家是空的，这是它与 WPF / Avalonia / WinUI / MAUI / WinForms / Razor 的六家差异里最容易被忽略的一条**：

- 全模块**没有 `ThemeValueConverters.cs`**（`git ls-files 'Src/Adapters/VeloxDev.Jalium/PlatformAdapters/ThemeValueConverters.cs'` 为空）；`IThemeValueConverter` 在本项目零命中。
- Jalium 也**没有主题 demo**：`Examples/Theme/` 下只有 `Avalonia` / `Avalonia Trimmed` / `WPF` / `WPF Trimmed`（`git ls-files Examples/Theme/`）。
- 所以 `Interpolator.CreateScheduler`（`PlatformAdapters/Interpolator.cs:26-29`）在本仓库**是一条无人的路**：它的唯一调用者是 Core `Src/Core/VeloxDev.Core/DynamicTheme/ThemeManager.cs:219`，而 `ThemeManager.SetPlatformInterpolator` 在本仓库只有 4 个非测试调用点，全是 Avalonia / WPF（`Examples/Theme/Avalonia/Demo/App.axaml.cs:25`、`Examples/Theme/Avalonia Trimmed/Demo/Views/MainWindow.axaml.cs:46`、`Examples/Theme/WPF/Demo/App.xaml.cs:16`、`Examples/Theme/WPF Trimmed/Demo/MainWindow.xaml.cs:47`）。
  ⇒ **别把它当成过渡轴的优先级选择来读**：过渡轴走的是 `Transition<T>` 的编译期类型实参，`CreateScheduler` 只是 DynamicTheme 的工厂接缝（休眠，但留着是对的 —— 它是这份实现唯一能告诉 DynamicTheme「这家的调度器是哪个类型」的地方）。

**两条轴的命名空间都寄生在 Core 上**：`VeloxDev.TransitionSystem`（`PlatformAdapters/*`）与 `VeloxDev.WorkflowSystem.AttachedBehaviors`（`Attached/Workflow/*`）。唯一例外是 9 个采样器，它们在 `VeloxDev.Adapters.NativeSamplers`（`PlatformAdapters/Samplers/PointSampler.cs:3`、`TransformSampler.cs:3`、`BrushSampler.cs:6`）—— 与另外六家同名同命名空间，所以**同时引用两家适配器的工程必然 CS0433**，绕法见 `Examples/Transition/AUTO TEST/Samplers/JaliumEntries.cs`。

**目录名与轴不对齐，别按目录推契约**：`Attached/` 只有 WorkflowSystem 一条轴（名字起得像「所有附着行为」，实际不是）。

---

## 二、`PlatformAdapters/`：注册与工厂

### 2.1 三段静态链，只有第一段需要宿主动手

| 要注册的东西 | 注册点 | 什么时候真的发生 |
|---|---|---|
| 9 个采样器（10 条登记） | `Interpolator` 的**静态构造**（`PlatformAdapters/Interpolator.cs:10-24`） | 第一次构造 `Transition<T>` 时：Core 的字段初始化 `protected TInterpolatorCore interpolator = new();` |
| 采样器所在的宿主 / 解释器 / 优先级 | `TransitionScheduler` 的类型实参（`PlatformAdapters/TransitionScheduler.cs:5-8`） | 同上，全部编译期写死 |
| DynamicTheme 的调度器工厂 | `Interpolator.CreateScheduler`（`PlatformAdapters/Interpolator.cs:26-29`） | 宿主显式调 `SetPlatformInterpolator` —— **本仓库无人调**（§一） |

**没有程序集级入口，也没有 `Initialize()`**：全模块 `ModuleInitializer` / `[assembly:` 零命中，唯一的静态构造就是 `Interpolator.cs:10`。⇒ 只用 `Transition<T>` 的宿主什么都不用做。

### 2.2 十行登记里的两条非常规写法

```csharp
// PlatformAdapters/Interpolator.cs:12-13 的注释：Exact-type lookup — register BOTH Brush and
// SolidColorBrush so brush properties declared as either type animate
RegisterInterpolator(typeof(Brush), new BrushSampler());            // :20
RegisterInterpolator(typeof(SolidColorBrush), new BrushSampler());  // :21 —— 同一个实例，两条登记
```

两条只在读这个文件时才知道的推论：

1. **`BrushSampler` 是唯一被登记两次的采样器**（`:20-21` 共享一个实例）。Core 的解析次序是「精确类型 → 最近基类 → 接口按名」，`SolidColorBrush` 的最近基类本来就是 `Brush`，所以 `:21` 严格来说是**冗余**的 —— 它防的是「宿主把属性声明成 `SolidColorBrush` 且 Core 的基类回溯在某些路径下不生效」，代价为零。**别以为漏登记的族类型会被基类接住而放心不加**：这一家的姿态是所有要动画的类型都显式登记。
2. **`Transform3D` 用了全限定名**（`:23`）—— 因为 `Jalium.UI.Media` 下同时有 `Transform`（2D）与 `Media3D.Transform3D`。这一条与 WPF 的 `Transform` 家族登记是两个不同的形状：WPF 登记 `typeof(Transform)` 一登记就接住整族，Jalium 两个都显式登记（`Transform` + `Transform3D`），**没有登记 `TransformGroup` / `TranslateTransform` 这些子类**。

### 2.3 这家的三个平台类型是它独有的

`TransitionEffect.Priority` 硬编码为 `DispatcherPriority.Render`（`PlatformAdapters/TransitionEffect.cs:7`），`UIThreadInspector.InternalPriority` 是 `Send`（`PlatformAdapters/UIThreadInspector.cs:31`）—— 两个优先级**语义不同、刻意不同**：前者是动画帧写入用的优先级，后者是「让回调插队到最前」用的。改成同一个会静默改变观感（帧晚一档）。
`UIThreadInspector.ThreadFor` 的兜底链是**四级**（`PlatformAdapters/UIThreadInspector.cs:14-18`）：target 自己的 `Dispatcher` → `Application.Current?.Dispatcher` → `Dispatcher.FromThread(CurrentThread)` → `Dispatcher.MainDispatcher`；整块包在 `try` 里，落空退 `ThreadRef.None`（`:21-25`，注释说明了理由），后果只是这个目标**没有 pacer**。`PostCore` 另有两道闸：拿不到 dispatcher 返回 false，`HasShutdownStarted` 也返回 false（`:35-36`）。

`.Property(...)` 的**流式重载集不属于 Core**：Core 的 `TransitionCore<...>` 基类里一个 `Property` 都没有（`Src/Core/VeloxDev.Core/TransitionSystem/Transition.cs` 全文无 `Property` 方法），13 个重载全写在各家适配器里（Jalium 13 个在 `PlatformAdapters/Transition.cs:34-137`；对照 WPF 28、Avalonia 30、WinForms 17）。⇒ **重载表的长度就是这一家「能被 `.Property(...)` 顺滑写出来的平台类型」的数量**，Jalium 的是：`Brush?`、`ICollection<Transform>`、`Transform3D?`、`Point`、`Rect`、`Thickness`、`CornerRadius`、`Size`、`Color`、`int`、`double`、`float`。注意 **`SolidColorBrush` 没有自己的重载**（虽然它被登记了）—— 声明成 `SolidColorBrush` 的属性靠泛型 `Property<TValue>`（`:34`）走。

`Property(Expression<Func<T, Transform?>>, ICollection<Transform>)` 是**唯一带分支的重载**（`Transition.cs:48-66`）：`Count == 1` 时把**单个实例**直接 `SetValue`，否则才包 `TransformGroup`。注释 `:51-53` 写明了理由（包起来会改运行时类型，破坏 `((TranslateTransform)x.RenderTransform).X` 这类嵌套路径）。⇒ **顺手「统一成总是包 TransformGroup」会让依赖具体变换子类类型的宿主静默失效**。

---

## 三、`Attached/Workflow/`：11 个文件、七角色、一套挂载习语

11 个文件里，**7 个是七角色实现，4 个是这一家独有的**：`IWorkflowTemplateSelector.cs`、`ViewPool.cs`、`ViewManager.cs`、`WorkflowTreeView.cs`（原因见 §四）。七角色各自的职责与 `PART_*` 约定在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3，本文不抄。

### 3.1 三种形态、一个统一习语

| 形态 | 文件 |
|---|---|
| `sealed class : DependencyObject`（附着行为） | `WorkflowSurfaceBehavior.cs`、`WorkflowSlotLayoutBehavior.cs`、`WorkflowNodeDragBehavior.cs`、`WorkflowSlotConnectionBehavior.cs` |
| `static class`（无状态挂点） | `WorkflowCanvasTransformBehavior.cs`、`ViewPool.cs` |
| 真控件（宿主写子类或直接 new） | `WorkflowGridDecorator : Decorator, IWorkflowGridDecorator`、`WorkflowMinimapOverlay : FrameworkElement, IWorkflowMinimapOverlay`、`WorkflowTreeView : Grid`、`ViewManager : IDisposable` |

**统一习语**（四个行为逐个相同）：`DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), …, new PropertyMetadata(false, OnIsEnabledChanged))` + 一个**私有附着 `"State"` DP** 存每元素状态；`OnIsEnabledChanged` 一律**先 `Detach` 再 `Attach``。私有 `State` DP 在 `WorkflowSurfaceBehavior.cs:73`、`WorkflowSlotLayoutBehavior.cs`、`WorkflowNodeDragBehavior.cs:38-42`。

**这一家与 XAML 六家最深的一条差异：Jalium 没有标记语言**，所以宿主**无法用 XAML 挂附着属性**，只能按名注册：

1. 每个行为的宿主类型门槛与 XAML 六家相同（`FrameworkElement` / `Control` / `UIElement`），但**没有 `UserControl` 这一档** —— 六家里「要 `FindName` 找部件就必须挂 `UserControl`」的约束在这家变成「**必须自己先 `NameScope.SetNameScope`**」。
2. `WorkflowTreeView` 的构造器是**全模块唯一一处「一次挂齐」的样本**，也是顺序约束的唯一可读证据（`WorkflowTreeView.cs:61-98`）：
   ```
   :61   NameScope.SetNameScope(this, new NameScope());
   :88-91 RegisterName("PART_SurfaceBorder"/"PART_GridDecorator"/"PART_ScrollViewer"/"PART_Canvas", …)
   :93-98 WorkflowSurfaceBehavior.SetIsEnabled(this, true) + 五个 *Name
   ```
   **`RegisterName` 必须在 `SetIsEnabled` 之前**：`Attach` → `ResolveNamedControls` 全靠 `control.FindName(name) as …`（`WorkflowSurfaceBehavior.cs:186-235`），顺序反了全部解析成 `null`，而每个解析失败都只是 `as` 的结果为 null，**不抛、不报**，表现为「行为挂上了但什么都不做」。
3. `FindName` 返回 `null` ⇒ 静默早退是这家的**默认失败模式**。`Refresh` 除了 `IsEnabled` 判断（`:103`）没有任何非空断言。

### 3.2 `WorkflowTreeView` 自己把六个附着属性挂上，唯独漏了 `ZoomEnabled`

`WorkflowTreeView.cs:93-98` 设了 `IsEnabled` + 五个 `*Name`，**没有 `SetZoomEnabled`**。而这正是 `Examples/Workflow/Jalium Trimmed/Demo/MainWindow.cs:60` 那句 `WorkflowSurfaceBehavior.SetZoomEnabled(surface, true)` 打不到的地方 —— 那句的 `surface` 是 demo 自己的 `TreeView : Canvas`，**而它从未调 `SetIsEnabled`**。`HookZoom` 的第一行就是 `if (control.GetValue(StateProperty) is not SurfaceState state) return;`（`WorkflowSurfaceBehavior.cs:281`），`StateProperty` 只在 `Attach` 里被写。⇒ **demo 里那句 `SetZoomEnabled` 是空操作**；demo 真正的缩放是自己在窗口级 `OnPreviewWindowMouseWheel` 里做的（`MainWindow.cs:122-156` 与自己的 `ZoomBy` `:166-230`）。`OnZoomEnabledChanged`（`:259`）本身是对的（直接调 `HookZoom` 或 `UnhookZoom`），它只是改变不了「没有 State」这个前提。

### 3.3 `WorkflowGridDecorator` 的标尺是 `28`，而且默认值是**无注释的**

`WorkflowGridDecorator.cs:16` 的 `new PropertyMetadata(28.0, OnVisualChanged)` —— **没有解释性注释**（对比 WinForms 的 36 是带注释的刻意背离）。并且这一家**适配器里就是 28**，36 只活在模板产物里（`Src/Templates/VeloxDev.Jalium.Templates/working/content/workflow-grid-decorator/TemplateClass.cs:17` 的 `RulerThickness = 36`，被 `…/workflow-link-view/TemplateClass.cs:28-30` 复制成 `RulerReserve = 36` 并被 `…/workflow-node-view` 硬编码 `+ 36`）。⇒ **两套表面量不同**：走 `WorkflowTreeView` 时标尺带是 28（`RulerBand => RulerThickness`，`:46`），走模板 `TreeView : Canvas` 时是 36。§四的对照表里这一条最容易被当成同一个数。

`ArrangeOverride`（`:68-80`）**刻意不给子元素留 inset** —— 标尺是画在子元素之上的覆盖带，不是布局内缩。⇒ 想「让内容避开标尺」不能改 `ArrangeOverride`，要改 `WorkflowSurfaceBehavior` 喂给 `SetVirtualizeInset` 的那个值（`:614`）。

---

## 四、这一家的结构性根因：**两套表面并存，而适配器自己那套几乎没人用**

**这是 Jalium 与另外六家最本质的差异，也是这份文件最该记住的一条。**

六家的模式是「适配器出零件，demo/template 用 XAML 把零件拼成表面」。Jalium 没有 XAML，于是它**同时给出了两个答案**：

| | 适配器自带：`WorkflowTreeView : Grid` | demo/template：`TreeView : Canvas` |
|---|---|---|
| 在哪 | `Attached/Workflow/WorkflowTreeView.cs`（226 行，`public`，可继承） | `Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/TreeView.cs`（模板 `workflow-tree-view` 的同名产物） |
| 装配 | **自装配**：ctor 里建 `Border`→`WorkflowGridDecorator`→`ScrollViewer`→`Canvas`，`RegisterName` ×4，六个附着属性一次挂齐（`:61-98`） | **手装配**：宿主必须按 8 步接线（`MainWindow.cs:37-60`），自己管 `AttachScrollViewer`/`SetTree`/`NotifyZoomCommitted`/`Changed`/`OriginX` |
| 公开面 | `PART_SurfaceBorder`/`PART_ScrollViewer`/`PART_Canvas`/`PART_GridDecorator`/`PART_MinimapOverlay` 是 **public 属性** + 已 `RegisterName`；`ViewModel`（= `DataContext`）、`TemplateSelector`（**普通自动属性**）、`GridDecorator`、`MinimapOverlay` | 自己的 `SetTree`/`AttachScrollViewer`/`NotifyZoomCommitted(double,double)`/`Changed` 事件 |
| 网格/标尺 | 用适配器的 `WorkflowGridDecorator` 实例（默认 28） | 自绘（36，`OriginX/OriginY` 从 `GridDecorator.RulerThickness` 取） |
| 缩放 | 由 `WorkflowSurfaceBehavior.ZoomBy`（`:344-402`）驱动，靠 `SetZoomEnabled` 打开 | 自己接窗口级 wheel + 自己的 `ZoomBy` + `_zoomPin`/`NotifyZoomCommitted` 深缩放守卫 |
| 画布尺寸 | `Width/Height = Layout.ActualSize.*`，**没有 `Math.Max` 下界**（`:222-223`） | `Math.Max(2000, Layout.ActualSize)`（模板 `TreeView.cs:145-151`） |
| 虚拟化 inset | 由 `WorkflowSurfaceBehavior.UpdateGridDecorator` 喂 `decorator.RulerBand`（`:614`） | 自己喂 `GridDecorator.RulerThickness`（模板 `TreeView.cs:203`） |

**三条推论：**

1. **`WorkflowTreeView` 是一条未被走通的成品路**。它自洽、能编译、装配最少，但**仓库里零消费者**（§五）。它的两个已知缺口是「没有 2000 下界」和「没有深缩放守卫」—— 后者不是遗漏：`_zoomPin`/`NotifyZoomCommitted` 这套在 `Src/Adapters/VeloxDev.Jalium/` 下**零命中**（`git grep -n "_zoomPin\|NotifyZoomCommitted" -- Src/Adapters/VeloxDev.Jalium` 无输出），模板产物与 demo 有，**反而 WinUI 的适配器有**（`Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:350-399`，注释明写 "mirrors Jalium TreeView.NotifyZoomCommitted"）。⇒ 这个守卫**只长在「自己管虚拟化的表面」那一侧**，`WorkflowSurfaceBehavior` 因为不自己调虚拟化（`ZoomBy` 里没有 `Virtualize` 调用）所以不需要它 —— 代价是缩放后要等 helper 的 ~10 fps 脏计时器才重算可见集。
2. **`GridDecorator` / `MinimapOverlay` 这两个 setter 是「一次性」的**，都只接受 `FrameworkElement`（`WorkflowTreeView.cs:33-54` 的 `if (value is FrameworkElement fe)`），而模板产物 `workflow-grid-decorator` 是个 **`static class`**，**给不了实例** —— 那个条目只对模板那套表面有用（详见 `memory/modules/Templates/adapters/jalium.md` §一·2）。
3. **`SwapGridDecorator` 必须重注册名字**（`WorkflowTreeView.cs:103-120`）：先把旧 decorator 的 `Child` 摘掉再把 `ScrollViewer` 挂到新 decorator 上，最后 `UnregisterName("PART_GridDecorator")` + `RegisterName("PART_GridDecorator", decorator)`（`:118-119`）。**漏了这两行 ⇒ `FindName` 仍返回旧实例，`ResolveNamedControls` 把 inset 喂给一个已经不在树上的元素**，且不报错。

---

## 五、仓库里到底被消费了什么（实测）

引用 `VeloxDev.Jalium` **项目**的工程只有 4 个（`git grep -ln 'ProjectReference Include=".*VeloxDev.Jalium/VeloxDev.Jalium.csproj"'`）：`Examples/Transition/Jalium/Demo/`（`:10`）、`Examples/Transition/AUTO TEST/Samplers/VeloxDev.SamplerTest.csproj`（`:28`）、`Examples/Workflow/Jalium/Demo/`（`:35`）、`Examples/Workflow/Jalium Trimmed/Demo/`（`:56`）。逐个核它们真正用到的类型：

| 工程 | 用到的适配器类型 |
|---|---|
| `Examples/Transition/Jalium/Demo/` | 只用到 `PlatformAdapters/` 的 `TransitionEffect` 与 `Transition<T>`（`MainWindow.cs:419` 一族、`:13` 的类注释「Standalone animation test for the VeloxDev.Jalium PlatformAdapters」）；**`Interpolator` / `TransitionScheduler` / `UIThreadInspector` 名字零命中** —— 它们只在 `Transition<T>` 的类型实参里被间接使用 |
| `Examples/Transition/AUTO TEST/Samplers/` | 只走**反射**（`JaliumEntries.cs`、`SamplerCoverageTests.cs:30` 的 `ExpectedAdapterAssemblies`） |
| `Examples/Workflow/Jalium Trimmed/Demo/` | `ViewPool`（`Views/Workflow/TreeView.cs:114-115`）、`IWorkflowTemplateSelector`（`TemplateSelector.cs:14`）、`WorkflowMinimapOverlay`（作为基类，`MinimapOverlay.cs:9`）、`WorkflowSurfaceBehavior.SetZoomEnabled`（`MainWindow.cs:60`，**空操作**，§3.2） |
| `Examples/Workflow/Jalium/Demo/` | 只有 `WorkflowMinimapOverlay`（作为基类，`Views/Workflow/Minimap.cs:8`） |

**哪些类型在全仓库零消费者**（按类型名在 `Examples/` 下 grep）：`WorkflowTreeView`（`MainWindow.cs:12` 那条 `using WorkflowTreeView = Demo.Views.Workflow.TreeView;` 是**别名指向 demo 自己的类**，不是适配器那个 —— 这是最容易读反的一处）、`WorkflowSurfaceBehavior`（除 `SetZoomEnabled`）、`WorkflowCanvasTransformBehavior`、`WorkflowGridDecorator`、`WorkflowNodeDragBehavior`、`WorkflowSlotConnectionBehavior`、`WorkflowSlotLayoutBehavior`、`ViewManager`。同样零调用者的是 `ViewPool.UpdateRenderTransforms`（`ViewPool.cs:39-46`，`internal static`）与 `WorkflowCanvasTransformBehavior.Apply`（`WorkflowCanvasTransformBehavior.cs:23-24`）。

⇒ **这份适配器的「已用面积」是：`PlatformAdapters/` 的过渡轴（真被跑）、`ViewPool` + `IWorkflowTemplateSelector` + `WorkflowMinimapOverlay`（真被跑）、其余七个角色类（编译得过、测试不到）。** 把它们当「有 bug 的活代码」改之前，先确认你改的那条路上真的有调用者。
（哪一条是刻意留的「可选成品」、哪一条是没跟上的死路，**树里判不出来 —— 存疑**，别按注释的语气猜。）

---

## 六、`GlobalUsings.cs` 与 `VeloxDev.Jalium.csproj`

**`GlobalUsings.cs` 三行，七个适配器逐字相同**（`global using` `VeloxDev.TransitionSystem` / `VeloxDev.TransitionSystem.Abstractions` / `VeloxDev.Threading`）：

- 这就是为什么 `PlatformAdapters/` 的文件除 `Jalium.UI*` 之外几乎不写 `using`，却能直接用 `InterpolatorCore`、`ISampler`（定义在 `...Abstractions`）。
- **它不含 `VeloxDev.WorkflowSystem`** ⇒ `Attached/Workflow/` 里需要 Core 契约的文件各自写 `using VeloxDev.WorkflowSystem;`（`WorkflowTreeView.cs:6`）。
- **`global using` 是编译期的，不随引用传给消费者**：宿主要用 `Transition<T>` 仍得自己写 `using VeloxDev.TransitionSystem;`（`Examples/Transition/Jalium/Demo/MainWindow.cs:8`）。

**csproj 里影响代码本身的条件**（`VeloxDev.Jalium.csproj`）：

| 事实 | 行 | 后果 |
|---|---|---|
| `<TargetFramework>net10.0</TargetFramework>`（**单 TFM、不带平台后缀**） | `:7` | 七家里只有这家与 Razor 是单 TFM 且不带 `-windows`（Razor 是 `net6.0`）。`:4-6` 的注释把理由写明了：适配器只用跨平台核心（Controls/Media/Interop/Core），**不用 `net10.0-windows` 的 `Jalium.UI.Desktop` 入口包**，所以能同时服务 Windows / Linux / Android |
| 唯一的 Jalium 引用是 `Jalium.UI.Controls 26.10.8` | `:32` | `:29-31` 的注释：这是**能提供 `Canvas`/`ScrollViewer`/`Border`/`Control` + `DrawingContext`/`Geometry`/`FormattedText` 的**最低**平台中立包。**别名包 `Jalium.UI.Desktop` 由消费 demo 自己引**（`Examples/Workflow/Jalium Trimmed/Demo/Demo.csproj`）⇒ 适配器与 demo 的包版本可以不同步 |
| Debug → `ProjectReference`（`:27`）／非 Debug → `PackageReference VeloxDev.Core 9.0.0`（`:28`） | — | 与另外六家同形的双轨；两条同时生效会报重复成员 |
| `NoWarn` 写成**一条分号列表** `1573;1591;8605;8604` | `:12` | 七家里只有这家是这个集合：多出的 `8605`/`8604`（可空引用协变/逆变）是 `Attached/Workflow/WorkflowSlotLayoutBehavior.cs` 的递归下钻（`FindDescendantWithSlotDataContext`）真的会触发的 |

---

## 七、陷阱（带依据）

1. **`ViewPool` 的 `Unloaded` 一次性退订会把画布永久留在空白态。** `panel.Unloaded += (_, _) => manager.Dispose();` 只在 manager **首次创建**时订阅一次（`ViewPool.cs:60-65`），而 `Dispose()` → `Detach()` → `ClearAll()` 会把所有视图 `Collapsed` + `DataContext = null`（`ViewManager.cs:52-61`、`:186-204`），`_collection` 也置空。**表面移出树再放回去不会重新触发 `OnChanged`**（那只在附着属性变化时跑），于是没有任何东西重建视图 ⇒ 空白画布，无异常。demo 从不把表面移出树，所以看不出来。
2. **重设 `ItemsSource` 或 `TemplateSelector` 是「全拆重建」而不是增量对齐。** 两个 DP 共用同一个 `OnChanged`（`ViewPool.cs:19`/`:25`），而 `Attach` 第一行就是 `Detach()`（`ViewManager.cs:35`）⇒ 只要改其中一个，所有池内视图都会 `Collapsed` 后重挂，节点的入场动画/局部状态全部重来。想「只换选择器」做不到。
3. **`WorkflowTreeView.TemplateSelector` 是普通自动属性，设晚了完全无效。** `WorkflowTreeView.cs:23` 是 `public IWorkflowTemplateSelector? TemplateSelector { get; set; }`（**不是 DP、没有回调**），唯一转发点是 `OnDataContextChanged` 里的条件赋值（`:150-153`）。⇒ 先给 `ViewModel` 再给 `TemplateSelector` ⇒ 选择器永远不会被送进 `ViewPool`，而 `ViewManager.AddItem` 在 `_selector is null` 时**静默返回**（`ViewManager.cs:131`）⇒ 画布上什么都没有。**正确次序：先 `TemplateSelector` 再 `ViewModel`。**
4. **`ViewPool` 的两个 DP 必须同时非空才建 manager**（`ViewPool.cs:58-69`）；只给一个（哪怕先给了 `ItemsSource`）走的是 `else` 分支的 `existing.Detach()`（`:70-73`）。所以「按 `VisibleItems` 先绑上、稍后补选择器」这种写法在中间态是**什么都没有**，不是「有框没内容」。
5. **`ViewManager` 的池按 `item.GetType()` 键**（`ViewManager.cs:15`/`:136`/`:175`/`:193`），而同一个 item 被 `ReferenceEquals` 去重（`:131`）⇒ 同一集合里放两个引用相同的 item 只会得到一个视图。**`RemoveItem` 把 `DataContext` 置 `null` 后入池**（`:172-173`），复用时靠 `ApplyContext` 重新赋（`:148`）—— 视图若在字段里缓存了「我这个 item 是谁」而不监听 `DataContextChanged`，就会拿着旧模型继续画。
6. **`WorkflowTreeView` 的画布尺寸没有下界。** `UpdateCanvasSize` 直接写 `PART_Canvas.Width/Height = _tree.Layout.ActualSize.*`（`:222-223`，随后 `InvalidateMeasure()`）。`ActualSize` 很小时滚动内容就缩到很小 —— 模板那套的 `Math.Max(2000, …)` 下界在这条路上**没有**。
7. **`WorkflowSlotLayoutBehavior` 的类注释与代码不符。** 类注释（`:12-14`）说它「相对坐标宿主测量并减去 `Layout.ActualOffset`」，而 `SyncSlot` 用的是 `Canvas.GetLeft/GetTop(control) + ActualWidth/Height / 2` 配 `WorkflowSurfaceMath.SlotAnchorFromNode(...)`（`:357-358`，同处 `:350-352` 的注释明写「Pure model math — no TranslatePoint / render-transform / canvas dependence」）。**以代码为准**；`SyncSlot` 的两个形参 `host`/`coordinateHost` 在函数体里根本没被读，`GetActualOffset`（`:407-415`）全仓零调用者。
8. **`WorkflowSurfaceBehavior.ZoomBy` 尾部留了一句 `Debug.WriteLine`**（`:401`，`System.Diagnostics.Debug.WriteLine($"[WorkflowSurfaceBehavior] zoom wheel -> Scale {next}")`）。Debug 构建下每次缩放都打一行 —— 排查缩放问题时它是好用的探针，交付前记得它还在。
9. **`IsSurfaceBlankInteraction` 的祖先链判定里没有 `IsWorkflowLinkVisual` 这一步**（`WorkflowSurfaceBehavior.cs:634-660`，末条是 `ancestors.Any(x => x == state.Canvas || x == state.ScrollViewer || x == state.PointerPressSource || x == state.GridDecorator)`，前面的节点/插槽判定是 `:636`/`:642` 的 `IsWorkflowNodeOrSlotVisual`）⇒ 落在**连线上**的按下会被判成「空白交互」，从而触发画布平移/取消选择。是否刻意**树里判不出来 —— 存疑**。
10. **`WorkflowCanvasTransformBehavior` 的三句注释在本仓库全是反的。** 类文档说「WorkflowSurfaceBehavior sets this property directly; node/link views read it (via the ViewManager)」（`WorkflowCanvasTransformBehavior.cs:6-10`），`OnTransformChanged` 的空体也说「The ViewManager mirrors it onto active node/link views' RenderTransform」（`:26-30`）。实测：①这个类型名在整个 Jalium 子树里**零引用**（`git grep -n "WorkflowCanvasTransformBehavior" -- Src/Adapters/VeloxDev.Jalium` 无输出；仓库里其余命中全是 Avalonia / WPF 各自的同名类型与它们的 XAML 绑定），`Apply`（`:23-24`）**没有任何调用者**；②所谓镜像方 `ViewPool.UpdateRenderTransforms`（`ViewPool.cs:39-46`）是 `internal static` 且同样的零调用者。⇒ **这条通道两头都没接上**，别把「给它补逻辑」当成修 bug；也别拿这份注释去推 WPF / Avalonia 的同名行为。
11. **`WorkflowMinimapOverlay` 单次点击即导航。** `OnMiniMouseDown` 先置 `_dragging = true` 就调 `PanToMini`（`:244-253`）⇒ 单击 = 居中到该点，不是「先选中」。`NavigateToWorld` 需要 `_tree` 与 `ScrollViewer` **同时非空**（`:224-240`），而 `ScrollViewer` 是普通属性（`:55`），只有 `WorkflowTreeView.AddMinimap` 会在 `overlay is WorkflowMinimapOverlay builtin` 时替你赋（`WorkflowTreeView.cs:131-134`）⇒ 自己 new 出来的小地图不赋就是「只看不动」。

---

## 八、入口：我要改 X 先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 画布平移 / 缩放 / Ctrl+滚轮 / 空白判定 / 命名部件解析 | `Attached/Workflow/WorkflowSurfaceBehavior.cs`（数学在 Core `WorkflowSurfaceMath`） |
| 「宿主怎么一次挂齐」（这一家唯一可读的装配样本） | `Attached/Workflow/WorkflowTreeView.cs:61-98` |
| 节点拖拽的落点与坐标宿主 | `Attached/Workflow/WorkflowNodeDragBehavior.cs` |
| 插槽锚点写回、刷新时机 | `Attached/Workflow/WorkflowSlotLayoutBehavior.cs`（注意 §七·7） |
| 插槽两阶段连接命令 | `Attached/Workflow/WorkflowSlotConnectionBehavior.cs`（全模块最短的习语样本，58 行） |
| 视图池、每元素视图的创建/复用/回收 | `Attached/Workflow/ViewManager.cs`（挂点 `ViewPool.cs`） |
| 「item 类型 → 视图」的工厂契约 | `Attached/Workflow/IWorkflowTemplateSelector.cs`（11 行，替换 `DataTemplateSelector`） |
| 网格与标尺的绘制、标尺带宽度 | `Attached/Workflow/WorkflowGridDecorator.cs` |
| 小地图外观与导航 | `Attached/Workflow/WorkflowMinimapOverlay.cs` |
| 线程、优先级、pacer、调度器 | `PlatformAdapters/` 那 5 个短文件 + `PlatformAdapters/UIThreadInspector.cs` |
| 让某个 Jalium 类型可动画 | `PlatformAdapters/Samplers/` + `PlatformAdapters/Interpolator.cs:14-23` 的登记表 |
| 流式 `.Property(...)` 支持哪些类型 | `PlatformAdapters/Transition.cs:34-137`（13 个重载） |
| 包结构、TFM、双轨引用 | `VeloxDev.Jalium.csproj` |
| **宿主怎么把适配器那套接起来** | `Examples/Workflow/Jalium/Demo/Views/Workflow/NodeEditorSurface.cs` 与 `Examples/Workflow/Jalium Trimmed/Demo/MainWindow.cs`（8 步装配在 `:37-60`）；另一条路见 `…/Jalium Trimmed/Demo/Views/Workflow/TreeView.cs` |

---

## 九、这份文件没写的东西

- 七个角色各自要暴露什么成员、`PART_*` 命名约定、七角色职责表 —— `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3。
- 这一家的平台硬限制与刻意背离（`Visual.ShouldRenderChild` 按 `RenderSize` 盒裁剪 ⇒ 自盒化；`IsVirtual` 跳过 + `PortCenter` 反查；纯模型数学；`_zoomPin` 的来龙去脉；`RulerBand` 的消费者）—— `memory/modules/WorkflowSystem/adapters/jalium.md`。
- 过渡轴侧采样器逐一分析、`TransformSampler` 的端点短路与草稿实例类型守卫、`BrushSampler` 为什么只能混一个代表色 —— `memory/modules/TransitionSystem/adapters/jalium.md`。
- 模板侧：七个条目产出什么形状、`static class` 产物、12 个空转符号、标尺 36 被复制的三处、`RulerReserve` —— `memory/modules/Templates/adapters/jalium.md`；包结构与跨平台族划分在 `memory/modules/Templates/architecture.md`。
- 主题切换的完整流向 —— **这一家没有这条线**（§一），见 `memory/modules/DynamicTheme/architecture.md`。
- 人面向的「怎么用这套模板搭一个 Jalium 工作流视图」—— `skills/veloxdev-create-workflow/references/gui/jalium.md`。
