# VeloxDev.Jalium — 架构

> 本适配器是标记驱动（`.jalxaml`），与 WPF 同形。角色机制与平台差异的落点以
> [WorkflowSystem/adapters/jalium.md](../../WorkflowSystem/adapters/jalium.md) §一 与 §〇 为准。

> 代码：`Src/Adapters/VeloxDev.Jalium/`（`WorkflowSystem/AttachedBehaviors/` 是附着行为，视图池的层在 `ViewHosting/`；
> `TransitionSystem/` 有平台类型与 `Samplers/`；顶层 `GlobalUsings.cs`）。
> `git ls-files` 的 glob `'.../**/*.cs'` 只匹配**子目录**里的文件（七家都漏掉本级 `GlobalUsings.cs`）。
>
> **本模块没有 `adapters/` 子目录**（模块名即平台）。两条轴的平台差异分别落在
> `memory/modules/TransitionSystem/adapters/jalium.md`、`memory/modules/WorkflowSystem/adapters/jalium.md`、`memory/modules/Templates/adapters/jalium.md`。

---

## 一、一个项目、两条轴（不是三条）

这个项目叫「Jalium 适配器」，但它只实现了 **两个** Core 模块的平台侧契约，彼此**在程序集内互不引用**：

| 轴 | Core 契约 | 本项目落点 | 平台差异记在哪 |
|---|---|---|---|
| TransitionSystem | 宿主 / 解释器 / 调度器 / 帧 pacer / 采样器 | `TransitionSystem/`（平台类型 + 一组采样器） | `memory/modules/TransitionSystem/adapters/jalium.md` |
| WorkflowSystem | 七个视图角色 | `WorkflowSystem/AttachedBehaviors/`（一组文件：表面/插槽/拖拽/连线手势/模型事件五个附着行为 + 连线自盒化助手 + 小地图 + 视图池挂点；池的各层在 `ViewHosting/`；见 §三） | `memory/modules/WorkflowSystem/adapters/jalium.md` |

**第三条轴（DynamicTheme）在这一家是空的，与其余六家不同**：

- 全模块**没有 `ThemeValueConverters.cs`**；`IThemeValueConverter` 在本项目零命中。
- Jalium 也**没有主题 demo**：`Examples/Theme/` 下只有 `Avalonia` / `Avalonia Trimmed` / `WPF` / `WPF Trimmed`。
- `Interpolator.CreateScheduler` 在本仓库无人调用：调用者只有 Core `Src/Core/VeloxDev.Core/DynamicTheme/ThemeManager.cs`，而 `ThemeManager.SetPlatformInterpolator` 只在 Avalonia / WPF 接线。
  它是 DynamicTheme 的工厂接缝，与过渡轴的优先级无关（过渡轴走 `Transition<T>` 的编译期类型实参）。

**两条轴的命名空间都寄生在 Core 上**：`VeloxDev.TransitionSystem`（`TransitionSystem/*`）、`VeloxDev.WorkflowSystem.AttachedBehaviors`（`WorkflowSystem/AttachedBehaviors/*` 的附着行为）与 `VeloxDev.WorkflowSystem.ViewHosting`（`WorkflowSystem/ViewHosting/*` 的池层）。唯一例外是采样器，它们在 `VeloxDev.Adapters.NativeSamplers`（`TransitionSystem/Samplers/` 的 `namespace` 声明）—— 与另外六家同名同命名空间，所以**同时引用两家适配器的工程必然 CS0433**。

**目录名与轴不对齐**：`AttachedBehaviors/` 只有 WorkflowSystem 一条轴。

---

## 二、`TransitionSystem/`：注册与工厂

### 2.1 两段静态链，都不需要宿主动手

| 要注册的东西 | 注册点 | 什么时候真的发生 |
|---|---|---|
| 全部采样器（含一条重复登记） | `Interpolator` 的**静态构造**（`TransitionSystem/Interpolator.cs`） | 第一次构造 `Transition<T>` 时：Core 的字段初始化 `protected TInterpolatorCore interpolator = new();` |
| 采样器所在的宿主 / 解释器 / 优先级 | `TransitionScheduler` 的类型实参（`TransitionSystem/TransitionScheduler.cs`） | 同上，全部编译期写死 |
| DynamicTheme 的调度器工厂 | `Interpolator.CreateScheduler`（`Interpolator.cs`） | 宿主显式调 `SetPlatformInterpolator` —— **本仓库无人调**（§一） |

**没有程序集级入口，也没有 `Initialize()`**：全模块 `ModuleInitializer` / `[assembly:` 零命中，唯一的静态构造就是 `Interpolator` 的静态构造。⇒ 只用 `Transition<T>` 的宿主什么都不用做。

### 2.2 登记里的两条非常规写法

```csharp
// Interpolator 静态构造的注释：按精确类型查找 —— Brush 与 SolidColorBrush 都要注册
RegisterInterpolator(typeof(Brush), new BrushSampler());
RegisterInterpolator(typeof(SolidColorBrush), new BrushSampler());  // 同一个实例，两条登记
```

1. **`BrushSampler` 是唯一被登记两次的采样器**（两条 `RegisterInterpolator` 共享一个实例）。Core 的解析次序是「精确类型 → 最近基类 → 接口按名」，`SolidColorBrush` 的最近基类就是 `Brush`，所以第二条是**冗余**的 —— 它防的是「宿主把属性声明成 `SolidColorBrush` 且基类回溯在某些路径下不生效」，代价为零。这一家的姿态是所有要动画的类型都显式登记。
2. **`Transform3D` 用了全限定名**（`Interpolator` 静态构造里的 `Jalium.UI.Media.Media3D.Transform3D`）—— 因为 `Jalium.UI.Media` 下同时有 `Transform`（2D）与 `Media3D.Transform3D`。这一条与 WPF 的 `Transform` 家族登记是两个不同的形状：WPF 登记 `typeof(Transform)` 一登记就接住整族，Jalium 两个都显式登记（`Transform` + `Transform3D`），**没有登记 `TransformGroup` / `TranslateTransform` 这些子类**。

### 2.3 这家的平台类型是它独有的

`TransitionEffect.Priority` 硬编码为 `DispatcherPriority.Render`，`UIThreadInspector.InternalPriority` 是 `Send` —— 两个优先级**语义不同、刻意不同**：前者是动画帧写入用的优先级，后者是「让回调插队到最前」用的。改成同一个会静默改变观感（帧晚一档）。
`UIThreadInspector.ThreadFor` 的兜底链是**四级**：target 自己的 `Dispatcher` → `Application.Current?.Dispatcher` → `Dispatcher.FromThread(CurrentThread)` → `Dispatcher.MainDispatcher`；整块包在 `try` 里，落空退 `ThreadRef.None`，后果只是这个目标**没有 pacer**。`PostCore` 另有两道闸：拿不到 dispatcher 返回 false，`HasShutdownStarted` 也返回 false。

`.Property(...)` 的**流式重载集不属于 Core**：Core 的 `TransitionCore<...>` 基类里一个 `Property` 都没有，重载全写在各家适配器里（Jalium 的在 `TransitionSystem/Transition.cs`；各家数量不等）。⇒ **重载表就是这一家「能被 `.Property(...)` 顺滑写出来的平台类型」的清单**。注意 **`SolidColorBrush` 没有自己的重载**（虽然它被登记了）—— 声明成 `SolidColorBrush` 的属性靠泛型 `Property<TValue>` 走。

`Property(Expression<Func<T, Transform?>>, ICollection<Transform>)` 是**唯一带分支的重载**（`Transition.cs` 的 `Transform` 重载）：`Count == 1` 时把**单个实例**直接 `SetValue`，否则才包 `TransformGroup`。该重载里的注释写明了理由。⇒ **顺手「统一成总是包 TransformGroup」会让依赖具体变换子类类型的宿主静默失效**。

---

## 三、`WorkflowSystem/AttachedBehaviors/`：附着行为 + 自盒化助手 + 池 + 小地图

表面是**挂在模板根上的附着行为**（不是可继承的 `Canvas` 基类），与 WPF 逐项对齐；
节点/连线的样子由模板的 `.jalxaml` 决定。文件构成：

| 文件 | 角色 | 附着属性 |
|---|---|---|
| `WorkflowSurfaceBehavior.cs` | **表面**（`static`）：按名字认领模板声明的部件、喂视口、平移、Ctrl+滚轮缩放、把指针与按键翻译进 Core 的输入路由、弹连线菜单、发布画布变换 | `IsEnabled` / `ScrollViewerName` / `CanvasName` / `GridDecoratorName` / `PointerPressSourceName` / `MinimapOverlayName` / `ZoomEnabled` / `CanvasTransform` / `LinkMenuKey` |
| `WorkflowSlotLayoutBehavior.cs` | **槽锚点的唯一写回点**：按 `SlotNames` / `SlotEnumeratorNames` 找控件、量中心、写 `slot.Anchor`（`SlotAnchorFromVisualCenter`） | `IsEnabled` / `SlotNames` / `SlotEnumeratorNames` / `CoordinateHostName` / `CoordinateHostType` / `State` |
| `WorkflowMinimapOverlay.cs` | 小地图，`FrameworkElement, IWorkflowMinimapOverlay`；一组 DP（尺寸、调色板、视口），`ScrollViewerName` 由模板给 | — |
| `ViewHosting/ViewFactory.cs` | 模板分派：选择器 → 沿视觉树上溯 `Resources`（按 `DataType`）→ `Application.Resources`，按类型缓存；`DataTemplate.LoadContent()` | — |
| `ViewHosting/ViewContainer.cs` | 视觉树成员与绑定态：`Attach` 幂等入 `Children`，`Deactivate` 隐藏 + `DataContext = null`，`Destroy` 才摘树 | — |
| `ViewHosting/ViewArranger.cs` | 空实现：几何由节点/连线模板绑定，连线盒由 `WorkflowLinkBounds` 自盒化 | — |
| `ViewHosting/ViewScheduler.cs` | 宿主 `Dispatcher.BeginInvoke(DispatcherPriority.Background)` 排批 | — |
| `ViewHosting/ViewDiagnostics.cs` | 失败写 `Debug.WriteLine`，同批其余项继续 | — |
| `ViewHosting/ViewLayerPolicy.cs` | `GetZOrder` 恒 0（模板绑 z） | — |
| `ViewHosting/ViewLinkLayer.cs` | `IsActive=false`：连线仍由主池一视图一条 | — |
| `ViewHosting/ViewMeasureHost.cs` | 空实现：布局 pass 自己量 | — |
| `ViewHosting/ViewSourcePipeline.cs` | Core `ViewSourcePipelineCore<...>` 的 Jalium 闭合；默认 `AdaptiveViewPoolPolicy` | — |
| `WorkflowNodeDragBehavior.cs` | 标题栏拖拽 → `node.MoveCommand` | 若干附着属性 |
| `WorkflowSlotConnectionBehavior.cs` | 端口手势：按下送出、松开接收，**并收拾失败**（松回自己、落空 ⇒ 收回虚拟连线） | `IsEnabled` |
| `WorkflowEvents.cs` | 模型事件转给宿主 | `Node` / `Slot` / `Tree` |
| `WorkflowLinkBounds.cs` | **自盒化助手**（`static`）：把连线元素的布局盒挪到曲线包围盒上（`BoxPad = 6`）—— 渲染器按 `RenderSize` 盒裁剪，画到盒外会被**静默丢弃** | — |
| `ViewPool.cs` | 池的挂点：`Panel` 上的 `ItemsSource` / `TemplateSelector`；任一变化都重建 `ViewSourcePipeline`，`Unloaded` 时释放 | `ItemsSource` / `TemplateSelector` |

> `WorkflowSystem/AttachedBehaviors/` 的附着行为在命名空间 `VeloxDev.WorkflowSystem.AttachedBehaviors`，`ViewHosting/` 的池层在 `VeloxDev.WorkflowSystem.ViewHosting`（两者都与六家同名同形）。

### 3.1 表面与「七个角色」的边界

`WorkflowSurfaceBehavior` 是**唯一持有状态**的角色：它按名字认领模板声明的部件，把可见集喂给 `ViewPool`，
另有一整套交互与记账：

- 部件解析（`FindName` 取 `PART_ScrollViewer` / `PART_Canvas` / `PART_GridDecorator` / `PART_SurfaceBorder` / `PART_MinimapOverlay`）；
- 视口记账 `UpdateViewport`（未测量时退回整张画布；标尺厚度喂给 `SetVirtualizeInset`）；
- 缩放钉 `ZoomPin` + `ZoomPinLifetimeMs = 250` —— **这套深缩放守卫在适配器里，由行为内部提交，宿主不参与**；
- 平移挂在**具名按下源的预览相**上（挂宿主冒泡相收不到：滚动视口那层会把 `MouseDown` 标成已处理）；
- 缩放挂 `ScrollViewer.PreviewMouseWheel` + Ctrl 门控；
- 指针与按键翻译进 `WorkflowInput.For(tree).Route(...)`，右键菜单从输入路由里弹。

其余角色都只伺候这一个表面：节点/连线视图由 `ViewPool` 建出的 `ViewSourcePipeline` 池化到画布，网格装饰器**由模板提供**
（适配器只按 `IWorkflowGridDecorator` 收），选择器是模板的 `DataTemplateSelector`。
**改「画布怎么动」先打开 `WorkflowSurfaceBehavior.cs`；改「卡片/连线长什么样」在模板的 `.jalxaml` 里。**

### 3.2 模板与 Trimmed demo 是「标记 + 薄 code-behind」

模板产物与 Trimmed demo 同形：`workflow-{tree,node,slot,link}-view` 各是一对 `.jalxaml` + `.jalxaml.cs`，
标记里声明部件、写绑定、挂附着行为，code-behind 只剩 `InitializeComponent`。
`workflow-grid-decorator`、`workflow-minimap-overlay`、`workflow-template-selector`
仍是 `.cs`。逐个角色的形状见 `memory/modules/Templates/adapters/jalium.md`。

### 3.3 小地图的 `ScrollViewer` 由模板按名字接上

`WorkflowMinimapOverlay.ScrollViewer` 是普通自动属性，`NavigateToCanvasLocal` 要求 `_tree` 与 `ScrollViewer` **同时非空**。**适配器会赋值**：`ScrollViewerName` DP + `ResolveScrollViewer()`（`FindName(name) is ScrollViewer viewer`，`Loaded` 时再兜一次）⇒ 模板只要写 `ScrollViewerName="PART_ScrollViewer"`（`workflow-tree-view/TemplateClass.jalxaml`）就接上，宿主不必自己赋（Trimmed demo 宿主是标记，没有 `ScrollViewer = viewer`）。（非 Trimmed demo `Examples/Workflow/Jalium/` 走 `Minimap(viewer)` 构造器。）

### 3.4 画布变换的通道：`CanvasTransform` 附着属性

视图池没有 `UpdateRenderTransforms` 这对镜像方法（`git grep -n UpdateRenderTransforms -- Src Examples` 零命中）。画布变换的通道是附着属性：表面把世界位移作为 `WorkflowSurfaceBehavior.CanvasTransform` 发布在**宿主**上（`CanvasTransformProperty` 注册，`ApplyLayout` 里 `SetCanvasTransform` 写入），tree-view 模板的 `UserControl` 把同一个 DP **用一个 CLR 属性再暴露一次**（`workflow-tree-view/TemplateClass.jalxaml.cs` 的 `CanvasTransform => GetValue(CanvasTransformProperty)`）——因为本家的绑定**读不到括号路径**（`(Canvas.Left)` 也不行，见 §〇）——节点/连线模板于是绑 `RenderTransform="{Binding CanvasTransform, RelativeSource={RelativeSource AncestorType=UserControl}}"`（`workflow-tree-view/TemplateClass.jalxaml` 的两处）。**同一个 DP 对象、同一个值、同一种通知，只是换了个名字给绑定看得见。**

---

## 四、与 WPF 的对齐

**以 WPF 为基准。** Jalium 有完整的 `.jalxaml` 工具链，与另外六家同形：

| | 适配器 | 模板与 Trimmed demo |
|---|---|---|
| 表面 | `WorkflowSurfaceBehavior`（附着行为，按名字认领部件） | 标记里挂 `WorkflowSurfaceBehavior.*`、给部件起 `x:Name` |
| 池化 | `ViewPool`（挂点）+ `ViewHosting/`（`ViewSourcePipeline` 闭合 Core 池；`ViewFactory` 三级回退） | 标记里绑 `ViewPool.ItemsSource` / `TemplateSelector` |
| 小地图 | `WorkflowMinimapOverlay`（可继承） | 薄子类设四个颜色；`ScrollViewerName` 接上（§3.3） |
| 节点 | `WorkflowNodeDragBehavior` + `WorkflowSlotLayoutBehavior` | 卡片是 `.jalxaml` 的 `UserControl` |
| 连线 | `WorkflowLinkBounds`（自盒化助手） | 自己的控件 + 自己的 `OnRender` |
| 网格 | 只按 `IWorkflowGridDecorator` 收 | **整个渲染器在模板里**（`sealed class : Grid`） |
| 端口 | `WorkflowSlotLayoutBehavior` 量测后写回 `slot.Anchor` | 标记里声明槽控件（`PART_InputSlot` / `PART_OutputSlots`） |

**画布变换是唯一换了一种写法的**：WPF 的模板用 `Path=(behaviors:…Transform)` 读附着属性，
而本家的绑定**读不到括号路径**（§〇 实测：连 `(Canvas.Left)` 也不行）—— 于是 tree-view 模板的类把那
同一个 DP **用一个 CLR 属性再暴露一次**，模板改绑那个名字（§3.4）。

⇒ 剩下与 WPF 的差异只有**实测挡住**的那几条（括号路径、渲染器盒裁剪、没有 `LayoutUpdated`），
清单与依据在 [`WorkflowSystem/adapters/jalium.md` §三](../WorkflowSystem/adapters/jalium.md)。

## 五、仓库里到底被消费了什么

引用 `VeloxDev.Jalium` 工程的宿主只有 `Examples/Transition/Jalium/Demo/`、`Src/Test/AutoTest/Samplers/VeloxDev.SamplerTest.csproj`（只反射）、`Examples/Workflow/Jalium Trimmed/Demo/`（标记 + 附着行为 + 池化）与 `Examples/Workflow/Jalium/Demo/`（只派生 `WorkflowMinimapOverlay`，不接池化）。过渡轴真被跑；Workflow 基类被 Trimmed demo 与模板真派生。

**零消费者**：选择器的 `SlotTemplate` / `TreeTemplate`（模板 `workflow-template-selector/TemplateClass.cs`）—— 所有宿主只给节点与连线两项赋值（`workflow-tree-view/TemplateClass.jalxaml` 只写 `NodeTemplate=` 与 `LinkTemplate=`），插槽与树 item 从不进池；若进了而模板未设，`SelectTemplate` 会抛 `InvalidOperationException`。

⇒ 改任何一处之前先确认那条路上真的有调用者。

---

## 六、`GlobalUsings.cs` 与 `VeloxDev.Jalium.csproj`

**`GlobalUsings.cs` 三行，七个适配器逐字相同**（`global using` `VeloxDev.TransitionSystem` / `VeloxDev.TransitionSystem.Abstractions` / `VeloxDev.Threading`）：

- 这就是为什么 `TransitionSystem/` 的文件除 `Jalium.UI*` 之外几乎不写 `using`，却能直接用 `InterpolatorCore`、`ISampler`。
- **它不含 `VeloxDev.WorkflowSystem`** ⇒ `WorkflowSystem/AttachedBehaviors/` 里需要 Core 契约的文件各自写 `using VeloxDev.WorkflowSystem;`。
- **`global using` 是编译期的，不随引用传给消费者**：宿主要用 `Transition<T>` 仍得自己写 `using VeloxDev.TransitionSystem;`。

**csproj 里影响代码本身的条件**（`VeloxDev.Jalium.csproj`）：

| 事实 | 元素 | 后果 |
|---|---|---|
| `<TargetFramework>net10.0</TargetFramework>`（**单 TFM、不带平台后缀**） | `TargetFramework` | 七家里**只有这家**是单 TFM 且不带 `-windows`（Razor 是多 TFM `net6.0;net8.0`，仍不带 `-windows`）。csproj 顶部注释把理由写明了：适配器只用跨平台核心，**不用 `net10.0-windows` 的 `Jalium.UI.Desktop` 入口包**，所以能同时服务 Windows / Linux / Android |
| 唯一的 Jalium 引用是 `Jalium.UI.Controls 26.10.8` | `PackageReference Include="Jalium.UI.Controls"` | csproj 的注释：这是**能提供 `Canvas`/`ScrollViewer`/`Border`/`Control` + `DrawingContext`/`Geometry`/`FormattedText` 的最低平台中立包**，也是**同代码最低可编版本**（26.10.7 缺 `MainDispatcher`，26.10.0–26.10.6 缺 `DispatcherPriority`）。Jalium 的 demo 用 26.10.9（消费端可自行拉高；26.10.8 多次测量 9.2–12.5s/10s，26.10.9 约 8.4–9.3s；26.11.0 的 `ScrollViewer` classic gutter 会在 `ArrangeOverride` 内 `InvalidateMeasure`，空闲每帧跑 layout，约 11.1s）。**别名包 `Jalium.UI.Desktop` 由消费 demo 自己引** ⇒ 两个包仍是两个版本位，可以不同步 |
| Debug → `ProjectReference`／非 Debug → `PackageReference VeloxDev.Core 11.0.0` | 两个引用项 | 与另外六家同形的双轨；两条同时生效会报重复成员 |
| `NoWarn` 写成**一条** `1573;1591` | `NoWarn` | 与 WPF 现状同形。**注意**：`8605` / `8604`（DP 的 CLR 包装拆箱）在这个集合里没有触发点 —— 模板里值类型 DP 走泛型 `Read<T>`，不注册任何 DP 的 CLR 包装 |

---

## 七、陷阱（带依据）

1. **`ViewPool` 的 `Unloaded` 释放会把画布留在空白态。** 挂点在每次重建后订阅 `panel.Unloaded`（`ViewPool.Rebuild` → `OnPanelUnloaded` → `Cleanup`），`Dispose()` 回收所有视图、隐藏并清 `DataContext`。**表面移出树再放回去不会重新触发 `OnChanged`**（那只在附着属性变化时跑），而 `WorkflowSurfaceBehavior.BindTree` 对同一棵树会提前返回 ⇒ 空白画布，无异常。重新设一次 `ItemsSource`（或走 `SetIsEnabled` 的完整重绑）才会重建系统。
2. **重设 `ItemsSource` 或 `TemplateSelector` 是「全拆重建」而不是增量对齐。** 两个 DP 共用同一个 `OnChanged`（`ViewPool.OnChanged`），`Rebuild` 第一行是 `Cleanup`（`Dispose` 旧系统）⇒ 只要改其中一个，所有池内视图都会隐藏并清 `DataContext`，再按新配置重新物化。
3. **`ViewPool` 只要 `ItemsSource` 非空就建系统，`TemplateSelector` 可以为空**（`ViewPool.Rebuild`）；为空时 `ViewFactory` 直接走资源回退。表面先写选择器、后写 `ItemsSource`（`BindTree`），所以第一次变化只做一次 `Cleanup` 空转，第二次才真正建系统。
4. **Core 池的回收桶按 `item.GetType()` 键，active/pending 按引用身份**（`ViewPoolCore` 的 `ViewReferenceComparer`）⇒ 同一集合里放两个引用相同的 item 只会得到一个视图。**`ViewContainer.Deactivate` 把 `Visibility` 置 `Collapsed`、`DataContext` 置 `null` 后入池**，复用时 `Activate` 重新赋。
5. **槽是按标记里的名字认领的**：`WorkflowSlotLayoutBehavior` 只认 `SlotNames` / `SlotEnumeratorNames` 列出的控件名（`FindName`），槽自己从控件的 `DataContext` 取。⇒ **标记里改掉 `x:Name` 而不改那两个字符串，端口就不会被量测**（`slot.Anchor` 保持旧值，命中与连线端点一起错位），且**不报错**。
6. **删视图时同时置 `Collapsed` 与 `DataContext = null`**（`ViewHosting/ViewContainer.cs` 的 `Deactivate`）⇒ 视图里读 `DataContext` 的代码（含连线视图的守卫）在池化回收后会看到 `null`。
7. **csproj 的两条独有设定**：单目标 `net10.0` 无平台后缀 ⇒ 只能引用 `Jalium.UI.Controls` 这个平台中性包；`NoWarn` 为 `1573;1591`。

---

## 八、入口：我要改 X 先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 画布平移 / 缩放 / 指针路由 / 命中 / 右键菜单 | `WorkflowSystem/AttachedBehaviors/WorkflowSurfaceBehavior.cs`（表面行为，`static` 类 + 若干附着属性） |
| 节点拖拽 | `WorkflowSystem/AttachedBehaviors/WorkflowNodeDragBehavior.cs` |
| 插槽连接手势 | `WorkflowSystem/AttachedBehaviors/WorkflowSlotConnectionBehavior.cs` |
| 槽的锚点（量测写回 `slot.Anchor`） | `WorkflowSystem/AttachedBehaviors/WorkflowSlotLayoutBehavior.cs` |
| 模型事件转接（node / slot / tree sink） | `WorkflowSystem/AttachedBehaviors/WorkflowEvents.cs`（转发在 Core `WorkflowEventRelay`） |
| 网格线与标尺的数学 / 刻度 / 标签 | **模板** `Src/Templates/VeloxDev.Jalium.Templates/working/content/workflow-grid-decorator/TemplateClass.cs`（`sealed class : Grid, IWorkflowGridDecorator`，整个渲染器都在里面） |
| 卡片长什么样 | **模板** `workflow-node-view/TemplateClass.jalxaml`（`UserControl` + `WorkflowSlotLayoutBehavior` / `WorkflowNodeDragBehavior`） |
| 连线的几何与自盒化 | 自盒化助手 `WorkflowSystem/AttachedBehaviors/WorkflowLinkBounds.cs` + **模板** `workflow-link-view/TemplateClass.jalxaml.cs`（`Refresh` 摆盒、`BuildCurve` 烘焙、`OnRender` 画） |
| 端口在哪、怎么命中 | 适配器 `WorkflowSlotLayoutBehavior.cs`（量测写回 `slot.Anchor`）+ Core `WorkflowSurfaceMath` |
| 视图池、每元素视图的创建/复用/回收 | `WorkflowSystem/ViewHosting/`（`ViewSourcePipeline.cs` 闭合 Core 池，各层一文件；挂点 `ViewPool.cs`；`DataTemplate` 由模板产出） |
| 「item 类型 → 视图」的模板分派 | **模板** `workflow-template-selector/TemplateClass.cs`（`sealed class : DataTemplateSelector`）+ `ViewPool.TemplateSelector` |
| 小地图外观与导航 | `WorkflowSystem/AttachedBehaviors/WorkflowMinimapOverlay.cs`（模板只是 `MinimapOverlay` 薄派生设四色） |
| 线程、优先级、pacer、调度器 | `TransitionSystem/` 那组短文件 + `TransitionSystem/UIThreadInspector.cs` |
| 让某个 Jalium 类型可动画 | `TransitionSystem/Samplers/` + `TransitionSystem/Interpolator.cs` 静态构造的登记表 |
| 流式 `.Property(...)` 支持哪些类型 | `TransitionSystem/Transition.cs`（重载表） |
| 包结构、TFM、双轨引用 | `VeloxDev.Jalium.csproj` |
| **宿主怎么接上适配器** | 纯标记：表面在 `UserControl` 根写 `behaviors:WorkflowSurfaceBehavior.*`（具名部件 + `ZoomEnabled` + `LinkMenuKey`）、`ViewPool.ItemsSource/TemplateSelector` 给 `PART_Canvas`、小地图 `ScrollViewerName="PART_ScrollViewer"`。缩放与视口由表面自己管；宿主不需要 `AttachScrollViewer` / `SetTree` / `NotifyZoomCommitted` 这些调用 |

---

## 九、这份文件没写的东西

- 七个角色各自的职责、`PART_*` 约定、契约本身 —— `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；**Jalium 侧的角色机制都在适配器附着行为里（`WorkflowSystem/AttachedBehaviors/`），模板是标记 + 薄派生**。
- 这一家的平台硬限制与刻意背离（`Visual.ShouldRenderChild` 按 `RenderSize` 盒裁剪 ⇒ `WorkflowLinkBounds` 自盒化；`IsVirtualLink` 跳过拖拽预览；`CanvasTransform` 通道与括号路径限制；`ZoomPin` 的来龙去脉）—— `memory/modules/WorkflowSystem/adapters/jalium.md`。
- 过渡轴侧采样器逐一分析、`TransformSampler` 的端点短路与草稿实例类型守卫、`BrushSampler` 为什么只能混一个代表色 —— `memory/modules/TransitionSystem/adapters/jalium.md`。
- 模板侧：各条目产出什么形状、标尺 36 的单一来源在**模板**的 `workflow-grid-decorator` —— `memory/modules/Templates/adapters/jalium.md`。
- 主题切换的完整流向 —— **这一家没有这条线**（§一），见 `memory/modules/DynamicTheme/architecture.md`。
- 人面向的「怎么用这套模板搭一个 Jalium 工作流视图」—— `skills/veloxdev-create-workflow/references/gui/jalium.md`。

## Demo 工作台外壳（示例侧）

`Examples/Workflow/Jalium/Demo/MainWindow.cs` 是代码搭的壳：32px 标题条 + 48 活动栏 + 272 侧栏 + 4 分隔 + 主区（`流程图`/`输出` 两个 Tab）+ 4 分隔 + 348 检查器（`属性`/`AI 助手`）+ 24 状态栏。节点卡的色板与形状全在 `Views/Workflow/CardPalette.cs`（唯一色值来源）与 `NodeChrome.cs`（外壳/标题行/幽灵按钮），改色只改 `CardPalette`；`AccentWidth`、`TitleSize` 等度量也在那里。Jalium 没有 `Grid.SetColumnSpan`，标题条/状态条靠外层单列 `Grid` 包住内层三列 `Grid` 实现，跨列不能直接加在内层根上。

- **运行管理器没有绑定**：`Runs.Changed` 时重建行，并**延后一个 dispatcher 轮次**再换按钮（点“暂停”时不能在按钮自己的点击栈里把按钮换掉）。行内容照 `WorkflowRunTaskViewModel` 的 `SourceText`/`RootLabel`/`StateText`/`StartedText`/`ElapsedText`/`ProgressText` 与 `Can*` 属性。
- **`WindowStartupLocation.CenterScreen` 在这版 Jalium 上会静默退出**（进程消失、无异常），所以壳里没有设它；1500×900 窗口的默认位置在 150% DPI 下可能把右边推出屏幕，挪一下窗口即可。
- **Agent 对话是纯文本列表**：Jalium 没有 Avalonia 那种 `MarkdownView`，工具调用以转录行出现，不折成 chat 围栏。
- 小地图仍是适配器自带的 overlay，主区窄时会盖住一块画布。
