# Templates — Jalium

> ⚠ **2026-10-05：Jalium 适配器已整体转成标记驱动（`.jalxaml`），与 WPF 逐行同形。**
> `WorkflowTreeView` / 三个 `*Attachment` / `WorkflowPortGeometry` / `WorkflowPortLayout` /
> `IWorkflowTemplateSelector` / `WorkflowTemplateSelector` / `WorkflowNodeView` / `WorkflowSlotView` /
> `WorkflowLinkView` 这些类型都不存在；本文下面凡提这些名字的段落，落点以
> [WorkflowSystem/adapters/jalium.md](../../WorkflowSystem/adapters/jalium.md) §一 与 §〇 为准。

> **本文只写模板侧独有的东西**：条目产出什么形状、哪些接线必须手写、这一家模板特有的坑。
> 契约（七角色、附着属性、注册位置）在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；
> **表面侧**（`Visual.ShouldRenderChild` 自盒化、`CanvasTransform` 通道、`ZoomPin`）现在都在**适配器的附着行为**里，
> 见 `memory/modules/WorkflowSystem/adapters/jalium.md`，本文只指路不抄；
> 包结构与跨平台族划分在 `../architecture.md` 与 `../extension.md`。
> **路径写法**：下文裸文件名都相对 `Src/Templates/VeloxDev.Jalium.Templates/working/content/`；
> 提到 demo 时相对仓库根的 `Examples/Workflow/Jalium Trimmed/`；引用别处一律写全路径。

代码目录：`Src/Templates/VeloxDev.Jalium.Templates/working/content/workflow-<角色>/`
（注意：csproj 与 `content/` **不同在 `working/` 下**，本家是唯一的结构离群，见 `../architecture.md` §二）。

---

## 一、这一家的条目产出什么形状

**七个条目里，四个产出 `.jalxaml` + `.jalxaml.cs` 一对**（tree / node / slot / link），**三个只产出一个 `.cs`**（grid-decorator / minimap-overlay / template-selector）。标记那四个 `primaryOutputs` 是 2 条，`.cs` 那三个是 1 条。四个标记产物的 code-behind 都是极薄的 `partial class : UserControl` —— 只有 `InitializeComponent()`（tree-view 多一个 `CanvasTransform` 再暴露），真正的行为在适配器的附着行为里。

| 条目 | 产物（行数） | 扮演什么 | 关键锚点 |
|---|---|---|---|
| tree-view（jalxaml 68 + cs 25 = 93） | `UserControl`（标记）+ `partial class` | 表面：根上写 `behaviors:WorkflowSurfaceBehavior.*` 具名部件；`Resources` 里 `NodeTemplate`／`LinkTemplate`／`TemplateSelector`／`ContextMenu`；`PART_*` 网格/滚动/画布/小地图 | `:8-15`（行为）、`:21-28`（NodeTemplate）、`:37-39`（selector）、`:44-67`（部件） |
| node-view（jalxaml 76 + cs 9 = 85） | `UserControl`（标记）+ `partial class` | 卡片：`Viewbox` + 设计尺寸 `Grid(260×180)`；`SlotNames`／`SlotEnumeratorNames` 指 `PART_InputSlot`／`PART_OutputSlots`；标题栏挂 `WorkflowNodeDragBehavior` | `:7-12`（行为）、`:16-17`（设计尺寸）、`:29-32`（拖拽） |
| link-view（jalxaml 5 + cs 314 = 319） | `UserControl`（标记）+ `partial class`（实现在 code-behind） | 连线：`LineColor`／`CanRender` DP + `StartLeft/Top`／`EndLeft/Top` 端点 DP，`Refresh` 自盒化、`BuildCurve` 烘焙、`OnRender` 画 | `cs:68-112`（DP）、`cs:212-245`（Refresh）、`cs:249-299`（Render/BuildCurve） |
| grid-decorator（449） | `sealed class TemplateClass : Grid, IWorkflowGridDecorator` | 网格/标尺：**整个渲染器都在这里** —— 两面自绘子层（世界网格 + 两条浮动标尺）、笔刷缓存、`GridStep`／`MajorLineEvery`／七色 | `:31`（类）、`:34`（`DefaultRulerThickness = 36`）、`:139-140`（符号）、`:324-405`（DrawGrid/DrawRulers） |
| slot-view（jalxaml 12 + cs 48 = 60） | `UserControl`（标记）+ `partial class` | 端口图形：标记里 `Path` + `WorkflowSlotConnectionBehavior`；code-behind 按 `SlotState` 算 `Foreground` 配色 | `jalxaml:7`（手势）、`jalxaml:9-10`（Path）、`cs:35-47`（配色） |
| minimap-overlay（24） | `class TemplateClass : WorkflowMinimapOverlay` | 薄派生：构造器设 `MinimapBackground`／`MinimapBorderBrush`／`NodeBrush`／`ViewportStroke` 四色（**不是空构造器**） | `:10`（类）、`:12-18`（四色） |
| template-selector（49） | `sealed class TemplateClass : DataTemplateSelector` | 分派：四个 `DataTemplate?` 属性（`NodeTemplate`／`SlotTemplate`／`LinkTemplate`／`TreeTemplate`），`SelectTemplate` 逐个查、缺了抛 | `:21`（类）、`:24-33`（四个属性）、`:36-48`（选择） |

⇒ 三条"读完文件才知道"的推论：

1. **`workflow-slot-view` 现在是标记 + 薄 code-behind**：`UserControl` 里一个 `Viewbox` 包 `Path`（`Data="TemplateSlotPath"`，`Fill` 绑 `Foreground`），code-behind 只按 `SlotState` 写 `Foreground` 四色（`cs:37-46`），连接手势由标记里的 `WorkflowSlotConnectionBehavior` 承担。**端口位置不再是模板里的一个 `Layout` 值** —— 由 `WorkflowSlotLayoutBehavior` 量测写回 `slot.Anchor`（见 `WorkflowSystem/adapters/jalium.md` §2.3）。旧版那套「表面/卡片/连线三处共读同一个 `WorkflowPortLayout` 实例」已随 `WorkflowPortLayout` / `WorkflowPortGeometry` 一起删除。
2. **`grid-decorator` 仍是 `.cs`，但形态变了**：`sealed class TemplateClass : Grid, IWorkflowGridDecorator`（**不再是派生适配器基类** —— 没有那个基类了）。两面自绘子层在构造器里装配，`RulerThickness` DP 默认值就是 `DefaultRulerThickness = 36`（`:34`）。整个渲染器都在这一个文件里 —— 改网格外观/间距/刻度就改它。
3. **`selector` 条目现在是 `DataTemplateSelector` 的派生类**（不是工厂方法）：tree-view 模板在 `Resources` 里 `new` 它（把 `NodeTemplate`／`LinkTemplate` 两个 `StaticResource` 赋进去），交给 `ViewPool.TemplateSelector`。`SlotTemplate`／`TreeTemplate` 未设 ⇒ 槽/树 item 一旦进池就抛 `InvalidOperationException`（`:41-46`）—— 这两类 item 从不进本仓库的池。

---

## 二、模板里必须手写、委派不掉的接线

### 2.1 表面 = 标记（根上挂行为）+ 薄 code-behind

七个条目里只有 tree-view 的产物含**任意**装配，而且它现在是标记：`UserControl` 根上写五个 `behaviors:WorkflowSurfaceBehavior.*` 具名部件（`ScrollViewerName`/`CanvasName`/`GridDecoratorName`/`PointerPressSourceName`/`MinimapOverlayName`）+ `IsEnabled`/`ZoomEnabled`/`LinkMenuKey`（`tree-view:8-15`），`Resources` 里放两个 `DataTemplate` 与 `TemplateSelector`。画布手势、命中、视口虚拟化、菜单、`ZoomPin` 守卫都在适配器行为 `WorkflowSurfaceBehavior` 里 —— **模板里没有第二份**。改这些机制要改 `Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowSurfaceBehavior.cs`，不是在模板里补代码。code-behind 只把 `CanvasTransform` 附着属性用一个同名 CLR 属性再暴露一次（`tree-view:19-22`，因为本家绑定读不到括号路径）。

### 2.2 七份产物合起来还缺一个宿主窗口 —— 但宿主现在也是标记

模板不含窗口/入口（与 `../architecture.md` §一"不是 demo"一致）。**2026-10-05 起宿主层也是标记**：Trimmed demo 的 `MainWindow.jalxaml`（10 行）+ `MainWindow.jalxaml.cs`（9 行，只有 `InitializeComponent`）、`Views/MainView.jalxaml`（6 行）+ `MainView.jalxaml.cs`（49 行，只造样例数据并 `DataContext = tree`）。所以**装配不再是"七步手写代码"**，而是「把 tree-view 条目的 `UserControl` 放进窗口 + 给它一个树 DataContext」：

| # | 宿主必须做 | 依据 |
|---|---|---|
| 1 | 把 tree-view 产物的 `UserControl` 放进窗口（`<views:MainView />` → `<local:TreeView />`） | `Demo/MainWindow.jalxaml:9`、`Demo/Views/MainView.jalxaml:5` |
| 2 | 备一个 `IWorkflowTreeViewModel` 并设成 `DataContext`（表面从 `DataContext` 取树，`WorkflowSurfaceBehavior.BindTree`） | `MainView.jalxaml.cs:23-25`、`WorkflowSurfaceBehavior.cs:671-702` |
| 3 | **其余全部在标记里**：具名部件、`ViewPool.ItemsSource`/`TemplateSelector`、`MinimapOverlay` 的 `ScrollViewerName`、`LinkMenuKey` —— 都不用宿主写代码 | `Demo/Views/Workflow/TreeView.jalxaml:9-16`、`:58-59`、`:63-67` |

⇒ 宿主只做上面三条，其余全在标记里；`AttachScrollViewer` / `SetTree` / 订阅 `ScrollChanged` 这些 API 都不存在。漏了 DataContext 只会让 `BindTree` 空转（什么都不显示）。

### 2.3 跨条目的**编译期**耦合：tree 少生成一条兄弟就编译不过

tree-view 的标记 `xmlns` 引用这些兄弟条目的类型：`workflowViews:NodeView` / `workflowViews:LinkView`（两个 `DataTemplate` 里，`:22`/`:30`）、`workflowViews:TemplateSelector`（`:37`）、`workflowViews:GridDecorator`（`:50`）、`workflowViews:MinimapOverlay`（`:62`）。
node-view 的标记引用 `local:SlotView`（`:41`、`:65`）。
⇒ **只生成 `jalium-v-tree` 会 CS0246**（缺 `NodeView`/`LinkView`/`TemplateSelector`/`GridDecorator`/`MinimapOverlay` 五个类型），**只生成 `jalium-v-node` 会缺 `SlotView`**。
slot-view / link-view / grid-decorator / minimap-overlay / selector **都不引用兄弟条目**（连线端点、端口位置全靠适配器行为/绑定），所以耦合面比旧版小。与 WinForms 那条同源，见 `../architecture.md` §五 与 `winforms.md` §三·P1。

### 2.4 符号是**内联进表达式**的，所以数值符号只能用数值

| 写法 | 依据 | 含义 |
|---|---|---|
| `GridStep = TemplateGridSpacing;` / `MajorLineEvery = TemplateMajorLineEvery;` | `grid-decorator/TemplateClass.cs:139-140` | 内联进 C# 表达式。`gridSpacing` 默认值 `'40d'` 在这里**是合法的 C#**（`40d`）—— 与 Razor 必须剥掉这个 `d` 正好相反；`majorLineEvery` 默认 `5` 是 int |
| `BorderThickness="TemplateNodeBorderThickness"` / `CornerRadius="TemplateNodeCornerRadius"` | `workflow-node-view/TemplateClass.jalxaml:26-27` | 内联进标记属性；默认 `1` / `6`，Jalium 按 Thickness / CornerRadius 解析 |
| `const double thickness = TemplateLinkThickness;` | `workflow-link-view/TemplateClass.jalxaml.cs:267` | **double** |
| `ColorConverter.ConvertFromString("Template…Color")` | `grid-decorator/TemplateClass.cs:131-138`、`workflow-link-view/TemplateClass.jalxaml.cs:102`、`workflow-slot-view/TemplateClass.jalxaml.cs:45` | C# 侧的颜色一律是**字符串** |
| `Background="TemplateSurfaceBackground"` / `Foreground="TemplateNodeForeground"` / `Data="TemplateSlotPath"` | `workflow-tree-view/TemplateClass.jalxaml:46`、`workflow-node-view/TemplateClass.jalxaml:12,24`、`workflow-slot-view/TemplateClass.jalxaml:6,10` | 标记侧的颜色/路径也是**字符串**字面量 |

⇒ 给 `gridSpacing`/`majorLineEvery`/`nodeBorderThickness`/`nodeCornerRadius`/`linkThickness` 传非数值（如 `40px`）
**生成时会成功、构建时才炸**（`dotnet new` 只做文本替换）。

---

## 三、这一家模板特有的坑

### P1 · 本家现在 **0 个**「没有 `replaces`」的空转 symbol（旧版 12 个已全部补上）

`../architecture.md` §7.1 的判据是「`type: parameter` 而**没有 `replaces`**」——`dotnet new --help` 收得下、命令行能传、不报错、也不替换文本。逐文件核当前七个 `template.json`：**每个 symbol 都带了 `replaces`** ⇒ Jalium 侧现在 **0 个**空转符号。

旧的 12 个（`gridBackground`×1、slot-view 四个、minimap-overlay 四个、tree-view 三个）**全部被接上了绘制面**：grid-decorator 现在真读 `TemplateGridBackground`（`workflow-grid-decorator/TemplateClass.cs:138`）、slot-view 真读 `TemplateSlotBackground`/`TemplateSlotColor`/`TemplateSlotPath`（`workflow-slot-view/TemplateClass.jalxaml:6,10`、`…jalxaml.cs:45`）、minimap-overlay 真读那四色（`workflow-minimap-overlay/TemplateClass.cs:14-17`）、tree-view 真读 `TemplateSurfaceBackground`/`TemplateSurfaceBorderBrush`/`…Thickness`/`…CornerRadius`（`workflow-tree-view/TemplateClass.jalxaml:46-49`）。**所以别再去补 `replaces` —— 已经补完了。**

⚠ 一处残留：**`slotBorderColor` 的 token（`TemplateSlotBorderColor`）在 Jalium 的 slot-view 产物里一处都不出现**（`git grep TemplateSlotBorderColor -- Src/Templates/VeloxDev.Jalium.Templates` 只命中它自己的 `template.json`）。它按 §7.1 的定义不算「空转」（有 `replaces`），但传 `--slotBorderColor` 实际仍什么都不改变 —— 新的 slot-view 是一条 `Path`，没有独立的边框面。

（`../architecture.md` §7.1 的跨平台表仍按旧版记着 Jalium 这 12 个；那份不在本文维护范围，以本节为准。）

### P2 · 标尺厚度 `36` 的单一来源在**模板的 grid-decorator**

旧的 P2 说厚度被复制三处、后来说收进适配器 —— 现在权威常量在**模板**里：`workflow-grid-decorator/TemplateClass.cs:34` 的
`public const double DefaultRulerThickness = 36;`（它同时是 `RulerThickness` DP 的默认值）。

| 位置 | 依据 |
|---|---|
| 权威常量 `public const double DefaultRulerThickness = 36;`（`RulerThickness` DP 默认值） | `Src/Templates/…/workflow-grid-decorator/TemplateClass.cs:34`、`:40-43` |
| 装饰器把接口属性映射过去：`public double RulerBand => RulerThickness;` | `Src/Templates/…/workflow-grid-decorator/TemplateClass.cs:258` |
| 表面读**接口属性** `IWorkflowGridDecorator.RulerBand`，拿不到装饰器才兜底 `?? 36d` | `Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowSurfaceBehavior.cs:742` |
| 模板侧**没有**第二个副本（link-view / node-view 都不再算标尺 reserve） | — |

⇒ **改厚度要改模板那一个常量**（或运行期改 `PART_GridDecorator.RulerThickness`）。本家也**没有**绑定式那条路
（WPF/Avalonia/WinUI/MAUI 是把 `TranslateTransform` 绑到 `PART_GridDecorator.RulerThickness`）。
跨平台对照见 `../architecture.md` §六·轴 1。

### P3 · 状态色现在在**模板**的 slot-view 里（读 `SlotState`）

- 四个插槽状态色由 `workflow-slot-view/TemplateClass.jalxaml.cs:35-47` 的 `UpdateForeground()` 算 —— 读的是 `SlotState`（一个 DP，由模板绑定送 `Slot.State`），写 `Foreground`，标记里的 `Path` 再 `Fill` 绑它。**不是每帧解析**：`SlotState` 变才重算一次。
- 对照 `../architecture.md` §7.4：这一家**不绑颜色属性、在 code-behind 里算** —— 状态色在模板的 slot-view 里算，没有适配器基类 `WorkflowSlotView`，也没有 `DrawCard`。

### P4 · link-view 的"自盒化"：助手在适配器，**调用在模板**

自盒化的实现是适配器的静态助手 `WorkflowLinkBounds.Apply`（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowLinkBounds.cs`，根因注释在 `:11-27`）；**调用方是模板产物** `workflow-link-view/TemplateClass.jalxaml.cs`：`Refresh()`（`:212`，摆盒调用 `WorkflowLinkBounds.Apply` 在 `:243`）把四个控制点交给助手摆盒，`BuildCurve()`（`:282-299`）画前把每个点减掉助手交回的原点。模板不只是"出线色" —— 它**必须**在正确时机调用这个助手，这是它在扩展点里的责任。机制说明见 `memory/modules/WorkflowSystem/adapters/jalium.md` §2.1。

### P5 · "深缩放不丢连线"的守卫在**适配器表面行为**里，宿主零调用者

`ZoomPin`（250 ms）+ `NotifyZoomCommitted` 这套 committed-target 守卫在 `WorkflowSurfaceBehavior`
（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowSurfaceBehavior.cs:87` 的 `ZoomPin` 字段、`:297-327` 的
`NotifyZoomCommitted`、`:770-789` 的 `UpdateViewport`）—— **不在模板产物里，也不由宿主调**：缩放手势本身归表面（`ZoomEnabled`），
`OnZoomPreviewMouseWheel` 算出提交目标后**自己**调 `NotifyZoomCommitted`（`:613`/`:626`）—— 缩放手势归表面，demo 宿主是标记、不调它。少了它，缩放后要等 helper 的 ~10 fps 脏计时器才重算可见集。

### P6 · 与 Jalium 自带类型的撞名（生成后通常要加别名）

| 撞什么 | 依据 |
|---|---|
| `Size` / `Offset` / `Anchor` 这些 Core 类型与 `Jalium.UI` 自带的重名 | demo 里显式 `using Size = VeloxDev.WorkflowSystem.Size;`（`Demo/Views/MainView.jalxaml.cs:5`）；模板的 grid-decorator 内部也把 `Jalium.UI.Size` 全限定（`workflow-grid-decorator/TemplateClass.cs:423`、`:439`，注释写明理由） |
| 生成的 `TreeView` 与框架自带的 `Jalium.UI.Controls.TreeView` | demo 现在把 `TreeView` 放在 `Demo.Views.Workflow` 命名空间、只用 `local:TreeView` 引用（`MainView.jalxaml:5`），**不再需要 `using WorkflowTreeView = …` 别名**（旧的 `MainWindow.cs:11-12` 那行已随宿主机标记化消失） |

### P7 · 两处"初始尺寸/兜底"都在**适配器表面行为**里，是刻意的

- `WorkflowSurfaceBehavior.CanvasWidth/CanvasHeight = 2000`（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowSurfaceBehavior.cs:48`、`:51`）只是**下界**：实际尺寸取 `Math.Max(2000, Layout.ActualSize)`（`UpdateCanvasSize`，`:744-754`），并用 `InvalidateMeasure()` 让宿主重新测量。
- 视口由具名 `ScrollViewer` 解析而来，表面同时挂 `ScrollChanged` **与** `SizeChanged`（`ResolveNamedParts`，`:468-489`），注释说明 Jalium 可能**不为视口尺寸变化发 `ScrollChanged`**；`UpdateViewport` 还在视口未测量时**退回整张画布**（`:802-810`），否则第一次虚拟化会在 0 尺寸视口上空转。

---

## 四、指路（这些结论已经在别处写好，本文不抄）

| 结论 | 在哪 |
|---|---|
| 表面侧机制（自盒化的 `Visual.ShouldRenderChild` 根因、`CanvasTransform` 通道与括号路径限制、`ZoomPin`、手势/命中/菜单） | `memory/modules/WorkflowSystem/adapters/jalium.md` §2；代码在 `Src/Adapters/VeloxDev.Jalium/Attached/Workflow/Workflow{SurfaceBehavior,SlotLayoutBehavior,NodeDragBehavior,SlotConnectionBehavior,Events,LinkBounds,MinimapOverlay}.cs` + `ViewPool.cs`/`ViewManager.cs` |
| 缩放枢轴 / `EnsureNegativeCover` / `ClampScrollOffset` 的模型侧语义 | `memory/modules/WorkflowSystem/extension.md` §3.9 与 `../extension.md` §4.1 的 #13 |
| 空转 symbol 的全局盘点（Jalium 侧现为 0） | `../architecture.md` §7.1 |
| 七家同一条目的结构差异（标尺 28/36、连线四族、minimap 薄壳 vs 自带实现） | `../architecture.md` §六 |
| 五类机械改动（本家产物同样源自 `Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/` 的同名文件，现在是同形的标记 + 薄 code-behind） | `../extension.md` §1.1 |
| 人面向的"怎么用这套模板" | `skills/veloxdev-create-workflow/references/gui/jalium.md` 的 `## Item templates` |
