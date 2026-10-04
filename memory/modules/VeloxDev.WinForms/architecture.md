# VeloxDev.WinForms — 架构

> 代码：`Src/Adapters/VeloxDev.WinForms/`。**32 个 .cs、7489 行**（`Attached/Workflow/` 21 个 6570 行，最大 `WorkflowTreeView.cs` 1288、`WorkflowSlotLayoutBehavior.cs` 775、`WorkflowSurfaceBehavior.cs` 732、`WorkflowNodeDragBehavior.cs` 529、`WorkflowSlotView.cs` 437、`WorkflowNodeView.cs` 423；`PlatformAdapters/` 10 个 893 行，最大 `ThemeValueConverters.cs` 473、`Transition.cs` 138、`UIThreadInspector.cs` 86；`PlatformAdapters/Samplers/` 1 个 23 行；顶层 `GlobalUsings.cs` 3 行）。同目录另有一份 `README.md`（245 行）。
> **计数写法**：`git ls-files 'Src/Adapters/VeloxDev.WinForms/*.cs' 'Src/Adapters/VeloxDev.WinForms/**/*.cs'` = **32**。只写 `'.../**/*.cs'` 得 **31** —— 这条 pathspec 不匹配目录**本级**的 `.cs`（漏掉 `GlobalUsings.cs`；七家各自都正好漏这 1 个）。
>
> 本文只写「读完这 32 个文件才知道的东西」。类型清单、成员表、继承树请看 IDE。
>
> **本模块没有 `adapters/` 子目录，也不该有**：模块名本身就是一个平台，不存在平台轴。它在三条轴上的平台差异分别落在
> `memory/modules/WorkflowSystem/adapters/winforms.md`、`memory/modules/TransitionSystem/adapters/winforms.md`、`memory/modules/Templates/adapters/winforms.md`，本文**指路不抄**。

---

## 一、一个项目、三条轴、外加一层基类

| 轴 | Core 契约 | 本项目落点 | 平台差异记在哪 |
|---|---|---|---|
| WorkflowSystem | 七个视图角色 + 模型事件 | `Attached/Workflow/`（21 文件） | `WorkflowSystem/adapters/winforms.md` |
| TransitionSystem | 宿主 / 解释器 / 调度器 / 帧 pacer / 采样器 | `PlatformAdapters/`（9 类型 + `Samplers/` 1 个） | `TransitionSystem/adapters/winforms.md` |
| DynamicTheme | `IThemeValueConverter` | `PlatformAdapters/ThemeValueConverters.cs`（13 个转换器） | DynamicTheme 模块记忆 |

三个只在读这个目录时才会发现的结构事实：

1. **三条轴互不引用，但没有任何编译期阻挡。** 实测 `Attached/` 下零 `Transition`/`Interpolator`/`ThemeManager` 符号、`PlatformAdapters/` 下零 `Workflow` 符号 —— 靠的是纪律：`GlobalUsings.cs` 已经把 `VeloxDev.TransitionSystem` 灌进了**每一个**文件（含 `Attached/`）。⇒ 「在这家写了一条跨轴的调用」不会被拦下，七家都维持着零引用，改的人得自己守。
2. **命名空间全部寄生在 Core 上，唯一例外的命名空间还和 Core 撞了名。** 唯一的采样器在 `VeloxDev.Adapters.NativeSamplers`（`PlatformAdapters/Samplers/PaddingSampler.cs:1`），而 **Core 自己的 15 个采样器在 `VeloxDev.TransitionSystem.NativeSamplers`**（`Src/Core/VeloxDev.Core/TransitionSystem/NativeSamplers/ColorSampler.cs:3`）—— 两个不同命名空间。⇒ 写 Core 那一支的 `using` 引不到这家唯一自带的采样器（AUTO TEST 用的是前者：`Examples/Transition/AUTO TEST/Samplers/WinFormsEntries.cs:1`）。
3. **`VeloxDev.WorkflowSystem.AttachedBehaviors` 在 Core 里不存在**：七家各声明一份同名命名空间。XAML 平台靠 `assembly=` 消歧，C# 宿主只能写全名或别名。

**DynamicTheme 这条轴在这家没有消费者**：`Examples/Theme/` 下只有 Avalonia/WPF 两族目录（无 WinForms），`ThemeManager.SetPlatformInterpolator` 的仓内调用点无一处是本平台。473 行转换器没有任何 demo/测试碰过 —— 它是三条轴里唯一「有实现、零使用」的。

---

## 二、这套适配器现在「给」什么：整套表面（2026-10-03 重构）

这家**不再是「薄适配器」**。与 Jalium 同批，它按 [adapter-base-class-specifications.md](../specifications/adapter-base-class-specifications.md) 的思路，把**平台机制整体搬进适配器基类**，模板/宿主只派生或赋参数。旧的「网格装饰器/小地图/连线视图的实现一个都没有」已是历史 —— 现在三种实现都在包里。

**基类层（新，宿主派生）：**

| 类型 | 基类 | 宿主做什么 | 关键成员 |
|---|---|---|---|
| `WorkflowTreeView` | `UserControl`（`abstract`，`:35`） | **派生**，实现 `CreateNodeView`（`:232`）/ `CreateLinkView`（`:237`），或改赋 `TemplateSelector`（`:145`） | 自带 `PART_ScrollViewer`/`PART_Canvas`/`PART_GridDecorator`（画布自己就是装饰器，`:60`）、chrome 配色、签名平移引擎、自绘网格与浮动标尺层（owned layered popup，`:1078` 的 `RulerOverlayForm`）、视图池接线、布局调度；`RulerReserve = SurfaceCanvas.DefaultRulerThickness`（`:45`）；`Input`（`:176`，`WorkflowInput`）；虚拟钩子 `OnTreeAttached`/`OnSurfaceRefreshed`/`OnConnecting`/`OnConnected`/`OnBuildLinkMenu`（`:241-280`） |
| `WorkflowNodeAttachment` | 附到用户的 `Control` 上（2026-10-04 起；**不再是基类**） | 用户自己的控件 + `Attach(this)`，自己在 `OnPaint` 里画整张卡 | `Node`/`Title`/`Collapse`/`SurfacePanOffset`/`SurfaceContentOffset`；`ApplySurfacePosition`（实现 `IWorkflowSurfaceNodeView`）；`ResolveInputSlot`/`ResolveSlotLabel`；事件 `Rebound`/`TitleChanged`/`CollapseChanged`/`AnchorChanged`/`Moving`…`Deleted` |
| `WorkflowSlotAttachment` | 附到用户的 `Control` 上（2026-10-04 起；**不再是基类**） | 用户自己的控件 + `Attach(this)`，自己在 `OnPaint` 里画 | `Slot`/`SlotPath`/`PathViewBox`/`SlotBackground`/`StandbyColor`/`BorderColor`/`IconPath`/`GlyphColor`；事件 `ChannelChanging`/`ChannelChanged`；图形解析器另立 `SvgPathParser.cs` |
| `WorkflowLinkAttachment` | 附到用户的 `Control` 上（2026-10-04 起；**不再是基类**） | 用户在视图构造里 `WorkflowLinkAttachment.Attach(this)`，自己在 `OnPaint` 里画 | `LineColor`/`Thickness`/`PullMinimum`/`SurfaceBackground`/`Curve`/`Link`；`Paint(Graphics)`/`Bind(link)`/`Attach`/`For`；事件 `PointerEntered`/`PointerLeft`/`PointerPressed`/`PointerReleased` |
| `WorkflowGridDecorator` | `Panel`（`:23`），实现 `IWorkflowGridDecorator` | **派生**，设调色板/间距 | `DefaultRulerThickness = 36`（`:31`）、`GridSpacing`/`MajorLineEvery`、`RulerThickness`、`RulerBand => RulerThickness`（`:165`） |
| `WorkflowTemplateSelector` | `IWorkflowTemplateSelector`（`:21`） | 设四个工厂 | `NodeViewFactory`/`SlotViewFactory`/`LinkViewFactory`/`TreeViewFactory`（`:24-33`）、`CreateView`（`:36`） |

**支撑件：**

- `IWorkflowSurfaceNodeView`（`:14`）：`ApplySurfacePosition(Point panOffset, Offset contentOffset)` —— 表面不能按类型认用户的卡片，于是把投影交给卡片自己放。
- `IWorkflowMinimapScrollSource`（`:16`）：`ViewportScrollRequested` 事件；**故意不进 Core 的 `IWorkflowMinimapOverlay`**（那个要框架中立），由 `WorkflowTreeView.MinimapOverlay` setter 订阅。
- `ModelChangeRelay`（`internal`，`:11`）：把「订阅、退订、`InvokeRequired` 编组」这一套收敛成一份，三个视图控件共用（这就是「code-only 平台把模型事件暴露成可重写钩子」那条提交的落地方式 —— 与四家 XAML 平台的 `WorkflowEvents` sink **不同形**）。
- `WorkflowSurfaceColors`（`:15`，解析 `#RRGGBB`/`#AARRGGBB`/颜色名）、`WorkflowSurfaceGraphics`（`:10`，圆角矩形）、`WorkflowSurfaceGrid`（`:14`，大/次网格线判定 + 标尺标签格式）—— 三份被多个表面控件共用的静态工具。

**附着行为层（旧，仍在）：** `WorkflowSurfaceBehavior`、`WorkflowSlotLayoutBehavior`、`WorkflowNodeDragBehavior`、`WorkflowSlotConnectionBehavior`、`WorkflowCanvasTransformBehavior` 仍是静态 `Get/Set` 形态；`WorkflowMinimapOverlay` 现在是一个**真正的可继承实现**（`Panel, IWorkflowMinimapOverlay, IWorkflowMinimapScrollSource`，`:23`），不再是「有 API 没读者」。`WorkflowTreeView` 的 ctor 自己把 `SetIsEnabled`/`SetZoomEnabled`/`Set*Name` 调一遍（`:374-380`）。

**这条链的因果**：没有标记语言 ⇒ 宿主只能用代码装配 ⇒ 适配器发可继承基类、模板只派生/填值；而 XAML 家惯用的 `WorkflowEvents` sink 在这里换成基类上的 `virtual` 钩子（转发由 `ModelChangeRelay` 做）。

---

## 三、宿主怎么把它接起来：派生 `WorkflowTreeView` + 命令式 `Set*` + pull

**没有属性系统、没有 `DataContext`、没有注册表。** 全模块 grep `ModuleInitializer` / `[assembly:` 零命中，唯一的静态构造是 `PlatformAdapters/Interpolator.cs:7-10`（注册采样器）。状态一律存在 `ConditionalWeakTable<Control, State>` 里，附着属性退化成静态 `Get`/`Set`。

**名字仍是唯一的接线语言。** 所有 `Set*Name` 立即解析，解析器是 `FindControlByName`（`WorkflowSurfaceBehavior.cs:691`）：**递归遍历宿主子树 + `Ordinal` 比较**，取第一个命中。⇒ 与 WPF 的 `NameScope` 不同：只管宿主子树、跨容器/非子孙一律找不到、**找不到不抛也不报**，只在那条数据推送处静默跳过。

**三个开关各做什么（这家最容易误判的一处）：**

| 调什么 | 真的做了什么 |
|---|---|
| `Set*Name` | 立即解析并接线；其中 ScrollViewer/Canvas/GridDecorator/MinimapOverlay 四个还会顺带对解析到的控件加 `WS_CLIPCHILDREN`（`EnsureClipChildrenForName`，`:677`） |
| `SetIsEnabled(true)`（`:141`） | 置位 + 给宿主加 `WS_CLIPCHILDREN` + 顶层窗体加 `WS_EX_COMPOSITED`（`NativeWindowStyleHelper.EnsureClipChildren/EnsureComposited`）。**不订阅任何事件**，也没有早退（重复调用只重刷样式位） |
| `SetZoomEnabled(true)`（`:170`） | 挂 `element.MouseWheel` **加上** `Application.AddMessageFilter(state)`（`:189`）—— `SurfaceState : IMessageFilter`（`:15`）；关掉时成对摘除（`:194`）。过滤器条件是 `WM_MOUSEWHEEL` + `Control.ModifierKeys != Keys.Control` 精确比较 + 「消息目标沿父链能找到 zoom-enabled 宿主」（`ResolveSurfaceHost`，`:100`） |

- **`Refresh(Control)`（`:417 起`）是唯一的重驱动入口**，第一行被 `IsEnabled` 门住；它把当前 scroll/content 偏移推给 GridDecorator/MinimapOverlay、调 `SetVirtualizeInset`（`:464`），循环末尾 `PerformLayout()` + `Invalidate()`，而 `Update()`（同步重画）**只在 `host.Capture` 为真时**调（`:488`）。
- **这家现在有自己的平移实现**（在 `WorkflowTreeView` 里，不在 `WorkflowSurfaceBehavior`）：`WorkflowSurfaceBehavior.ResolvePanOffset`（`:650`）仍只**反射读**宿主的 `PanOffset` 属性或私有 `_panOffset` 字段（注释明说：平移由宿主的 tree-view 私有持有、在 `ApplyPan` 里推给画布）；`WorkflowTreeView` 把 `PART_Canvas`（`SurfaceCanvas`）的 `PanOffset` 写成签名偏移（`:733`），卡片经 `IWorkflowSurfaceNodeView.ApplySurfacePosition` 自己落位。
- **视图池由赋值触发**：`ItemsSource` 与 `TemplateSelector` **都**非空才起 `ViewManager`（`ViewPool.cs:96`），任一置 `null` 即停。
- **滚动宿主只当视口用**：`PART_ScrollViewer.AutoScroll = false`（`:357`），因为平移直接改画布、且 WinForms 的 AutoScroll 位置被夹在 `>= 0`，只允许往右下平移。

---

## 四、`GlobalUsings.cs` 与 `VeloxDev.WinForms.csproj`

**`GlobalUsings.cs` 三行，与 WPF/WinUI 逐字相同**（`diff` 无输出）：`global using` `VeloxDev.TransitionSystem` / `VeloxDev.TransitionSystem.Abstractions` / `VeloxDev.Threading`。

- 这就是 `PlatformAdapters/` 里不写 `using` 却能用 `InterpolatorCore`/`ISampler` 的原因（它们在 `...Abstractions` 里）。
- **它不含 `VeloxDev.WorkflowSystem`** ⇒ `Attached/` 里需要 Core 契约的文件各自写这一行（基类文件大多靠命名空间嵌套免了它）。
- **`global using` 是编译期的，不随包/`ProjectReference` 传给消费者** —— 宿主要用 `Transition<T>` 仍得自己写 `using VeloxDev.TransitionSystem;`。

**csproj 里影响代码本身的条件**（`VeloxDev.WinForms.csproj`）：

| 事实 | 行 | 后果 |
|---|---|---|
| `<TargetFrameworks>netframework4.6.1;net5.0-windows;netcoreapp3.0` | `:4` | 七家里只有这家与 WPF 是这个三元组 |
| `UseWindowsForms` + `SuppressTfmSupportBuildWarnings` | `:6`、`:9` | 三元组里 `netcoreapp3.0` 不带 `-windows`，靠后者压掉兼容告警（WPF 同形） |
| `Nullable` + `ImplicitUsings` + `LangVersion latest` | `:5`、`:7`、`:8` | 全模块开可空 |
| `GeneratePackageOnBuild`（`:10`）+ `<Version>10.0.0</Version>`（`:12`） | — | 与其余六家同；**没有** `GenerateDocumentationFile` |
| Debug → `ProjectReference`（`:23`）／非 Debug → `PackageReference VeloxDev.Core 10.0.0`（`:24`） | — | 与生成器那套双轨同形；包里唯一的依赖是 Core |
| 三个 TFM 一起编 | — | 新写的 BCL 调用必须在 netframework4.6.1 与 netcoreapp3.0 上都存在，否则只在对应的那一次编译里报错（`bin/` 下每个 TFM 一份产物） |

**别用 `bin/`/`obj/` 目录名推支持的框架**：这家的 `bin/Debug/` 下有 `net8.0-windows`/`net10.0-windows` 两个 **csproj 未声明**的目录（与 `memory/modules/VeloxDev.Core.Test` 记的同类现象）；而两个下游 demo 分别是 `net10.0-windows` 与 `net9.0-windows`，它们解析到的是 `net5.0-windows` 那一份资产。

---

## 五、陷阱（带依据）

1. **几个「有 Get/Set、没读者」的公共面。** README 把它们都写成可用，但树里核不到读者：

   | 成员 | 写出处 | 读取处 |
   |---|---|---|
   | `WorkflowCanvasTransformBehavior.GetTransform` | `:34` 公开 getter（值存在 `ConditionalWeakTable<Control, TransformBox>`，`:29`）；写值走 `Apply`（`:65`） | 全仓零命中 —— 基类卡片靠 `IWorkflowSurfaceNodeView.ApplySurfacePosition` 拿投影，不读它 |
   | `WorkflowSlotLayoutBehavior.Get/SetLayoutPropertyName`（`:210`/`:223`）、`Get/SetActualOffsetPropertyName`（`:237`/`:250`） | — | 唯一消费者是私有 `GetActualOffset`（`:743`），而它**自己零调用者** ⇒ 三个公共成员连成的整条链还是死的（README `:176` 却在描述它） |
   | `WorkflowSurfaceBehavior.SetPointerPressSourceName`（`:344`） | 写进 state | 只有自己的 getter 读 |

   ⇒ 这类「加完没人用」的成员是这家的历史包袱；改之前先 grep 调用点。

2. **`ScheduleSync` 在句柄未建时把请求「丢掉」而不是「推迟」。** `ScheduleSync`（`:440-466`）先置 `SyncPending = true`，但若 `!control.IsHandleCreated` 就把它复位成 `false` 后返回（`:465`）—— 请求消失，不是排队。⇒ 在节点卡 ctor（句柄未建）里调 `Refresh`/`SetIsEnabled` 不会立刻量锚点；要同步量必须走 `SyncNow`（`:288-317`，模板与 demo 都在用）。

3. **坐标宿主的两档解析方向相反，而且没有校验。** `ResolveCoordinateHost`：按**名字**时是在 `parentHost` 的**子树**里找；按**类型**时是沿**祖先**链上溯（默认 `typeof(Panel)`）；最后的兜底又是 `parentHost` 本身。⇒ 名字档漏了会静默落到类型档；类型档是**就近匹配**，节点卡与画布之间只要有一个 `Panel`，锚点就会按错的坐标系写下去而**不报错**（demo 因此显式传 `typeof(WorkflowCanvas)`）。

4. **`SyncSlot` 的 `SlotAnchorFromNode` 兜底不可达。** 那一段只在 `coordinateHost` 为 `null` 时执行，而所有调用点传进来的都是 `ResolveCoordinateHost` 的返回值 —— 那个方法**永不返回 null**。⇒ 这家事实上只有 `SlotAnchorFromCanvasLocal` 一条路（这也是唯一正确的路）。

5. **`ThemeValueConverters.cs` 的类名与 `System.Drawing` 撞名，写短名解析到自己。** 文件里凡要用 GDI+ 那个必须全限定：`new System.Drawing.ColorConverter()`（`:328`、`:443`、`:453`）、`new System.Drawing.FontConverter()`（`:460`）；`ObjectConverter` 那条通用路径才用 `TypeDescriptor.GetConverter`（`:465`）。⇒ 在这个命名空间下新写转换器时写短名**不报错**，只是转换结果悄悄不对。13 个类：Double `:6`、Int `:24`、Float `:42`、Point `:60`、PointF `:93`、Size `:126`、SizeF `:159`、Rectangle `:192`、RectangleF `:229`、Padding `:266`、Color `:315`、Font `:359`、Object `:429`。

6. **`NativeWindowStyleHelper` 里 `WS_CLIPCHILDREN` 与 `WS_EX_COMPOSITED` 是同一个数值 `0x02000000`**（`:23-24`），却被喂给 `SetWindowLong` 的两个**不同 index**（普通样式 vs 扩展样式）。⇒ 改这两个常量时把两者当成「可互换的字面量」会静默改错样式位；`ApplyStyle` 只在对应位缺失时才调 `SetWindowPos(SWP_FRAMECHANGED)`（`:191`），所以症状是「有时生效」。`CompositedMaxControlCount = 100`（`:36`，判据 `CountDescendants`，`:133`/`:152`）不是防御性代码 —— 超了就不合成，坑的机制见 `WorkflowSystem/adapters/winforms.md`。

7. **`WorkflowTreeView` 的平移与 AutoScroll 是互斥的两种模型。** 表面把 `PART_ScrollViewer.AutoScroll` 关掉（`:357`）并在画布上维护**签名** `PanOffset`；任何「把 AutoScroll 打开」或「去 `WorkflowSurfaceBehavior` 里补平移」的改动都会让两套平移打架（表面行为只负责**反射读** pan，见 §三）。

---

## 六、入口：我要改 X 先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 工作表表面：chrome / 平移 / 网格 / 标尺 / 池接线 / 布局调度 | `Attached/Workflow/WorkflowTreeView.cs`（派生类只实现两个工厂） |
| 节点卡片 | `Attached/Workflow/WorkflowNodeAttachment.cs`（附加）+ 用户卡片自己的 `OnPaint` |
| 插槽 / 连线图形 | `Attached/Workflow/WorkflowSlotAttachment.cs`、`WorkflowLinkAttachment.cs` |
| 网格/标尺调色板与间距 | `Attached/Workflow/WorkflowGridDecorator.cs` |
| 「item 类型 → 视图」的工厂 | `Attached/Workflow/WorkflowTemplateSelector.cs` / `ViewManager.cs:14` 的 `IWorkflowTemplateSelector` |
| 滚轮缩放 / Ctrl 判定 / 消息过滤器 | `Attached/Workflow/WorkflowSurfaceBehavior.cs`（`SetZoomEnabled` `:170`、过滤器 `:47`） |
| 名字解析、`Refresh` 推给装饰器/小地图的偏移 | 同上（`FindControlByName` `:691`；`Refresh` `:417`） |
| 节点拖拽的落点、坐标宿主、拖拽期重画 | `Attached/Workflow/WorkflowNodeDragBehavior.cs` |
| 插槽锚点写回与同步时机（含唯一的同步入口 `SyncNow`） | `Attached/Workflow/WorkflowSlotLayoutBehavior.cs` |
| 插槽两阶段连接手势 | `Attached/Workflow/WorkflowSlotConnectionBehavior.cs` |
| 视图池 / 物品→控件的工厂 / 上下文注入 | `Attached/Workflow/ViewManager.cs`（挂点 `ViewPool.cs`） |
| 小地图（现在是可继承基类） | `Attached/Workflow/WorkflowMinimapOverlay.cs` |
| Win32 样式、100 个控件的阈值 | `Attached/Workflow/NativeWindowStyleHelper.cs` |
| 线程、优先级、pacer、调度器、采样器 | `PlatformAdapters/` —— 差异与坑见 `TransitionSystem/adapters/winforms.md` |
| 主题字符串 → 值 | `PlatformAdapters/ThemeValueConverters.cs` |
| 「宿主怎么把这些接起来」 | 派生一个 `WorkflowTreeView` 子类的模板：`Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-tree-view/TemplateClass.cs`；两个 demo 在 `Examples/Workflow/WinForms*/` |
| 包结构、TFM、双轨引用 | `VeloxDev.WinForms.csproj` |

---

## 七、这份文件没写的东西

- 七个角色各自要暴露什么成员、附着属性名、`PART_*` 命名约定 —— `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3。
- 这家的平台硬限制（无透明分层、两套平移模型、同步重画、反射接缝…）与逐条坑 —— `memory/modules/WorkflowSystem/adapters/winforms.md`。
- TransitionSystem 侧：`NonPriority`、手写 `Property` 重载、`CreateFramePacer` 条件、`"WindowsFormsSynchronizationContext"` 字符串比较、`PaddingSampler` 的截断 —— `memory/modules/TransitionSystem/adapters/winforms.md`。
- 模板侧：七个条目产出什么形状、`IWorkflowMinimapScrollSource` 的 CS0246 耦合、六份 `ParseColor` —— `memory/modules/Templates/adapters/winforms.md`。
- 「怎么用这套模板搭一个 WinForms 工作流视图」（人面向）—— `skills/veloxdev-create-workflow/references/gui/winforms.md`。
