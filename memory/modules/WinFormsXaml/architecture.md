# VeloxDev.WinForms.Xaml — 架构

> 包：`VeloxDev.WinForms`（适配器 + 工作流表面）；视觉运行时与生成器是独立仓 `Easel` 的 `WinForms.Easel` / `WinForms.Easel.Generator`（子模块 `Easel/`）。
> 宿主行为：Easel 的 `IVisualHostBehavior` + `VisualElement.AddHostBehavior/RemoveHostBehavior` 在视觉根挂上/摘下宿主时回调 `Attach(VisualHost)` / `Detach()`；`WorkflowSurfaceBehavior` 的附着属性因此挂在视觉根上，由 Easel 注入宿主，不自己遍历树找 `VisualHost`。适配器内部仍用宿主键的引擎，控制器只把视觉根配置搬成 `SurfaceConfig` 快照。`VisualHost.DataContext` 向视觉根与后代继承（根元素本地值优先），`VisualHost.Post` 把一帧内的重绑合并成一次消息泵回调（WPF 调度器优先级语义）。
> 代码：运行时 `Easel/Src/Easel/`（`Primitives/`、`Visuals/`、`Visuals/Controls/`、`Theme.cs`）；生成器 `Easel/Src/Generators/Easel.Generator/`；工作流表面与适配器侧行为在 `Src/Adapters/VeloxDev.WinForms/WorkflowSystem/AttachedBehaviors/`。命名空间是 `Easel*`（控件为 `Easel.Visuals.Controls*`）。
> 定位：**一套视觉 XAML 后端**。控件是 `VisualElement`，`VisualHost` 是唯一 HWND；不是 WPF 兼容层。
> 面向消费者的运行时手册在 `skills/veloxdev-create-workflow/references/practices/gui/winforms-xaml.md`；动画在 `skills/veloxdev-create-animation/references/practices/winforms-xaml.md`。
> 独立 Easel 仓的抽取与动画注入契约见 [easel-extraction.md](easel-extraction.md)。

## 一、运行时

| 文件 | 类型 | 职责 |
| --- | --- | --- |
| `Primitives/Thickness.cs` / `LayoutPrimitives.cs` / `TextPrimitives.cs` | `Thickness` / `GridLength` / `RowDefinition` / `ColumnDefinition` / 布局枚举 | 布局值类型与枚举，全部在 `VeloxDev.WinForms.Xaml.Controls` |
| `Primitives/XamlDrawing.cs` | `internal XamlDrawing` | 自绘 chrome 辅助（环外轮廓 -0.5） |
| `Primitives/PathGeometry.cs` | `Shapes.SvgPathParser` | `<Path Data>` 的 SVG 子集解析 |
| `DependencyProperty.cs` + `DependencyProperty.Visual.cs` | `DependencyProperty` / `PropertyMetadata` | WPF 写法注册；视觉值经 `IWinformsObject` 的优先级表存取、类型校验 |
| `WinformsObject.cs` | `IWinformsObject` / `WinformsObject` | 视觉基类核心：优先级层（Animation→Local→StyleTrigger→Template→Style→Inherited）、`GetObservable`/`Bind`、`INotifyPropertyChanged` |
| `Visuals/Visual.cs` | `Visual` / `ContainerVisual` / `ShapeVisual` / `TextVisual` / `CurveBatchVisual` / `DropShadowEffect` | 视觉树节点：`Bounds`（父坐标）/ `Transform` / `Opacity` / `Clip` / `Effect`；`RenderContent` / `HitTestContent` |
| `Visuals/VisualElement.cs` | `VisualElement : ContainerVisual, IDisposable` | 布局/输入/渲染基类：`Margin`/`Padding`/`Width`/`Height`/对齐/`Background`/`CornerRadius` 等 DP，measure/arrange、指针/键盘事件、逻辑焦点 |
| `Visuals/VisualHost.cs` | `VisualHost : System.Windows.Forms.Control` | 唯一 HWND：`Root`、`Resources`、`DpiScaleOverride`、`RenderTo`、`ProcessPointer*` / `ProcessKey*` / 文本输入 |
| `Easel/Src/Easel/Visuals/VisualForm.cs` / `Window.cs` | `VisualForm : Form` / `Window : VisualForm` | 窗口壳：内部持有 dock-filled `VisualHost`；`VisualForm.Root` 换根，`DataContext` 转发给宿主。`Window` 增加 XAML 根形状：`Content`（单根）、`Title`（映射 `Text`）、`Resources`（宿主字典） |
| `Visuals/Controls/` | 视觉控件 | `UserControl`/`ContentControl`/`Border`/`StackPanel`/`Grid`/`DockPanel`/`Canvas`/`WrapPanel`/`UniformGrid`/`Viewbox`/`ScrollViewer`/`ScrollBar`/`ScrollContentPresenter`/`ItemsControl`/`ListBox`/`VirtualList`/`TreeView`/`ComboBox`/`TextBlock`/`TextBox`/`RichTextBox`/`Button` 系/`Slider`/`ProgressBar`/`Calendar`/`DatePicker`/`VisualMenu`/`VisualContextMenu`/`VisualPopup`/`VisualPresenter`/Shapes |
| `Visuals/VisualBindingOperations.cs` | `VisualBindingOperations` / `VisualBindingExpression` | OneWay/OneTime/TwoWay、`DataContext` 路径、Converter/StringFormat/FallbackValue/TargetNullValue；`RelativeSource.Self`/`TemplateBinding`；无 `ElementName`/校验规则 |
| `Visuals/Styling.cs` | `VisualStyle` / `VisualSetter` / `VisualTrigger` | 显式/隐式样式；setter 写 `Style` 层、trigger 写 `StyleTrigger` 层；`EnterAction`/`ExitAction` 在激活/失效时各跑一次；移除只清自己那两层 |
| `Visuals/VisualTriggerActions.cs` | `VisualTriggerAction` / `TriggerActionGroup` / `TransitionAction` / `KeyFrameAnimation` / `VisualTransition` | 视觉目标（`VisualElement`）的触发器动作；动画经 `Transition<VisualElement>` 写 `Animation` 层，同一目标的新动画覆盖旧动画，`Stop` 停住 |
| `Visuals/VisualControlTemplate.cs` | `VisualControlTemplate` | 模板应用：`TemplatedParent`、每实例 `VisualNameScope`、`GetTemplateChild<T>`；模板触发器写 `Template` 层，动作与 setter 用同一个 `TargetName` 部件 |
| `Visuals/Controls/ButtonBase.cs` | `ButtonBase` | `Command`/`CommandParameter` DP；激活时 `CanExecute` 通过才执行；拒绝时命令负责禁用/恢复 |
| `Visuals/VisualResources.cs` | `VisualResourceLookup` | 按「元素 → 祖先 → `VisualHost.Resources`」查找并做类型转换 |
| `Visuals/DataTemplate.cs` | `DataTemplate` | `Func<VisualElement>` 工厂，由生成器填充；keyed 资源存进 `Resources` |
| `Visuals/VisualTemplateSelector.cs` | `VisualTemplateSelector` | `object?` → `VisualElement?`；工作流的选择器基类 |
| `Visuals/VisualShapes.cs` | Shapes | `Rectangle`/`Ellipse`/`Line`/`Polygon`/`Polyline`/`Path` |
| `Visuals/VisualText.cs` | `VisualText.Draw` | 视觉子树的文字一律走 GDI+ `DrawString`；`TextRenderer` 不认 world transform |
| `Visuals/VisualInput.cs` | 输入重载 | `VisualHost` 把指针/滚轮/键/文本路由到最深命中元素 |
| `Theme.cs` | `Theme` / `ThemePalette` | 明/暗调色板；视觉控件渲染时直接读 `Theme.Current` |
| `WorkflowSystem/AttachedBehaviors/WorkflowSurfaceBehavior.cs` | 附着行为 | `VisualHost` 上解析 `PART_*` 视觉部件；相机矩阵（`PART_Canvas.Transform`）、Core 输入路由、网格/小地图/视口/虚拟化推送；相机 API `GetZoom`/`GetPan`/`SetPan`/`SurfaceToWorld`/`WorldToSurface`/`GetWorldViewport`/`SetWorldViewport`/`ZoomAt`/`ProcessWheel`/`Refresh` |
| `WorkflowSystem/AttachedBehaviors/ViewPool.cs` | 附着行为 | 通用视觉池（与工作流无关）：一个 item 一个视觉子元素，`ItemsSource`/`TemplateSelector`/`ItemTemplate`，设 `DataContext`，按 `Panel.ZIndex` + 源序排序 |
| `WorkflowSystem/AttachedBehaviors/WorkflowSlotLayoutBehavior.cs` / `WorkflowNodeDragBehavior.cs` / `WorkflowSlotConnectionBehavior.cs` | `VisualElement` 附着行为 | 槽测量写 `slot.Anchor`、标题行拖动、按下-释放在接收口完成连接；由 `WorkflowSurfaceBehavior` 在每个视觉元素上 `Ensure` |
| `WorkflowSystem/AttachedBehaviors/WorkflowMinimapOverlay.cs` / `WorkflowInfoOverlay.cs` | `VisualElement` | 视觉小地图与 HUD；表面推偏移、拖小地图反推滚动 |
| `WorkflowSystem/Platform/NativeWindowStyleHelper.cs` | Win32 辅助 | `WS_CLIPCHILDREN`/`WS_EX_COMPOSITED`、`BeginRedraw`/`EndRedraw` |

- **布局**：`VisualElement.Measure/Arrange` 管 Margin、显式尺寸、Min/Max、对齐与 Visibility；面板在 `MeasureOverride/ArrangeOverride` 只算内容。`Hidden` 占位不绘制，`Collapsed` 不测量不占位。`PropertyMetadata.AffectsMeasure/AffectsArrange/AffectsRender` 驱动失效。**`InvalidateArrange` 只失效摆放，保留测量缓存**；`Arrange` 在最终尺寸不变且子树未失效时跳过 `ArrangeOverride`，`Canvas.Left/Top` 只让父级重排，不把整张卡的内容再排一遍。可用尺寸真变了，`Measure` 自己会按 available 比较重测。
- **输入**：`VisualHost` 命中测试最深元素，事件沿 `ParentElement` 冒泡；焦点是逻辑焦点，`FocusElement` 把原生焦点留在 Host 上，Tab 在焦点链里走。指针捕获、Enter/Leave、滚轮、文本输入都集中在 Host。
- **`ContentControl`** 的 `ContentTemplate` 只作用于非 `VisualElement` 内容：模板创建视觉并设 `DataContext = Content`；`VisualElement` 内容直通。解析顺序：presenter 自己的 selector → 自己的 `ContentTemplate` → `TemplatedParent` 的 selector → `ContentTemplate` → 文本投影。`ContentTemplateSelector`（`DataTemplateSelector`）拿到原始 `Content`；`ContentStringFormat` 只在文本投影路径按当前文化 `string.Format`。`ContentControl.Template` 走 `VisualControlTemplate`，与内容模板是两条路。
- **`ScrollViewer` 是 `ContentControl`**：默认模板（`DefaultTemplate`）是 `Grid`：星号主格放 `ScrollContentPresenter(PART_ScrollContentPresenter)`，右列/下行各是 `Auto` 轨道放 `ScrollBar(PART_HorizontalScrollBar`/`PART_VerticalScrollBar)`，两条占用视口而不是覆盖内容；调用方 `Template` 或样式模板优先于默认 chrome。`ScrollContentPresenter` 按视口宽、纵向不设限量内容，裁切并偏移，并实现 `IScrollInfo`；`ScrollBar` 负责拇指/轨道/键盘、可选模板部件（`PART_Track`/`PART_Thumb`/`PART_*RepeatButton`）与按住自动翻页。`CanContentScroll=true` 且内容是 `IScrollInfo` 时，extent/offset/滚轮/分页都交给内容自己的单位。外部相机模式（`IsExternalScroll` + `SetExternalOffset`）只让 presenter 报范围、不偏移内容，条的手势转成 `ScrollOffsetRequested`。
- **`TextBox`** 自绘插入符/选区/剪贴板，默认全程走视觉路径（DP、模板、绑定、撤销全保留）；`UseNativeEditor=true` 时才用 `NativeControlHost` + 原生 `System.Windows.Forms.TextBox` 岛兜底 IME/无障碍，代价是失去视觉路径的选区/插入符外观。选区高亮、选中文字颜色、插入符颜色/粗细/闪烁周期由 `SelectionBrush`/`SelectionForeground`/`CaretBrush`/`CaretThickness`/`CaretBlinkInterval` 五个 DP 定制，只作用于自绘路径；原生岛显示时选区和插入符由系统绘制。聚焦的空字段不画占位文字。其余控件（`RichTextBox`、`ComboBox`、`Calendar` 等）都是纯视觉控件；另一条原生路径是 `native:`（`NativeElement`）与显式 `NativeControlHost`。
- **`DataContext`** 是 inherited 属性；没有本地层时从 `ParentElement` 解析，视觉根没有本地值时继承 `VisualHost.DataContext`；祖先切换向无本地值的后代补发 `Inherited` 变更，子树换父级时 `VisualElement.OnParentChanged` 也给自身与后代补这一跳。绑定只监听本元素的 DataContext 变化，少了这一跳，预构造的嵌套子树挂到有上下文的父级后会一直停在空源上（如 XAML 视图里的按钮 `Command` 绑定）。工作流表面把 tree 设在视觉根或宿主上即可 —— DataContext 是树的唯一来源。`VisualForm`/`Window` 把 Form 自己的 `DataContext` 同步到 `VisualHost.DataContext`；code-behind 里设上下文即可。
- **覆盖层**：`VisualHost` 在根树之后绘制、之前命中铺满客户区的覆盖层；`VisualPopup` 挂进去，不受祖先 `ScrollViewer.Clip` 裁切。弹层脱离根树，打开时把目标 `DataContext` 以 `Inherited` 层推给内容。
- **`RichTextBox`** 用扁平字符缓冲，每字符带 `RichTextStyle`；绘制必须走 `VisualText.Draw`。
- **渲染缓存**：`Visual.CacheAsBitmap` 缓存本地子树；`VisualElement.InvalidateVisual` 标脏最近的缓存并上报矩形，`VisualHost` 只清/画脏矩形；有变换或 `Effect` 时整面重画。
- **默认暗色对齐 Scribalix / VS Code workbench**：画布 `#141414`、面板 `#252526`、raised `#2D2D2D`、输入 `#2A2A2A`、边框 `#333333`、文本 `#E7E7E7`/`#A0A0A0`、accent/focus `#0078D4`、选中 `#094771`；light 为 Light+（`#FFFFFF`/`#F3F3F3`、选中 `#ADD6FF`）。4px 圆角、28px 控件高。
- **`Theme` 内置三套调色板**：`ThemeMode.Dark` / `Light` / `HighContrast`。高对比是纯黑底、白字、琥珀 accent（`#FFB000`）、聚焦 `#FFD700`、`CornerRadius=2`、`ControlHeight=30`；`ThemeMode` 切换整套 `ThemePalette`，控件在下次构造/根重建时读取。
- **DPI**：`VisualHost` 按 `DeviceDpi/96` 物理渲染；布局、相机、模型锚点与装饰偏移都是逻辑 DIP。
- **属性没有别名**（用户定）：一个概念只有一个 DP 名，`Background`/`Foreground` 而不是 `BackColor`/`ForeColor`。

## 二、源生成器

- `[XamlProperty]`：partial 属性 → 静态 `DependencyProperty` + CLR 包装；`[XamlAttachedProperty]`：静态 partial `GetX`/`SetX` → `RegisterAttached`。
- 没有 `DefaultValue` 时写 `default(T)`；非可空引用类型必须给 `DefaultValue` 或声明可空，否则报 `VELOX_XAMLP005`。
- `*.xaml`（`AdditionalFiles`）→ `private void InitializeComponent()`（普通方法，不要求/不生成 partial 声明）。
- **前缀解析**：不带前缀按视觉后端找 —— `Xaml.Visuals.Controls` → `.Visuals.Controls.Shapes` → `.Visuals` → 共享原语；`local:` 是调用方类型；历史 `v:` 是别名；`native:` 是内置前缀（不用写 `xmlns:native`，生成器解析前补声明）并包成 `NativeElement<T>`：布局/附加属性写包装元素，CLR 属性与事件写原生控件；CLR 属性绑定经 `NativePropertyBinding.For` 的代理 DP，`Mode=TwoWay` 按 `INotifyPropertyChanged` → `XxxChanged` → `Application.Idle`/200ms 兜底回读；原生 `Enter`/`Leave` 触发包装元素的逻辑 `GotFocus`/`LostFocus`，`LostFocus` 触发器与视觉目标一致。原生子级在生成期包进隐式纵向 `StackPanel`。找不到报 `VELOX_XAML019`（Error）。
- 视觉根必须派生自 `VisualElement`；单内容容器（`UserControl`/`ContentControl`/`ScrollViewer`）的子元素生成 `Content =`，其余 `Add`；有 `Items` 的父级写 `Items.Add`。
- `x:Name` 生成字段并写 `VisualElement.Name`；模板里注册进 `VisualNameScope`。显式 `Name="..."` 后写覆盖。
- 资源：标量、空元素对象（`<local:Selector x:Key="..." Template="{StaticResource ...}" />`）、`<Style>`、`<ControlTemplate>`、`<DataTemplate>`。keyed `DataTemplate` 编成工厂存进 `Resources`；`<DataTemplate>` 也可直接作 `ItemsControl.ItemTemplate`。`{DynamicResource}` 报诊断。
- 子元素上的 `{StaticResource}` 以视图根为锚，因为生成代码先赋属性、后挂父级。
- 内联 `Trigger`：`<Trigger Property="IsHot" Value="True"><Trigger.Setters>…</Trigger.Setters></Trigger>`；样式触发器经 `<Style.Triggers>`。触发器可带 `<Trigger.EnterAction>` / `<Trigger.ExitAction>`，内容为 `TransitionAction`、`KeyFrameAnimation`、`TriggerActionGroup` 或派生 `VisualTriggerAction` 的自定义动作。
- 事件：CLR 事件（`Click`/`PointerPressed`/`KeyInputDown` 等）由生成代码原位订阅。
- 诊断 `VELOX_XAML001–019` / `VELOX_XAMLP001–005`；`VELOX_XAML002`（未知成员）是 Error。

## 三、边界与不变量

- 支持：视觉控件树、DP/附加属性、`DataContext` 继承、OneWay/TwoWay/OneTime 绑定、Converter/StringFormat/Fallback/TargetNull、资源、显式/隐式 `VisualStyle`、样式/模板触发器与 enter/exit 动作、`ControlTemplate` + `ContentPresenter`、`ContentTemplate`、`ItemsControl.ItemTemplate`、keyed `DataTemplate`、`ScrollViewer` 默认/自定义模板与 `ScrollBar` 模板部件、`VisualPopup`/`VisualMenu`、`NativeElement`（`native:`，窗区域圆角/弹层挖洞、`IsSnapshot` 快照）、`NativeControlHost`、`VirtualizingPanel`（回收式可见窗）、`DataGridColumn.CellTemplate`/`CellFactory`、`SpatialGridHashMap`/`SpatialVirtualization`、DPI、`Theme`。
- 不支持：设计器/热重载、`ElementName`/校验规则、`DynamicResource`、可变行高虚拟化、树虚拟化、每条关键帧独立 `Ease`、`CommandBinding`/`InputBindings`、原生岛被变换/裁剪/混色、原生 CLR 属性绑定、原生控件直接套原生控件。
- **原生控件经 `NativeElement` 进视觉树**（`native:` 直接写）：它量 `GetPreferredSize`、按 `Bounds` 摆放、仍是矩形轴对齐 HWND 岛；`Content` 由挂在原生控件上的嵌套 `VisualHost` 画在 `DisplayRectangle` 内；多个原生子级由生成器包进隐式纵向 `StackPanel`。`CornerRadius` 用 `SetWindowRgn` 圆角化窗口；打开的弹层用 `RGN_DIFF` 从岛的区域里挖掉自己的矩形。没有 `INotifyPropertyChanged`/`XxxChanged` 的原生属性由控件显式调用 `NativePropertyBinding.NotifyChanged` 回报（不轮询）。`IsSnapshot=true` 时摘掉 HWND、把 `DrawToBitmap` 位图画进视觉管线，`Opacity`/`Transform`/`Clip` 这才生效；有 Form 时配一个 `Opacity=1/255` 的 owned 输入代理（0 会点击穿透）：透明度/平移精确，缩放按缩放后尺寸重排控件，旋转用多边形窗口区域保持整控件可点（内部命中与 IME 位置近似）。活体的 `CornerRadius` 只是二值窗区域裁切，原生边框在角上被切断；快照模式按圆角路径裁位图，`BorderBrush`/`BorderThickness` 只在快照模式画在内侧（盖住原生边框与角上的弧线）。
- **对话框与窗口 chrome**：`VisualDialog` 在 overlay 里铺 40% 遮罩（高对比 60%）+ 居中卡片（Surface/1px 边框/12 圆角/阴影），打开时隐藏原生子 HWND、关闭按原位恢复；焦点锁在顶层对话框、Tab 循环、Enter 主按钮、Esc 取消，多对话框堆叠。`VisualTitleBar` + `WindowChrome` 把 Form 设为无边框：空白标题区返回 `HTCAPTION`（Windows 负责拖动/吸附/拖回还原），按钮与可聚焦元素返回 `HTCLIENT`，未最大化时 8 DIP 边缘返回 `HT*`，最大化按工作区，DWM 圆角尽力而为。
- **虚拟化与空间索引**：`VirtualizingPanel`（`ItemExtent` 统一行高、可见窗、回收池、`IScrollInfo`）支撑 `VirtualList`（`ItemHeight` 是别名）；DataGrid 只有设 `CellTemplate`/`CellFactory` 的列把可见单元格实现成元素（行的 item 是 DataContext），其余单元格仍自绘。`Easel.Visuals.Spatial` 的 `SpatialGridHashMap<T>`/`SpatialVirtualization<T>` 用 `RectangleF` 做视口与深度查询；Easel 布局不发 bounds-changed 事件，`VisualSpatialBoundsProvider` 需显式 `Refresh()`。
- `VisualTemplateSelector` 是 item → 元素的唯一接缝；`VisualStyle` 是描述不是活对象，改已应用样式的集合不会自动重应用。
- `VisualElement.Resources` 与 `VisualHost.Resources` 是普通 `ResourceDictionary`；静态资源在构造期解析，键必须先声明。

## 四、坑（带依据）

- **`Path` 的 `Stretch` 用显式 `Matrix(m11, m12, m21, m22, dx, dy)` 构造**：GDI+ 行向量下 `Scale`/`Translate` 叠加不会把 `-source` 一起缩放，整条几何会平移出控件。回归：`Path_UniformStretch_FitsTheGeometryIntoTheContentBounds`。
- **`Grid` 先按轨道量、再按格子收紧**：`Grid.ResolveTracks` 第一遍 Auto 轨道沿该轴不设限（`int.MaxValue`，容器减掉 Padding 后按 `int.MaxValue / 2` 判定）、像素与星号轨道按整张网格量；轨道尺寸定下后按子项实际格子再量一次。少第二遍时 `ScrollViewer` 会把内容算成整格宽，安排进窄轨道后右对齐内容被推出裁剪区，槽位锚点也落在裁剪区外。回归：`Grid_MeasuresAChildInsideItsFixedTrack`、`Grid_AutoRowMeasuresANestedStarGridAtItsContentHeight`。
- **`ScrollContentPresenter` 宽按视口、纵向不设限量内容**：宽若也不设限，`VirtualList` 会把 `int.MaxValue` 当 desired width，行内 `TextBlock` 画到 `int.MaxValue` 宽的矩形上抛 GDI+ `OverflowException`；纵向不设限才能让列表报告完整 extent。回归：`VirtualList_RealizesOnlyVisibleRowsAndFollowsTheScrollOffset`、`ScrollViewer_MeasuresStarContentAtItsNaturalHeight`。
- **DataContext 绑定的源是父级上下文**：`VisualBindingExpression` 对 `DataContext` 目标的绑定取 `ParentElement.DataContext`，并同时订阅父级的 DataContext 变化与 `VisualElement.ParentChanged`；父上下文/父级变化后重新解析源，复用视图的本地 DataContext 绑定才不会停在旧值。回归场景：ViewPool 复用 EnumSelectorNodeView 后输入口重新绑到新 SlotViewModel。
- **重绘脏区按本地矩形变换**：`InvalidateVisual` 取 `GetInvalidationBounds()`（元素可覆写成实际绘制区）经 `PointToSurface` 变成表面包围盒，不再因为祖先有 `Transform` 就整面重画；位图缓存和 `Effect` 仍走整面/外扩。`VisualHost.LastPaintTimestamp`/`PaintCount`/`LastPaintWasClockDriven` 记录绘制来源，动画宿主据此让定时帧给拖动重绘让位。`MotionScale` 默认 1（关闭），手势期间可降分辨率；`Visual.RenderDamage` 只对局部脏区裁剪不相交子树，位图缓存/Effect 不裁。
- **局部重绘的裁剪区要外扩 1 个逻辑单位**：`OnPaint` 先用 `BackColor` 铺脏区，`RenderPhysical` 再把物理脏区转成逻辑裁剪；DPI/GDI+ 取整会让脏区最后一列/行没被视觉树覆盖，拖动时留下一条宿主底色的 1px 竖线。`RenderPhysical` 现在把逻辑裁剪 `Inflate(1,1)`。`Slider` 会被水平栈拉伸到整列高，整列失效会把这条缝拉满高度，所以它的 `GetInvalidationBounds` 只报中间轨道带。
- **子元素实际尺寸变化自动反映到 extent/条**：内容 `Width/Height` 或 desired 改变后，presenter 重测并更新 extent/viewport，条的最大值随之变化；工作流把世界范围写进 `PART_Canvas.Width/Height`，条仍由控件层测量驱动，相机只决定子元素该多大。回归：`ScrollViewer_FollowsChildSizeChanges`。
- **`ScrollViewer` 的 desired 要回到 presenter 的内容尺寸**：默认模板的 `Grid` 有星号轨道，会按可用尺寸铺满；`ScrollViewer.MeasureOverride` 必须返回 presenter 的 desired（+ Padding），否则放进 ComboBox 下拉弹层/StackPanel 的 Auto 尺寸时会把整条可用宽度占掉，弹层盖住旁边控件。回归：`ComboBox_OutsidePressClosesAndFocusesTheClickedElement`。
- **滚动条占 Auto 轨道，不覆盖内容**：`ScrollBar` 只在自身轨道条带（含命中内缩）应答；默认 chrome 的两条条各占一行/一列，presenter 视口随之缩小。显示/隐藏用 `Visibility`；切换后必须让 `TemplateRoot`/`ScrollViewer` 重新 `Measure`——父级的测量缓存不会因子元素折叠/展开自动失效。轨道按下先翻一页、同一笔继续拖动；按住约 400ms 后每 50ms 重复一页；悬停拇指用主题前景色高亮。回归：`ScrollViewer_TrackPressContinuesAsTheSameDrag`、`ScrollViewer_ScrollBarCornerIsNotShared`、`ScrollViewer_ThumbBrightensUnderThePointer`、`ScrollBar_TemplatePartsPositionTheThumb`、`ScrollBar_TrackPressPagesOnceThenRepeats`。
- **布局期间的新失效不能被本轮结尾吞掉**：`VisualHost.EnsureLayout` 在进入时置 `_layoutValid = true`，结束不再写 true；measure/arrange 里触发的 `InvalidateMeasure` 会把它留成 false，下一帧再排。滚动条第一次算出范围就是在 presenter 的 Arrange 里改 `Visibility`，少了这条会永远错过第二次测量。回归：`ScrollViewer_AutoBarsSettleAfterTheFirstLayout`。
- **WPF 语义补齐**：`Binding` 支持 `FindAncestor`/`AncestorLevel`、`PropertyChanged`/`LostFocus`/`Explicit`、`OneWayToSource`/`Default`、`ValidatesOnExceptions`/`DataErrors`/`NotifyDataErrors` 与 `ConverterCulture`；资源改为按目标元素链解析并支持 `DynamicResource`；`DataTemplate`/`ItemTemplate`/`ItemsPanelTemplate` 有独立名字作用域；支持附加属性 property element、直接 `Style` setter/trigger、`ItemsPanel`/`ItemsPresenter`/`IsItemsHost`，不支持的触发器/模板显式报 `VELOX_XAML004`。
- **渲染与输入属性是 DP**：`Opacity`/`Transform`/`Clip`/`ClipToBounds`/`Effect`/`CacheAsBitmap` 在 `VisualElement` 上有 DP 覆盖；`FontSize`/`FontFamily`/`FontWeight` 继承，`Foreground` 以 Brush 存储、保留 Color 访问器；`IsEnabled` 继承，`TabIndex`/`IsTabStop` 参与焦点顺序。
- **控件模板**：`TemplatedControl`/`ButtonBase`/`ItemsControl`/TextBox/RichTextBox/ComboBox/Menu/Calendar/DatePicker/TreeView/VirtualList 都应用 `Template` 与模板触发器；TextBox 的 `PART_ContentHost` 原生编辑器只能覆盖定位（HWND 不随视觉变换/裁剪），RichTextBox/ComboBox 等缺必需部件显式抛 `InvalidOperationException`。剩余边界：VirtualList 无逐行容器、TreeView 无虚拟化、templated Menu 的下拉仍自绘。
- **3D 投影滤波按局部足迹**：`Visual.DrawProjected` 走 `VisualProjectionFilter.Sample`——足迹 ≥1.1 各向异性面积（每轴 1..4）、≤0.9 Catmull-Rom、其余双线性，源矩形边缘仍 2×2 覆盖率；1x 源对 4x 内容仍差约 20 dB，源分辨率是上限。回归：`ProjectionFilterTests`。
- **动画类型**：`Matrix`/`RectangleF?` 已注册采样器；`bool` 不是可动画类型，`ClipToBounds`/`CacheAsBitmap` 等 bool 过渡显式抛 `NotSupportedException`，无采样器的属性不会被接受后冻结。旧 native XAML profile 已删除。
- **`CanContentScroll` 的单位由内容决定**：内容是 `IScrollInfo` 时，`ScrollViewer` 用它报告的 extent/viewport/offset 驱动两条条与滚轮/分页，presenter 只负责测量/裁切并把自身偏移归零；`VirtualList` 仍是像素滚动，尚未实现按项的逻辑单位。回归：`ScrollViewer_CanContentScrollUsesTheContentsScrollInfo`。
- **原生 TextBox 不支持透明背景**：`TextBox.ApplyNativeProperties` 通过 `ResolveNativeBackground` 取自身或最近不透明祖先的颜色（兜底 `Theme.Current.ControlBackground`）。把视觉层的 `Transparent` 直接写进 `_editor.BackColor` 会抛 `ArgumentException: 控件不支持透明的背景色`。回归：`TextBox_TransparentBackgroundFallsBackToTheNearestOpaqueAncestor`。
- **`PathGeometry.Parse` 的每个 `M` 必须 `StartFigure`**（再补零长度段把起点挪到 M 坐标）；当成 `L` 会在子路径间画连接线，白色描边就成了横穿字形的割线。回归：`Path_MultipleMoveCommands_StartSeparateFigures`。
- **视觉子树的文字走 `VisualText.Draw`**：`TextRenderer` 直接写 HDC、不认 world transform，嵌套平移后文字会画到表面原点。
- **`DockPanel` 顶部子项要显式高度**：没有 `RowDefinitions` 的 `Grid` 默认一行星号，会吃掉全部可用高度。
- **`NativeElement`/`NativeControlHost` 是 airspace 岛**：子 HWND 永远浮在视觉树之上；活体 HWND 拒绝 `WS_EX_LAYERED` 与仿射变换，`Opacity`/`Transform` 只能走 `IsSnapshot`；弹层靠窗区域挖洞覆盖它。`Content` 的嵌套表面同样受岛边界约束。
- **原生岛不会每趟布局都重走 `ArrangeOverride`**：`NativeElement.ArrangeOverride` 是 `VisualHost.PlaceNative` 的登记点，但 `VisualElement.Arrange` 在尺寸不变且已安排过时跳过 `ArrangeOverride`；`EnsureLayout` 末尾的扫描曾因此把岛当成“这趟没安排到”摘掉（岛仍 `IsUsingNativeEditor`，却已脱离宿主、拿不到焦点）。扫描现在对仍满足 `host.Surface == this && host.IsVisible` 的宿主按 `AbsoluteBounds` 重新 `PlaceNative`，其余才 `RemoveNative`。回归：`TextBox_NativeEditorSurvivesTheNextLayoutPass`。
- **原生岛拿走原生焦点不算逻辑失焦**：`VisualHost.OnLostFocus` 只在 `!ContainsFocus` 时清 `_focused`，`FocusElement` 末尾也只在 `!ContainsFocus` 时重新聚焦宿主。否则宿主焦点会把岛顶掉，`TextBox.OnLostFocus` 再藏岛，焦点在宿主与岛之间反复来回。回归：点击文本框后原生编辑器应保持 `NativeEditor.Focused`，且下一次布局不摘岛。
- **原生岛按祖先 Clip 裁剪，但不缩小窗口**：`VisualHost.PlaceNative` 把子 HWND 摆到完整排列矩形，再把宿主客户区与祖先 `ClipToBounds`/`Clip` 的交集交给 `NativeWindowRegion` 做窗口区域；交集小于 1px 就隐藏。按交集缩小窗口会让原生内容跟着重排——视频被缩放、编辑器重折行，拖动分割条时画面尺寸会跳变。回归：`TextBox_NativeEditorIsClippedToTheScrollViewport`、`PartiallyClippedNativeIslandKeepsItsArrangeSize`、`NativeRegionClipsToTheVisibleIntersection`。
- **原生岛在贴屏前统一移动**：绘制期间的 `PlaceNative` 只排队，`OnPaint` 末尾 `FlushNativePlacements` 才改子 HWND 的位置与窗口区域；手势期间再 `DwmFlush` 等这一帧合成。父窗口不设 `WS_CLIPCHILDREN`，岛腾出的旧位置在同一帧补画。回归：`NativeIslandMovesWhenTheFrameIsPresented`。
- **手势期间原生岛默认冻结**：`VisualHost.FreezeNativeIslandsDuringGestures`（默认 true）在指针按下时把每个活体岛截成位图并隐藏 HWND，由视觉管线跟着卡片画；松手后按当前排列矩形摆回。子 HWND 的位置和父窗口帧无法保证同一合成帧，快速拖动分割条时视频画面会拖影，冻结是消除它的唯一可靠手段；快照模式的元素本来就画位图，不参与冻结。回归：`AGestureFreezesNativeIslandsAndReleaseRestoresThem`。
- **`Calendar` 不在“今天”上留标记**：只画选中日的 accent 填充。焦点环矩形或小圆点都会在换选日期后留在旧日期上，看起来像没清掉的选中框。回归：`Calendar_SelectingAnotherDayLeavesNoMarkerOnToday`。
- **插入符按字形推进定位**：TextBox 的文字绘制与前缀测量使用 `VisualText.DrawTight`/`MeasureWidthTight`（GenericTypographic）；默认 `MeasureString` 首尾各留约 1/6 em padding，插入符会停在最后一个字之后的一段空白上。回归：`TextBox_CaretSitsAtTheEndOfTheLastCharacter`。
- **插入符默认取主题的 `Caret`**：`ThemePalette.Caret`（缺省回退 `Focus`）；TextBox/RichTextBox 的 `CaretBrush` 未设置时用它，Dark+/Light+ 是聚焦色，Gallery 自定义高对比主题是黄色 `#FFD54A`。回归：`TextBox_UsesTheThemeCaretByDefault`。
- **Ins 切换覆盖模式**：`IsOverwriteMode`（Insert 键切换）在视觉路径画方块插入符并覆盖后续字符，换行仍插入；方块按 `CaretBlockOpacity`（默认 0.35）半透明填充，并描 1px 实色边，字符仍可见。原生岛沿用系统的插入行为。回归：`TextBox_InsertKeyTogglesOverwriteMode`、`TextBox_OverwriteReplacesTheNextCharacter`、`TextBox_OverwriteModeDrawsABlockCaret`。
- **Backspace 不能在 `Text` 变更后再自减**：`Text` 的变更回调会 `ClampSelection` 把行尾光标夹到新长度；先算 `removeAt` 再删、删除后用 `removeAt` 赋回光标。旧顺序在行尾删除会多退一格。回归：`TextBox_DeleteAndBackspaceKeepTheCaretAcrossModes`。
- **单行文本在内容矩形里垂直居中**：`RenderEditorSurface` 用 `textTop` 偏移把单行文本、选区与插入符一起居中；多行（`AcceptsReturn`/`Wrap`）保持顶部对齐。回归：`TextBox_CentersSingleLineTextVertically`。
- **第一次点原生文本框要同步光标并补焦点**：`OnPointerPressed`/`OnPointerMoved` 在 `_nativeActive` 时调用 `SyncEditorFromVisual()`；`ShowNativeEditor` 在布局之后投递一次 `FocusNativeEditorIfNeeded`，并用 40 ms × 25 次重试与宿主 `GotFocus` 兜底（激活与焦点归还可能跨消息），`ProcessPointerDown` 先让宿主取得 WinForms 焦点。缺它时第一次点击只打开编辑器、插入符要再点一次才出现。回归：`FirstClickPositionsTheNativeEditorCaret`、`FirstClickFocusesTheNativeEditor`。
- **点不可聚焦区域要让焦点退出**：`ProcessPointerDown` 找不到可聚焦祖先时，若目标不在当前焦点子树且不在 `VisualPopup` 里就 `FocusElement(null)`；缺它时点卡片/面板不会退出输入框。回归：`ClickingANonFocusableAreaOutsideTheFieldClearsFocus`。
- **`TextBox` 的选择索引可能落在文本之外**：原生编辑器同步、绑定回写与布局都会改文本；`SelectionStart`/`SelectionLength`/`CaretIndex` 现在夹到 `0..Text.Length`，文本修改入口先 `ClampSelection()`，替换后再按新文本长度夹一次。回归：`TypingSurvivesAnOutOfRangeSelection`。
- **MTA 线程上的宿主不注册 OLE 拖放**：`VisualHost` 只在 STA 设置 `AllowDrop`；MTA 宿主仍能创建句柄与渲染，只是没有原生拖放。回归：`AnMtaThreadCanCreateTheSurfaceHandle`。
- **`DataGrid` 表头先画、行后画**：滚动偏移不是行高整数倍时首行会越过 `_bodyTop`，行文字与选中色盖到表头上；`DrawRows`/`DrawDropIndicator` 必须裁到 `_bodyTop.._bodyBottom`。回归：`ScrollingAPartialRowKeepsItUnderTheHeader`。
- **Z 序缓冲不能跨渲染重入复用**：`ContainerVisual` 的 `_zOrder` 是每帧复用的列表；子项绘制期间对同一容器做命中（或其它重入）会清空它，外层 `foreach` 随即抛 `Collection was modified`。`BeginZOrder`/`EndZOrder` 在重入层改用独立列表，正常情况下仍然零分配。回归：`AReentrantHitTestDuringAChildRenderDoesNotBreakTheWalk`。

## 五、验证

```bash
dotnet test Src/Test/Unit/WinFormsXaml/VeloxDev.WinForms.Xaml.Test/VeloxDev.WinForms.Xaml.Test.csproj
dotnet test Src/Test/Headless/VeloxDev.WinForms.Headless.Tests/VeloxDev.WinForms.Headless.Tests.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File Src/Test/Harness/verify-workflow-item-templates-all.ps1 -Strict
dotnet build "Examples/Workflow/WinForms Trimmed/Demo/Demo.csproj"
dotnet build "Examples/Workflow/WinForms/Demo/Demo.csproj"
```

视觉测试多数只经 `VisualHost.RenderTo` 与 `ProcessPointer*`，不必 `Show()`；`WorkflowCameraTests` 覆盖相机/命中/缩放，`WorkflowSurfaceBehaviorTests` 覆盖槽测量与标题拖动，`VisualViewPoolTests` 覆盖池的物化/回收/排序。模板 harness 证明七家生成、构建、drift 0，但不证明绘制；画面验证跑 Trimmed / Full demo 或 `XamlGallery`。
