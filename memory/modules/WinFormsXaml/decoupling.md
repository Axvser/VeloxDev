# 控件解耦

## 机制

- 默认外观：`Src/Easel/Themes/Defaults/<Family>Defaults.xaml` 按族提供隐式 `Style`，由 `DefaultStyles` 合并；`VisualElement.ResolveImplicitStyle` 在元素/祖先 `Styles` 与 `Surface.Styles` 之后回退到 `Theme.FindDefaultStyle`。默认样式按**确切类型**匹配，派生类型要自己的样式或 `BasedOn`。
- 模板取色：`VisualHost.RefreshThemeResources` 把 `Theme.CreateResourceDictionary(Theme.EffectiveMode)` 合并进宿主 `Resources`，模板用 `DynamicResource` 取主题键；主题变化时重建。
- `TemplatedControl.DefaultTemplate` 是代码兜底，顺序 `Template` > 样式模板 > `DefaultTemplate`；`TemplatedControl.IsFocusVisible` 由 `VisualElement.OnFocusVisibleChanged` 同步。
- 部件：`GetTemplateChild<T>` + `PART_*`；装饰部件设 `IsHitTestVisible=False` 只跳过自己的内容命中，子元素仍可命中。
- **模板触发器**：Setter 必须放在 `<Trigger.Setters>`（或 `<Setters>`）下；触发器写 `Template` 层，低于本地值，部件不能同时写会被触发器驱动的本地 `Opacity`/`Visibility`。成对 True/False 触发器或 `Style.Setters` 提供初始态。
- **嵌套模板**：`SetTemplatedParentRecursive` 只在 `TemplatedParent` 为空或相同时赋值，但继续递归；内层控件的模板部件保持自己的源，嵌在部件内容区里的外层部件仍拿到外层控件。简单的“遇到不同父级就返回”会漏掉 ScrollViewer 内容区里的部件（RichTextBox 不画）。
- `TemplateBinding` 读 `Color` 型 CLR 属性时，未设置的 DP 给出 `Color.Empty`；绑定层按无值清回目标默认。`DynamicResource` 不能用于 `VisualSetter.Value`，也不能写在 `TextBlock.Foreground` 这类 Color 访问器上；用 `TemplateBinding`、模板部件或 `Path.Fill`。
- 数据列表的项外观在容器：`ListBox.Items`/`VirtualizingListBox.Items` 暴露 `ListBoxItem` 容器；取 ItemTemplate 根用 `((ListBoxItem)Items[i]).Content`。

## 性能边界

- Range 控件的值变化只重摆 `PART_Fill`/`PART_Thumb` 并 `InvalidateVisual`，不能再 `InvalidateArrange` 整页；回归 `LayoutRegressionTests`。
- ComboBox 在 Arrange 时把弹层子元素的 `MaxWidth` 收口到自身宽度；ListBox/ItemsControl 默认外壳会想要整块可用宽度。
- GridSplitter 尺寸没有实际变化时不触发布局。
- 主题切换原地修改共享画笔颜色，不清空/重建主题字典；`Theme.CreateResourceDictionary` 必须克隆画笔（共享会污染静态主题）。`ResourceBinding` 只订阅元素/链根/表面/命中来源的字典，但父链的 `ParentChanged` 仍要逐个监听（模板部件随后才挂到弹层/列表）。
- `ColorBrush` 链接源 `SolidColorBrush`：StaticResource 的 Foreground 被 DP 强转后仍跟随主题画笔变色；主题排版样式不要再把 `Text/Muted` 写进 setter，Caption 用透明度表达 muted。
- Gallery 不重建 shell：每个视图克隆并跟踪主题字典，`GalleryTheme.RefreshPalette` 原地改色；inspector 的标签直接持有 shell 主题源画笔，不重建；只有构造期捕获 `Theme.Current` 的 `DynamicResourceCard`/`CompositionLabView` preview 重建；`GridSplitter` 按 `Theme.Current` 自绘底色。Dark→Light 实测 **0.8ms**（副屏，对比模板化前整壳重建 13.4ms），High contrast 也验证通过；圆角/控件高度等度量由 `SetThemeMetric` 跟随主题，无需重建。
- 基准 `Src/Test/Performance/Easel.Benchmarks`（严格 runner，JSON/budget，页面含一个可视 Slider）：1200×800、1× DPI、200 行 ListBox + 20 Button + 8 TextBox + 6 Slider/5 ProgressBar + ComboBox。模板化前基线 → 当前：slider 2.391→2.153ms、splitter 3.412→2.779ms、full-frame 2.906→2.485ms、theme-toggle 2.983→2.335ms，全部更优；build-page 6.650→16.768ms、theme-rebuild 7.724→13.616ms。
- build-page/theme-rebuild 的剩余差距是结构性的：模板节点数远多于基线。ListBox 保留 200 个 index-stable 容器，只 attach 视口窗口与 ~20 行模板；无列表同页 build ~10.4ms，是该设计的实测地板。
- 快路径现状：全帧按 `Graphics.IsVisible` 剔除、恒等变换子树用托管偏移做脏区剔除、纯布局节点跳过 GDI+ 状态栈、主题画笔原地改色 + 直接整帧绘制、Range 默认模板单 chrome 一次绘制；行实例复用、BindingPath 缓存、DP 单层内联、Grid 轨道复用、模板单遍遍历、惰性行模板 + 视口窗口。

### 真机交互链路（副屏 SendInput 探针）

- **继承属性只能通知直接子级**：`NotifyDataContextDescendants` 里 `RaisePropertyChanged` 已让子级 `OnPropertyChanged` 继续下传；再显式递归会变成 2^n。`NotifyInheritedDescendants` 同样只遍历直接子级。回归 `InheritedPropertyPropagationTests`。
- **固定宽、不换行的 TextBlock 改文本只重画**：`ShouldInvalidateMeasure` 对这类 Text 变更返回 false；缺它时 Slider 的 readout 绑定每帧触发整窗 measure。副屏滑块帧间隔中位 32→16ms。
- **`IsLayoutBoundary`**：边界内部尺寸变化只重排边界子树；边界自身尺寸必须由父级固定。Gallery 的 `PreviewScroll` 已启用。
- **`TreeView.Reveal` 只在展开状态/可视行变化时 Rebuild**；Gallery 选中期间抑制 `SelectedItem` 回传，避免每页构建两次。进入页面由 66–497ms 降到多数 1–20ms。
- **Shell splitter**：`ScrollContentPresenter` 的无界探针 100000 会让 TreeView 模板根在无界/有界之间来回测；`Grid.ArrangeOverride` 在尺寸差时重测子项；`DockPanel` measure/arrange 用不同剩余空间。修完后每帧 layout 23→~1ms、MeasureOverride 26k→~40；副屏 sash 帧间隔 46→16ms（右）/31ms（左，探针 sleep 占 ~15.6ms）。布局期间 ScrollViewer 滚动条失效不排第二帧。
- **Gallery 昂贵页面**：旧卡销毁延后一帧、inspector 行按 editor kind 复用、WPF 媒体栈预热、视频首帧后置开播；scenario.video 290→9ms、player 88→21ms、defaults.controls 5019→23ms；全部卡片 warm ≤19ms。
- **密码插入符**：单行密码按固定 `PasswordCellWidth` 逐点绘制掩码，插入符/选区/命中都用同一 cell；不要用整串 `MeasureString`/`MeasureInkRight`，150% DPI 下会把 trailing bearing 算进前缀而差一格。
- **RadioButton 再次点击取消**：`ActivateFromInput` 在已选中时写 `false`；同组互斥仍只在写 `true` 时清理。回归 `RadioButton_ActivationTogglesOffOnASecondClick`。
- **播放器封面**：平放 `Border`，流动 `LinearGradientBrush` 只挂在 `BorderBrush`（不是内容背景）；33ms tick 直接改 `GradientStops[i].Offset` 与起止点再 `InvalidateVisual`；左栏与封面同宽 260。
- **快照输入代理**：`NativeSnapshotProxy` 必须 `WS_EX_NOACTIVATE` + `WM_MOUSEACTIVATE` 返回 `MA_NOACTIVATE`（缺它时代理激活会让宿主标题栏/边缘变白）；代理底色用原生控件 `BackColor`，不要用洋红，`PrintWindow` 会把它带进快照。

### 构建与依赖

- Gallery 的 MVVM 在 `Easel/Examples/XamlGallery/Demo/Mvvm/DemoMvvm.cs`，不引用 `VeloxDev.Core`/`VeloxDev.Core.Generator` 或任何 VeloxDev 包；Demo 只保留对 Easel 源码与生成器项目的内部引用。
- Demo 的生成器 `ProjectReference` 只在 Debug 生效；Release 通过 Easel 传递的 `WinForms.Easel.Generator` 包解析，缺它 XAML 类会生成两份 `InitializeComponent`。
- 非测试项目（Easel 四 TFM、Generator、Templates、Demo）Debug/Release 均以 `-warnaserror` 构建通过；`Directory.Build.props` 的 `NuGetAudit=false` 仅用于离线环境，不改变包解析。
- **GC 分析**：`Src/Test/GcAnalysis` 独立控制台（不引用 Demo/单元测试/基准）；场景 `host-lifecycle`/`page-build`/`template-lifecycle`/`binding-lifecycle`/`theme-switch`/`list-window`。测量轮禁 tracker，泄漏轮用 WeakReference，强制全回收后存活即泄漏；输出 allocation median/min/max、Gen0/1/2、pause、retained 与 baseline 比较。命令与退出码见 README。
- **ListBox 内容延迟**：每源项仍有一个 index-stable `ListBoxItem`，但 `Content` 由工厂延迟到行进入视口/应用模板/读取 Content；ComboBox 弹出列表关闭该延迟（闭合测量与提交依赖内容）。GC 分析 list-window 1.6→1.2MB、page-build 1.4→1.3MB。

## 已接管的控件

`Button`/`RepeatButton`/`ToggleButton`/`CheckBox`/`RadioButton`、`TextBox`/`RichTextBox`、`ComboBox`/`ComboBoxItem`、`Expander`/`GroupBox`/`TabControl`/`TabItem`、`Slider`/`ProgressBar`、`ItemsControl`/`ListBox`/`ListBoxItem`/`VirtualizingListBox`/`TreeView`/`TreeViewItem`、`DataGrid`/DataGrid 键控单元格、`Calendar`/`CalendarItem`/`DatePicker`、`VisualMenu`/`VisualContextMenu`/`VisualMenuItemContainer`、`ScrollBar`/`HeaderedContentControl`/`DropZone`。自绘路径保留为无模板兜底；特殊用法（`VisualTitleBar` caption、`VisualDialog` 按钮）用显式空样式退出默认。

## 剩余边界

- `DataGrid`：行/表头仍由网格在几何部件内自绘；按键控单元格模板已接管 Badge/Check/Progress，文本列保留自绘。
- `VisualContextMenu` 仍保留自己的模板引擎，尚未统一为 `TemplatedControl`。
- `CalendarItem` 的 42 个日格是代码创建的 `CalendarDayCell`，不是逐格模板；导航箭头是装饰零件。`DatePicker.IsDropDownOpen` 仍是普通 CLR 属性，模板触发器读不到。
- `ComboBox` 可编辑态仍把占位文字同步成编辑器文本；`ScrollBar` 默认样式故意不声明模板拇指/重复按钮（命中与保留角行为在引擎里）；`DropZoneChrome` 用代码画虚线。
- `TabControl` 禁用态不下推给 TabItem；`DynamicResource` 仍不能写进 Setter。
