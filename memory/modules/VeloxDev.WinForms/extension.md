# VeloxDev.WinForms — 扩展

> 配套阅读：`architecture.md`（本目录）。平台差异与坑见 `memory/modules/WorkflowSystem/adapters/winforms.md`、`memory/modules/TransitionSystem/adapters/winforms.md`、`memory/modules/Templates/adapters/winforms.md`；契约在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3。

## 一、扩展点地图

这家**没有注册表、没有分析器、没有 `Initialize()`** —— 「加东西」= 加文件 + 在 `VisualHost` 上挂附着属性 / 在宿主侧调静态 `Get`/`Set`。

| 我想加 | 挂点 | 位置 |
|---|---|---|
| 一张新的工作流表面 | `VisualHost` + `WorkflowSurfaceBehavior`：设 `IsEnabled`/`ZoomEnabled`/`VisualTemplateSelector`（或 `VisualTemplateSelectorKey`）与部件 `*Name`；tree 是视觉 `UserControl`，`PART_Canvas` 是视觉 `Canvas` 并挂 `behaviors:ViewPool` | `WorkflowSystem/AttachedBehaviors/WorkflowSurfaceBehavior.cs`；示例 `Examples/Workflow/WinForms Trimmed/Demo/Views/Workflow/TreeView.xaml` |
| node/slot/link 视觉模板 | 视觉 `UserControl` 根；node `Viewbox > Grid 260×180` + `PART_InputSlot`/`PART_OutputSlots`，slot `Viewbox > Path`，link DP + `OnRender` 发布曲线；选择器派生 `VisualTemplateSelector`，tree 资源用 `DataTemplate` 包住它们 | 模板 `workflow-node-view` / `workflow-slot-view` / `workflow-link-view`；池 `ViewPool.cs` + `WorkflowSystem/ViewHosting/` |
| 换网格/标尺外观 | 模板条目实现 `IWorkflowGridDecorator`，设调色板与间距；宿主用 `GridDecoratorName` 接上 | 模板 `workflow-grid-decorator/TemplateClass.cs` |
| 「item 类型 → 视图」的选择器 | 派生 `VisualTemplateSelector`（`SelectTemplate` 返回 `VisualElement`） | `Easel.Visuals.VisualTemplateSelector` + 模板派生类 |
| 一个新的附着行为 | `public static class` + `ConditionalWeakTable<VisualHost, State>` + 静态 `Get`/`Set` | `WorkflowSystem/AttachedBehaviors/`；若每次刷新要推值，改 `WorkflowSurfaceBehavior.Refresh` |
| 一个新的「名字」通道 | 状态里加 `string? XxxName`，用 `FindVisualNamed` 解析 | `WorkflowSurfaceBehavior.FindVisualNamed` |
| 一个新的 Win32 补偿 | `NativeWindowStyleHelper` 加常量 + 方法 | `WorkflowSystem/Platform/NativeWindowStyleHelper.cs`；**必须同时加调用点** |
| 一个采样器 | `TransitionSystem/Samplers/XxxSampler.cs`，命名空间 `VeloxDev.Adapters.NativeSamplers` | 登记在 `Interpolator` 静态构造；且必须给 AUTO TEST 加表项（§四·3） |
| 一个主题转换器 | `DynamicTheme/ThemeValueConverters.cs` 加 `IThemeValueConverter` | **无注册**（按目标类型键）；这仨平台上无 demo 可验 |
| 一个自绘小地图 | 派生 `WorkflowMinimapOverlay`（已实现两个契约接口） | `WorkflowSystem/AttachedBehaviors/WorkflowMinimapOverlay` |

## 二、官方做法 vs 看着能编译、但错的捷径

| # | 错的捷径 | 为什么错 | 官方做法 | 依据 |
|---|---|---|---|---|
| 1 | 把 `WorkflowSurfaceBehavior` 挂到普通 `Control` | 非 `VisualHost` 分支把 `state.VisualHost = null`，界面空着也不报错 | 宿主必须是 `VisualHost`，`Root` 是视觉树 | `WorkflowSurfaceBehavior.EnsureParts` |
| 2 | 在宿主里自己 `new` 网格装饰器/小地图 | 两者是模板角色（适配器只留契约）；自造会与表面重复一套 | 模板条目实现 `IWorkflowGridDecorator`、派生 `WorkflowMinimapOverlay`，用 `GridDecoratorName`/`MinimapOverlayName` 接上 | `IWorkflowGridDecorator` / `IWorkflowMinimapOverlay` |
| 3 | `using VeloxDev.TransitionSystem.NativeSamplers;` | 这家在 `VeloxDev.Adapters.NativeSamplers`；另一个是 Core 自己的采样器 | 用 `VeloxDev.Adapters.NativeSamplers` | `Samplers/PaddingSampler.cs` 的命名空间 |
| 4 | 在 `AttachedBehaviors/` 里直接用 `Transition<T>` | 编译得过因为 `GlobalUsings.cs` 灌了命名空间，但跨轴零引用是七家纪律 | 保持零引用；跨轴的事在宿主侧接 | `GlobalUsings.cs` |
| 5 | 照 WPF 心智期待名字作用域 / 重名报错 | 视觉部件是递归子树顺序查找 + `Ordinal` 比较，取第一个命中，不抛不报 | 名字唯一，挂在视觉根子树里 | `WorkflowSurfaceBehavior.FindVisualNamed` |
| 6 | 不写 `CoordinateHostName` 就以为锚点另有默认 | 默认只找最近的视觉 `Canvas`，找不到才退到树根；锚点写在该宿主的局部坐标里 | 节点/插槽显式给 `CoordinateHostName`（模板用 `PART_Canvas`） | `WorkflowSlotLayoutBehavior.ResolveCoordinateHost` |
| 7 | 在视觉画布里用 `layout.Scale` 模拟缩放 | 视觉树要一条相机矩阵；树的 `Scale` 会被表面复位成 1 | 用 `WorkflowSurfaceBehavior` 的相机 | `ApplyCamera` |
| 8 | 把弹层/原生控件塞进视觉子树 | 原生 HWND 浮在视觉树之上，不能被变换/裁剪 | 用 `VisualPopup`；原生内容只经 `NativeControlHost` | `VisualHost` / `NativeControlHost` |

## 三、步骤清单

### 3.1 搭一张工作流表面

1. 根控件（或 XAML 根）派生 `VisualElement`；写 `PART_Canvas` / `PART_ScrollViewer` / `PART_GridDecorator` 的 `x:Name`。
2. `new VisualHost { Root = treeRoot }`，在宿主上设 `IsEnabled`、`ZoomEnabled`、`VisualTemplateSelector`（或让 tree 资源里的 `VisualTemplateSelectorKey` 解析）与 `*Name`。
3. 节点/插槽/连线是视觉 `UserControl`；tree 用 `DataTemplate` 包住它们，`PART_Canvas` 挂 `behaviors:ViewPool.ItemsSource="{Binding Helper.VisibleItems}"` 与 `behaviors:ViewPool.TemplateSelector`。
4. 节点/插槽模板在根上挂对应的 `WorkflowSlotLayoutBehavior` / `WorkflowNodeDragBehavior` / `WorkflowSlotConnectionBehavior`；网格/小地图：模板条目实现 `IWorkflowGridDecorator`、派生 `WorkflowMinimapOverlay`，按名字接上。
5. `SetWorkflowTree(host, tree)`；模型变更后 `Refresh(host)`。

### 3.2 加一个采样器

1. `TransitionSystem/Samplers/XxxSampler.cs`，`namespace VeloxDev.Adapters.NativeSamplers`，实现 `ISampler`（`PaddingSampler.cs` 是唯一先例）。
2. 在 `TransitionSystem/Interpolator.cs` 静态构造里 `RegisterInterpolator(typeof(T), new XxxSampler());`。
3. 要能被 `Transition` 面用上，可能需要在 `TransitionSystem/Transition.cs` 加 `Property` 重载。
4. **必须**给 `Src/Test/AutoTest/Samplers/WinFormsEntries.All` 加 `EntryFactory.Create<...>`：`SamplerCoverageTests` 会反射比对，漏了直接失败。

### 3.3 加一个主题转换器

1. 在 `DynamicTheme/ThemeValueConverters.cs` 加一个 `IThemeValueConverter`（按目标类型键，无需注册）。
2. 本文件与 `System.Drawing` 多个同名类型共存，用框架那个必须全限定。
3. 转换失败静默返回 `null` 是刻意的。

### 3.4 加一个 Win32 补偿

1. 在 `WorkflowSystem/Platform/NativeWindowStyleHelper.cs` 加常量与方法（`internal static`）。
2. `SetWindowLong` 的 index 不能凭常量值猜 —— 两个常量值相同（`0x02000000`）。
3. 至少加一个调用点，并处理「句柄还没建」：`ApplyStyle` 只在位缺失时才 `SetWindowPos`，`HandleCreated` 处有重挂。

### 3.5 加一个附着行为

1. `WorkflowSystem/AttachedBehaviors/WorkflowXxxBehavior.cs`，`public static class`。
2. `private sealed class XxxState` + `static readonly ConditionalWeakTable<VisualHost, XxxState>`。
3. 每个属性一对 `GetXxx(VisualHost)` / `SetXxx(VisualHost, T?)`；null 抛 `ArgumentNullException`。
4. 名字通道走 `FindVisualNamed`（私有，在 `WorkflowSurfaceBehavior`）。
5. 要不要进 `Refresh`：每次刷新要推值就改 `WorkflowSurfaceBehavior.Refresh`；否则自己订阅。
6. **零注册**：加完就能被宿主调用。

## 四、联动清单

### 4.1 改任何 `Set*` / `Get*` 的名字或语义

- [ ] **本模块 README**（`Src/Adapters/VeloxDev.WinForms/README.md`）
- [ ] **七个模板条目**（`Src/Templates/VeloxDev.WinForms.Templates/working/content/*/`：tree 是视觉 `UserControl` + `WorkflowSurfaceBehavior`；node/slot/link 是视觉视图；grid/minimap/selector 纯 `.cs`）
- [ ] **两个 Workflow demo**：`Examples/Workflow/WinForms/Demo/` 与 `Examples/Workflow/WinForms Trimmed/Demo/`
- [ ] `skills/veloxdev-create-workflow/references/practices/gui/winforms.md`、`winforms-xaml.md`
- [ ] `memory/modules/{WorkflowSystem,Templates}/adapters/winforms.md` + 本文

### 4.2 加 / 换一个视觉模板角色

- [ ] 派生类放宿主工程（基类在适配器里）；模板要默认产出就改对应 `TemplateClass`
- [ ] README 的 `Workflow surface` / `Behaviors` 表
- [ ] 两个 demo 与 `Src/Templates/VeloxDev.WinForms.Templates/`

### 4.3 加一个采样器

- [ ] `TransitionSystem/Samplers/` 新文件 + `Interpolator` 静态构造注册
- [ ] **`Src/Test/AutoTest/Samplers/WinFormsEntries.All`** —— 漏了被 `SamplerCoverageTests` 判失败
- [ ] 端点值造不出来 → 进 `UnreachableSamplers.All`
- [ ] `TransitionSystem/adapters/winforms.md` 与本文

### 4.4 改 TFM / 包结构

- [ ] `VeloxDev.WinForms.csproj` 的 `TargetFrameworks`（四元组）
- [ ] `skills/veloxdev-create-workflow/references/practices/gui/winforms.md`
- [ ] `VeloxDev.slnx`

## 五、几个「以为能改、其实不该改」的地方

1. **`WorkflowSurfaceBehavior` 的 Win32 样式调用不要摘**：`EnsureClipChildren`/`EnsureComposited` 是自绘画布不闪的前提。
2. **消息过滤器的两个条件不要简化**：`ResolveSurfaceHost` 同时认 `_filterHost` 与父链；`ModifierKeys != Keys.Control` 是精确比较，改 `HasFlag` 会让 Ctrl+Shift+滚轮被吞。
3. **`WS_CLIPCHILDREN` 与 `WS_EX_COMPOSITED` 两个同值常量不要合并**。
4. **视觉相机与 HWND 坍缩的缩放方向相反**：视觉上滚 `zoom *= 1.1`，旧路径 `Scale *= 1/1.1`。
