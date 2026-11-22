# Templates — WinForms

> 契约（七角色、附着属性、注册位置）在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；
> **适配器侧**的硬限制与坑在 `memory/modules/WorkflowSystem/adapters/winforms.md`；
> 包结构与跨平台族划分在 `../architecture.md` 与 `../extension.md`。
> **路径写法**：下文裸文件名都相对 `Src/Templates/VeloxDev.WinForms.Templates/working/content/`；引用别处一律写全路径。

代码目录：`Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-<角色>/`。

---

## 一、这一家的条目产出什么形状

**XAML-first**：带标记的四个条目产出 `TemplateClass.xaml` + `TemplateClass.xaml.cs`（与其余六家同名的代码隐藏命名）；`grid-decorator` / `minimap-overlay` / `template-selector` 仍是纯 `.cs`。`InitializeComponent` 由生成器输出为 private 普通方法。

| 条目 | 形状 | 关键锚点 |
| --- | --- | --- |
| tree-view | `TemplateClass.xaml` + `.xaml.cs`：根是 `UserControl`，XAML 声明 `PART_Surface` / `PART_GridDecorator` / `PART_ScrollViewer` / `PART_Canvas` / `PART_MinimapOverlay`，根上挂 `behaviors:WorkflowSurfaceBehavior.*`，画布上挂 `behaviors:WorkflowLinkLayer.*`；**code-behind 只有 `InitializeComponent()`** | 行为按 `Control.Name` 解析部件；资源里声明 `TemplateSelector` 与滚动条样式 |
| node-view | `TemplateClass.xaml` + `.xaml.cs`：`Border` 画卡面，`Panel` 里 `native:Label` 显示标题、`PART_OutputSlots` 由行行为生成端口行 | `WorkflowNodeViewBehavior` / `WorkflowNodeTitleBehavior` / `WorkflowNodeRowsBehavior`；行与输入口在面板内整体垂直居中 |
| link-view | `TemplateClass.xaml`（空壳 + 注释）+ `.xaml.cs`：**per-link 可选形态**；默认表面不池化连线，由 `WorkflowLinkLayer` 在 `PART_Canvas` 一趟画完 | `WorkflowLinkAttachment`（显式每线一视图时）；`WorkflowLinkLayer`（默认） |
| slot-view | `TemplateClass.xaml` + `.xaml.cs`：`Path` 画字形，`WorkflowSlotConnectionBehavior` 管拖线；code-behind 从 `DataContext` 取 slot 并把状态映射成填充色 | `TemplateSlotPath`、状态色；`WorkflowSlotConnectionBehavior` |
| grid-decorator | **纯 `.cs`**：`TemplateClass : Panel, IWorkflowGridDecorator`，同一文件还导出 `SurfaceScrollableControl` 与 `SurfaceCanvas`（tree 的 `local:` 类型） | 网格、标尺带、圆角表面 |
| minimap-overlay | **纯 `.cs`**：`TemplateClass : WorkflowMinimapOverlay`，只给调色板 | 定位与拖拽映射在适配器 |
| template-selector | **纯 `.cs`**：`TemplateClass : VeloxDev.WinForms.Xaml.TemplateSelector`，用 `DataTemplate` 属性按 item 类型返回模板 | tree 的资源按 `WorkflowTemplateSelector` 键引用它 |

**四个 XAML 条目的 code-behind 基类写全名**：`TemplateClass : VeloxDev.WinForms.Xaml.Controls.UserControl`。WinForms 原生 `UserControl` 与 XAML 的同名基类在同一引用图里，短名分不清继承的是哪一层。纯 `.cs` 条目继承原生类型。

---

## 二、模板里手写的是什么

- **tree**：唯一的代码是一行 `InitializeComponent()`；一条注释指出连线菜单与键/滚轮策略属于宿主（`WorkflowSurfaceBehavior.SetLinkMenuBuilder`、树的 `IInputEvents`）。调色板由 `template.json` 的 `Template*` 符号替换进 XAML。
- **selector**：只声明「哪个 item 用哪个视图」；dispatch、未设置工厂的诊断与接口实现都在适配器基类。
- **node / link / slot**：node 由 `WorkflowNodeViewBehavior` 挂 attachment、拖动与槽布局，`WorkflowNodeTitleBehavior` / `WorkflowNodeRowsBehavior` 负责标题和端口行的缩放/居中；link/slot 在构造里调 attachment；绘制逻辑留在自己的 `OnPaint`。
- **grid / minimap**：只给调色板与间距；网格/标尺/小地图数学在适配器与模板的绘制代码里。

⇒ **模板选择器接线与 WPF/Avalonia/Jalium 同形**：tree 的资源声明 `NodeTemplate`/`LinkTemplate` 两个 `DataTemplate` 与 `<local:CustomTemplateSelector x:Key="WorkflowTemplateSelector" … />`，根上 `behaviors:WorkflowSurfaceBehavior.TemplateSelector="{StaticResource WorkflowTemplateSelector}"`。派生类继承通用 `TemplateSelector`；优先级高于 `NodeViewType` / `LinkViewType`（后两者是按类型名反射的内置兜底）。

**Trimmed 默认画面与 WPF 对齐的基线**：`ClientSize=1085×683`（对应 WPF `Width=1100/Height=720` 的内容区）、`RulerThickness=28`、`SurfaceCornerRadius=0`、标尺底 `#C8252526`、HUD 左 46 / 底 12、minimap `Margin=0,12,12,0`（200×140）、节点标题/端口行按 collapse 缩放并在面板内整体垂直居中、slot 字形只填不描边；网格线在 `SmoothingMode.None` 下画，避免 GDI+ 抗锯齿把 1px 线画亮。仍不同的是窗口非客户区、滚动条（WinForms 自绘细条 vs WPF 原生带箭头）和文字光栅化（GDI+ vs WPF）。

---

## 三、这一家模板特有的坑

### P1 · 七个条目有编译期兄弟依赖，必须一起生成

tree 的 XAML 直接引用 `local:GridDecorator` / `local:SurfaceCanvas`（`grid-decorator` 条目）与 `local:CustomTemplateSelector`（`selector` 条目）；selector 又引用 `NodeView` / `LinkView`，node-view 引用 `SlotView`。**只生成 tree 会编译失败**。`verify-workflow-item-templates-all.ps1` 一次生成七个并编译；tree 的头部注释也写了「先生成兄弟条目」。

### P2 · `XamlRuntime.ParseColor` 会静默吃掉写错的颜色

解析不出 `#RRGGBB` / `#AARRGGBB` 时走 `Color.FromName`；未知名字返回 ARGB 全 0 的透明黑。⇒ 占位符少一位或拼错时**不抛不报**，表现是这条线/这个背景不见了。模板里的颜色都是 `template.json` 的 `replaces` 符号；校验脚本会断言生成文件里没有残留占位符。

### P3 · `SlotPath` 用 XAML 的 `Shapes.SvgPathParser`

解析器是 `VeloxDev.WinForms.Xaml.Controls.Shapes.SvgPathParser.Parse`（`<Path Data>` 共用那份）；它支持 `M/L/C/Q/A/Z` 这一档常用命令，看不懂的字符就地停下，**图标只画出一部分而不报错**。坐标系是 1024 的 artboard 缩放到控件尺寸，`BorderColor` 的 1.5f 描边在 20px 端口上只剩约 0.03 设备像素。

### P4 · 插槽行宿主名必须与槽布局行为同步

`PART_OutputSlots` 的 `x:Name` 必须同步到 `WorkflowNodeRowsBehavior.RowsHostName`，再经它传给 `WorkflowSlotLayoutBehavior.SlotEnumeratorNames`。行为在该宿主的后代里找 `DataContext` 是 `ISlotComponent` 的控件；名字对不上时 slot anchor 全 NaN、连线被隐藏。

### P5 · `x:Name` 同时写原生 `Name`

生成器把 `x:Name` 写成三层：code-behind 字段、`NameScope` 注册、原生 `Control.Name`。行为按原生 `Control.Name` 解析 `PART_*`，所以模板只写 `x:Name="PART_Canvas"` 一次即可。显式 `Name="..."` 仍会在 `x:Name` 之后覆盖原生值；两者名字不同时，行为找的是后写的那个。

### P6 · 标尺是 `WorkflowRulerOverlayForm`，不是模板里的控件

适配器的 `WorkflowSurfaceBehavior` 在表面句柄建立后创建分层浮层窗（`WS_EX_LAYERED` + `UpdateLayeredWindow`），由 `IWorkflowGridDecorator.RulerBand` 驱动。tree 的 `RulerBand` 是 0（标尺不占内容内缩）；独立 `grid-decorator` 的 `RulerBand => RulerThickness`。两者语义不同，别按「一致性」把 tree 改成非 0。

### P7 · 默认连线是画布上的一笔，不是每线一个窗口

`WorkflowLinkLayer` 挂在 `PART_Canvas` 上，在画布的 `Paint` 里按 `VisibleItems` 画所有可见线并 `PublishCurve`；表面行为在平移后调用 `WorkflowLinkLayer.Refresh` 重发几何（命中/悬停不读上一帧）。**这是 WinForms 的默认路径**：`ViewLinkLayer.IsActive=true`，池不再物化连线。端点模型坐标按 slot 缓存（节点内局部偏移），且 `ApplyPanCore` 先写 ScrollViewer Offset 再量插槽 —— 否则节点移出可见集、测量停住时，曲线出线方向会随平移翻转。

下面这段描述的是 **per-link 可选路径**（`workflow-link-view` 条目仍保留）：

`WorkflowLinkAttachment` 把窗口区域雕成描边带：底色必须是画布网格底色（XAML 控件写 `Background`，原生控件写 `BackColor`），`RegionPad = 1.5f` 给抗锯齿留余量；零长度折线会让 `Widen` 抛 `ExternalException`，`IsDrawable` 兜住零长度与 NaN。改线宽/形状时三处一起看：`_thickness`、`RegionPad`、`IsDrawable`。

---

## 四、指路

| 结论 | 在哪 |
|---|---|
| 适配器侧的硬限制、平移与 `AutoScroll` 互斥、同步重画 | `memory/modules/WorkflowSystem/adapters/winforms.md` |
| XAML 运行时、生成器、对象资源、`.xaml.cs` | `memory/modules/WinFormsXaml/architecture.md` |
| 适配器项目结构与三条轴 | `memory/modules/VeloxDev.WinForms/architecture.md` |
| 「怎么用这套模板搭一个 WinForms 工作流视图」 | `skills/veloxdev-create-workflow/references/gui/winforms.md` 的 `## Item templates` |
