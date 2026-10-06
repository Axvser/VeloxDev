# Templates — WinForms

> **本文只写模板侧独有的东西**：条目产出什么形状、哪些接线必须手写、这一家模板特有的坑。
> 契约（七角色、附着属性、注册位置）在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；
> **适配器侧**的硬限制与坑在 `memory/modules/WorkflowSystem/adapters/winforms.md`，本文只指路不抄；
> 包结构与跨平台族划分在 `../architecture.md` 与 `../extension.md`。
> **路径写法**：下文裸文件名都相对 `Src/Templates/VeloxDev.WinForms.Templates/working/content/`；引用别处一律写全路径。

代码目录：`Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-<角色>/`。

---

## 一、这一家的条目产出什么形状

**七个条目全部只产出一个 `TemplateClass.cs`**，一个标记文件都没有（`primaryOutputs` 在各条目
`.template.config/template.json` 里都是 1 条；`ls workflow-*/` 下除 `.template.config/` 外只有
`TemplateClass.cs`）。⇒ **这一家的"平台语言"就是 C# 本身**，所有 `PART_*` 约定都退化成了
`Name = "PART_..."` 字符串字面量，没有任何名字作用域可依赖。

| 条目 | 形状 | 关键锚点 |
|---|---|---|
| tree-view | **2026-10-03 起**：`sealed class TemplateClass : WorkflowTreeView`（52 行）——调色板 + `CreateNodeView` / `CreateLinkView` / `OnBuildLinkMenu`。搭壳、两个私有嵌套类（`SurfaceCanvas` / `RulerOverlayForm`）、平移引擎、视图池全在适配器的基类里：`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowTreeView.cs` | 基类见左；模板只剩 `:18` 起的构造器、两个工厂 override 与 `OnBuildLinkMenu` |
| link-view | **2026-10-04 起**：`sealed class : Control` + `WorkflowLinkAttachment.Attach(this)`——自己的控件、自己的 `OnPaint`；雕窗口区域、端点订阅、几何、命中发布全在助手 `WorkflowLinkAttachment` | 助手见左；不再是基类派生 |
| node-view（**2026-10-04 起不再是薄派生**：自己的 `UserControl` + `WorkflowNodeAttachment.Attach`） | **2026-10-04 起**：`sealed class : UserControl`（436 行）——三个私有嵌套面板（`DynamicOutputsPanel` / `DynamicSlotRow` / `DoubleBufferedPanel`）、`OnRebound` / `OnCollapse`、绘制。绑定/定位/折叠/标题与端口解析在适配器的 `WorkflowNodeAttachment` | 附件见左 |
| slot-view（**2026-10-04 起不再是薄派生**：自己的 `Control` + `WorkflowSlotAttachment.Attach`） | **2026-10-04 起**：`sealed class : Control`（70 行）—— 图形 + 调色值 + `SlotPath`。**那个 `SvgPathParser` 搬进了适配器**（`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/SvgPathParser.cs`（`BuildPath` 在 `:20`）；该文件共 197 行） | 附件见左 |
| grid-decorator | **2026-10-03 起**：`sealed class : WorkflowGridDecorator`（41 行）——八个颜色 + 间距 + 每几条一条主线。网格与标尺的绘制（含 `OnPaintBackground` 那一趟）在基类 | 基类见左 |
| minimap-overlay | **2026-10-03 起**：`sealed class : WorkflowMinimapOverlay`（22 行）——4 个调色值。定位、拖拽映射、布局数学全在基类（它并实现了 `IWorkflowMinimapScrollSource`，那个接口也搬进了适配器） | 基类见左 |
| template-selector | **2026-10-03 起**：`sealed class : WorkflowTemplateSelector`（19 行）——构造函数里给 node / link 两个工厂赋值。四个 `Func<…, Control>` 工厂、分流与诊断都在基类。**树视图不再引用它**——树的基类自带一个记录视图角色的选择器 | 基类见左 |

---

## 二、模板里手写的是什么（其余 2026-10-03 起全委派给基类）

**模板只剩三件事**（`workflow-tree-view/TemplateClass.cs`，52 行）：调色板（构造器里从 `Template*`
符号解析四个表面颜色）、两个工厂 override（`CreateNodeView` → `new NodeView { ViewModel = node }`、
`CreateLinkView` → `new LinkView { ViewModel = link }`）、以及一个 `OnBuildLinkMenu` 空 override
（右键菜单的增删点，基类默认加 "Delete"）。

全在适配器基类 `Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowTreeView.cs` 里（模板里没有这些）：
外壳与三个部件（`SetScrollViewer/SetCanvas/SetGridDecorator` 在 `:381-383`）、`PART_GridDecorator => PART_Canvas`
别名（`:62`）、池数据源与虚拟化（`ApplyPan` 里写
`helper.Viewport`）、`ArrangeLinkViews` 的 z 序、小地图 `MinimapOverlay` 属性接线、布局/平移流水线。
⇒ **改这些机制要改适配器，不是在模板里补代码**；机制与坑见
`memory/modules/WorkflowSystem/adapters/winforms.md`。

⚠ **`PART_GridDecorator` 别名与独立 `workflow-grid-decorator` 条目仍无引用关系**：tree-view 模板里
连 `GridDecorator` 这个类型名都没有，独立装饰器类型名只在旧模板里出现过。**生成不生成
`winforms-v-decorator`，`winforms-v-tree` 的行为都不变**（见 `../architecture.md` §五）。

⚠ **两个工厂在模板里只赋 node/link**（`NodeViewFactory`/`LinkViewFactory`）；插槽与树 item 的工厂保持
`null` —— 一旦有插槽/树对象流进池，基类 `WorkflowTemplateSelector.CreateView` 会抛
`InvalidOperationException`（`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowTemplateSelector.cs:36` 起）。

---

## 三、这一家模板特有的坑

### P1 · 只生成 tree 不生成 minimap ⇒ **生成出来的代码编译不过** —— **tree-view 这一处已消除**

`IWorkflowMinimapScrollSource` 声明在适配器
（`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/IWorkflowMinimapScrollSource.cs`），tree 的基类引它、
不引 minimap 产物；minimap 模板与它的镜像都没有本地声明。`winforms-v-tree` 的编译期兄弟只剩
NodeView 与 LinkView，而 `verify-workflow-item-templates-all.ps1` 会把七条一起生成并编译（`verify-workflow-item-templates.ps1` 现在转发到它），再出现这类漏依赖会当场红。

⚠ **这类耦合本身没有任何文件写明，只能读代码发现**：若接口只声明在
`workflow-minimap-overlay/TemplateClass.cs`（`TemplateNamespace` 内）、没有任何 `using` 能引到它，而 tree-view
又在同命名空间下直接用它做模式匹配，那两个条目就**必须一起生成**，否则 tree-view 报 CS0246。
对照 Avalonia 那条"生成即失败"是**写在注释里的设计**（`../architecture.md` §7.3）。

### P2 · 六个 `ParseColor` 副本，而且这一家多一条 `Color.FromName` 兜底

**2026-10-03 起这六份没了**：解析器只有一处 —— 适配器的 `WorkflowSurfaceColors.Parse`
（`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowSurfaceColors.cs`），模板通过各自基类的
`protected static ParseColor` 调它。下面这段记的是修复前的形状，**结论（`Color.FromName` 兜底会静默吃掉写错的占位符）仍然成立**：

这一家的实现**比其他平台多一个兜底**：解析不出 `#RRGGBB`/`#AARRGGBB` 时
`return Color.FromName(value);`（现在只此一处：适配器 `Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowSurfaceColors.cs:44`）。
⇒ 占位符写错（少一位、拼错名字）时**不抛也不报**，`Color.FromName` 对未知名字返回
ARGB 全 0 的透明黑 —— 表现是"这条线/这个背景不见了"。改解析规则要动六处。

### P3 · `slotPath` 是**真的被解析**的，但只认 `M/m`、`A/a`、`Z`

解析器现在在**适配器**：`SvgPathParser` 搬成了独立文件 `Src/Adapters/VeloxDev.WinForms/Attached/Workflow/SvgPathParser.cs`：
`WorkflowSlotAttachment.Paint` 把 `SlotPath` 交给它（`WorkflowSlotAttachment.cs:264` 调 `BuildPath`、`SvgPathParser.cs:20` 定义）；
解析器只实现这三个命令（`SvgPathParser.cs:18` 的注释），`default` 分支是 `return path;`（`SvgPathParser.cs:83`，
注释「看不懂的命令：体面地停下，而不是抛」`SvgPathParser.cs:82`）。
⇒ 给 `--slotPath` 传一段含 `C`/`L`/`Q` 的路径，**图标会静默只画出一部分**，不报错。
（`A` 的实现还在 `SvgPathParser.cs:95` 的 `AddArc`：`rx==ry` 的圆弧、`d > 2r` 时把半径撑到 `d/2`、`largeArc==sweep` 选远心。）
坐标系是 1024 的 artboard 缩放到控件尺寸（`WorkflowSlotAttachment.cs` 的 `pathViewBox = 1024f` `:57`、
`g.ScaleTransform(target.Width / pathViewBox, …)` `:236`）—— 缩放**在描边之前**，所以 `BorderColor` 那条 `1.5f`
的描边在 20px 的插槽上只剩约 `1.5 × 20/1024 ≈ 0.03` 设备像素。
⚠ 注意这一家是**唯一既真的替换 `slotPath`、又真的画这道描边**的，`slotBorderColor` 在
WPF/WinUI/Avalonia 三家是空转参数（`../architecture.md` §7.1）。

### P4 · 输入端口靠**反射属性名**决定，没有任何控件名参与

`ResolveInputSlot()` 现在在**适配器**（`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowNodeAttachment.cs:239`）
遍历节点的 `Slots`，返回第一个"能作源、不能作目标"的口（`:249-251`）；这样的口不存在时返回 `null`。
注释 `:236-238` 说明了为什么这个回退要按通道读：**输入插槽的通道在渲染时可能还是生成器默认值，
因为写通道是走异步命令的**。模板在 `OnRebound` 里调它（`workflow-node-view/TemplateClass.cs:343`），
没解析出输入口时也按同一判据补一个裸端口（`:360-366`）。
⇒ 一个既不能作源也不能作目标的口会被当作"未配置"跳过（`:368-372`）—— 表现是端口行多一行或少一行，而不是报错。

### P5 · 同一份设计常量与两个"名字"都靠**跨文件约定**传递

| 传什么 | 怎么传 | 依据 |
|---|---|---|
| 世界原点要留出的标尺宽度 | 单一来源：`WorkflowTreeView.RulerReserve = SurfaceCanvas.DefaultRulerThickness`，节点卡不再复制 `+ 36`（旧模板里的那份已随重构消失） | `Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowTreeView.cs:47` |
| 画布平移量 | 适配器 `WorkflowNodeAttachment.SurfacePanOffset` 属性，由附件内部直接写，不再反射父链 | `Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowNodeAttachment.cs:192`（写入 `:201`） |
| 节点显示标题 | 视图声明的 `NodeTitle` 委托（在适配器里） | `Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowNodeAttachment.cs:345-351`（`IWorkflowNodeViewModel` 不暴露名字） |

⇒ 标尺与平移都是**单一来源/显式属性**，改名不再静默失效；**标题也不再靠反射** —— 由视图声明的 `NodeTitle`
委托读取（未声明即无标题）。这与适配器侧那几处解析接缝（见
`memory/modules/WorkflowSystem/adapters/winforms.md` §4.5）现已合并成同一套。

### P6 · 标尺带是一个 `WS_EX_LAYERED` 浮层窗体，生命周期是模板自己管的

`RulerOverlayForm`（适配器 `Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowTreeView.cs:1171`，
ctor `:1195`）由 `EnsureRulerOverlay()` 在 `OnHandleCreated` 时建（`:494-504`，调用在 `:502`），也允许在 `ApplyPan`
里惰性补建（`:830`）；窗体取 `FindForm()` 作 owner 并 `Show(owner)`（`:511`、`:515`）。
同步点：宿主 `LocationChanged` / owner `Move`（`:519`、`:520-521`），每次 `SyncRulerOverlay()` 重设
`Location = PART_ScrollViewer.PointToScreen(Point.Empty)` 与 `Size = PART_ScrollViewer.ClientSize`（`:530-531`）。
`RefreshSurface()` 只在**像素尺寸变了**才重建 `Bitmap`（`Format32bppPArgb`，`:1249`），
再缩放绘图后调 `UpdateLayeredWindow`（`:1351`，`UlwAlpha = 2` `:1179`）。
标尺带的 alpha 是 **`#70252526`**（`:1186`），注释 `:1184-1185` 写明这是**刻意偏离**：别家用 `0xC8`，
但这一家的带子同时压在卡片与网格上，`0xC8` 会让带下的网格只剩约 3.5 亮度、看不见。
⚠ "为什么非要开一个顶层窗体"的机制结论（子控件永远画在父的 `OnPaintBackground` 之上、
`WS_EX_LAYERED` 子窗口在某些系统上 `ERROR_NOT_SUPPORTED`）已经写在
`memory/modules/WorkflowSystem/adapters/winforms.md` §2.3，本文不抄。

### P7 · `ApplyPan` 会把越界的 pan "折进" `NegativeOffset` 并原地改写拖拽原点

`WorkflowTreeView.cs:811-827`（`ApplyPan`）：只要 `_panOffset` 的正分量存在，就 `layout.NegativeOffset += grow`，
然后 **同步减去** `_panOffsetAtPress` 与 `_panOffset` 的同一分量，注释写明理由：
"否则每次同一绝对增量的重入 `ApplyPan` 都会再折一次，平移会跑飞"。
⇒ 这一家的"静止时滚动偏移恒为 0、只有越过原点才长世界"是**基类**保证的（对照
`memory/modules/WorkflowSystem/adapters/winforms.md` §2.4 说的两套平移模型），
**改动这一段要同时验算"折进"与"回写 pan"两件事，只改一半的表现是平移加速或原点弹跳**。

### P8 · 同一家内部两个"网格装饰器"的语义不同

2026-10-03 起两者都在适配器里：独立角色是 `WorkflowGridDecorator.cs`，tree 内部那个是
`WorkflowTreeView.cs` 的私有嵌套 `SurfaceCanvas`。

| | `WorkflowGridDecorator`（独立角色） | `SurfaceCanvas`（tree 内部） |
|---|---|---|
| 画在哪 | `OnPaintBackground` 里画网格与标尺，`OnPaint` 空（`WorkflowGridDecorator.cs:168`、`:179`） | `OnPaintBackground` 画网格（`WorkflowTreeView.cs:1115`）；**没有 `OnPaint`** —— 连线由各自的池化视图自己画 |
| 标尺 | **自己画**：填充两条带 + `DrawRulers`（`WorkflowGridDecorator.cs:238`） | **不画**：`RulerBand => 0`（`WorkflowTreeView.cs:1113`），交给 `RulerOverlayForm` |
| `RulerThickness` | 有，默认 36（`WorkflowGridDecorator.cs:31`、`:142`） | 没有这个属性（浮层窗体持有 `RulerThickness`） |
| 调色板 | 八个可设属性，默认即模板默认（`WorkflowGridDecorator.cs:35-42`） | 网格背景跟随 `SurfaceBackground`，其余三个**硬编码字面量**（`WorkflowTreeView.cs` 的 `#2A2D2E`/`#3A3D40`/`#4D4D4D`） |

⇒ `RulerBand => 0` 是这一家 tree-view 的**正确**取值（标尺不占内容内缩，是浮层），
别照着独立装饰器的 `RulerBand => RulerThickness`（`WorkflowGridDecorator.cs:165`）
"修"成一致；两者是不同角色的两个对象。

### P9 · 连线的"雕窗"是这一家能把连线池化的前提，动几何就动它

WinForms 的子窗口**不透明、也不与兄弟合成**，所以连线视图不能像其余六家那样用一张透明
全幅画布去放：一旦做成矩形窗口，它要么盖掉网格，要么（透明版）把下面的兄弟一起擦掉 ——
这正是这一家早年放弃子窗口、改成"画布代画"的原因（`WorkflowSystem/adapters/winforms.md` 里
没有这条，只在旧注释里）。现在的做法是**把窗口区域雕成折线的描边带**：

- `RebuildGeometry`（适配器 `Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowLinkAttachment.cs`）：
  取四点 → `Widen(厚度 + 2×RegionPad)`（`:383`）→ 取 `GetBounds` 落在整像素上（`:385`），
  路径平移到窗口局部后交给 `Region`（`:440`）。
  ⇒ **窗口只在线的位置上存在**，网格在它周围照常可见，命中测试也只落在线上（视图还 `Enabled = false`）。
- `BackColor` 必须是**画布网格底色**（`:105`）：雕出来的带子会被自己的背景填满，颜色不一致就是一条可见的缝。
- `RegionPad = 1.5f`（`:61`）：给抗锯齿留的余量。调大 → 带子变宽，网格线被擦掉的缺口变明显；
  调小 → 线边缘被区域裁掉，看着发毛。
- ⚠ **零长度折线（连线手势的第一帧）`Widen` 会抛 `ExternalException`**（GDI+ 拒绝无法描边的路径），
  异常会从 `IsVisible` 的 setter 里冒到消息泵 —— 所以有 `IsDrawable`（`:446`，注释 `:444-445`）兜住零长度与 NaN 两种。
  改这一带时不要删掉它。

⇒ 结论：**连线的"一个视图一个窗口"是靠区域雕出来的，不是靠透明**。改厚度/折线形状/抗锯齿时，
三处要一起看：`_thickness`、`RegionPad`、`IsDrawable`。

---

## 四、指路（这些结论已经在别处写好，本文不抄）

| 结论 | 在哪 |
|---|---|
| 为什么装饰器/小地图的实现不进适配器、`FindControlByName` 的解析语义 | `memory/modules/WorkflowSystem/adapters/winforms.md` §一、§三·5/6 |
| 透明分层不可用 ⇒ 不透明绘制；节点/插槽的 `A == 0xFF` 硬约束 | 同上 §2.3（该节引的就是本文 `workflow-tree-view:340-353`、`workflow-node-view:716-725`） |
| 两套平移模型、`AutoScrollMinSize` 会重新打开 `AutoScroll` | 同上 §2.4、§2.5 |
| `SyncNow` 的由来与调用点 | 同上 §三·4 |
| `SetPointerPressSourceName` 只写不读、`GetTransform` 无消费者、`LayoutPropertyName` 死链 | 同上 §4.1、§4.2、§4.3 |
| 插槽锚点必须走 `SlotAnchorFromCanvasLocal` | 同上 §4.4 |
| 反射接缝（树成员 / pan / `OnMinimapScrollRequested`） | 同上 §4.5 |
| 拖拽期必须同步 `Update()` + 递归 `RedrawTree` | 同上 §2.7、§4.7 |
| 滚轮方向与缩放提交顺序（模板只消费，不改） | `memory/modules/WorkflowSystem/extension.md` §3.9 |
| 人面向的"怎么用这套模板" | `skills/veloxdev-create-workflow/references/gui/winforms.md` 的 `## Item templates` |
