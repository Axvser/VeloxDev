# Templates — Razor (Blazor)

> **本文只写模板侧独有的东西**：条目产出什么形状、哪些接线必须手写、这一家模板特有的坑。
> 契约（七角色、附着属性、注册位置）在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；
> **适配器侧**的硬限制与坑在 `memory/modules/WorkflowSystem/adapters/razor.md`（尤其 §一 组件对应表、
> §二·1 服务端没有元素/几何往返、§二·2 尺寸/网格/坐标轴/小地图视口归 JS 独占、§三 刻意背离、§四 坑），
> 本文只指路不抄；包结构与跨平台族划分在 `../architecture.md` 与 `../extension.md`。
> **路径写法**：下文裸文件名都相对 `Src/Templates/VeloxDev.Razor.Templates/working/content/`；引用别处一律写全路径。

代码目录：`Src/Templates/VeloxDev.Razor.Templates/working/content/workflow-<角色>/`。

---

## 一、这一家的条目产出什么形状

**七个条目都产出两个文件**（`TemplateClass.razor` + `TemplateClass.razor.cs`，
`primaryOutputs` 各 2 条）。⇒ 与别家不同，"写标记"在这家不是**某个**条目的特征，而是**全部**条目的形态；
甚至 `template-selector` 这个在七家里最像纯代码的条目，在这家也是一层 `.razor`（12 行）包着 `ViewPool`。

| 条目 | `.razor` 的形状 | 关键锚点（`.razor` / `.razor.cs`） |
|---|---|---|
| tree-view | 一个 `<WorkflowSurfaceBehavior>` + 三个**片段参数槽**（`GridDecorator` / `Minimap` / `ChildContent`），内容层只有**一个** `<TemplateSelector>`：节点与连线都由它物化 | `:7,9-16`、`:17,25,30` / `:23`、`:27-43` |
| link-view | 一个 `<svg>` + `<polyline points="@points">`（虚线 `stroke-dasharray="6 4"`） | `:17-21,24,32` / `:173-202`（几何）、`:184,193`（两道守卫） |
| node-view | 两层适配器行为包裹 + **定尺寸卡片 div**，`transform:scale()` 缩放 | `:8-10`、`:15-21` / `:77,81-90` |
| slot-view | 一个 `<svg viewBox="0 0 1024 1024">` + `<path d="TemplateSlotPath">` | `:11,12,15` / `:19-20,42-65` |
| grid-decorator | **薄壳**：`@if (Viewport is { } vp)` 后渲染适配器的 `<WorkflowGridDecorator>`（15 行） | `:4,6-14` / `:16,20,24,28` |
| minimap-overlay | **薄壳**：同上，渲染适配器的 `<WorkflowMinimapOverlay>`（15 行） | `:6-14` / `:16,20,24` |
| template-selector | `<ViewPool ItemsSource KeySelector>` + 一个 `<ItemTemplate>` | `:7-11` / `:38-54` |

⇒ 三个"薄壳"条目（decorator / minimap / selector）**自己不画任何东西**，
它们存在的唯一目的是把 `Template*` 符号解析成 CSS 值/组件参数后交给适配器组件。
⇒ **C# 侧的"形状"全在 `.razor.cs`**：这家的 `.razor` 几乎只有 `@if` + 元素树，
凡是数值（缩放因子、点串、rgba 串）都在 code-behind 算。

---

## 二、模板里必须手写、委派不掉的接线

1. **同一段标记里 `GridDecorator` 出现两次，是两层不同的东西**：
   外层 `<GridDecorator Context="vp">`（`workflow-tree-view/TemplateClass.razor:17`）**不是组件**，它是在给
   `WorkflowSurfaceBehavior` 的**片段参数** `GridDecorator` 传值（适配器：
   `Src/Adapters/VeloxDev.Razor/Attached/Workflow/WorkflowSurfaceBehavior.razor.cs:43` 的
   `RenderFragment<SurfaceViewport>?`），`Context="vp"` 给这个片段的形参起名；
   内层 `<GridDecorator Viewport="vp" …/>`（`:18`）才是**本模板生成的那个组件**。
   ⇒ 两者同名是必需的、不是笔误：若外层按类型解析，内层就成了它的 `ChildContent`，
   而生成的 `GridDecorator` 没有这个参数 —— 标记直接编译不过。**不要把这个嵌套"简化"成一层。**
   `Minimap` / `ChildContent` 是同一套写法（`:25,30`），只是**没有重名**，所以只有 decorator 这一处会看错。
   ⚠ 第三个名字相近的类型是 **`WorkflowGridDecorator`**（适配器里真正画标尺的那个），
   由生成的 `GridDecorator.razor:6` 渲染。改模板时不要把 `<WorkflowGridDecorator>` 当成重命名对象。

2. **tree-view 必须自己订阅树模型**，这是这一家最承重的一段手写代码。
   `ComponentBase, IDisposable`（`workflow-tree-view/TemplateClass.razor.cs:23`），订阅五类：
   树的 `PropertyChanged`（`:72-76`）、`Nodes` / `Links` 的 `CollectionChanged`（`:78-79`）、
   `VirtualLink` 的 `PropertyChanged`（`:83-87`）、以及**逐节点**的 `PropertyChanged`（`:115-125`）。
   理由写在类的注释 `:17-21`：Blazor 没有绑定自动刷新，而连线/节点的新增与拖拽都要有人触发重渲染
   （池只对 `VisibleItems` 自己的 `CollectionChanged` 负责，见第 6 条）。
   - 换树在 `OnParametersSet` 里按引用判定后重订（`:57-66`）。
   - 节点集合变化时**整个重订一遍**（`UnsubscribeNodeChanges()` → `SubscribeNodeChanges()`，`:150-156`），
     所以新增/删除节点不会漏订阅。
   - 拖拽期只对 `Anchor`/`Size` 两个属性名重渲染，且在 `WorkflowGeometryScope.IsZooming` 期间
     **直接 return**（`:137-148`）；理由 `:139-142`：缩放中由 JS 同步落位，这里重渲染会闪。
   - ⚠ **一处与注释不符、以代码为准**：`UnsubscribeTree()` 里那两条集合解订阅走的是**当前** `Tree`
     （`:100-104` 的 `if (Tree is not null) { Tree.Nodes.CollectionChanged -= …; }`），
     而 `OnParametersSet` 里 `Tree` 已经是**新**实例（参数先赋值、后调 `OnParametersSet`），
     于是**旧树上的两个集合处理器从来没有被摘掉**（`_subscribedTree`/`_subscribedVirtualLink` 两处
     走的是捕获字段，只有这一对走属性）。后果是换树后旧树仍然持有本组件并可能触发 `StateHasChanged`；
     `Dispose` 时摘的是当前树，所以 `:165-168` 那一路是对的。**未实测**，但读代码可判定。

3. **连线的视图由池物化，模板只提供 `LinkTemplate`**：tree-view 把
   `LinkTemplate="RenderGenericLink(sc)"`（`workflow-tree-view/TemplateClass.razor:36`）交给选择器，
   该模板是 `@code` 里的一个成员（`:44-47`），里面只干两件事：把生成的 `<LinkView>` 包进
   `display:contents;pointer-events:none` 的 wrapper（**`pointer-events:none` 是必需的**：SVG 铺满整张画布，
   少了它画布的平移/手势会被它吃掉；`display:contents` 让 wrapper 不产生盒子，SVG 的定位祖先仍是内容层），
   并把 `SurfaceCanvas` 的 `Width/Height` 传下去（连线是整画布尺寸的绝对定位 SVG）。
   虚线的 `stroke-dasharray` 仍是 link-view 条目里的字面量（`workflow-link-view/TemplateClass.razor:15`）；
   三个 `data-veloxdev-*` 属性（`link-view/TemplateClass.razor:18-20`）来自
   `WorkflowRuntimeIds.Get`（`:11-13`）—— 适配器把这个 API 设成 `public` **就是为了给模板/ demo 的
   link-view 用**（`Src/Adapters/VeloxDev.Razor/Attached/Workflow/WorkflowRuntimeIds.cs:11-13` 的 XML 明写）。
   ⇒ 漏写这三个属性不会报错，代价在深缩放：JS 无法把这条 polyline 与折叠后的实时插槽对上
   （机制见 `memory/modules/WorkflowSystem/adapters/razor.md` §二·1 / §二·2）。
   ⚠ JS 侧 `wwwroot/veloxdev.workflow.js:253-255` 的注释已按"连线也进池"改写（原句 "Link SVGs are not
   pooled" 已删）。**结论不变**：`resolveLinkPolyline` 每趟重新 query `[data-veloxdev-link-id]`、不缓存元素
   引用 —— 池化后元素随可见集进出 DOM，缓存本来也站不住。

4. **slot-view 必须被 `WorkflowSlotConnectionBehavior` 包住，且只能包一层**
   （`workflow-slot-view/TemplateClass.razor:8`）。二次包裹的后果写在生成的 tree-view 里：
   `workflow-tree-view/TemplateClass.razor:58-60` 的注释 ——
   再套一层"会重复测量锚点、重复挂连线手势处理器"。这是这一家唯一把"不要做什么"写进产物的注释。

5. **node-view 的两层包裹是契约的一部分**（`WorkflowSlotLayoutBehavior` 外、`WorkflowNodeDragBehavior` 内，
   `workflow-node-view/TemplateClass.razor:8-10`），文件头注释 `:4` 明说"keep the wrappers"。
   注意拖拽宿主的尺寸**不是**标记里的常量，而是 code-behind 从模型算出来的
   （`:10` 的 `width:{Node.Size.Width}px;height:{Node.Size.Height}px`），
   内部卡片才是 260×180 定尺寸后 `transform:scale(...)`（`:16-17`）。**两层尺寸不是一回事。**

6. **池喂的是 `Helper.VisibleItems`（可见节点 + 连线 + 虚拟连线），节点与连线由同一个选择器派发**：
   `Items="Tree.GetHelper().VisibleItems"`、`KeySelector="i => i"`，配 `NodeTemplate` + `LinkTemplate`
   两个模板（`workflow-tree-view/TemplateClass.razor:34-36`）。
   `VisibleItems` 由 `TreeHelper.Install` 里的 `EnableMap` 建出来
   （`Src/Core/VeloxDev.Core/WorkflowSystem/Templates/Helpers/TreeHelper.cs:113,119`），
   **首元素恒为 `tree.VirtualLink`**（`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Virtualization/WorkflowSpatialEx.cs:64-65,168-169`），
   其余按视口增删 ⇒ **虚拟连线天然被池覆盖**，不要再给模板加 `@if (Tree.VirtualLink.IsVisible)` 分支；
   画不画由生成的 `<LinkView>` 自己的 `CanRender` 门决定。
   ⚠ "选择器"在这一家是**组件**，不是选择器实例：适配器的对应物是 `ViewPool.ItemTemplate` + 消费方自己派发
   （`Src/Adapters/VeloxDev.Razor/README.md:15`；这家 `ViewPool.razor.cs` 没有 `TemplateSelector` 参数，
   整个适配器也没有 `ViewManager`）⇒ 判定"这家接没接选择器"看 `<TemplateSelector>` 那一行即可，
   **搜 `ViewPool.TemplateSelector` 在本家恒空**。
   同族对照：WPF / WinUI / Avalonia / Jalium 同样喂 `Helper.VisibleItems`；**WinForms 喂全量 `Nodes`**
   （`Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-tree-view/TemplateClass.cs:728`）；
   MAUI 喂的是去掉连线的包装（`NodeOnlyVisibleItems`，连线交给共享 overlay 画）。

7. **输入/输出插槽是模板自己按通道拆的**：`InputSlotsOf`（只带 source 标志、带任何 target 标志的都排除）与
   `OutputSlotsOf`（带 target 标志）（`workflow-tree-view/TemplateClass.razor.cs:183-192`），
   插槽显示名走**反射** `IConditionalSlotProvider<>`（`:201-227`，理由 `:194-200`：名字在
   `ConditionalSlot<>` 包装器上，不在插槽 VM 上）。
   两处尺寸也在模板里写死：输入 `SlotSize="18"`、输出 `SlotSize="14"`
   （`workflow-tree-view/TemplateClass.razor:63,75`），而 slot-view 自己的默认是 `IconSize = 20`
   （`workflow-slot-view/TemplateClass.razor.cs:19`）—— **改插槽大小要改的是 tree-view 这两个字面量**，
   不是 slot-view 的常量。

8. **标尺避让在这一家是"两个独立的 28"，模板只写了一个**：
   生成的 tree-view 给 decorator 传 `RulerThickness="28"`（`workflow-tree-view/TemplateClass.razor:19`），
   **但从不给 `<WorkflowSurfaceBehavior>` 传 `RulerThickness`**（`:9-16` 只有
   `GridSpacing` / `GridColor` / `Background`）—— surface 那边用的是适配器默认 28
   （`Src/Adapters/VeloxDev.Razor/Attached/Workflow/WorkflowSurfaceBehavior.razor.cs:79`）。
   两者相等只是**默认值巧合**。⇒ 改标尺厚度要同时改**两处**，只改 `:19` 会让刻度带与"世界原点预留"
   错开同样的像素数。同一份常量在 grid-decorator 条目里也是硬编码（`grid-decorator/TemplateClass.razor.cs:20`），
   而**两个条目的 `template.json` 都没有 `rulerThickness` 符号** ⇒ Razor 的标尺厚度**不可经 CLI 配置**。

---

## 三、这一家模板特有的坑

### P1 · 三个"结构性空转"符号：不是漏了 `replaces`，是没有对应的绘制面

`../architecture.md` §7.1 记了本家 5 个空转 symbol。它们与别家的空转**不同类**，根因都在结构上：

| 空转符号 | 为什么换不回来 |
|---|---|
| `surfaceBorderBrush`、`surfaceBorderThickness`、`surfaceCornerRadius`（tree-view） | 生成的 tree-view **没有任何外层容器元素**（`.razor` 的根就是 `<WorkflowSurfaceBehavior>`），而适配器的外壳 `<div class="veloxdev-wf-surface">` 是适配器渲染的、没有内联边框样式（`Src/Adapters/VeloxDev.Razor/Attached/Workflow/WorkflowSurfaceBehavior.razor:4`）⇒ 没地方画这个边框 |
| `gridBackground`、`minorGridColor`、`majorGridColor`（grid-decorator） | 在 Razor 里网格背景/细线/粗线/坐标轴**不由 decorator 画**，而由 surface 画布元素的 CSS 变量承载：`--veloxdev-gs/-gc/-mgc/-ac`（`WorkflowSurfaceBehavior.razor.cs:122-131`，参数 `Background:55` / `GridColor:59` / `MajorGridColor:67` / `AxisColor:75`）⇒ decorator 上放这三个颜色没有绘制面 |
| `slotBackground`（slot-view） | slot-view 只画一个 `<path>`（fill + stroke，`workflow-slot-view/TemplateClass.razor:12-16`），适配器的连接行为也不提供背景层；这一条是**唯一在 `template.json` 的 `description` 里自陈**的（`workflow-slot-view/.template.config/template.json:54`：`Accepted for cross-GUI CLI parity; this GUI's slot has no separate background surface.`） |

⇒ **不要在 Razor 上给这三个补 `replaces`**（理由与 `../extension.md` §4.3 同）。

### P2 · 生成 tree-view 之后，decorator 条目的**四个**符号被覆盖、三个仍有效

tree-view 实例化生成组件时**显式传了**哪些参数（`workflow-tree-view/TemplateClass.razor:18-23`）决定了这件事：

| decorator 的符号 | 用生成的 tree-view 时 |
|---|---|
| `gridSpacing`、`rulerBackground`、`rulerTickColor`、`rulerDividerColor` | **被覆盖、无效** —— tree-view 传的是自己的值，且这四个值里有三个是**硬编码字面量**（见 P3） |
| `axisColor`、`rulerLabelColor`、`majorLineEvery` | **有效**（tree-view 不传，落到生成组件自己的符号默认值上） |

⇒ 想改标尺配色却改了 `razor-v-decorator` 的参数时，表现是"什么都没发生"。
**单独**生成 `razor-v-decorator` 放在自己的表面上时，它的符号全部有效 —— 差别只在有没有 tree-view 宿主。

### P3 · tree-view 里的颜色：只有一个是符号，五个是硬编码，且变量名会骗人

`workflow-tree-view/TemplateClass.razor.cs:170-175`：

| 字段 | 值 | 是符号吗 |
|---|---|---|
| `Background` | `ToCss("TemplateSurfaceBackground")` | **是**（tree-view 的 `template.json` 里只有 `surfaceBackground` 一个颜色符号） |
| `MinorGridColor` | `ToCss("#2A2D2E")` | 否 |
| `RulerBackground` | `ToCss("#C8252526")` | 否 |
| `RulerTickColor` | `ToCss("#555555")` | 否 |
| `RulerDividerColor` | `ToCss("#3A3D40")` | 否 |
| `NodeForegroundCss` | `ToCss("#DD1E1E1E")` | 否（用在输出插槽的标签上，`.razor:73`） |

⚠ **变量名会骗人**：名叫 `MinorGridColor` 的那个字段喂的是 **surface** 的 `GridColor`（`.razor:15`），
而 surface 的 `MajorGridColor` / `AxisColor` / `MajorLineEvery` 在生成的 tree-view 里**从来不被传**
（`:9-16`）。⇒ 生成出来的项目里，**粗网格线颜色与坐标轴颜色根本改不了**（只能手工往
`<WorkflowSurfaceBehavior>` 上加参数），而细网格颜色要去改 code-behind 的那个字面量。
`AxisColor` 若不传，适配器会退回 `TickColor`（`Src/Adapters/VeloxDev.Razor/Attached/Workflow/WorkflowGridDecorator.razor.cs:91`）。

### P4 · minimap 薄壳把一个适配器默认值**写死覆盖**了，且没有符号

`workflow-minimap-overlay/TemplateClass.razor:13` 传 `ViewportFill="transparent"`，
而适配器的默认是 `rgba(255,255,255,0.15)`（`Src/Adapters/VeloxDev.Razor/Attached/Workflow/WorkflowMinimapOverlay.razor.cs:66`）。
⇒ 生成的小地图是"只有描边的视口框"，没有任何 `Template*` 或 CLI 参数能把它换回来 ——
想要半透明填充只能手改这一行。同一文件里的 `Width="180" Height="120"`（tree-view 传的，`:27`）
与 adaptor 默认一致（`:37,41`），改小地图尺寸要改 tree-view 的标记。

### P5 · 三条"参数/格式"暗坑，都会静默或延迟到运行期才炸

| 坑 | 依据 | 表现 |
|---|---|---|
| `gridSpacing` 的符号默认值是 **`'40d'`**（XAML 的 double 字面量风格），所以要 `ParseGridValue` 剥尾缀 `d` | `grid-decorator/TemplateClass.razor.cs:24,56-65` + `.template.config/template.json` 的 `"defaultValue": "40d"` | 不剥就是 `double.Parse("40d")` 抛 `FormatException` —— 是生成**组件构造时**才炸，`dotnet new` 只做文本替换不会发现 |
| 尾缀 `d` 只有**这一处**被剥：`nodeBorderThickness` / `nodeCornerRadius` 走 `WithCssUnits`（只补单位、不剥后缀） | `node-view/TemplateClass.razor.cs:72-73,96-109` | 若把这两个符号的值写成 XAML 风格的 `1d`，得到的是 `1dpx`，浏览器**静默丢弃**这条声明 ⇒ 边框消失、圆角消失，都不报错（默认值 `'1'`/`'6'` 恰好不带 `d`，所以开箱是对的） |
| `linkThickness` / `majorLineEvery` 是裸 `double.Parse` / `int.Parse` | `link-view/TemplateClass.razor.cs:61`、`grid-decorator/TemplateClass.razor.cs:28` | 传 `--linkThickness 2d` 或 `--majorLineEvery 5d` ⇒ 运行期 `FormatException`；默认值 `'2'`/`'5'` 不带 `d` |

### P6 · link-view 的 `Sync` 只在 `OnInitialized` 跑 —— 现在由池的 `@key` 兜住

`Sync(Link)`（订阅链自身与两个端点）**只从 `OnInitialized` 调用**（`workflow-link-view/TemplateClass.razor.cs:104-107`），
`OnParametersSet` 只在有 override 参数时重渲染（`:153-160`）；而 `BuildPoints()` 与 `@if` 门用的是
`CanRender` / `IsVirtual` 两个**状态字段**（`:93-97`，由 `Sync` 写）。
⇒ 这里成立的前提是「一个 link 实例始终配同一个 `LinkView` 实例」。
tree-view 现在把连线交给池（本文 §二·3 / §二·6），池对每个 item 下 `@key`（`KeySelector="i => i"`，
`Src/Adapters/VeloxDev.Razor/Attached/Workflow/ViewPool.razor:10`）⇒ 配对按**对象身份**稳定。
**历史**：连线还是手写 `@foreach (var link in Tree.Links)`（无 `@key`）时，集合重排/换对象会让组件实例按
**位置**复用、`CanRender`/`IsVirtual` 留在旧值上（该显示时不显示，且不报错）—— 那条结论是从代码与 Blazor
差分语义推得的**未实测**判断，对应的代码路径已经不存在。**以后若有人把连线拿出池自己 `foreach`，要补 `@key`。**

### P7 · 同一份小工具在六个文件里各抄一遍

`ToCss`（`#AARRGGBB` → `rgba(...)`）与 `HexByte` 在**六处**逐字重复：
`tree-view/TemplateClass.razor.cs:234-257`、`link-view/…:69-92`、`node-view/…:116-139`、
`slot-view/…:72-95`、`grid-decorator/…:71-94`、`minimap-overlay/…:40-63`（只有 `template-selector` 没有）。
⇒ 改解析规则（比如支持 3 位缩写、或支持 `#RGB`）要改六处。
（WinUI 那家的**两份**重复见 `../adapters/winui.md` §三·P4。）
同一类的还有**设计尺寸 260 在 node-view 里写了两份**：标记的 `width:260px;height:180px`
（`workflow-node-view/TemplateClass.razor:16`）与 code-behind 的 `DesignWidth = 260`（`.razor.cs:77`）。
后者是 `ScaleCss` 的分母（`:81-90`）⇒ 只改标记里的 260，缩放因子会按错误的设计宽算，
卡片内容与它的盒子对不上（表现是缩小时文字/插槽跑出卡片或被裁）。
标记内部还有一对必须同步的数字：卡头 `height:36px`（`.razor:23`）与卡体 `top:36px`（`:28`）。

---

## 四、指路（这些结论已经在别处写好，本文不抄）

| 结论 | 在哪 |
|---|---|
| 组件对应表、`SurfaceViewportFeed` 的用途、JS 侧持有几何 | `memory/modules/WorkflowSystem/adapters/razor.md` §一 |
| 服务端不做元素测量、几何往返、`WorkflowRuntimeIds` 的反查是线性扫描 | 同上 §二·1、§四·3 |
| 尺寸/网格/坐标轴/小地图视口归 JS 独占（本文 P1/P3 的"网格不是 decorator 画"的上游原因） | 同上 §二·2 |
| 跨进程编组没有失败信号、滚轮方向在 JS 侧翻号 | 同上 §二·4、§二·5 |
| 区域设置陷阱（写 CSS/SVG 的 `double` 必须 `InvariantCulture`，含解析侧，且模板/适配器两侧都有） | 同上 §四·1 —— 本文的 `ToCss` / `ToFixed` 系写法都在其射程内 |
| 适配器里没有 `_disposed` 守卫、`MarkDirty` 的 16ms 异步窗口 | 同上 §四·5 |
| `WorkflowCanvasTransformBehavior` 是静态助手、不是组件 | 同上 §四·6 |
| 五类机械改动、`InfoOverlay` 是 demo 独有（本文 §二·6 的池喂什么在此） | `../extension.md` §1.1 |
| 本家 5 个空转 symbol 的清单、24 个空转参数的全局盘点 | `../architecture.md` §7.1 |
| 七家同一条目的结构差异（连线怎么画、标尺厚度 28/36、minimap 薄壳 vs 自带实现） | `../architecture.md` §六 |
| 滚轮方向与缩放提交顺序（模板只消费，不改） | `memory/modules/WorkflowSystem/extension.md` §3.9 |
| 人面向的"怎么用这套模板" | `skills/veloxdev-create-workflow/references/gui/razor.md` 的 `## Item templates` |
