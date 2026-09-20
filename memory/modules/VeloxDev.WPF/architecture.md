# VeloxDev.WPF — 架构

> 代码：`Src/Adapters/VeloxDev.WPF/`。**30 个 .cs、3675 行**（`Attached/Workflow/` 8 个 2296 行，最大三个是 `WorkflowSurfaceBehavior.cs` 693、`WorkflowMinimapOverlay.cs` 508、`WorkflowSlotLayoutBehavior.cs` 443；`PlatformAdapters/` 21 个 1376 行，最大是 `ThemeValueConverters.cs` 323、`Samplers/TransformSampler.cs` 262、`Transition.cs` 224；顶层 `GlobalUsings.cs` 3 行）。
> **计数写法**：`git ls-files 'Src/Adapters/VeloxDev.WPF/*.cs' 'Src/Adapters/VeloxDev.WPF/**/*.cs'`。只写 `'.../**/*.cs'` 会得到 **29** —— 这条 pathspec 只匹配**子目录里**的 `.cs`，该目录**本级**的文件一个都不算（漏掉 `GlobalUsings.cs`；同理 `PlatformAdapters/**/*.cs` 只有 12 个采样器 / 665 行，`PlatformAdapters/` 本级那 9 个文件要另写 `PlatformAdapters/*.cs`）。
>
> 本文只写「读完这 30 个文件才知道的东西」。类型清单、成员表、继承树请看 IDE。
>
> **本模块没有 `adapters/` 子目录，也不该有**：模块名本身就是一个平台，不存在平台轴。它在三条轴上的平台差异分别落在
> `memory/modules/WorkflowSystem/adapters/wpf.md`、`memory/modules/TransitionSystem/adapters/wpf.md`、`memory/modules/DynamicTheme/architecture.md`，本文**指路不抄**。

---

## 一、一个项目、三条轴、三份契约

这个项目叫「WPF 适配器」，但它实际同时实现了三个 Core 模块的平台侧契约，彼此**在程序集内互不引用**（实测：`Attached/` 下零 `Transition`/`Interpolator`/`ThemeManager` 符号，`PlatformAdapters/` 下零 `Workflow` 符号）：

| 轴 | Core 契约 | 本项目落点 | 平台差异记在哪 |
|---|---|---|---|
| WorkflowSystem | 七个视图角色 | `Attached/Workflow/`（8 文件） | `memory/modules/WorkflowSystem/adapters/wpf.md` |
| TransitionSystem | 宿主 / 解释器 / 调度器 / 帧 pacer / 采样器 | `PlatformAdapters/`（6 类型 + `Samplers/` 12 个） | `memory/modules/TransitionSystem/adapters/wpf.md` |
| DynamicTheme | `IThemeValueConverter` | `PlatformAdapters/ThemeValueConverters.cs` | `memory/modules/DynamicTheme/architecture.md` |

**目录名与轴不对齐，别按目录推契约**：`Attached/` 只有 WorkflowSystem 一条轴（名字起得像「所有附着行为」，实际不是）；TransitionSystem 与 DynamicTheme 两条轴混在 `PlatformAdapters/` 里。

**三条轴的命名空间全部寄生在 Core 上**，各自占用 Core 那一条（`VeloxDev.TransitionSystem`、`VeloxDev.WorkflowSystem.AttachedBehaviors`、`VeloxDev.DynamicTheme`）。唯一例外是 12 个采样器，它们在 `VeloxDev.Adapters.NativeSamplers`（理由：`System.Windows.Point/Size/Color` 与 Core 用 `System.Drawing` 注册的三个同名不同型，见 `TransitionSystem/adapters/wpf.md` §一）。

两个直接后果：

1. **`VeloxDev.WorkflowSystem.AttachedBehaviors` 在 Core 里不存在** —— 它是七家适配器各自声明的一份同名命名空间（`git grep -l "namespace VeloxDev.WorkflowSystem.AttachedBehaviors"` 命中七家、命中 Core 0 次）。所以宿主 XAML 必须写 `assembly=`：`xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.WPF"`（`Examples/Workflow/WPF Trimmed/Demo/Views/Workflow/TreeView.xaml:6`，同文件 `:5` 的 `xmlns:workflowViews` 走 demo 自己的命名空间）。**写成 `assembly=VeloxDev.Core` 是找不到行为类型的**（同 demo 里 `Examples/Workflow/WPF/Demo/Views/Workflow/WorkflowView.xaml:9` 的 `xmlns:workflow` 才是 `assembly=VeloxDev.Core`，那指的是 VM 契约）。
2. 一个同时引用两家适配器的项目里，这些**类型名会歧义**（`ViewManager`/`WorkflowSurfaceBehavior` 两家同名）。代价的真实样貌见 `Examples/Transition/AUTO TEST/Samplers/WpfEntries.cs:9-20`：文件顶部一排显式类型别名 + 注释「本工程同时引用了 WinForms 与 MAUI 适配器」。

**不解决什么（常被误以为在这里）：**

| 不在模块内 | 实际归谁 |
|---|---|
| 七个角色的职责、附着属性名、`PART_*` 约定、注册位置 | `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3 |
| 缩放/平移/虚拟化/槽位锚点的**数学** | Core `WorkflowSurfaceMath`、`WorkflowSlotUpdateGate` 等 |
| 契约需要哪些成员、`FindOrCreate` 为什么必须用 | `memory/modules/TransitionSystem/extension.md` |
| 「主题是什么」「切换什么时候发生」 | DynamicTheme 模块 |
| 生成器 / 分析器 | **本项目不引用分析器包**（`VeloxDev.WPF.csproj` 的引用项只有 `VeloxDev.Core` 一条，`:26-27`），自己一行 `[VeloxProperty]` 都没有 |
| `dotnet new` 模板包 | `Src/Templates/VeloxDev.WPF.Templates/` 是**纯内容包**（`IncludeBuildOutput=false`、`EnableDefaultCompileItems=false`、无任何 `ProjectReference`）⇒ 装模板**不会**带来 `VeloxDev.WPF` 依赖，宿主必须自己引 |

---

## 二、`PlatformAdapters/`：注册与工厂

### 2.1 三条注册路径，只有一条需要宿主动手

| 要注册的东西 | 注册点 | 什么时候真的发生 |
|---|---|---|
| 12 个采样器 | `Interpolator` 的**静态构造**（`PlatformAdapters/Interpolator.cs:12-26`） | 第一次**构造** `Transition<T>` 时：Core 的字段初始化 `protected TInterpolatorCore interpolator = new();`（`Src/Core/VeloxDev.Core/TransitionSystem/Transition.cs:290`，约束 `:269` 的 `new()`） |
| 采样器所在的宿主/解释器/优先级 | `TransitionScheduler` 的类型实参（`PlatformAdapters/TransitionScheduler.cs:5-11`） | 同上，全部编译期写死 |
| DynamicTheme 的调度器工厂 | `Interpolator.CreateScheduler`（`PlatformAdapters/Interpolator.cs:30-33`） | **宿主必须显式调** `ThemeManager.SetPlatformInterpolator(new Interpolator())` |

**没有程序集级入口，也没有 `Initialize()`**：全模块 grep `ModuleInitializer` / `[assembly:` 零命中，唯一的静态构造就是 `Interpolator.cs:12`。⇒ 只用 `Transition<T>` 的宿主什么都不用做（静态构造会随 `new()` 跑）；而只走 DynamicTheme 的宿主漏掉 `SetPlatformInterpolator` 会**静默瞬切**（WPF 的两个调用点：`Examples/Theme/WPF/Demo/App.xaml.cs:16`、`Examples/Theme/WPF Trimmed/Demo/MainWindow.xaml.cs:47`；理由与各家的差异见 DynamicTheme 与 TransitionSystem 的记忆）。

工厂写的是 `FindOrCreate` 而不是 `new`（`PlatformAdapters/Interpolator.cs:32`）—— 只有它会把调度器**按 target 归档**，这是之后 `Pause`/`Seek`/`Exit` 找得回动画的唯一原因（契约在 `TransitionSystem/extension.md`）。

### 2.2 这家的线程判定：从 target 上取 dispatcher，取不到才退

`UIThreadInspector.ThreadFor` 整块包在 `try` 里（`PlatformAdapters/UIThreadInspector.cs:10-24`）：target 是 `DispatcherObject` 就取**它自己的** `Dispatcher`（`:14-16`），否则 `Application.Current?.Dispatcher ?? Dispatcher.FromThread(CurrentThread)`；两条都落空就是 `ThreadRef.None`（`ThreadRef.From(null)` 的定义在 `Src/Core/VeloxDev.Core/Threading/ThreadRef.cs`），后果只是这个目标**没有 pacer**，动画照跑。

**别把它抄成 Avalonia 的形状** —— 那边是一行 `ThreadRef.From(Dispatcher.UIThread)`（`Src/Adapters/VeloxDev.Avalonia/PlatformAdapters/UIThreadInspector.cs:9`），恒取 UI 线程。这家取 target 的 dispatcher，意味着对一个属于别的 dispatcher 的对象做动画会真切到**那条**队列上；`IsCurrentThread` 的 `thread.TryGet<Dispatcher>(out var d) && d.CheckAccess()`（`:26-27`）也是同一件事的验算。唯一与 Jalium 同形。

其余是空壳，**别以为漏写了**：`State : StateCore`（`PlatformAdapters/State.cs:3`）、`TransitionScheduler : TransitionSchedulerCore<…>`（`TransitionScheduler.cs:5-11`）、`Transition : TransitionCore`（`Transition.cs:10-13`）。`TransitionEffects` 的三个预设（`TransitionEffects.cs:5/9/13`：`Empty` 0s、`Theme` 0.46s、`Hover` 0.32s）**六家逐字相同**（WinUI 只是 `class` 而非 `static class`），不是这家特有的。

---

## 三、`Attached/Workflow/`：每个角色的入口与挂载方式

### 3.1 六种挂法，四种门槛

| 角色 | 怎么挂上去 | 宿主类型门槛 | 每元素状态存哪 | 需要谁的 `DataContext` |
|---|---|---|---|---|
| 画布宿主 `WorkflowSurfaceBehavior` | `IsEnabled="True"` + 5 个 `*Name` | **`UserControl`**（`:113`） | 私有附着 DP `State`（`:70-74`） | **宿主自己**的 DC 必须是 `IWorkflowTreeViewModel`（`:295`/`:456`/`:503`/`:563`/`:579`） |
| 插槽布局 `WorkflowSlotLayoutBehavior` | `IsEnabled="True"` + `SlotNames`/`SlotEnumeratorNames`/`CoordinateHost*` | **`UserControl`**（`:78`） | 私有附着 DP `State`（`:55-59`） | **被挂的那个 UserControl** 自己的 DC 是 node VM（`:252`） |
| 节点拖拽 `WorkflowNodeDragBehavior` | `IsEnabled="True"` + `CoordinateHostName/Type` | **`UIElement`**（`:55`）——最宽 | 私有附着 DP `State`（`:38-42`） | 自己或**任一祖先**（`:176-188`） |
| 插槽连接 `WorkflowSlotConnectionBehavior` | `IsEnabled="True"` | **`Control`**（`:21`） | 无 | **必须是自己**，不找祖先（`:38`/`:49`） |
| 视图池 `ViewPool` | 赋 `ItemsSource`（`ViewPool.cs:14-18`）+ 可选 `TemplateSelector` | **`Panel`** | 唯一用 `ConditionalWeakTable<Panel, ViewManager>`（`ViewPool.cs:38`） | 不读；它是**写** DC 的那一方（`ViewManager.cs:175`） |
| 画布变换 `WorkflowCanvasTransformBehavior` | 不用挂：XAML 里**绑**到它的 `Transform` | `UIElement`（值载体） | 无 | 不读 |
| 小地图 `WorkflowMinimapOverlay` | 不用挂：**继承它** + 赋 `WorkflowTree`/`ScrollViewerName` | 继承（它是 `public class`，全模块唯一非 `sealed`） | 实例字段（它本身就是元素） | 不读 |

三条容易踩的：`IsEnabled` 默认 **false**（四个开关的 `PropertyMetadata(false, …)`），必须显式打开；`ViewPool` 与 `WorkflowCanvasTransformBehavior` **没有** `IsEnabled`（前者靠赋值触发，后者是被读的通道，见 `WorkflowCanvasTransformBehavior.cs:25-30` 的故意空回调）；把「插槽连接」挂到插槽的**外层容器**上会因为 DC 不在自己身上而静默失效（`WorkflowNodeDragBehavior` 反而会沿祖先找到，别拿它推）。

每个 `OnIsEnabledChanged` 都是**先 `Detach` 再 `Attach`**（`WorkflowSurfaceBehavior.cs:127-140` 最清楚），所以重复置 `True` 不会叠加订阅；`Detach` 里 `ClearValue(StateProperty)`（`:155`）顺带丢状态。

### 3.2 `Refresh` 是唯一的重驱动入口，而且被 `IsEnabled` 挡着

`WorkflowSurfaceBehavior.Refresh(UserControl)`（`:97-109`）会重解析 5 个命名部件 → `ApplyLayout` → `UpdateVisibleRegion`；判断在**第一行**：`if (!GetIsEnabled(host)) return;`（`:99-102`）⇒ 没打开开关时调它**什么也不做、不抛**。

它是给宿主在「模型改完了」之后手动调的口子：WPF 的 demo 调在 `Examples/Workflow/WPF/Demo/Views/Workflow/WorkflowView.xaml.cs:81`、`:140`、`:188`、`:193`（后两处经 `Dispatcher.InvokeAsync(..., Background)`）。**七套模板里只有 WinForms 的 tree-view 调它**（`Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-tree-view/TemplateClass.cs:735`），WPF 模板不调 —— 也就是说在 WPF 上「改完模型要刷视图」不是必需的（`ScrollChanged`/`DataContextChanged`/`Loaded` 会驱动），但 demo 里那几处是必需的。

### 3.3 小地图：这个项目里唯一被**继承**的角色

`WorkflowMinimapOverlay` 是**基类**，宿主写子类；模板 `workflow-minimap-overlay` 就是它的空壳子类（`Src/Templates/VeloxDev.WPF.Templates/working/content/workflow-minimap-overlay/TemplateClass.cs:12-23` 只设四个画刷）。它自带一整套外观 DP 与默认值（`WorkflowMinimapOverlay.cs:73-119`），并自己订阅 Core 模型（节点/连线的集合与 `PropertyChanged`，`:217-299`）。

三个只在读这个文件时才知道的点：

- **它拿自己的 `ScrollViewer` 只靠自己那一侧**：`OnLoaded` 沿视觉树上溯到最近的 `UserControl` 再 `FindName(ScrollViewerName)`（`:185-204`，注释 `:189-191` 解释了为什么不能从 `Window` 找），**而不是**问同宿主上的 `WorkflowSurfaceBehavior`。
- 绘制全在 `OnRender`（`:442-507`），交互全在自己覆写的四个鼠标方法（`:368-401`）—— 见 `WorkflowSystem/adapters/wpf.md` §2.6。
- `RulerBand` 硬编码返回 `0`（`:132`），而 `RulerThickness` DP（`:63-65`）全模块从不被读 —— 这条已在 `WorkflowSystem/adapters/wpf.md` 坑 2，本文不重复。

### 3.4 画布变换的值写在**宿主**上

`ApplyLayout` 把新的 `TranslateTransform` 写到**宿主 UserControl**的附着属性上（`WorkflowSurfaceBehavior.cs:571` 的 `WorkflowCanvasTransformBehavior.Apply(host, transform)`），再由模板把节点/连线视图各自的 `RenderTransform` 用 `RelativeSource AncestorType` 绑到宿主的这个属性读走（`Examples/Workflow/WPF Trimmed/Demo/Views/Workflow/TreeView.xaml:21`、`:33`）。⇒ **要改「画布怎么变换」改的是 `WorkflowSurfaceBehavior` 里那个写值处，不是 `WorkflowCanvasTransformBehavior.cs`**（后者只有 31 行，一个 DP 加一个空回调）。

---

## 四、`GlobalUsings.cs` 与 `VeloxDev.WPF.csproj`

**`GlobalUsings.cs` 三行，七个适配器逐字相同**（`global using` `VeloxDev.TransitionSystem` / `VeloxDev.TransitionSystem.Abstractions` / `VeloxDev.Threading`）：

- 这就是为什么本模块的 PlatformAdapters 文件都不写 `using VeloxDev.TransitionSystem;` 却能用 `InterpolatorCore`、`ISampler`（它们定义在 `...Abstractions` 里，`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:7`）。
- **它不含 `VeloxDev.WorkflowSystem`** ⇒ `Attached/` 里 5 个文件各自写 `using VeloxDev.WorkflowSystem;`（另 3 个不需要：`ViewPool.cs`、`WorkflowCanvasTransformBehavior.cs`、`ViewManager.cs`）。
- **`global using` 是编译期的，不随包/`ProjectReference` 传给消费者**：宿主要用 `Transition<T>` 仍得自己写 `using VeloxDev.TransitionSystem;`（`Examples/Theme/WPF/Demo/App.xaml.cs:3`、`Examples/Theme/WPF Trimmed/Demo/MainWindow.xaml.cs:3` 都写了）。⇒ 别指望引用 `VeloxDev.WPF` 之后自己项目里能少写一行 using。

**csproj 里影响代码本身的条件**（`VeloxDev.WPF.csproj`）：

| 事实 | 行 | 后果 |
|---|---|---|
| `<TargetFrameworks>netframework4.6.1;net5.0-windows;netcoreapp3.0` | `:7` | 七家里只有这家与 WinForms 是这个三元组；`netcoreapp3.0` 是三元组里唯一不带 `-windows` 的，`UseWPF`（`:10`）由此成立，配 `SuppressTfmSupportBuildWarnings`（`:18`）压告警 |
| 三元组里**没有 netstandard2.0** | — | `PlatformAdapters/Transition.cs:197-222` 的 `#if !NETSTANDARD2_0`（4 个 `System.Numerics` 重载）在这家**恒为真**。六家写了这个守卫，只有 Avalonia（`netstandard2.0;net6.0`）那条是真的 |
| Debug → `ProjectReference`（`:26`）／非 Debug → `PackageReference`（`:27`） | — | 与生成器那套双轨同形；包里唯一的依赖是 `VeloxDev.Core` 9.0.0 |
| `GeneratePackageOnBuild`（`:12`）+ `GenerateDocumentationFile`（`:6`） | — | 全仓只有 Core、Jalium、WPF 三个项目生成 XML 文档 |
| `NoWarn` 写成**两条属性**（`:4` = `1573`、`:5` = `1591`） | — | 后者覆盖前者，见坑 2 |

---

## 五、陷阱（带依据）

1. **小地图根本不画连线，但它的连线订阅是活的。** 类文档写着 "a thumbnail overview of all nodes, **links**, and the visible viewport"（`WorkflowMinimapOverlay.cs:14-18`），而 `OnRender`（`:442-507`）只画节点矩形与视口框；`LinkBrush`（`:85-87`/`:144`）与 `LinkStrokeThickness`（`:67-69`/`:140`）**全模块从不被读**（grep 只有声明与访问器命中）。同时 `_subscribedLinks` 那一套是完整的：`SubscribeLink`（`:264-269`）、`OnLinksChanged`（`:278-287`）、`UnsubscribeFromTree` 里的退订（`:249-254`）都在，`IWorkflowLinkViewModel` 的 Sender/Receiver 变更也会 `MarkDirty`。⇒ 想「让连线出现在小地图里」要自己补 `OnRender` 的分支，别以为订阅没接上。**以代码为准，注释说反了。**
2. **`NoWarn` 写两遍，第一条被覆盖**（`VeloxDev.WPF.csproj:4` 与 `:5`）。MSBuild 按序求值同名属性，最终 `NoWarn` 只有 `1591`，`1573`（「参数缺 `<param>` 标签」）**没有生效**。当前无实际症状 —— 全模块 `<param name=` 零命中，`<summary>` 只有 8 处（`ViewPool.cs:8`、`WorkflowCanvasTransformBehavior.cs:6`、`WorkflowMinimapOverlay.cs:14`、`Samplers/BrushSampler.cs:77`/`:85`、`Samplers/ColorSampler.cs:28`、`Samplers/DropShadowEffectSampler.cs:6`/`:62`），没有「部分参数写了 `<param>`」的方法。正确形参考 `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:4`（一条 `1573;1591;NU5104`）。
3. **`ThemeValueConverters.cs` 里 4 个类名与 WPF 自带类型撞名**：`BrushConverter`、`ColorConverter`、`ThicknessConverter`、`CornerRadiusConverter` 在 `System.Windows.Media`/`System.Windows` 里都有。所以文件里凡要用 WPF 自己那个必须全限定：`new System.Windows.Media.BrushConverter()`（`:156`、`:210`、`:246`）。⇒ 在这个命名空间下新写转换器时，写短名会解析到**本项目**这一个，且不报错（同名不同类型，转换结果只是悄悄不对）。
4. **`ObjectConverter` 有两处直接碰 `Application.Current`，只有一处有守卫**：`ThemeResourceLookup.TryFindResource` 先判 `Application.Current is null` 再找（`:285-288`，并递归 `MergedDictionaries`，`:313-319`），而 `:265` 的 `Application.Current.TryFindResource(strValue)` 没有。无 `Application` 的进程（控制台探针、单测）走到那条分支会抛 `NullReferenceException`，被紧随其后的 `catch { return null; }`（`:272-275`）吞掉。⇒ 症状是**静默拿到 null**，不是异常。转换器遍地 `catch { return null; }` 是刻意姿态，与 DynamicTheme「异常不逃逸」一致。
5. **插槽布局的触发名单是拼出来的，属性名对不上就不排队。** `Sync` 每次都重建 `state.SlotPropertyNames`（`WorkflowSlotLayoutBehavior.cs:266-288`）：先放 `Anchor`/`Size`，再把 `SlotNames`/`SlotEnumeratorNames` 里每个控制名连「去掉 `PART_` 前缀」两种形式都塞进去，最后硬编码兜底 `InputSlot`/`OutputSlot`/`OutputSlots`（`:286-288`）。而 `OnNodePropertyChanged` 只认这个集合（`:186-196` 的 `!state.SlotPropertyNames.Contains(e.PropertyName)` 直接 return）。⇒ 你的节点 VM 用别的属性名发通知时，改那个属性的动画不会触发锚点重算 —— **改锚点或尺寸才会**。
6. **`WorkflowMinimapOverlay` 的 `ScrollViewer` 只解析一次，而且只在那一刻。** `ScrollViewerName` 是**无回调**的普通 DP（`:121-123`），查找只在 `OnLoaded` 里做一次（`:185-204`），晚设（数据绑定后到、或换模板重建）就永远没有 `_scrollViewer`，此后 `NavigateToWorld` 被 `:427` 的 `is not null` 挡住 —— 小地图变成只看不动的图。demo 都是内联在 XAML 里设的名（`Examples/Workflow/WPF Trimmed/Demo/Views/Workflow/TreeView.xaml:60-64`），所以看不出来。
7. **`ViewManager` 的模板缓存按 `Type` 键、永不失效**（`ViewManager.cs:22` 的 `_templateMap`，写入在 `:241`/`:257`/`:270`，读取短路在 `:231-232`）。⇒ `DataTemplateSelector` 对同一类型的**第二次**返回不会被再问一遍；selector 里写「按状态换模板」的逻辑在池化复用下只会生效一次，而且没有清缓存的 API。
8. **`ViewManager.Attach` 会先退订旧集合再 `ClearAllViews()`**（`:24-42`），所以换 `ItemsSource` 是「全部拆掉重建」而不是「增量对齐」；`_viewPool` 里那些复用的 `FrameworkElement` 不参与（它们只是被 `Visibility.Collapsed`，仍在 `panel.Children` 里，见 `WorkflowSystem/adapters/wpf.md` 坑 8）。分批创建是 `DispatcherPriority.Background` + 每批 3 个（`:115-147`，`batchSize` 在 `:125`），只有带 `DispatcherPriority` 的三家有这个能力 —— 别家的补偿见同文档 §2.8。

---

## 六、入口：我要改 X 先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 画布平移 / 缩放 / Ctrl+滚轮 / 空白判定 / 平移到边时的扩张 | `Attached/Workflow/WorkflowSurfaceBehavior.cs`（数学在 Core `WorkflowSurfaceMath`） |
| 节点拖拽的落点与坐标宿主 | `Attached/Workflow/WorkflowNodeDragBehavior.cs` |
| 插槽锚点写回、刷新时机 | `Attached/Workflow/WorkflowSlotLayoutBehavior.cs` |
| 插槽两阶段连接命令 | `Attached/Workflow/WorkflowSlotConnectionBehavior.cs` |
| 视图池、`DataTemplate` 查找、分批 | `Attached/Workflow/ViewManager.cs`（挂点 `ViewPool.cs`） |
| 小地图外观与导航 | `Attached/Workflow/WorkflowMinimapOverlay.cs` |
| 画布变换的值（写值处） | `Attached/Workflow/WorkflowSurfaceBehavior.cs` 的 `ApplyLayout`（不是 `WorkflowCanvasTransformBehavior.cs`） |
| 线程、优先级、pacer、调度器 | `PlatformAdapters/` 六个类型 —— 差异与坑见 `memory/modules/TransitionSystem/adapters/wpf.md` |
| 让某个 WPF 类型可动画 | `PlatformAdapters/Samplers/` + `PlatformAdapters/Interpolator.cs:12-28` 的注册表 |
| 主题字符串 → 值 | `PlatformAdapters/ThemeValueConverters.cs` |
| 包结构、TFM、双轨引用 | `VeloxDev.WPF.csproj` |
| **宿主怎么把这些接起来（最小可读样本）** | `Examples/Workflow/WPF Trimmed/Demo/Views/Workflow/TreeView.xaml`（79 行，六种挂法一次看全）；完整版在 `Examples/Workflow/WPF/Demo/Views/Workflow/WorkflowView.xaml` |

---

## 七、这份文件没写的东西

- 七个角色各自要暴露什么成员、`PART_*` 命名约定、七角色职责表 —— `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3 与 `skills/veloxdev-create-workflow/references/view-layer.md`。
- 这家的平台硬限制、与别家的刻意背离、逐条坑（`UserControl` 边界、名字作用域、三连 `UpdateLayout()`、`MouseUp` 的 `handledEventsToo`、缩放提交顺序…）—— `memory/modules/WorkflowSystem/adapters/wpf.md`。
- TransitionSystem 侧的采样器逐一分析、pacer 与 `HasShutdownStarted`、27 个 `Property` 重载里只有 `Transform` 不可替代 —— `memory/modules/TransitionSystem/adapters/wpf.md`。
- 主题切换的完整流向、两级缓存、`Current` 的所有权 —— `memory/modules/DynamicTheme/architecture.md`。
- 人面向的「怎么用这套模板搭一个 WPF 工作流视图」—— `skills/veloxdev-create-workflow/references/gui/wpf.md`。
