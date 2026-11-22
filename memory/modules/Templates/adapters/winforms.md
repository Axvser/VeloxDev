# Templates — WinForms

> 契约（七角色、附着属性、注册位置）在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；
> **适配器侧**的硬限制与坑在 `memory/modules/WorkflowSystem/adapters/winforms.md`；
> 视觉表面的契约在 [`../../VeloxDev.WinForms/visual-workflow-hosting.md`](../../VeloxDev.WinForms/visual-workflow-hosting.md)。
> 包结构与跨平台族划分在 `../architecture.md` 与 `../extension.md`。
> **路径写法**：下文裸文件名都相对 `Src/Templates/VeloxDev.WinForms.Templates/working/content/`；引用别处一律写全路径。

代码目录：`Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-<角色>/`。

---

## 一、这一家的条目产出什么形状

**视觉后端**：七项都落在 `Src/Adapters/VeloxDev.WinForms/WorkflowSystem/VisualHosting/` 上，不再使用 `AttachedBehaviors/` 的 `WorkflowSurfaceBehavior` / `PART_*` 窗口部件。

| 条目 | 形状 | 关键锚点 |
| --- | --- | --- |
| tree-view | `TemplateClass.xaml` + `.xaml.cs`：根是 `UserControl`，内放一个 `hosting:VisualWorkflowSurface`；code-behind 设调色板并暴露 `ViewModel` / `TemplateSelector` / `Surface` | `VisualWorkflowSurface.Tree`；`GridDecorator` / `RulerOverlay` / `LinkLayer` / `Minimap` |
| node-view | `TemplateClass.xaml` + `.xaml.cs`：XAML 自根是**普通视觉 Grid**，内含 `Viewbox` + 固定设计尺寸的 Grid；code-behind 只设字体，**不继承** WorkflowSystem 基类 | 表面自动挂 `VisualWorkflowNodeBehavior`；正文用 XAML 绑定，槽字形由行为排在标题下方 |
| slot-view | `TemplateClass.xaml` + `.xaml.cs`：XAML 自根只给背景；code-behind 是**普通视觉 Border**，`OnRender` 里用 `VisualWorkflowSlotBehavior.GetSlot(this)` 取 state；必须由宿主装进 `TemplateSelector.SlotTemplate` | `SlotState`、`TemplateSlotColor` / `TemplateSlotBorderColor` |
| link-view | `TemplateClass.xaml`（空自根）+ `.xaml.cs`：**per-link 可选形态**；默认表面用单个 `VisualWorkflowLinkLayer` 一趟画完 | `Link`、`LinkCurve`、`PublishCurve` |
| grid-decorator | **纯 `.cs`**：普通视觉 `Border` + `IWorkflowGridDecorator`，`OnRender` 里直接画网格/轴（不调适配器绘制 helper）；不含 WorkflowSystem 基类 | `GridSpacing` / `MajorLineEvery` / 三种线色 / 四个 offset |
| minimap-overlay | **纯 `.cs`**：普通视觉 `Border`，在 `Child` 里组合适配器的 sealed `VisualWorkflowMinimap` 并设四色 | `NodeBrush` / `ViewportStroke` |
| template-selector | **纯 `.cs`**：`TemplateClass : VisualTemplateSelector`，用 `VisualDataTemplate` 工厂按 item 类型返回视图 | `SelectTemplate`、`VisualDataTemplate.Create` |

**四个 XAML 条目的 code-behind 基类写全名**：tree 是 `VeloxDev.WinForms.Xaml.Controls.UserControl`；node 是视觉 `Grid`，slot 是视觉 `Border`，link 是 `VisualElement`。纯 `.cs` 条目继承视觉后端类型。

node/slot/grid/minimap 都**没有** WorkflowSystem 专用基类：node 由表面挂 `VisualWorkflowNodeBehavior`，slot 由 node 行为挂 `VisualWorkflowSlotBehavior`，grid 是普通视觉 + 共享绘制 helper，minimap 是普通视觉组合 sealed 默认件。`VisualWorkflowSurface`、`VisualWorkflowGridDecorator`、`VisualWorkflowMinimap` 都是 `sealed` 角色件，不是模板基类。

---

## 二、模板里手写的是什么

- **tree**：调色板（`XamlRuntime.ParseColor` 读 `Template*` 字面量）与 `ViewModel` / `TemplateSelector` 两个属性。连线菜单、键/滚轮策略、HUD 都是宿主策略，模板不带。
- **node**：XAML 里的 `Viewbox` + 固定设计 Grid、标题/副标题绑定；槽位由 `VisualWorkflowNodeBehavior` 按 `node.Slots` 生成并摆列。
- **slot / link**：字形与曲线的绘制；slot 的状态从 `VisualWorkflowSlotBehavior` 读取，状态色、线色/线宽是扩展点。
- **grid / minimap**：只给调色板与间距；网格/小地图数学在适配器基类里。
- **selector**：只声明「哪个 item 用哪个模板」；`VisualTemplateSelector` 基类没有调度与兜底，宿主负责把结果交给表面。

---

## 三、这一家模板特有的坑

### P1 · 模板之间没有编译期依赖，但仍要一起生成

tree 的 XAML 只引用适配器的 `VisualWorkflowSurface`；`NodeView` / `CustomTemplateSelector` 只由宿主在 code-behind 里接入。只生成 tree 也能编译，但生成的工程不会显示卡片图案 —— 宿主需自己把 `CustomTemplateSelector` 装到 `VisualWorkflowSurface.TemplateSelector`。

### P2 · 自根 XAML 的根属性走视觉依赖属性

node / slot / link 的 `x:Class` 与根元素同名（`<local:TemplateClass …>`）。生成器把根属性写到 `this` 上，`Background` / `BorderBrush` 走 `XamlRuntime.ConvertValue` 的 `Brush` 转换，`BorderThickness` 走 `ParseThickness`，`CornerRadius` 是 `float` —— 占位符必须能解析成这几种类型，否则生成代码编译失败。

### P3 · `Pen` 与 `Xaml.Pen` 同名

模板文件同时 `using VeloxDev.WinForms.Xaml;`（为 `XamlRuntime`）与 `System.Drawing` 时，`new Pen(...)` 是二义引用。模板统一用 `using DrawingPen = System.Drawing.Pen;`。

### P4 · 连线命中读 `VisibleItems`

`LinkHitTestEx.HitTestVisibleLinks` 只遍历树的可见集。模板不负责虚拟化，宿主必须给树配虚拟化 helper（`new TreeHelper(200)` 或 demo 的 `AgentHelper`），否则任何连线都命中不到。表面在每次 arrange / pan / zoom 后把 `Viewport(-Pan.X, -Pan.Y, w, h)` 写回 helper。

### P5 · 默认删除与滚轮在表面里

`VisualWorkflowSurface` 已处理 Delete、Ctrl+Z/Y、Ctrl+wheel 缩放与 Shift+wheel 横滚。宿主的输入策略若也处理这些键，必须 `PreventDefault`，否则连线会删两次。

### P6 · 小地图、网格与刻度带是表面的内部实例

tree 里的 `VisualWorkflowSurface` 自己组合 `GridDecorator` / `RulerOverlay` / `Minimap` / `LinkLayer`；`workflow-grid-decorator` 与 `workflow-minimap-overlay` 条目是独立扩展点（或调色板参考），**不会**被 tree 自动实例化。改 tree 的观感直接改 `workflowHost.GridDecorator` / `workflowHost.RulerOverlay` / `workflowHost.Minimap`。

`workflow-slot-view` 同样不会自动生效：宿主必须在自己的 `VisualTemplateSelector` 上设 `SlotTemplate`，表面创建节点时会把同一个选择器转发给卡片的槽位。

**Trimmed demo 的初始状态**：视觉表面没有 `CanvasLayout.ActualOffset`，用 `Surface.SetPan(320,260)` 对齐其它六家的 `NegativeOffset=(320,260)`；树只设三个输出选择器与四个输入端口的通道，**不要**再把分支槽改成 `OneTarget` 或预建连线。

---

## 四、指路

| 结论 | 在哪 |
| --- | --- |
| 视觉表面的组成、坐标契约与扩展点 | [`../../VeloxDev.WinForms/visual-workflow-hosting.md`](../../VeloxDev.WinForms/visual-workflow-hosting.md) |
| 适配器侧的硬限制、平移与 `AutoScroll` 互斥、同步重画 | `memory/modules/WorkflowSystem/adapters/winforms.md` |
| XAML 运行时、生成器、对象资源、`.xaml.cs` | `memory/modules/WinFormsXaml/architecture.md` |
| 适配器项目结构与三条轴 | `memory/modules/VeloxDev.WinForms/architecture.md` |
| 「怎么用这套模板搭一个 WinForms 工作流视图」 | `skills/veloxdev-create-workflow/references/gui/winforms.md` 的 `## Item templates` |
