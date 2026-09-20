# Templates — WinUI

> **本文只写模板侧独有的东西**：条目产出什么形状、哪些接线必须手写、这一家模板特有的坑。
> 契约（七角色、附着属性、注册位置）在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；
> **适配器侧**的硬限制与坑在 `memory/modules/WorkflowSystem/adapters/winui.md`，本文只指路不抄；
> 包结构与跨平台族划分在 `../architecture.md` 与 `../extension.md`。
> **路径写法**：下文裸文件名都相对 `Src/Templates/VeloxDev.WinUI.Templates/working/content/`；引用别处一律写全路径。

代码目录：`Src/Templates/VeloxDev.WinUI.Templates/working/content/workflow-<角色>/`。

---

## 一、这一家的条目产出什么形状

四个条目出 XAML（`link-view` / `node-view` / `slot-view` / `tree-view`），三个不出
（`grid-decorator` / `minimap-overlay` / `template-selector`）。

| 条目 | 形状 | 关键锚点 |
|---|---|---|
| link-view | **空壳**（6 行，XAML 的全部内容是根元素上的 `Clip="{x:Null}"`），几何在代码里构造 | `workflow-link-view/TemplateClass.xaml:5`、`.xaml.cs:26-28,57-58` |
| node-view | `Viewbox` + 固定设计尺寸卡片，三处 `Clip="{x:Null}"`，两侧 `SlotState` 都绑 | `workflow-node-view/TemplateClass.xaml:12,17-18,39-40,63` |
| slot-view | `Viewbox` + 一个 `Path`，状态在 code-behind 里换算成 `Fill` | `workflow-slot-view/TemplateClass.xaml:15-19`、`.xaml.cs:33-42` |
| tree-view | 外壳 + 两个 `DataTemplate` 资源 + 一个 `TemplateSelector` 资源 | `workflow-tree-view/TemplateClass.xaml:15-38` |
| grid-decorator | `sealed class : Grid, IWorkflowGridDecorator`，**池化** `Line`/`TextBlock`，八个静态画刷 | `workflow-grid-decorator/TemplateClass.cs:28,32-39` |
| minimap-overlay | **薄壳**：继承适配器的 `WorkflowMinimapOverlay`，只设四个画刷（42 行） | `workflow-minimap-overlay/TemplateClass.cs:13-41` |
| template-selector | `DataTemplateSelector` 子类，四个 `DataTemplate?` 属性 | `workflow-template-selector/TemplateClass.cs:12-20` |

三个不出 XAML 的条目**仍然可以有 XAML 之外的形状差**：template-selector 是七家里唯一
**同时覆写两个重载**的（`SelectTemplateCore(object)` 与 `SelectTemplateCore(object, DependencyObject)`，
后者直接转发给前者，`workflow-template-selector/TemplateClass.cs:22,36-37`）。

---

## 二、模板里必须手写、委派不掉的接线

1. **节点模板不绑 `RenderTransform`**。`workflow-tree-view/TemplateClass.xaml:17-21` 只绑
   `Width` / `Height` / `Canvas.Left` / `Canvas.Top` / `Canvas.ZIndex` 五项，**没有**第四项变换绑定 ——
   对比 WPF/Avalonia 两家都在模板根上绑了 `WorkflowCanvasTransformBehavior.Transform`。
   原因（画布平移走合成变换、不镜像到子元素）在 `memory/modules/WorkflowSystem/adapters/winui.md` §三·D1，
   本文只指路。⇒ **跨平台抄这段模板时，"少了一个绑定"是这一家的正确形态，不是漏写。**
2. **连线模板故意不绑 `Width`/`Height`**，理由写在 `workflow-tree-view/TemplateClass.xaml:24-26`：
   `LinkView drives its own box (position −ActualOffset, size = model ActualSize) in code so the box is
   authoritative and never lags an ElementName ActualWidth binding while the canvas grows during deep zoom.`
   ⇒ 与 WPF 的 `Width="{Binding ElementName=PART_Canvas, Path=ActualWidth}"` **正好相反**。
3. **`PART_Canvas` 的几何走 `Layout.ActualSize`**，不是 `ElementName=PART_Canvas` 自引用：
   `workflow-tree-view/TemplateClass.xaml:50-52`。而 `Background="Transparent"`（`:53`）与
   `ViewPool` 两个属性（`:54-55`）与其他家一致。
4. **`ScrollViewer` 要显式 `ZoomMode="Disabled"`**（`workflow-tree-view/TemplateClass.xaml:49`）——
   WinUI 的 `ScrollViewer` 自带缩放，不关掉会与适配器的 Ctrl+滚轮打架。
   ⚠ 这条在 WPF/Avalonia/MAUI/Razor 的 tree-view 里**没有对应物**（那几个没有这个属性），
   抄这段时容易一起漏。
5. **标尺避让由模板自己做**：`Canvas.RenderTransform` 里的 `TranslateTransform` 绑
   `ElementName=PART_GridDecorator` 的 `RulerThickness`（`workflow-tree-view/TemplateClass.xaml:56-59`）。
6. **`Clip="{x:Null}"` 是一条链，不是一处**。link-view 的根（`workflow-link-view/TemplateClass.xaml:5`）
   与 node-view 的三处（`:12` 根、`:18` 设计尺寸卡片、`:39` 插槽宿主 Grid）都要写。
   理由（这一家的保留式几何会被元素盒裁掉）在 `memory/modules/WorkflowSystem/adapters/winui.md` §二·L2
   与 §三·D1，本文只指路。

---

## 三、这一家模板特有的坑

### P1 · link-view 的 `Path` 是**代码构造的保留式几何**，不是 `OnRender`

`TemplateClass.xaml.cs:11-12` 有一句别名注释：`using System.IO.Path` 的隐式 using 会撞名，
所以 `using Path = Microsoft.UI.Xaml.Shapes.Path;`。几何是三个字段
`_path` / `_pathGeometry` / `_pathFigure`（`:26-28`），在构造函数里 new 出来并 `container.Children.Add(_path)`（`:57-58`），
DP 变更回调调 `UpdatePath()`（`:97`），`UpdatePath` 里 `EnsureGeometry()`（`:211-213`）后写 `_path.Data`（`:265`）。
`:54-55` 的注释点明了动机：**WPF 的连线是 `OnRender` 画的、永不被裁，这里用整条 `Clip = null` 链复现那个"不被裁"的效果**。

⇒ 改这一家的连线时，**不要去找 `OnRender`**；要改的是三个字段的生命周期与 `UpdatePath` 的触发点。

### P2 · grid-decorator 是**池化 + 结构值守卫**，不是每帧重画

八个静态画刷在 `workflow-grid-decorator/TemplateClass.cs:32-39` 各从一个 `TemplateXxx` 占位符解析；
`Loaded` / `SizeChanged` 都挂 `RebuildSurface()`（`:164-165`），`LayoutUpdated` 挂 `ApplyChildLayout()`（`:166`）。
`ApplyChildLayout` 有一个**结构值守卫**：`_lastWidth` 初值 `-1`（`:63`），宽度/高度/标尺厚度三个值
**都**在 `0.5` 以内（`:240-242`）才早退，否则重排并把三个值写回（`:247` 起）。

⇒ **改这里的第 2 步是"要不要多一个守卫量"**：新增一个会影响子元素布局的输入（比如新的边距常量）时，
必须把它一起纳入那个 `0.5` 判断，否则要么每次 `LayoutUpdated` 都重排（卡），
要么改了值却不重排（静默不更新）。

### P3 · `slotBorderColor` 在这一家的 slot-view 里是空转参数

`workflow-slot-view/.template.config/template.json` 声明了 `slotBorderColor` 但**没有 `replaces`**。
slot-view 的 `Path` 只有一个 `Fill`（`workflow-slot-view/TemplateClass.xaml:18` 的 `RootPath`），
`UpdateForeground()` 只写 `RootPath.Fill`（`.xaml.cs:35-41`）。⇒ 传这个参数**什么也不会发生**，
而且不报错。全平台共有 24 个这样的参数，见 `../architecture.md` §7.1。

### P4 · 两处 `ParseColor` 是**逐字重复**的

`workflow-slot-view/TemplateClass.xaml.cs:44-59` 与 `workflow-minimap-overlay/TemplateClass.cs:26-41`
各有一份完全相同的 `ParseColor(string hex)`（处理 8 位 `#AARRGGBB` 与 6 位 `#RRGGBB` 两种长度）。
⇒ 改一处的解析规则（比如支持 3 位缩写）时，另一处不会跟着变。同一份 `ParseColor` 在
Razor 模板里有**六份**逐字重复（`../adapters/razor.md` §二·7），所以这条不是 WinUI 独有；
WinUI 独有的是"只重复两份、且两份之间逐字相同"。

---

## 四、指路（这些结论已经在别处写好，本文不抄）

| 结论 | 在哪 |
|---|---|
| 画布平移用合成 `Translation`、模板不做变换绑定、以及该文件里那句与代码不符的注释 | `memory/modules/WorkflowSystem/adapters/winui.md` §三·D1 与 §四·P1 |
| 没有公共 `OnRender` ⇒ 保留式元素池化；`ScrollViewer` 缩放/指针事件都走冒泡 `handledEventsToo` | 同上 §二·L1/L2 |
| `LayoutUpdated` 逐元素、`SizeChanged` 并行挂、两者都同步调 `Sync` | 同上 §二·L3 |
| 名称解析只有 `FindName` 一条路、解析失败静默；改模板布局后要 `Refresh(UserControl)` | 同上 §二·L4 与 §四·P5 |
| 缩放的 `DispatcherQueuePriority` 档位表与"落地前重校验" | 同上 §二·L6（模板不实现，只在模板里被消费） |
| 滚轮方向与缩放提交顺序 | `memory/modules/WorkflowSystem/extension.md` §3.9 |
| 人面向的"怎么用这套模板" | `skills/veloxdev-create-workflow/references/gui/winui.md` 的 `## Item templates` |
