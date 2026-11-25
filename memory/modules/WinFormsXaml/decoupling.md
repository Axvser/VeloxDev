# 控件解耦

## 机制

- 默认外观：`Src/Easel/Themes/Defaults/<Family>Defaults.xaml` 按族提供隐式 `Style`，由 `DefaultStyles` 合并；`VisualElement.ResolveImplicitStyle` 在元素/祖先 `Styles` 与 `Surface.Styles` 之后回退到 `Theme.FindDefaultStyle`。默认样式按**确切类型**匹配，派生类型要自己的样式或 `BasedOn`。
- 模板取色：`VisualHost.RefreshThemeResources` 把 `Theme.CreateResourceDictionary(Theme.EffectiveMode)` 合并进宿主 `Resources`，模板用 `DynamicResource` 取主题键；主题变化时重建。
- `TemplatedControl.DefaultTemplate` 是代码兜底，顺序 `Template` > 样式模板 > `DefaultTemplate`；`TemplatedControl.IsFocusVisible` 由 `VisualElement.OnFocusVisibleChanged` 同步。
- 部件：`GetTemplateChild<T>` + `PART_*`；装饰部件设 `IsHitTestVisible=False` 只跳过自己的内容命中，子元素仍可命中。
- **模板触发器**：Setter 必须放在 `<Trigger.Setters>`（或 `<Setters>`）下；触发器写 `Template` 层，低于本地值，部件不能同时写会被触发器驱动的本地 `Opacity`/`Visibility`。成对 True/False 触发器或 `Style.Setters` 提供初始态。
- **嵌套模板**：`SetTemplatedParentRecursive` 只在 `TemplatedParent` 为空或相同时赋值，但继续递归；内层控件的模板部件保持自己的源，嵌在部件内容区里的外层部件仍拿到外层控件。简单的“遇到不同父级就返回”会漏掉 ScrollViewer 内容区里的部件（RichTextBox 不画）。
- `TemplateBinding` 读 `Color` 型 CLR 属性时，未设置的 DP 给出 `Color.Empty`；绑定层按无值清回目标默认。`DynamicResource` 不能用于 `VisualSetter.Value`，也不能写在 `TextBlock.Foreground` 这类 Color 访问器上；用 `TemplateBinding`、模板部件或 `Path.Fill`。
- 数据列表的项外观在容器：`ListBox.Items`/`VirtualList.Items` 暴露 `ListBoxItem` 容器；取 ItemTemplate 根用 `((ListBoxItem)Items[i]).Content`。

## 性能边界

- Range 控件的值变化只重摆 `PART_Fill`/`PART_Thumb` 并 `InvalidateVisual`，不能再 `InvalidateArrange` 整页；回归 `LayoutRegressionTests`。
- ComboBox 在 Arrange 时把弹层子元素的 `MaxWidth` 收口到自身宽度；ListBox/ItemsControl 默认外壳会想要整块可用宽度。
- GridSplitter 尺寸没有实际变化时不触发布局。
- 主题切换原地修改共享画笔颜色，不清空/重建主题字典；`Theme.CreateResourceDictionary` 必须克隆画笔，缺它时第一个宿主改色会污染静态主题。`ResourceBinding` 只订阅元素/链根/表面/命中来源的字典，但父链的 `ParentChanged` 仍要逐个监听（模板部件随后才挂到弹层/列表）。
- 基准 `Src/Test/Performance/Easel.Benchmarks`：1200×800、1× DPI、200 行 ListBox + 20 Button + 8 TextBox + 5 Slider/ProgressBar + ComboBox。模板化前 `886ab07` → 当前：slider 1.86→1.46ms、splitter 1.82→1.69ms、palette toggle 3.92→7.3ms、palette + 整页重建 14→145ms。交互已持平，整页重建仍受模板/绑定实例化支配；下一步是列表虚拟化或惰性模板实例化。
- Gallery 的快速主题路径（不重建 shell）需要样式里的静态画笔与构造期主题值也能刷新；直接改画笔会让标题/分割器等保持旧色，因此仍走重建。

## 已接管的控件

`Button`/`RepeatButton`/`ToggleButton`/`CheckBox`/`RadioButton`、`TextBox`/`RichTextBox`、`ComboBox`/`ComboBoxItem`、`Expander`/`GroupBox`/`TabControl`/`TabItem`、`Slider`/`ProgressBar`、`ItemsControl`/`ListBox`/`ListBoxItem`/`VirtualList`/`TreeView`/`TreeViewItem`、`DataGrid`/DataGrid 键控单元格、`Calendar`/`CalendarItem`/`DatePicker`、`VisualMenu`/`VisualContextMenu`/`VisualMenuItemContainer`、`ScrollBar`/`HeaderedContentControl`/`DropZone`。自绘路径保留为无模板兜底；特殊用法（`VisualTitleBar` caption、`VisualDialog` 按钮）用显式空样式退出默认。

## 剩余边界

- `DataGrid`：行/表头仍由网格在几何部件内自绘；按键控单元格模板已接管 Badge/Check/Progress，文本列保留自绘。
- `VisualContextMenu` 仍保留自己的模板引擎，尚未统一为 `TemplatedControl`。
- `CalendarItem` 的 42 个日格是代码创建的 `CalendarDayCell`，不是逐格模板；导航箭头是装饰零件。`DatePicker.IsDropDownOpen` 仍是普通 CLR 属性，模板触发器读不到。
- `ComboBox` 可编辑态仍把占位文字同步成编辑器文本；`ScrollBar` 默认样式故意不声明模板拇指/重复按钮（命中与保留角行为在引擎里）；`DropZoneChrome` 用代码画虚线。
- `TabControl` 禁用态不下推给 TabItem；`DynamicResource` 仍不能写进 Setter。
