# Templates — Jalium

> **本文只写模板侧独有的东西**：条目产出什么形状、哪些接线必须手写、这一家模板特有的坑。
> 契约（七角色、附着属性、注册位置）在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；
> **表面侧**（`RegisterName` 的硬要求、`Visual.ShouldRenderChild` 自盒化、纯模型数学、`_zoomPin`）在
> `memory/modules/WorkflowSystem/adapters/jalium.md`，本文只指路不抄 —— **这些落点现在都在模板产物/demo，
> 不在适配器**（适配器里六个画布角色行为与自装配外壳 `WorkflowTreeView` 已删除）；
> 包结构与跨平台族划分在 `../architecture.md` 与 `../extension.md`。
> **路径写法**：下文裸文件名都相对 `Src/Templates/VeloxDev.Jalium.Templates/working/content/`；
> 提到 demo 时相对仓库根的 `Examples/Workflow/Jalium Trimmed/`；引用别处一律写全路径。

代码目录：`Src/Templates/VeloxDev.Jalium.Templates/working/content/workflow-<角色>/`
（注意：csproj 与 `content/` **不同在 `working/` 下**，本家是唯一的结构离群，见 `../architecture.md` §二）。

---

## 一、这一家的条目产出什么形状

**七个条目各产出一个 `.cs`**（`primaryOutputs` 全是 1 条），没有任何标记语言 —— 这一家没有 XAML/AXAML。
但更值得记的是：**其中三个条目的产物是 `static class`，根本不是控件。**

| 条目 | 产出的类型 | 在模板里扮演什么 | 关键锚点 |
|---|---|---|---|
| tree-view | `class TemplateClass : Canvas`（553 行） | **表面本体**：自绘网格/标尺、自己做命中与手势、自己管虚拟化 | `:19`、`:38-39`、`:83`、`:106`、`:220`、`:228` |
| node-view | `class TemplateClass : Canvas` + 私有嵌套 `NodeCardLayer : Canvas`（Viewbox 的子） | 卡片：设计尺寸绘制 + Viewbox 缩放 | `:17`、`:27-28`、`:39-43`、`:169`、`:209-216` |
| link-view | `class TemplateClass : FrameworkElement` | 连线：**自盒化**（元素盒 == 每线 bbox） | `:23`、`:26,30`、`:144`、`:202-226` |
| grid-decorator | **`static class TemplateClass`** | 网格/标尺的**数学与绘制方法**（不是控件、不实现任何接口） | `:13`、`:15-17`、`:36`、`:62` |
| slot-view | **`static class TemplateClass`** | **不画任何东西**：端口布局常量 + 端口枚举/命中索引（源真相） | `:10`、`:12-20`、`:23`、`:30`、`:56`、`:62` |
| minimap-overlay | `class TemplateClass : WorkflowMinimapOverlay`，**空构造器**（14 行） | 薄壳（七家里最薄） | `:9-13` |
| template-selector | **`static class TemplateClass`** + 私有 `WorkflowViewSelector : IWorkflowTemplateSelector` | 工厂：`CreateSelector()` 返回选择器实例 | `:9`、`:12`、`:16-21` |

⇒ 三条"读完文件才知道"的推论：

1. **`workflow-slot-view` 在这一家产出的不是插槽视图**，而是一份"端口在哪"的共享数学
   （`DesignWidth = 260`/`DesignHeight = 180`/`TitleBarH = 36`/`RowH = 26`/`InputPortX = 10`/`OutputInset = 15`，
   `:12-20`），卡片、连线端点与命中**共用它**（`node-view:39,175,184,188,194,199,203`、
   `link-view:156,159-161,173-174`、`tree-view:249-258,359,389,423,533`）。
   ⇒ 它的四个颜色符号全部空转（§三·P1），改"插槽外观"要改的是 node-view 的 `DrawCard`。
2. **`grid-decorator` 产出的是一个 `static class`，不实现 `IWorkflowGridDecorator`**
   （Core 的接口成员：四个偏移属性 + `RulerBand`，
   `Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/IWorkflowGridDecorator.cs:15-35`）。
   Jalium 这家**现在没有任何 `IWorkflowGridDecorator` 实现**：适配器里那个实现随 `WorkflowGridDecorator.cs` 删除，
   模板产物又是静态类。表面靠 `tree-view` 对 `GridDecorator.RulerThickness` / `GridDecorator.DrawGrid(...)` 的
   **静态调用**拿网格与标尺（`tree-view:38-39,203,314`），不经过接口。
   ⇒ **这个条目只对模板那套表面有用**（见 §二·1）。
3. **selector 条目的产物是工厂方法而不是类**：tree-view 条目**自己**在属性默认值里调它
   （`tree-view:104` 的 `= TemplateNamespace.TemplateSelector.CreateSelector()`），所以七份产物
   生成完就已经接上；宿主再调一次只是覆盖（demo `MainWindow.cs:39`）。拿到的实例只认**两种** item
   （link/node，`:16-21`），插槽与树 item 会抛 `InvalidOperationException`（`:20`）。
   **这个默认值不能是 `null`**（2026-09-25 前它曾是）：`ViewPool` 要 `ItemsSource` 与 `TemplateSelector`
   **都非空**才建 `ViewManager`（`memory/modules/WorkflowSystem/adapters/jalium.md:95`），null 时走
   `Detach()`、`AddItem` 静默返回（同文件 `:107`）⇒ **生成出来的项目画布全空、不报错**。
   当年只有 demo 能跑起来（`MainWindow.cs:39` 从外面赋了值），这正是「demo 好、模板坏」的样本。

---

## 二、模板里必须手写、委派不掉的接线

### 2.1 表面只剩模板这一套（适配器那套已删除）

曾经有两套表面：适配器自带的成品 `WorkflowTreeView : Grid`，和模板产出的 `TreeView : Canvas`。**适配器那套已删除**（零消费者），所以**现在只有模板产出的 `TreeView : Canvas`**：它自己画网格与标尺（`:311-335`）、自己命中（`:353-406`）、自己处理拖拽/平移/连线手势（`:410-552`）、自己维护视口与虚拟化（`:153-208`）。

⇒ 后果：网格装饰器是模板自己的静态类，**没有任何 `IWorkflowGridDecorator` 实例可换**（§一·2）。小地图条目不受影响 —— 它继承适配器的 `WorkflowMinimapOverlay`（实例、实现 `IWorkflowMinimapOverlay`），仍然可用。

### 2.2 **七份产物合起来还缺一个宿主窗口**，且宿主有严格的装配顺序

模板不含窗口/入口（与 `../architecture.md` §一"不是 demo"一致）。Jalium 这家要求的宿主装配顺序
可以从 demo 的 `MainWindow.cs:37-60` 逐行读出来：

| # | 宿主必须做 | 依据 |
|---|---|---|
| 1 | `new TreeView { TemplateSelector = TemplateSelector.CreateSelector() }` —— **可省**：tree-view 的 `TemplateSelector` 属性自带默认选择器（`tree-view:104`），这一句只是把它换成另一份等价实例 | `MainWindow.cs:37-40` |
| 2 | 放进 `ScrollViewer`，且 `PanningMode = PanningMode.None`（**表面自己处理鼠标平移**） | `MainWindow.cs:43-49` |
| 3 | `surface.AttachScrollViewer(viewer)` —— **必须在 `SetTree` 之前**，注释 `tree-view:86-88` 说明了原因（视口尺寸在 `SetTree` 时就要可读，否则第一次虚拟化要等一次可能不来的 `ScrollChanged`） | `MainWindow.cs:51-54` |
| 4 | `surface.SetTree(tree)` | `MainWindow.cs:55` |
| 5 | `surface.DataContext = tree` —— **`SetTree` 只存 `_tree`**，池化视图是从 `DataContext` 取 item 的 | `MainWindow.cs:56-58` |
| 6 | 订阅 `viewer.ScrollChanged` / `viewer.SizeChanged` / `surface.Changed`，把 6 个数值（ContentOffset/ScrollOffset/Viewport 宽高）喂给小地图那一类叠加层 | `MainWindow.cs:87-109` |
| 7 | 自己接缩放（窗口级 Ctrl+wheel 与 Ctrl+`+`/`-`），并在每次提交后调 `surface.NotifyZoomCommitted(...)` | `MainWindow.cs:121-142,146-155,165-229`；API 在 `tree-view:220-239` |

⇒ 第 5、7 两条最容易漏且**都不报错**：漏了 5 ⇒ 池化视图读不到 item；漏了 7 ⇒
深缩放窗口里连线会被虚拟化剔掉约 100 ms（`tree-view:210-219` 的注释把这条写明了）。
`AttachScrollViewer`/`SetTree`/`NotifyZoomCommitted`/`Changed` 都是生成产物上的公开成员，
所以"模板不含入口"这件事的代价在这一家是**七个步骤的手写装配**。

### 2.3 跨条目的**编译期**耦合：tree 少生成一条兄弟就编译不过

tree-view 正文里出现这些兄弟条目的静态成员：`GridDecorator.RulerThickness`（`:38-39,203`）、
`SlotView.*`（`:249-258,359,389,423,533`），以及 selector 条目的工厂（`:104` 的属性默认值）。
selector 条目引用 `new NodeView()` / `new LinkView()`（`:18-19`）。
⇒ **只生成 `jalium-v-tree` 会 CS0246**（缺 `GridDecorator`、`SlotView`、`TemplateSelector` 三个类型），
只生成 `jalium-v-selector` 会缺 `NodeView`/`LinkView`。
与 WinForms 那条同源但覆盖面更大（那家是 `IWorkflowMinimapScrollSource` 一个类型），
见 `../architecture.md` §五 与 `winforms.md` §三·P1。

### 2.4 符号是**内联进 C# 表达式**的，所以数值符号只能用数值

| 写法 | 依据 | 含义 |
|---|---|---|
| `public const double GridStep = TemplateGridSpacing;` | `grid-decorator:15` | 默认值 `'40d'` 在这里**是合法的 C#**（`40d`）—— 与 Razor 必须剥掉这个 `d` 正好相反 |
| `public const double MajorStep = TemplateGridSpacing * TemplateMajorLineEvery;` | `grid-decorator:16` | const 表达式 ⇒ 两个符号都必须是字面量，不能是 `double.Parse(...)` |
| `new Pen(brush, TemplateNodeBorderThickness)` / `...TemplateNodeCornerRadius, ...` | `node-view:174-175` | 这两个符号被当 **double** 用（不是 CSS 长度） |
| `ColorConverter.ConvertFromString("Template…Color")` | `grid-decorator:19-25`、`node-view:21`、`link-view:32`、`tree-view:25` | 颜色一律是**字符串** |

⇒ 给 `gridSpacing`/`majorLineEvery`/`nodeBorderThickness`/`nodeCornerRadius` 传非数值（如 `40px`、`2d` 之外的写法）
**生成时会成功、构建时才炸**（`dotnet new` 只做文本替换）。

---

## 三、这一家模板特有的坑

### P1 · 本家 12 个空转符号**全部**是结构性的

`../architecture.md` §7.1 记了本家 12 个空转 symbol（本家是全仓库最多的一家）。逐条核代码，它们与 Razor 那 7 个同类 ——
**不是漏了 `replaces`，是没有对应的绘制面**：

| 空转符号 | 为什么换不回来 |
|---|---|
| `gridBackground`（grid-decorator） | `DrawGrid` 只画线、**不填任何矩形**（`:36-55`）；背景由 surface 的 `Background`（`tree-view:25`）承担 |
| `slotBackground`/`slotColor`/`slotBorderColor`/`slotPath`（slot-view，四个全空转） | 这个文件**一行绘制代码都没有**（§一·1）—— 它是端口数学 |
| `minimapBackground`/`minimapBorder`/`nodeFill`/`viewportStroke`（minimap-overlay，四个全空转） | 产物是个**空子类**（`:9-13`），连一个 `Template*` token 都不含；颜色全在适配器的 `WorkflowMinimapOverlay` 默认值里 |
| `surfaceBorderBrush`/`surfaceBorderThickness`/`surfaceCornerRadius`（tree-view） | 产物是 `Canvas` 子类，只设 `Background`（`:25`），没有边框/圆角面 |

⇒ 补 `replaces` 在 Jalium 上是纯负收益（理由同 `../extension.md` §4.3）。
本家 12 个 + Razor 7 个 = **24 个空转参数里有 19 个属于"没有绘制面"这一类**。

### P2 · 标尺厚度 `36` 没有符号，而且被复制到三处、被引用到两处

| 位置 | 依据 |
|---|---|
| 权威常量 `public const double RulerThickness = 36;` | `grid-decorator:17` |
| link-view 复制成 `RulerReserve = 36`，注释**明确要求与上一条一起改** | `link-view:28-30` |
| node-view **硬编码字面量** `+ 36`（注释说明与树的 `OriginX` 加的是同一个预留） | `node-view:157-160` |
| tree-view 的 `OriginX/OriginY` = 布局偏移 + `GridDecorator.RulerThickness` | `tree-view:38-39` |
| tree-view 把它喂给 `SetVirtualizeInset`（现在只有这一条路） | `tree-view:203` |

⇒ 改厚度要改**两处常量 + 一处字面量**；本家**没有**"绑定式"的那条路
（WPF/Avalonia/WinUI/MAUI 是把 `TranslateTransform` 绑到 `PART_GridDecorator.RulerThickness`）。
跨平台对照见 `../architecture.md` §六·轴 1。

### P3 · 状态色硬编码，且**卡片背景/边框色每次绘制才解析**

- 四个插槽状态色是字面量、不绑任何属性：待机 `Color.FromArgb(0xDD,0x1E,0x1E,0x1E)`、Sender `#FF6347`、
  Receiver `#32CD32`、两者 `#EE82EE`（`node-view:22-25,135-143`，读的是 `Slot.State`，`:187,202`）。
  对照 `../architecture.md` §7.4：这一家**不绑属性、在代码里算**。
- ⚠ 同一文件里颜色处理**不一致**：`TemplateNodeForeground` 是 `static readonly`
  （`node-view:21`），而 `TemplateNodeBackground` 与 `TemplateNodeBorderBrush`
  **在 `DrawCard` 里每次 `OnRender` 都 `ColorConverter.ConvertFromString` 一次**（`:173-174`）。
  ⇒ 拖动/悬停引起的重绘会反复解析这两个字符串；改这里时顺手提到静态字段是安全的（同文件其它色已是静态）。

### P4 · link-view 的"自盒化"必须由模板持续维持

`link-view` 的注释 `:14-20` 把根因写全了（渲染器按**布局盒**裁剪子元素、不看绘制内容），
机制本身在 `memory/modules/WorkflowSystem/adapters/jalium.md`。**模板侧要记住的是维持成本**：
盒子必须在任何会折叠/移动端点的事件上重算 —— 端点节点 `Anchor`/`Size`（`:107-114`）、
链自身与两端插槽的属性变化（`:126-130`）、以及**布局对象**的每一次变化（`Scale`、cover 增长导致的 `ActualOffset`，
`:72-83`）。⇒ 少订阅其中任何一处，盒子就停在旧位置，深缩放时整条线被剔除（不报错）。
另外两个模板才知道的常量：`BoxPad = 6`（笔半宽 + 抗锯齿余量，`:26`）与
`IsHitTestVisible = false` + `Panel.SetZIndex(this, -100)`（`:47-48`）。
`IsDragPreview`（`:136-137`）跳过两个端点都是 `SlotDefaultViewModel` 的拖拽预览 ——
那条预览由 surface 自己在 `OnPostRender` 里画（`tree-view:330-334`），池里的 LinkView 必须让它过去。

### P5 · "深缩放不丢连线"的守卫在**模板产物**里

`_zoomPin`（250 ms）+ `NotifyZoomCommitted` 这套 committed-target 守卫在 tree-view 里（`:52-60,153-178,220-239`），
**适配器里没有**（全仓 `Src/Adapters/VeloxDev.Jalium/` 搜 `_zoomPin`/`NotifyZoomCommitted` 零命中）——
适配器现在根本没有表面，六个画布角色行为已删除。
⇒ 缩放与视口提交全在宿主侧：模板/demo 的 `TreeView` 自己维护这套守卫；demo 的窗口级 Ctrl+wheel
（`MainWindow.cs:121-142,146-155`）算出提交目标后调 `surface.NotifyZoomCommitted(...)`（`:216,227`）。
少了它，缩放后要等 helper 的 ~10 fps 脏计时器才重算可见集（这正是 `tree-view:210-219` 注释里说的 ~100 ms 窗口）。

### P6 · 类名与属性名的撞名（生成后第一件事通常是加别名）

| 撞什么 | 依据 |
|---|---|
| 生成的 `TreeView` 与框架自带的 `Jalium.UI.Controls.TreeView` | demo 用 `using WorkflowTreeView = Demo.Views.Workflow.TreeView;` 绕开（`MainWindow.cs:11-12,24`） |
| 宿主那句 `surface.TemplateSelector = TemplateSelector.CreateSelector();` 里**属性名 == 类型名**（属性在 `tree-view:104`，类型由 selector 条目产出，`template-selector:9`） | **类内部同名会挡住类型名**（简单名先命中成员），所以 tree-view 自己那行默认值只能写成全限定的 `TemplateNamespace.TemplateSelector.CreateSelector()`（`tree-view:104`） |
| `Size` / `Offset` / `Anchor` 这些 Core 类型与 Jalium 自带的重名 | demo 里显式 `using Size = VeloxDev.WorkflowSystem.Size;`（`MainWindow.cs:13`） |

### P7 · 两处"初始尺寸/兜底"是刻意的

- `CanvasWidth/CanvasHeight = 2000`（`tree-view:21-22`）只是**下界**：实际尺寸取
  `Math.Max(2000, Layout.ActualSize)`（`UpdateCanvasSize`，`:145-151`），并用 `InvalidateMeasure()` 让宿主重新测量。
- `AttachScrollViewer` 同时挂 `ScrollChanged` **与** `SizeChanged`（`:96-97`），注释 `:86-88` 说明
  Jalium 可能**不为视口尺寸变化发 `ScrollChanged`**；`UpdateViewport` 还在视口未测量时
  **退回整张画布**（`:189-199`），否则第一次虚拟化会在 0 尺寸视口上空转。

---

## 四、指路（这些结论已经在别处写好，本文不抄）

| 结论 | 在哪 |
|---|---|
| 表面 `RegisterName` 的硬要求、自装配/网格/视口的落点（模板产物 `TemplateClass.cs`） | `memory/modules/WorkflowSystem/adapters/jalium.md` 与 `extension.md` §3.9 |
| 渲染器按布局盒裁剪（`Visual.ShouldRenderChild`）⇒ 自盒化的机制；纯模型数学；`_zoomPin` 的来龙去脉 | `memory/modules/WorkflowSystem/adapters/jalium.md` |
| 缩放枢轴 / `EnsureNegativeCover` / `ClampScrollOffset` 的模型侧语义 | `memory/modules/WorkflowSystem/extension.md` §3.9 与 `../extension.md` §4.1 的 #13 |
| 本家 12 个空转 symbol 的清单与 24 个的全局盘点 | `../architecture.md` §7.1 |
| 七家同一条目的结构差异（标尺 28/36、连线四族、minimap 薄壳 vs 自带实现） | `../architecture.md` §六 |
| 五类机械改动（本家产物同样源自 `Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/` 的同名文件） | `../extension.md` §1.1 |
| 人面向的"怎么用这套模板" | `skills/veloxdev-create-workflow/references/gui/jalium.md` 的 `## Item templates` |
