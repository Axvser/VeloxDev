# VeloxDev.WinForms — 架构

> 代码：`Src/Adapters/VeloxDev.WinForms/`（`WorkflowSystem/AttachedBehaviors/`、`Xaml/` 是 XAML 运行时、`TransitionSystem/` 含一个采样器、`DynamicTheme/ThemeValueConverters.cs`、顶层 `GlobalUsings.cs`）。同目录另有一份 `README.md`。表面由 XAML 根 + `WorkflowSurfaceBehavior` 接管，网格装饰器归模板。
> **当前是 XAML-first 架构**：适配器包含 XAML 运行时；生成器独立在 `Src/Generators/VeloxDev.WinForms.Generator/`；Surface、节点/插槽/连线模板与两个 Demo 的视觉壳由 XAML 定义，低层 attachment/behavior 仍留在 code-behind。包版本 12.0.0。
> **XAML 基础控件库在 `Xaml/Controls/`**（形状在 `Xaml/Controls/Shapes/`）：全部派生 XAML `Control`，属性走 DP，因而 Style/Trigger/Binding/Transition 都可用；原生 WinForms 元素只能接收附加值。**XAML 前缀严格**：无前缀 = VeloxDev（`Controls` → `Controls.Shapes` → `.Xaml`），`native:` = `System.Windows.Forms`，`local:` = 模板/Demo；根元素按同一张表解析。`v:` 仅作 VeloxDev 旧别名保留。XAML 视图 code-behind 的基类写完整命名空间（`VeloxDev.WinForms.Xaml.Controls.UserControl`）。
> **XAML 运行时的扩展面**：`NameScope` + `ElementName`/`RelativeSource`/`x:Static`，绑定侧 `Converter`/`StringFormat`/`FallbackValue`/`TargetNullValue`/`UpdateSourceTrigger`/索引器，`Setter.Value` 可绑定，`ControlTemplate` 带 `TargetType`/`TemplatedParent`/部件/模板触发器，`Panel.ZIndex`，`TriggerActionGroup`/`LoopTime`/`IsAutoReverse`，`ItemsPanel`/`ItemContainerStyle`/固定行高虚拟化，以及 `Menus.cs`/`Popup.cs` 的声明式外壳。运行时细节与边界见 `memory/modules/WinFormsXaml/architecture.md`。
> **计数写法**：`git ls-files 'Src/Adapters/VeloxDev.WinForms/*.cs' 'Src/Adapters/VeloxDev.WinForms/**/*.cs'`。只写 `'.../**/*.cs'` 会漏 —— 这条 pathspec 不匹配目录**本级**的 `.cs`（漏掉 `GlobalUsings.cs`；七家各自都正好漏这处）。
>
> **本模块没有 `adapters/` 子目录，也不该有**：模块名本身就是一个平台，不存在平台轴。它在三条轴上的平台差异分别落在
> `memory/modules/WorkflowSystem/adapters/winforms.md`、`memory/modules/TransitionSystem/adapters/winforms.md`、`memory/modules/Templates/adapters/winforms.md`，本文**指路不抄**。

---

## 一、一个项目、三条轴、外加一层基类

| 轴 | Core 契约 | 本项目落点 | 平台差异记在哪 |
|---|---|---|---|
| WorkflowSystem | 七个视图角色 + 模型事件 | `WorkflowSystem/AttachedBehaviors/` | `WorkflowSystem/adapters/winforms.md` |
| TransitionSystem | 宿主 / 解释器 / 调度器 / 帧 pacer / 采样器 | `TransitionSystem/`（平台类型 + 一个采样器） | `TransitionSystem/adapters/winforms.md` |
| DynamicTheme | `IThemeValueConverter` | `DynamicTheme/ThemeValueConverters.cs`（一组转换器） | DynamicTheme 模块记忆 |

三个只在读这个目录时才会发现的结构事实：

1. **三条轴互不引用，但没有任何编译期阻挡。** 实测 `AttachedBehaviors/` 下零 `Transition`/`Interpolator`/`ThemeManager` 符号、`TransitionSystem/` 下零 `Workflow` 符号 —— 靠的是纪律：`GlobalUsings.cs` 已经把 `VeloxDev.TransitionSystem` 灌进了**每一个**文件（含 `AttachedBehaviors/`）。⇒ 「在这家写了一条跨轴的调用」不会被拦下，七家都维持着零引用。
2. **命名空间全部寄生在 Core 上，唯一例外的命名空间还和 Core 撞了名。** 唯一的采样器在 `VeloxDev.Adapters.NativeSamplers`（`TransitionSystem/Samplers/PaddingSampler.cs` 的命名空间），而 **Core 自己的采样器在 `VeloxDev.TransitionSystem.NativeSamplers`**（`Src/Core/VeloxDev.Core/TransitionSystem/NativeSamplers/ColorSampler.cs` 的命名空间）—— 两个不同命名空间。⇒ 写 Core 那一支的 `using` 引不到这家唯一自带的采样器（AUTO TEST 用的是前者：`Src/Test/AutoTest/Samplers/WinFormsEntries.cs`）。
3. **`VeloxDev.WorkflowSystem.AttachedBehaviors` 在 Core 里不存在**：七家各声明一份同名命名空间。XAML 平台靠 `assembly=` 消歧，C# 宿主只能写全名或别名。

**DynamicTheme 这条轴在这家没有消费者**：`Examples/Theme/` 下只有 Avalonia/WPF 两族目录（无 WinForms），`ThemeManager.SetPlatformInterpolator` 的仓内调用点无一处是本平台。这些转换器没有任何 demo/测试碰过 —— 它是三条轴里唯一「有实现、零使用」的。

---

## 二、这套适配器现在「给」什么：整套表面

表面引擎是 `WorkflowSurfaceBehavior`，**没有树基类**。模板/Demo 的 tree 是普通 `UserControl`，在根上挂 `behaviors:WorkflowSurfaceBehavior.*` 附着属性；行为按 `PART_*` 名称解析部件，接管 pan/zoom、输入路由、ViewPool、ruler/minimap、布局调度与 teardown。网格装饰器与小地图是**模板角色**，适配器只留 `IWorkflowGridDecorator` / `IWorkflowMinimapOverlay` 契约（没有 `WorkflowGridDecorator` 类）。

| 类型 | 形态 | 宿主/模板做什么 | 关键成员 |
|---|---|---|---|
| `WorkflowSurfaceBehavior` | 附着行为（静态 `RegisterAttached`） | tree XAML 根上设 `IsEnabled` / `ZoomEnabled` / `TemplateSelector` 与五个 `*Name` | `EnsureParts` / `EnsurePool` / `ApplyPan` / `WorkflowTree` / `LinkMenuBuilder` |
| `WorkflowNodeAttachment` | 附到用户的 `Control` 上 | 用户自己的控件 + `Attach(this)`，自己在 `OnPaint` 里画整张卡 | `Node`/`Title`/`Collapse`/`SurfacePanOffset`/`SurfaceContentOffset`；`ApplySurfacePosition`；事件 `Rebound`/`TitleChanged`/`CollapseChanged`/`AnchorChanged`/`Moving`…`Deleted` |
| `WorkflowSlotAttachment` | 附到用户的 `Control` 上 | 用户自己的控件 + `Attach(this)`，自己在 `OnPaint` 里画 | `Slot`/`SlotPath`/`PathViewBox`/`SlotBackground`/`StandbyColor`/`BorderColor`/`IconPath`/`GlyphColor`；事件 `ChannelChanging`/`ChannelChanged`；`SvgPathParser` |
| `WorkflowLinkAttachment` | 附到用户的 `Control` 上 | 视图构造里 `WorkflowLinkAttachment.Attach(this)`，自己在 `OnPaint` 里画 | `LineColor`/`Thickness`/`PullMinimum`/`SurfaceBackground`/`Curve`/`Link`；`Paint(Graphics)`/`Bind(link)`/`Attach`/`For`；事件 `PointerEntered`/`PointerLeft`/`PointerPressed`/`PointerReleased` |
| `VeloxDev.WinForms.Xaml.TemplateSelector` | 通用选择器基类 | 派生类用 `DataTemplate` 属性按 item 类型选模板；tree 的资源按 `{StaticResource}` 引用 | `SelectTemplate(object?)` → `DataTemplate?` |
| `WorkflowNodeRowsBehavior` / `WorkflowNodeTitleBehavior` / `WorkflowNodeViewBehavior` | 节点侧的三个附着行为 | 节点 XAML 声明 `PART_Card` / `PART_Header` / `PART_Title` / `PART_OutputSlots` | 行重建、标题缩放、拖动/槽布局接线 |
| `WorkflowMinimapOverlay` | `Panel, IWorkflowMinimapOverlay, IWorkflowMinimapScrollSource` | 模板派生，只给调色板 | 定位、拖拽映射、`ScrollOffsetX/Y` |

**支撑件：**

- `IWorkflowSurfaceNodeView`：`ApplySurfacePosition(Point panOffset, Offset contentOffset)` —— 表面不能按类型认用户的卡片，于是把投影交给卡片自己放。
- `IWorkflowMinimapScrollSource`：`ViewportScrollRequested` 事件；**故意不进 Core 的 `IWorkflowMinimapOverlay`**（那个要框架中立），由表面行为订阅。
- `ModelChangeRelay`（`internal`）：把「订阅、退订、`InvokeRequired` 编组」收敛成一份，三个视图控件共用。
- `WorkflowRulerOverlayForm`：`IWorkflowGridDecorator.RulerBand` 驱动的分层标尺浮层；由表面行为在句柄建立后创建。
- `WorkflowSurfaceColors`（解析 `#RRGGBB`/`#AARRGGBB`/颜色名）、`WorkflowSurfaceGraphics`（圆角矩形）、`WorkflowSurfaceGrid`（大/次网格线判定 + 标尺标签格式）。

---

## 三、宿主怎么把它接起来：XAML 根 + 附着行为 + pull

**XAML 侧有 DP/附加属性；适配器内部状态仍存在 `ConditionalWeakTable<Control, State>` 里。** 没有跨模块的 `ModuleInitializer` / `[assembly:`；XAML 运行时由 `Xaml/DependencyProperty` 提供 DP，`WorkflowSurfaceBehavior` 用 `RegisterAttached` 暴露接线面。

**名字仍是唯一的接线语言。** 表面行为按 `Control.Name` 解析 `PART_*`，解析器 `FindControlByName`（`WorkflowSlotLayoutBehavior`）：**递归遍历宿主子树 + `Ordinal` 比较**，取第一个命中。⇒ 与 WPF 的 `NameScope` 不同：只管宿主子树、跨容器/非子孙一律找不到、**找不到不抛也不报**，只在那条数据推送处静默跳过。`x:Name` 只生成字段，不会写原生 `Name`；模板里的 `Name="PART_*"` 才是契约。

**三个开关各做什么：**

| 调什么 | 真的做了什么 |
|---|---|
| `ScrollViewerName` / `CanvasName` / `GridDecoratorName` / `MinimapOverlayName` / `PointerPressSourceName` | 配置变化时 `EnsureParts` 立即解析并接线；ScrollViewer/Canvas/GridDecorator 还会顺带加 `WS_CLIPCHILDREN` |
| `IsEnabled` | 启动/停止表面生命周期；加 `WS_CLIPCHILDREN` 与按子窗口数量启用的 `WS_EX_COMPOSITED`（`NativeWindowStyleHelper`） |
| `ZoomEnabled` | 挂 `element.MouseWheel` **加上** `Application.AddMessageFilter(state)` —— `SurfaceState : IMessageFilter`；关掉时成对摘除。过滤器条件是 `WM_MOUSEWHEEL` + `ModifierKeys != Keys.Control` + 「消息目标沿父链能找到 zoom-enabled 宿主」 |

- **`Refresh(Control)` 是唯一的重驱动入口**，第一行被 `IsEnabled` 门住；它把当前 scroll/content 偏移推给 GridDecorator/MinimapOverlay、调 `SetVirtualizeInset`，循环末尾 `PerformLayout()` + `Invalidate()`，而 `Update()`（同步重画）**只在 `host.Capture` 为真时**调。
- **平移在 `WorkflowSurfaceBehavior.ApplyPan` / `ApplyPanCore`**：`state.PanOffset` 持有视图世界偏移，直接推给画布、GridDecorator、Minimap 和每个池化视图；卡片经 `IWorkflowSurfaceNodeView.ApplySurfacePosition` 自己落位。
- **视图池由赋值触发**：`EnsurePool` 把 `tree.GetHelper().VisibleItems` 与 `TemplateSelector`（或内置的 `NodeViewType`/`LinkViewType` 反射选择器）交给 `ViewPool`；池本身是 Core 的 `ViewPool`，由 `ViewHosting/ViewScheduler` 经 `Control.BeginInvoke` 异步分批物化。
- **滚动宿主只当视口用**：`PART_ScrollViewer.AutoScroll = false`，因为平移直接改画布、且 WinForms 的 AutoScroll 位置被夹在 `>= 0`，只允许往右下平移。

---

## 四、`GlobalUsings.cs` 与 `VeloxDev.WinForms.csproj`

**`GlobalUsings.cs` 三行，与 WPF/WinUI 逐字相同**（`diff` 无输出）：`global using` `VeloxDev.TransitionSystem` / `VeloxDev.TransitionSystem.Abstractions` / `VeloxDev.Threading`。

- 这就是 `TransitionSystem/` 里不写 `using` 却能用 `InterpolatorCore`/`ISampler` 的原因（它们在 `...Abstractions` 里）。
- **它不含 `VeloxDev.WorkflowSystem`** ⇒ `AttachedBehaviors/` 里需要 Core 契约的文件各自写这一行（基类文件大多靠命名空间嵌套免了它）。
- **`global using` 是编译期的，不随包/`ProjectReference` 传给消费者** —— 宿主要用 `Transition<T>` 仍得自己写 `using VeloxDev.TransitionSystem;`。

**csproj 里影响代码本身的条件**（`VeloxDev.WinForms.csproj`）：

| 事实 | 元素 | 后果 |
|---|---|---|
| `<TargetFrameworks>netframework4.6.1;net5.0-windows;netcoreapp3.0;net8.0-windows` | `TargetFrameworks` | 七家里只有这家与 WPF 是这个四元组 |
| `UseWindowsForms` + `SuppressTfmSupportBuildWarnings` | 同名属性 | 四元组里 `netcoreapp3.0` 不带 `-windows`，靠后者压掉兼容告警（WPF 同形） |
| `Nullable` + `ImplicitUsings` + `LangVersion latest` | 同名属性 | 全模块开可空 |
| `GeneratePackageOnBuild` + `<Version>12.0.0</Version>` | 同名属性 | 与其余六家同；**没有** `GenerateDocumentationFile` |
| Debug → `ProjectReference`／非 Debug → `PackageReference VeloxDev.Core 12.0.0` | 两个引用项 | 与生成器那套双轨同形；包里唯一的依赖是 Core |
| 四个 TFM 一起编 | — | 新写的 BCL 调用必须在 netframework4.6.1 与 netcoreapp3.0 上都存在，否则只在对应的那一次编译里报错（`bin/` 下每个 TFM 一份产物） |

**别用 `bin/`/`obj/` 目录名推支持的框架**：`bin/Debug/` 里可能残留旧的 `net10.0-windows` 目录；声明的是四元组，两个下游 demo 都是 `net10.0-windows`，它们解析到 `net8.0-windows` 那份资产。旧 TFM 的兼容层在 `Xaml/Compat.cs`：IsExternalInit、UnconditionalSuppressMessageAttribute、Clamp/IsFinite/TickCount64。

---

## 五、陷阱（带依据）

1. **`WorkflowCanvasTransformBehavior.GetTransform` 有 Get/Set、没读者。** README 写成可用，但全仓零命中：公开 getter 的值存在 `ConditionalWeakTable<Control, TransformBox>`，写值走 `Apply`；卡片靠 `IWorkflowSurfaceNodeView.ApplySurfacePosition` 拿投影，不读它。⇒ 改之前先 grep 调用点。

2. **`ScheduleSync` 在句柄未建时把请求「丢掉」而不是「推迟」。** `ScheduleSync` 先置 `SyncPending = true`，但若 `!control.IsHandleCreated` 就把它复位成 `false` 后返回 —— 请求消失，不是排队。⇒ 在节点卡 ctor（句柄未建）里调 `Refresh`/`SetIsEnabled` 不会立刻量锚点；要同步量必须走 `SyncNow`（模板与 demo 都在用）。

3. **坐标宿主的两档解析方向相反，而且没有校验。** `ResolveCoordinateHost`：按**名字**时是在 `parentHost` 的**子树**里找；按**类型**时是沿**祖先**链上溯（默认 `typeof(Panel)`）；最后的兜底又是 `parentHost` 本身。⇒ 名字档漏了会静默落到类型档；类型档是**就近匹配**，节点卡与画布之间只要有一个 `Panel`，锚点就会按错的坐标系写下去而**不报错**（demo 因此显式传 `typeof(WorkflowCanvas)`）。

4. **`SyncSlot` 的 `SlotAnchorFromNode` 兜底不可达。** 那一段只在 `coordinateHost` 为 `null` 时执行，而所有调用点传进来的都是 `ResolveCoordinateHost` 的返回值 —— 那个方法**永不返回 null**。⇒ 这家事实上只有 `SlotAnchorFromCanvasLocal` 一条路（这也是唯一正确的路）。

5. **`ThemeValueConverters.cs` 的类名与 `System.Drawing` 撞名，写短名解析到自己。** 文件里凡要用 GDI+ 那个必须全限定：`new System.Drawing.ColorConverter()`（三处）、`new System.Drawing.FontConverter()`；`ObjectConverter` 那条通用路径刻意**不用** `TypeDescriptor.GetConverter`（注释写明），改走显式转换表。⇒ 在这个命名空间下新写转换器时写短名**不报错**，只是转换结果悄悄不对。

6. **`NativeWindowStyleHelper` 里 `WS_CLIPCHILDREN` 与 `WS_EX_COMPOSITED` 是同一个数值 `0x02000000`**，却被喂给 `SetWindowLong` 的两个**不同 index**（普通样式 vs 扩展样式）。⇒ 改这两个常量时把两者当成「可互换的字面量」会静默改错样式位；`ApplyStyle` 只在对应位缺失时才调 `SetWindowPos(SWP_FRAMECHANGED)`，所以症状是「有时生效」。`CompositedMaxControlCount = 100`（判据 `CountDescendants`）不是防御性代码 —— 超了就不合成，坑的机制见 `WorkflowSystem/adapters/winforms.md`。

7. **表面的平移与 AutoScroll 互斥。** `WorkflowSurfaceBehavior` 把 `PART_ScrollViewer.AutoScroll` 关掉并维护 `state.PanOffset`；打开 AutoScroll 或另起一套 pan 会和它打架（见 §三）。

---

## 六、入口：我要改 X 先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 工作表表面：chrome / 平移 / 池接线 / 布局调度 / 标尺浮层 | `WorkflowSystem/AttachedBehaviors/WorkflowSurfaceBehavior.cs`（模板只在 XAML 根上挂属性） |
| 节点卡片 | `WorkflowSystem/AttachedBehaviors/WorkflowNodeAttachment.cs`（附加）+ 用户卡片自己的 `OnPaint` |
| 插槽 / 连线图形 | `WorkflowSystem/AttachedBehaviors/WorkflowSlotAttachment.cs`、`WorkflowLinkAttachment.cs` |
| 网格/标尺调色板与间距 | 模板条目 `workflow-grid-decorator/TemplateClass.cs`（含同文件的 `SurfaceCanvas`） |
| 「item 类型 → 视图」的选择器 | `Src/Adapters/VeloxDev.WinForms/Xaml/TemplateSelector.cs`（通用基类）+ 模板里的派生类 |
| 滚轮缩放 / Ctrl 判定 / 消息过滤器 | `WorkflowSystem/AttachedBehaviors/WorkflowSurfaceBehavior.cs`（`SetZoomEnabled`、`SurfaceState.OnMessage`） |
| 名字解析、`Refresh` 推给装饰器/小地图的偏移 | 同上（`FindControlByName` 在 `WorkflowSlotLayoutBehavior.cs`；`Refresh` 在 `WorkflowSurfaceBehavior.cs`） |
| 节点拖拽的落点、坐标宿主、拖拽期重画 | `WorkflowSystem/AttachedBehaviors/WorkflowNodeDragBehavior.cs` |
| 插槽锚点写回与同步时机（含唯一的同步入口 `SyncNow`） | `WorkflowSystem/AttachedBehaviors/WorkflowSlotLayoutBehavior.cs` |
| 插槽两阶段连接手势 | `WorkflowSystem/AttachedBehaviors/WorkflowSlotConnectionBehavior.cs` |
| 视图池 / 物品→控件的工厂 / 上下文注入 | `WorkflowSystem/AttachedBehaviors/ViewPool.cs`（挂点）+ `WorkflowSystem/ViewHosting/`（`ViewSourcePipeline`、`ViewContainer`、`ViewFactory`） |
| 小地图（现在是可继承基类） | `WorkflowSystem/AttachedBehaviors/WorkflowMinimapOverlay.cs` |
| Win32 样式、100 个控件的阈值 | `WorkflowSystem/AttachedBehaviors/NativeWindowStyleHelper.cs` |
| 线程、优先级、pacer、调度器、采样器 | `TransitionSystem/` —— 差异与坑见 `TransitionSystem/adapters/winforms.md` |
| 主题字符串 → 值 | `DynamicTheme/ThemeValueConverters.cs` |
| 「宿主怎么把这些接起来」 | `Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-tree-view/TemplateClass.xaml` + `.xaml.cs`；两个 demo 在 `Examples/Workflow/WinForms Trimmed/` 与 `Examples/Workflow/WinForms/`，视觉壳均已 XAML 化 |
| 包结构、TFM、双轨引用 | `VeloxDev.WinForms.csproj` |

---

## 七、这份文件没写的东西

- 七个角色各自要暴露什么成员、附着属性名、`PART_*` 命名约定 —— `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3。
- 这家的平台硬限制（无透明分层、平移与 AutoScroll 互斥、同步重画、反射接缝…）与逐条坑 —— `memory/modules/WorkflowSystem/adapters/winforms.md`。
- TransitionSystem 侧：`NonPriority`、手写 `Property` 重载、`CreateFramePacer` 条件、`"WindowsFormsSynchronizationContext"` 字符串比较、`PaddingSampler` 的截断 —— `memory/modules/TransitionSystem/adapters/winforms.md`。
- 模板侧：各条目产出什么形状、七个条目的编译期依赖、`x:Name` 与 `Name` 的分工、SvgPathParser、连线雕窗 —— `memory/modules/Templates/adapters/winforms.md`。
- 「怎么用这套模板搭一个 WinForms 工作流视图」（人面向）—— `skills/veloxdev-create-workflow/references/gui/winforms.md`。

## Demo 工作台外壳（示例侧）

`Examples/Workflow/WinForms/Demo/Form1.cs` 的 `BuildWorkbenchFrame` 在 Designer 的 `splitContainer` 外面套 32px 标题条 + 24px 状态条，`BuildSidebar` 再把 `Panel1` 换成 48px 活动栏 + 侧栏；`Panel1/2.Padding=Empty`、`SplitterWidth=4`、`SplitterDistance=320` 是工作台版式。**全部工作流视图在 `Views/Workflow/`、命名空间 `Demo.Views.Workflow`**：表面是 `WorkflowCanvas.xaml` + `.xaml.cs`，节点卡 `WorkflowNodeCard.xaml` + `.xaml.cs`，网格/`SurfaceCanvas` 在 `GridDecorator.cs`，连线和端口在 `LinkView.cs` / `SlotView.cs`，HUD/小地图在 `InfoOverlay.cs` / `MinimapOverlay.cs`，色板与自绘原语在 `CardTheme.cs` / `FramePanel.cs` / `GhostButton.cs`；`Views/Workbench/` 只放外壳件。WinForms 没有 `BorderThickness`/圆角属性，分隔线用 1px `Panel`/自绘。

- **Designer 只保留仍在用的控件**：`splitContainer`、五个动作按钮（reload/undo/redo/save/select）、权限模式标签与选择器、Agent 日志与输入、执行日志、`workflowSurfaceControl`。工作台在 `BuildExplorerPage` / `BuildAgentPage` / `BuildOutputPanel` / `BuildMainArea` 里重新挂载它们。
- **两个 demo 的视图选择器同形**：tree/canvas 的 `Resources` 声明 `NodeTemplate`/`LinkTemplate` 两个 `DataTemplate` 与 `<local:CustomTemplateSelector x:Key="WorkflowTemplateSelector" … />`，根上 `TemplateSelector="{StaticResource WorkflowTemplateSelector}"`；派生类继承通用 `VeloxDev.WinForms.Xaml.TemplateSelector` 并按 item 类型返回模板。键/滚轮策略（Delete、Ctrl+Z/Y、Shift+滚轮横滚）订在树的 `IInputEvents` 上，两边一致。

- **四区外壳**：主区（`流程图`/`输出`）+ 348px 检查器（`属性`/`AI 助手`）；运行侧栏由 `Views/Workflow/RunManagerPanel.cs` 绑 `TreeViewModel.Runs`，检查器由 `Views/Workflow/WorkflowInspectorPanel.cs` 绑 `SelectedElement`/`CompiledStructure`，子代理面板是 `SubAgentPanel.cs`；`Views/Workbench/` 放共享外壳件（`WorkbenchPalette` / `StatusBarStrip` / `WorkbenchTabButton`）。
- **标题条有菜单与命令中心**：`BuildTitleStrip` 用 `GhostButton` 做“文件/编辑/视图/帮助”和居中的“搜索命令 Ctrl+Shift+P”；下拉是 `Views/Workbench/DarkPopupMenu`（无边框 `Form`，自己画 panel/hover/快捷键列），不是 `ContextMenuStrip`。**实测 2026-11-18**：`ContextMenuStrip` 即使设了自定义 `ToolStripRenderer`、`ToolStripManager.Renderer` 和 `Application.SetColorMode(SystemColorMode.Dark)`，弹层仍是浅色面板 + 浅色字；换自己的 Form 才稳定。原生窗口边框继续负责最小化/最大化/关闭，不画假按钮。
- **Explorer 的动作是 `Segoe Fluent Icons` 的 24px 图标按钮**（保存/加载/撤销/重做/重载示例），与 Avalonia 面板头同节奏。
- **元素表是自建的 `Views/Workflow/ElementRowList.cs`**：每行是 Label + 定位/删除两个 `Segoe Fluent Icons` 图标按钮（24px 行高）；选中行的背景切到 `CardTheme.Hover` 并写 `TreeViewModel.SelectedElement`。`Dock=Top` 的控件是后加的在上，所以 `SetEntries` 要 **反向添加** 才保持 `#0` 在顶部。
- **没有 XAML 双向选择绑定**：元素列表与运行行都用点击事件写 `TreeViewModel.SelectedElement` / `Runs.Selected`，列表行本身只读；这与 Avalonia 的 `SelectedItem` 绑定是同一状态的两种平台写法。
- **脚本化验证**：WinForms 的坐标点击在 150% DPI 环境下不可靠，用 UI Automation 按 `Name=Compile/Run` 的 `InvokePattern`，比 `mouse_event` 稳。
