# VeloxDev.WinForms — 扩展

> 配套阅读：`architecture.md`（本目录，「这家给什么、怎么挂上去」）。
> 平台差异与坑见 `memory/modules/WorkflowSystem/adapters/winforms.md`、`memory/modules/TransitionSystem/adapters/winforms.md`、`memory/modules/Templates/adapters/winforms.md`；七个角色的职责与附着属性名见 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3。本文只写**在这个项目里动手**的部分。

---

## 一、扩展点地图

这家**没有注册表、没有分析器、没有 `Initialize()`** —— 「加东西」= 加文件 + （若走表面）派生基类 + 在宿主侧调静态 `Get`/`Set`。下表是每个方向真正的挂点：

| 我想加 | 挂点 | 位置 |
|---|---|---|
| 一张新的工作流表面 | **派生 `WorkflowTreeView`**（`abstract`），实现 `CreateNodeView`/`CreateLinkView`，或赋 `TemplateSelector` | `Attached/Workflow/WorkflowTreeView.cs:37`（工厂 `:234`/`:239`） |
| 一种新的节点卡片 | **`WorkflowNodeAttachment.Attach(this)`**（2026-10-04 起不再是可继承基类），卡片自绘并按 `PART_*` 名接线 | `Attached/Workflow/WorkflowNodeAttachment.cs:55` |
| 一种新的插槽图形 | **`WorkflowSlotAttachment.Attach(this)`** | `Attached/Workflow/WorkflowSlotAttachment.cs:66` |
| 一种新的连线图形 | **自己的控件 + `WorkflowLinkAttachment.Attach(this)`**，在 `OnPaint` 里画（2026-10-04 起不再是基类） | `WorkflowLinkAttachment.cs` |
| 换网格/标尺外观 | **派生 `WorkflowGridDecorator`**，设调色板与间距 | `WorkflowGridDecorator.cs:23` |
| 「item 类型 → 视图」的工厂 | `WorkflowTemplateSelector`（设四个工厂）或直接实现 `IWorkflowTemplateSelector.CreateView(object item)` | `WorkflowTemplateSelector.cs:21` / `ViewManager.cs:14` |
| 一个新的附着行为 | 新建 `public static class` + `ConditionalWeakTable<Control, State>` + 静态 `Get`/`Set`，命名空间写 `VeloxDev.WorkflowSystem.AttachedBehaviors` | `Attached/Workflow/` 下新文件；若需要 `Refresh` 推数据，改 `WorkflowSurfaceBehavior.Refresh`（`:351`） |
| 一个新的「名字」通道 | 状态里加 `string? XxxName` + 用 `FindControlByName` 解析 | `WorkflowSlotLayoutBehavior.cs:652` |
| 一个新的 Win32 补偿 | `NativeWindowStyleHelper` 加常量 + 方法 | `:23-40`、`:191`；**必须同时加调用点**（现有 4 处） |
| 一个采样器 | `PlatformAdapters/Samplers/XxxSampler.cs`，命名空间 `VeloxDev.Adapters.NativeSamplers` | 登记在 `Interpolator.cs:9`；且必须给 AUTO TEST 加表项（§四·3）；要能被 `Transition` 面用可能还要加 `Property` 重载（`Transition.cs:34-155`） |
| 一个主题转换器 | `PlatformAdapters/ThemeValueConverters.cs` 里加一个 `IThemeValueConverter` 类 | **无注册**（按目标类型键）。但这三个平台上无 demo 可验（`Examples/Theme/` 没有 WinForms） |
| 一个自绘小地图 | 派生 `WorkflowMinimapOverlay`（已实现 `IWorkflowMinimapOverlay` + `IWorkflowMinimapScrollSource`） | `Attached/Workflow/WorkflowMinimapOverlay.cs:23` |

---

## 二、官方做法 vs 看着能编译、但错的捷径

**这一节是全文最重要的部分。** 下面每一条都能编译通过，有些跑起来还「像是好的」。

| # | 错的捷径 | 为什么错 | 官方做法 | 依据 |
|---|---|---|---|---|
| 1 | 在 `WorkflowSurfaceBehavior` 里补平移（`MouseDown/Move/Up`）或打开 `PART_ScrollViewer.AutoScroll` | 这家有**两套平移模型**：表面行为只**反射读** pan，真正的签名平移在 `WorkflowTreeView` 的画布上 | 平移在 `WorkflowTreeView`（`ApplyPan` 写 `SurfaceCanvas.PanOffset`）；要改就改基类或派生覆写 | `WorkflowSurfaceBehavior.cs:582`（`ResolvePanOffset`）；`WorkflowTreeView.cs:845` |
| 2 | 自己 `new` 一个网格装饰器/小地图/连线控件，再 `Set*Name` 接上（旧写法） | 现在适配器**自带**这些实现与基类；自己在宿主里重造会与池/表面重复一套 | 派生对应基类，或把它作为 `MinimapOverlay`/`TemplateSelector` 赋给 `WorkflowTreeView` | `WorkflowGridDecorator.cs:23`、`WorkflowLinkAttachment.cs`、`WorkflowMinimapOverlay.cs:23` |
| 3 | 用 `SetLayoutPropertyName` / `SetActualOffsetPropertyName` 改「读宿主的哪个布局属性」 | 这两个公共成员唯一的消费者是私有 `GetActualOffset`（`:743`），而它**自己零调用者** ⇒ 整条链是死代码，改了没有任何反应 | 基类自己从 `IWorkflowTreeViewModel.Layout` 取 | `WorkflowSlotLayoutBehavior.cs:210`、`:237`、`:743` |
| 4 | 在节点卡 ctor 里调 `Refresh`/`SetIsEnabled`，指望锚点当场量好 | `ScheduleSync` 在 `!control.IsHandleCreated` 时把 `SyncPending` 复位后返回 —— 请求被**丢掉**，不是排队 | 要同步量就调 `WorkflowSlotLayoutBehavior.SyncNow(control)` | `WorkflowSlotLayoutBehavior.cs:440-466`（`:465`）、`:288` |
| 5 | 自绘画布用 `WorkflowCanvasTransformBehavior.GetTransform(host)` 拿平移量 | 写进去了，**全仓零读者**（写值走 `Apply` `:65`）。基类卡片走 `IWorkflowSurfaceNodeView.ApplySurfacePosition`，表面走自己的 pan 字段 | 你既然是自绘，pan 就在你自己手里，不必绕这一圈 | `WorkflowCanvasTransformBehavior.cs:34`、`:65` |
| 6 | `using VeloxDev.TransitionSystem.NativeSamplers;` 想用这家唯一的采样器 | 这家在 `VeloxDev.Adapters.NativeSamplers`；`VeloxDev.TransitionSystem.NativeSamplers` 是 **Core 自己**那 15 个的命名空间 | 用 `VeloxDev.Adapters.NativeSamplers` | `Samplers/PaddingSampler.cs:1`；`Src/Core/VeloxDev.Core/TransitionSystem/NativeSamplers/ColorSampler.cs:3` |
| 7 | 在 `Attached/` 里直接用 `Transition<T>`/`Interpolator`（因为编译得过） | 编译得过是因为 `GlobalUsings.cs` 把 `VeloxDev.TransitionSystem` 灌进了每个文件 —— 但「三条轴在程序集内互不引用」是七家共同守的纪律，破了不会报错 | 保持零引用；跨轴的事在宿主侧接 | `GlobalUsings.cs`（3 行）；`architecture.md` §一 |
| 8 | 照 WPF 的心智模型期待名字作用域 / 重名报错 | 这家是**递归子树顺序查找 + `Ordinal` 比较**，取第一个命中；跨容器、非子孙一律找不到，而且**不抛不报** | 名字只挂在宿主子树里，且保证唯一 | `WorkflowSlotLayoutBehavior.cs:652` |
| 9 | 以为 `SetIsEnabled` 会订阅事件、或有早退 | 它只做两件事：置位 + 加 `WS_CLIPCHILDREN`/`WS_EX_COMPOSITED`；订阅是各 `Set*Name` 干的 | 别靠 `SetIsEnabled` 反复调来「重新接入」 | `WorkflowSurfaceBehavior.cs:140` |
| 10 | 传 `typeof(Panel)` 当坐标宿主，而节点卡与画布之间还夹着一层 `Panel` | 类型档是**就近匹配、无校验**，会静默按错的坐标系写锚点 | 能确定类型就传真正画布的类型（demo 传 `typeof(WorkflowCanvas)`） | `WorkflowSlotLayoutBehavior.cs`（`ResolveCoordinateHost`）；`WorkflowNodeDragBehavior.cs`（同形） |
| 11 | 给节点卡片加 Dispose 逻辑却不用助手 | 卡片是子控件树，直接 `Dispose` 父控件会漏掉订阅（`ModelChangeRelay`）与子控件 | 用 `DisposeChildren(parent)` 收尾 | `WorkflowNodeAttachment.cs:282` |
| 12 | 以为隐藏的视图会被 `Remove` | `ViewManager` 只把视图 `IsVisible=false` + `ZIndex=-100` 留在画布 `Controls` 里（移除触发昂贵重排） | 遍历子控件时不能假设「看不见 = 不在」 | `ViewManager.cs`（池化重排注释） |

---

## 三、步骤清单

### 3.1 派生一张工作流表面（这家最常见的扩展）

1. **建文件**：派生 `WorkflowTreeView`（`abstract`，`WorkflowTreeView.cs:37`），实现 `CreateNodeView`（`:234`）/`CreateLinkView`（`:239`）。基类 ctor（`:340`）已把 `PART_ScrollViewer`/`PART_Canvas` 建好并调好 `WorkflowSurfaceBehavior.SetIsEnabled/SetZoomEnabled/Set*Name`（`:377-383`）——**不要再自己接一遍**。
2. **两个工厂**：返回的节点卡片**应实现 `IWorkflowSurfaceNodeView`**（`:14`），否则表面无法在平移时给它落位；连线视图不必（几何由插槽行为重测后重建）。
3. **配色/边距**：`SurfaceBackground`/`SurfaceBorderBrush`/`SurfaceBorderThickness`/`SurfaceCornerRadius`（`:181-229`）；`RulerReserve` 是常量（`:47`）。
4. **小地图**（可选）：new 一个派生 `WorkflowMinimapOverlay` 的实例并赋给 `MinimapOverlay`（`:105`）—— 若它实现 `IWorkflowMinimapScrollSource`，表面会把视口拖动接到自己的 pan 上。
5. **模板/派生联动**：`Src/Templates/VeloxDev.WinForms.Templates/working/content/` 下对应条目（7 个），以及两个 demo。

### 3.2 加一个采样器

1. `PlatformAdapters/Samplers/XxxSampler.cs`，`namespace VeloxDev.Adapters.NativeSamplers`，实现 `ISampler` 三个方法（`PaddingSampler.cs` 是唯一先例）。
2. 在 `PlatformAdapters/Interpolator.cs:9` 的静态构造里 `RegisterInterpolator(typeof(T), new XxxSampler());`。
3. 要能被 `Transition` 面用上，可能需要在 `PlatformAdapters/Transition.cs` 加 `Property` 重载（`:34` 是泛型那个，`:42-155` 是 16 个手写重载，`:127` 起在 `#if !NETSTANDARD2_0` 里）。
4. **必须**给 `Examples/Transition/AUTO TEST/Samplers/WinFormsEntries.All`（`:25`）加一条 `EntryFactory.Create<...>`：`SamplerCoverageTests` 会反射产品程序集里所有 `ISampler` 与注册表比对，**漏了就直接失败**。
5. 闭式解怎么定、`SamplerRule` 该选哪个 —— 见 `memory/modules/TransitionSystem/extension.md`；采样器在本平台的行为差异见 `TransitionSystem/adapters/winforms.md`。

### 3.3 加一个主题转换器

1. 在 `PlatformAdapters/ThemeValueConverters.cs` 里加一个 `IThemeValueConverter` 类（13 个先例，`:6-495`）。**按目标类型键，无需注册。**
2. 命名冲突要当场处理：本文件与 `System.Drawing` 多个同名类型共存，凡要用框架那个必须全限定（`:328`、`:460` 就是被逼出来的写法）。
3. 转换失败一律静默返回 `null` 是这家（也是 DynamicTheme）的刻意姿态，别改成抛。
4. 验证方式：**这三个平台上没有 Theme demo**（`Examples/Theme/` 只有 Avalonia/WPF），只能自己起宿主验。

### 3.4 加一个 Win32 补偿

1. 在 `Attached/Workflow/NativeWindowStyleHelper.cs` 加常量与方法（`internal static`，不对外）。
2. `SetWindowLong` 的 index（普通样式 vs 扩展样式）不要凭常量值猜 —— 这家两个常量**值相同**（`0x02000000`，`:23-24`）。
3. 至少加一个调用点，并想清楚「句柄还没建」的情况：`ApplyStyle`（`:191`）只在位缺失时才动 `SetWindowPos`，而 `HandleCreated` 处有重挂（`:74`、`:99`）。

### 3.5 加一个附着行为（旧的静态 `Get`/`Set` 形态）

1. `Attached/Workflow/WorkflowXxxBehavior.cs`，`namespace VeloxDev.WorkflowSystem.AttachedBehaviors;`，类写 `public static class`。
2. `private sealed class XxxState` + `static readonly ConditionalWeakTable<Control, XxxState> States`（照 `WorkflowSurfaceBehavior` 的 `GetState`）。
3. 每个属性一对 `public static T? GetXxx(Control element)` / `SetXxx(Control element, T? value)`；`element is null` 抛 `ArgumentNullException`（全模块一致）。
4. 若宿主用**名字**指认目标控件，走 `FindControlByName` 那一套（私有，`WorkflowSlotLayoutBehavior.cs:652` —— 这家没有共享的名字解析工具）。
5. 要不要进 `Refresh`：如果适配器需要在每次刷新周期把值推给你的行为，改 `WorkflowSurfaceBehavior.Refresh`（`:351`）；否则你的行为自己订阅。
6. **零注册**：加完就能被宿主调用（这也是为什么这家最容易长出「有 API 没读者」的成员，见 `architecture.md` §五·1）。

---

## 四、联动清单（改一个 X 时必须同步修改的所有位置）

**漏一处通常不报错，只是静默少一个能力或文档说谎。**

### 4.1 改任何 `Set*` / `Get*` 的名字或语义（这家最重的联动）

- [ ] **本模块 README**（`Src/Adapters/VeloxDev.WinForms/README.md`，245 行，逐表列 API；它自己就是消费者入口）
- [ ] **7 个模板条目**（`Src/Templates/VeloxDev.WinForms.Templates/working/content/*/`；`tree-view` 现在派生 `WorkflowTreeView`、`node-view` 用 `WorkflowNodeAttachment.Attach`、`slot-view` 用 `WorkflowSlotAttachment.Attach`、`link-view`/`grid-decorator`/`minimap-overlay`/`template-selector` 各有形状）
- [ ] **两个 Workflow demo**：`Examples/Workflow/WinForms/Demo/` 与 `Examples/Workflow/WinForms Trimmed/Demo/`
- [ ] `skills/veloxdev-create-workflow/references/gui/winforms.md`
- [ ] `memory/modules/{WorkflowSystem,TransitionSystem,Templates}/adapters/winforms.md` + 本文

### 4.2 加 / 换一个视图角色

- [ ] 派生类放你的宿主工程（基类在适配器里）；模板要默认产出就改对应 `TemplateClass.cs`
- [ ] README 的 `## Surface controls` / `## Behaviors` 表
- [ ] 两个 demo 与 `Src/Templates/VeloxDev.WinForms.Templates/`

### 4.3 加一个采样器

- [ ] `PlatformAdapters/Samplers/` 新文件 + `Interpolator.cs:9` 注册
- [ ] **`Examples/Transition/AUTO TEST/Samplers/WinFormsEntries.All`** —— 漏了会被 `SamplerCoverageTests` 反射比对直接判失败（`:21` 的 `ExpectedAdapterAssemblies` 里必须有 `VeloxDev.WinForms`）
- [ ] 若端点值在这个进程里造不出来 → 进 `UnreachableSamplers.All`（**可证伪**的登记，不是垃圾桶）
- [ ] `AdapterSamplerEntries.cs` **不用改**（聚合器已经引用 `WinFormsEntries.All`）
- [ ] `TransitionSystem/adapters/winforms.md` 与本文

### 4.4 改 TFM / 包结构

- [ ] `VeloxDev.WinForms.csproj:4`（TFM 四元组 `netframework4.6.1;net5.0-windows;netcoreapp3.0;net8.0-windows`）；下游按 `net8.0-windows` 或 `net5.0-windows` 那份资产解析
- [ ] `skills/veloxdev-create-workflow/references/gui/winforms.md`（它把四元组写进了面向消费者的摘要）
- [ ] `VeloxDev.slnx`（项目注册）
- [ ] 生成器双轨引用与 TFM 的关系见 `memory/modules/VeloxDev.Core.Generator/architecture.md` §五

---

## 五、几个「以为能改、其实不该改」的地方

1. **别把 `SetIsEnabled` 里那两句 `EnsureClipChildren`/`EnsureComposited` 摘掉**（`WorkflowSurfaceBehavior.cs:140` 附近）：它们是 WinForms 上「自绘画布 + 子窗口节点卡」这种分层重画唯一不闪的手段（机制见 `WorkflowSystem/adapters/winforms.md` §2.3）。
2. **别把消息过滤器的两个条件简化**：`ResolveSurfaceHost`（`:99`）同时认 `_filterHost` 与「沿父链找到 zoom-enabled 宿主」，后者才是多宿主/多窗口下收窄的关键；`Control.ModifierKeys != Keys.Control` 是**精确**比较，改成 `HasFlag` 会让 Ctrl+Shift+滚轮也被吞掉。
3. **`WS_CLIPCHILDREN` 与 `WS_EX_COMPOSITED` 这两个同值常量不要合并**（`NativeWindowStyleHelper.cs:23-24`）：它们写向两个不同的样式空间。
4. **`SyncSlot` 里那段注释是结论不是废话**：为什么用 `SlotAnchorFromCanvasLocal` 而不是 `SlotAnchorFromNode`（`WorkflowSlotLayoutBehavior.cs` 的 `SyncSlot`）—— 改之前先读，`WorkflowSystem/adapters/winforms.md` §4.4 有同一条。
5. **`ScheduleSync` 的「句柄未建就丢弃」先别当成 bug 修**：`SyncNow` 是给这个缺口准备的同步口，模板与 demo 都在用它；改成排队会改变模板里已经调好的重测时序。
6. **`WorkflowTreeView` 的 `PART_ScrollViewer.AutoScroll` 保持 `false`**（`:360`）：这是「画布固定、平移改卡片」这套模型的必要前提，打开它会与签名 pan 打架（`architecture.md` §五·7）。
