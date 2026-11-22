# VeloxDev.WinForms — 扩展

> 配套阅读：`architecture.md`（本目录，「这家给什么、怎么挂上去」）。
> 平台差异与坑见 `memory/modules/WorkflowSystem/adapters/winforms.md`、`memory/modules/TransitionSystem/adapters/winforms.md`、`memory/modules/Templates/adapters/winforms.md`；七个角色的职责与附着属性名见 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3。本文写**在这个项目里动手**的部分。

---

## 一、扩展点地图

这家**没有注册表、没有分析器、没有 `Initialize()`** —— 「加东西」= 加文件 + 在 XAML 根上挂附着属性 / 在宿主侧调静态 `Get`/`Set`。下表是每个方向真正的挂点：

| 我想加 | 挂点 | 位置 |
|---|---|---|
| 一张新的工作流表面 | **XAML 根 + `WorkflowSurfaceBehavior` 附着属性**：根上设 `IsEnabled`/`ZoomEnabled`/`TemplateSelector` 与五个 `*Name`；部件按 `PART_*` 命名 | `WorkflowSystem/AttachedBehaviors/WorkflowSurfaceBehavior.cs`；示例 `Examples/Workflow/WinForms Trimmed/Demo/Views/Workflow/TreeView.xaml` |
| 一种新的节点卡片 | **`WorkflowNodeAttachment.Attach(this)`**，卡片自绘并按 `PART_*` 名接线 | `WorkflowSystem/AttachedBehaviors/WorkflowNodeAttachment.Attach` |
| 一种新的插槽图形 | **`WorkflowSlotAttachment.Attach(this)`** | `WorkflowSystem/AttachedBehaviors/WorkflowSlotAttachment.Attach` |
| 一种新的连线图形 | **自己的控件 + `WorkflowLinkAttachment.Attach(this)`**，在 `OnPaint` 里画 | `WorkflowLinkAttachment.cs` |
| 换网格/标尺外观 | 模板条目实现 `IWorkflowGridDecorator`，设调色板与间距；宿主用 `GridDecoratorName` 接上 | 模板 `workflow-grid-decorator/TemplateClass.cs`（含 `SurfaceCanvas`） |
| 「item 类型 → 视图」的选择器 | 派生 `VeloxDev.WinForms.Xaml.TemplateSelector`（`DataTemplate` 属性 + `SelectTemplate`） | `Xaml/TemplateSelector.cs` + 模板里的派生类 |
| 一个新的附着行为 | 新建 `public static class` + `ConditionalWeakTable<Control, State>` + 静态 `Get`/`Set`，命名空间写 `VeloxDev.WorkflowSystem.AttachedBehaviors` | `WorkflowSystem/AttachedBehaviors/` 下新文件；若需要 `Refresh` 推数据，改 `WorkflowSurfaceBehavior.Refresh` |
| 一个新的「名字」通道 | 状态里加 `string? XxxName` + 用 `FindControlByName` 解析 | `WorkflowSlotLayoutBehavior.FindControlByName` |
| 一个新的 Win32 补偿 | `NativeWindowStyleHelper` 加常量 + 方法 | `NativeWindowStyleHelper` 的样式常量与 `ApplyStyle`；**必须同时加调用点**（现有几处） |
| 一个采样器 | `TransitionSystem/Samplers/XxxSampler.cs`，命名空间 `VeloxDev.Adapters.NativeSamplers` | 登记在 `Interpolator` 的静态构造；且必须给 AUTO TEST 加表项（§四·3）；要能被 `Transition` 面用可能还要加 `Property` 重载（`Transition.cs`） |
| 一个主题转换器 | `DynamicTheme/ThemeValueConverters.cs` 里加一个 `IThemeValueConverter` 类 | **无注册**（按目标类型键）。但这三个平台上无 demo 可验（`Examples/Theme/` 没有 WinForms） |
| 一个自绘小地图 | 派生 `WorkflowMinimapOverlay`（已实现 `IWorkflowMinimapOverlay` + `IWorkflowMinimapScrollSource`） | `WorkflowSystem/AttachedBehaviors/WorkflowMinimapOverlay` |

---

## 二、官方做法 vs 看着能编译、但错的捷径

下面每一条都能编译通过，有些跑起来还「像是好的」。

| # | 错的捷径 | 为什么错 | 官方做法 | 依据 |
|---|---|---|---|---|
| 1 | 打开 `PART_ScrollViewer.AutoScroll` 或另起一套 pan | 表面用 `state.PanOffset` 直接推画布与池化视图；AutoScroll 位置被夹在 `>= 0`，两套会打架 | 平移只走 `WorkflowSurfaceBehavior.ApplyPan` / `ApplyPanCore` | `WorkflowSurfaceBehavior.cs` 的 `ApplyPan*`；`architecture.md` §三 |
| 2 | 在宿主里自己 `new` 网格装饰器/小地图/连线控件 | 网格装饰器与小地图是模板角色（适配器只留契约）；自造会与表面/池重复一套 | 模板条目实现 `IWorkflowGridDecorator`、派生 `WorkflowMinimapOverlay`，宿主用 `GridDecoratorName`/`MinimapOverlayName` 接上 | `IWorkflowGridDecorator` / `IWorkflowMinimapOverlay` 契约 |
| 3 | 在节点卡 ctor 里调 `Refresh`/`SetIsEnabled`，指望锚点当场量好 | `ScheduleSync` 在 `!control.IsHandleCreated` 时把 `SyncPending` 复位后返回 —— 请求被**丢掉**，不是排队 | 要同步量就调 `WorkflowSlotLayoutBehavior.SyncNow(control)` | `WorkflowSlotLayoutBehavior.ScheduleSync` / `SyncNow` |
| 4 | 自绘画布用 `WorkflowCanvasTransformBehavior.GetTransform(host)` 拿平移量 | 写进去了，**全仓零读者**（写值走 `Apply`）。基类卡片走 `IWorkflowSurfaceNodeView.ApplySurfacePosition`，表面走自己的 pan 字段 | 你既然是自绘，pan 就在你自己手里，不必绕这一圈 | `WorkflowCanvasTransformBehavior.GetTransform` / `Apply` |
| 5 | `using VeloxDev.TransitionSystem.NativeSamplers;` 想用这家唯一的采样器 | 这家在 `VeloxDev.Adapters.NativeSamplers`；`VeloxDev.TransitionSystem.NativeSamplers` 是 **Core 自己那组采样器**的命名空间 | 用 `VeloxDev.Adapters.NativeSamplers` | `Samplers/PaddingSampler.cs` 的命名空间；`Src/Core/VeloxDev.Core/TransitionSystem/NativeSamplers/ColorSampler.cs` 的命名空间 |
| 6 | 在 `AttachedBehaviors/` 里直接用 `Transition<T>`/`Interpolator`（因为编译得过） | 编译得过是因为 `GlobalUsings.cs` 把 `VeloxDev.TransitionSystem` 灌进了每个文件 —— 但「三条轴在程序集内互不引用」是七家共同守的纪律，破了不会报错 | 保持零引用；跨轴的事在宿主侧接 | `GlobalUsings.cs`；`architecture.md` §一 |
| 7 | 照 WPF 的心智模型期待名字作用域 / 重名报错 | 这家是**递归子树顺序查找 + `Ordinal` 比较**，取第一个命中；跨容器、非子孙一律找不到，而且**不抛不报** | 名字只挂在宿主子树里，且保证唯一 | `WorkflowSlotLayoutBehavior.FindControlByName` |
| 8 | 以为 `SetIsEnabled` 会订阅事件、或有早退 | 它只做两件事：置位 + 加 `WS_CLIPCHILDREN`/`WS_EX_COMPOSITED`；订阅是各 `Set*Name` 干的 | 别靠 `SetIsEnabled` 反复调来「重新接入」 | `WorkflowSurfaceBehavior.SetIsEnabled` |
| 9 | 传 `typeof(Panel)` 当坐标宿主，而节点卡与画布之间还夹着一层 `Panel` | 类型档是**就近匹配、无校验**，会静默按错的坐标系写锚点 | 能确定类型就传真正画布的类型（demo 传 `typeof(WorkflowCanvas)`） | `WorkflowSlotLayoutBehavior.ResolveCoordinateHost`；`WorkflowNodeDragBehavior`（同形） |
| 10 | 给节点卡片加 Dispose 逻辑却不用助手 | 卡片是子控件树，直接 `Dispose` 父控件会漏掉订阅（`ModelChangeRelay`）与子控件 | 用 `DisposeChildren(parent)` 收尾 | `WorkflowNodeAttachment.DisposeChildren` |
| 11 | 以为隐藏的视图会被 `Remove` | `ViewContainer` 只把视图 `Visible = false` 留在画布 `Controls` 里（移除触发昂贵重排） | 遍历子控件时不能假设「看不见 = 不在」 | `ViewHosting/ViewContainer.cs` |

---

## 三、步骤清单

### 3.1 搭一张工作流表面（这家最常见的扩展）

1. **根节点**：用 `UserControl`（或任意 XAML 根），在根上设 `behaviors:WorkflowSurfaceBehavior.IsEnabled="True"`、`ZoomEnabled`、`TemplateSelector`，并用 `ScrollViewerName`/`CanvasName`/`GridDecoratorName`/`MinimapOverlayName`/`PointerPressSourceName` 指向部件。
2. **部件名**：目标控件写原生 `Name="PART_*"`；`x:Name` 只生成字段，不会写原生 `Name`。
3. **卡片/连线**：节点卡片实现 `IWorkflowSurfaceNodeView`（平移时表面调 `ApplySurfacePosition`）；连线默认由 `WorkflowLinkLayer` 统一画，per-link 视图才挂 `WorkflowLinkAttachment`。
4. **网格/小地图**：模板条目分别实现 `IWorkflowGridDecorator`、派生 `WorkflowMinimapOverlay`，再按名字接上。
5. **模板/派生联动**：`Src/Templates/VeloxDev.WinForms.Templates/working/content/` 下对应条目，以及两个 demo。

### 3.2 加一个采样器

1. `TransitionSystem/Samplers/XxxSampler.cs`，`namespace VeloxDev.Adapters.NativeSamplers`，实现 `ISampler` 那几个方法（`PaddingSampler.cs` 是唯一先例）。
2. 在 `TransitionSystem/Interpolator.cs` 的静态构造里 `RegisterInterpolator(typeof(T), new XxxSampler());`。
3. 要能被 `Transition` 面用上，可能需要在 `TransitionSystem/Transition.cs` 加 `Property` 重载（先是泛型 `Property<TValue>` 兜底，其后是一组手写重载，`System.Numerics` 那几个在 `#if !NETSTANDARD2_0` 里）。
4. **必须**给 `Src/Test/AutoTest/Samplers/WinFormsEntries.All` 加一条 `EntryFactory.Create<...>`：`SamplerCoverageTests` 会反射产品程序集里所有 `ISampler` 与注册表比对，**漏了就直接失败**。
5. 闭式解怎么定、`SamplerRule` 该选哪个 —— 见 `memory/modules/TransitionSystem/extension.md`；采样器在本平台的行为差异见 `TransitionSystem/adapters/winforms.md`。

### 3.3 加一个主题转换器

1. 在 `DynamicTheme/ThemeValueConverters.cs` 里加一个 `IThemeValueConverter` 类（既有若干先例）。**按目标类型键，无需注册。**
2. 命名冲突要当场处理：本文件与 `System.Drawing` 多个同名类型共存，凡要用框架那个必须全限定（`System.Drawing.ColorConverter` / `System.Drawing.FontConverter` 就是被逼出来的写法）。
3. 转换失败一律静默返回 `null` 是这家（也是 DynamicTheme）的刻意姿态，别改成抛。
4. 验证方式：**这三个平台上没有 Theme demo**（`Examples/Theme/` 只有 Avalonia/WPF），只能自己起宿主验。

### 3.4 加一个 Win32 补偿

1. 在 `WorkflowSystem/AttachedBehaviors/NativeWindowStyleHelper.cs` 加常量与方法（`internal static`，不对外）。
2. `SetWindowLong` 的 index（普通样式 vs 扩展样式）不要凭常量值猜 —— 这家两个常量**值相同**（`WS_CLIPCHILDREN` / `WS_EX_COMPOSITED` 都是 `0x02000000`）。
3. 至少加一个调用点，并想清楚「句柄还没建」的情况：`ApplyStyle` 只在位缺失时才动 `SetWindowPos`，而 `HandleCreated` 处有重挂（`EnsureClipChildren` / `EnsureComposited`）。

### 3.5 加一个附着行为（静态 `Get`/`Set` 形态）

1. `WorkflowSystem/AttachedBehaviors/WorkflowXxxBehavior.cs`，`namespace VeloxDev.WorkflowSystem.AttachedBehaviors;`，类写 `public static class`。
2. `private sealed class XxxState` + `static readonly ConditionalWeakTable<Control, XxxState> States`（照 `WorkflowSurfaceBehavior` 的 `GetState`）。
3. 每个属性一对 `public static T? GetXxx(Control element)` / `SetXxx(Control element, T? value)`；`element is null` 抛 `ArgumentNullException`（全模块一致）。
4. 若宿主用**名字**指认目标控件，走 `FindControlByName` 那一套（私有，`WorkflowSlotLayoutBehavior.FindControlByName` —— 这家没有共享的名字解析工具）。
5. 要不要进 `Refresh`：如果适配器需要在每次刷新周期把值推给你的行为，改 `WorkflowSurfaceBehavior.Refresh`；否则你的行为自己订阅。
6. **零注册**：加完就能被宿主调用（这也是为什么这家最容易长出「有 API 没读者」的成员，见 `architecture.md` §五·1）。

---

## 四、联动清单（改一个 X 时必须同步修改的所有位置）

**漏一处通常不报错，只是静默少一个能力或文档说谎。**

### 4.1 改任何 `Set*` / `Get*` 的名字或语义（这家最重的联动）

- [ ] **本模块 README**（`Src/Adapters/VeloxDev.WinForms/README.md`，逐表列 API；它自己就是消费者入口）
- [ ] **各模板条目**（`Src/Templates/VeloxDev.WinForms.Templates/working/content/*/`；`tree-view` 是 `UserControl` + `WorkflowSurfaceBehavior`，画布上挂 `WorkflowLinkLayer`；`node-view` 用 `WorkflowNodeAttachment.Attach`，`slot-view` 用 `WorkflowSlotAttachment.Attach`；`link-view` 是 per-link 可选路径；`grid-decorator`/`minimap-overlay`/`template-selector` 各有形状）
- [ ] **两个 Workflow demo**：`Examples/Workflow/WinForms/Demo/` 与 `Examples/Workflow/WinForms Trimmed/Demo/`
- [ ] `skills/veloxdev-create-workflow/references/gui/winforms.md`
- [ ] `memory/modules/{WorkflowSystem,TransitionSystem,Templates}/adapters/winforms.md` + 本文

### 4.2 加 / 换一个视图角色

- [ ] 派生类放你的宿主工程（基类在适配器里）；模板要默认产出就改对应 `TemplateClass.cs`
- [ ] README 的 `## Surface controls` / `## Behaviors` 表
- [ ] 两个 demo 与 `Src/Templates/VeloxDev.WinForms.Templates/`

### 4.3 加一个采样器

- [ ] `TransitionSystem/Samplers/` 新文件 + `Interpolator` 静态构造注册
- [ ] **`Src/Test/AutoTest/Samplers/WinFormsEntries.All`** —— 漏了会被 `SamplerCoverageTests` 反射比对直接判失败（`ExpectedAdapterAssemblies` 里必须有 `VeloxDev.WinForms`）
- [ ] 若端点值在这个进程里造不出来 → 进 `UnreachableSamplers.All`（**可证伪**的登记，不是垃圾桶）
- [ ] `AdapterSamplerEntries.cs` **不用改**（聚合器已经引用 `WinFormsEntries.All`）
- [ ] `TransitionSystem/adapters/winforms.md` 与本文

### 4.4 改 TFM / 包结构

- [ ] `VeloxDev.WinForms.csproj` 的 `TargetFrameworks`（TFM 四元组 `netframework4.6.1;net5.0-windows;netcoreapp3.0;net8.0-windows`）；下游按 `net8.0-windows` 或 `net5.0-windows` 那份资产解析
- [ ] `skills/veloxdev-create-workflow/references/gui/winforms.md`（它把四元组写进了面向消费者的摘要）
- [ ] `VeloxDev.slnx`（项目注册）
- [ ] 生成器双轨引用与 TFM 的关系见 `memory/modules/VeloxDev.Core.Generator/architecture.md` §五

---

## 五、几个「以为能改、其实不该改」的地方

1. **别把 `SetIsEnabled` 里那两句 `EnsureClipChildren`/`EnsureComposited` 摘掉**（`WorkflowSurfaceBehavior.SetIsEnabled` 附近）：它们是 WinForms 上「自绘画布 + 子窗口节点卡」这种分层重画唯一不闪的手段（机制见 `WorkflowSystem/adapters/winforms.md` §2.3）。
2. **别把消息过滤器的两个条件简化**：`ResolveSurfaceHost` 同时认 `_filterHost` 与「沿父链找到 zoom-enabled 宿主」，后者才是多宿主/多窗口下收窄的关键；`Control.ModifierKeys != Keys.Control` 是**精确**比较，改成 `HasFlag` 会让 Ctrl+Shift+滚轮也被吞掉。
3. **`WS_CLIPCHILDREN` 与 `WS_EX_COMPOSITED` 这两个同值常量不要合并**：它们写向两个不同的样式空间。
4. **`SyncSlot` 用 `SlotAnchorFromCanvasLocal` 而不是 `SlotAnchorFromNode`**（`WorkflowSlotLayoutBehavior.SyncSlot`；同一结论见 `WorkflowSystem/adapters/winforms.md` §4.4）。
5. **`ScheduleSync` 的「句柄未建就丢弃」有 `SyncNow` 作为同步口**，模板与 demo 都在用它；改成排队会改变模板里已经调好的重测时序。
6. **表面的 `PART_ScrollViewer.AutoScroll` 保持 `false`**：这是「画布固定、平移改卡片」这套模型的必要前提，打开它会与签名 pan 打架（`architecture.md` §五·7）。
