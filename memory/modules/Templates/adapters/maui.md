# Templates — MAUI

> **本文只写模板侧独有的东西**：条目产出什么形状、哪些接线必须手写、这一家模板特有的坑。
> 契约（七角色、附着属性、注册位置）在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；
> **适配器侧**的硬限制与坑在 `memory/modules/WorkflowSystem/adapters/maui.md`，本文只指路不抄；
> 包结构与跨平台族划分在 `../architecture.md` 与 `../extension.md`。
> **路径写法**：下文裸文件名都相对 `Src/Templates/VeloxDev.MAUI.Templates/working/content/`；引用别处一律写全路径。

代码目录：`Src/Templates/VeloxDev.MAUI.Templates/working/content/workflow-<角色>/`。

---

## 一、这一家的条目产出什么形状

四个条目出 XAML（`link-view` / `node-view` / `slot-view` / `tree-view`），三个不出
（`grid-decorator` / `minimap-overlay` / `template-selector`）。

| 条目 | 形状 | 关键锚点 |
|---|---|---|
| link-view | **不是几何视图**：一个 `ContentView`（`InputTransparent="True"`）里放适配器的 `behaviors:WorkflowLinkOverlay`，把树＋四个滚动/内容偏移＋标尺厚度＋三个颜色绑过去 | `workflow-link-view/TemplateClass.xaml:13-22`、`.xaml.cs:17-41` 是 8 个 `BindableProperty` |
| node-view | `ContentView` 自己挂 `WorkflowSlotLayoutBehavior` 的三个名字 | `workflow-node-view/TemplateClass.xaml:8-11` |
| slot-view | 圆点由 `IDrawable` **按几何画**，不是 SVG | `workflow-slot-view/TemplateClass.xaml.cs:35-49` |
| tree-view | `ContentView`（`x:Name="Root"`）+ 代码里过滤出"只有节点"的池数据源 | `workflow-tree-view/TemplateClass.xaml:8`、`.xaml.cs:52-105` |
| grid-decorator | `sealed class : Grid, IWorkflowGridDecorator`，两个 `GraphicsView` + `IDrawable` | `workflow-grid-decorator/TemplateClass.cs:17,20-21,46-60` |
| minimap-overlay | **薄壳**：继承适配器的 `WorkflowMinimapOverlay`，只设四个颜色（21 行） | `workflow-minimap-overlay/TemplateClass.cs:12-20` |
| template-selector | `DataTemplateSelector` 子类，四个 `DataTemplate?`，`OnSelectTemplate` 抛 `"Xxx is not set."` | `workflow-template-selector/TemplateClass.cs:33` |

三层 z 序写在 tree-view 的注释里：`grid < links < nodes < rulers`（`workflow-tree-view/TemplateClass.xaml:35`）。

---

## 二、模板里必须手写、委派不掉的接线

### 2.1 link 层是 `ScrollView` 的**兄弟**，且在 `GridDecorator` **内部**

`workflow-tree-view/TemplateClass.xaml:32-53` 的结构是 `GridDecorator` → { `LinkView`, `ScrollView` }。
`LinkView` 的五个绑定全部指向 `PART_GridDecorator`（`:37-41`），而 `WorkflowTree` 走
`{Binding BindingContext, Source={x:Reference Root}}`（`:36`）。
文件里 `:33-35` 的注释把理由写明了：视口级 → **不随世界画布长大** → 深缩放不撞 Win2D 纹理上限，
并给出换算式 `px = Ruler + anchor + ContentOffset − Scroll`。

⇒ **把 `LinkView` 挪进 `ScrollView` 会让深缩放下的连线消失**，而且不报错。
对照 WPF/WinUI 的 link 层在 `Canvas` **里面** —— 这是这一家最不能照抄别家的一处。

### 2.2 池的数据源被换成了"只有节点"的包装

`PART_Canvas` 的 `ViewPool.ItemsSource` **不绑 `Helper.VisibleItems`**，而是绑
`{Binding NodeItemsSource, Source={x:Reference Root}}`（`workflow-tree-view/TemplateClass.xaml:50`）。
`NodeItemsSource` 是 tree-view 自己的 `BindableProperty`（`.xaml.cs:24-34`），
在 `BindingContextChanged` 时重建（`:14,36-45`），值是内部类 `NodeOnlyVisibleItems`
—— 一个 `ObservableCollection<IWorkflowViewModel>`，**逐项过滤掉 `IWorkflowLinkViewModel`**（`:52-105`）。

⇒ 这一家的 `TemplateSelector` 因此**只需要 `NodeTemplate`**，
`workflow-tree-view/TemplateClass.xaml:23` 的实例化只有这一个实参，注释 `:20-22` 说明原因。
**别的平台把 `Helper.VisibleItems` 直接喂给池**；在这一家照抄会重新引入"每条线一个 `GraphicsView`"，
注释 `:21-22` 说那正是深缩放消失的成因。

### 2.3 `PART_RulerOffsetHost` 是夹在 `ScrollView` 与 `PART_Canvas` 之间的第二个 `AbsoluteLayout`

`workflow-tree-view/TemplateClass.xaml:45-52`：`PART_ScrollViewer` → `PART_RulerOffsetHost`
（`TranslationX/Y` 绑 `PART_GridDecorator.RulerThickness`）→ `PART_Canvas`。
⇒ 标尺避让在这一家是**外层容器的位移**，不是其它家的 `Canvas.RenderTransform`。
`PART_Canvas` 自己仍是 `BackgroundColor="Transparent"`（`:49`）并挂 `ViewPool.TemplateSelector`（`:51`）。
⚠ `WorkflowSurfaceBehavior.CanvasName` 指的是 `PART_Canvas`（`:12`），**不是** `PART_RulerOffsetHost`。

### 2.4 插槽宿主 Grid 必须 `InputTransparent="True" CascadeInputTransparent="False"`

`workflow-node-view/TemplateClass.xaml:31`。这一对组合是**相反的两个方向**：
`InputTransparent="True"` 让自己不挡下层，`CascadeInputTransparent="False"`
阻止这个"透明"继续传给子元素 —— 于是里面的 `SlotView` 仍然收得到连线手势。
⇒ **只写前者会让插槽彻底点不动；只写后者等于没写。** 七家里只有这一家有这一对。

同一格还有 `ZIndex="6"`，与 WPF/WinUI 的 `Panel.ZIndex="6"` / `Canvas.ZIndex="6"` 是同一个值。

### 2.5 node-view 的"弹性缩放"是 code-behind 算的，不是布局

`workflow-node-view/TemplateClass.xaml:12-13` 的注释：code-behind 按
`collapsed width / 260` 缩放标题行、字号与插槽字形，让内容重排进折叠后的盒子、不裁切。
⇒ 设计尺寸 `260` 在这一家是 **`workflow-node-view/TemplateClass.xaml.cs:6` 的 `DesignWidth` 常量**，
不是标记里的 `Width="260"`（WPF/WinUI 那边是标记里的固定 `Grid Width="260" Height="180"`）。
**改节点设计尺寸时两家的改法不同。**

### 2.6 插槽状态是 `BindableProperty` + 两侧绑定

`SlotState` 在 slot-view 里注册（`workflow-slot-view/TemplateClass.xaml.cs:8-13`），
变更回调只做 `Invalidate()`（`:27-33`）；node-view 两处都绑上
（`workflow-node-view/TemplateClass.xaml:32` 绑 `State`、`:53` 绑 `Slot.State`）。

---

## 三、这一家模板特有的坑

### P1 · `slotPath` 是空转参数，因为这一家从**几何**画插槽

`workflow-slot-view/.template.config/template.json` 声明了 `slotPath` 却没有 `replaces`。
根因在代码：`SlotDrawable.Draw` 用 `canvas.FillCircle` / `canvas.DrawCircle`
按 `dirtyRect` 算半径（`workflow-slot-view/TemplateClass.xaml.cs:40-48`），
一份 SVG 路径都没有。⇒ **想在 MAUI 上换插槽图标，要改的是 `SlotDrawable`，不是任何 `TemplateSlotPath`。**
其余 23 个空转参数与全局清单见 `../architecture.md` §7.1。

### P2 · `NodeOnlyVisibleItems` 不处理 `Replace` / `Move`

`workflow-tree-view/TemplateClass.xaml.cs:72-105` 的 `switch` 只有 `Add` / `Remove` / `Reset`。
⇒ 上游若用 `Replace` 原地换掉一个可见项，池**不会**收到任何通知，节点视图不刷新也不报错
（与本仓库适配器侧"`ViewManager` 多半不处理 `Replace`"是同一类问题，
对照表见 `memory/modules/WorkflowSystem/adapters/wpf.md` §三·5）。

### P3 · 包装器的重订只在 `BindingContextChanged` 时发生

`workflow-tree-view/TemplateClass.xaml.cs:14` 把重建挂在 `BindingContextChanged` 上，
`:38-41` 在重建前 `Detach()` 旧的。⇒ **换树必须换 `BindingContext`**；
直接改底层树的 `VisibleItems` 内容不会重建包装器（这是对的），
但如果宿主换了树对象却没触发 `BindingContextChanged`，池会继续订阅旧的集合 —— 表面显示旧数据。

### P4 · grid-decorator 的两个 `GraphicsView` 都必须 `InputTransparent = true`

`workflow-grid-decorator/TemplateClass.cs:46-59`：`_gridGraphicsView` 与 `_rulerGraphicsView`
各自 `InputTransparent = true`，后者还 `ZIndex = 10` 并且**加在 `Children` 的最后**（`:59-60`）。
⇒ 漏掉 `InputTransparent` 会让整块画布的指针手势被装饰器吃掉（平移、框选全失效），
漏掉 `ZIndex` 或添加顺序会让标尺被网格盖住。

### P5 · `RulerThickness` 在这一家是 `28d`，而且**只有这一家是 double 字面量**

`workflow-grid-decorator/TemplateClass.cs:24` 写的是 `28d`（其余各家写 `28` 或 `28.0`）。
与 WinForms/Jalium 的 `36` 对照表在 `../architecture.md` §六·轴 1。

---

## 四、指路（这些结论已经在别处写好，本文不抄）

| 结论 | 在哪 |
|---|---|
| 原生 extent 是异步重测的、`await Task.Yield()` 两次、`PanGestureRecognizer` 平移、没有空白判定 | `memory/modules/WorkflowSystem/adapters/maui.md` |
| 槽位锚点走 `GetCenterRelativeTo` + `SlotAnchorFromCanvasLocal` | 同上 |
| `ViewManager` 用 16 ms 定时器代替 `DispatcherPriority.Background`（`BatchSize = 8`） | 同上 |
| `WorkflowLinkOverlay` 换掉 `WorkflowCanvasTransformBehavior` 这层关系 | 同上 |
| 滚轮方向与缩放提交顺序 | `memory/modules/WorkflowSystem/extension.md` §3.9 |
| 人面向的"怎么用这套模板" | `skills/veloxdev-create-workflow/references/gui/maui.md` 的 `## Item templates` |
