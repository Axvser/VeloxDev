# 控件解耦

## 已落地的机制

- 默认外观：`Easel/Src/Easel/Themes/DefaultStyles.xaml` 里按类型放隐式 `Style`（生成器把无 key 的 Style 存成 `dictionary[typeof(T)]`）；`VisualElement.ResolveImplicitStyle` 在元素/祖先 `Styles` 与 `Surface.Styles` 之后回退到 `Theme.FindDefaultStyle`。
- 模板取色：`VisualHost.RefreshThemeResources` 把 `Theme.CreateResourceDictionary(Theme.EffectiveMode)` 合并进宿主的 `Resources`，默认模板用 `DynamicResource` 取主题键；主题变化时重建这本字典。
- `TemplatedControl.DefaultTemplate` 是代码兜底，生效顺序 `Template` > 样式模板 > `DefaultTemplate`。
- `TemplatedControl.IsFocusVisible` 由 `VisualElement.OnFocusVisibleChanged` 同步，供模板触发器使用；指针态已有 `ButtonBase.IsPointerOver` / `IsPressed`，选中态有 `IsChecked`。
- 部件：`GetTemplateChild<T>` + `PART_*`。模板内容带 `IsTemplateContent` 标记；部件设 `IsHitTestVisible=False` 后只跳过自己的内容命中，子元素仍可命中（内容区照常接收输入）。不设的装饰部件会把命中从控件手里抢走。
- 默认样式按**确切类型**匹配（WPF 语义）；派生类型要自己的样式或 `BasedOn`。`Theme.DefaultStyles` 里目前有 `Button` 的默认模板；`RepeatButton`/`ToggleButton`/`CheckBox`/`RadioButton` 仍是自绘。
- `TemplateBinding` 读 `Color` 型 CLR 属性时，未设置的 DP 会给出 `Color.Empty`；绑定层把 `Color.Empty` 当作无值并清回目标默认，缺它时文字会用透明色画（按钮文字消失）。

## 分类

- 已有强部件契约：`ComboBox`（`PART_Popup`/`PART_ItemsPresenter`/`PART_EditableTextBox`/`PART_ToggleButton`）、`TextBox`（`PART_ContentHost`）、`RichTextBox`（`PART_ContentHost`/`PART_ScrollViewer`）、`Calendar`（`PART_CalendarItem`）、`DatePicker`、`ScrollViewer`（内置 `DefaultChrome`）、`TreeViewItem`、`VisualTitleBar`、`VisualDialog`、`ScrollBar`（自有模板引擎）、`ContentControl`。
- 需要外壳模板 + 部件：`DataGrid`（没有 `PART_*`，`DataGridColumnKind` 把 Badge/Check/Progress 写死）、`Expander`、`TabControl`/`TabItem`、`Slider`、`GroupBox`、`HeaderedContentControl`、`DropZone`、`CalendarItem`、`VisualMenu`/`VisualContextMenu`、`Button` 系。
- 需要容器/项样式：`ItemsControl`/`ListBox`/`VirtualList`/`TreeView`、`DataGrid` 的行/单元格/表头、`ComboBoxItem`。
- 原语无需模板：`Border`、`TextBlock`、`Image`、面板、`ContentPresenter`、Shapes。

## 迁移顺序与约束

1. `Button` 系（`Button`/`RepeatButton`/`ToggleButton`/`CheckBox`/`RadioButton`）：先给 `VisualTitleBar` 的 caption 按钮、`VisualDialog` 的 primary/secondary/cancel 等特殊用法显式样式或模板，再启用通用默认模板；装饰部件设 `IsHitTestVisible=False`。通用 Button 模板会同时作用于这些特例，标题栏按钮的透明背景与命中测试会被覆盖。
2. `TextBox`/`RichTextBox`/`ComboBox` 默认模板（部件契约已存在）。
3. `Expander`/`TabControl`/`GroupBox`/`Slider`/`ProgressBar`（先改为 `TemplatedControl` 或提供模板部件）。
4. `ItemsControl`/`ListBox`/`VirtualList`/`TreeView` 的容器样式；`DataGrid` 最后：先公开行/单元格/表头样式与 presenter，再替换 `DataGridColumnKind`，最后才是整表模板。
