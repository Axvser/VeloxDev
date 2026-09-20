# VeloxDev.WinForms — 扩展

> 配套阅读：`architecture.md`（本目录，「这家少什么、怎么挂上去」）。
> 平台差异与坑见 `memory/modules/WorkflowSystem/adapters/winforms.md`、`memory/modules/TransitionSystem/adapters/winforms.md`、`memory/modules/Templates/adapters/winforms.md`；七个角色的职责与附着属性名见 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3。本文只写**在这个项目里动手**的部分。

---

## 一、扩展点地图

这家**没有注册表、没有分析器、没有 `Initialize()`** —— 「加东西」= 加文件 + 在宿主侧调静态 `Get`/`Set`。下表是每个方向真正的挂点：

| 我想加 | 挂点 | 位置 | 要"登记"到哪 |
|---|---|---|---|
| 一个新的附着行为 | 新建 `public static class` + `ConditionalWeakTable<Control, State>` + 静态 `Get`/`Set`，命名空间写 `VeloxDev.WorkflowSystem.AttachedBehaviors` | `Attached/Workflow/` 下新文件 | **没有任何注册表**。唯一要考虑的是：它若需要 `Refresh` 推数据，得改 `WorkflowSurfaceBehavior.Refresh`（`WorkflowSurfaceBehavior.cs:422-494`） |
| 一个新的"名字"通道 | 状态里加 `string? XxxName` + 用 `FindControlByName` 解析 | `WorkflowSurfaceBehavior.cs:658-669` | 无 |
| 一个新的 Win32 补偿 | `NativeWindowStyleHelper` 加常量 + 方法 | `:23-40`、`:197-215` | **必须同时加调用点**（现有 4 处：`WorkflowNodeDragBehavior.cs:133`、`WorkflowSurfaceBehavior.cs:151`、`:654`） |
| 一个采样器 | `PlatformAdapters/Samplers/XxxSampler.cs`，命名空间跟 `PaddingSampler.cs:1`（`VeloxDev.Adapters.NativeSamplers`） | — | `Interpolator.cs:9` 的静态构造一行注册；**且**必须给 AUTO TEST 加表项（见 §四·4.3）；要能被 `Transition` 用可能还要加 `Property` 重载（`Transition.cs:30-135`） |
| 一个主题转换器 | `PlatformAdapters/ThemeValueConverters.cs` 里加一个 `IThemeValueConverter` 类 | — | **无注册**（按目标类型键）。但这三个平台上无 demo 可验（`Examples/Theme/` 没有 WinForms） |
| 网格装饰器 / 小地图 / 连线视图的**实现** | **不在本模块**，在你的工程里；然后实现 `IWorkflowGridDecorator` / `IWorkflowMinimapOverlay` / 自绘 | 对照 `Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-{grid-decorator,minimap-overlay,link-view}/TemplateClass.cs` | 用 `SetGridDecoratorName` / `SetMinimapOverlayName` 把名字接上（`README.md:49`） |
| 一个视图工厂 | 实现适配器的 `IWorkflowTemplateSelector`（**不是** `DataTemplateSelector`） | `ViewManager.cs:14-20` | `ViewPool.SetTemplateSelector(container, selector)` + `SetItemsSource` 成对 |
| 让一种新控件进池 | 在 selector 的 `CreateView` 里加分派 | — | 无（模板那份 selector 对未支持的类型直接抛 `InvalidOperationException`：`workflow-template-selector/TemplateClass.cs:22-32`） |

---

## 二、官方做法 vs 看着能编译、但错的捷径

**这一节是全文最重要的部分。** 下面每一条都能编译通过，有些跑起来还「像是好的」。

| # | 错的捷径 | 为什么错 | 官方做法 | 依据 |
|---|---|---|---|---|
| 1 | 写 `SetPointerPressSourceName(canvas, "X")` 想让 `X` 成为「空白按下开始平移」的判定源 | 这家**根本没有平移实现**：`WorkflowSurfaceBehavior` 没有 `MouseDown`/`MouseMove`/`MouseUp`，也从不设 `Capture`（只在 `Refresh` 里读 `host.Capture` 决定要不要同步重画）；该字段写进去后除了自己的 getter 没有人读 | 平移由宿主控件自己实现；适配器只**反射读**宿主的 `PanOffset` 属性或私有 `_panOffset` 字段 | `WorkflowSurfaceBehavior.cs:349`、`:356`、`:22`；`ResolvePanOffset` `:614-637`；`host.Capture` `:490` |
| 2 | 小地图：先 `SetWorkflowTree(ctl, tree)` 再 `SetIsEnabled(ctl, true)` | `SetIsEnabled` 先 `Detach`，`Detach` 里 `state.Tree = null` ⇒ 树绑定被清掉，`Attach` 之后不再订阅任何东西，**且不报错** | 顺序反过来：先 `SetIsEnabled` 再 `SetWorkflowTree`（或开完开关后再绑一次） | `WorkflowMinimapOverlay.cs:69`、`:126-134`、`:101` |
| 3 | 用 `SetLayoutPropertyName` / `SetActualOffsetPropertyName` 改「读宿主的哪个布局属性」 | 这两个公共成员唯一的消费者是私有 `GetActualOffset`（`:758`），而它**自己零调用者** ⇒ 整条链是死代码，改了没有任何反应 | 装饰器/小地图自己从 `IWorkflowTreeViewModel.Layout` 取 | `WorkflowSlotLayoutBehavior.cs:210`、`:237`、`:758` |
| 4 | 在节点卡 ctor 里调 `Refresh`/`SetIsEnabled`，指望锚点当场量好 | `ScheduleSync` 在 `!control.IsHandleCreated` 时把 `SyncPending` 复位后返回 —— 请求被**丢掉**，不是排队 | 要同步量就调 `WorkflowSlotLayoutBehavior.SyncNow(control)`（两份模板与 demo 都这么写） | `WorkflowSlotLayoutBehavior.cs:442-468`（`:467`）、`:288` |
| 5 | 指望适配器给你造网格装饰器 / 小地图 / 连线控件 | 全模块唯一的控件创建是 `_selector.CreateView(item)` + `_host.Controls.Add(view)`，全在你提供的 selector 里 | 自己写类实现接口，再用 `Set*Name` 命名接上 | `ViewManager.cs:161`、`:168`；`:14-20` |
| 6 | 自绘画布用 `WorkflowCanvasTransformBehavior.GetTransform(host)` 拿平移量来翻译绘图原点 | 写进去了，**全仓零读者**（写值处 `WorkflowSurfaceBehavior.cs:479`）。模板/两份 demo 的画布都是从自己的 pan 字段推 | 你既然是自绘，pan 就在你自己手里，不必绕这一圈 | `WorkflowCanvasTransformBehavior.cs:34`、`:65-74`；模板 `workflow-tree-view/TemplateClass.cs` 的画布用 `ScrollOffsetX/ContentOffsetX` |
| 7 | `using VeloxDev.TransitionSystem.NativeSamplers;` 想用这家唯一的采样器 | 这家在`VeloxDev.Adapters.NativeSamplers`；`VeloxDev.TransitionSystem.NativeSamplers` 是 **Core 自己**那 15 个的命名空间 | 用 `VeloxDev.Adapters.NativeSamplers`（AUTO TEST 就是这么写的） | `Samplers/PaddingSampler.cs:1`；`Src/Core/VeloxDev.Core/TransitionSystem/NativeSamplers/ColorSampler.cs:3`；`Examples/Transition/AUTO TEST/Samplers/WinFormsEntries.cs:1` |
| 8 | 在 `Attached/` 里直接用 `Transition<T>`/`Interpolator`（因为编译得过） | 编译得过是因为 `GlobalUsings.cs` 把 `VeloxDev.TransitionSystem` 灌进了每个文件 —— 但「三条轴在程序集内互不引用」是七家共同守的纪律，破了不会报错 | 保持零引用；跨轴的事在宿主侧接 | `GlobalUsings.cs`（3 行）；`architecture.md` §一 |
| 9 | 照 WPF 的心智模型期待名字作用域 / 重名报错 | 这家是**递归子树顺序查找 + `Ordinal` 比较**，取第一个命中；跨容器、非子孙一律找不到，而且**不抛不报** | 名字只挂在宿主子树里，且保证唯一 | `WorkflowSurfaceBehavior.cs:658-669` |
| 10 | 以为 `SetSlotNames` 必填才能量锚点 | 两个名字都空时它退化成「遍历子树找所有能解析出 slot 的控件」 | 只挂枚举器通道也行（WinForms 的 node-view 模板就不设 `SlotNames`） | `WorkflowSlotLayoutBehavior.cs:524-561`；`workflow-node-view/TemplateClass.cs:117-118` |
| 11 | 以为 `SetIsEnabled` 会订阅事件、或以为它有早退（像小地图那个一样） | 它只做两件事：置位 + 加 `WS_CLIPCHILDREN`/`WS_EX_COMPOSITED`；订阅是各 `Set*Name` 干的 | 别靠 `SetIsEnabled` 反复调来"重新接入" | `WorkflowSurfaceBehavior.cs:137-153` |
| 12 | 传 `typeof(Panel)` 当坐标宿主，而节点卡与画布之间还夹着一层 `Panel` | 类型档是**就近匹配、无校验**，会静默按错的坐标系写锚点 | 能确定类型就传真正画布的类型（demo 传 `typeof(WorkflowCanvas)`） | `ResolveCoordinateHost` `:613-638`；`Examples/Workflow/WinForms/Demo/Controls/WorkflowNodeCard.cs:82` |

---

## 三、步骤清单

### 3.1 加一个新的附着行为（这家最常见的扩展）

1. **建文件**：`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowXxxBehavior.cs`，`namespace VeloxDev.WorkflowSystem.AttachedBehaviors;`（照 `WorkflowCanvasTransformBehavior.cs:6`），类写成 `public static class`。
2. **状态**：`private sealed class XxxState { ... }` + `static readonly ConditionalWeakTable<Control, XxxState> States = new();`，`GetState` 用 `States.GetValue(element, static _ => new XxxState())`（照 `WorkflowSlotLayoutBehavior.cs:788-789`）。
3. **API**：每个属性写一对 `public static T? GetXxx(Control element)` / `SetXxx(Control element, T? value)`；`element is null` 抛 `ArgumentNullException`（全模块一致）。
4. **接线方向**：若宿主用**名字**指认你的目标控件，走 `FindControlByName` 那一套（私有 `static`，注意它在 `WorkflowSurfaceBehavior` 里是私有的 —— 这家没有共享的名字解析工具，要么再写一份，要么从 `WorkflowSlotLayoutBehavior` 抄）。
5. **要不要进 `Refresh`**：如果适配器需要在每次刷新周期把值推给你的行为，改 `WorkflowSurfaceBehavior.Refresh`（`:422-494`）加一段；否则你的行为自己订阅。
6. **零注册**：没有中央列表，加完就能被宿主调用（这也是为什么这家最容易长出「有 API 没读者」的成员，见 `architecture.md` §五·1）。

### 3.2 加一个采样器

1. `PlatformAdapters/Samplers/XxxSampler.cs`，`namespace VeloxDev.Adapters.NativeSamplers`，实现 `ISampler` 三个方法（`PaddingSampler.cs` 是唯一先例）。
2. 在 `PlatformAdapters/Interpolator.cs:9` 的静态构造里 `RegisterInterpolator(typeof(T), new XxxSampler());`。
3. 要能被 `Transition` 面用上，可能需要在 `PlatformAdapters/Transition.cs` 加 `Property` 重载（`:30` 是泛型那个，`:37-135` 是 16 个手写重载）。
4. **必须**给 `Examples/Transition/AUTO TEST/Samplers/WinFormsEntries.cs` 的 `All` 加一条 `EntryFactory.Create<...>`：`SamplerCoverageTests` 会反射产品程序集里所有 `ISampler` 与注册表比对，**漏了就直接失败**（`SamplerRegistry.cs` 的 remarks 与 `SamplerCoverageTests.cs` 的类注释都写了这条）。
5. 闭式解怎么定、`SamplerRule` 该选哪个 —— 见 `memory/modules/TransitionSystem/extension.md`；采样器在本平台的行为差异见 `TransitionSystem/adapters/winforms.md`。

### 3.3 加一个主题转换器

1. 在 `PlatformAdapters/ThemeValueConverters.cs` 里加一个 `IThemeValueConverter` 类（13 个先例，`:6-473`）。**按目标类型键，无需注册。**
2. 命名冲突要当场处理：本文件与 `System.Drawing` 多个同名类型共存，凡要用框架那个必须全限定（`:317`、`:447` 就是被逼出来的写法）。
3. 转换失败一律静默返回 `null` 是这家（也是 DynamicTheme）的刻意姿态，别改成抛。
4. 验证方式：**这三个平台上没有 Theme demo**（`Examples/Theme/` 只有 Avalonia/WPF），只能自己起宿主验。

### 3.4 加一个 Win32 补偿

1. 在 `Attached/Workflow/NativeWindowStyleHelper.cs` 加常量与方法（`internal static`，不对外）。
2. `SetWindowLong` 的 index（普通样式 vs `-20` 扩展样式）不要凭常量值猜 —— 这家两个常量**值相同**（`0x02000000`，`:23-24`）。
3. 至少加一个调用点，并想清楚「句柄还没建」的情况：`ApplyStyle` 只在位缺失时才动 `SetWindowPos`，而 `HandleCreated` 处有重挂（`:110-124`、`:179-185`）。

### 3.5 加一个模板条目 / 改模板

不在本模块。七个条目的形状、必须手写的接线、这一家模板特有的坑：`memory/modules/Templates/adapters/winforms.md`。注册位置：`memory/modules/WorkflowSystem/extension.md` §4.3。

---

## 四、联动清单（改一个 X 时必须同步修改的所有位置）

**漏一处通常不报错，只是静默少一个能力或文档说谎。**

### 4.1 改任何 `Set*` / `Get*` 的名字或语义（这家最重的联动）

- [ ] **本模块 README**（`Src/Adapters/VeloxDev.WinForms/README.md`，194 行，逐表列 API；它自己就是消费者入口）
- [ ] **7 个模板条目的 `TemplateClass.cs`**（`Src/Templates/VeloxDev.WinForms.Templates/working/content/*/`）。实测调用点：`tree-view` 7 处（`WorkflowSurfaceBehavior` 六个 + `ViewPool` 两处/`Refresh`）、`node-view` 7 处（`WorkflowNodeDragBehavior` 2 + `WorkflowSlotLayoutBehavior` 5，含 `SyncNow`/`Refresh`）、`slot-view` 1 处（`WorkflowSlotConnectionBehavior.SetIsEnabled`）；`grid-decorator` / `minimap-overlay` / `template-selector` 三个条目不调这些静态方法
- [ ] **两个 Workflow demo**：`Examples/Workflow/WinForms/Demo/`（`Controls/WorkflowCanvas.cs:154/165/180-185/282/352/459/476/498/508/558/568/605/633/732/978`、`Controls/WorkflowNodeCard.cs:81-84`、`Form1.cs:20-22/240`、`Views/SlotView.cs:46`）与 `Examples/Workflow/WinForms Trimmed/Demo/Views/Workflow/`（`TreeView.cs:156-162/703/718/736-737/743/942/1024/1132-1133`、`NodeView.cs:112-118/452/581`、`SlotView.cs:43`）
- [ ] `skills/veloxdev-create-workflow/references/gui/winforms.md`（`:5` 自称是这份 README 的摘要）
- [ ] `memory/modules/{WorkflowSystem,TransitionSystem,Templates}/adapters/winforms.md` + 本文

### 4.2 加一个新的附着行为

- [ ] 新文件（§3.1）。**没有注册表**：不加也能被宿主调用
- [ ] 若它参与刷新周期 → `WorkflowSurfaceBehavior.Refresh`
- [ ] README 的 `## Behaviors` 表（`:17-30`）与本节
- [ ] 若模板要默认挂上 → 对应 `TemplateClass.cs` 的 ctor；不改则**生成的代码不会用它**

### 4.3 加一个采样器

- [ ] `PlatformAdapters/Samplers/` 新文件 + `Interpolator.cs:9` 注册
- [ ] **`Examples/Transition/AUTO TEST/Samplers/WinFormsEntries.All`** —— 漏了会被 `SamplerCoverageTests` 反射比对直接判失败（`SamplerCoverageTests.cs:27` 的 `ExpectedAdapterAssemblies` 里必须有 `VeloxDev.WinForms`，否则整个程序集数不到）
- [ ] 若端点值在这个进程里造不出来 → 进 `UnreachableSamplers.All`（那是一条**可证伪**的登记，不是垃圾桶）
- [ ] `AdapterSamplerEntries.cs` **不用改**（聚合器已经引用 `WinFormsEntries.All`）
- [ ] `TransitionSystem/adapters/winforms.md` 与本文

### 4.4 改 TFM / 包结构

- [ ] `VeloxDev.WinForms.csproj:4`（TFM 三元组）；下游按 `net5.0-windows` 那份资产解析
- [ ] `skills/veloxdev-create-workflow/references/gui/winforms.md:3`（它把三元组写进了面向消费者的摘要）
- [ ] `VeloxDev.slnx:13`（项目注册）
- [ ] 生成器双轨引用与 TFM 的关系见 `memory/modules/VeloxDev.Core.Generator/architecture.md` §五

---

## 五、几个「以为能改、其实不该改」的地方

1. **别把 `SetIsEnabled` 里那两句 `EnsureClipChildren`/`EnsureComposited` 摘掉**（`WorkflowSurfaceBehavior.cs:151-152`）：它们是 WinForms 上「自绘画布 + 子窗口节点卡」这种分层重画唯一不闪的手段（机制见 `WorkflowSystem/adapters/winforms.md` §2.3）。
2. **别把消息过滤器的两个条件简化**：`ResolveSurfaceHost`（`:97-115`）同时认 `_filterHost` 与「沿父链找到 zoom-enabled 宿主」，后者才是多宿主/多窗口下收窄的关键；`Control.ModifierKeys != Keys.Control` 是**精确**比较，改成 `HasFlag` 会让 Ctrl+Shift+滚轮也被吞掉（现在是交给原生滚动）。
3. **`WS_CLIPCHILDREN` 与 `WS_EX_COMPOSITED` 这两个同值常量不要合并**（`NativeWindowStyleHelper.cs:23-24`）：它们写向两个不同的样式空间。
4. **`SyncSlot` 里那段注释是结论不是废话**：为什么用 `SlotAnchorFromCanvasLocal` 而不是 `SlotAnchorFromVisualCenter`（`WorkflowSlotLayoutBehavior.cs:592-605`）—— 改之前先读，`WorkflowSystem/adapters/winforms.md` §4.4 有同一条。
5. **`ScheduleSync` 的「句柄未建就丢弃」先别当成 bug 修**：`SyncNow` 是给这个缺口准备的同步口，两份模板与 demo 都在用它；改成排队会改变模板里已经调好的重测时序（`workflow-tree-view/TemplateClass.cs:920-927` 的注释就是围绕这个写的）。
