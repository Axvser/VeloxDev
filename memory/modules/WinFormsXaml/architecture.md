# VeloxDev.WinForms.Xaml — 架构

> 包结构：`VeloxDev.WinForms`（适配器 + XAML 运行时）与 `VeloxDev.WinForms.Generator`（专属生成器），都是 **11.2.0**。
> 代码：`Src/Adapters/VeloxDev.WinForms/Xaml/`（运行时）、`Src/Generators/VeloxDev.WinForms.Generator/`（Roslyn 增量生成器）、`Src/Adapters/VeloxDev.WinForms/WorkflowSystem/AttachedBehaviors/`（Surface 行为基类）；Demo 在 `Examples/Workflow/WinForms Trimmed` 与 `Examples/Workflow/WinForms`；测试在 `Src/Test/Unit/WinFormsXaml/VeloxDev.WinForms.Xaml.Test/`。
> 定位：WinForms 原生控件 + XAML 子集 + 依赖属性/绑定/Trigger/TransitionSystem 动画；不是 WPF 兼容层。
> 面向消费者的完整运行时手册在 `skills/veloxdev-create-workflow/references/practices/gui/winforms-xaml.md`；动画专用在 `skills/veloxdev-create-animation/references/practices/winforms-xaml.md`。

## 一、运行时

| 文件 | 类型 | 职责 |
|---|---|---|
| `DependencyProperty.cs` | `DependencyProperty` / `PropertyMetadata` | `Register` / `RegisterAttached`；本地值存 `XamlControl`，附加值存按控件弱键的侧表；变更/强制回调 |
| `Controls/Control.cs` | `Control : System.Windows.Forms.ScrollableControl, IInputElement` | 依赖属性宿主、继承 `DataContext`、绑定、Trigger 评估、资源查找、焦点/悬停/捕获；`Margin`/`Padding` 是 `Thickness` DP 并同步原生属性 |
| `Controls/ControlBases.cs` | `Panel` / `ScrollableControl` / `Canvas` / `UserControl` | 基础控件层：全部继承 `Control`，统一在 `VeloxDev.WinForms.Xaml.Controls` |
| `Controls/Border.cs` | `Border : Control` | 通用圆角背景/边框/单子内容；`Background` / `BorderBrush` / `BorderThickness`（`Thickness`）/ `CornerRadius` / `Backdrop` |
| `Controls/Thickness.cs` | `Thickness` | 布局值类型：`Left/Top/Right/Bottom`、`Parse`、`ToPadding` |
| `Controls/ScrollViewer.cs` | `ScrollViewer` / `ScrollBarVisibility` | 单内容 + 横/纵 `ScrollBar`；`ScrollOffset` / `Viewport*` / `Extent*` / `Scroll`；Auto/Visible/Hidden/Disabled；`HorizontalBarStyle` / `VerticalBarStyle` |
| `Controls/ScrollBar.cs` | `ScrollBar` / `ScrollBarOrientation` | 自绘条：`TrackBrush` / `ThumbBrush` / `ThumbHoverBrush` / `ThumbBorder*` / `ThumbCornerRadius` / `BarThickness` / `ThumbMinLength`；拖动/轨道翻页 |
| `TransitionSystem/Samplers/ThicknessSampler.cs` | `ThicknessSampler` | `Thickness` 的 `ISampler`，注册到 WinForms `Interpolator` |
| `Controls/LayoutPrimitives.cs` | 布局原语 | `Visibility` / `HorizontalAlignment` / `VerticalAlignment` / `DockPlacement` / `GridUnitType` / `Stretch`、`GridLength`、`RowDefinition` / `ColumnDefinition`、内部 `XamlLayout`（测量/摆放 XAML 与原生子控件） |
| `Controls/StackPanel.cs` / `DockPanel.cs` / `WrapPanel.cs` / `Grid.cs` / `ControlBases.cs` | 布局控件 | 垂直/水平堆叠、停靠、换行、行列网格；`Canvas` 的 `Left`/`Top`/`Right`/`Bottom` 与 `Grid.Row`/`Column`/`RowSpan`/`ColumnSpan` 都是 `RegisterAttached`，原生子控件也能被这些面板量/摆 |
| `Controls/ContentControl.cs` / `ItemsControl.cs` | 内容控件 | 单内容（子控件、`ContentTemplate` 或文本）；`ItemsSource` + `ItemTemplate` 逐项生成，订阅 `INotifyCollectionChanged` |
| `Controls/TextBlock.cs` / `TextBox.cs` | 文本控件 | `TextBlock`/`Label` 自绘文本；`TextBox`/`PasswordBox` 由内嵌原生 `TextBox` 承担编辑，DP 与原生双向同步 |
| `Controls/Button.cs` / `ToggleButton.cs` / `Selectors.cs` | 输入/选择控件 | 自绘 `Button`/`RepeatButton`/`ToggleButton`/`CheckBox`/`RadioButton`；`ListBox`/`ComboBox` 内嵌原生选择器，`ItemsSource`/`SelectedItem`/`SelectedIndex` 双向同步 |
| `Controls/ProgressBar.cs` / `Slider.cs` / `Image.cs` / `Separator.cs` | 范围/媒体 | 自绘进度条、滑块、位图与分隔线；值在 DP 层强制到范围 |
| `Controls/Shapes.cs` / `PathGeometry.cs` | 矢量形状 | 命名空间是 `VeloxDev.WinForms.Xaml.Controls.Shapes`；`Rectangle`/`Ellipse`/`Line`/`Path`，SVG 子集解析 |
| `WorkflowSurfaceBehavior.cs` | 附加行为 | 从任意根 `Control` 按 `PART_*` 名称解析 ScrollViewer/Canvas/GridDecorator/Minimap/PointerPressSource；承担 pan/zoom、输入路由、ViewPool、ruler/minimap、布局调度、树订阅与 teardown；`NodeViewType`/`LinkViewType`/`TemplateSelector`/`LinkMenuBuilder` |
| `WorkflowLinkLayer.cs` | 附加行为 | 在画布 `Paint` 里一趟画完所有可见连线并发布曲线；`LineColor` / `Thickness` / `Hovered*` / `Virtual*` / `FlowHead` / `FlowIntensity`；由表面行为自动挂到 `PART_Canvas` |
| `WorkflowNodeRowsBehavior.cs` | 附加行为 | 从 node.Slots 重建 `PART_DynamicOutputs` 的输入口与输出行；`RowsHostName` / `SlotViewType` / `SlotViewFactory` / 颜色与尺寸 |
| `WorkflowNodeViewBehavior.cs` | 附加行为 | 节点 attachment、拖动/槽布局、坐标宿主、卡片 backdrop 接线 |
| `WorkflowNodeTitleBehavior.cs` | 附加行为 | 标题 Label 绑定，按 collapse 缩放标题条高度/字号/Padding |
| `WorkflowRulerOverlayForm.cs` | 内部 WinForms 宿主 | `IWorkflowGridDecorator.RulerBand` 驱动的分层标尺浮层 |
| `IInputElement.cs` | `IInputElement` | `Focusable` / `IsKeyboardFocused` / `IsPointerOver` / `Focus` / `CapturePointer` / `ReleasePointerCapture` |
| `Binding.cs` / `BindingExpression.cs` | `Binding` / `BindingMode` | OneWay / TwoWay / OneTime；`INotifyPropertyChanged`；原生控件走 `Control.DataBindings` |
| `Trigger.cs` | `Trigger` / `Setter` / `TriggerCollection` / `DataTrigger` / `MultiTrigger` / `Condition` | 单属性、数据路径、多条件 Trigger；进入应用 setter、离开按逆序恢复；移除/替换活动 trigger 会先走 exit |
| `Style.cs` | `Style` | `TargetType` / `Setters` / `Triggers` / `BasedOn` / `Template`；显式/隐式、原生 Control 样式；本地 > style trigger > style setter > 默认；集合变更自动重应用 |
| `ControlTemplate.cs` | `ControlTemplate` | 单 Control 根的内容模板；Style.Template 应用时加入目标 Controls，移除时清理 |
| `XamlTransition.cs` | `XamlTransition` / `DependencyTransitionProperty` | 用 `Transition<Control>` 把 DP 包装成 `ITransitionProperty`；走既有 WinForms TransitionSystem，run 会被登记，`Stop`/互斥 supersede 生效；`AnimateKeyFrames` 把关键帧展开成 `Then` 链 |
| `KeyFrameAnimation.cs` | `KeyFrameAnimation` / `KeyFrame` | `TriggerAction` 的关键帧形态：`Property` + `Duration` + `Ease` + `IList<KeyFrame>`；按 `Offset` 排序后每对相邻帧一段 `Transition`，首帧大于 0 时从当前值过去 |
| `InputEvents.cs` / `XamlInput.cs` | `PointerEventArgs` / `Pointer*EventArgs` / `KeyInputEventArgs` / `XamlInput` | 原生事件优先；显式指针/`KeyInputDown`/`KeyInputUp` 抽象仅用于跨平台输入；`XamlControl` 冒泡，原生控件经 `XamlInput.Add*` 桥接 |
| `XamlRuntime.cs` | 解析/转换 helpers | 颜色、枚举、Padding/Size/Point 等字面量 |
| `DataTemplate.cs` | `DataTemplate` | `Func<Control>` 工厂；由生成器填充 |

## 二、源生成器

- `[XamlProperty]`：partial 属性 → 静态 `DependencyProperty` + CLR 包装。
- `[XamlAttachedProperty]`：静态 partial `GetX`/`SetX` → `RegisterAttached` + 实现。
- 没有 `DefaultValue` 时生成器写 `default(T)`；非可空引用类型再被 getter 的 `!` 包成非空返回值，实际是 null。`[XamlProperty]` / `[XamlAttachedProperty]` 的非可空引用类型必须给 `DefaultValue` 或声明成可空，否则报 `VELOX_XAMLP005`。
- `*.xaml`（`AdditionalFiles`）→ `private void InitializeComponent()`（普通方法，不要求/不生成 partial 声明）：原生控件、派生控件、嵌套 `Controls`、`x:Name` 字段、属性/事件、`{Binding}`、`{StaticResource}`、`DataTemplate`。已有同名成员报 `VELOX_XAML015` 并跳过生成。
- 根方法里的事件订阅统一写在 `ResumeLayout(false)` 之前、所有子元素建好之后；否则初始化中途触发的事件会读到尚未赋值的 `x:Name` 字段。`DataTemplate` / `ControlTemplate` 工厂内的订阅仍原位。
- 附加属性属性值：`prefix:Owner.Property="..."`，支持字面量、`{Binding}`、`{StaticResource}`；`XamlControl.SetAttachedBinding` 为原生控件驱动 OneWay/OneTime；TwoWay 在原生附加值上不支持。
- 内联 Trigger：`<Owner.Triggers><Trigger Property="IsHot" Value="True"><Trigger.Setters><Setter Property="BorderWidth" Value="4"/></Trigger.Setters><Trigger.EnterAction><TransitionAction Property="Glow" To="1" Duration="0:0:0.3"/></Trigger.EnterAction></Trigger></Owner.Triggers>`；`Property` 相对控件类型解析，也支持 `Owner.Property`。
- 样式：`<Style x:Key="NodeStyle" TargetType="local:NodeCard">` + `Style.Setters` / `Style.Triggers` / `Style.Template` / `BasedOn`；显式 `Style="{StaticResource ...}"`，无 `x:Key` 的 `TargetType` 隐式样式；`TargetType` 支持 `{x:Type local:NodeCard}` 和原生 `Control`。
- 资源除了标量、`Style` / `DataTemplate` / `ControlTemplate`，还接受**空元素对象资源**：`<local:CustomTemplateSelector x:Key="..." />` 会 `new` 一个实例、按属性赋值后放进 `Resources`。非空元素的资源仍会报错。
- **自动脏标记**：`PropertyMetadata.AffectsRender` 默认 `true`。任何依赖属性在 XAML `Control` 上变化（本地值、绑定、Style、Trigger、Transition 都一样）都会在回调之后 `Invalidate()`；非视觉配置属性用 `new PropertyMetadata(...) { AffectsRender = false }` 退出。原生 WinForms 目标不受此影响（它们的重绘由原生属性自己负责）。回归：`DependencyPropertyTests` 的 invalidations 用例。
- 带 XAML 的条目一律 `TemplateClass.xaml` + `TemplateClass.xaml.cs`（与其余各家同形）；纯代码条目仍是 `.cs`。
- `DataTrigger`（`Binding`/`Path` + `Value`，支持 `Items[0].Name` / `Map["key"]` 简单索引器，并订阅路径上集合的 `INotifyCollectionChanged`；Add/Remove/Replace/Reset/Move 会重解析）、`MultiTrigger`（`Conditions` 全满足）、`ControlTemplate`（单根内容模板）均可用；原生样式只支持 Setter。
- 自定义 `TriggerAction`：XAML 中 `clr-namespace:` 的 action 类型，要求公共无参构造和公共可写实例属性；支持字面量、`{StaticResource}`、`{x:Null}`。
- `TransitionAction` 支持可选 `Ease`（Sine/Quad/Cubic/Quart/Quint/Expo/Circ/Back/Elastic/Bounce + In/Out/InOut；未知值报 `003` 并保留 action）。
- 显式 `Style="{StaticResource Key}"` 走 `ApplyStyleResource`：资源替换/移除/重新添加会重应用或回退；隐式样式走类型键。
- 样式内部集合（Trigger.Setter/Condition/Trigger 集合）变更会通知 Style 并重应用活动控件。
- 前缀是**严格**的：`v:` 是适配器类型（生成器按 `Controls` → `Controls.Shapes` → 运行时 `.Xaml` 的顺序找），`local:` 是模板/Demo 自己的类型，**不带前缀只解析原生 WinForms 表**。VeloxDev 类型漏写 `v:` 报 `VELOX_XAML019`，不会因为「`v` 与原生重名」而悄悄改选适配器；根元素也一样（`<local:TreeView>` 或 `<v:UserControl>`，不把 `<TreeView>` 当本地类型）。
- XAML 事件：WinForms 原生事件优先（`MouseDown` / `Click` / `KeyDown` 等）；显式指针抽象是 `PointerPressed` / `PointerMoved` / `PointerReleased` / `PointerEntered` / `PointerExited` / `PointerWheelChanged`，显式键盘抽象是 `KeyInputDown` / `KeyInputUp`；原生控件通过 `XamlInput.Add*` 桥接。
- Surface：**没有 `WorkflowTreeView` 基类，也没有 `InitializeSurface`**。Tree 模板是 `v:UserControl`，XAML 自己声明 `PART_Surface` / `PART_GridDecorator` / `PART_ScrollViewer` / `PART_Canvas` / `PART_MinimapOverlay`，根上挂 `behaviors:WorkflowSurfaceBehavior.*`；行为按名称解析部件。pan/zoom、输入、池、ruler/minimap 全在行为里。
- 模板选择器与 WPF/Avalonia/Jalium 同形：通用基类是 `VeloxDev.WinForms.Xaml.TemplateSelector`（只认 `object?` → `DataTemplate?`，无工作流类型）；tree 的 `Resources` 里声明 `<v:DataTemplate x:Key="NodeTemplate">…</v:DataTemplate>` 与 `<local:CustomTemplateSelector x:Key="WorkflowTemplateSelector" NodeTemplate="{StaticResource NodeTemplate}" />`，根上 `behaviors:WorkflowSurfaceBehavior.TemplateSelector="{StaticResource WorkflowTemplateSelector}"`。`TemplateSelector` 优先于 `NodeViewType` / `LinkViewType`；后两者是内置按类型名反射的兜底。
- 连线右键菜单没有声明式条目：宿主在 code-behind 调 `WorkflowSurfaceBehavior.SetLinkMenuBuilder(this, (menu, link) => ...)`；条目与策略都留在宿主。
- 装饰器归模板：`GridDecorator : Panel, IWorkflowGridDecorator`、`MinimapOverlay`、`SurfaceScrollableControl`/`SurfaceCanvas` 都由模板/Demo 定义；适配器只保留 `IWorkflowGridDecorator` 契约。
- 视图归模板：Node 模板由 `v:Border` + `Label` + 行宿主组装，行为由 `WorkflowNodeViewBehavior` / `WorkflowNodeTitleBehavior` / `WorkflowNodeRowsBehavior` 提供；Slot/Link 由 `v:UserControl` + attachment + 模板级绘制组装。没有 `NodeCard` / `SlotGlyph` / `LinkCurve` 基类。
- 模板 code-behind：Tree/Node 只剩构造 + `InitializeComponent()`；Grid/Slot/Link 保留各自绘制（WPF 对应模板同样有代码）。全量 Demo 的自绘变体保留更多宿主专用代码。
- 诊断 `VELOX_XAML001–017` / `VELOX_XAMLP001–004`；未知类型/属性/坏值/缺 `x:Class`/重复名/缺 handler/非 partial/已有 `InitializeComponent`/未知附加属性/坏 TargetType/坏 Trigger/事件重载或签名不符等。

## 三、边界

- 支持：XAML 编译、原生 WinForms 控件、派生控件、DP/附加属性、XAML 附加属性属性值、XAML 风格 Pointer/Key 事件与原生桥接、内联 Trigger / DataTrigger / MultiTrigger / Setter / TransitionAction、显式/隐式 Style / BasedOn / Style.Triggers / Template、原生 Control 样式、OneWay/TwoWay/OneTime、DataContext 继承、资源（含空元素对象资源）、DataTemplate、TransitionSystem 动画、样式集合变更自动刷新、简单索引器 DataTrigger、`Ease`。
- 不支持：WPF templated parent/parts/template triggers、设计器/热重载、`ElementName`/`RelativeSource`/`x:Static`、原生附加值的 TwoWay、原生样式的 Trigger/Template、无 `INotifyCollectionChanged` 的集合通知、自定义 action 的嵌套内容/非 `{StaticResource}` 标记、非 `Control` 祖先接收冒泡事件、原生控件横向滚轮。
- XAML 附加属性必须带前缀：`v:Owner.Property`（适配器 owner）或 `clr-namespace:...` 的 `local:Owner.Property`；无前缀点号属性报 `VELOX_XAML004`。
- 原生控件不是 XAML 的 `Control`：绑定走 `System.Windows.Forms.Control.DataBindings`，DP 附加属性可用但无 `DataContext` 继承语义；样式 Setter 可作用于普通 CLR 属性。
- 动画走 WinForms 适配器的互斥调度器：同一控件上的新 trigger 动画会 supersede 旧动画。
- 布局属性：`Thickness` 是 `Margin` / `Padding` / `Border.BorderThickness` 的类型；DP 变更同步到原生 `Padding`/`Margin`，`ThicknessSampler` 已注册，`XamlTransition.Animate` 可动画这三类属性（每帧一个 box，与既有 `PaddingSampler` 相同）。
- 适配器/模板/Demo 中不再有 `Xaml*` 类型别名。**XAML 视图的 code-behind 用完整命名空间声明基类**（`public sealed partial class TreeView : VeloxDev.WinForms.Xaml.Controls.UserControl`），因为 `System.Windows.Forms.UserControl` 与 XAML 的 `UserControl` 同名；模板与 Trimmed demo 的四个视图已按此写法。纯代码控件（不是 XAML 视图）仍可直接继承原生 WinForms 类型。
- 基础控件都有 DP：`Width`/`Height`/`Min*`/`Max*`、`Background`/`Foreground`（`BackColor`/`ForeColor` 的 WPF 别名）、`Font`、`Visibility`、`IsEnabled`、`HorizontalAlignment`/`VerticalAlignment`；`Margin`/`Padding` 仍是 `Thickness` DP。原生控件可以接收附加 DP，但不会进入 XAML 的变更/触发/过渡管线。
- **XAML 控件默认透明**：没人设 `BackColor`/`Background`（DP 或原生值）时 `OnPaintBackground` 不画系统灰，而是把父控件的背景/自绘内容按父坐标画进自己的 DC（`Control.PaintParentBackground`）——WinForms 子窗口 DC 不含父窗口像素，留空会在缩放/移动后露出上一次窗口表面的残留。半透明 `Background` 也先走这条伪透明路径再叠加颜色，不透明背景直接填充；`Button`/`CheckBox`/`TextBlock` 因此不会在暗色表面上盖出灰块。回归：`ATransparentControl_PaintsItsParentBackgroundUnderItself`。`Border` 仍按自己的 `Background`/`Backdrop` 绘制。样式/触发器退出时，`BackColor`/`ForeColor`/`Font`/`Width`/`Height`/`Margin`/`Padding` 会还原到接管前的原生值（`Control` 里的 `*Fallback` 字段）。
- **XAML 里的 `behaviors:Foo.IsEnabled` 要求 `Foo` 真的注册了 `RegisterAttached` 的 `IsEnabledProperty`**。WinForms 的静态行为常用 `ConditionalWeakTable` + `Get/Set`，没有 DP；把这种调用从 code-behind 搬进 XAML 会在 `XamlRuntime.RequireAttached` 抛「not registered」，而 Core 池只把异常记为 `ViewDiagnostics`，**HUD 仍显示可见计数、画布却一个节点/连线都没有**。`WorkflowSlotConnectionBehavior` 已补上 `IsEnabledProperty`；新增/搬迁行为时先核这条。
- 文本内容可直接写在元素体里（`<v:Button>OK</v:Button>`、`<v:TextBlock>hello</v:TextBlock>`）：生成器在无子元素时把文本写进 `Content`（优先）或 `Text`；混合内容仍不支持。
- 双缓冲：所有 `VeloxDev.WinForms.Xaml.Controls` 基础控件统一 `DoubleBuffered + OptimizedDoubleBuffer + AllPaintingInWmPaint + ResizeRedraw`；`WorkflowSurfaceBehavior` 不再无条件关闭 `WS_EX_COMPOSITED`，改由 `NativeWindowStyleHelper` 按子窗口数量阈值启用（小型 workflow 表面整窗合成，大型工作台保留 `WS_CLIPCHILDREN`）。缩放把一个滚轮格的三趟 `ApplyPan/SetScrollOffset/Refresh` 收成一次折叠 + 一次 `Refresh`；节点标题条高度、字号、左右 Padding 用同一个 collapse 因子缩放。
- 缩放帧率：滚轮消息只累加 `pendingZoomFactor`，由 `SurfaceState` 的 16ms WinForms Timer 每帧最多执行一次 `ApplyZoomFactor`；高分辨率滚轮/触控板一帧几十笔会合并成一笔，不再逐笔重排重绘。
- 缩放原子性：一次 `ApplyZoomFactor` 会先对 host/canvas/grid/所有池化视图 `WM_SETREDRAW(false)`（引用计数），再改 Scale/节点尺寸/字号/连线，最后逐级恢复并 `RedrawWindow` 一次；避免“新尺寸 + 旧位置”的中间帧和旧后缓冲拉伸。
- 平移原子性：`ApplyPan` 同样包 host/canvas/grid/所有池化视图；小地图拖动、滚轮平移、布局刷新都只提交最终几何一次。
- 定位：`SurfaceScroller.CenterOn` 必须用 `WorkflowSurfaceMath.CenterScroll`（`world - viewport/2 + CanvasLayout.ActualOffset`），不能用裸 `world - viewport/2`；节点的 `Anchor`/`Size` 是折叠后的画布局部坐标，平移/缩放后 ActualOffset 不为 0。还要再加 `VisualContentOffset - ActualOffset`（即 `IWorkflowGridDecorator.RulerBand`），否则定位会固定往左上偏一条标尺。
- 定位后新物化的节点/连线：`OnCanvasControlAdded` 必须立刻 `ApplySurfacePosition(PanOffset, VisualContentOffset)`，节点还要 `WorkflowSlotLayoutBehavior.SyncNow`；否则池在视口变化后新建的视图会停在默认 `(0,0)` 投影，表现为其它节点没有重新测绘。
- 拖动节点：`WorkflowNodeDragBehavior.OnMouseMove` 在 `MoveCommand` 后必须立刻 `WorkflowSlotLayoutBehavior.SyncNow(control)`；link 几何订阅的是插槽锚点，不是节点锚点，等布局批次会让连线停在旧端点（只拖节点、不拖画布时最明显）。
- 单层连线的端点**不能每次都用 `slot.Anchor − 当前投影`**：节点被移出可见集后测量会停住，而画布偏移继续变，模型端点会被平移量拖走，`LinkCurve.PortOutward` 在边上就会翻向 —— 曲线弯折方向来回摆。首选来源是 `WorkflowSlotLayoutBehavior.TryGetLocalAnchor(slot)`：从插槽控件沿父链累加 Left/Top 到节点根，得到**节点客户区里的局部中心**，只随布局/缩放变，完全不经过屏幕坐标；模型端点 = `node.Anchor + 局部中心`。取不到时（虚拟端点、自定义未走该行为的布局）才回退到按 slot 缓存的模型端点。`ApplyPanCore` 还必须**先写 ScrollViewer 的 Offset，再量节点/插槽**。回归：`LinkLayer_KeepsTheModelCurveWhenTheSlotMeasurementIsStale`；长连线探针对角平移时模型采样点保持不变。
- 单层连线的「拖动中实时跟随」：`WorkflowLinkLayer` 必须订阅**端点节点的 `PropertyChanged`**（不只 link/slot），否则节点 `Anchor` 变了但几何不脏，画布重绘还画旧曲线；`ApplyPanCore` 必须**先 `WorkflowLinkLayer.Refresh` 再 `canvas.Update`**，否则拖画布时同步重画的是上一帧几何。回归：`LinkLayer_FollowsANodeAnchorChange`；逐帧探针（画布平移/节点 MoveCommand → SyncNow → Update）端点误差为 0。
- 输入路由：**`SurfaceState` 的 `IMessageFilter` 统一处理 `WM_MOUSEMOVE`** —— 节点/插槽是子窗口，表面根的 `MouseMove` 收不到它们；`ResolveTarget` 必须 **Tag 优先**，因为继承来的 `DataContext` 可能是树而不是节点。节点/插槽的 `MouseDown`/`MouseUp` 由各自行为把**每个按钮**都 route 到 Core；离开表面（包括从节点直接移出）由 50ms 的 pointer watch 补 `PointerExited`，且 target 必须传 `input.PointerTarget`（传 null 只会通知树，节点/插槽的 `Input.PointerExited` 收不到）。回归：`ComponentPressAndRelease_ReachCoreWithTheNodeTarget`、`ResolveTarget_PrefersTheAttachmentTagOverAnInheritedDataContext`。
- 视图回收：**`ViewContainer.Deactivate` 会把 `Tag` 清空，`Activate` 必须无条件写回 item**。附件在「同一视图再次绑回同一 item」时走 `ReferenceEquals` 早退，不会补 `Tag`；Trimmed 的 `NodeView` 又没有 `ViewModel` 属性兜底，于是 `WorkflowNodeDragBehavior` / `WorkflowSlotLayoutBehavior` 的 `ResolveNode` 返回 null —— 表现是移出可见区再回来后节点拖不动、槽位锚点停在旧坐标、连线不显示。回归测试：`SurfaceContractTests.ViewContainer_RestoresTheContextTagWhenAViewIsReactivatedForTheSameItem`。
- 视图回收：池回收只 `Visible=false`，再进入可见区不会触发 `ControlAdded`。`WorkflowSurfaceBehavior` 对每个池化视图订阅 `VisibleChanged`，可见时重新 `ApplySurfacePosition`、`SyncNow` 槽布局、`WorkflowLinkAttachment.ApplySurfacePosition + Refresh` 并 `WorkflowNodeDragBehavior.RefreshHooks`；否则移出再移回来的节点投影/拖拽/连线会失效。
- 正向越界：`ApplyPanCore` 在 `PanOffset < 0` 且 `-Pan > ActualSize - viewport` 时调用 `ClampScrollOffset` 写 `PositiveOffset`；写入后必须重算新的 max 再落 Pan，否则定位/拖动只能到旧边界。负向由 `FoldPositivePan` 写 `NegativeOffset`。
- Link 交叉：每条 link 仍是独立子窗口，但 `WorkflowLinkAttachment` 给窗口加 `WS_EX_LAYERED` + `LWA_COLORKEY`，把 `SurfaceBackground` 设为透明色键；窗口区域仍只保留描边带。这样交叉处不会再被上一条线的背景带擦出缺口。色键在 HandleCreated/底色变化时重装。
- ScrollViewer：Tree 模板的 `PART_ScrollViewer` 用 `v:ScrollViewer`；`WorkflowSurfaceBehavior` 在 `ApplyPanCore` 同步 `Extent = CanvasLayout.ActualSize`、`Viewport = ResolveClientSize`、`Offset = -PanOffset`（滚动坐标 = viewportWorld + ActualOffset），滚动条 `Scroll` 直接反算 `SetScrollOffset(offset)`，有重入保护。`-Pan - ActualOffset` 是视图世界坐标，不能直接喂滚动条，否则负向扩展后 thumb 被钉回 0。旧的 `ScrollableControl` 仍可用。条是自己绘制的 `ScrollBar`，不是原生滚动条；模板用 `HorizontalBarStyle` / `VerticalBarStyle` 套 `v:Style`。
- ScrollViewer 的条厚由 `BarThickness` 决定（不再读 `PreferredSize`）；`LayoutChildren` 把可见条 `BringToFront`，否则 XAML 后加的内容会盖住它们。
- `WorkflowNodeRowsBehavior.RowsHostName` 必须同步到 `WorkflowSlotLayoutBehavior.SlotEnumeratorNames`；XAML 属性顺序可能先 `IsEnabled` 后 `RowsHostName`，只在 Attach 时设一次会把名字固化成 `PART_DynamicOutputs`，slot anchor 全 NaN、link 被隐藏。`SetRowsHostName` / `EnsureSlotLayout` 每次都要重设。
- 拖动画布闪烁：`ApplyPan` 的原子 `WM_SETREDRAW` scope 只用于非手势（小地图/布局/定位）；`state.IsPanning || host.Capture` 时走原 ApplyPanCore，靠 `canvas.Update` 跟手。`EndRedraw` 用 `RDW_NOERASE`，不要 `RDW_ERASE`。
- 首次物化：`EnsurePool` 换源前调用 `BeginInitialReveal`，冻结 host/canvas/grid/已存在视图并继续冻结新加入的池化视图；`ViewPool.GetPendingViewCount == 0` 连续两帧后（或 2s 超时）统一恢复重绘。池分批物化不再逐批露出半成品。
- 可写模型属性可以变 null：`INodeComponent.Slots`、`ITreeComponent.VirtualLink`、`ITreeComponent.Layout` 都是公开 setter。`WorkflowNodeRowsBehavior.Rebuild` 要 `node.Slots is not { }` 并跳过 null 元素；`WorkflowSlotConnectionBehavior` / `WorkflowNodeDragBehavior` 用 `VirtualLink is { IsVisible: true }`；`WorkflowSurfaceBehavior.ApplyZoomFactor` 用 `tree.Layout is not { } layout`。换文档/反序列化把树清空时，消息泵与缩放计时器里的 NRE 会持续复现。
- 模型线程回到 UI 线程：`ModelChangeRelay.Handle`、`WorkflowBindingExtensions` 的 `CollectionSubscription.OnCollectionChanged`、`WorkflowNodeAttachment.OnSlotsCollectionChanged`、`WorkflowNodeTitleBehavior.QueueTitleUpdate` 在 `BeginInvoke` 前查 `IsDisposed` / `Disposing` / `IsHandleCreated`，并吞掉 `InvalidOperationException`；销毁与模型写入可以同时发生。
- DataContext 型宿主的连线：`WorkflowLinkLayer.ResolveTree` 除 `WorkflowTree` DP 外还要读 `WorkflowSurfaceBehavior.GetSurfaceTree`（表面已解析的 `SurfaceState.Tree`）。只读 DP 时，模板按注释把树放进 DataContext 会只有节点、没有连线。
- 运行时的 null 契约：`DependencyProperty.RegisterCore` 拒绝「非可空值类型 + null 默认值」，可空值类型的盒装默认值放行；`XamlRuntime.GetResource` 的转换结果为 null 时抛 `InvalidOperationException`；`SetterCollection` 拒收 null；`KeyFrameAnimation` 与 `AnimateKeyFrames` 跳过 null 帧；`ContentControl.RebuildTemplate` / `ItemsControl.CreateItemVisual` 对模板工厂返回 null 分别保持空与退回文本行，`ContentControl.ApplyContent` 在内容控件之间切换时先摘掉旧的 `_contentChild`。
- `Path` 的 `Stretch` 必须用显式 `Matrix(m11, m12, m21, m22, dx, dy)` 构造：GDI+ 行向量下 `Scale`/`Translate` 叠加不会把 `-source` 一起缩放，整条几何会平移出控件；18×18 的 `v:Path`（SlotView）因此完全不可见。回归：`Path_UniformStretch_FitsTheGeometryIntoTheContentBounds`。
- `PathGeometry.Parse` 的每个 `M` 必须 `StartFigure`（再补一个零长度段把起点挪到 M 坐标）；当成 `L` 会在两个子路径之间画连接线，白色描边就成了横穿字形的割线。回归：`Path_MultipleMoveCommands_StartSeparateFigures`。

## 四、验证

```bash
dotnet test Src/Test/Unit/WinFormsXaml/VeloxDev.WinForms.Xaml.Test/VeloxDev.WinForms.Xaml.Test.csproj
dotnet build "Examples/Workflow/WinForms Trimmed/Demo/Demo.csproj"
dotnet build "Examples/Workflow/WinForms/Demo/Demo.csproj"
```

### 有头视图池基准（2000 节点 / 3000 连线 / 1200×800）

`dotnet run -c Debug -p:Optimize=true --project Src/Test/Benchmarks/VeloxDev.Adapters.Benchmarks -- headed --adapter winforms …`

| 阶段 | WinForms（单层连线后） | WinForms（旧：每线一视图） | WPF | Avalonia |
| --- | ---: | ---: | ---: | ---: |
| Initial（可见 179） | 60.0 ms / 1.18 MB | 102.9 ms / 1.19 MB | 7.7 ms / 1.60 MB | 11.9 ms / 2.29 MB |
| Scroll step（活跃 121 / 可见 284） | 23.0 ms / 2.07 MB | 49.9 ms / 0.23 MB | 3.1 ms / 0.56 MB | 11.9 ms / 0.26 MB |
| Full visibility（可见 5001） | 4.14 s / 438 MB | 13.9 s / 364 MB | 0.21 s / 44 MB | 0.32 s / 62 MB |

**连线不池化**：`ViewLinkLayer.IsActive=true` 把 `ILinkComponent` 挡在主池外，`WorkflowLinkLayer` 在画布 `Paint` 里一趟画完并发布曲线。于是 WinForms 的 `Active/Created` 只数节点（2000），`Visible` 仍是树的全量可见项（5001）。旧实现里 3000 条连线各是一个 HWND；这也是 13.9 s 的主要来源。实测 2000 个含子控件的视图 `Controls.Add` ≈ 2.3 s，随后 3000 个连线视图再 ≈ 8.2 s（Add 随现有控件数增长），3000 次 `SendToBack` ≈ 6.7 s，5000 个 `AddRange` 与逐个 Add 同价（≈ 8.6 s）。⇒ **full-visibility 在 WinForms 仍由节点 HWND 主导**；连线侧没有数量级差。
单层连线的分配比旧版高（scroll 2.07 MB vs 0.23 MB，full 438 MB vs 364 MB）：每次几何变脏会为所有可见线重发采样曲线（命中判定与绘制共用同一份）。正确性优先；若以后要压分配，按 link 缓存曲线、只重建锚点变了的那些。

基准工程：`WinFormsTreeBandsBenchmarks` 反射 `WorkflowRulerOverlayForm.DrawBands`；网格渲染归 item template，适配器没有可测的 `WorkflowGridDecorator` 类型。
