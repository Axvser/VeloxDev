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
| tree-view | `UserControl` + 程序化搭壳，内部两个私有嵌套类：`SurfaceCanvas : Panel, IWorkflowGridDecorator` 与 `RulerOverlayForm : Form` | `:32`、`:121-146`、`:181`、`:354` |
| link-view | `Control`，但**从不作为子窗口存在** —— 由宿主画布在自己的 `OnPaint` 里代画 | `:18`、`:262`（`public void Render(Graphics)`） |
| node-view | `UserControl` + 三个私有嵌套面板（`DynamicOutputsPanel` / `DynamicSlotRow` / `DoubleBufferedPanel`） | `:28`、`:129`、`:219`、`:282` |
| slot-view | `Control` + 一个手写的 SVG 路径解析器（嵌套 `static class SvgPathParser`） | `:22`、`:198` |
| grid-decorator | `Panel, IWorkflowGridDecorator`，网格与标尺**画在 `OnPaintBackground`**，`OnPaint` 是空的 | `:17`、`:70-83` |
| minimap-overlay | `Panel, IWorkflowMinimapOverlay, IWorkflowMinimapScrollSource`，自带右上角定位与拖拽；**并在同一文件底部声明了 `IWorkflowMinimapScrollSource` 接口本身** | `:20`、`:78-85`、`:332-334` |
| template-selector | 实现适配器的 `IWorkflowTemplateSelector`（**自定义接口，不是 `DataTemplateSelector`**），四个 `Func<…, Control>` 工厂 | `:12`、`:14-17` |

---

## 二、模板里必须手写、委派不掉的接线

1. **外壳完全程序化**：五个开关/名字一次挂全（`WorkflowSurfaceBehavior.SetIsEnabled/SetZoomEnabled/
   SetScrollViewerName/SetCanvasName/SetGridDecoratorName/SetPointerPressSourceName`，`:153-159`），
   而 `PART_ScrollViewer` / `PART_Canvas` 的 `Name` 是**手工赋的**（`:126`、`:141`）。
   ⚠ `SetPointerPressSourceName`（`:159`）在这家**只写不读**，写了不生效也不报错 —— 依据与四个写入点见
   `memory/modules/WorkflowSystem/adapters/winforms.md` §4.1，本文不抄。
2. **`PART_GridDecorator` 是 `PART_Canvas` 的别名**：`public Control PART_GridDecorator => PART_Canvas;`（`:55`），
   `SetGridDecoratorName(this, "PART_Canvas")`（`:158`）。网格的实体是内部类 `SurfaceCanvas`（`:181`），
   **独立生成的那个 `workflow-grid-decorator` 条目的类型名，在这个文件里只出现在一句注释里**（`:1` 的
   "composes the GridDecorator"），正文没有任何 `GridDecorator` 类型引用。
   ⇒ **生成不生成 `winforms-v-decorator`，`winforms-v-tree` 的行为都不变。** 对照：WPF/WinUI/Avalonia/MAUI
   的 tree-view 都真的实例化 `<GridDecorator>`（见 `../architecture.md` §五）。
3. **插槽布局只挂枚举器通道，没有输入端口那一档**：只设 `SetSlotEnumeratorNames(this, "PART_DynamicOutputs")`
   与 `SetIsEnabled`（`workflow-node-view/TemplateClass.cs:117-118`），**从不调用 `SetSlotNames`**
   —— 七家里只有这一家不设 `SlotNames`。产出文件里**不存在名为 `PART_InputSlot` 的控件**，
   `PART_InputSlot` 只出现在两处注释里（`:183`、`:497`）。
4. **坐标宿主要传"类型"而不是名字**：`SetCoordinateHostType(this, typeof(Panel))`
   （`workflow-node-view/TemplateClass.cs:114`、`:119`，节点拖拽与插槽布局各一次）。
   其余四家有标记语言的平台写 `CoordinateHostName="PART_Canvas"`
   （如 `Src/Templates/VeloxDev.WPF.Templates/working/content/workflow-node-view/TemplateClass.xaml:10,29`）。
   ⇒ **抄这段时不要把 `typeof(Panel)` 换成一个字符串**：这一家的附着属性根本没有"名字"这一档。
5. **池的数据源是整棵 `Nodes`，且只给一个工厂**：`ViewPool.SetItemsSource(PART_Canvas, _tree?.Nodes)`
   与 `ViewPool.SetTemplateSelector(PART_Canvas, _selector)`（`:728-729`）成对出现；
   `_selector` 只在构造时赋了 `NodeViewFactory`（`:150-151`），**另外三个工厂保持 `null`**。
   连线不进池（由渲染器列表代画）、插槽由节点卡片自己建，所以池只会遇到节点；
   一旦有非节点对象流进池，`CreateView` 会抛 `InvalidOperationException`（selector 的 `:22-31`）。
   ⚠ 与其余六家的**结构差异**：别家喂的是 `Helper.VisibleItems`（空间窗口），这一家喂的是全量 `Nodes`。
   ⚠ `Helper.VisibleItems` 在 `Src/Adapters/VeloxDev.WinForms/` 里**零命中**，只被用来算
   `SetVirtualizeInset`（`:990`）→ **这一家模板不发散虚拟化，池只是个回收缓存**。demo 同形：
   `Examples/Workflow/WinForms Trimmed/Demo/Views/Workflow/TreeView.cs:736` 是同一行代码。
6. **连线渲染器列表的装配顺序与生命周期**：`_linkRenderers`（`:96`）在构造时交给画布（`:143`），
   画布用它 `OnPaint` 逐条 `lv.Render(g)`（`:264-276`，注释 `:91-95` 说明为什么不做子窗口）；
   `RebuildLinkRenderers` 先 `Dispose` 全部旧的再重建（`:746-764`），顺序是
   **`_tree.VirtualLink` 在前、真实连线随后**；每条渲染器的 `ExternalInvalidate` 指向画布（`:779-784`）。
   ⇒ 改"连线怎么画"要动的是 host 的 `OnPaint` 与 `Render` 两侧，不是某个标记里的形状。
7. **小地图是"设属性"而不是"写标记"**：`MinimapOverlay` 属性的 setter 负责
   `value.Name = "PART_MinimapOverlay"`、`Controls.Add`、`BringToFront`、
   `WorkflowSurfaceBehavior.SetMinimapOverlayName(this, "PART_MinimapOverlay")`，并订阅/退订
   `IWorkflowMinimapScrollSource.ViewportScrollRequested`（`:663-697`）。
   ⇒ **不设这个属性 = 适配器找不到小地图**（没有 XAML 里那个"摆进去就生效"的等价物）。
8. **布局/平移是一条显式流水线**：`ScheduleLayout()`（`_layoutPending` 去重 + `BeginInvoke`）
   → `ApplyCanvasSize()` → `WorkflowSurfaceBehavior.Refresh(this)` → `ApplyPan()`（`:1003-1028`）。
   `ApplyPan` 负责把世界原点推给装饰器（`:947-954`）、标尺浮层与树视口（`:958` 起），
   并在推之前对每张节点卡同步 `ApplyPosition()` + `WorkflowSlotLayoutBehavior.SyncNow(nodeView)`
   （`:929-936`，同步重测的理由见 `memory/modules/WorkflowSystem/adapters/winforms.md` §三·4）。

---

## 三、这一家模板特有的坑

### P1 · 只生成 tree 不生成 minimap ⇒ **生成出来的代码编译不过**

`IWorkflowMinimapScrollSource` 这个接口**只存在于这一家的模板里**：它声明在
`workflow-minimap-overlay/TemplateClass.cs:332-334`（文件末尾，`TemplateNamespace` 内），
没有任何 `using` 能引到它；tree-view 在同命名空间下直接用它做模式匹配
（`:674`、`:686`）。⇒ **两个条目必须一起生成**，否则 tree-view 报 CS0246。
全仓库 `grep IWorkflowMinimapScrollSource` 只命中这四个位置（均在 `Src/Templates/VeloxDev.WinForms.Templates/` 内）。
对照 Avalonia 那条"生成即失败"是**写在注释里的设计**（`../architecture.md` §7.3），
这一条**没有任何地方写过**，只能靠读代码发现。

### P2 · 六个 `ParseColor` 副本，而且这一家多一条 `Color.FromName` 兜底

`private static Color ParseColor` 在 `workflow-{grid-decorator,link-view,minimap-overlay,node-view,slot-view,tree-view}`
六处各一份（`:253`、`:296`、`:299`、`:751`、`:149`、`:1090`），只有 `template-selector` 没有。
这一家的实现**比其他平台多一个兜底**：解析不出 `#RRGGBB`/`#AARRGGBB` 时
`return Color.FromName(value);`（`workflow-link-view/TemplateClass.cs:320`，其余五份同形）。
⇒ 占位符写错（少一位、拼错名字）时**不抛也不报**，`Color.FromName` 对未知名字返回
ARGB 全 0 的透明黑 —— 表现是"这条线/这个背景不见了"。改解析规则要动六处。

### P3 · `slotPath` 是**真的被解析**的，但只认 `M/m`、`A/a`、`Z`

`workflow-slot-view/TemplateClass.cs:112` 把 `TemplateSlotPath` 交给嵌套的
`SvgPathParser.BuildPath`（`:198`）；解析器只实现这三个命令（`:195-198` 的注释），
`default` 分支是 `return path;`（`:261-263`，注释 "Unsupported command: stop gracefully rather than throw"）。
⇒ 给 `--slotPath` 传一段含 `C`/`L`/`Q` 的路径，**图标会静默只画出一部分**，不报错。
（`A` 的实现还在 `:275-319`：`rx==ry` 的圆弧、`d > 2r` 时把半径撑到 `d/2`、`largeArc==sweep` 选远心。）
坐标系是 1024 的 artboard 缩放到控件尺寸（`ArtboardSize = 1024f` `:104`、`g.ScaleTransform(Width/1024, Height/1024)` `:116`）
—— 缩放**在描边之前**，所以 `TemplateSlotBorderColor` 那条 `1.5f` 的描边在 20px 的插槽上
只剩约 `1.5 × 20/1024 ≈ 0.03` 设备像素。
⚠ 注意这一家是**唯一既真的替换 `slotPath`、又真的画这道描边**的运动方式，`slotBorderColor` 在
WPF/WinUI/Avalonia/Jalium 四家是空转参数（`../architecture.md` §7.1）。

### P4 · 输入端口靠**反射属性名**决定，没有任何控件名参与

`ResolveInputSlot()`（`workflow-node-view/TemplateClass.cs:594` 起）遍历节点的
**全部公开实例属性**，找名字等于 `InputSlot`（忽略大小写）的那个（`:613`），
找不到时退回到"能作源、不能作目标"的固定插槽（`:618` 起）；属性 getter 抛异常时 `catch` 跳过（`:607-610`）。
注释 `:588-592` 说明了为什么不用通道判定：**输入插槽的通道在渲染时可能还是生成器默认值，
因为写通道是走异步命令的**。
⇒ 你的节点 VM 若把输入端口起名成别的（如 `Input`），这一家会退回按通道猜 —— 猜错的表现是
"多出一行带标签的输出行"而不是报错。

### P5 · 同一份设计常量与两个"名字"都靠**跨文件约定**传递

| 传什么 | 怎么传 | 依据 |
|---|---|---|
| 世界原点要留出的标尺宽度 | tree-view 定义 `public const double RulerReserve = SurfaceCanvas.DefaultRulerThickness`（`:37`），而 node-view **重新硬编码字面量 `+ 36`** | `workflow-node-view/TemplateClass.cs:419-420`；两个文件没有共享类型，node-view 引不到 tree-view 的常量 |
| 画布平移量 | node-view 沿父链**反射**找名为 `PanOffset` 的 `Point` 属性 | `:463-475`（注释 `:457-462` 写明"surface 是 tree view 里的私有嵌套控件"） |
| 节点显示标题 | 反射 `Name` ?? `Title` | `:490`（`IWorkflowNodeViewModel` 不暴露名字） |

⇒ 改这三处任一名字/数值都是**静默失效**：标尺偏移不一致 → 连线整体错位一格；
`PanOffset` 改名 → 节点不再跟随平移（只剩网格在动）；`Name`/`Title` 改名 → 卡片标题为空。
这与适配器侧那三处反射（见 `memory/modules/WorkflowSystem/adapters/winforms.md` §4.5）是**两套独立的反射接缝**。

### P6 · 标尺带是一个 `WS_EX_LAYERED` 浮层窗体，生命周期是模板自己管的

`RulerOverlayForm`（`:354`）由 `EnsureRulerOverlay()` 在 `OnHandleCreated` 时建
（`:800-809`），也允许在 `ApplyPan` 里惰性补建（`:916-918`）；窗体取 `FindForm()` 作 owner
并 `Show(owner)`（`:824`）。同步点挂得很多：宿主 `Resize` / `LocationChanged` / owner 的 `Move` / `Resize`
（`:827-830`），每次都 `SyncRulerOverlay()` 重设 `Location = PART_ScrollViewer.PointToScreen(Point.Empty)`
与 `Size = PART_ScrollViewer.ClientSize`（`:834-842`）。
`RefreshSurface()` 只在**像素尺寸变了**才重建 `Bitmap`（`:446-450`，`Format32bppPArgb`），
再按 `pw/Width`、`ph/Height` 缩放绘图后调 `UpdateLayeredWindow`（`:554`，`UlwAlpha = 2`）。
标尺带的 alpha 是 **`#70252526`**（`:372`），注释 `:368-371` 写明这是**刻意偏离**：别家用 `0xC8`，
但这一家的带子同时压在卡片与网格上，`0xC8` 会让带下的网格只剩约 3.5 亮度、看不见。
⚠ "为什么非要开一个顶层窗体"的机制结论（子控件永远画在父的 `OnPaintBackground` 之上、
`WS_EX_LAYERED` 子窗口在某些系统上 `ERROR_NOT_SUPPORTED`）已经写在
`memory/modules/WorkflowSystem/adapters/winforms.md` §2.3，本文不抄。

### P7 · `ApplyPan` 会把越界的 pan "折进" `NegativeOffset` 并原地改写拖拽原点

`:905-914`：只要 `_panOffset` 的正分量存在，就 `layout.NegativeOffset += grow`，
然后 **同步减去** `_panOffsetAtPress` 与 `_panOffset` 的同一分量，注释 `:910-911` 写明理由：
"否则每次同一绝对增量的重入 `ApplyPan` 都会再折一次，平移会跑飞"。
⇒ 这一家的"静止时滚动偏移恒为 0、只有越过原点才长世界"是由模板保证的（对照
`memory/modules/WorkflowSystem/adapters/winforms.md` §2.4 说的两套平移模型），
**改动这一段要同时验算"折进"与"回写 pan"两件事，只改一半的表现是平移加速或原点弹跳**。

### P8 · 同一家内部两个"网格装饰器"的语义不同

| | 独立条目 `workflow-grid-decorator` | tree-view 内部的 `SurfaceCanvas` |
|---|---|---|
| 画在哪 | `OnPaintBackground` 里 `Render`，`OnPaint` 空（`:70-83`） | `OnPaintBackground` 画网格（`:243-262`），`OnPaint` 画连线（`:264-276`） |
| 标尺 | **自己画**：填充两条带 + `DrawRulers`（`:103-106`、`:142`） | **不画**：`RulerBand => 0`（`:241`），交给 `RulerOverlayForm` |
| `RulerThickness` | 有，默认 36（`:21`、`:49`） | 没有这个属性（浮层窗体持有 `RulerThickness`，`:823`） |
| 调色板 | 八个 `Template*` 占位符（`:23-32`） | 网格背景用 `TemplateSurfaceBackground`，其余三个**硬编码字面量**（`:191-193` 的 `#2A2D2E`/`#3A3D40`/`#4D4D4D`） |

⇒ `RulerBand => 0` 是这一家 tree-view 的**正确**取值（标尺不占内容内缩，是浮层），
别照着独立装饰器的 `RulerBand => RulerThickness`（`workflow-grid-decorator/TemplateClass.cs:68`）
"修"成一致；两者是不同角色的两个对象。

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
