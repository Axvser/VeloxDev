# 控件解耦

## 已落地的机制

- 默认外观：`Src/Easel/Themes/Defaults/<Family>Defaults.xaml` 按族提供隐式 `Style`，由 `DefaultStyles` 合并；`VisualElement.ResolveImplicitStyle` 在元素/祖先 `Styles` 与 `Surface.Styles` 之后回退到 `Theme.FindDefaultStyle`。默认样式按**确切类型**匹配，派生类型要自己的样式或 `BasedOn`。
- 模板取色：`VisualHost.RefreshThemeResources` 把 `Theme.CreateResourceDictionary(Theme.EffectiveMode)` 合并进宿主 `Resources`，模板用 `DynamicResource` 取主题键；主题变化时重建。
- `TemplatedControl.DefaultTemplate` 是代码兜底，顺序 `Template` > 样式模板 > `DefaultTemplate`；`TemplatedControl.IsFocusVisible` 由 `VisualElement.OnFocusVisibleChanged` 同步。
- 部件：`GetTemplateChild<T>` + `PART_*`；装饰部件设 `IsHitTestVisible=False` 只跳过自己的内容命中，子元素仍可命中。
- **模板触发器**：Setter 必须放在 `<Trigger.Setters>`（或 `<Setters>`）下；触发器写 `Template` 层，低于本地值，部件不能同时写会被触发器驱动的本地 `Opacity`/`Visibility`。成对 True/False 触发器或 `Style.Setters` 提供初始态。
- `TemplateBinding` 读 `Color` 型 CLR 属性时，未设置的 DP 给出 `Color.Empty`；绑定层按无值清回目标默认。`DynamicResource` 不能用于 `VisualSetter.Value`（生成器拒绝），也不能写在 `TextBlock.Foreground` 这类 Color 访问器上；用 `TemplateBinding`、模板部件或 `Path.Fill`。
- 数据列表的项外观在容器：`ListBox.Items`/`VirtualList.Items` 暴露 `ListBoxItem` 容器，选中/悬停由容器模板负责；取 ItemTemplate 根用 `((ListBoxItem)Items[i]).Content`。

## 已由默认模板接管的控件

`Button`/`RepeatButton`/`ToggleButton`/`CheckBox`/`RadioButton`、`TextBox`/`RichTextBox`、`ComboBox`、`Expander`/`GroupBox`/`TabControl`/`TabItem`、`Slider`/`ProgressBar`、`ItemsControl`/`ListBox`/`ListBoxItem`/`VirtualList`/`TreeView`/`TreeViewItem`、`DataGrid`。自绘路径保留为无模板兜底；特殊用法（`VisualTitleBar` caption、`VisualDialog` 按钮）用显式空样式退出默认。

## 剩余的解耦缝

- `DataGrid`：`DataGridColumnKind` 的 Badge/Check/Progress 仍是 `DrawCell` 分支；行/表头/单元格部件目前是几何区域，未成为自绘的 presenter。
- `ComboBox`：可编辑时点击编辑器不会开关下拉；默认模板没有 `PART_ToggleButton`/`PART_Arrow`，箭头颜色没有跟随控件 `Foreground`。
- 容器：`TabControl` 还没有项容器样式。
- 未模板化：`Calendar`/`DatePicker`（`CalendarItem` 仍自绘月格）、`VisualMenu`/`VisualContextMenu`、`ScrollBar`（自有模板引擎）、`HeaderedContentControl`、`DropZone`、`ComboBoxItem`。
- 原语无需模板：`Border`、`TextBlock`、`Image`、面板、`ContentPresenter`、Shapes。
