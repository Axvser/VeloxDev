# Templates — Avalonia

> **本文只写模板侧独有的东西**：条目产出什么形状、哪些接线必须手写、这一家模板特有的坑。
> 契约（七角色、附着属性、注册位置）在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；
> **适配器侧**的硬限制与坑在 `memory/modules/WorkflowSystem/adapters/avalonia.md`，本文只指路不抄；
> 包结构与跨平台族划分在 `../architecture.md` 与 `../extension.md`。
> **路径写法**：下文裸文件名都相对 `Src/Templates/VeloxDev.Avalonia.Templates/working/content/`；引用别处一律写全路径。

代码目录：`Src/Templates/VeloxDev.Avalonia.Templates/working/content/workflow-<角色>/`。

---

## 一、这一家的条目产出什么形状

四个条目出 AXAML（`link-view` / `node-view` / `slot-view` / `tree-view`），三个不出
（`grid-decorator` / `minimap-overlay` / `template-selector`）。

| 条目 | 形状 | 关键锚点 |
|---|---|---|
| link-view | **空壳**：`TemplateClass.axaml` 全文只有一个空 `<Control>`（9 行），几何全在 code-behind | `workflow-link-view/TemplateClass.axaml:2-9` |
| node-view | `Viewbox` + 固定设计尺寸卡片，**`x:CompileBindings="False"`** | `workflow-node-view/TemplateClass.axaml:15,21-22` |
| slot-view | `Viewbox` + 一个 `Path`，**没有状态属性** | `workflow-slot-view/TemplateClass.axaml:11-14` |
| tree-view | 外壳 + 两个 **keyed** `DataTemplate`（`NodeTemplate` / `LinkTemplate`）+ 一个声明出来的 `TemplateSelector` 资源 | `workflow-tree-view/TemplateClass.axaml:27,34,44` |
| grid-decorator | `sealed class : Panel, IWorkflowGridDecorator` + 两个私有嵌套 `Control` 覆写 `public override void Render(DrawingContext)` | `workflow-grid-decorator/TemplateClass.cs:21,134,177` |
| minimap-overlay | **薄壳**：继承适配器的 `WorkflowMinimapOverlay`，只设四个画刷（25 行） | `workflow-minimap-overlay/TemplateClass.cs:13-24` |
| template-selector | `IDataTemplate` 子类（**不是** `DataTemplateSelector`） | `workflow-template-selector/TemplateClass.cs:13-32` |

画刷解析用 `Color.Parse(hex)` 包成 `ImmutableSolidColorBrush`（`workflow-minimap-overlay/TemplateClass.cs:23-24`）。

---

## 二、模板里必须手写、委派不掉的接线

1. **`x:DataType="vm:TreeViewModel"` 是故意指向用户类型的占位**（`workflow-tree-view/TemplateClass.axaml:15`，
   `xmlns:vm` 在 `:9` 是 `using:TemplateNamespace`）。文件头 `:1-4` 的注释把后果写明了：
   `until you do, the build fails HERE on vm:TreeViewModel`。⇒ **这条目生成出来就是编译不过的**，
   见 `../architecture.md` §7.3 —— 这是设计，不是缺陷。
2. **`RenderTransform` 用 `$parent[...]` 而不是 `RelativeSource`**：节点模板
   `workflow-tree-view/TemplateClass.axaml:30`、连线模板 `:40` 都写
   `{Binding $parent[local:TemplateClass].(behaviors:WorkflowCanvasTransformBehavior.Transform)}`。
   与 WPF 的 `RelativeSource AncestorType` 是同一件事的两种语法。
3. **连线视图的盒子由 tree-view 给**：`Width/Height` 绑 `$parent[Canvas].Bounds.Width/Height`，`ZIndex="-1"`
   （`workflow-tree-view/TemplateClass.axaml:37,39`）。⇒ link-view 自己的 AXAML 是空的（§一），
   别去找它的 XAML。
4. **`Canvas` 的两条几何/虚拟化接线**：`Width/Height` 绑 `Layout.ActualSize`
   （`workflow-tree-view/TemplateClass.axaml:64`）、`behaviors:ViewPool.ItemsSource="{Binding Helper.VisibleItems}"`（`:65`）。
5. **标尺避让由模板自己做**：`Canvas.RenderTransform` 里的 `TranslateTransform` 绑
   `ElementName=PART_GridDecorator` 的 `RulerThickness`（`workflow-tree-view/TemplateClass.axaml:67-68`）。
6. **五个 `PART_*` 名字一次挂全，外加 `IsEnabled` / `ZoomEnabled` 两个开关**
   （`workflow-tree-view/TemplateClass.axaml:16-22`）。这一段与 WPF/WinUI/MAUI 的 tree-view
   **逐条对应**（各自 `:8-15` / `:8-15` / `:9-16`），Razor 的组件写法同名同序
   （`workflow-tree-view/TemplateClass.razor:10-17`）。⇒ 这一段的六个名字是**适配器按名字解析**的，
   改名就是静默失效。

---

## 三、这一家模板特有的坑

### P1 · 插槽**永远不会变色** —— 七家里唯一

`SlotState`（连线手势下变紫/番茄红/青柠）在这一家的模板里**一处都没有**（`grep SlotState
VeloxDev.Avalonia.Templates/working/content/` 零命中）。slot-view 只把描边颜色绑到
`$parent[UserControl].Foreground`：

```xml
<Path Fill="{Binding $parent[UserControl].Foreground}" Data="TemplateSlotPath" />
```

`workflow-slot-view/TemplateClass.axaml:12` —— 而 `workflow-slot-view/TemplateClass.axaml.cs`
是一个**只有 `InitializeComponent()` 的空 partial 类**（12 行），没有任何地方会写 `Foreground`。
⇒ 插槽在 Avalonia 上不会随连线状态变色，**且不报错**。对照：WPF/WinUI/MAUI 在 slot-view 里注册了
`SlotState` DP/BindableProperty 并在 node-view 里绑上；Jalium/Razor/WinForms 在 C# 里算。
全表在 `../architecture.md` §7.4。

### P2 · `x:CompileBindings="False"` 只在 node-view 里，而且带一句 TODO

`workflow-node-view/TemplateClass.axaml:15-16` 是
`x:CompileBindings="False"` 加注释 `<!-- Replace x:CompileBindings=False with a concrete x:DataType after defining your ViewModel. -->`。
**tree-view 没有这一句**（它靠项目级默认 + `x:DataType`）。⇒ 两个条目的编译绑定策略是**故意不同**的：
tree-view 用编译器帮你查错，node-view 先关掉让你填 `x:DataType`。改其中一个之前先看另一个，别对齐。

### P3 · 模板注册：**keyed 资源 + 声明选择器**（2026-09-25 起）

tree-view 用两个 **keyed** `<DataTemplate x:Key="NodeTemplate" …>`（`workflow-tree-view/TemplateClass.axaml:27`）
与 `LinkTemplate`（`:34`），在 `:44-46` 声明 `local:TemplateSelector`，并在 `Canvas` 上设
`behaviors:ViewPool.TemplateSelector="{StaticResource WorkflowTemplateSelector}"`（`:65`）。
⇒ 生成出来的 `workflow-template-selector` 条目现在是**真的被接上**的
（不是「生成但不接线」的 selector）。selector 的 XML 注释仍告诉你要 assign 四个模板
（`workflow-template-selector/TemplateClass.cs:8-12`），tree-view 这一份只给了 Node/Link 两个。

⚠ 模板查找找不到时 `Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/ViewManager.cs` 会
**抛 `InvalidOperationException`**（与 WPF 的静默三级回退不同）；树的 item 与插槽不进池，
所以 selector 的 `SlotTemplate`/`TreeTemplate` 留空是正常的。

### P4 · `IsScrollInertiaEnabled="False"` 是必须的

`workflow-tree-view/TemplateClass.axaml:61`。触控/惯性滚动会与适配器的缩放枢轴补偿打架
（数学在 `Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Math/WorkflowSurfaceMath.cs`，七家共用）。
⇒ 从别家抄 tree-view 时别把这一条丢掉 —— 别家没有同名属性，容易一起漏。

---

## 四、指路（这些结论已经在别处写好，本文不抄）

| 结论 | 在哪 |
|---|---|
| 坐标换算选哪个 `SlotAnchorFrom*`、缩放的枢轴补偿 | `memory/modules/WorkflowSystem/adapters/avalonia.md` 与 `extension.md` §3.9 |
| 适配器侧"平台检测"等 Avalonia 独有文件 | 同上 |
| `ViewPool` / `ViewManager` 的模板查找与池化细节 | `Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/ViewManager.cs:166,172`（找不到模板时抛 `InvalidOperationException`） |
| 滚轮方向与缩放提交顺序（模板只消费，不改） | `memory/modules/WorkflowSystem/extension.md` §3.9 |
| 人面向的"怎么用这套模板" | `skills/veloxdev-create-workflow/references/gui/avalonia.md` 的 `## Item templates` |
