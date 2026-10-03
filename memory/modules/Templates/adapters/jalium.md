# Templates — Jalium

> **本文只写模板侧独有的东西**：条目产出什么形状、哪些接线必须手写、这一家模板特有的坑。
> 契约（七角色、附着属性、注册位置）在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；
> **表面侧**（`Visual.ShouldRenderChild` 自盒化、纯模型数学、`_zoomPin`）现在都在**适配器基类**里，
> 见 `memory/modules/WorkflowSystem/adapters/jalium.md`，本文只指路不抄；
> 包结构与跨平台族划分在 `../architecture.md` 与 `../extension.md`。
> **路径写法**：下文裸文件名都相对 `Src/Templates/VeloxDev.Jalium.Templates/working/content/`；
> 提到 demo 时相对仓库根的 `Examples/Workflow/Jalium Trimmed/`；引用别处一律写全路径。

代码目录：`Src/Templates/VeloxDev.Jalium.Templates/working/content/workflow-<角色>/`
（注意：csproj 与 `content/` **不同在 `working/` 下**，本家是唯一的结构离群，见 `../architecture.md` §二）。

---

## 一、这一家的条目产出什么形状

**七个条目各产出一个 `.cs`**（`primaryOutputs` 全是 1 条），没有任何标记语言 —— 这一家没有 XAML/AXAML。
**2026-10-03 起，七个产物全部是「薄派生」**：控件类派生适配器基类、只设属性或重写一个方法；只有两个条目还是 `static class`（一个出值、一个出工厂）。

| 条目 | 产物类型（行数） | 扮演什么 | 关键锚点 |
|---|---|---|---|
| tree-view（23） | `sealed class TemplateClass : WorkflowTreeView` | 表面：构造器设 `SurfaceBackground` / `ConnectingLinkColor` / `PortLayout` / `GridDecorator` / `TemplateSelector` | `:13`、`:15-22` |
| node-view（63） | `sealed class TemplateClass : WorkflowNodeView` | 卡片：`override DrawCard` 画设计尺寸的卡 | `:14`、`:22-62` |
| link-view（18） | `sealed class TemplateClass : WorkflowLinkView` | 连线：设 `LinkColor` / `Thickness` | `:11`、`:15-16` |
| grid-decorator（25） | `sealed class TemplateClass : WorkflowGridDecorator` | 网格/标尺：设七色 + `GridStep` / `MajorLineEvery` | `:11`、`:15-23` |
| slot-view（23） | **`static class TemplateClass`** + `static readonly WorkflowPortLayout Layout` | **不画任何东西**：卡片设计尺寸与端口位置的值 | `:11`、`:14-22` |
| minimap-overlay（14） | `class TemplateClass : WorkflowMinimapOverlay`，**空构造器** | 薄壳（七家里最薄） | `:9-13` |
| template-selector（25） | **`static class TemplateClass`** + 私有 `Selector : WorkflowTemplateSelector` | 工厂：`CreateSelector()` 返回选择器实例 | `:11`、`:15`、`:17-24` |

⇒ 三条"读完文件才知道"的推论：

1. **`workflow-slot-view` 在这一家产出的是一个 `sealed class : WorkflowSlotView`（23 行）** —— 一个真正的端口图形控件，加上一份共享的 `static readonly WorkflowPortLayout Layout`（`DesignWidth = 260` / `DesignHeight = 180` / `TitleBarH = 36` / `RowH = 26` / `InputPortX = 10` / `OutputInset = 15` / `InputPortRadius = 9` / `OutputPortRadius = 7`）。**曾经不是**：旧版这一条是 `static class`，同时装几何常量与端口枚举，而**端口图形由 node-view 的 `DrawCard` 画成圆点** —— 所以「改插槽外观」那时要改的是 node-view。现在枚举/定位在 `WorkflowPortGeometry`，图形在 `WorkflowSlotView`，卡片只按 `Layout` 托管它。⇒ **这一条是 `adapter-base-class-specifications.md` §三 举的「跨角色牵扯」范例**：表面、卡片、连线三处共读**同一个 `Layout` 实例**，谁改了别人的尺寸都要一起改。
2. **`grid-decorator` 现在派生 `WorkflowGridDecorator`（控件基类），不再是 `static class`。** 表面持有一个 `GridDecorator` 实例（`tree-view:20` 的 `new GridDecorator()`），网格本体的世界坐标数学 / 刻度 / 标签 / 笔刷缓存都在基类。**这一家仍不实现 `IWorkflowGridDecorator` 接口**（基类没实现它）—— 要换装饰器得派生 `WorkflowGridDecorator`，不是实现那个接口。
3. **`selector` 条目的产物是工厂方法而不是类**：tree-view 的构造器**自己**调它（`tree-view:21` 的 `TemplateNamespace.TemplateSelector.CreateSelector()`），所以七份产物生成完就已经接上。拿到的实例是 `WorkflowTemplateSelector` 的私有派生，只设 `NodeViewFactory` / `LinkViewFactory` 两个工厂（`template-selector:21-22`）；**插槽与树 item 会走基类的「工厂未设」分支抛 `InvalidOperationException`**（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowTemplateSelector.cs:37-48`）—— 这两类 item 从不进本仓库的池。
   `TemplateSelector` 属性现在由 tree-view 构造器设成非 null（`tree-view:21`）⇒ `WorkflowTreeView.SetTree` 两个附着属性一起给，池一定建得起来。

---

## 二、模板里必须手写、委派不掉的接线

### 2.1 表面 = 派生基类 + 设值

七个条目里只有 tree-view 的产物含**任意**装配：它派生 `WorkflowTreeView` 并设五个公开属性（`tree-view:17-21`）。画布手势、命中、视口虚拟化、网格渲染、`_zoomPin` 守卫都在基类 —— **模板里没有第二份**。改这些机制要改适配器基类（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowTreeView.cs`），不是在模板里补代码。

### 2.2 **七份产物合起来还缺一个宿主窗口**，且宿主有严格的装配顺序

模板不含窗口/入口（与 `../architecture.md` §一"不是 demo"一致）。Jalium 这家要求的宿主装配顺序可以从 demo 的 `MainWindow.cs:37-58` 逐行读出来：

| # | 宿主必须做 | 依据 |
|---|---|---|
| 1 | `new TreeView()` —— tree-view 的构造器已自设 `PortLayout`/`GridDecorator`/`TemplateSelector`，宿主要覆盖才显式赋 | `MainWindow.cs:37-40` |
| 2 | 放进 `ScrollViewer`，且 `PanningMode = PanningMode.None`（**表面自己处理鼠标平移**） | `MainWindow.cs:43-49` |
| 3 | `surface.AttachScrollViewer(viewer)` —— **必须在 `SetTree` 之前**，注释说明了原因（视口尺寸在 `SetTree` 时就要可读，否则第一次虚拟化要等一次可能不来的 `ScrollChanged`） | `MainWindow.cs:51-54` |
| 4 | `surface.SetTree(tree)` —— 内部会 `ViewPool.SetTemplateSelector` + `SetItemsSource` 两个一起给 | `MainWindow.cs:55` |
| 5 | `surface.DataContext = tree` —— **`SetTree` 只存 `_tree`**，池化视图是从 `DataContext` 取 item 的 | `MainWindow.cs:56-58` |
| 6 | 订阅 `viewer.ScrollChanged` / `viewer.SizeChanged` / `surface.Changed`，把 6 个数值（ContentOffset/ScrollOffset/Viewport 宽高）喂给小地图那一类叠加层 | `MainWindow.cs:86-108` |
| 7 | 自己接缩放（窗口级 Ctrl+wheel 与 Ctrl+`+`/`-`），并在每次提交后调 `surface.NotifyZoomCommitted(...)` | `MainWindow.cs:121-155`、`:165-229`；API 在基类 `WorkflowTreeView.cs:191-209` |

⇒ 第 5、7 两条最容易漏且**都不报错**：漏了 5 ⇒ 池化视图读不到 item；漏了 7 ⇒
深缩放窗口里连线会被虚拟化剔掉约 100 ms（基类 `WorkflowTreeView.cs:182-209` 的注释把这条写明了）。
`AttachScrollViewer`/`SetTree`/`NotifyZoomCommitted`/`Changed`/`OriginX`/`ContentOriginX` 都是基类的公开成员，
所以"模板不含入口"这件事的代价在这一家是**七步手写装配**。

### 2.3 跨条目的**编译期**耦合：tree 少生成一条兄弟就编译不过

tree-view 正文里出现这些兄弟条目的成员：`SlotView.Layout`（`:19`）、`new GridDecorator()`（`:20`）、
`TemplateNamespace.TemplateSelector.CreateSelector()`（`:21`）。
selector 条目引用 `new NodeView()` / `new LinkView()`（`:21-22`）。
⇒ **只生成 `jalium-v-tree` 会 CS0246**（缺 `GridDecorator`、`SlotView`、`TemplateSelector` 三个类型），
只生成 `jalium-v-selector` 会缺 `NodeView`/`LinkView`。
node-view 与 link-view **不再**引用兄弟条目（端口位置读基类的 `PortLayout` 属性），
所以这条耦合比旧版小了。与 WinForms 那条同源，见 `../architecture.md` §五 与 `winforms.md` §三·P1。

### 2.4 符号是**内联进 C# 表达式**的，所以数值符号只能用数值

| 写法 | 依据 | 含义 |
|---|---|---|
| `GridStep = TemplateGridSpacing;` | `grid-decorator:22` | 默认值 `'40d'` 在这里**是合法的 C#**（`40d`）—— 与 Razor 必须剥掉这个 `d` 正好相反 |
| `MajorLineEvery = TemplateMajorLineEvery;` | `grid-decorator:23` | 整数 |
| `new Pen(..., TemplateNodeBorderThickness)` / `...TemplateNodeCornerRadius, ...` | `node-view:30,32` | 这两个符号被当 **double** 用（不是 CSS 长度） |
| `Thickness = TemplateLinkThickness;` | `link-view:16` | double |
| `ColorConverter.ConvertFromString("Template…Color")` | `grid-decorator:15-21`、`node-view:19,27,29`、`link-view:15`、`tree-view:17-18` | 颜色一律是**字符串** |

⇒ 给 `gridSpacing`/`majorLineEvery`/`nodeBorderThickness`/`nodeCornerRadius`/`linkThickness` 传非数值（如 `40px`）
**生成时会成功、构建时才炸**（`dotnet new` 只做文本替换）。

---

## 三、这一家模板特有的坑

### P1 · 本家 12 个空转符号**全部**是结构性的

`../architecture.md` §7.1 记了本家 12 个空转 symbol（本家是全仓库最多的一家）。逐条核代码，它们与 Razor 那 7 个同类 ——
**不是漏了 `replaces`，是没有对应的绘制面**：

| 空转符号 | 为什么换不回来 |
|---|---|
| `gridBackground`（grid-decorator） | 基类 `DrawGrid` 只画线、**不填任何矩形**（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowGridDecorator.cs:116-137`）；背景由表面 `SurfaceBackground`（`tree-view:17`）承担 |
| `slotBackground`/`slotColor`/`slotBorderColor`/`slotPath`（slot-view，四个全空转） | 这个文件**一行绘制代码都没有**（§一·1）—— 它是端口布局值 |
| `minimapBackground`/`minimapBorder`/`nodeFill`/`viewportStroke`（minimap-overlay，四个全空转） | 产物是个**空子类**（`:9-13`），连一个 `Template*` token 都不含；颜色全在适配器的 `WorkflowMinimapOverlay` 默认值里 |
| `surfaceBorderBrush`/`surfaceBorderThickness`/`surfaceCornerRadius`（tree-view） | 产物是 `WorkflowTreeView`（`Canvas`）子类，只设 `SurfaceBackground`（`tree-view:17`），没有边框/圆角面 |

⇒ 补 `replaces` 在 Jalium 上是纯负收益（理由同 `../extension.md` §4.3）。
本家 12 个 + Razor 7 个 = **24 个空转参数里有 19 个属于"没有绘制面"这一类**。

### P2 · 标尺厚度 `36` 现在是**单一来源**（旧版的三处复制已消失）

旧的 P2 说标尺厚度被复制三处（`GridDecorator` 常量 + link-view 的 `RulerReserve = 36` + node-view 硬编码 `+ 36`）。2026-10-03 重构后**全部收进适配器**：

| 位置 | 依据 |
|---|---|
| 权威常量 `public const double RulerThickness = 36;` | `Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowGridDecorator.cs:25` |
| link-view 端点：`origin + WorkflowGridDecorator.RulerThickness` | `Src/Adapters/.../WorkflowLinkView.cs:243-244` |
| node-view 定位：`Anchor + WorkflowGridDecorator.RulerThickness` | `Src/Adapters/.../WorkflowNodeView.cs:63,66` |
| tree-view 的 `OriginX/OriginY` 与 `SetVirtualizeInset` | `Src/Adapters/.../WorkflowTreeView.cs:128,131,584` |

⇒ **改厚度要改的是那一个常量**；模板侧没有任何副本。本家也**没有**绑定式那条路
（WPF/Avalonia/WinUI/MAUI 是把 `TranslateTransform` 绑到 `PART_GridDecorator.RulerThickness`）。
跨平台对照见 `../architecture.md` §六·轴 1。

### P3 · 状态色在基类里，卡片背景/边框色仍每次绘制才解析

- 四个插槽状态色由基类 `WorkflowNodeView.SlotBrush` 算（`Src/Adapters/.../WorkflowNodeView.cs:80-88`），
  读的是 `Slot.State`；模板的 `DrawCard` 只是调用它（`node-view:44,59`）。对照 `../architecture.md` §7.4：这一家**不绑属性、在代码里算**。
- ⚠ 模板的 `DrawCard` 里颜色处理**不一致**：`TemplateNodeForeground` 是 `static readonly`
  （`node-view:18-19`），而 `TemplateNodeBackground` 与 `TemplateNodeBorderBrush`
  **每次 `DrawCard` 都 `ColorConverter.ConvertFromString` 一次**（`:27-29`）。
  ⇒ 拖动/悬停引起的重绘会反复解析这两个字符串；改这里时顺手提到静态字段是安全的（同文件其它色已是静态）。

### P4 · link-view 的"自盒化"机制已进包，模板只出线色

旧的 P4（模板必须持续维持自盒化）**已随重构移进适配器**：`UpdateBounds` + `OnRender` 烘焙现在在
`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowLinkView.cs:255-271` + `:89-114`，根因（渲染器按布局盒裁剪）
在 `:14-22` 的注释里。模板产物只有 18 行，设 `LinkColor` / `Thickness`。**要维护自盒化请改基类**；机制说明见
`memory/modules/WorkflowSystem/adapters/jalium.md` §2.1。

### P5 · "深缩放不丢连线"的守卫现在在**适配器基类**里

`_zoomPin`（250 ms）+ `NotifyZoomCommitted` 这套 committed-target 守卫在 `WorkflowTreeView`
（`Src/Adapters/.../WorkflowTreeView.cs:51-53,182-209,539-561`）—— **不再在模板产物里**，旧记忆的「适配器零命中」已作废。
宿主只需在提交缩放后调 `surface.NotifyZoomCommitted(...)`：demo 的窗口级 Ctrl+wheel
（`MainWindow.cs:121-155`）算出提交目标后调它（`:216,227`）。少了它，缩放后要等 helper 的 ~10 fps
脏计时器才重算可见集（那正是 `WorkflowTreeView.cs:182-189` 注释里说的 ~100 ms 窗口）。

### P6 · 类名与属性名的撞名（生成后第一件事通常是加别名）

| 撞什么 | 依据 |
|---|---|
| 生成的 `TreeView` 与框架自带的 `Jalium.UI.Controls.TreeView` | demo 用 `using WorkflowTreeView = Demo.Views.Workflow.TreeView;` 绕开（`MainWindow.cs:11-12`） |
| tree-view 里**属性名 `TemplateSelector` == 兄弟类型名**（属性在基类 `WorkflowTreeView.cs:92`，类型由 selector 条目产出） | **类内部同名会挡住类型名**（简单名先命中成员），所以 tree-view 构造器那行只能写成全限定的 `TemplateNamespace.TemplateSelector.CreateSelector()`（`tree-view:21`） |
| `Size` / `Offset` / `Anchor` 这些 Core 类型与 Jalium 自带的重名 | demo 里显式 `using Size = VeloxDev.WorkflowSystem.Size;`（`MainWindow.cs:13`） |

### P7 · 两处"初始尺寸/兜底"都在**基类**里，是刻意的

- `CanvasWidth/CanvasHeight = 2000`（`Src/Adapters/.../WorkflowTreeView.cs:32,35`）只是**下界**：实际尺寸取
  `Math.Max(2000, Layout.ActualSize)`（`UpdateCanvasSize`，`:530-537`），并用 `InvalidateMeasure()` 让宿主重新测量。
- `AttachScrollViewer` 同时挂 `ScrollChanged` **与** `SizeChanged`（`:142-157`），注释说明
  Jalium 可能**不为视口尺寸变化发 `ScrollChanged`**；`UpdateViewport` 还在视口未测量时
  **退回整张画布**（`:571-580`），否则第一次虚拟化会在 0 尺寸视口上空转。

---

## 四、指路（这些结论已经在别处写好，本文不抄）

| 结论 | 在哪 |
|---|---|
| 表面侧机制（自盒化的 `Visual.ShouldRenderChild` 根因、纯模型数学、`_zoomPin`、手势/命中） | `memory/modules/WorkflowSystem/adapters/jalium.md` §2；代码在 `Src/Adapters/VeloxDev.Jalium/Attached/Workflow/Workflow{TreeView,LinkView,NodeView,GridDecorator,PortGeometry,PortLayout,TemplateSelector}.cs` |
| 缩放枢轴 / `EnsureNegativeCover` / `ClampScrollOffset` 的模型侧语义 | `memory/modules/WorkflowSystem/extension.md` §3.9 与 `../extension.md` §4.1 的 #13 |
| 本家 12 个空转 symbol 的清单与 24 个的全局盘点 | `../architecture.md` §7.1 |
| 七家同一条目的结构差异（标尺 28/36、连线四族、minimap 薄壳 vs 自带实现） | `../architecture.md` §六 |
| 五类机械改动（本家产物同样源自 `Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/` 的同名文件，现在是同形的薄派生） | `../extension.md` §1.1 |
| 人面向的"怎么用这套模板" | `skills/veloxdev-create-workflow/references/gui/jalium.md` 的 `## Item templates` |
