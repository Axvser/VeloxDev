# Templates — WPF

> **本文只写模板侧独有的东西**：条目产出什么形状、哪些接线必须手写、这一家模板特有的坑。
> 契约（七角色、附着属性、注册位置）在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；
> **适配器侧**的硬限制与坑在 `memory/modules/WorkflowSystem/adapters/wpf.md`，本文只指路不抄；
> 包结构与跨平台族划分在 `../architecture.md` 与 `../extension.md`。
> **路径写法**：下文裸文件名都相对 `Src/Templates/VeloxDev.WPF.Templates/working/content/`；引用别处一律写全路径。

代码目录：`Src/Templates/VeloxDev.WPF.Templates/working/content/workflow-<角色>/`。

---

## 一、这一家的条目产出什么形状

**四个条目出 XAML，三个不出**：`workflow-link-view`、`workflow-node-view`、`workflow-slot-view`、
`workflow-tree-view` 是 `TemplateClass.xaml` + `TemplateClass.xaml.cs` 一对；
`workflow-template-selector`（`DataTemplateSelector`）、`workflow-grid-decorator`、`workflow-minimap-overlay`
是单个 `TemplateClass.cs`。

| 条目 | 形状 | 关键锚点 |
|---|---|---|
| link-view | **空壳**：`TemplateClass.xaml` 全文只有一个空 `<UserControl>`（5 行），几何全在 code-behind | `workflow-link-view/TemplateClass.xaml:2-5`、`.xaml.cs:66` 的 `OnRender` |
| node-view | `Viewbox` + 固定设计尺寸卡片 | `workflow-node-view/TemplateClass.xaml:16-17` |
| slot-view | `Viewbox` + 一个 `Path` | `workflow-slot-view/TemplateClass.xaml:14-17` |
| tree-view | 五层嵌套外壳（`Border` → `GridDecorator` → `ScrollViewer` → `Canvas` + 平级 `MinimapOverlay`） | `workflow-tree-view/TemplateClass.xaml:41-69` |
| grid-decorator | `RulerThickness` 默认 **28**，`RulerBand => RulerThickness` | `workflow-grid-decorator/TemplateClass.cs:38` |
| minimap-overlay | **薄壳**：继承适配器的 `WorkflowMinimapOverlay`，构造器里只设四个画刷（24 行） | `workflow-minimap-overlay/TemplateClass.cs:12-23` |
| template-selector | `DataTemplateSelector` 子类 | `workflow-template-selector/TemplateClass.cs` |

画刷解析用 `ColorConverter.ConvertFromString(hex)`（`workflow-minimap-overlay/TemplateClass.cs:23`）——
与其他平台的解析 API 不同，见 `../architecture.md` 的 minimap 一族。

---

## 二、模板里必须手写、委派不掉的接线

这六条不在适配器里，也不是任何生成器补的：

1. **两个 `xmlns` 前缀指向同一个命名空间** —— `xmlns:local` 与 `xmlns:workflowViews` **都是**
   `clr-namespace:TemplateNamespace`（`workflow-tree-view/TemplateClass.xaml:5-6`）。
   这是"七个条目必须落进同一个命名空间"这条契约的**实现本身**：`local` 指自己，`workflowViews` 指兄弟。
2. **`RenderTransform` 绑定必须写在 `DataTemplate` 的根元素上**，两处：
   `workflow-tree-view/TemplateClass.xaml:22`（节点）与 `:34`（连线），
   写法都是 `{Binding RelativeSource={RelativeSource AncestorType={x:Type local:TemplateClass}}, Path=(behaviors:WorkflowCanvasTransformBehavior.Transform)}`。
   适配器只负责写这个附着属性的值，**不会**替你把它绑到视图上。
3. **`PART_Canvas` 的三件套**：`Background="Transparent"`（`:54`，命中测试要它）、
   `behaviors:ViewPool.ItemsSource="{Binding Helper.VisibleItems}"`（`:55`）、
   `behaviors:ViewPool.TemplateSelector="{StaticResource WorkflowTemplateSelector}"`（`:56`）。
   ⇒ **WPF 是 `ViewPool` 两个属性都设的那一类**；Avalonia 只设前一个，见 `adapters/avalonia.md`。
4. **标尺避让由模板自己做**：`Canvas.RenderTransform` 里的 `TranslateTransform` 把 X/Y 都绑到
   `ElementName=PART_GridDecorator` 的 `RulerThickness`（`workflow-tree-view/TemplateClass.xaml:58-59`）。
   适配器不参与 —— 它只把装饰器的 `RulerBand` 转发给虚拟化内缩。
5. **连线视图的盒子由 tree-view 给**：`LinkTemplate` 里 `Width/Height` 绑
   `ElementName=PART_Canvas` 的 `ActualWidth/ActualHeight`，并 `Panel.ZIndex="-1"`
   （`workflow-tree-view/TemplateClass.xaml:31-33`）。⇒ link-view 自己的 XAML 是空的（§一），
   它拿到了一个画布大小的盒子、再在其中 `OnRender`。**改 link-view 时不要去找它的 XAML**。
6. **五个 `PART_*` 名字一次挂全**：`ScrollViewerName` / `CanvasName` / `GridDecoratorName` /
   `PointerPressSourceName` / `MinimapOverlayName`（`workflow-tree-view/TemplateClass.xaml:10-14`）。
   适配器靠 `FindName` 在**同一个 `UserControl` 的 XAML 作用域**里找它们 ——
   名字作用域的限制见 `memory/modules/WorkflowSystem/adapters/wpf.md` §2.2，此处不抄。

---

## 三、这一家模板特有的坑

### P1 · slot-view 把同一对命令挂了两条路 —— 先确认哪条在跑

模板的 slot-view **同时**做了两件事：

- `behaviors:WorkflowSlotConnectionBehavior.IsEnabled="True"`（`workflow-slot-view/TemplateClass.xaml:11`）
- 自己挂 `MouseLeftButtonUp="OnPointerReleased"` / `MouseLeftButtonDown="OnPointerPressed"`（`:12-13`），
  两个处理器分别在 code-behind 里执行 `context.SendConnectionCommand.Execute(null)`
  （`workflow-slot-view/TemplateClass.xaml.cs:48`）与 `context.ReceiveConnectionCommand.Execute(null)`（`:57`）

而适配器的附着行为走的是 `PreviewMouseLeftButtonDown` / `PreviewMouseLeftButtonUp` 并置 `e.Handled = true`
（`Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowSlotConnectionBehavior.cs:26-33,43-44`，同位置见
`memory/modules/WorkflowSystem/adapters/wpf.md` §二·L1 的对照）。

⇒ **同一对命令挂了两条路**。按 WPF 路由语义，Preview（隧道）那条置 `Handled` 之后，
冒泡的 `MouseLeftButtonDown/Up` 不会到达 XAML 注册的处理器（**此推断未实测**；
代码与注释里没有记录哪条是主路径）。改这里时先确认哪条真的在跑，
**不要以为这是两套需要同时维护的机制**，也不要顺手删掉看起来"没生效"的那条 ——
`IsEnabled="True"` 一旦被用户关掉，冒泡那条就是唯一的路。

### P2 · node-view 的三个 `ClipToBounds="False"` 不是装饰

`workflow-node-view/TemplateClass.xaml:11`（根）、`:17`（设计尺寸卡片）、`:38`（插槽宿主 Grid）**三处都要**。
卡片被 `Viewbox Stretch="Uniform"` 缩放到节点当前尺寸，任何一层剪裁都会在节点缩小时把插槽切掉。

### P3 · `Viewbox` 下的卡片必须钉在**设计尺寸**上

`:16-17` 是 `<Viewbox Stretch="Uniform">` 套 `<Grid Width="260" Height="180">`，文件里 `:13-15` 的注释
说明了理由：钉住 260×180 才能让 Viewbox 的缩放因子恰好等于节点缩放因子的倒数。
⇒ **不要**把这两个 `Width`/`Height` 改成 `{Binding Size.Width}` 之类的绑定，那会让 Viewbox 多算一次缩放。

### P4 · `d:DesignWidth/DesignHeight="20"` 只是设计时提示

`workflow-slot-view/TemplateClass.xaml:9` 的 `d:DesignHeight="20" d:DesignWidth="20"` 与
`mc:Ignorable="d"`（`:8`）配套 —— 它不影响运行时布局。真正决定插槽大小的是插槽宿主那一层的布局，
所以**不要**照着这两个数去调插槽尺寸。

---

## 四、指路（这些结论已经在别处写好，本文不抄）

| 结论 | 在哪 |
|---|---|
| `UserControl` 是行为链的边界、`FindName` 的作用域不跨 `DataTemplate` | `memory/modules/WorkflowSystem/adapters/wpf.md` §2.1/§2.2 |
| 三连 `UpdateLayout()` 才能读到 `ScrollViewer` 的真实 extent | 同上 §2.3 |
| `MouseUp` 必须 `handledEventsToo: true` 挂、`Mouse.Capture` + `LostMouseCapture` | 同上 §2.4 |
| `IsWorkflowLinkVisual` 靠类名白名单（改连线视图类名会静默改变手势归属） | 同上 §三·2 与坑 1 |
| `Move`/`Replace` 两种集合变更不处理 | 同上 §三·5 |
| 网格装饰器的 `RulerBand => RulerThickness` 在模板里（`workflow-grid-decorator/TemplateClass.cs:134`），以及"小地图的 `RulerThickness` DP 从未被读" | 同上 坑 2 |
| 节点拖拽需要真 `Background` / 真背景 `Border` | `memory/workflow-node-drag-hit-test.md` |
| 滚轮方向与缩放提交顺序（模板只消费，不改） | `memory/modules/WorkflowSystem/extension.md` §3.9 |
| 人面向的"怎么用这套模板" | `skills/veloxdev-create-workflow/references/gui/wpf.md` 的 `## Item templates` |
