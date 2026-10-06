# VeloxDev.Jalium — 扩展

> ⚠ **2026-10-05：Jalium 适配器已整体转成标记驱动（`.jalxaml`），与 WPF 逐行同形。**
> 本文下面凡提 `WorkflowTreeView` / 三个 `*Attachment` / `WorkflowPortGeometry` / `WorkflowPortLayout` /
> `IWorkflowTemplateSelector` / `WorkflowTemplateSelector` / `WorkflowNodeView` / `WorkflowSlotView` /
> `WorkflowLinkView` 的段落**都已作废**（那些类型全部删除）。现行落点见
> [WorkflowSystem/adapters/jalium.md](../../WorkflowSystem/adapters/jalium.md) §一 与 §〇。

> 读法：七角色契约、附着属性名、注册位置在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3 —— **Jalium 侧这七个角色现在由适配器的附着行为 + 静态助手提供（网格装饰器归模板），模板是标记 + 薄 code-behind**（见下 §一）。
> 两条轴上「这家和别家不一样」的地方在 `memory/modules/{TransitionSystem,WorkflowSystem,Templates}/adapters/jalium.md`。
> 本文**只回答一件事**：在这个项目里加一个 X，动哪几处、按什么顺序、哪条看着能编译的捷径是错的。
> 目录与轴的对照、登记表所在、csproj 的取值、陷阱清单见 `architecture.md`，本文不抄。

---

## 一、扩展点地图

| 我想加 | 官方挂点（具体成员） | 位置 |
|---|---|---|
| 让一个 Jalium 类型可动画 | 实现 `ISampler`，再 `RegisterInterpolator(typeof(T), new XSampler())` | `PlatformAdapters/Interpolator.cs:13-22`（唯一的登记表，10 行） |
| 换线程句柄 / 优先级 / 解释器 | 改 `TransitionSchedulerCore<UIThreadInspector, TransitionInterpreter, DispatcherPriority>` 的类型实参 | `PlatformAdapters/TransitionScheduler.cs` |
| 一条过渡时长预设 | `TransitionEffects` 的三个静态属性（可 `set`，进程级） | `PlatformAdapters/TransitionEffects.cs:6` / `:11` / `:16` |
| 让 `.Property(...)` 能顺滑写某个 Jalium 类型 | 加一个 `Property(Expression<Func<T, TXxx>>, TXxx, object?)` 重载 | `PlatformAdapters/Transition.cs`（**这一家 13 个**） |
| 加 / 换工作流表面 | 在 item template 的 `UserControl` 根上写 `behaviors:WorkflowSurfaceBehavior.*`（具名部件 + `ZoomEnabled` + `LinkMenuKey`）；要改手势/命中/视口/菜单数学就改表面行为 | `Src/Templates/…/workflow-tree-view/TemplateClass.jalxaml`、`Attached/Workflow/WorkflowSurfaceBehavior.cs` |
| 卡片长什么样 | 改 `workflow-node-view` 的标记（`UserControl` + `Viewbox` + 设计尺寸 `Grid`），节点拖拽挂 `WorkflowNodeDragBehavior` | `Src/Templates/…/workflow-node-view/TemplateClass.jalxaml` |
| 换连线外观（颜色 / 线宽 / 命中 / 虚线段） | 改 `workflow-link-view` 的 code-behind（`UserControl`，`LineColor` DP + `TemplateLinkColor`/`TemplateLinkThickness` 符号）；自盒化助手在适配器 | `Src/Templates/…/workflow-link-view/TemplateClass.jalxaml.cs`、`Attached/Workflow/WorkflowLinkBounds.cs` |
| 换网格 / 标尺外观 | 改 `workflow-grid-decorator`（`sealed class : Grid, IWorkflowGridDecorator`，**整个渲染器都在这一个文件里**），设调色板与 `GridStep` / `MajorLineEvery` | `Src/Templates/…/workflow-grid-decorator/TemplateClass.cs` |
| 端口位置（`slot.Anchor`）与卡片设计尺寸 | `slot.Anchor` 由 `WorkflowSlotLayoutBehavior` 量测写回；卡片设计尺寸写在该条目的标记里（`Viewbox` 内固定 260×180） | `Attached/Workflow/WorkflowSlotLayoutBehavior.cs`、`Src/Templates/…/workflow-node-view/TemplateClass.jalxaml` |
| 端口图形 | 改 `workflow-slot-view` 的标记（`UserControl` + `Path`）与 code-behind 的 `SlotState` 配色；连接手势挂 `WorkflowSlotConnectionBehavior` | `Src/Templates/…/workflow-slot-view/TemplateClass.jalxaml(.cs)` |
| 换「item 类型 → 视图」的分派 | 在 tree-view 模板的 `Resources` 里改 `DataTemplate` 与 selector（现在是 `sealed class : DataTemplateSelector`） | `Src/Templates/…/workflow-template-selector/TemplateClass.cs` |
| 换池化行为 | 改 `ViewPool` 的两个附着属性 / `ViewManager` 的集合同步（分批大小在 `ProcessNextBatch`） | `Attached/Workflow/ViewPool.cs`、`ViewManager.cs` |
| 小地图换皮 | 继承 `WorkflowMinimapOverlay`（`public class`，可继承），设四色；模板只产出一个薄派生 | `Attached/Workflow/WorkflowMinimapOverlay.cs` |
| 让 DynamicTheme 在这一家真动画 | 覆写 `InterpolatorCore.CreateScheduler` —— **已实现但本仓库无人调用** | `PlatformAdapters/Interpolator.cs:26-29`；理由见 `architecture.md` §一 |

**这里没有的扩展点（别去找）：**

- **「表面自己在模板里做一遍机械」这条路已经堵上。** 这些**平台机制都在适配器附着行为里**：`WorkflowSurfaceBehavior`（手势/视口/命中/菜单）、`WorkflowSlotLayoutBehavior`（量测写回锚点）、`WorkflowNodeDragBehavior`（节点拖拽）、`WorkflowSlotConnectionBehavior`（连接手势）、`WorkflowEvents`（模型事件转接）、`WorkflowLinkBounds`（连线自盒化）。**要加 / 改这些行为就改对应行为，不要再去模板里造第二份** —— 但**网格/标尺是例外**：它整块在模板的 `workflow-grid-decorator` 里（适配器只留 `IWorkflowGridDecorator` 接口）。
- **没有程序集级入口，也没有 `Initialize()`**：全模块唯一静态构造是 `Interpolator.cs:12`。
- **没有 `ThemeValueConverters.cs`，也没有主题 demo**：这一条轴在这家是空的（`architecture.md` §一）。
- **有标记语言**（`.jalxaml`），所以宿主本来就不需要自己 `NameScope.SetNameScope` + `RegisterName`：表面与槽布局行为都用 `FrameworkElement.FindName` 在模板的 name scope 里按 `x:Name` 取部件（`.jalxaml` 工具链 + `FindName` 已实测可用，见 `WorkflowSystem/adapters/jalium.md` §2.2）。
- **没有 `adapters/` 子目录**：模块名本身就是平台。
- **没有「注册一个 ViewModel 类型」的地方**：适配器不 `new` Tree/Node，只消费宿主给的模型。

---

## 二、官方做法 vs 看着能编译、但错的捷径

**这一节是全文最重要的部分。** 下面每一条走捷径都能编译通过，其中几条跑起来还像是好的。

| # | 错的捷径 | 为什么错 | 官方做法 | 依据 |
|---|---|---|---|---|
| 1 | 在 `Samplers/` 加一个 `ISampler` 类就以为生效了 | 登记表是唯一开关；没登记 = 该采样器**永不运行**，且库里没有任何东西会报错 | 同时在 `Interpolator` 的静态构造里加一行 | `PlatformAdapters/Interpolator.cs:13-22` |
| 2 | 登记了但忘了改测试表 | 不会编译错，运行到 `EveryShippedSampler_IsAccountedFor` 才红：它反射所有 `VeloxDev.*` 程序集里的 `ISampler`，再与 `SamplerRegistry.Entries` ∪ `UnreachableSamplers.All` 求差 | `Examples/Transition/AUTO TEST/Samplers/JaliumEntries.cs` 加一条 `Entry(...)`（`All` 在 `:174`，`Adapter` 常量在 `:29`） | `Samplers/SamplerCoverageTests.cs:63-85`；`SamplerRegistry.cs` → `AdapterSamplerEntries.cs:15` → `JaliumEntries.All` |
| 3 | 改采样器类名只改文件 | 测试按**字符串**反射取类型：`Type.GetType($"VeloxDev.Adapters.NativeSamplers.{name}, {JaliumAssembly}", throwOnError: true)`。编译不报错，**测试运行时才炸** | 改名要同步 `JaliumEntries.cs` 里那 9 个字符串 | `JaliumEntries.cs:34`（`throwOnError: true`） |
| 4 | 把登记代码写在采样器自己的文件里 / `App` 里 / `Main` 里 | 没人保证它会跑在第一个 `new Transition<T>()` 之前；`RegisterInterpolator` 是**末位胜出** | 只在静态构造里集中登记 | Core `Effects/Transition.cs` 的字段初始化器 |
| 5 | 照 WPF 的样子「登记基类型接住整族」，于是给 `TranslateTransform` / `RotateTransform` / `TransformGroup` 各登记一条 | 这家的姿态是**只登记基类型**（`typeof(Transform)`）＋ 精确类型的少数几条；多余登记会被精确命中抢先 | 只登记 `Transform`（`:21`）与 `Media3D.Transform3D`（`:22`）两条 | `PlatformAdapters/Interpolator.cs:21-22` |
| 6 | 只设 `ViewPool.ItemsSource`（或只设 `TemplateSelector`）就以为视图会出来 | 两个 DP 共用 `OnChanged`，只有**两者都非空**才建 manager；否则走 `else` 分支把 manager `Detach()` | 两个一起给 —— 模板里两者都写在 `PART_Canvas` 上（或由表面行为从资源按 `WorkflowSurfaceBehavior.TemplateSelectorKey` 一并设） | `Attached/Workflow/ViewPool.cs:45`/`:55-70`；`WorkflowSurfaceBehavior.cs:681-689` |
| 7 | 把 `Property(…, ICollection<Transform>)` 的「单个直接赋值」分支改成统一 `TransformGroup` | 会改运行时类型，破坏 `((TranslateTransform)x.RenderTransform).X` 这类嵌套路径 | 保持 `Count == 1` 直赋、其余包组 | `PlatformAdapters/Transition.cs:54-72`（注释 `:58-59`） |
| 8 | 把 `.Property(...)` 的某个重载「补进 Core」 | Core 的 `TransitionCore<...>` 里**一个 `Property` 都没有**，13/28/30/17 个重载分别写在各家适配器里 —— 补进 Core 等于改七家的形状 | 重载留在本家 `PlatformAdapters/Transition.cs` | `Src/Core/VeloxDev.Core/TransitionSystem/Effects/Transition.cs` 全文无 `Property` 方法 |
| 9 | 让 `Attached/` 里的东西直接驱动一个 `Transition<T>`（或反过来） | 两条轴在程序集内互不引用是既成事实；跨一条就再也拆不开 | 想跨轴就在宿主侧接线 | `architecture.md` §一 |
| 10 | 按别家的条数猜这一家有几种采样器 | 条数本来就不等（Jalium 9 个类 / 10 条登记；WPF 12、Avalonia 14、WinUI 10、WinForms 1、Razor 1） | 按 `Interpolator.cs:13-22` 现读 | 同左 |
| 11 | 在模板/demo 里重写适配器行为已经做掉的机制（自己管缩放/视口、自己算端口锚点、自己写连线自盒化） | 机制与策略会分叉；两边都会编译通过，出问题时看不出谁是对的 | 平台机制改 `Attached/Workflow/` 下的行为 / 助手，策略留模板标记或 code-behind，判据见 `adapter-base-class-specifications.md` §2.1 | `architecture.md` §四；`Src/Templates/…/workflow-*`（表面/卡片/槽/连线是标记 + 薄 code-behind，网格/小地图/选择器是 `.cs`） |

---

## 三、步骤清单

### A. 让一个新的 Jalium 类型可动画（加一个采样器）—— 最常见的路径

1. **建文件** `PlatformAdapters/Samplers/XxxSampler.cs`，命名空间 **`VeloxDev.Adapters.NativeSamplers`**（七家共用这个名字），实现 `ISampler`。端点约定：`t == 0` / `t == 1` 原样交出调用方给的起点/终点实例（范式见 `PlatformAdapters/Samplers/TransformSampler.cs`）。
2. **登记**：`PlatformAdapters/Interpolator.cs:13-22` 加一行 `RegisterInterpolator(typeof(X), new XxxSampler());`。要登记的是**基类型**还是**精确类型**，照 §二·5 的姿态选。
3. **补验证表**（**最容易漏的一步**）：`Examples/Transition/AUTO TEST/Samplers/JaliumEntries.cs` 加 ① 私有 `Target` 类上的一条属性（`:42`），② 一条 `Entry("XxxSampler", SamplerRule.…, start, end, x => x.属性, t => 闭式解)`，放进 `All`（`:174`）。漏了不会在库里报错，但 `EveryShippedSampler_IsAccountedFor` 会红。
4. **可选**：给 `.Property(...)` 加一个对应重载（§三·D），否则调用方只能走泛型 `Property<TValue>`。
5. **同步 `memory/modules/TransitionSystem/adapters/jalium.md`** 的条数与清单。

> 判断「我登记对了没」：`RegisterInterpolator` 返回什么也不告诉你，**没有「列出全部登记项」的公开 API**。最快的自查是跑一次 `Examples/Transition/Jalium/Demo/`。

### B. 加 / 换一个工作流视图角色

**这条路现在是「标记 + 薄 code-behind + 适配器附着行为」。** 平台机制都在适配器行为里（§一），模板只写形状与绑定：

1. **表面**：item template 的 `UserControl` 根写 `behaviors:WorkflowSurfaceBehavior.*` 具名部件（`ScrollViewerName`/`CanvasName`/`GridDecoratorName`/`PointerPressSourceName`/`MinimapOverlayName`），配上 `ZoomEnabled` 与 `LinkMenuKey`。要改**手势/命中/视口/菜单数学**时改 `WorkflowSurfaceBehavior.cs`，不在模板里补代码。
2. **卡片**：改 `workflow-node-view` 的 `.jalxaml`（`UserControl` + `Viewbox` + 设计尺寸 `Grid`，插槽控件的 `PART_*` 名要和 `WorkflowSlotLayoutBehavior` 的 `SlotNames` / `SlotEnumeratorNames` 对上）；标题栏挂 `WorkflowNodeDragBehavior`。**端口位置不靠卡片画** —— 由 `WorkflowSlotLayoutBehavior` 量测写回 `slot.Anchor`，命中读它。
3. **连线**：改 `workflow-link-view` 的 `.jalxaml.cs`（`LineColor` DP、`Refresh` 里的自盒化调用、`BuildCurve` 的烘焙、`OnRender` 的笔）。自盒化助手 `WorkflowLinkBounds` 在适配器里，别在模板里造第二份。
4. **网格/标尺**：改 `workflow-grid-decorator/TemplateClass.cs`（`sealed class : Grid, IWorkflowGridDecorator`），设调色板与 `GridStep` / `MajorLineEvery`。
5. **端口图形与连接手势**：改 `workflow-slot-view` 的标记与 code-behind（`SlotState` 配色），手势挂 `WorkflowSlotConnectionBehavior`。**没有 `WorkflowPortLayout` 值了** —— 端口位置是量测出来的。
6. **选择器**：改 `workflow-template-selector/TemplateClass.cs`（`sealed class : DataTemplateSelector`，四个 `DataTemplate?` 属性），tree-view 模板的 `Resources` 里构造它并交给 `ViewPool.TemplateSelector`（或由表面行为按资源键设）。
7. **模板与 Trimmed demo 是镜像**（`Src/Templates/…/working/content/` 与 `Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/`），同一笔改两处；改完跑 `Src/Verification/verify-workflow-item-templates-all.ps1 -Platform Jalium -Strict`（旧的 `verify-jalium-item-templates.ps1` 现在是它的转发器，调用照旧可用）。

### C. 让宿主接上适配器

Jalium 适配器对外提供：过渡轴的 `Transition<T>`、整套工作流附着行为（表面/槽布局/节点拖拽/连接手势/模型事件/自盒化助手 + 视图池两件套 + 小地图）。宿主接线现在**全是标记**（Trimmed demo 的 `MainWindow.jalxaml` + `Views/MainView.jalxaml.cs` 里没有一行接线 —— 那个文件只干两件事：`LoadTree` 造三个示例节点并配槽位选择器，然后把树交给 `DataContext`）：

1. **表面**：把 tree-view 条目的 `UserControl` 放进窗口，根上写 `behaviors:WorkflowSurfaceBehavior.*`。**`CanvasName` / `TemplateSelector` 缺一，池里就什么也不出**（`ViewPool` 两个 DP 要都非空）。
2. **池化自动完成**：`PART_Canvas` 上写 `behaviors:ViewPool.ItemsSource="{Binding Helper.VisibleItems}"` 与 `behaviors:ViewPool.TemplateSelector="{StaticResource WorkflowTemplateSelector}"`，树由 `DataContext` 进来（表面行为 `BindTree` 里 `host.DataContext as IWorkflowTreeViewModel`，`WorkflowSurfaceBehavior.cs:671-702`）。**没有 `AttachScrollViewer` / `SetTree` 这些宿主调用了。**
3. **小地图**：`MinimapOverlay` 写在标记里，`ScrollViewerName="PART_ScrollViewer"` —— `ResolveScrollViewer()` 自己 `FindName` 接上（`WorkflowMinimapOverlay.cs:160`/`:182-188`），不必宿主赋 `ScrollViewer`。
4. **缩放与视口全归表面**：`ZoomEnabled="True"` 之后 Ctrl+滚轮由 `WorkflowSurfaceBehavior.OnZoomPreviewMouseWheel` 处理，`ZoomPin`（250ms）与 `NotifyZoomCommitted` 都在表面内部（`:613`/`:626`）。**宿主零调用者。**

### D. 给 `.Property(...)` 加一个重载

1. `PlatformAdapters/Transition.cs` 内，形状一律是：`public Transition<T> Property(Expression<Func<T, TXxx>> propertyLambda, TXxx newValue, object? interpolationOptions = null)`，体三行 —— `state.SetValue(...)` → 可选 `state.SetOptions(...)` → `return this;`。
2. 新重载**必须**与 `Interpolator` 的登记项对应；不对应的重载是死代码。
3. **不要**把重载搬到 Core：见 §二·8。

### E. 改 TFM / 引用方式

1. `VeloxDev.Jalium.csproj:7` 是**单 TFM、不带平台后缀**的 `net10.0`，`:4-6` 的注释写明了理由（只用跨平台核心，不用 `Jalium.UI.Desktop`）。改成 `net10.0-windows` 会让这个包**不再能服务 Linux / Android**。
2. `:29` / `:30` 是一对**互斥**的双轨（Debug `ProjectReference` / 非 Debug `PackageReference`），改一条要同时看另一条。
3. `Jalium.UI.Controls`（适配器，`:34`）与 `Jalium.UI.Desktop`（消费 demo）是**两个包、两个版本位**；升其中一个不必同步另一个，但 `Jalium.UI.Controls` 是「能编过 `Canvas`/`DrawingContext` 的最低包」，降级会缺绘制面。

---

## 四、联动清单（加一个 X 时必须同步修改的所有位置）

**漏一处通常不报错，只是静默少一个能力。**

### 4.1 加一个平台采样器

- [ ] `Src/Adapters/VeloxDev.Jalium/PlatformAdapters/Samplers/XxxSampler.cs`（命名空间 `VeloxDev.Adapters.NativeSamplers`）
- [ ] `Src/Adapters/VeloxDev.Jalium/PlatformAdapters/Interpolator.cs:13-22` 登记（**不登记 = 永不运行**）
- [ ] `Examples/Transition/AUTO TEST/Samplers/JaliumEntries.cs`：`Target` 属性 + 一条 `Entry`（**漏了直接红**）
- [ ] 要进流的 `.Property(...)` 重载：`Src/Adapters/VeloxDev.Jalium/PlatformAdapters/Transition.cs`
- [ ] 与 Core / 别家撞名时，命名空间留在 `VeloxDev.Adapters.NativeSamplers`（绕法是 `JaliumEntries.cs:34` 那条按程序集限定名的反射）
- [ ] `memory/modules/TransitionSystem/adapters/jalium.md`（条数与清单）

> **这一家没有 `UnreachableSamplers` 条目**（`UnreachableSamplers.cs` 的条目全是 WinUI / MAUI），所以**每一个** Jalium 采样器都必须在 `JaliumEntries.All` 里被闭式解验 —— 没有「先挂个理由放着」这条退路。这条退路本身也是可证伪的：`EveryUnreachableSampler_NeedsAValueThatCannotBeBuiltHere` 会真的去构造那个端点值（`SamplerCoverageTests.cs:88`）。

### 4.2 加 / 换一个工作流视图角色（现在走「标记 + 薄 code-behind + 适配器附着行为」）

- [ ] **平台机制**：先判断这段是不是扩展点（判据 §2.1 in `adapter-base-class-specifications.md`）。是机制 → 改 `Src/Adapters/VeloxDev.Jalium/Attached/Workflow/` 下对应的附着行为 / 助手；是策略（形状、配色、间距）→ 留在模板或 demo 的标记 / code-behind。
- [ ] **表面 / 卡片 / 连线 / 网格 / 选择器**：`Src/Templates/VeloxDev.Jalium.Templates/working/content/` 下对应条目（`workflow-tree-view` / `-node-view` / `-slot-view` / `-link-view` / `-grid-decorator` / `-minimap-overlay` / `-template-selector`）与镜像 `Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/`（**同形，两处一起改**）。
- [ ] 若新表面要池化：在 `PART_Canvas` 上同时给 `ViewPool.ItemsSource` 与 `ViewPool.TemplateSelector`（**两个都要**）
- [ ] 两套 demo 都要过：`Examples/Workflow/Jalium/Demo/`（自己写的 `NodeEditorSurface : Canvas`，不接适配器行为）与 `Examples/Workflow/Jalium Trimmed/Demo/`（标记 + 行为那条）
- [ ] 跑 `Src/Verification/verify-workflow-item-templates-all.ps1 -Platform Jalium -Strict`（旧的 `verify-jalium-item-templates.ps1` 是它的转发器）
  - 这个通用校验（2026-10-04 起）能抓到旧 Jalium 脚本漏掉的一类**pack 缺陷**：模板引用了自己 pack 未声明的符号 —— 例如 `jalium-v-tree` 曾调用 `ColorConverter.ConvertFromString("TemplateLinkColor")` 而 tree pack 无 `linkColor` 符号，字面量直达运行期；旧脚本在比较前把这个调用归一化掉，所以看不见。已改为模板硬编码颜色、七个 tree pack 的参数集保持一致。
- [ ] `skills/veloxdev-create-workflow/references/gui/jalium.md`
- [ ] `memory/modules/WorkflowSystem/adapters/jalium.md`
- [ ] `memory/modules/Templates/adapters/jalium.md`（模板侧形状变了才动）
- [ ] `VeloxDev.slnx` **只在新增项目时**才动

---

## 五、几个「以为能改、其实不该改」的地方

1. **`TransitionEffects` 的三个时长**（`PlatformAdapters/TransitionEffects.cs:6/11/16`：`Empty` 0s、`Theme` 0.46s、`Hover` 0.32s）**六家逐字相同** —— 改这里等于改所有平台的默认观感。
2. **`Interpolator.cs:20` 那条看似冗余的 `SolidColorBrush` 登记不要顺手删。** 它确实被 `typeof(Brush)`（`:19`）的基类回溯覆盖，删了多半不报错也不改行为 —— 但它防的是「属性声明成 `SolidColorBrush`」这一类，代价为零。**要删就先跑一遍 `Examples/Transition/Jalium/Demo/`。**
3. **`ZoomPin` / `NotifyZoomCommitted` 现在在 `WorkflowSurfaceBehavior` 里**（`WorkflowSurfaceBehavior.cs:87` 的 `ZoomPin` 字段、`:297-327` 的 `NotifyZoomCommitted`、`:770-789` 的 `UpdateViewport`），不要搬到宿主窗口，也不必在模板里重造。缩放手势本身归表面（`ZoomEnabled` + `OnZoomPreviewMouseWheel`，`:613`/`:626` 自己调），宿主**零调用者**。
4. **七家的采样器类名不要「统一化」**（如 `PointSampler` → `JaliumPointSampler`）：命名空间的跨家重名是既成事实，改名只会让两处字符串表（`JaliumEntries.cs:34`、以及别家同名反射）同时错位。
5. **标尺厚度 `36` 的单一来源在模板**：`workflow-grid-decorator/TemplateClass.cs:34` 的 `public const double DefaultRulerThickness = 36;`（它同时是 `RulerThickness` DP 的默认值）。适配器不抄这个常量 —— 表面从解析到的装饰器读接口属性 `IWorkflowGridDecorator.RulerBand`，只在**拿不到装饰器时兜底**写 `?? 36d`（`WorkflowSurfaceBehavior.cs:742`）。旧记忆里「`WorkflowGridDecorator.cs:25` 的 `const RulerThickness`，表面/节点卡/连线三处都读它」已作废（那个类已删）—— 改厚度改模板那一个常量。
6. **`NoWarn` 现在只是 `1573;1591`（`VeloxDev.Jalium.csproj:14`）**：旧记忆里的 `8605;8604` 触发点（DP 的 CLR 包装拆箱）已消失（模板里值类型 DP 走泛型 `Read<T>`），不要按旧记忆去「保留」它们。
7. **`ViewPool` 的两个附着属性共用 `OnChanged`**（`ViewPool.cs:45`）：任何「只改一个」的想法都会重建 manager（`Attach` 第一行 `Detach`），节点入场动画/局部状态全部重来。
