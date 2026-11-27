# Templates — WinForms

> 契约（七角色、附着属性、注册位置）在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；
> **适配器侧**的硬限制与坑在 `memory/modules/WorkflowSystem/adapters/winforms.md`；
> 包结构与跨平台族划分在 `../architecture.md` 与 `../extension.md`。
> **路径写法**：下文裸文件名都相对 `Src/Templates/VeloxDev.WinForms.Templates/working/content/`。

代码目录：`Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-<角色>/`。

---

## 一、条目产出什么形状

**XAML-first，整棵是视觉树**：tree/node/slot/link 产出 `TemplateClass.xaml` + `.xaml.cs`（code-behind 基类 `Easel.Visuals.Controls.UserControl`）；grid-decorator / minimap-overlay / template-selector 是纯 `.cs`。`InitializeComponent` 由生成器输出为 private 普通方法。

| 条目 | 形状 | 关键锚点 |
| --- | --- | --- |
| tree-view | 视觉 `UserControl` 根 `Grid`：`PART_SurfaceBorder`（`Background`/`BorderBrush`/`BorderThickness`/`CornerRadius` 令牌）> `PART_GridDecorator`（`GridSpacing=40`、`MajorLineEvery=5`、`RulerThickness=28`、`DrawSurface=False`）> `PART_ScrollViewer` > `PART_Canvas`（`behaviors:ViewPool.ItemsSource="{Binding Helper.VisibleItems}"`、`behaviors:ViewPool.TemplateSelector="{StaticResource WorkflowTemplateSelector}"`）；同级的 `PART_MinimapOverlay` 是视觉元素。资源里声明 `NodeTemplate` / `LinkTemplate` / `SlotTemplate`、**空** `LinkContextMenu` 与 `CustomTemplateSelector x:Key="WorkflowTemplateSelector"`。**根元素上声明 `behaviors:WorkflowSurfaceBehavior.IsEnabled/CanvasName/ScrollViewerName/GridDecoratorName/MinimapOverlayName/PointerPressSourceName/ZoomEnabled/LinkMenuKey`**；**code-behind 只有 `InitializeComponent()`** | 表面行为挂在视觉根上，Easel 用 `IVisualHostBehavior` 注入 `VisualHost`；部件按 `VisualElement.Name` 解析；`VisualTemplateSelectorKey` 默认就是 `WorkflowTemplateSelector`，宿主从 tree 根资源解析；DataContext 是树唯一来源，Attach 后变化经 `VisualHost.Post` 合并重绑；连线菜单是根上的 `LinkMenuKey="LinkContextMenu"`；模板的资源保持为空，Demo 自己加 `VisualMenuItem CommandPath` 条目 |
| node-view | 视觉 `UserControl`：`Viewbox > Grid 260×180`；**backdrop `Border`（`TemplateSurfaceBackground`）** 与卡面 `Border`（`TemplateNodeBackground`）、标题 `TextBlock`（`FontWeight=SemiBold`）、`PART_InputSlot` 与 `PART_OutputSlots`（`ItemsControl` + `DataTemplate` 行，`Panel.ZIndex=6`）。根挂 `WorkflowSlotLayoutBehavior`（`IsEnabled` / `SlotNames` / `SlotEnumeratorNames` / `CoordinateHostName`），标题行挂 `WorkflowNodeDragBehavior`（`IsEnabled` / `CoordinateHostName`） | `WorkflowSurfaceBehavior` 遍历视觉树对每个元素调 `Ensure`；行为自己测量命名槽、只允许标题拖动。没写 `IsEnabled` 就不生效，没有部件名回退 |
| link-view | 视觉 `UserControl`（空壳）+ DP `StartLeft/StartTop/EndLeft/EndTop/CanRender/IsVirtual/LineColor/LineThickness`；`LineColor`/`LineThickness` 默认值来自 `TemplateLinkColor`/`TemplateLinkThickness`（`float`，与适配器附着属性同型）；`OnRender` 画 cubic 并 `PublishCurve`；构造里 `IsHitTestVisible = false`，并设 `UnboundedHitTest = true`（Easel 的 damage cull 会按画布盒把负象限的整棵视图剔除） | `LinkTemplate` 把端点绑到 `Sender.Anchor.*` / `Receiver.Anchor.*`、`CanRender` 绑 `IsVisible`，`Width`/`Height` 用 `{Binding Width/Height, ElementName=PART_Canvas}` 取画布实际尺寸；表面 `ApplyLinkStyles` 写颜色、线宽与 `IsVirtual`；`HighlightColor`/`HighlightThickness`/`IsHighlighted` 由适配器按 `SetIfPresent` 写入，模板不声明这些 DP |
| slot-view | 视觉 `UserControl`：`Shapes.Ellipse PART_Glyph`；根挂 `WorkflowSlotConnectionBehavior.IsEnabled="True"`；**code-behind 只有 `InitializeComponent()`** | 连接手势；`PART_Glyph.Fill` / `Stroke` 直接用 `TemplateSlotColor` / `TemplateSlotBorderColor`，`StrokeThickness` 1.5 |
| grid-decorator | **纯 `.cs`**：`TemplateClass : VisualElement, IWorkflowGridDecorator`，三层：世界网格层、内容（ScrollViewer）、视口固定标尺层。**不画表面边框** | 网格、标尺带；`DrawSurface=False` 时让 `PART_SurfaceBorder` 画表面背景；表面边框只有 `PART_SurfaceBorder` 一处 |
| minimap-overlay | **纯 `.cs`**：`TemplateClass : WorkflowMinimapOverlay`（`VisualElement`），只给调色板 | 与 `PART_SurfaceBorder` 同级放进 tree 根 `Grid`；定位与拖拽映射在适配器，拖小地图经 `IWorkflowMinimapScrollSource` 反推视口 |
| template-selector | **纯 `.cs`**：`TemplateClass : Visuals.VisualTemplateSelector`，按 item 类型返回 `NodeTemplate?.Create()` / `SlotTemplate?.Create()` / `LinkTemplate?.Create()`（缺模板时回落 `new NodeView()` 等） | tree 资源按 `WorkflowTemplateSelector` 键引用它 |

**纯 `.cs` 条目继承视觉 `VisualElement` 或适配器基类**；XAML 条目的基类写全名。

---

## 二、模板里手写的是什么

- **tree**：唯一代码是一行 `InitializeComponent()`；根元素声明 `behaviors:WorkflowSurfaceBehavior.*`（`IsEnabled`、四个 `PART_*` 名、`PointerPressSourceName`、`ZoomEnabled`、`LinkMenuKey="LinkContextMenu"`），XAML 不写宿主；DataContext 是树的唯一来源（见 §P4b）。资源里用 `DataTemplate` 包住 `NodeView` / `LinkView` / `SlotView`，选择器资源 `WorkflowTemplateSelector` 把三者的 `DataTemplate` 属性接上；`PART_Canvas` 用 `behaviors:ViewPool` 把它绑到 `Helper.VisibleItems`。连线菜单是 tree 资源里的**空** `VisualContextMenu x:Key="LinkContextMenu"`；Demo 自己加 `VisualMenuItem`（`CommandPath` 按菜单的 DataContext 逐段取属性）；`SetLinkMenuBuilder` 只用于动态/纯代码菜单。键/滚轮策略与示例模型属于 demo 树的 code-behind（树的 `IInputEvents`），模板只留 `InitializeComponent()`。调色板由 `template.json` 的 `Template*` 符号替换进 XAML。
- **selector**：只声明「哪个 item 用哪个视图」；返回 `NodeTemplate?.Create()` / `SlotTemplate?.Create()` / `LinkTemplate?.Create()`，缺模板时回落 `new NodeView()` 等。
- **node / slot**：node 的行由模板 `ItemsControl` 生成；根上的 `WorkflowSlotLayoutBehavior` 声明槽名与坐标宿主，标题行的 `WorkflowNodeDragBehavior` 声明拖动区；slot 根上的 `WorkflowSlotConnectionBehavior` 声明连接手势。`ViewPool` 设 `DataContext`；`WorkflowSlotLayoutBehavior` 找部件、写锚点；slot 的 `PART_Glyph.Fill` / `Stroke` 直接用模板颜色符号；slot code-behind 只有 `InitializeComponent()`。
- **link**：`LinkTemplate` 为每条 link 建一个 `LinkView`，端点由绑定给出；它自己画并发布曲线。`linkColor`/`linkThickness` 只改 `LinkView` 的 `LineColor`/`LineThickness` 默认值；这条 link 没有 surface 背景符号，表面底色归 tree 的 `PART_SurfaceBorder`。
- **grid / minimap**：只给调色板与间距；网格/标尺/小地图/HUD 数学在适配器视觉元素与模板的绘制代码里。

**与 WPF tree 同构**：小地图 `PART_MinimapOverlay` 在 tree 的视觉 `Grid` 里，与 `PART_SurfaceBorder` 同级；`PointerPressSourceName` 指向 `PART_SurfaceBorder`，空白画布拖动从该部件起算。没有 HWND 兄弟层。

**Trimmed 默认画面与 WPF 对齐的基线**：`ClientSize=1085×683`、`RulerThickness=28`、`SurfaceCornerRadius=0`、标尺底 `#C8252526`、HUD 左 46 / 底 12、minimap `Margin=0,12,12,0`（200×140）、节点标题/端口行随整图相机矩阵缩放并在面板内垂直居中、slot 字形实心并在 glyph 空间描 `BorderColor` 一圈；网格线在 `SmoothingMode.None` 下画。仍不同的是窗口非客户区、滚动条（自绘细条 vs WPF 原生带箭头）和文字光栅化（GDI+ vs WPF）。

---

## 三、这一家模板特有的坑

### P1 · 七个条目有编译期兄弟依赖，必须一起生成

tree 的 XAML 直接引用 `local:GridDecorator`、`local:NodeView`、`local:SlotView`、`local:LinkView` 与 `local:CustomTemplateSelector`（后三者包在资源 `DataTemplate` 里）。**只生成 tree 会因缺少这些兄弟类型编译失败**。`verify-workflow-item-templates-all.ps1` 一次生成七个并编译。

### P2 · `XamlRuntime.ParseColor` 会静默吃掉写错的颜色

解析不出 `#RRGGBB` / `#AARRGGBB` 时走 `Color.FromName`；未知名字返回 ARGB 全 0 的透明黑。⇒ 占位符少一位或拼错时不抛不报，表现是这条线/这个背景不见了。模板颜色都是 `template.json` 的 `replaces` 符号，校验脚本会断言生成文件里没有残留占位符。

### P1b · tree 资源就是 selector 的接线

tree 资源里 `DataTemplate x:Key="NodeTemplate"` / `LinkTemplate` / `SlotTemplate` 包住兄弟视图，`CustomTemplateSelector x:Key="WorkflowTemplateSelector"` 引用它们。宿主不需要 `new` 选择器：`WorkflowSurfaceBehavior.VisualTemplateSelectorKey` 默认是 `WorkflowTemplateSelector`，先从 tree 根的 `VisualElement.Resources` 找，再找 `VisualHost.Resources`。显式 `SetVisualTemplateSelector` 仍优先。

### P4 · 视觉节点的行属于模板

`PART_OutputSlots` 是 `ItemsControl`，`ItemsSource="{Binding OutputSlots.Items}"`，`ItemTemplate` 是 `<DataTemplate>`。部件名由根上的 `WorkflowSlotLayoutBehavior.SlotNames` / `SlotEnumeratorNames` 声明（逗号分隔）；不写就不测量任何槽。`ViewPool` 设 `DataContext`，`WorkflowSlotLayoutBehavior` 发布 SlotView 锚点；`DataTemplate` 由生成器把模板体编成工厂。

- **ItemsControl 默认外观透明且不带滚动宿主**：默认模板只剩 `ItemsPresenter`（无 `Input` 底、无 1px 边框、无内层 `ScrollViewer`），node 模板的 `PART_OutputSlots` 直接用默认即可；需要滚动的宿主自己外包 `ScrollViewer`。

### P4b · 附着行为是 WPF 形状的，不是 HWND 行为

`WorkflowSlotLayoutBehavior` / `WorkflowNodeDragBehavior` / `WorkflowSlotConnectionBehavior` 只声明 `DependencyProperty.RegisterAttached`，由 `WorkflowSurfaceBehavior` 遍历视觉树对每个元素调 `Ensure`，行为自己读附着属性。模板里配了某个行为时，只有被标记的元素（及其后代）生效；没挂或没写 `IsEnabled` 就不生效（没有整卡可拖、全部插槽可连的回退）。表面行为本身是视觉根 + `IVisualHostBehavior`：根上的 `IsEnabled=True` 注册控制器，根挂上 `VisualHost` 时 Easel 调 `Attach(host)`，根离开宿主/释放时调 `Detach()`。**树的唯一来源是 DataContext**：应用把树设在视觉根或 `VisualHost` 上，`VisualHost.DataContext` 向视觉根与后代继承（视觉根的本地值优先）；引擎不写也不清 `surface.DataContext`；没有 `WorkflowTree` 属性，也没有 `SetWorkflowTree` 别名，直接设 `DataContext`。Attach 后 DataContext 变化经 `VisualHost.Post` 合并重绑（WPF 调度器优先级语义）；没有 `_applying` 屏蔽。

### P5 · `x:Name` 写字段与视觉 `Name`

生成器把 `x:Name` 写成 code-behind 字段并写 `VisualElement.Name`；表面行为按 `VisualElement.Name` 解析 `PART_*`，模板只写 `x:Name="PART_Canvas"` 一次即可。显式 `Name="..."` 仍会在其后覆盖。

### P6 · 视觉 decorator 自己画标尺

`GridDecorator` 是视觉三层：网格层在内容下、标尺层在内容上，固定视口。`RulerBand => RulerThickness`，表面把它同时用于虚拟化 inset 与内容偏移。

### P7 · 视觉路径每线一个 LinkView

`LinkTemplate` 为每条 link（含 virtual link）建一个 `LinkView`；端点由 `DataTemplate` 绑定到 `Sender.Anchor.*` / `Receiver.Anchor.*`，颜色、`LineThickness`/`HighlightThickness` 与 `IsVirtual` 由表面 `ApplyLinkStyles` 写（宿主属性是 `VisualLink*`，模板 DP 同型 `float`）；`OnRender` 画 cubic 并 `PublishCurve`。

- **语言/运行时基线**：runtime 资产含 net461/netcoreapp3.0/net5/net8；Item Template 源码与生成代码要求 C# 10（`LangVersion latest`），默认 C# 7.3 的 .NET Framework 项目不能编译。net48 运行 smoke 在 `Src/Test/Compatibility/VeloxDev.WinForms.Net48.Smoke`；.NET Framework 的实时 per-monitor DPI 弱于现代 TFM。

---

## 四、指路

| 结论 | 在哪 |
|---|---|
| 适配器侧的硬限制、相机、输入、DPI | `memory/modules/WorkflowSystem/adapters/winforms.md` |
| XAML 运行时、生成器、对象资源、`.xaml.cs` | `SubRepos/Easel/Src/Easel` 与 `SubRepos/Easel/Src/Generators/Easel.Generator`；记忆见 `SubRepos/Easel/memory/modules/Easel/` |
| 适配器项目结构与三条轴 | `memory/modules/VeloxDev.WinForms/architecture.md` |
| 「怎么用这套模板搭一个 WinForms 工作流视图」 | `skills/veloxdev-create-workflow/references/practices/gui/winforms.md` 的 `## Item templates` |
